"""Task-scoped, accounting passthrough to an Anthropic-compatible API.

Guest-local Claude Code sends ``/t/<task>/v1/messages`` requests; the gateway
forwards them with the operator's upstream credential, records usage in the
token log and enforces the per-task budget.

Usage::

    OSW_ANTHROPIC_UPSTREAM_BASE=https://<host> \\
    OSW_ANTHROPIC_UPSTREAM_TOKEN=<secret> \\
    python -m hcu.gateway.anthropic_proxy --host <bridge-ip> --port 8537 \\
        --out runs/anthropic-tokens.jsonl
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

BUDGETED_PATH = "/v1/messages"
UPSTREAM_ENV = "OSW_ANTHROPIC_UPSTREAM_BASE"
TOKEN_ENV = "OSW_ANTHROPIC_UPSTREAM_TOKEN"


def extract_usage(payload: dict) -> dict:
    usage = payload.get("usage") or {}
    input_tokens = int(usage.get("input_tokens") or 0)
    cache_read = int(usage.get("cache_read_input_tokens") or 0)
    cache_creation = int(usage.get("cache_creation_input_tokens") or 0)
    output_tokens = int(usage.get("output_tokens") or 0)
    return {
        "input_tokens": input_tokens,
        "cached_tokens": cache_read,
        "cache_creation_input_tokens": cache_creation,
        "output_tokens": output_tokens,
        "reasoning_tokens": int(
            (usage.get("output_tokens_details") or {}).get("thinking_tokens")
            or 0
        ),
        "total_tokens": (
            input_tokens + cache_creation + cache_read + output_tokens
        ),
    }


def parse_sse_usage(raw: bytes) -> tuple[dict | None, str | None]:
    """Return the maximum streamed usage per field and the message ID."""
    totals = None
    response_id = None
    for line in raw.splitlines():
        if not line.startswith(b"data:"):
            continue
        chunk = line[5:].strip()
        if not chunk or chunk == b"[DONE]":
            continue
        try:
            event = json.loads(chunk)
        except json.JSONDecodeError:
            continue
        message = event.get("message")
        if isinstance(message, dict):
            response_id = str(message.get("id") or response_id or "") or None
            if message.get("usage"):
                totals = extract_usage(message)
        if event.get("usage"):
            current = extract_usage(event)
            if totals is None:
                totals = current
            else:
                for key, value in current.items():
                    totals[key] = max(int(totals.get(key) or 0), int(value or 0))
    return totals, response_id


class AnthropicGateway(PassthroughGateway):
    name = "anthropic_proxy"
    unforwarded_request_headers = (
        PassthroughGateway.unforwarded_request_headers | {"x-api-key"}
    )

    def is_budgeted_path(self, path: str) -> bool:
        return path == BUDGETED_PATH

    def auth_headers(self) -> dict[str, str]:
        return {
            "authorization": f"Bearer {self.upstream_token}",
            "x-api-key": self.upstream_token,
        }

    def request_model(self, path: str, body: bytes) -> str | None:
        if body[:1] == b"{":
            try:
                return json.loads(body).get("model")
            except json.JSONDecodeError:
                pass
        return None

    def json_usage(self, payload: dict) -> tuple[dict | None, str | None]:
        return (
            extract_usage(payload) if payload.get("usage") else None,
            payload.get("id"),
        )

    def sse_usage(self, raw: bytes) -> tuple[dict | None, str | None]:
        return parse_sse_usage(raw)

    def error_body(self, status: int, message: str) -> dict:
        return {"type": "error", "error": {"type": "api_error", "message": message}}

    def generation_succeeded(self, body: bytes, headers) -> bool:
        events = response_payloads(body, headers)
        if any(event.get("type") == "error" or event.get("error") for event in events):
            return False
        if "text/event-stream" in headers.get("content-type", ""):
            return any(event.get("type") == "message_stop" for event in events)
        return bool(events) and events[-1].get("type") == "message" and bool(
            events[-1].get("stop_reason")
        )


make_handler = make_passthrough_handler


def main() -> int:
    parser = argparse.ArgumentParser()
    add_passthrough_arguments(
        parser, port=8537, upstream_env=UPSTREAM_ENV, token_env=TOKEN_ENV
    )
    args = parser.parse_args()
    check_passthrough_arguments(
        parser, args, upstream_env=UPSTREAM_ENV, token_env=TOKEN_ENV
    )
    gateway = AnthropicGateway(
        upstream_base=args.upstream_base,
        upstream_token=args.upstream_token,
        token_log=TokenLog(Path(args.out)),
        max_requests_per_task=args.max_requests_per_task,
        rate_limit_max_wait=args.rate_limit_max_wait,
    )
    return serve_passthrough(gateway, args.host, args.port)


if __name__ == "__main__":
    raise SystemExit(main())
