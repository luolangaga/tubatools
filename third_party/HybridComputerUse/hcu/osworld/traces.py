"""Summaries of the task agents' JSONL traces and of failure messages."""

from __future__ import annotations

import json
import re

from hcu.osworld.agents import GEMINI_MCP_SERVER
from hcu.osworld.prompts import split_completion_message


FAILURE_SUMMARY_MAX_CHARS = 4096
# Codex reports "rate limit exceeded", Azure "exceeded token rate limit",
# Gemini "RESOURCE_EXHAUSTED"; all are infrastructure, not agent failures.
RATE_LIMIT_PATTERN = re.compile(
    r"rate[ _-]?limit|too many requests|resource_exhausted"
)
# Classifications that describe the model service, not the agent. The queue
# re-runs such tasks instead of recording a failure.
INFRASTRUCTURE_FAILURES = frozenset({"upstream_rate_limit"})


def empty_trace_summary() -> dict:
    return {
        "events": 0,
        "mcp_calls": 0,
        "shell_calls": 0,
        "tool_breakdown": {},
        "input_tokens": 0,
        "cached_input_tokens": 0,
        "output_tokens": 0,
        "reasoning_tokens": 0,
        "last_agent_message": "",
        "last_substantive_message": "",
        "completion_sentinel": None,
        "agent_error": None,
        "agent_error_original_chars": 0,
        "failure_classification": None,
    }


def record_agent_message(summary: dict, message: str) -> None:
    """Remember the latest agent message and its completion declaration."""
    summary["last_agent_message"] = message
    body, sentinel = split_completion_message(message)
    if body:
        summary["last_substantive_message"] = body
    if sentinel is not None:
        summary["completion_sentinel"] = sentinel


def record_agent_error(summary: dict, message: str) -> None:
    summary["agent_error_original_chars"] = len(message)
    summary["failure_classification"] = classify_turn_failure(message)
    summary["agent_error"] = concise_failure_message(message)


def summarize_codex_trace(raw: bytes | None) -> dict:
    summary = empty_trace_summary()
    if not raw:
        return summary

    for line in raw.decode("utf-8", errors="replace").splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        summary["events"] += 1
        if event.get("type") == "turn.failed":
            failure = event.get("error") or {}
            if isinstance(failure, dict):
                message = str(
                    failure.get("message")
                    or failure.get("code")
                    or failure
                )
            else:
                message = str(failure)
            record_agent_error(summary, message or "turn failed")
        if event.get("type") == "turn.completed":
            usage = event.get("usage") or {}
            summary["input_tokens"] += int(usage.get("input_tokens") or 0)
            summary["cached_input_tokens"] += int(
                usage.get("cached_input_tokens") or 0
            )
            summary["output_tokens"] += int(usage.get("output_tokens") or 0)
            summary["reasoning_tokens"] += int(
                usage.get("reasoning_output_tokens") or 0
            )
        if event.get("type") != "item.completed":
            continue
        item = event.get("item") or {}
        item_type = item.get("type")
        if item_type == "agent_message":
            record_agent_message(summary, str(item.get("text") or ""))
        elif item_type == "mcp_tool_call":
            summary["mcp_calls"] += 1
            name = str(item.get("tool") or item.get("name") or "unknown")
            summary["tool_breakdown"][name] = (
                summary["tool_breakdown"].get(name, 0) + 1
            )
        elif item_type == "command_execution":
            summary["shell_calls"] += 1
    return summary


def summarize_claude_trace(raw: bytes | None) -> dict:
    summary = empty_trace_summary()
    if not raw:
        return summary

    shell_tools = {"Bash", "BashOutput", "KillShell"}
    for line in raw.decode("utf-8", errors="replace").splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        summary["events"] += 1
        if event.get("type") == "assistant":
            for block in (event.get("message") or {}).get("content", []) or []:
                block_type = block.get("type")
                if block_type == "thinking":
                    summary["reasoning_tokens"] += int(
                        (block.get("usage") or {}).get("thinking_tokens") or 0
                    )
                elif block_type == "text":
                    message = str(block.get("text") or "")
                    if message.strip():
                        record_agent_message(summary, message)
                elif block_type == "tool_use":
                    name = str(block.get("name") or "unknown")
                    if name in shell_tools:
                        summary["shell_calls"] += 1
                    elif name.startswith("mcp__"):
                        summary["mcp_calls"] += 1
                        short_name = name.rsplit("__", 1)[-1]
                        summary["tool_breakdown"][short_name] = (
                            summary["tool_breakdown"].get(short_name, 0) + 1
                        )
        elif event.get("type") == "result":
            usage = event.get("usage") or {}
            summary["input_tokens"] = int(usage.get("input_tokens") or 0)
            summary["cached_input_tokens"] = int(
                usage.get("cache_read_input_tokens") or 0
            )
            summary["output_tokens"] = int(usage.get("output_tokens") or 0)
            summary["reasoning_tokens"] = max(
                summary["reasoning_tokens"],
                int(
                    (usage.get("output_tokens_details") or {}).get(
                        "thinking_tokens"
                    )
                    or 0
                ),
            )
            message = str(event.get("result") or "")
            if message.strip():
                record_agent_message(summary, message)
            if event.get("is_error"):
                record_agent_error(
                    summary,
                    message or str(event.get("error") or "Claude Code failed"),
                )
    return summary


def count_tool_call(summary: dict, name: str, mcp_prefix: str, shell_tool: str) -> None:
    if name.startswith(mcp_prefix):
        summary["mcp_calls"] += 1
        short_name = name[len(mcp_prefix):]
        summary["tool_breakdown"][short_name] = (
            summary["tool_breakdown"].get(short_name, 0) + 1
        )
    elif name == shell_tool:
        summary["shell_calls"] += 1


def summarize_gemini_trace(raw: bytes | None) -> dict:
    """Gemini CLI stream-json: message deltas, tool_use rows, one result."""
    summary = empty_trace_summary()
    if not raw:
        return summary
    text = ""
    for line in raw.decode("utf-8", errors="replace").splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        summary["events"] += 1
        kind = event.get("type")
        if kind == "message" and event.get("role") == "assistant":
            chunk = str(event.get("content") or "")
            text = text + chunk if event.get("delta") else chunk
        elif kind == "tool_use":
            if text.strip():
                record_agent_message(summary, text)
            text = ""
            name = str(event.get("tool_name") or "")
            # Historical runs used an underscore in the Gemini server key.
            prefix = ("mcp_computer_use_" if name.startswith("mcp_computer_use_")
                      else f"mcp_{GEMINI_MCP_SERVER}_")
            count_tool_call(
                summary,
                name,
                prefix,
                "run_shell_command",
            )
        elif kind == "result":
            # input_tokens already includes the cached prompt tokens.
            stats = event.get("stats") or {}
            summary["input_tokens"] = int(stats.get("input_tokens") or 0)
            summary["cached_input_tokens"] = int(stats.get("cached") or 0)
            summary["output_tokens"] = int(stats.get("output_tokens") or 0)
            if event.get("status") != "success":
                record_agent_error(
                    summary,
                    str(event.get("error") or event.get("status") or "failed"),
                )
        elif kind == "error":
            record_agent_error(summary, str(event.get("message") or "error"))
    if text.strip():
        record_agent_message(summary, text)
    return summary


def summarize_grok_trace(raw: bytes | None) -> dict:
    """Grok Build streaming-json: text chunks, tool_call rows, one end row."""
    summary = empty_trace_summary()
    if not raw:
        return summary
    text = ""
    for line in raw.decode("utf-8", errors="replace").splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        summary["events"] += 1
        kind = event.get("type")
        if kind == "text" and isinstance(event.get("data"), str):
            text += event["data"]
        elif kind == "tool_call":
            if text.strip():
                record_agent_message(summary, text)
            text = ""
            name = str(event.get("toolName") or "")
            arguments = event.get("rawInput")
            # MCP tools are reached through Grok's use_tool dispatcher.
            if name == "use_tool" and isinstance(arguments, dict):
                name = str(arguments.get("tool_name") or name)
            count_tool_call(
                summary, name, "computer_use__", "run_terminal_command"
            )
        elif kind == "end":
            # Grok reports cache reads apart from input_tokens, and the
            # summary counts cumulative input with cached history included.
            usage = event.get("usage") or {}
            cached = int(usage.get("cache_read_input_tokens") or 0)
            summary["input_tokens"] = (
                int(usage.get("input_tokens") or 0)
                + cached
                + int(usage.get("cache_creation_input_tokens") or 0)
            )
            summary["cached_input_tokens"] = cached
            summary["output_tokens"] = int(usage.get("output_tokens") or 0)
            summary["reasoning_tokens"] = int(usage.get("reasoning_tokens") or 0)
            if event.get("stopReason") != "end_turn":
                record_agent_error(
                    summary, str(event.get("stopReason") or "no stop reason")
                )
        elif kind == "error":
            record_agent_error(summary, str(event.get("message") or "error"))
    if text.strip():
        record_agent_message(summary, text)
    return summary


TRACE_SUMMARIZERS = {
    "codex": summarize_codex_trace,
    "claude-code": summarize_claude_trace,
    "gemini-cli": summarize_gemini_trace,
    "grok-build": summarize_grok_trace,
}


def summarize_agent_trace(raw: bytes | None, agent: str) -> dict:
    return TRACE_SUMMARIZERS[agent](raw)


def classify_turn_failure(message: str) -> str:
    normalized = (message or "").casefold()
    if RATE_LIMIT_PATTERN.search(normalized):
        return "upstream_rate_limit"
    if (
        "tool_argument_too_large" in normalized
        or "argument list too long" in normalized
        or "os error 7" in normalized
    ):
        return "tool_argument_too_large"
    if "no space left on device" in normalized or "os error 28" in normalized:
        return "disk_full"
    if "out of memory" in normalized or "oom-kill" in normalized:
        return "guest_oom"
    return "unclassified_turn_failure"


def concise_failure_message(message: str) -> str:
    message = message or ""
    classification = classify_turn_failure(message)
    if classification == "tool_argument_too_large":
        size_match = re.search(
            r"largest argv element is (\d+) bytes and total argv size is "
            r"(\d+) bytes",
            message,
            flags=re.IGNORECASE,
        )
        sizes = (
            f" largest_argv_bytes={size_match.group(1)}"
            f" total_argv_bytes={size_match.group(2)}"
            if size_match
            else ""
        )
        return (
            "tool_argument_too_large: command was rejected before execution; "
            "use an existing file or stream data through stdin/write_stdin."
            f"{sizes} original_chars={len(message)}"
        )
    if len(message) <= FAILURE_SUMMARY_MAX_CHARS:
        return message
    prefix = message[:FAILURE_SUMMARY_MAX_CHARS]
    return (
        f"{prefix}\n[error summary truncated: original_chars={len(message)}]"
    )
