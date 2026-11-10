#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: smoke-mcp.py /path/to/computer-use", file=sys.stderr)
        return 2
    process = subprocess.Popen(
        [sys.argv[1], "mcp"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
    )
    assert process.stdin is not None
    assert process.stdout is not None
    try:
        for request_id, method, params in (
            (
                1,
                "initialize",
                {
                    "protocolVersion": "2025-06-18",
                    "capabilities": {},
                    "clientInfo": {"name": "hcu-smoke", "version": "1"},
                },
            ),
            (2, "tools/list", {}),
        ):
            process.stdin.write(
                json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": request_id,
                        "method": method,
                        "params": params,
                    }
                )
                + "\n"
            )
            process.stdin.flush()
            while True:
                response = json.loads(process.stdout.readline())
                if response.get("id") == request_id:
                    break
            if "error" in response:
                raise RuntimeError(response["error"])
            if method == "tools/list":
                names = [
                    tool["name"]
                    for tool in response["result"].get("tools", [])
                ]
                if names != ["AX", "GUI"]:
                    raise RuntimeError(f"unexpected public tools: {names}")
                print(json.dumps({"public_tools": names}, sort_keys=True))
    finally:
        process.terminate()
        process.wait(timeout=5)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
