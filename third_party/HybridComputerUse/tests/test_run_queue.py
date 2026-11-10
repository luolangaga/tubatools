"""Config validation, run ownership, resume and retries of the queue.

No Docker, OSWorld checkout or model: children are short Python processes.
"""

import json
from pathlib import Path
import sys

import pytest

from hcu.osworld import run_queue
from hcu.osworld.config import load_config, runner_command


def write_config(path: Path, **values) -> Path:
    path.write_text(json.dumps({"tasks": ["a", "b"], **values}))
    return path


def test_defaults_and_relative_paths(tmp_path):
    path = write_config(
        tmp_path / "panel.json", guest_image="images/guest.qcow2", max_model_calls=5,
    )
    config = load_config(path)
    assert config["run_id"] == "panel"
    assert config["benchmark_version"] == "v2"
    assert config["max_active_tasks"] == 1
    # Relative paths are relative to the config file, not the working directory.
    assert config["guest_image"] == str(tmp_path / "images/guest.qcow2")
    assert config["token_log"] == str(Path(config["results_dir"]) / "panel/gateway-tokens.jsonl")
    assert [task["id"] for task in config["tasks"]] == ["a", "b"]


@pytest.mark.parametrize("values, message", [
    ({"gateway_base": "http://x"}, "Unknown config keys: gateway_base"),
    ({"environment_file": "eval.env"}, "Unknown config keys"),
    ({"benchmark_version": "v3"}, "v1 or v2"),
    ({"run_id": "a/b"}, "run_id"),
    ({"tasks": []}, "tasks must not be empty"),
    ({"tasks": ["a", "a"]}, "unique"),
    ({"tasks": [{"id": "a", "flavor": 1}]}, "flavor"),
    ({"tasks": [".."]}, "task id"),
    ({"max_active_tasks": 0}, "max_active_tasks"),
])
def test_invalid_configs_are_rejected(tmp_path, values, message):
    with pytest.raises(ValueError, match=message):
        load_config(write_config(tmp_path / "c.json", **values))


def test_runner_command_forwards_options_and_task_overrides(tmp_path):
    config = load_config(write_config(
        tmp_path / "c.json", benchmark_version="v1", agent="codex", model="gpt-5.4",
        volume_size=64, ram_size="4G", task_domain="chrome", instructions_file="",
        channels="gui,keyboard", offline_assets=True, retain_containers=False,
        gateway="http://172.21.194.1:8611",
        tasks=[{"id": "a", "task_domain": "gimp", "ram_size": "8G", "stratum": "s"}],
    ))
    command = runner_command(config, config["tasks"][0], tmp_path / "a", "2026-10-18T00:00:00Z")

    def value(flag):
        return command[command.index(flag) + 1]

    assert command[:3] == [sys.executable, "-m", "hcu.osworld.run_task"]
    assert value("--task-id") == "a"
    assert value("--benchmark-version") == "v1"
    assert value("--task-domain") == "gimp"
    assert value("--ram-size") == "8G"
    assert value("--volume-size") == "64"
    assert value("--channels") == "gui,keyboard"
    assert value("--gateway") == "http://172.21.194.1:8611"
    assert value("--run-started-at") == "2026-10-18T00:00:00Z"
    # An empty instructions_file disables the profile instead of using the default.
    assert value("--instructions-file") == ""
    assert "--offline-assets" in command and "--retain-container" not in command
    assert "--stratum" not in command


def test_example_configs_load_and_name_existing_repo_files():
    root = Path(__file__).resolve().parents[1]
    for path in sorted((root / "configs").glob("*.example.json")):
        config = load_config(path)
        command = runner_command(config, config["tasks"][0], Path("/unused"))
        assert "--task-id" in command, path


@pytest.fixture
def queue_config(tmp_path):
    def make(**values):
        values.setdefault("results_dir", str(tmp_path / "runs"))
        return load_config(write_config(tmp_path / "panel.json", **values))
    return make


def test_same_config_resumes_and_scheduling_may_change(queue_config):
    base = run_queue.claim_run(queue_config())
    assert (base / "config.json").is_file()
    assert run_queue.claim_run(queue_config(max_active_tasks=4)) == base


def test_different_config_cannot_reuse_the_run_directory(queue_config):
    run_queue.claim_run(queue_config(model="gpt-5.4"))
    with pytest.raises(SystemExit, match="changed: model"):
        run_queue.claim_run(queue_config(model="gpt-5.5"))


def test_foreign_directory_is_not_adopted(queue_config, tmp_path):
    (tmp_path / "runs/panel").mkdir(parents=True)
    (tmp_path / "runs/panel/something").write_text("x")
    with pytest.raises(SystemExit, match="no config.json"):
        run_queue.claim_run(queue_config())


def fake_runner(monkeypatch, results: dict):
    """Children write the given result.json (or nothing) and exit."""
    def command(config, task, result_dir, started=""):
        payload = results[task["id"]]
        code = (
            "import json, pathlib, sys; d = pathlib.Path(sys.argv[1]); "
            "d.mkdir(parents=True, exist_ok=True); "
            + (f"(d / 'result.json').write_text({json.dumps(json.dumps(payload))})" if payload else "pass")
        )
        return [sys.executable, "-c", code, str(result_dir)]

    monkeypatch.setattr(run_queue, "runner_command", command)


def run_to_completion(config):
    queue = run_queue.Queue(config, poll_interval=0.05)
    return queue, queue.run()


def test_queue_runs_tasks_and_reports_scores(queue_config, monkeypatch):
    fake_runner(monkeypatch, {
        "a": {"status": "completed", "score": 1.0, "termination_reason": "completed"},
        "b": {"status": "completed", "score": 1.0, "termination_reason": "max_model_calls"},
    })
    queue, code = run_to_completion(queue_config(max_active_tasks=2))
    assert code == 0
    status = (queue.base / "QUEUE_STATUS.md").read_text()
    assert "| a |  | valid | 1.0 |" in status
    assert "| b |  | budget | 0.0 |" in status


def test_child_without_result_is_a_failure_not_a_score(queue_config, monkeypatch):
    fake_runner(monkeypatch, {"a": None, "b": {"status": "completed", "score": 0.0}})
    queue, code = run_to_completion(queue_config())
    assert code == 1
    assert "without result.json" in queue.failures["a"]


def test_rate_limited_task_is_retried_not_scored(queue_config, monkeypatch):
    attempts = []
    rate_limited = {
        "status": "error", "failure_classification": "upstream_rate_limit",
        "error": "rate limit exceeded",
    }

    def command(config, task, result_dir, started=""):
        attempts.append(task["id"])
        payload = rate_limited if attempts.count(task["id"]) == 1 else {"status": "completed", "score": 1.0}
        code = (
            "import json, pathlib, sys; d = pathlib.Path(sys.argv[1]); d.mkdir(parents=True); "
            f"(d / 'result.json').write_text({json.dumps(json.dumps(payload))})"
        )
        return [sys.executable, "-c", code, str(result_dir)]

    monkeypatch.setattr(run_queue, "runner_command", command)
    monkeypatch.setattr(run_queue, "INFRASTRUCTURE_PAUSE_SEC", 0.0)
    queue, code = run_to_completion(queue_config(tasks=["a"]))
    assert code == 0 and attempts == ["a", "a"]
    assert json.loads((queue.base / "tasks/a/result.json").read_text())["score"] == 1.0
    assert "upstream_rate_limit" in (queue.base / "retries/a-1/reason.txt").read_text()
    assert json.loads((queue.base / "QUEUE_STATE.json").read_text())["retries"] == {"a": 1}


def test_restart_moves_unfinished_attempts_aside_and_keeps_finished_ones(queue_config, monkeypatch):
    config = queue_config()
    base = run_queue.claim_run(config)
    (base / "tasks/a").mkdir()
    (base / "tasks/a/result.json").write_text('{"status": "completed", "score": 1.0}')
    (base / "tasks/b").mkdir()
    (base / "tasks/b/meta.json").write_text("{}")
    (base / "QUEUE_STATE.json").write_text(json.dumps({
        "run_started_at": "2026-10-18T00:00:00Z", "children": {"b": 999999999},
    }))
    launched = []
    fake_runner(monkeypatch, {"b": {"status": "completed", "score": 0.0}})
    original = run_queue.runner_command

    def record(config, task, result_dir, started=""):
        launched.append((task["id"], started))
        return original(config, task, result_dir, started)

    monkeypatch.setattr(run_queue, "runner_command", record)
    queue, code = run_to_completion(config)
    assert code == 0
    # The run keeps its original start time, so task dates do not drift.
    assert launched == [("b", "2026-10-18T00:00:00Z")]
    assert (base / "retries/b-1/task/meta.json").is_file()


def test_dry_run_prints_commands_without_creating_the_run(tmp_path, capsys):
    path = write_config(tmp_path / "c.json", results_dir=str(tmp_path / "runs"), max_model_calls=3)
    assert run_queue.main([str(path), "--dry-run"]) == 0
    out = capsys.readouterr().out
    assert "--max-requests-per-task 3" in out
    assert len([line for line in out.splitlines() if line.startswith("[")]) == 2
    assert not (tmp_path / "runs").exists()
