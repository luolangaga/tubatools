"""Drive the task agent through the turns of one task phase.

Each turn launches the guest launcher as a transient systemd unit and polls
for its turn-scoped completion record. A watchdog detects a vanished agent
or a failed guest filesystem, the model-call budget can stop a turn early,
and ``ASK_USER`` questions are answered by the task's user simulator.
"""

from __future__ import annotations

from dataclasses import dataclass
import datetime
import json
import logging
from pathlib import Path
import shlex
import sys
import time
from typing import TYPE_CHECKING

import requests

from hcu.osworld.agents import AGENT_PROFILES, thread_id_from_trace
from hcu.osworld.diagnostics import (
    collect_guest_infra_diagnostic,
    collect_workload_unit_diagnostic,
)
from hcu.osworld.guest import (
    GUEST_DONE,
    GUEST_FINAL,
    GUEST_LAUNCHER,
    GUEST_LAUNCHER_STATUS,
    GUEST_PROMPT,
    GUEST_STDERR,
    GUEST_TRACE,
    fetch_guest_file,
    request_execute,
    request_launch,
    save_guest_artifact,
)
from hcu.osworld.prompts import (
    ASK_USER_SENTINEL,
    DEFAULT_USER_RESPONSE,
    task_prompt,
    tool_argument_resume_prompt,
    transient_resume_prompt,
    user_question,
    user_response_prompt,
)
from hcu.osworld.traces import concise_failure_message, summarize_agent_trace
from hcu.osworld.usage import read_task_log

if TYPE_CHECKING:
    from desktop_env.desktop_env import DesktopEnv


def write_text(path: Path, content: str) -> None:
    path.write_text(content, encoding="utf-8")


AGENT_WATCHDOG_GRACE_SEC = 120.0
AGENT_WATCHDOG_INTERVAL_SEC = 30.0
AGENT_WATCHDOG_ABSENT_THRESHOLD = 2
# The gateway already waits out most rate limits. A turn that still ends in a
# rate limit resumes the same session; the backoff extends the phase deadline.
RATE_LIMIT_RESUME_LIMIT = 2
RATE_LIMIT_RESUME_BACKOFF_SEC = 60.0
TOOL_ARGUMENT_RETRY_LIMIT = 1
# Also bounds final-response tool execution when the operator sets no timeout.
MODEL_PROGRESS_TIMEOUT_SEC = 1800.0


@dataclass(frozen=True)
class TurnSettings:
    """Run options that control how agent turns are launched and stopped."""

    agent: str
    max_model_calls: int
    poll_interval: float
    max_user_simulator_turns: int


class AgentProcessLostError(RuntimeError):
    pass


class GuestFilesystemError(RuntimeError):
    pass


class AgentWatchdog:
    def __init__(
        self,
        result_dir: Path,
        *,
        workload_unit: str,
        process_name: str = "codex",
        grace_sec: float = AGENT_WATCHDOG_GRACE_SEC,
        interval_sec: float = AGENT_WATCHDOG_INTERVAL_SEC,
        absent_threshold: int = AGENT_WATCHDOG_ABSENT_THRESHOLD,
    ):
        self.result_dir = result_dir
        self.workload_unit = workload_unit
        self.process_name = process_name
        self.grace_sec = grace_sec
        self.interval_sec = interval_sec
        self.absent_threshold = absent_threshold
        self.started_at = time.monotonic()
        self.last_check_at: float | None = None
        self.consecutive_absences = 0

    def guest_state(self, base_url: str) -> str:
        result = request_execute(
            base_url,
            [
                "bash",
                "-lc",
                "options=$(findmnt -no OPTIONS / 2>/dev/null || true); "
                "available_kb=$(df -Pk / 2>/dev/null "
                "| awk 'NR==2 {print $4}'); "
                "probe=/tmp/.osworld-watchdog-write-$$; "
                "if case \",$options,\" in *,ro,*) true;; *) false;; esac; "
                "then echo filesystem_failure; "
                "elif [ \"${available_kb:-0}\" -le 0 ]; "
                "then echo disk_full; "
                "elif ! (umask 077; printf ok >\"$probe\") 2>/dev/null; "
                "then echo filesystem_failure; "
                "elif rm -f \"$probe\" 2>/dev/null "
                f"&& pgrep -x {shlex.quote(self.process_name)} >/dev/null; then "
                f"echo {shlex.quote(self.process_name)}; "
                f"elif pgrep -f '^/bin/bash {GUEST_LAUNCHER}' "
                ">/dev/null; then "
                "echo launcher; "
                "else echo absent; fi",
            ],
            timeout=30,
        )
        return str(result.get("output", "")).strip()

    def preserve(self, base_url: str, state: str, label: str) -> None:
        preserved = {}
        artifact_map = {
            GUEST_TRACE: f"{label}-agent.jsonl",
            GUEST_STDERR: f"{label}-agent.stderr",
            GUEST_FINAL: f"{label}-final.txt",
            GUEST_LAUNCHER_STATUS: f"{label}-launcher-status.json",
        }
        for guest_path, local_name in artifact_map.items():
            local_path = self.result_dir / local_name
            try:
                data = fetch_guest_file(
                    base_url,
                    guest_path,
                    attempts=3,
                    initial_backoff_sec=1.0,
                )
            except requests.RequestException:
                data = None
            if data is not None:
                local_path.write_bytes(data)
                preserved[local_name] = len(data)

        screenshot_path = self.result_dir / f"{label}-agent-process-lost.png"
        try:
            response = requests.get(f"{base_url}/screenshot", timeout=30)
            response.raise_for_status()
            screenshot_path.write_bytes(response.content)
            preserved[screenshot_path.name] = len(response.content)
        except requests.RequestException:
            pass

        infra = collect_guest_infra_diagnostic(base_url)
        infra_text_path = self.result_dir / f"{label}-guest-infra.txt"
        if infra["output"]:
            infra_text_path.write_text(
                infra["output"],
                encoding="utf-8",
            )
            preserved[infra_text_path.name] = len(
                infra["output"].encode("utf-8")
            )

        workload = collect_workload_unit_diagnostic(
            base_url,
            self.workload_unit,
        )
        workload_text_path = (
            self.result_dir / f"{label}-workload-unit.txt"
        )
        if workload["output"]:
            workload_text_path.write_text(
                workload["output"],
                encoding="utf-8",
            )
            preserved[workload_text_path.name] = len(
                workload["output"].encode("utf-8")
            )

        diagnostic = {
            "status": "agent_process_lost",
            "guest_process_state": state,
            "workload_unit": self.workload_unit,
            "workload_properties": workload["properties"],
            "workload_classifications": workload["classifications"],
            "workload_transport_error": workload["transport_error"],
            "consecutive_absences": self.consecutive_absences,
            "preserved_artifacts": preserved,
            "infra_classifications": infra["classifications"],
            "infra_transport_error": infra["transport_error"],
            "detected_at": time.strftime(
                "%Y-%m-%dT%H:%M:%SZ",
                time.gmtime(),
            ),
        }
        (self.result_dir / f"{label}-agent-process-lost.json").write_text(
            json.dumps(diagnostic, indent=2),
            encoding="utf-8",
        )

    def check(self, base_url: str, label: str) -> None:
        now = time.monotonic()
        if now - self.started_at < self.grace_sec:
            return
        if (
            self.last_check_at is not None
            and now - self.last_check_at < self.interval_sec
        ):
            return
        self.last_check_at = now
        try:
            state = self.guest_state(base_url)
        except Exception as exc:
            print(
                "Agent watchdog process check failed: "
                f"{type(exc).__name__}: {exc}",
                file=sys.stderr,
                flush=True,
            )
            return
        if state in {self.process_name, "launcher"}:
            self.consecutive_absences = 0
            return
        if state in {"filesystem_failure", "disk_full"}:
            self.preserve(base_url, state, label)
            if state == "disk_full":
                raise GuestFilesystemError(
                    "Guest root filesystem exhausted all available space "
                    "during the agent run"
                )
            raise GuestFilesystemError(
                "Guest root filesystem became read-only during the agent run"
            )
        if state != "absent":
            self.consecutive_absences = 0
            return
        self.consecutive_absences += 1
        if self.consecutive_absences < self.absent_threshold:
            return
        self.preserve(base_url, state, label)
        raise AgentProcessLostError(
            f"{self.process_name} and the guest launcher disappeared before producing "
            f"{GUEST_DONE}"
        )


def append_artifact(path: Path, data: bytes | None) -> None:
    if not data:
        return
    with path.open("ab") as handle:
        handle.write(data)
        if not data.endswith(b"\n"):
            handle.write(b"\n")


def upload_prompt(env: DesktopEnv, local_path: Path, content: str) -> None:
    write_text(local_path, content)
    env.setup_controller._upload_file_setup([
        {"local_path": str(local_path), "path": GUEST_PROMPT},
    ])


def wait_for_agent_turn(
    *,
    base_url: str,
    result_dir: Path,
    label: str,
    settings: TurnSettings,
    token_log: Path,
    affinity_task: str,
    expected_turn_token: str,
    workload_unit: str,
    deadline: float,
) -> dict:
    agent = settings.agent
    watchdog = AgentWatchdog(
        result_dir,
        workload_unit=workload_unit,
        process_name=AGENT_PROFILES[agent]["process"],
    )
    done_payload = None
    stop_attempted = False

    def read_log():
        state = read_task_log(token_log, affinity_task)
        if state.retry_exhausted:
            raise RuntimeError("Gateway consecutive generation failure limit exhausted")
        return state

    def fetch_done() -> dict | None:
        raw = fetch_guest_file(base_url, GUEST_DONE)
        if raw:
            candidate = json.loads(raw)
            if candidate.get("turn_token") == expected_turn_token:
                return candidate
        return None

    def stop_agent() -> None:
        nonlocal stop_attempted
        stop_attempted = True
        request_execute(
            base_url,
            [
                "bash",
                "-lc",
                "pids=$(pgrep -x "
                + shlex.quote(AGENT_PROFILES[agent]["process"])
                + " || true); "
                "[ -z \"$pids\" ] || kill -TERM $pids",
            ],
            timeout=30,
        )
        time.sleep(3)

    artifacts = {}
    original_error = None
    rate_limit_wait_sec = 0.0
    try:
        state = read_log()
        initial_wait = state.rate_limit_wait_sec
        progress = (state.completed_responses, state.rate_limit_waits)
        last_progress = time.monotonic()
        while True:
            # Gateway rate-limit waits are infrastructure time: they extend
            # the deadline and count as progress.
            rate_limit_wait_sec = state.rate_limit_wait_sec - initial_wait
            if time.monotonic() >= min(
                deadline + rate_limit_wait_sec,
                last_progress + MODEL_PROGRESS_TIMEOUT_SEC,
            ):
                break
            done_payload = fetch_done()
            if done_payload is not None:
                break
            watchdog.check(base_url, label)
            # Response N may still execute tools; only rejection of N+1 stops it.
            if state.budget_rejected:
                break
            time.sleep(settings.poll_interval)
            state = read_log()
            if (state.completed_responses, state.rate_limit_waits) != progress:
                progress = (state.completed_responses, state.rate_limit_waits)
                last_progress = time.monotonic()

        state = read_log()
        budget_exhausted = state.budget_rejected
        truncated = done_payload is None or budget_exhausted
        if truncated:
            stop_agent()
            done_payload = fetch_done() or done_payload
            budget_exhausted = read_log().budget_rejected
    except BaseException as exc:
        original_error = exc
        raise
    finally:
        # Preserve every available artifact before outer cleanup destroys the guest.
        # Secondary stop/export errors must not replace the original failure.
        if original_error is not None and not stop_attempted:
            try:
                stop_agent()
            except Exception:
                logging.exception("Failed to stop agent while preserving turn failure")
        export_error = None
        for guest_path, suffix, aggregate in (
            (GUEST_TRACE, "agent.jsonl", "agent.jsonl"),
            (GUEST_STDERR, "agent.stderr", "agent.stderr"),
            (GUEST_FINAL, "final.txt", None),
        ):
            try:
                data = save_guest_artifact(base_url, guest_path, result_dir / f"{label}-{suffix}")
                artifacts[guest_path] = data
                if aggregate:
                    append_artifact(result_dir / aggregate, data)
            except Exception as exc:
                logging.exception("Failed to preserve turn artifact %s", guest_path)
                if export_error is None:
                    export_error = exc
        if original_error is None and export_error is not None:
            raise export_error

    trace_raw = artifacts.get(GUEST_TRACE)
    stderr_raw = artifacts.get(GUEST_STDERR)
    final_raw = artifacts.get(GUEST_FINAL)
    final_message = (
        final_raw.decode("utf-8", errors="replace").strip()
        if final_raw else ""
    )
    return {
        "done_payload": done_payload,
        "truncated": truncated,
        "budget_exhausted": budget_exhausted,
        "trace_raw": trace_raw,
        "stderr_raw": stderr_raw,
        "final_message": final_message,
        "thread_id": thread_id_from_trace(trace_raw, agent),
        "workload_unit": workload_unit,
        "rate_limit_wait_sec": round(rate_limit_wait_sec, 3),
    }


def run_agent_phase(
    *,
    env: DesktopEnv,
    base_url: str,
    result_dir: Path,
    prompt_path: Path,
    phase_index: int,
    phase_name: str,
    instruction: str,
    current_date: str | None,
    settings: TurnSettings,
    token_log: Path,
    affinity_task: str,
    deadline: float,
    multi_phase: bool,
) -> dict:
    agent = settings.agent
    can_ask_user = env.user_simulator is not None
    upload_prompt(
        env,
        prompt_path,
        task_prompt(
            instruction,
            current_date=current_date,
            can_ask_user=can_ask_user,
        ),
    )
    session_id = ""
    user_exchanges = []
    turns = []

    def phase_result(turn: dict) -> dict:
        return {
            "phase_index": phase_index,
            "phase_name": phase_name,
            "instruction": instruction,
            "turns": turns,
            "user_exchanges": user_exchanges,
            "final_message": turn["final_message"],
            "done_payload": turn["done_payload"],
            "truncated": turn["truncated"],
            "budget_exhausted": turn["budget_exhausted"],
        }

    turn_index = 0
    retries = _TurnRetries()
    while True:
        turn_index += 1
        label = f"phase-{phase_index:02d}-turn-{turn_index:02d}"
        workload_unit = _launch_agent_turn(
            base_url, result_dir, agent, session_id, label
        )
        turn = wait_for_agent_turn(
            base_url=base_url,
            result_dir=result_dir,
            label=label,
            settings=settings,
            token_log=token_log,
            affinity_task=affinity_task,
            expected_turn_token=label,
            workload_unit=workload_unit,
            deadline=deadline,
        )
        deadline += turn["rate_limit_wait_sec"]
        trace_summary = summarize_agent_trace(turn["trace_raw"], agent)
        observed_thread_id = turn["thread_id"] or session_id
        turns.append(_turn_record(
            turn_index,
            label,
            observed_thread_id,
            workload_unit,
            turn,
            trace_summary,
            retries,
        ))
        if turn["truncated"]:
            return phase_result(turn)
        returncode = (
            turn["done_payload"].get("returncode")
            if turn["done_payload"] else None
        )
        if returncode not in {0, None}:
            deadline += _resume_failed_turn(
                env=env,
                prompt_path=prompt_path,
                agent=agent,
                turn_index=turn_index,
                returncode=returncode,
                turn=turn,
                trace_summary=trace_summary,
                observed_thread_id=observed_thread_id,
                retries=retries,
                deadline=deadline,
            )
            session_id = observed_thread_id
            continue
        if not session_id:
            session_id = turn["thread_id"]
        question = user_question(turn["final_message"])
        if question is None:
            return phase_result(turn)
        if not session_id:
            raise RuntimeError(
                f"ASK_USER requires a persisted {agent} session ID, "
                "but none was recorded"
            )
        if (
            settings.max_user_simulator_turns > 0
            and len(user_exchanges) >= settings.max_user_simulator_turns
        ):
            raise RuntimeError(
                f"Phase {phase_index} exceeded --max-user-simulator-turns="
                f"{settings.max_user_simulator_turns}"
            )
        answer = DEFAULT_USER_RESPONSE
        if env.user_simulator is not None:
            answer = env.user_simulator.respond(question)
        exchange = {
            "phase_index": phase_index,
            "phase_name": phase_name,
            "turn_index": turn_index,
            "question": question,
            "answer": answer,
            "timestamp": datetime.datetime.now().strftime(
                "%Y%m%d@%H%M%S%f"
            ),
        }
        user_exchanges.append(exchange)
        _append_user_exchange_logs(result_dir, exchange, multi_phase)
        upload_prompt(env, prompt_path, user_response_prompt(answer))


@dataclass
class _TurnRetries:
    """Resumes already spent on failed turns of one phase."""

    rate_limit_resumes: int = 0
    tool_argument_retries: int = 0


def _launch_agent_turn(
    base_url: str,
    result_dir: Path,
    agent: str,
    session_id: str,
    label: str,
) -> str:
    """Clear the previous turn's files and start or resume the agent."""
    request_execute(
        base_url,
        [
            "rm",
            "-f",
            GUEST_DONE,
            GUEST_TRACE,
            GUEST_STDERR,
            GUEST_FINAL,
        ],
        timeout=30,
    )
    command = [GUEST_LAUNCHER, "start", "", label]
    if session_id:
        if not AGENT_PROFILES[agent]["can_resume"]:
            raise RuntimeError(
                f"{agent} cannot resume its session under the same system "
                "prompt, so it cannot answer ASK_USER"
            )
        command = [GUEST_LAUNCHER, "resume", session_id, label]
    workload_unit = request_launch(base_url, command)
    write_text(
        result_dir / f"{label}-workload-unit.json",
        json.dumps(
            {
                "unit_name": workload_unit,
                "turn_token": label,
                "requested_at": time.strftime(
                    "%Y-%m-%dT%H:%M:%SZ",
                    time.gmtime(),
                ),
            },
            indent=2,
        ),
    )
    return workload_unit


def _turn_record(
    turn_index: int,
    label: str,
    observed_thread_id: str,
    workload_unit: str,
    turn: dict,
    trace_summary: dict,
    retries: _TurnRetries,
) -> dict:
    return {
        "turn_index": turn_index,
        "label": label,
        "thread_id": observed_thread_id,
        "workload_unit": workload_unit,
        "agent_returncode": (
            turn["done_payload"].get("returncode")
            if turn["done_payload"] else None
        ),
        "truncated": turn["truncated"],
        "budget_exhausted": turn["budget_exhausted"],
        "final_message": turn["final_message"],
        "agent_error": trace_summary["agent_error"],
        "failure_classification": (
            trace_summary["failure_classification"]
        ),
        "rate_limit_wait_sec": turn["rate_limit_wait_sec"],
        "retry_assisted": (
            retries.rate_limit_resumes > 0
            or retries.tool_argument_retries > 0
        ),
    }


def _resume_failed_turn(
    *,
    env: DesktopEnv,
    prompt_path: Path,
    agent: str,
    turn_index: int,
    returncode: int,
    turn: dict,
    trace_summary: dict,
    observed_thread_id: str,
    retries: _TurnRetries,
    deadline: float,
) -> float:
    """Prepare to resume a failed turn's session, or raise.

    A rate limit or an oversized tool argument resumes the same session with
    a prompt that explains the failure, within its retry limit. Returns the
    time the resume adds to the phase deadline. Any other failure raises.
    """
    failure_classification = trace_summary[
        "failure_classification"
    ]
    can_resume = AGENT_PROFILES[agent]["can_resume"]
    if (
        failure_classification == "upstream_rate_limit"
        and can_resume
        and observed_thread_id
        and retries.rate_limit_resumes < RATE_LIMIT_RESUME_LIMIT
    ):
        retries.rate_limit_resumes += 1
        upload_prompt(
            env,
            prompt_path,
            transient_resume_prompt(
                trace_summary["agent_error"]
                or "upstream rate limit"
            ),
        )
        print(
            f"{agent} turn ended on a rate limit; resuming the same session "
            f"after {RATE_LIMIT_RESUME_BACKOFF_SEC:.0f}s "
            f"({retries.rate_limit_resumes}/{RATE_LIMIT_RESUME_LIMIT})",
            file=sys.stderr,
            flush=True,
        )
        time.sleep(RATE_LIMIT_RESUME_BACKOFF_SEC)
        return RATE_LIMIT_RESUME_BACKOFF_SEC
    if (
        failure_classification == "tool_argument_too_large"
        and can_resume
        and observed_thread_id
        and retries.tool_argument_retries < TOOL_ARGUMENT_RETRY_LIMIT
        and time.monotonic() < deadline
    ):
        retries.tool_argument_retries += 1
        upload_prompt(
            env,
            prompt_path,
            tool_argument_resume_prompt(),
        )
        print(
            f"{agent} shell argv exceeded the process limit; resuming "
            "the same thread with binary-safe transfer guidance "
            f"({retries.tool_argument_retries}/{TOOL_ARGUMENT_RETRY_LIMIT})",
            file=sys.stderr,
            flush=True,
        )
        return 0.0
    stderr_text = (
        turn["stderr_raw"].decode(
            "utf-8",
            errors="replace",
        ).strip()
        if turn["stderr_raw"] else ""
    )
    failure_message = concise_failure_message(
        trace_summary["agent_error"]
        or stderr_text
        or "no stderr"
    )
    raise RuntimeError(
        f"{agent} turn {turn_index} exited with return code "
        f"{returncode}: {failure_message}"
    )


def _append_user_exchange_logs(
    result_dir: Path,
    exchange: dict,
    multi_phase: bool,
) -> None:
    """Append one ASK_USER exchange to the simulator log and the trajectory."""
    turn_index = exchange["turn_index"]
    question = exchange["question"]
    answer = exchange["answer"]
    phase_index = exchange["phase_index"]
    phase_name = exchange["phase_name"]
    with (result_dir / "user-simulator.jsonl").open(
        "a",
        encoding="utf-8",
    ) as handle:
        handle.write(json.dumps(exchange, ensure_ascii=False) + "\n")
    trajectory_row = {
        "step_num": turn_index,
        "action_timestamp": exchange["timestamp"],
        "action": ASK_USER_SENTINEL,
        "question": question,
        "user_answer": answer,
        "screenshot_file": None,
    }
    if multi_phase:
        trajectory_row.update({
            "phase_index": phase_index,
            "phase_name": phase_name,
        })
    with (result_dir / "traj.jsonl").open(
        "a",
        encoding="utf-8",
    ) as handle:
        handle.write(json.dumps(
            trajectory_row,
            ensure_ascii=False,
        ))
        handle.write("\n")
