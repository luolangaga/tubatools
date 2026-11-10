"""Run the tasks of one configuration, a bounded number at a time.

    python -m hcu.osworld.run_queue CONFIG [--dry-run]

Results go to ``<results_dir>/<run_id>/``::

    config.json          the configuration this run directory belongs to
    tasks/<id>/          one attempt per task; result.json is its outcome
    logs/<id>.log        runner output of the current attempt
    retries/<id>-<n>/    earlier attempts that infrastructure interrupted
    QUEUE_STATE.json     live children and the run start time, for resume
    QUEUE_STATUS.md      progress, rewritten every poll

Restarting the same command resumes the run: finished tasks are kept, live
children are adopted, and attempts without a result are moved to retries/.
A task whose attempt failed for an infrastructure reason (for example an
upstream rate limit) is retried after a pause instead of being scored.
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import time

from hcu.osworld.config import load_config, run_dir, runner_command
from hcu.osworld.summarize import aggregate, read_task
from hcu.osworld.traces import INFRASTRUCTURE_FAILURES
from hcu.paths import REPO_ROOT

# Keys an operator may change when resuming a run.
SCHEDULING_KEYS = ("max_active_tasks", "launch_stagger_seconds")
INFRASTRUCTURE_RETRY_LIMIT = 3
INFRASTRUCTURE_PAUSE_SEC = 300.0


def write_json(path: Path, value) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def read_result(task_dir: Path) -> dict | None:
    try:
        result = json.loads((task_dir / "result.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    return result if isinstance(result, dict) else None


def claim_run(config: dict) -> Path:
    """Create the run directory, or resume it if it holds the same config."""
    base = run_dir(config)
    saved_path = base / "config.json"
    comparable = {key: value for key, value in config.items() if key not in SCHEDULING_KEYS}
    if saved_path.is_file():
        saved = json.loads(saved_path.read_text(encoding="utf-8"))
        saved = {key: value for key, value in saved.items() if key not in SCHEDULING_KEYS}
        if saved != comparable:
            changed = sorted(
                key for key in set(saved) | set(comparable)
                if saved.get(key) != comparable.get(key)
            )
            raise SystemExit(
                f"{base} belongs to a different configuration (changed: {', '.join(changed)}). "
                "Use a new run_id."
            )
    elif base.exists() and any(base.iterdir()):
        raise SystemExit(f"{base} exists but has no config.json; use a new run_id.")
    for name in ("tasks", "logs", "retries"):
        (base / name).mkdir(parents=True, exist_ok=True)
    write_json(saved_path, config)
    return base


def check_guest_image(config: dict) -> None:
    if not config.get("guest_image"):
        return
    from desktop_env.providers.volume import validate_guest_image_capacity

    required = max(int(task.get("volume_size", config.get("volume_size", 40))) for task in config["tasks"])
    validate_guest_image_capacity(config["guest_image"], required)


def process_alive(pid: int, result_dir: Path) -> bool:
    """True if ``pid`` is still the runner child for ``result_dir``."""
    try:
        argv = Path(f"/proc/{pid}/cmdline").read_bytes().split(b"\0")
    except OSError:
        return False
    return str(result_dir).encode() in argv


class Queue:
    def __init__(self, config: dict, poll_interval: float) -> None:
        self.config = config
        self.poll_interval = poll_interval
        self.base = claim_run(config)
        self.children: dict[str, subprocess.Popen | int] = {}
        self.failures: dict[str, str] = {}
        self.retries: dict[str, int] = {}
        self.paused_until = 0.0
        self.stop_requested = False
        state = self.read_state()
        self.started_at = state.get("run_started_at") or time.strftime(
            "%Y-%m-%dT%H:%M:%SZ", time.gmtime()
        )
        self.retries = {str(k): int(v) for k, v in (state.get("retries") or {}).items()}
        for task in config["tasks"]:
            task_id = task["id"]
            pid = (state.get("children") or {}).get(task_id)
            if pid and process_alive(int(pid), self.task_dir(task_id)):
                self.children[task_id] = int(pid)
            elif self.task_dir(task_id).exists() and read_result(self.task_dir(task_id)) is None:
                self.move_to_retries(task_id, "interrupted before writing result.json")

    def read_state(self) -> dict:
        try:
            return json.loads((self.base / "QUEUE_STATE.json").read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return {}

    def task_dir(self, task_id: str) -> Path:
        return self.base / "tasks" / task_id

    def move_to_retries(self, task_id: str, reason: str) -> None:
        attempt = len(list((self.base / "retries").glob(f"{task_id}-*"))) + 1
        destination = self.base / "retries" / f"{task_id}-{attempt}"
        destination.mkdir(parents=True)
        if self.task_dir(task_id).exists():
            self.task_dir(task_id).replace(destination / "task")
        log = self.base / "logs" / f"{task_id}.log"
        if log.exists():
            log.replace(destination / "runner.log")
        (destination / "reason.txt").write_text(reason + "\n", encoding="utf-8")

    def status(self, task_id: str) -> str:
        if task_id in self.children:
            return "running"
        if task_id in self.failures:
            return "failed"
        result = read_result(self.task_dir(task_id))
        return "pending" if result is None else "finished"

    def launch(self, task: dict) -> None:
        task_id = task["id"]
        command = runner_command(self.config, task, self.task_dir(task_id), self.started_at)
        with (self.base / "logs" / f"{task_id}.log").open("ab", buffering=0) as log:
            self.children[task_id] = subprocess.Popen(
                command, cwd=REPO_ROOT, stdout=log, stderr=subprocess.STDOUT,
                env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"},
                start_new_session=True,
            )

    def reap(self) -> None:
        for task_id, child in list(self.children.items()):
            if isinstance(child, int):
                if process_alive(child, self.task_dir(task_id)):
                    continue
            elif child.poll() is None:
                continue
            del self.children[task_id]
            result = read_result(self.task_dir(task_id))
            if result is None:
                self.failures[task_id] = "runner exited without result.json; see logs/"
            elif (
                result.get("status") == "error"
                and result.get("failure_classification") in INFRASTRUCTURE_FAILURES
            ):
                self.retry_after_infrastructure_failure(task_id, result)

    def retry_after_infrastructure_failure(self, task_id: str, result: dict) -> None:
        count = self.retries.get(task_id, 0) + 1
        if count > INFRASTRUCTURE_RETRY_LIMIT:
            return  # result.json stays and reports the infrastructure failure
        self.retries[task_id] = count
        self.move_to_retries(task_id, f"{result['failure_classification']}: {result.get('error')}")
        self.paused_until = time.time() + INFRASTRUCTURE_PAUSE_SEC
        print(
            f"{task_id}: {result['failure_classification']}; retry {count}/"
            f"{INFRASTRUCTURE_RETRY_LIMIT} after a {INFRASTRUCTURE_PAUSE_SEC:.0f}s launch pause",
            flush=True,
        )

    def schedule(self) -> None:
        stagger = float(self.config.get("launch_stagger_seconds", 0))
        for task in self.config["tasks"]:
            if self.stop_requested or time.time() < self.paused_until:
                return
            if len(self.children) >= int(self.config["max_active_tasks"]):
                return
            if self.status(task["id"]) == "pending":
                self.launch(task)
                self.write_state()
                if stagger > 0:
                    time.sleep(stagger)

    def write_state(self) -> None:
        write_json(self.base / "QUEUE_STATE.json", {
            "run_started_at": self.started_at,
            "children": {
                task_id: child if isinstance(child, int) else child.pid
                for task_id, child in self.children.items()
            },
            "failures": self.failures,
            "retries": self.retries,
        })

    def render_status(self) -> None:
        rows = [read_task(self.task_dir(task["id"]), task) for task in self.config["tasks"]]
        totals = aggregate(rows)
        lines = [
            f"# {self.config['run_id']}",
            "",
            f"Updated: {time.strftime('%Y-%m-%d %H:%M:%S UTC', time.gmtime())}",
            f"Finished: {sum(row['status'] != 'missing' for row in rows)}/{len(rows)}; "
            f"running: {len(self.children)}/{self.config['max_active_tasks']}",
            f"Mean score so far: {totals['observed_mean']}",
            "Launch pause: "
            + (f"{self.paused_until - time.time():.0f}s" if time.time() < self.paused_until else "none"),
            "",
            "| Task | Stratum | State | Score |",
            "| --- | --- | --- | --- |",
        ]
        for task, row in zip(self.config["tasks"], rows):
            state = self.status(task["id"])
            if state == "finished":
                state = row["status"]
            elif state == "failed":
                state = f"failed: {self.failures[task['id']]}"
            lines.append(
                f"| {task['id']} | {task.get('stratum') or ''} | {state} | "
                f"{'' if row['score'] is None else row['score']} |"
            )
        (self.base / "QUEUE_STATUS.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    def done(self) -> bool:
        return not self.children and all(
            self.status(task["id"]) in {"finished", "failed"} for task in self.config["tasks"]
        )

    def run(self) -> int:
        signal.signal(signal.SIGINT, lambda *_: setattr(self, "stop_requested", True))
        signal.signal(signal.SIGTERM, lambda *_: setattr(self, "stop_requested", True))
        while True:
            self.reap()
            self.schedule()
            self.write_state()
            self.render_status()
            if self.done():
                return 1 if self.failures else 0
            if self.stop_requested:
                # Children keep running in their own sessions; a restart adopts them.
                return 130
            time.sleep(self.poll_interval)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("config", type=Path)
    parser.add_argument("--dry-run", action="store_true",
                        help="Validate the configuration and print each task's command.")
    parser.add_argument("--poll-interval", type=float, default=10.0)
    args = parser.parse_args(argv)
    config = load_config(args.config)
    if args.dry_run:
        base = run_dir(config)
        try:
            check_guest_image(config)
        except (OSError, RuntimeError, ValueError) as exc:
            print(f"guest image problem (a real run stops here): {exc}")
        print(f"run directory: {base}")
        print(f"token log: {config['token_log']}")
        if int(config.get("max_model_calls") or 0) > 0:
            print(
                "gateway must run with: --out "
                f"{config['token_log']} --max-requests-per-task {config['max_model_calls']}"
            )
        for task in config["tasks"]:
            print(json.dumps(runner_command(config, task, base / "tasks" / task["id"])))
        return 0
    check_guest_image(config)
    Path(config["token_log"]).parent.mkdir(parents=True, exist_ok=True)
    Path(config["token_log"]).touch(exist_ok=True)
    return Queue(config, args.poll_interval).run()


if __name__ == "__main__":
    raise SystemExit(main())
