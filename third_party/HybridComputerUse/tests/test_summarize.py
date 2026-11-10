"""Scoring policy of summarize, read from result.json alone."""

import json
import subprocess
import sys

import pytest

from hcu.gateway.gemini_proxy import extract_usage
from hcu.osworld.summarize import aggregate, read_task, render_csv, render_markdown, summarize
from hcu.osworld.usage import collect_task_proxy_usage

COMPLETE_USAGE = {"proxy_usage": {
    "status": "completed", "unaccounted_requests": 0, "input_tokens": 10, "output_tokens": 4,
}}


@pytest.fixture
def run(tmp_path):
    root = tmp_path / "run"
    tasks = ["valid", "budget", "timeout", "evaluator", "error", "missing"]
    (root / "tasks").mkdir(parents=True)
    (root / "config.json").write_text(json.dumps({
        "run_id": "run", "agent": "codex", "tasks": [{"id": task} for task in tasks],
    }))

    def result(task_id, **fields):
        path = root / "tasks" / task_id
        path.mkdir()
        (path / "result.json").write_text(json.dumps({"task_id": task_id, **fields}))

    return root, result


def test_policy_for_every_outcome(run):
    root, result = run
    result("valid", status="completed", score=0.75, termination_reason="completed",
           duration_sec=12.5, usage=COMPLETE_USAGE)
    result("budget", status="completed", score=1.0, termination_reason="max_model_calls")
    result("timeout", status="completed", score=1.0, termination_reason="timeout")
    result("evaluator", status="evaluator_error", score=None,
           evaluator_errors=[{"error": "ValueError: score must be finite"}])
    result("error", status="error", failure_classification="unclassified_turn_failure",
           error="RuntimeError: agent exited")
    report = summarize(root)
    rows = {row["task_id"]: row for row in report["tasks"]}
    assert [rows[key]["status"] for key in rows] == [
        "valid", "budget", "timeout", "evaluator_error", "error", "missing",
    ]
    # A truncated run scores zero even when the evaluator passed it.
    assert (rows["budget"]["raw_score"], rows["budget"]["score"]) == (1.0, 0.0)
    assert rows["evaluator"]["score"] is None and rows["error"]["score"] is None
    assert rows["valid"]["input_tokens"] == 10 and rows["budget"]["input_tokens"] is None
    totals = report["aggregate"]
    assert totals["final_score"] is None and not totals["complete"]
    assert totals["lower_bound"] == round(0.75 / 6, 4)
    assert totals["observed_mean"] == 0.25
    assert "valid" in render_markdown(report) and render_csv(report).count("\n") == 7


def test_complete_run_uses_the_configured_denominator(tmp_path):
    rows = [
        read_task(tmp_path / "none", {"id": "x"}) | {"status": "valid", "score": score}
        for score in (1.0, 0.0, 0.5)
    ]
    assert aggregate(rows)["final_score"] == 0.5


@pytest.mark.parametrize("content", ["{not json", "[1, 2]", '{"status": "completed", "score": NaN}'])
def test_unreadable_or_invalid_result_is_visible_not_scored(tmp_path, content):
    (tmp_path / "result.json").write_text(content)
    row = read_task(tmp_path, {"id": "x"})
    assert row["status"] in {"error", "evaluator_error"}
    assert row["score"] is None


def test_cli_writes_summary_files(run):
    root, result = run
    for task in ("valid", "budget", "timeout", "evaluator", "error", "missing"):
        result(task, status="completed", score=1.0, termination_reason="completed")
    output = subprocess.run(
        [sys.executable, "-m", "hcu.osworld.summarize", str(root)],
        capture_output=True, text=True, check=True,
    )
    assert json.loads(output.stdout)["final_score"] == 1.0
    assert json.loads((root / "summary/summary.json").read_text())["aggregate"]["complete"]


def collect(tmp_path, rows):
    log = tmp_path / "gateway.jsonl"
    log.write_text("\n".join(json.dumps(row) for row in rows) + "\n")
    return collect_task_proxy_usage(log, "task-a")[0]


def test_usage_excludes_unadmitted_and_non_generation_requests(tmp_path):
    usage = extract_usage({"usageMetadata": {
        "promptTokenCount": 20, "toolUsePromptTokenCount": 3,
        "cachedContentTokenCount": 5, "candidatesTokenCount": 7,
        "thoughtsTokenCount": 11, "totalTokenCount": 41,
    }})
    task = {"task": "task-a"}
    proxy = collect(tmp_path, [
        {**task, "generation_request": True, "generation_admitted": True,
         "model_step_completed": True, **usage},
        {**task, "budget_rejected": True},
        {**task, "budget_inflight": True},
        {**task, "generation_retry_exhausted": True, "generation_admitted": False},
        {**task, "generation_request": False, "path": "/v1/messages/count_tokens"},
        {**task, "rate_limit_wait": True, "wait_sec": 3.5},
        {"task": "other", "input_tokens": 999, "output_tokens": 999},
    ])
    assert proxy["request_attempts"] == 5
    assert proxy["gateway_rejected_requests"] == 3
    assert proxy["model_requests"] == proxy["accounted_responses"] == 1
    assert proxy["unaccounted_requests"] == 0
    assert proxy["total_tokens"] == 41
    assert (proxy["rate_limit_waits"], proxy["rate_limit_wait_sec"]) == (1, 3.5)


def test_admitted_generation_without_usage_leaves_tokens_unknown(tmp_path, run):
    proxy = collect(tmp_path, [
        {"task": "task-a", "generation_request": True, "generation_admitted": True,
         "model_step_completed": True, "input_tokens": 3, "output_tokens": 2},
        {"task": "task-a", "generation_request": True, "generation_admitted": True,
         "http_status": 503},
    ])
    assert proxy["unaccounted_requests"] == 1
    root, result = run
    result("valid", status="completed", score=1.0, usage={"proxy_usage": proxy})
    row = read_task(root / "tasks/valid", {"id": "valid"})
    assert row["input_tokens"] is None

