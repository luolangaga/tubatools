#!/usr/bin/env python3
"""Adapt MCP tool schemas for Claude Code without changing tool behavior."""

from __future__ import annotations

import json
import subprocess
import sys
import threading


def merge_property_schemas(schemas: list[object]) -> object:
    unique = []
    for schema in schemas:
        if schema not in unique:
            unique.append(schema)
    if len(unique) == 1:
        return unique[0]
    if all(isinstance(schema, dict) and "const" in schema for schema in unique):
        merged = dict(unique[0])
        merged.pop("const", None)
        merged["enum"] = [schema["const"] for schema in unique]
        return merged
    return {"anyOf": unique}


def adapt_input_schema(schema: object) -> object:
    if not isinstance(schema, dict) or "oneOf" not in schema:
        return schema
    branches = schema["oneOf"]
    if not isinstance(branches, list) or not all(
        isinstance(branch, dict) for branch in branches
    ):
        return schema

    adapted = {
        key: value
        for key, value in schema.items()
        if key not in {"oneOf", "allOf", "properties", "required"}
    }
    adapted.setdefault("type", "object")
    property_variants: dict[str, list[object]] = {}
    required_sets = []
    for branch in branches:
        for name, property_schema in (branch.get("properties") or {}).items():
            property_variants.setdefault(name, []).append(property_schema)
        required_sets.append(set(branch.get("required") or []))
    adapted["properties"] = {
        name: merge_property_schemas(variants)
        for name, variants in property_variants.items()
    }
    if required_sets:
        required = sorted(set.intersection(*required_sets))
        if required:
            adapted["required"] = required
    adapted["description"] = (
        str(adapted.get("description") or "").rstrip()
        + "\n\nClaude Code compatibility view: fields are mode-specific. "
        "The MCP server validates the original discriminated union before "
        "performing any action."
    ).strip()
    return adapted


def adapt_message(message: object) -> object:
    if not isinstance(message, dict):
        return message
    result = message.get("result")
    if not isinstance(result, dict) or not isinstance(result.get("tools"), list):
        return message
    adapted = dict(message)
    adapted_result = dict(result)
    adapted_tools = []
    for tool in result["tools"]:
        if not isinstance(tool, dict):
            adapted_tools.append(tool)
            continue
        adapted_tool = dict(tool)
        if "inputSchema" in adapted_tool:
            adapted_tool["inputSchema"] = adapt_input_schema(
                adapted_tool["inputSchema"]
            )
        adapted_tools.append(adapted_tool)
    adapted_result["tools"] = adapted_tools
    adapted["result"] = adapted_result
    return adapted


def copy_stderr(source) -> None:
    for chunk in iter(lambda: source.read(65536), b""):
        sys.stderr.buffer.write(chunk)
        sys.stderr.buffer.flush()


def forward_stdin(source, destination) -> None:
    try:
        for line in source:
            destination.write(line)
            destination.flush()
    except (BrokenPipeError, OSError):
        pass
    finally:
        try:
            destination.close()
        except OSError:
            pass


def main() -> int:
    if len(sys.argv) < 2:
        print(
            "usage: claude_mcp_schema_adapter.py COMMAND [ARG ...]",
            file=sys.stderr,
        )
        return 2
    process = subprocess.Popen(
        sys.argv[1:],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    assert process.stdin is not None
    assert process.stdout is not None
    assert process.stderr is not None
    threading.Thread(
        target=forward_stdin,
        args=(sys.stdin.buffer, process.stdin),
        daemon=True,
    ).start()
    threading.Thread(
        target=copy_stderr,
        args=(process.stderr,),
        daemon=True,
    ).start()

    for line in process.stdout:
        outgoing = line
        try:
            message = json.loads(line)
            outgoing = (
                json.dumps(
                    adapt_message(message),
                    ensure_ascii=False,
                    separators=(",", ":"),
                ).encode("utf-8")
                + b"\n"
            )
        except (json.JSONDecodeError, UnicodeDecodeError):
            pass
        sys.stdout.buffer.write(outgoing)
        sys.stdout.buffer.flush()
    return process.wait()


if __name__ == "__main__":
    raise SystemExit(main())
