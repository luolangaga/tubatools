#!/usr/bin/env python3
"""Compare the client-visible bytes of two computer-use binaries.

Usage: compare_mcp_bytes.py BASELINE_BINARY CANDIDATE_BINARY

For each binary, the script sends MCP `initialize` and `tools/list` over
stdio and records the raw response lines, then records the stdout of the
`help` and `schema` commands. It exits 1 and prints the first differing
offset when any output differs. Each binary runs with the same minimal
environment (each COMPUTER_USE_CHANNELS setting the runners use), so
differences come from the binaries alone. No tool is called, so no desktop
or model is needed.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys

REQUESTS = (
    (
        1,
        "initialize",
        {
            "protocolVersion": "2025-06-18",
            "capabilities": {},
            "clientInfo": {"name": "hcu-maint-compare", "version": "1"},
        },
    ),
    (2, "tools/list", {}),
)
CHANNELS = ("", "a11y,keyboard", "gui,keyboard")


def environment(channels: str) -> dict[str, str]:
    env = {"PATH": "/usr/bin:/bin", "HOME": os.environ.get("HOME", "/tmp")}
    if channels:
        env["COMPUTER_USE_CHANNELS"] = channels
    return env


def mcp_bytes(binary: str, channels: str) -> list[tuple[str, bytes]]:
    process = subprocess.Popen(
        [binary, "mcp"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        env=environment(channels),
    )
    assert process.stdin is not None and process.stdout is not None
    outputs = []
    try:
        for request_id, method, params in REQUESTS:
            line = {"jsonrpc": "2.0", "id": request_id, "method": method, "params": params}
            process.stdin.write(json.dumps(line).encode() + b"\n")
            process.stdin.flush()
            while True:
                raw = process.stdout.readline()
                if not raw:
                    raise RuntimeError(f"{binary}: EOF before {method} response")
                if json.loads(raw).get("id") == request_id:
                    outputs.append((f"mcp {method} channels={channels!r}", raw))
                    break
    finally:
        process.terminate()
        process.wait(timeout=5)
    return outputs


def command_bytes(binary: str, args: list[str]) -> tuple[str, bytes]:
    result = subprocess.run(
        [binary, *args],
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        env=environment(""),
        timeout=30,
        check=False,
    )
    return (f"{' '.join(args)} (exit {result.returncode})", result.stdout)


def collect(binary: str) -> list[tuple[str, bytes]]:
    outputs = []
    for channels in CHANNELS:
        outputs.extend(mcp_bytes(binary, channels))
    outputs.append(command_bytes(binary, ["help"]))
    outputs.append(command_bytes(binary, ["schema"]))
    return outputs


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    baseline, candidate = collect(sys.argv[1]), collect(sys.argv[2])
    failed = False
    for (label, want), (other_label, got) in zip(baseline, candidate):
        if label != other_label:
            print(f"DIFF {label}: exit status or label differs: {other_label}")
            failed = True
            continue
        if want == got:
            print(f"same {label}: {len(want)} bytes")
            continue
        failed = True
        offset = next(
            (i for i, (a, b) in enumerate(zip(want, got)) if a != b),
            min(len(want), len(got)),
        )
        print(f"DIFF {label}: {len(want)} vs {len(got)} bytes, first difference at byte {offset}")
        print(f"  baseline:  {want[max(0, offset - 60):offset + 60]!r}")
        print(f"  candidate: {got[max(0, offset - 60):offset + 60]!r}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
