"""Install the task-agent runtime into the guest and verify the guest.

The runtime (agent binary, MCP binary, launcher, configuration and prompts)
is uploaded before task setup so that setup cannot observe a missing agent.
The guest clock is aligned to the run-bound task date before setup as well.
"""

from __future__ import annotations

import datetime
import json
import os
import shlex

from hcu.osworld.agents import GEMINI_MCP_SERVER
from hcu.osworld.guest import (
    GUEST_CLAUDE_CONFIG_DIR,
    GUEST_CODEX,
    GUEST_CODEX_HOME,
    GUEST_GEMINI_HOME,
    GUEST_GROK_HOME,
    GUEST_LAUNCHER,
    GUEST_MCP,
    GUEST_MCP_RUNTIME_ROOT,
    request_execute,
)


def validate_guest_current_date(
    *,
    expected_date: str,
    command_output: str,
) -> str:
    observed_date = str(command_output or "").strip()
    try:
        datetime.date.fromisoformat(expected_date)
        datetime.date.fromisoformat(observed_date)
    except ValueError as exc:
        raise RuntimeError(
            "Guest current-date preflight returned an invalid ISO date: "
            f"expected={expected_date!r}, observed={observed_date!r}"
        ) from exc
    if observed_date != expected_date:
        raise RuntimeError(
            "Guest current date does not match the immutable run-bound date: "
            f"expected={expected_date}, observed={observed_date}. "
            "The runner must align the guest to the run timezone before task "
            "setup."
        )
    return observed_date


def align_guest_run_clock(
    setup_controller,
    *,
    expected_date: str,
    timezone_name: str,
) -> None:
    datetime.date.fromisoformat(expected_date)
    normalized_timezone = str(timezone_name or "").strip()
    if not normalized_timezone:
        raise ValueError("Run timezone must not be empty")
    setup_controller.execute(
        [
            "bash",
            "-lc",
            (
                "set -e; "
                "timedatectl_retry() { "
                "for attempt in 1 2 3 4 5 6; do "
                "output=$(printf '%s\\n' '{CLIENT_PASSWORD}' | "
                "sudo -S timedatectl \"$@\" 2>&1) && return 0; "
                "rc=$?; "
                "case \"$output\" in "
                "*'Previous request is not finished'*) sleep 2 ;; "
                "*) printf '%s\\n' \"$output\" >&2; return \"$rc\" ;; "
                "esac; "
                "done; "
                "printf '%s\\n' \"$output\" >&2; return \"$rc\"; "
                "}; "
                "timedatectl_retry set-timezone "
                f"{shlex.quote(normalized_timezone)}; "
                "timedatectl_retry set-ntp false; "
                "timedatectl_retry set-time "
                f"{shlex.quote(expected_date + ' 12:00:00')}"
            ),
        ],
        timeout=60,
    )


def runtime_bootstrap_command(client_password: str) -> list[str]:
    runtime_dirs = (
        GUEST_CODEX_HOME,
        GUEST_CLAUDE_CONFIG_DIR,
        GUEST_GEMINI_HOME,
        GUEST_GROK_HOME,
        GUEST_MCP_RUNTIME_ROOT,
    )
    quoted_dirs = " ".join(shlex.quote(path) for path in runtime_dirs)
    return [
        "bash",
        "-lc",
        f"printf '%s\\n' {shlex.quote(client_password)} "
        "| sudo -S sh -c "
        + shlex.quote(
            # .mcp.json ships in the guest images and registers MCP servers
            # that are not part of this runner; a client that reads it would
            # start them alongside the task MCP.
            "rm -rf /home/user/.codex /home/user/.claude "
            "/home/user/.claude.json /home/user/.gemini /home/user/.grok "
            "/home/user/.mcp.json "
            "/home/user/AGENTS.md /home/user/CLAUDE.md /home/user/GEMINI.md"
        )
        + " "
        f"&& printf '%s\\n' {shlex.quote(client_password)} "
        f"| sudo -S install -d -o user -g user {quoted_dirs} "
        f"&& test -w {shlex.quote(GUEST_CODEX_HOME)} "
        f"&& test -w {shlex.quote(GUEST_CLAUDE_CONFIG_DIR)}",
    ]


def validate_provider_mount_isolation(container, guest_image: str) -> list[dict]:
    container.reload()
    mounts = list(container.attrs.get("Mounts") or [])
    expected_source = os.path.realpath(guest_image)
    bind_mounts = [
        mount for mount in mounts if mount.get("Type") == "bind"
    ]
    if len(bind_mounts) != 1:
        raise RuntimeError(
            "OSWorld V2 runtime must have exactly one host bind mount: "
            + json.dumps(mounts, sort_keys=True)
        )
    guest_mount = bind_mounts[0]
    valid_guest_mount = (
        os.path.realpath(str(guest_mount.get("Source") or ""))
        == expected_source
        and guest_mount.get("Destination") == "/System.qcow2"
        and not bool(guest_mount.get("RW"))
    )
    if not valid_guest_mount:
        raise RuntimeError(
            "OSWorld V2 runtime exposes an unexpected host mount: "
            + json.dumps(guest_mount, sort_keys=True)
        )
    unexpected_mounts = [
        mount
        for mount in mounts
        if mount is not guest_mount
        and not (
            mount.get("Type") == "volume"
            and mount.get("Destination") == "/storage"
        )
    ]
    if unexpected_mounts:
        raise RuntimeError(
            "OSWorld V2 runtime exposes an unexpected additional mount: "
            + json.dumps(unexpected_mounts, sort_keys=True)
        )
    return mounts


def install_guest_runtime(
    setup_controller,
    *,
    client_password: str,
    upload_rows: list[dict[str, str]],
    executable_paths: tuple[str, ...] = (
        GUEST_CODEX,
        GUEST_MCP,
        GUEST_LAUNCHER,
    ),
    unpack: dict[str, str] | None = None,
) -> None:
    """Upload the agent runtime; ``unpack`` maps uploaded npm tarballs to the
    directories their ``package/`` tree is extracted into."""
    base_url = setup_controller.http_server
    request_execute(
        base_url,
        runtime_bootstrap_command(client_password),
    )
    setup_controller._upload_file_setup(upload_rows)
    for archive, target in (unpack or {}).items():
        request_execute(
            base_url,
            [
                "bash",
                "-lc",
                f"mkdir -p {shlex.quote(target)} "
                f"&& tar -xzf {shlex.quote(archive)} -C {shlex.quote(target)} "
                "--strip-components=1 "
                f"&& rm -f {shlex.quote(archive)}",
            ],
            timeout=300,
        )
    request_execute(
        base_url,
        [
            "chmod",
            "0755",
            *executable_paths,
        ],
    )


def public_mcp_probe_config(config: str, agent: str, expected_tools: list[str]) -> dict:
    """Read the transport from the rendered client config, including adapters."""
    if agent in {"codex", "grok-build"}:
        try:
            import tomllib
        except ImportError:  # Python 3.10
            import tomli as tomllib
        server = tomllib.loads(config)["mcp_servers"]["computer_use"]
    elif agent in {"claude-code", "gemini-cli"}:
        key = GEMINI_MCP_SERVER if agent == "gemini-cli" else "computer_use"
        server = json.loads(config)["mcpServers"][key]
    else:
        raise ValueError(f"Unsupported MCP probe client: {agent}")
    if server.get("enabled") is False:
        if expected_tools:
            raise ValueError("MCP disabled but public tools are expected")
        return {"enabled": False, "expected_tools": []}
    command, args, env = server.get("command"), server.get("args", []), server.get("env", {})
    if not isinstance(command, str) or not command:
        raise ValueError("MCP configuration has no stdio command")
    if not isinstance(args, list) or not all(isinstance(x, str) for x in args):
        raise ValueError("MCP args must be strings")
    if not isinstance(env, dict) or not all(isinstance(k, str) and isinstance(v, str) for k, v in env.items()):
        raise ValueError("MCP env must contain string values")
    # Codex 0.146.0's initialize request uses 2026-07-28. Other clients are
    # checked at the server's supported structured-output protocol boundary.
    return {
        "command": command, "args": args, "env": env,
        "expected_tools": expected_tools,
        "protocol": "2026-07-28" if agent == "codex" else "2025-06-18",
        "cwd": "/home/user",
        "client_env": {
            "codex": {"CODEX_HOME": GUEST_CODEX_HOME, "CODEX_SQLITE_HOME": GUEST_CODEX_HOME},
            "claude-code": {"CLAUDE_CONFIG_DIR": GUEST_CLAUDE_CONFIG_DIR},
            "gemini-cli": {"GEMINI_CLI_HOME": GUEST_GEMINI_HOME},
            "grok-build": {"GROK_HOME": GUEST_GROK_HOME},
        }[agent],
    }


def public_mcp_probe_script() -> str:
    return r'''
import json
import os
import re
import signal
import subprocess
import sys
import tempfile

config = json.loads(sys.argv[1])
if config.get("enabled") is False:
    print(json.dumps({"public_tools": [], "skipped": "MCP disabled"}))
    sys.exit(0)

# The outer HTTP timeout must not leave a probe blocked on a silent child.
def timeout(signum, frame):
    raise TimeoutError("MCP public probe exceeded 90 seconds")
signal.signal(signal.SIGALRM, timeout)
signal.alarm(90)
stderr = tempfile.TemporaryFile(mode="w+t")
child_env = dict(os.environ, **config.get("client_env", {}))
child_env.update(config.get("env", {}))

process = subprocess.Popen(
    [config["command"], *config.get("args", [])],
    env=child_env,
    cwd=config.get("cwd"),
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=stderr,
    text=True,
    bufsize=1,
)
next_id = 1


def request(method, params):
    global next_id
    request_id = next_id
    next_id += 1
    process.stdin.write(json.dumps({
        "jsonrpc": "2.0",
        "id": request_id,
        "method": method,
        "params": params,
    }) + "\n")
    process.stdin.flush()
    while True:
        line = process.stdout.readline()
        if not line:
            stderr.seek(0)
            raise RuntimeError(
                "MCP exited during public-surface probe: "
                + stderr.read()[-2000:]
            )
        message = json.loads(line)
        if message.get("id") != request_id:
            continue
        if "error" in message:
            raise RuntimeError(str(message["error"]))
        result = message.get("result")
        if not isinstance(result, dict):
            raise RuntimeError("MCP response has no result object")
        return result


def call(name, arguments):
    result = request("tools/call", {"name": name, "arguments": arguments})
    if result.get("isError") or result.get("is_error") or result.get("error"):
        raise RuntimeError(name + " tool failed: " + str(result)[:2000])
    content = result.get("content")
    if not isinstance(content, list) or not content:
        raise RuntimeError(name + " returned no content")
    text = []
    metadata = [result.get("structuredContent"), result.get("structured_content")]
    for item in content:
        if item.get("type") == "text":
            value = item.get("text", "")
            text.append(value)
            try:
                metadata.append(json.loads(value))
            except (ValueError, TypeError):
                pass
    for value in metadata:
        if isinstance(value, dict) and (value.get("ok") is False or value.get("error")):
            raise RuntimeError(name + " reported failure: " + str(value)[:2000])
    return "\n".join(text), content


try:
    initialized = request(
        "initialize",
        {
            "protocolVersion": config["protocol"],
            "capabilities": {},
            "clientInfo": {
                "name": "osworld-v2-harness-preflight",
                "version": "1",
            },
        },
    )
    # Read after initialize, when exec and the configured transport are ready.
    with open("/proc/%s/environ" % process.pid, "rb") as handle:
        actual_env = dict(item.split(b"=", 1) for item in handle.read().split(b"\0") if b"=" in item)
    if any(actual_env.get(key.encode()) != value.encode() for key, value in config.get("env", {}).items()):
        raise RuntimeError("MCP child environment differs from client configuration")
    negotiated = initialized.get("protocolVersion")
    if not isinstance(negotiated, str) or negotiated < "2025-06-18":
        raise RuntimeError("MCP negotiated a legacy protocol; output contract was not checked")
    process.stdin.write(json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized"}) + "\n")
    process.stdin.flush()
    tools = request("tools/list", {}).get("tools") or []
    names = [tool.get("name") for tool in tools]
    expected = config["expected_tools"]
    if names != expected:
        raise RuntimeError("unexpected public MCP tools: " + repr(names))
    report = {"public_tools": names, "requested_protocol": config["protocol"],
              "negotiated_protocol": negotiated, "child_env_verified": True, "checks": {}}
    if "AX" in names:
        apps, _ = call("AX", {"mode": "apps"})
        windows = re.findall(r"^(w[1-9][0-9]*)\s+.*$", apps, re.M)
        if not windows and "No top-level windows are visible to the accessibility provider." not in apps and "app=" not in apps and "not input targets:" not in apps:
            raise RuntimeError("AX apps returned no window inventory evidence")
        report["checks"]["apps"] = "passed"
        required_title = config.get("required_window_title")
        if required_title:
            matches = [line.split()[0] for line in apps.splitlines()
                       if re.match(r"^w[1-9][0-9]*\s", line) and required_title in line]
            if len(matches) != 1:
                raise RuntimeError("AX apps did not return one canary fixture window")
            target = {"window_ref": matches[0]}
        else:
            target = {"window_ref": windows[0]} if windows else None
        if target:
            tree, _ = call("AX", dict(mode="observe", observe="tree", **target))
            if not re.search(r"^ax:\s*$", tree, re.M) or not re.search(r"^window:", tree, re.M):
                raise RuntimeError("AX observe returned no window tree evidence")
            report["checks"]["observe"] = "passed"
            query = config.get("required_query")
            if not query:
                node = re.search(r'^\s*- ([\w-]+)(?: ("(?:[^"\\]|\\.)*"))?[^\n]*\[ref=r[1-9][0-9]*\]', tree, re.M)
                if node:
                    query = {"role": node.group(1)}
                    if node.group(2) and json.loads(node.group(2)).strip():
                        query["name_equals"] = json.loads(node.group(2))
            if query:
                found, _ = call("AX", dict(mode="find", query=query, **target))
                if not re.search(r"^matches: [1-9][0-9]*\b", found, re.M) or not re.search(r"\br[1-9][0-9]*\b", found):
                    raise RuntimeError("AX find did not return the observed control and its ref")
                report["checks"]["find"] = "passed"
            else:
                report["checks"]["find"] = "skipped: no addressable controls"
        else:
            report["checks"].update(observe="skipped: no addressable windows", find="skipped: no addressable windows")
    if "GUI" in names:
        _, content = call("GUI", {"actions": [{"type": "screenshot"}]})
        if not any(item.get("type") == "image" and item.get("data") for item in content):
            raise RuntimeError("GUI returned no screenshot")
        report["checks"]["screenshot"] = "passed"
    print(json.dumps(report, sort_keys=True))
finally:
    signal.alarm(0)
    process.terminate()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=5)
    stderr.close()
'''
