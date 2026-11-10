"""Task-scoped model usage from the gateway token log.

Gateways append one JSON row per model request. Rows carry the task
affinity ID and explicit successful-generation delivery markers. Budget
counts use those markers; accounting retains attempts and reconciles token
usage against the agent trace.
"""

from __future__ import annotations

from dataclasses import dataclass
import json
from pathlib import Path

from hcu.gateway.common import read_log_rows


def collect_task_proxy_usage(
    token_log: Path,
    affinity_task: str,
) -> tuple[dict, bytes]:
    """Separate request classes from delivery steps and token accounting.

    ``model_requests`` counts admitted generation attempts, including failures;
    ``non_generation_requests`` counts auxiliary calls. All explicit usage is
    retained (for example, Responses compaction can report billable tokens).
    Missing usage is a coverage gap for generations, not for token-count calls.
    Unclassified records remain evidence but cannot establish complete coverage.
    """
    summary = {
        "status": "missing",
        "request_attempts": 0,
        "model_requests": 0,
        "non_generation_requests": 0,
        "unclassified_requests": 0,
        "completed_model_responses": 0,
        "budget_rejected_requests": 0,
        "gateway_rejected_requests": 0,
        "accounted_responses": 0,
        "accounted_model_responses": 0,
        "unaccounted_requests": 0,
        "input_tokens": 0,
        "cached_input_tokens": 0,
        "output_tokens": 0,
        "reasoning_tokens": 0,
        "total_tokens": 0,
        "errors": 0,
        "rate_limited_requests": 0,
        "rate_limit_waits": 0,
        "rate_limit_wait_sec": 0.0,
        "endpoints": [],
        "endpoint_affinity_ok": True,
    }
    if not token_log.is_file():
        return summary, b""

    matching_lines = []
    endpoints = set()
    for row in read_log_rows(token_log):
        if row.get("task") != affinity_task:
            continue
        if row.get("rate_limit_wait") is True:
            summary["rate_limit_waits"] += 1
            summary["rate_limit_wait_sec"] = round(
                summary["rate_limit_wait_sec"] + float(row.get("wait_sec") or 0), 3
            )
            continue
        matching_lines.append(
            json.dumps(row, ensure_ascii=False, separators=(",", ":"))
        )
        summary["request_attempts"] += 1
        if row.get("budget_rejected") is True:
            summary["budget_rejected_requests"] += 1
            summary["gateway_rejected_requests"] += 1
            continue
        if row.get("budget_inflight") or (
            row.get("generation_retry_exhausted") and not row.get("generation_admitted")
        ):
            summary["gateway_rejected_requests"] += 1
            continue
        generation = row.get("generation_request")
        if generation is True:
            summary["model_requests"] += 1
            if row.get("model_step_completed") is True:
                summary["completed_model_responses"] += 1
        elif generation is False:
            summary["non_generation_requests"] += 1
        else:
            summary["unclassified_requests"] += 1
        if row.get("endpoint"):
            endpoints.add(str(row["endpoint"]))
        if (
            row.get("error")
            or row.get("generation_error")
            or row.get("accounting_error")
            or row.get("upstream_error")
            or int(row.get("http_status") or 0) >= 400
        ):
            summary["errors"] += 1
        if row.get("rate_limited") or int(row.get("http_status") or 0) == 429:
            summary["rate_limited_requests"] += 1
        if row.get("input_tokens") is None:
            continue
        summary["accounted_responses"] += 1
        if generation is True:
            summary["accounted_model_responses"] += 1
        summary["input_tokens"] += int(row.get("input_tokens") or 0)
        summary["cached_input_tokens"] += int(row.get("cached_tokens") or 0)
        summary["output_tokens"] += int(row.get("output_tokens") or 0)
        summary["reasoning_tokens"] += int(row.get("reasoning_tokens") or 0)
        summary["total_tokens"] += int(row.get("total_tokens") or 0)

    summary["unaccounted_requests"] = (
        summary["model_requests"] - summary["accounted_model_responses"]
    )
    summary["endpoints"] = sorted(endpoints)
    summary["endpoint_affinity_ok"] = len(endpoints) <= 1
    summary["status"] = (
        "incomplete" if summary["unclassified_requests"]
        else "completed" if summary["request_attempts"] else "no_matching_rows"
    )
    frozen_rows = (
        ("\n".join(matching_lines) + "\n").encode("utf-8")
        if matching_lines
        else b""
    )
    return summary, frozen_rows


def reconcile_trace_and_proxy_usage(
    trace_summary: dict,
    proxy_usage: dict,
    agent: str = "codex",
) -> dict:
    trace_usage = {
        "input_tokens": int(trace_summary.get("input_tokens") or 0),
        "cached_input_tokens": int(
            trace_summary.get("cached_input_tokens") or 0
        ),
        "output_tokens": int(trace_summary.get("output_tokens") or 0),
        "reasoning_tokens": int(
            trace_summary.get("reasoning_tokens") or 0
        ),
    }
    trace_summary["trace_usage"] = trace_usage
    trace_summary["proxy_usage"] = proxy_usage
    if int(proxy_usage.get("accounted_responses") or 0) <= 0:
        trace_summary["token_usage_source"] = f"{agent}_trace"
        trace_summary["token_usage_reconciled"] = False
        return trace_summary

    proxy_totals = {
        "input_tokens": int(proxy_usage.get("input_tokens") or 0),
        "cached_input_tokens": int(
            proxy_usage.get("cached_input_tokens") or 0
        ),
        "output_tokens": int(proxy_usage.get("output_tokens") or 0),
        "reasoning_tokens": int(
            proxy_usage.get("reasoning_tokens") or 0
        ),
    }
    trace_summary.update(proxy_totals)
    trace_summary["token_usage_source"] = "proxy_token_log"
    trace_summary["token_usage_reconciled"] = (
        trace_usage != proxy_totals
    )
    return trace_summary


def normalize_budget_termination(
    trace_summary: dict,
    *,
    budget_exhausted: bool,
) -> dict:
    if not budget_exhausted:
        return trace_summary
    original = trace_summary.get("failure_classification")
    if original and original != "model_call_budget_exhausted":
        trace_summary["failure_classification_original"] = original
    trace_summary["failure_classification"] = (
        "model_call_budget_exhausted"
    )
    return trace_summary


@dataclass
class TaskLogState:
    """What the runner needs from the token log while an agent turn runs."""

    completed_responses: int = 0
    budget_rejected: bool = False
    retry_exhausted: bool = False
    rate_limit_waits: int = 0
    rate_limit_wait_sec: float = 0.0


def read_task_log(token_log: Path, affinity_task: str) -> TaskLogState:
    """Read the token log once and summarize this task's rows.

    Only delivered generations count toward the budget, never attempts.
    ``rate_limit_wait`` rows announce gateway waits, which are infrastructure
    time and extend the runner's deadlines.
    """
    state = TaskLogState()
    for row in read_log_rows(token_log):
        if row.get("task") != affinity_task:
            continue
        if row.get("rate_limit_wait") is True:
            state.rate_limit_waits += 1
            state.rate_limit_wait_sec += float(row.get("wait_sec") or 0)
            continue
        state.completed_responses += row.get("model_step_completed") is True
        state.budget_rejected |= row.get("budget_rejected") is True
        state.retry_exhausted |= row.get("generation_retry_exhausted") is True
    return state


def check_gateway_budget(health_output: str, max_model_calls: int) -> None:
    """Fail before the agent starts if the gateway enforces a different cap.

    The gateway owns admission, so a mismatch cannot be repaired later
    without killing an agent while it executes the last response's tools.
    """
    if max_model_calls <= 0:
        return
    try:
        limit = json.loads(health_output).get("max_completed_responses_per_task")
    except (ValueError, AttributeError):
        limit = None
    if limit != max_model_calls:
        raise RuntimeError(
            "Task/gateway successful-response budget mismatch: "
            f"runner={max_model_calls}, gateway={limit!r}. "
            "Start the gateway with --max-requests-per-task equal to max_model_calls."
        )
