"""Render one agent configuration that uses the computer-use MCP.

This renders the OSWorld runner's agent context for local use: the same
Codex feature switches and disabled bundled skills, the Claude Code MCP
adapter with its tool set and replacement system prompt, the Gemini CLI
settings with its isolated context file, or the Grok Build config bound to a
gateway. Codex uses workspace-write sandboxing and on-request approvals.
Claude also gets a Python launcher with explicit model, effort and task
gateway settings; its local tool execution requires manual permission.

Usage::

    python -m hcu.render_agent_config --agent codex --model gpt-5.5 \\
        --gateway http://127.0.0.1:8611 --mcp artifacts/computer-use \\
        --output ~/.codex/config.toml
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import sys

from hcu.osworld import agents
from hcu.paths import (
    CODE_MODE_INSTRUCTIONS_FILE, DEFAULT_BASE_INSTRUCTIONS_FILE, DEFAULT_INSTRUCTIONS_FILE, REPO_ROOT,
)

CLAUDE_MCP_ADAPTER = (
    REPO_ROOT / "hcu/osworld/guest_files/claude_mcp_schema_adapter.py"
)
AGENTS = ("codex", "claude-code", "gemini-cli", "grok-build")


def validate_output(agent: str, output: Path) -> None:
    if agent == "grok-build" and output.name != "config.toml":
        raise ValueError(
            "Grok --output must be <home>/config.toml; "
            "GROK_HOME points to <home>."
        )
    if agent == "gemini-cli" and (
        output.name != "settings.json" or output.parent.name != ".gemini"
    ):
        raise ValueError(
            "Gemini --output must be <home>/.gemini/settings.json; "
            "GEMINI_CLI_HOME points to <home>."
        )


def claude_launcher(args: argparse.Namespace, prompt: Path) -> str:
    command = [
        "claude", "--bare", "--model", args.model, "--effort", args.effort,
        "--permission-mode", "manual",
        "--mcp-config", str(args.output), "--strict-mcp-config",
        "--setting-sources", "", "--system-prompt-file", str(prompt),
        "--tools", agents.CLAUDE_BUILTIN_TOOLS, "--disable-slash-commands",
        "--no-chrome", "--prompt-suggestions", "false",
    ]
    environment = {
        "ANTHROPIC_BASE_URL": f"{args.gateway.rstrip('/')}/t/{args.task_id}",
        # Bare mode reads API-key authentication, not the user's OAuth login.
        "ANTHROPIC_API_KEY": "hcu-task-gateway",
        "ANTHROPIC_AUTH_TOKEN": "hcu-task-gateway",
        "CLAUDE_CODE_USE_BEDROCK": "0",
        "CLAUDE_CODE_USE_VERTEX": "0",
        "CLAUDE_CODE_USE_FOUNDRY": "0",
        "CLAUDE_CODE_SKIP_AUTH_LOGIN": "true",
        "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC": "1",
        "CLAUDE_CODE_DISABLE_AUTO_MEMORY": "1",
        "DISABLE_TELEMETRY": "1",
        "DISABLE_ERROR_REPORTING": "1",
        "DISABLE_AUTOUPDATER": "1",
        "DISABLE_NON_ESSENTIAL_MODEL_CALLS": "1",
    }
    return (
        '"""Launch Claude with the generated local Computer Use profile."""\n'
        "import os\n"
        "import subprocess\n"
        "import sys\n\n"
        f"command = {command!r}\n"
        f"environment = {environment!r}\n\n"
        "if __name__ == '__main__':\n"
        "    raise SystemExit(subprocess.call(\n"
        "        command + sys.argv[1:], env=dict(os.environ, **environment)\n"
        "    ))\n"
    )


def render(args: argparse.Namespace) -> None:
    validate_output(args.agent, args.output)
    gateway = args.gateway.rstrip("/")
    base_instructions = args.base_instructions.read_text(encoding="utf-8")
    instructions = (
        Path(args.instructions_file).expanduser().read_text(encoding="utf-8")
        if args.instructions_file
        else ""
    )
    if args.agent == "codex":
        if args.code_mode:
            instructions = agents.code_mode_profile(
                instructions, CODE_MODE_INSTRUCTIONS_FILE.read_text(encoding="utf-8"))
        args.output.write_text(
            agents.codex_config(
                model=args.model,
                effort=args.effort,
                base_url=f"{gateway}/t/{args.task_id}/v1",
                codex_home=args.codex_home,
                base_instructions=str(args.base_instructions.expanduser().resolve()),
                mcp_command=str(args.mcp.expanduser().absolute()),
                channels=args.channels,
                developer_instructions=instructions,
                execution_context="standalone",
                code_mode=args.code_mode,
            ),
            encoding="utf-8",
        )
        print("Codex shell permissions: workspace-write sandbox, on-request approvals")
        print("The MCP desktop session is not isolated by the Codex shell sandbox.")
    elif args.agent == "claude-code":
        args.output.write_text(
            agents.claude_mcp_config(
                adapter=str(CLAUDE_MCP_ADAPTER),
                mcp_command=str(args.mcp.expanduser().absolute()),
                channels=args.channels,
                python_executable=sys.executable,
            ),
            encoding="utf-8",
        )
        prompt = args.output.with_suffix(".system-prompt.md")
        prompt.write_text(
            agents.claude_system_prompt(base_instructions, instructions),
            encoding="utf-8",
        )
        launcher = args.output.with_suffix(".launch.py")
        launcher.write_text(claude_launcher(args, prompt), encoding="utf-8")
        print(f"Claude launcher: {launcher} (run with Python; requires claude on PATH)")
        print("Claude tool permissions: manual approval")
        print(f"Claude replacement system prompt: {prompt}")
    elif args.agent == "gemini-cli":
        args.output.write_text(
            agents.gemini_settings(
                model=args.model,
                effort=args.effort,
                mcp_command=str(args.mcp),
                channels=args.channels,
            ),
            encoding="utf-8",
        )
        # Gemini CLI reads the profile as the global context file next to its
        # isolated settings, and the base instructions replace its core prompt.
        context = args.output.parent / "GEMINI.md"
        context.write_text(instructions, encoding="utf-8")
        args.output.with_name("base-instructions.md").write_text(
            base_instructions, encoding="utf-8"
        )
        args.output.with_name("experiments.json").write_text(
            agents.gemini_experiments(), encoding="utf-8"
        )
        print(f"Gemini context file: {context}")
        print(
            "Gemini environment: "
            + json.dumps({
                "GEMINI_CLI_HOME": str(args.output.parent.parent),
                "GEMINI_SYSTEM_MD": str(
                    args.output.with_name("base-instructions.md")
                ),
                "GEMINI_EXP": str(args.output.with_name("experiments.json")),
                "GOOGLE_GEMINI_BASE_URL": f"{gateway}/t/{args.task_id}",
                "GEMINI_API_KEY": "placeholder",
            })
        )
    elif args.agent == "grok-build":
        args.output.write_text(
            agents.grok_config(
                model=args.model,
                base_url=f"{gateway}/t/{args.task_id}/v1",
                effort=args.effort,
                mcp_command=str(args.mcp),
                channels=args.channels,
            ),
            encoding="utf-8",
        )
        prompt = args.output.with_name("system-prompt.md")
        prompt.write_text(
            agents.grok_system_prompt(base_instructions, instructions), encoding="utf-8"
        )
        print(f"Grok system prompt: {prompt}")
        print(
            "Grok environment: "
            + json.dumps({
                "GROK_HOME": str(args.output.parent),
                "GROK_GATEWAY_TOKEN": "placeholder",
            })
        )
    print(args.output)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--agent", choices=AGENTS, required=True)
    parser.add_argument(
        "--model",
        default="",
        help="Model name; defaults to the agent's own default.",
    )
    parser.add_argument(
        "--effort",
        default="",
        help="Reasoning effort or, for Gemini CLI, a thinking level.",
    )
    parser.add_argument("--task-id", default="manual")
    parser.add_argument("--gateway", required=True)
    parser.add_argument("--mcp", type=Path, required=True)
    parser.add_argument(
        "--output", type=Path, required=True,
        help=(
            "Config path; Gemini requires <home>/.gemini/settings.json; "
            "Grok requires <home>/config.toml."
        ),
    )
    parser.add_argument(
        "--base-instructions",
        type=Path,
        default=DEFAULT_BASE_INSTRUCTIONS_FILE,
    )
    parser.add_argument(
        "--instructions-file",
        default=str(DEFAULT_INSTRUCTIONS_FILE),
        help="Profile instructions; pass an empty string to disable the profile.",
    )
    parser.add_argument(
        "--channels",
        default="",
        choices=("", "a11y,keyboard", "gui,keyboard"),
        help="Expose only one desktop channel; empty exposes both tools.",
    )
    parser.add_argument(
        "--code-mode",
        action="store_true",
        help=(
            "Codex only: the code tool calls the computer-use MCP tools from "
            "code; appends instructions/hybrid-code.md to the profile."
        ),
    )
    parser.add_argument(
        "--codex-home",
        default=os.environ.get("CODEX_HOME") or str(Path.home() / ".codex"),
        help="Local Codex home; defaults to CODEX_HOME or ~/.codex.",
    )
    args = parser.parse_args()
    if args.code_mode and args.agent != "codex":
        parser.error("--code-mode applies only to --agent codex")
    profile = agents.AGENT_PROFILES[args.agent]
    args.model = args.model or profile["default_model"]
    args.effort = args.effort or profile["default_effort"]
    args.codex_home = str(Path(args.codex_home).expanduser().absolute())
    args.output = args.output.expanduser().resolve()
    args.base_instructions = args.base_instructions.expanduser()
    args.mcp = args.mcp.expanduser().absolute()
    try:
        validate_output(args.agent, args.output)
    except ValueError as exc:
        parser.error(str(exc))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    render(args)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
