"""Task-scoped, accounting passthrough to the Gemini API.

Gemini CLI speaks Google's ``generateContent`` protocol, so it cannot use the
OpenAI or Anthropic gateways. Guest-local Gemini CLI sends
``/t/<task>/v1beta/models/<model>:<method>`` requests; the gateway forwards
them with the operator's API key in ``x-goog-api-key``, records the
``usageMetadata`` of every response in the token log and enforces the
per-task budget. As for Claude Code, the budget counts successful generation
responses that reached the agent; token counting is not budgeted.

Usage::

    OSW_GEMINI_UPSTREAM_TOKEN=<secret> \\
    python -m hcu.gateway.gemini_proxy --host <bridge-ip> --port 8571 \\
        --out runs/gemini-tokens.jsonl
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from hcu.gateway.common import (
    PassthroughGateway,
    TokenLog,
    add_passthrough_arguments,
    check_passthrough_arguments,
    make_passthrough_handler,
    response_payloads,
    serve_passthrough,
)

BUDGETED_METHODS = frozenset({"generateContent", "streamGenerateContent"})
UPSTREAM_ENV = "OSW_GEMINI_UPSTREAM_BASE"
TOKEN_ENV = "OSW_GEMINI_UPSTREAM_TOKEN"
DEFAULT_UPSTREAM = "https://generativelanguage.googleapis.com"


def split_model_method(path: str) -> tuple[str | None, str]:
    """Split ``.../models/<model>:<method>`` into the model and the method."""
    last = path.rsplit("/", 1)[-1]
    model, separator, method = last.partition(":")
    return (model or None, method) if separator else (None, "")


def extract_usage(payload: dict) -> dict:
    """Usage in the token log's convention: input includes cached and
    tool-use prompt tokens, output includes thinking tokens."""
    usage = payload.get("usageMetadata") or {}
    prompt = int(usage.get("promptTokenCount") or 0)
    tool_prompt = int(usage.get("toolUsePromptTokenCount") or 0)
    candidates = int(usage.get("candidatesTokenCount") or 0)
    thoughts = int(usage.get("thoughtsTokenCount") or 0)
    return {
        "input_tokens": prompt + tool_prompt,
        "cached_tokens": int(usage.get("cachedContentTokenCount") or 0),
        "output_tokens": candidates + thoughts,
        "reasoning_tokens": thoughts,
        "total_tokens": int(usage.get("totalTokenCount") or 0),
    }


def parse_sse_usage(raw: bytes) -> tuple[dict | None, str | None]:
    """Return the usage of the last chunk that carries it; chunks report
    cumulative totals."""
    usage = None
    response_id = None
    for line in raw.splitlines():
        if not line.startswith(b"data:"):
            continue
        try:
            event = json.loads(line[5:].strip() or b"null")
        except json.JSONDecodeError:
            continue
        if not isinstance(event, dict):
            continue
        response_id = event.get("responseId") or response_id
        if event.get("usageMetadata"):
            usage = extract_usage(event)
    return usage, response_id


class GeminiGateway(PassthroughGateway):
    name = "gemini_proxy"
    json_array_responses = True
    unforwarded_request_headers = (
        PassthroughGateway.unforwarded_request_headers | {"x-goog-api-key"}
    )

    def is_budgeted_path(self, path: str) -> bool:
        return split_model_method(path)[1] in BUDGETED_METHODS

    def auth_headers(self) -> dict[str, str]:
        return {"x-goog-api-key": self.upstream_token}

    def request_model(self, path: str, body: bytes) -> str | None:
        return split_model_method(path)[0]

    def json_usage(self, payload: dict) -> tuple[dict | None, str | None]:
        return (
            extract_usage(payload) if payload.get("usageMetadata") else None,
            payload.get("responseId"),
        )

    def sse_usage(self, raw: bytes) -> tuple[dict | None, str | None]:
        return parse_sse_usage(raw)

    def error_body(self, status: int, message: str) -> dict:
        return {"error": {
            "code": status,
            "status": "RESOURCE_EXHAUSTED" if status == 429 else "UNAVAILABLE",
            "message": message,
        }}

    def generation_succeeded(self, body: bytes, headers) -> bool:
        events = response_payloads(body, headers, allow_json_array=self.json_array_responses)
        if any(event.get("error") for event in events):
            return False
        return any(
            event.get("promptFeedback", {}).get("blockReason")
            or any(candidate.get("finishReason") for candidate in event.get("candidates", []))
            for event in events
        )


make_handler = make_passthrough_handler


def main() -> int:
    parser = argparse.ArgumentParser()
    add_passthrough_arguments(
        parser, port=8571, upstream_env=UPSTREAM_ENV, token_env=TOKEN_ENV,
        upstream_default=DEFAULT_UPSTREAM,
    )
    args = parser.parse_args()
    check_passthrough_arguments(
        parser, args, upstream_env=UPSTREAM_ENV, token_env=TOKEN_ENV
    )
    gateway = GeminiGateway(
        upstream_base=args.upstream_base,
        upstream_token=args.upstream_token,
        token_log=TokenLog(Path(args.out)),
        max_requests_per_task=args.max_requests_per_task,
        rate_limit_max_wait=args.rate_limit_max_wait,
    )
    return serve_passthrough(gateway, args.host, args.port)


if __name__ == "__main__":
    raise SystemExit(main())
