"""Guest launcher script that runs one agent turn and records how it ended."""

from __future__ import annotations

import shlex

from hcu.osworld.agents import CLAUDE_BUILTIN_TOOLS
from hcu.osworld.guest import (
    GUEST_BASE_INSTRUCTIONS,
    GUEST_CLAUDE_CODE,
    GUEST_CLAUDE_CONFIG_DIR,
    GUEST_CLAUDE_MCP_CONFIG,
    GUEST_CODEX,
    GUEST_CODEX_HOME,
    GUEST_DONE,
    GUEST_FINAL,
    GUEST_GEMINI_CLI,
    GUEST_GEMINI_EXPERIMENTS,
    GUEST_GEMINI_HOME,
    GUEST_GEMINI_NODE,
    GUEST_GEMINI_TRUSTED,
    GUEST_GROK,
    GUEST_GROK_HOME,
    GUEST_LAUNCHER_STATUS,
    GUEST_PROMPT,
    GUEST_STDERR,
    GUEST_TRACE,
)


GROK_DISALLOWED_TOOLS = (
    "todo_write,enter_plan_mode,exit_plan_mode,skill,ask_user_question,image_edit,"
    "workflow,scheduler_create,scheduler_delete,scheduler_list,monitor,send_feedback"
)


def claude_mcp_tools(public_tools=None) -> str:
    names = public_tools or ("AX", "GUI")
    qualified = []
    for name in names:
        name = str(name or "").strip()
        if not name or "," in name:
            raise ValueError(f"invalid MCP tool name: {name!r}")
        if name.startswith("mcp__computer_use__"):
            qualified.append(name)
        else:
            qualified.append(f"mcp__computer_use__{name}")
    if not qualified:
        raise ValueError("Claude Code requires at least one MCP tool")
    return ",".join(qualified)


# Shared by the agents whose stream does not write the final answer itself:
# take the last assistant text from the JSONL trace.
FINAL_MESSAGE_SCRIPT = """\
python3 - {trace} {final} <<'PY'
import json
import sys

last = ""
with open(sys.argv[1], encoding="utf-8", errors="replace") as handle:
    for line in handle:
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
{extract}
with open(sys.argv[2], "w", encoding="utf-8") as handle:
    handle.write(last)
PY
"""


def final_message_command(trace: str, final: str, extract: str) -> str:
    return FINAL_MESSAGE_SCRIPT.format(
        trace=shlex.quote(trace), final=shlex.quote(final), extract=extract
    )


def launcher_script(
    *,
    agent: str = "codex",
    model: str = "",
    effort: str = "xhigh",
    gateway_endpoint: str = "",
    public_mcp_tools=None,
) -> str:
    """Guest launcher for one agent turn.

    ``gateway_endpoint`` is the task-scoped gateway URL. Codex and Grok Build
    read it from their configuration files; Claude Code and Gemini CLI get it
    through the environment.
    """
    if agent == "claude-code":
        mcp_tools = claude_mcp_tools(public_mcp_tools)
        allowed_tools = f"{CLAUDE_BUILTIN_TOOLS},{mcp_tools}"
        common = (
            f"{shlex.quote(GUEST_CLAUDE_CODE)} -p "
            "--bare "
            f"--model {shlex.quote(model)} "
            f"--effort {shlex.quote(effort)} "
            "--permission-mode bypassPermissions "
            f"--mcp-config {shlex.quote(GUEST_CLAUDE_MCP_CONFIG)} "
            "--strict-mcp-config "
            "--setting-sources '' "
            f"--system-prompt-file {shlex.quote(GUEST_BASE_INSTRUCTIONS)} "
            f"--tools {shlex.quote(CLAUDE_BUILTIN_TOOLS)} "
            f"--allowedTools {shlex.quote(allowed_tools)} "
            "--disable-slash-commands "
            "--no-chrome "
            "--prompt-suggestions false "
            "--output-format stream-json --verbose"
        )
        start_command = (
            f"{common} < {shlex.quote(GUEST_PROMPT)} "
            f"> {shlex.quote(GUEST_TRACE)} "
            f"2> {shlex.quote(GUEST_STDERR)}"
        )
        resume_command = (
            f"{common} --resume \"$session_id\" "
            f"< {shlex.quote(GUEST_PROMPT)} "
            f"> {shlex.quote(GUEST_TRACE)} "
            f"2> {shlex.quote(GUEST_STDERR)}"
        )
        finalize_command = final_message_command(GUEST_TRACE, GUEST_FINAL, """\
        if event.get("type") == "result" and isinstance(event.get("result"), str):
            last = event["result"]
        elif event.get("type") == "assistant":
            for block in (event.get("message") or {}).get("content", []) or []:
                if block.get("type") == "text" and str(block.get("text") or "").strip():
                    last = str(block["text"])""")
        agent_environment = f"""\
export CLAUDE_CONFIG_DIR={shlex.quote(GUEST_CLAUDE_CONFIG_DIR)}
export ANTHROPIC_BASE_URL={shlex.quote(gateway_endpoint)}
export ANTHROPIC_AUTH_TOKEN=hcu-task-gateway
export CLAUDE_CODE_SKIP_AUTH_LOGIN=true
export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_CODE_ATTRIBUTION_HEADER=0
export DISABLE_TELEMETRY=1
export DISABLE_ERROR_REPORTING=1
export DISABLE_AUTOUPDATER=1
export DISABLE_NON_ESSENTIAL_MODEL_CALLS=1
export CLAUDE_CODE_DISABLE_FEEDBACK_SURVEY=1
export DISABLE_COST_WARNINGS=1
"""
    elif agent == "codex":
        start_command = (
            f"{shlex.quote(GUEST_CODEX)} exec "
            "--strict-config --json --color never --skip-git-repo-check "
            f"--output-last-message {shlex.quote(GUEST_FINAL)} "
            f"- < {shlex.quote(GUEST_PROMPT)} "
            f"> {shlex.quote(GUEST_TRACE)} "
            f"2> {shlex.quote(GUEST_STDERR)}"
        )
        resume_command = (
            f"{shlex.quote(GUEST_CODEX)} exec resume "
            "--strict-config --json --skip-git-repo-check "
            f"--output-last-message {shlex.quote(GUEST_FINAL)} "
            f"\"$session_id\" - < {shlex.quote(GUEST_PROMPT)} "
            f"> {shlex.quote(GUEST_TRACE)} "
            f"2> {shlex.quote(GUEST_STDERR)}"
        )
        finalize_command = ""
        agent_environment = f"""\
export CODEX_HOME={shlex.quote(GUEST_CODEX_HOME)}
export CODEX_SQLITE_HOME={shlex.quote(GUEST_CODEX_HOME)}
"""
    elif agent == "gemini-cli":
        # Assistant text streams as deltas; a turn ends with one "result" row.
        common = (
            f"{shlex.quote(GUEST_GEMINI_NODE)} {shlex.quote(GUEST_GEMINI_CLI)} "
            f"--model {shlex.quote(model)} "
            "--include-directories /home/user "
            "--approval-mode yolo "
            "--output-format stream-json"
        )
        start_command = (
            f"{common} < {shlex.quote(GUEST_PROMPT)} "
            f"> {shlex.quote(GUEST_TRACE)} "
            f"2> {shlex.quote(GUEST_STDERR)}"
        )
        resume_command = (
            f"{common} --resume \"$session_id\" "
            f"< {shlex.quote(GUEST_PROMPT)} "
            f"> {shlex.quote(GUEST_TRACE)} "
            f"2> {shlex.quote(GUEST_STDERR)}"
        )
        finalize_command = final_message_command(GUEST_TRACE, GUEST_FINAL, """\
        if event.get("type") == "message" and event.get("role") == "assistant":
            chunk = str(event.get("content") or "")
            last = last + chunk if event.get("delta") else chunk""")
        agent_environment = f"""\
export GEMINI_CLI_HOME={shlex.quote(GUEST_GEMINI_HOME)}
export GEMINI_CLI_SYSTEM_SETTINGS_PATH={shlex.quote(GUEST_GEMINI_HOME + "/absent-system-settings.json")}
export GEMINI_CLI_SYSTEM_DEFAULTS_PATH={shlex.quote(GUEST_GEMINI_HOME + "/absent-system-defaults.json")}
export GEMINI_CLI_TRUSTED_FOLDERS_PATH={shlex.quote(GUEST_GEMINI_TRUSTED)}
export GEMINI_SYSTEM_MD={shlex.quote(GUEST_BASE_INSTRUCTIONS)}
export GEMINI_EXP={shlex.quote(GUEST_GEMINI_EXPERIMENTS)}
export GEMINI_CLI_NO_RELAUNCH=1
export GOOGLE_GENAI_API_VERSION=v1beta
export GOOGLE_GEMINI_BASE_URL={shlex.quote(gateway_endpoint)}
export GEMINI_API_KEY=hcu-task-gateway
"""
    elif agent == "grok-build":
        common = (
            f"{shlex.quote(GUEST_GROK)} --cwd /home/user "
            "--model gateway "
            f"--reasoning-effort {shlex.quote(effort)} "
            f"--system-prompt-override=\"$(cat {shlex.quote(GUEST_BASE_INSTRUCTIONS)})\" "
            "--output-format streaming-json --always-approve "
            "--no-plan --no-subagents --disable-web-search --verbatim "
            f"--disallowed-tools {shlex.quote(GROK_DISALLOWED_TOOLS)}"
        )
        start_command = (
            f"{common} --prompt-file {shlex.quote(GUEST_PROMPT)} "
            f"> {shlex.quote(GUEST_TRACE)} "
            f"2> {shlex.quote(GUEST_STDERR)}"
        )
        resume_command = (
            "echo 'Grok Build cannot resume under the system prompt override' "
            f"> {shlex.quote(GUEST_STDERR)}; false"
        )
        finalize_command = final_message_command(GUEST_TRACE, GUEST_FINAL, """\
        if event.get("type") == "text" and isinstance(event.get("data"), str):
            last += event["data"]
        elif event.get("type") == "tool_call":
            last = \"\"""")
        agent_environment = f"""\
export GROK_HOME={shlex.quote(GUEST_GROK_HOME)}
export GROK_GATEWAY_TOKEN=hcu-task-gateway
export GROK_DISABLE_AUTOUPDATER=1
export GROK_MEMORY=0
export GROK_LOG_FILE={shlex.quote(GUEST_GROK_HOME + "/grok.log")}
export RUST_LOG=warn
"""
    else:
        raise ValueError(f"Unsupported task agent: {agent}")
    return f"""\
#!/bin/bash
set +e
export HOME=/home/user
{agent_environment}
export PATH=/home/user/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
cd /home/user
rm -f {shlex.quote(GUEST_TRACE)} {shlex.quote(GUEST_STDERR)} \
  {shlex.quote(GUEST_FINAL)} {shlex.quote(GUEST_DONE)} \
  {shlex.quote(GUEST_LAUNCHER_STATUS)}
mode="${{1:-start}}"
session_id="${{2:-}}"
turn_token="${{3:-unknown}}"
write_status() {{
  python3 - "$1" "$turn_token" {shlex.quote(GUEST_LAUNCHER_STATUS)} <<'PY'
import json
import os
import sys
import time
path = sys.argv[3]
tmp = path + ".tmp"
with open(tmp, "w", encoding="utf-8") as handle:
    json.dump({{
        "state": sys.argv[1],
        "turn_token": sys.argv[2],
        "pid": os.getppid(),
        "timestamp": time.time(),
    }}, handle)
    handle.flush()
    os.fsync(handle.fileno())
os.replace(tmp, path)
PY
}}
write_done() {{
  python3 - "$1" "$turn_token" {shlex.quote(GUEST_DONE)} <<'PY'
import json
import os
import sys
path = sys.argv[3]
tmp = path + ".tmp"
with open(tmp, "w", encoding="utf-8") as handle:
    json.dump({{
        "returncode": int(sys.argv[1]),
        "turn_token": sys.argv[2],
    }}, handle)
    handle.flush()
    os.fsync(handle.fileno())
os.replace(tmp, path)
PY
}}
launcher_exit() {{
  rc=$?
  trap - EXIT
  write_status "exited:$rc" || true
  write_done "$rc" || true
  exit "$rc"
}}
trap launcher_exit EXIT
write_status "started"
if [ "$mode" = "resume" ]; then
  {resume_command}
else
  {start_command}
fi
rc=$?
{finalize_command}
exit "$rc"
"""
