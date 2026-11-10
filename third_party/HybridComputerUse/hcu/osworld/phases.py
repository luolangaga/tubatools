"""Run the agent and official evaluator for each task phase and write the run's result."""

from __future__ import annotations

from dataclasses import dataclass, field
import json
import math
from pathlib import Path
import sys
import time
import traceback

from hcu.osworld.agent_loop import TurnSettings, run_agent_phase
from hcu.osworld.diagnostics import collect_guest_infra_diagnostic
from hcu.osworld.evaluation import run_official_evaluator
from hcu.osworld.prompts import (
    COMPLETE_SENTINEL,
    INFEASIBLE_SENTINEL,
    split_completion_message,
)
from hcu.osworld.staging import TaskRun
from hcu.osworld.task import phase_delivery_status, task_value
from hcu.osworld.traces import (
    classify_turn_failure,
    concise_failure_message,
    summarize_agent_trace,
)
from hcu.osworld.usage import (
    collect_task_proxy_usage,
    normalize_budget_termination,
    reconcile_trace_and_proxy_usage,
)
from desktop_env.desktop_env import DesktopEnv


@dataclass
class PhaseOutcome:
    """Aggregated agent and evaluator results across the task phases."""

    phase_results: list[dict] = field(default_factory=list)
    phase_details: list[dict] = field(default_factory=list)
    total_score: float | None = None
    official_evaluator_errors: list[dict] = field(default_factory=list)
    truncated: bool = False
    budget_exhausted: bool = False
    truncated_phases: list[int] = field(default_factory=list)
    done_payload: dict | None = None
    final_message: str = ""
    evaluator_payload: dict | None = None
    final_response: str = ""
    completion_sentinel: str | None = None


def run_phases(run: TaskRun, env: DesktopEnv, base_url: str) -> PhaseOutcome:
    """Run the agent and the official evaluator for every task phase."""
    args = run.args
    meta = run.meta
    phase_definitions = run.phases or [{
        "name": "Task",
        "instruction": run.instruction,
        "evaluate": lambda current_env: current_env.evaluate(),
    }]
    outcome = PhaseOutcome()
    turn_settings = TurnSettings(
        agent=args.agent,
        max_model_calls=args.max_model_calls,
        poll_interval=args.poll_interval,
        max_user_simulator_turns=args.max_user_simulator_turns,
    )
    use_proxy = bool(
        task_value(run.task, "proxy", False)
        and getattr(env, "enable_proxy", False)
    )

    for phase_index, phase in enumerate(phase_definitions, start=1):
        phase_name = str(phase.get("name") or f"Phase {phase_index}")
        phase_instruction = str(phase.get("instruction") or run.instruction)
        if phase_index > 1:
            if hasattr(env, "_step_no"):
                env._step_no = 0
            if hasattr(env, "action_history"):
                env.action_history.clear()
            if hasattr(env, "_traj_no"):
                env._traj_no += 1
            setup = phase.get("setup")
            if not callable(setup):
                raise ValueError(
                    f"Phase {phase_index} has no callable setup"
                )
            setup(env.setup_controller, use_proxy=use_proxy)
            env.is_environment_used = True
            env.setup_controller.assert_root_writable()
            pause = float(phase.get("pause_after_setup_seconds", 5))
            if pause > 0:
                time.sleep(pause)

        env.instruction = phase_instruction
        meta["status"] = "agent_running"
        meta["active_phase"] = phase_index
        meta["active_phase_name"] = phase_name
        run.write_meta(ensure_ascii=False)
        phase_deadline = (
            float("inf")
            if args.timeout == 0
            else time.monotonic() + args.timeout
        )
        agent_phase = run_agent_phase(
            env=env,
            base_url=base_url,
            result_dir=run.result_dir,
            prompt_path=run.prompt_path,
            phase_index=phase_index,
            phase_name=phase_name,
            instruction=phase_instruction,
            current_date=run.current_date,
            settings=turn_settings,
            token_log=run.token_log,
            affinity_task=run.affinity_task,
            deadline=phase_deadline,
            multi_phase=bool(run.phases),
        )
        outcome.final_message = agent_phase["final_message"]
        outcome.done_payload = agent_phase["done_payload"]
        if agent_phase["truncated"]:
            outcome.truncated = True
            outcome.budget_exhausted = (
                outcome.budget_exhausted
                or bool(agent_phase["budget_exhausted"])
            )
            outcome.truncated_phases.append(phase_index)
        (
            outcome.final_response,
            outcome.completion_sentinel,
        ) = split_completion_message(outcome.final_message)
        if outcome.completion_sentinel == INFEASIBLE_SENTINEL:
            env.action_history.append("FAIL")
        elif outcome.completion_sentinel == COMPLETE_SENTINEL:
            env.action_history.append("DONE")

        save_phase_screenshot(env, run.result_dir, phase_index, "before-eval")
        official_channel = run_official_evaluator(
            lambda: phase["evaluate"](env)
        )
        official_completed = official_channel["status"] == "completed"
        phase_evaluator_payload = (
            official_channel["result"]
            if official_completed
            else {
                "status": "error",
                "score": None,
                "error": official_channel["error"],
                "traceback": official_channel["traceback"],
                "result_repr": official_channel["result_repr"],
            }
        )
        phase_score = (
            official_channel["result"]["score"]
            if official_completed
            else None
        )
        if not official_completed:
            outcome.official_evaluator_errors.append({
                "phase_index": phase_index,
                "phase_name": phase_name,
                "error": official_channel["error"],
                "traceback": official_channel["traceback"],
                "result_repr": official_channel["result_repr"],
            })
        outcome.evaluator_payload = phase_evaluator_payload
        if phase_score is not None:
            outcome.total_score = (
                0.0 if outcome.total_score is None else outcome.total_score
            ) + phase_score
        save_phase_screenshot(env, run.result_dir, phase_index, "after-eval")

        phase_result = {
            "phase_index": phase_index,
            "phase_name": phase_name,
            "instruction": phase_instruction,
            "score": phase_score,
            "evaluator_status": official_channel["status"],
        }
        phase_detail = {
            **phase_result,
            "evaluator": phase_evaluator_payload,
            "agent": agent_phase,
        }
        outcome.phase_results.append(phase_result)
        outcome.phase_details.append(phase_detail)
        gate_min_score = phase.get("gate_min_score")
        if not official_completed:
            phase_detail["gate_stop"] = "official_evaluator_error"
        elif (
            gate_min_score is not None
            and phase_score < float(gate_min_score)
        ):
            phase_detail["gate_stop"] = "below_gate_min_score"
        if (
            official_completed
            and phase.get("gate")
            and phase_score <= 0.0
        ):
            phase_detail["gate_stop"] = "zero_score_gate"
        if phase_detail.get("gate_stop"):
            break
    return outcome


def save_phase_screenshot(
    env: DesktopEnv,
    result_dir: Path,
    phase_index: int,
    label: str,
) -> None:
    screenshot = env.controller.get_screenshot()
    if screenshot is not None:
        (result_dir / f"phase-{phase_index:02d}-{label}.png").write_bytes(
            screenshot
        )
        (result_dir / f"final-{label}.png").write_bytes(screenshot)


def summarize_usage(run: TaskRun, outcome: PhaseOutcome) -> dict:
    """Summarize the aggregate agent trace and reconcile it with the proxy."""
    aggregate_path = run.result_dir / "agent.jsonl"
    aggregate_trace = (
        aggregate_path.read_bytes() if aggregate_path.is_file() else None
    )
    summary = summarize_agent_trace(aggregate_trace, run.args.agent)
    proxy_usage, frozen_proxy_rows = collect_task_proxy_usage(
        run.token_log,
        run.affinity_task,
    )
    if frozen_proxy_rows:
        proxy_evidence_path = run.result_dir / "task-agent-token-usage.jsonl"
        proxy_evidence_path.write_bytes(frozen_proxy_rows)
        proxy_usage["evidence_file"] = proxy_evidence_path.name
    summary = reconcile_trace_and_proxy_usage(
        summary,
        proxy_usage,
        run.args.agent,
    )
    return normalize_budget_termination(
        summary,
        budget_exhausted=outcome.budget_exhausted,
    )


def write_result(run: TaskRun, status: str, **fields) -> dict:
    """Write ``result.json``, the one authoritative record of a task run.

    Every run that gets this far writes exactly one: ``completed`` or
    ``evaluator_error`` after evaluation, ``error`` when the run failed.
    """
    args = run.args
    result = {
        "task_id": args.task_id,
        "benchmark_version": args.benchmark_version,
        "agent": args.agent,
        "model": args.model,
        "effort": args.effort,
        "status": status,
        "score": None,
        **fields,
        "duration_sec": round(time.monotonic() - run.started, 1),
    }
    (run.result_dir / "result.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False),
        encoding="utf-8",
    )
    run.meta.update({
        "status": status,
        "end_time": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    })
    run.write_meta(ensure_ascii=False)
    return result


def finish_run(
    run: TaskRun,
    env: DesktopEnv,
    base_url: str,
    outcome: PhaseOutcome,
) -> int:
    """Clamp the summed phase scores and write the result."""
    score = None
    errors = outcome.official_evaluator_errors
    if not errors:
        total = outcome.total_score
        if total is None or not math.isfinite(total):
            # Finite phase scores can still overflow when summed.
            errors.append({
                "stage": "aggregation",
                "error": "aggregate score is not a finite number",
                "result_repr": repr(total),
            })
        else:
            score = round(max(0.0, min(1.0, total)), 4)
    done_payload = outcome.done_payload
    result = write_result(
        run,
        "evaluator_error" if errors else "completed",
        score=score,
        termination_reason=(
            "max_model_calls" if outcome.budget_exhausted
            else "timeout" if outcome.truncated
            else "completed"
        ),
        truncated=outcome.truncated,
        truncated_phases=outcome.truncated_phases,
        evaluator_errors=errors,
        final_message=outcome.final_message,
        agent_returncode=done_payload.get("returncode") if done_payload else None,
        phase_delivery=phase_delivery_status(run.phases, outcome.phase_details),
        phases=outcome.phase_details,
        usage=summarize_usage(run, outcome),
    )
    print(json.dumps(
        {"result_dir": str(run.result_dir), "status": result["status"], "score": score},
    ))
    return 0


def record_failure(run: TaskRun, env: DesktopEnv | None, exc: Exception) -> int:
    """Record a fatal run error together with guest diagnostics."""
    raw_error_text = f"{type(exc).__name__}: {exc}"
    failure_classification = classify_turn_failure(raw_error_text)
    if env is not None:
        infra_diagnostic = collect_guest_infra_diagnostic(
            env.setup_controller.http_server
        )
        infra_output = str(infra_diagnostic.pop("output", ""))
        if infra_output:
            (run.result_dir / "fatal-guest-infra.txt").write_text(
                infra_output,
                encoding="utf-8",
            )
        (run.result_dir / "fatal-guest-infra.json").write_text(
            json.dumps(infra_diagnostic, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
    trace = concise_failure_message(traceback.format_exc())
    write_result(
        run,
        "error",
        failure_classification=failure_classification,
        error=concise_failure_message(raw_error_text),
        traceback=trace,
    )
    print(trace, file=sys.stderr)
    return 1
