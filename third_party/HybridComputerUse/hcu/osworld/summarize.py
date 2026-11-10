"""Summarize one run directory from its tasks' result.json files.

    python -m hcu.osworld.summarize RUN_DIR

Writes ``summary/summary.json``, ``summary/tasks.csv`` and
``summary/summary.md`` under the run directory. No Docker, checkout, gateway
or model is needed.

Scoring policy. The denominator is every task in the run's config.json.
A task counts its evaluator score only when it finished normally. A task that
hit the model-call budget or the timeout scores zero, even if the evaluator
passed it. A task with no result, an evaluator error or a runner error has no
score; the final score is then withheld and ``lower_bound`` counts it as zero.
Token counts are reported only when the gateway accounted for every
generation; otherwise they are unknown, never zero.
"""

from __future__ import annotations

import argparse
import csv
import io
import json
import math
from pathlib import Path

STATUSES = ("valid", "budget", "timeout", "evaluator_error", "error", "missing")


def read_task(task_dir: Path, task: dict) -> dict:
    """One task's row, read from its result.json alone."""
    try:
        result = json.loads((task_dir / "result.json").read_text(encoding="utf-8"))
    except FileNotFoundError:
        result = None
    except (OSError, ValueError) as exc:
        result = {"status": "error", "error": f"unreadable result.json: {exc}"}
    if not isinstance(result, dict):
        result = None if result is None else {"status": "error", "error": "result.json is not an object"}
    row = {
        "task_id": task["id"],
        "stratum": task.get("stratum"),
        "status": "missing",
        "raw_score": None,
        "score": None,
        "error": None,
        "failure_classification": None,
        "duration_sec": None,
        "input_tokens": None,
        "output_tokens": None,
        "rate_limit_wait_sec": None,
    }
    if result is None:
        return row
    raw = result.get("score")
    if isinstance(raw, bool) or not isinstance(raw, (int, float)) or not math.isfinite(raw):
        raw = None
    reason = result.get("termination_reason")
    if result.get("status") == "error":
        status = "error"
    elif result.get("status") == "evaluator_error" or raw is None:
        status = "evaluator_error"
    elif reason == "max_model_calls":
        status = "budget"
    elif reason == "timeout":
        status = "timeout"
    else:
        status = "valid"
    proxy = (result.get("usage") or {}).get("proxy_usage") or {}
    # Token totals are known only when every generation row carried usage.
    complete_usage = proxy.get("status") == "completed" and proxy.get("unaccounted_requests") == 0
    row.update({
        "status": status,
        "raw_score": raw,
        "score": raw if status == "valid" else 0.0 if status in {"budget", "timeout"} else None,
        "error": result.get("error") or result.get("evaluator_errors") or None,
        "failure_classification": result.get("failure_classification"),
        "duration_sec": result.get("duration_sec"),
        "input_tokens": proxy.get("input_tokens") if complete_usage else None,
        "output_tokens": proxy.get("output_tokens") if complete_usage else None,
        "rate_limit_wait_sec": proxy.get("rate_limit_wait_sec"),
    })
    return row


def aggregate(rows: list[dict]) -> dict:
    if not rows:
        raise ValueError("Cannot summarize an empty task selection")
    scores = [row["score"] for row in rows if row["score"] is not None]
    complete = len(scores) == len(rows)
    return {
        "tasks": len(rows),
        "status_counts": {status: sum(row["status"] == status for row in rows) for status in STATUSES},
        "scored": len(scores),
        "observed_mean": round(sum(scores) / len(scores), 4) if scores else None,
        "lower_bound": round(sum(scores) / len(rows), 4),
        "final_score": round(sum(scores) / len(rows), 4) if complete else None,
        "complete": complete,
    }


def summarize(run_dir: Path) -> dict:
    run_dir = run_dir.expanduser().resolve()
    config = json.loads((run_dir / "config.json").read_text(encoding="utf-8"))
    rows = [read_task(run_dir / "tasks" / task["id"], task) for task in config["tasks"]]
    return {
        "run_id": config["run_id"],
        "benchmark_version": config.get("benchmark_version"),
        "agent": config.get("agent"),
        "model": config.get("model"),
        "run_dir": str(run_dir),
        "aggregate": aggregate(rows),
        "tasks": rows,
        "retried_attempts": sorted(path.name for path in (run_dir / "retries").glob("*") if path.is_dir()),
    }


def render_csv(report: dict) -> str:
    output = io.StringIO(newline="")
    writer = csv.DictWriter(output, fieldnames=list(report["tasks"][0]))
    writer.writeheader()
    for row in report["tasks"]:
        writer.writerow({
            key: json.dumps(value, ensure_ascii=False) if isinstance(value, (list, dict)) else value
            for key, value in row.items()
        })
    return output.getvalue()


def render_markdown(report: dict) -> str:
    def cell(value):
        return "" if value is None else " ".join(str(value).split()).replace("|", r"\|")[:200]

    totals = report["aggregate"]
    lines = [
        f"# {report['run_id']}", "",
        f"Tasks: {totals['tasks']}; scored: {totals['scored']}",
        f"Final score: {cell(totals['final_score']) or 'withheld (unscored tasks)'}",
        f"Lower bound (unscored = 0): {totals['lower_bound']}",
        f"Mean of scored tasks: {cell(totals['observed_mean'])}",
        f"Status counts: {json.dumps(totals['status_counts'])}", "",
        "| Task | Status | RAW | Score | Seconds | Error |",
        "| --- | --- | --- | --- | --- | --- |",
    ]
    for row in report["tasks"]:
        lines.append("| " + " | ".join(cell(value) for value in (
            row["task_id"], row["status"], row["raw_score"], row["score"],
            row["duration_sec"], row["error"],
        )) + " |")
    if report["retried_attempts"]:
        lines += ["", "Retried attempts (not scored): " + ", ".join(report["retried_attempts"])]
    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("run_dir", type=Path)
    args = parser.parse_args(argv)
    report = summarize(args.run_dir)
    output = Path(report["run_dir"]) / "summary"
    output.mkdir(exist_ok=True)
    (output / "summary.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8",
    )
    (output / "tasks.csv").write_text(render_csv(report), encoding="utf-8")
    (output / "summary.md").write_text(render_markdown(report), encoding="utf-8")
    print(json.dumps({"output_dir": str(output), **report["aggregate"]}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
