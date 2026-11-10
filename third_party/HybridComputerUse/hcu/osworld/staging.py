"""Resolve a task run's inputs and stage the agent files uploaded to the guest."""

from __future__ import annotations

import argparse
from dataclasses import dataclass, field
import datetime
import json
from pathlib import Path
import time
from typing import Any
import uuid

from hcu.paths import (
    CODE_MODE_INSTRUCTIONS_FILE,
    DEFAULT_RESULTS_DIR,
    OSWORLD_ROOT,
)
from hcu.osworld.agents import (
    AGENT_PROFILES,
    claude_mcp_config,
    claude_system_prompt,
    code_mode_profile,
    codex_config,
    gemini_experiments,
    gemini_settings,
    grok_config,
    grok_system_prompt,
    public_tools,
    task_gateway_base,
)
from hcu.osworld.launcher import launcher_script
from hcu.gateway.common import TASK_AFFINITY_PREFIX
from hcu.osworld.guest import (
    GUEST_BASE_INSTRUCTIONS,
    GUEST_CLAUDE_MCP_ADAPTER,
    GUEST_CLAUDE_MCP_CONFIG,
    GUEST_CODEX_HOME,
    GUEST_CLAUDE_CODE,
    GUEST_CODEX,
    GUEST_CONFIG,
    GUEST_GEMINI_ARCHIVE,
    GUEST_GEMINI_CONTEXT,
    GUEST_GEMINI_EXPERIMENTS,
    GUEST_GEMINI_NODE,
    GUEST_GEMINI_SETTINGS,
    GUEST_GEMINI_TRUSTED,
    GUEST_GROK,
    GUEST_GROK_CONFIG,
    GUEST_LAUNCHER,
    GUEST_MCP,
    GUEST_PROMPT,
    GUEST_INSTRUCTIONS,
    safe_tag,
)
from hcu.osworld.prompts import task_prompt
from hcu.osworld.task import (
    bind_task_runtime_timezone,
    resolve_guest_image,
    task_current_date,
    task_phases,
    task_value,
)
from hcu.osworld.task_loader import load_task_config


def write_text(path: Path, content: str) -> None:
    path.write_text(content, encoding="utf-8")


# Where each agent's artifact lands in the guest.
GUEST_AGENT_BINARIES = {
    "codex": GUEST_CODEX,
    "claude-code": GUEST_CLAUDE_CODE,
    "gemini-cli": GUEST_GEMINI_ARCHIVE,
    "grok-build": GUEST_GROK,
}

CLAUDE_MCP_ADAPTER = (
    Path(__file__).resolve().parent
    / "guest_files"
    / "claude_mcp_schema_adapter.py"
)


@dataclass
class TaskRun:
    """Inputs resolved before the guest starts, plus the evolving metadata."""

    args: argparse.Namespace
    run_tag: str
    result_dir: Path
    token_log: Path
    affinity_task: str
    agent_binary: Path
    node_binary: Path | None
    mcp_binary: Path
    instructions_file: Path | None
    base_instructions_file: Path
    task: Any
    phases: list[dict]
    instruction: str
    current_date: str
    run_timezone: str
    run_started_at_utc: str
    explicit_task_date: Any
    guest_image: str
    agent_endpoint: str = ""
    meta: dict = field(default_factory=dict)
    started: float = 0.0
    recording_started: bool = False

    @property
    def profile(self) -> dict:
        return AGENT_PROFILES[self.args.agent]

    @property
    def config_path(self) -> Path:
        return self.result_dir / {
            "codex": "guest-config.toml",
            "claude-code": "guest-mcp.json",
            "gemini-cli": "guest-settings.json",
            "grok-build": "guest-config.toml",
        }[self.args.agent]

    @property
    def gemini_trusted_path(self) -> Path:
        return self.result_dir / "guest-trusted-folders.json"

    @property
    def gemini_experiments_path(self) -> Path:
        return self.result_dir / "guest-experiments.json"

    @property
    def prompt_path(self) -> Path:
        return self.result_dir / "prompt.txt"

    @property
    def launcher_path(self) -> Path:
        return self.result_dir / "guest-launcher.sh"

    @property
    def instructions_copy(self) -> Path:
        return self.result_dir / "operating-instructions.md"

    @property
    def mcp_public_tools(self) -> list[str]:
        return list(public_tools(self.args.channels))

    @property
    def base_instructions_copy(self) -> Path:
        return self.result_dir / "agent-base-instructions.md"

    def write_meta(self, **json_options) -> None:
        write_text(
            self.result_dir / "meta.json",
            json.dumps(self.meta, **json_options, indent=2),
        )



def prepare_run(args: argparse.Namespace) -> TaskRun:
    """Resolve result paths, artifacts and the task before any guest work."""
    run_tag = (
        f"{time.strftime('%Y%m%d-%H%M%S')}-task-{safe_tag(args.task_id)}-"
        f"{safe_tag(args.agent)}-{safe_tag(args.model)}-{safe_tag(args.effort)}-"
        f"{uuid.uuid4()}"
    )
    result_dir = Path(
        args.result_dir or DEFAULT_RESULTS_DIR / "single" / run_tag
    ).resolve()
    result_dir.mkdir(parents=True, exist_ok=False)
    token_log = Path(
        args.token_log or result_dir.parent / "gateway-tokens.jsonl"
    ).resolve()
    if args.max_model_calls > 0 and not token_log.is_file():
        raise FileNotFoundError(
            f"--max-model-calls requires the gateway token log: {token_log}"
        )
    affinity_task = f"{TASK_AFFINITY_PREFIX}{run_tag}"

    artifact_dir = Path(args.artifact_dir).resolve()
    profile = AGENT_PROFILES[args.agent]
    agent_binary = Path(
        args.agent_binary or artifact_dir / profile["artifact"]
    ).resolve()
    node_binary = (
        (artifact_dir / "node").resolve() if args.agent == "gemini-cli" else None
    )
    mcp_binary = Path(
        args.mcp_binary or artifact_dir / "computer-use"
    ).resolve()
    instructions_file = (
        Path(args.instructions_file).resolve() if args.instructions_file else None
    )
    base_instructions_file = Path(
        args.base_instructions_file
    ).resolve()
    required_files = [agent_binary, mcp_binary, base_instructions_file]
    if instructions_file is not None:
        required_files.append(instructions_file)
    if args.code_mode:
        required_files.append(CODE_MODE_INSTRUCTIONS_FILE)
    if args.agent == "claude-code":
        required_files.append(CLAUDE_MCP_ADAPTER)
    if node_binary is not None:
        required_files.append(node_binary)
    for required in required_files:
        if not required.is_file():
            raise FileNotFoundError(required)
    task = load_task_config(
        None,
        task_id=args.task_id,
        base_dir=str(OSWORLD_ROOT / "evaluation_examples"),
        domain=args.task_domain or None,
        eval_version=args.benchmark_version,
        prefer_class=args.benchmark_version == "v2",
    )
    phases = task_phases(task)
    run_started_at_utc = (
        args.run_started_at
        or datetime.datetime.now(datetime.timezone.utc).isoformat()
    )
    run_timezone = args.timezone
    explicit_task_date = (
        task.get("task_current_date")
        if hasattr(task, "get") and callable(getattr(task, "get"))
        else None
    )
    if explicit_task_date is None:
        explicit_task_date = getattr(task, "task_current_date", None)
    current_date = task_current_date(
        task,
        run_started_at_utc=run_started_at_utc,
        timezone_name=run_timezone,
    )
    bind_task_runtime_timezone(task, run_timezone)
    instruction = str(task_value(task, "instruction", ""))
    guest_image = resolve_guest_image(task, args)
    if not instruction:
        raise ValueError(f"Task {args.task_id} has no instruction")
    return TaskRun(
        args=args,
        run_tag=run_tag,
        result_dir=result_dir,
        token_log=token_log,
        affinity_task=affinity_task,
        agent_binary=agent_binary,
        node_binary=node_binary,
        mcp_binary=mcp_binary,
        instructions_file=instructions_file,
        base_instructions_file=base_instructions_file,
        task=task,
        phases=phases,
        instruction=instruction,
        current_date=current_date,
        run_timezone=run_timezone,
        run_started_at_utc=run_started_at_utc,
        explicit_task_date=explicit_task_date,
        guest_image=guest_image,
    )


def stage_local_files(run: TaskRun) -> None:
    """Write the agent configuration, instructions and launcher locally."""
    args = run.args
    run.agent_endpoint = task_gateway_base(
        args.gateway,
        run.run_tag,
        affinity_prefix=TASK_AFFINITY_PREFIX.rstrip("-"),
    )
    if args.agent == "codex":
        config = codex_config(
            model=args.model,
            effort=args.effort,
            base_url=run.agent_endpoint + "/v1",
            codex_home=GUEST_CODEX_HOME,
            base_instructions=GUEST_BASE_INSTRUCTIONS,
            mcp_command=GUEST_MCP,
            channels=args.channels,
            execution_context="isolated-guest",
            code_mode=getattr(args, "code_mode", False),
        )
    elif args.agent == "claude-code":
        config = claude_mcp_config(channels=args.channels)
    elif args.agent == "gemini-cli":
        config = gemini_settings(
            model=args.model, effort=args.effort, channels=args.channels
        )
    else:
        config = grok_config(
            model=args.model,
            base_url=run.agent_endpoint + "/v1",
            effort=args.effort,
            channels=args.channels,
        )
    write_text(run.config_path, config)
    write_text(
        run.prompt_path,
        task_prompt(
            run.instruction,
            current_date=run.current_date,
            can_ask_user=task_value(run.task, "user_simulator") is not None,
        ),
    )
    # Codex reads the profile from a task-local AGENTS.md and Gemini CLI from
    # its global GEMINI.md, both uploaded from instructions_copy; Claude Code and
    # Grok Build get it inside their replacement system prompts.
    if getattr(args, "code_mode", False):
        selected = (
            run.instructions_file.read_text(encoding="utf-8")
            if run.instructions_file is not None else ""
        )
        write_text(run.instructions_copy, code_mode_profile(
            selected, CODE_MODE_INSTRUCTIONS_FILE.read_text(encoding="utf-8")))
    elif run.instructions_file is not None:
        run.instructions_copy.write_bytes(run.instructions_file.read_bytes())
    base_instructions = run.base_instructions_file.read_text(encoding="utf-8")
    if run.instructions_file is not None:
        instructions = run.instructions_file.read_text(encoding="utf-8")
        if args.agent == "claude-code":
            base_instructions = claude_system_prompt(base_instructions, instructions)
        elif args.agent == "grok-build":
            base_instructions = grok_system_prompt(base_instructions, instructions)
    write_text(run.base_instructions_copy, base_instructions)
    if args.agent == "gemini-cli":
        write_text(
            run.gemini_trusted_path,
            json.dumps({"/home/user": "TRUST_FOLDER"}) + "\n",
        )
        write_text(run.gemini_experiments_path, gemini_experiments())
    write_text(
        run.launcher_path,
        launcher_script(
            agent=args.agent,
            model=args.model,
            effort=args.effort,
            gateway_endpoint=run.agent_endpoint,
            public_mcp_tools=run.mcp_public_tools,
        ),
    )


def initial_meta(run: TaskRun, docker_runtime: dict) -> dict:
    args = run.args
    return {
        "run_tag": run.run_tag,
        "task_id": args.task_id,
        "benchmark_version": args.benchmark_version,
        "task_domain": args.task_domain or None,
        "docker_runtime": docker_runtime,
        "asset_cache": args.asset_cache,
        "offline_assets": args.offline_assets,
        "instruction": run.instruction,
        "task_current_date": run.current_date,
        "task_current_date_source": (
            "task_config"
            if run.explicit_task_date is not None
            else "run_start"
        ),
        "task_current_date_timezone": run.run_timezone,
        "run_started_at_utc": run.run_started_at_utc,
        "task_protocol": "multi_phase" if run.phases else "single_phase",
        "phase_count": len(run.phases) if run.phases else 1,
        "model": args.model,
        "effort": args.effort,
        "agent": args.agent,
        "gateway": run.profile["gateway"],
        "gateway_endpoint": run.agent_endpoint,
        "affinity_task": run.affinity_task,
        "token_log": str(run.token_log),
        "provider": "docker",
        "guest_image": run.guest_image,
        "agent_binary": str(run.agent_binary),
        "claude_mcp_adapter": (
            str(CLAUDE_MCP_ADAPTER)
            if args.agent == "claude-code"
            else None
        ),
        "mcp_binary": str(run.mcp_binary),
        "mcp_channels": args.channels or None,
        "code_mode": bool(getattr(args, "code_mode", False)),
        "mcp_public_tools": run.mcp_public_tools,
        "instructions_file": str(run.instructions_file) if run.instructions_file else None,
        "base_instructions_file": str(run.base_instructions_file),
        "start_time": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "status": "initializing",
    }


def runtime_uploads(run: TaskRun) -> tuple[list[dict], list[str]]:
    """Return the guest runtime upload rows and the paths to mark executable."""
    args = run.args
    upload_rows = [
        {
            "local_path": str(run.base_instructions_copy),
            "path": GUEST_BASE_INSTRUCTIONS,
        },
        {"local_path": str(run.prompt_path), "path": GUEST_PROMPT},
        {"local_path": str(run.launcher_path), "path": GUEST_LAUNCHER},
        {"local_path": str(run.mcp_binary), "path": GUEST_MCP},
    ]
    executable_paths = [GUEST_MCP, GUEST_LAUNCHER]
    guest_binary = GUEST_AGENT_BINARIES[args.agent]
    upload_rows.append({"local_path": str(run.agent_binary), "path": guest_binary})
    if args.agent != "gemini-cli":
        executable_paths.append(guest_binary)
    if args.agent == "codex":
        upload_rows.append({"local_path": str(run.config_path), "path": GUEST_CONFIG})
        if run.instructions_file is not None or getattr(args, "code_mode", False):
            upload_rows.append(
                {"local_path": str(run.instructions_copy), "path": GUEST_INSTRUCTIONS}
            )
    elif args.agent == "claude-code":
        upload_rows.extend([
            {"local_path": str(run.config_path), "path": GUEST_CLAUDE_MCP_CONFIG},
            {"local_path": str(CLAUDE_MCP_ADAPTER), "path": GUEST_CLAUDE_MCP_ADAPTER},
        ])
    elif args.agent == "gemini-cli":
        upload_rows.extend([
            {"local_path": str(run.node_binary), "path": GUEST_GEMINI_NODE},
            {"local_path": str(run.config_path), "path": GUEST_GEMINI_SETTINGS},
            {"local_path": str(run.gemini_trusted_path), "path": GUEST_GEMINI_TRUSTED},
            {
                "local_path": str(run.gemini_experiments_path),
                "path": GUEST_GEMINI_EXPERIMENTS,
            },
        ])
        if run.instructions_file is not None:
            upload_rows.append(
                {"local_path": str(run.instructions_copy), "path": GUEST_GEMINI_CONTEXT}
            )
        executable_paths.append(GUEST_GEMINI_NODE)
    else:
        upload_rows.append({"local_path": str(run.config_path), "path": GUEST_GROK_CONFIG})
    return upload_rows, executable_paths
