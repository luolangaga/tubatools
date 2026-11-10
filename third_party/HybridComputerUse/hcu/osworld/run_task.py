"""Run one OSWorld V1 or V2 task with the hybrid agent in the official QEMU runtime.

The task agent (Codex or Claude Code) and the computer-use MCP run inside
the QEMU guest; the outer Docker container is the stock OSWorld runtime that
only hosts the VM. The official evaluator scores the guest afterwards.
"""

from __future__ import annotations


import argparse
import os
import time

from hcu.paths import (
    DEFAULT_ARTIFACT_DIR,
    DEFAULT_ASSET_CACHE,
    DEFAULT_BASE_INSTRUCTIONS_FILE,
    DEFAULT_INSTRUCTIONS_FILE,
    OSWORLD_ROOT,
    anchor_path_args,
)
from hcu.osworld.agents import AGENT_PROFILES, AGENTS
from hcu.osworld.guest_lifecycle import close_guest, prepare_guest, start_guest
from hcu.osworld.phases import finish_run, record_failure, run_phases
from hcu.osworld.preflight import docker_runtime
from hcu.osworld.staging import initial_meta, prepare_run, stage_local_files
from desktop_env.file_source import configure as configure_assets


GATEWAY_PORTS = {"openai": 8611, "anthropic": 8537, "gemini": 8571}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--task-id", required=True)
    parser.add_argument(
        "--benchmark-version",
        choices=("v1", "v2"),
        default="v2",
        help=(
            "Task/evaluator generation. Both use the official Docker/QEMU "
            "runtime; v1 loads the legacy JSON task suite."
        ),
    )
    parser.add_argument(
        "--task-domain",
        default="",
        help="Optional V1 task domain; UUID lookup is automatic when unique.",
    )
    parser.add_argument(
        "--agent",
        choices=AGENTS,
        default="codex",
        help="Guest-local Task Agent.",
    )
    parser.add_argument(
        "--model",
        default="",
        help=(
            "Task Agent model. Defaults: gpt-5.5 (Codex), claude-opus-5 "
            "(Claude Code), gemini-3.8-flash (Gemini CLI), grok-4.7 (Grok Build)."
        ),
    )
    parser.add_argument(
        "--effort",
        default="",
        help=(
            "Reasoning effort; for Gemini CLI a thinking level or native. "
            "Defaults to xhigh, and to high for Gemini CLI."
        ),
    )
    parser.add_argument(
        "--gateway",
        default="",
        help=(
            "Model gateway URL reachable from the QEMU guest. Default: the "
            "Docker bridge gateway at the agent's port (OpenAI 8611, "
            "Anthropic 8537, Gemini 8571)."
        ),
    )
    parser.add_argument(
        "--artifact-dir",
        default=str(DEFAULT_ARTIFACT_DIR),
    )
    parser.add_argument(
        "--agent-binary",
        default="",
        help=(
            "Explicit agent executable, or the Gemini CLI npm tarball; "
            "otherwise the agent's file under --artifact-dir."
        ),
    )
    parser.add_argument(
        "--mcp-binary",
        default="",
        help="Explicit MCP executable; otherwise use artifact-dir/computer-use.",
    )
    parser.add_argument(
        "--guest-image",
        default="",
        help="Optional QCOW2 override for the evaluated guest.",
    )
    parser.add_argument(
        "--instructions-file",
        default=str(DEFAULT_INSTRUCTIONS_FILE),
        help="Profile instructions given to the agent; empty disables the profile.",
    )
    parser.add_argument(
        "--channels",
        default="",
        choices=("", "a11y,keyboard", "gui,keyboard"),
        help=(
            "Expose only the AX channel (a11y,keyboard) or only the GUI "
            "channel (gui,keyboard); empty exposes both."
        ),
    )
    parser.add_argument(
        "--code-mode",
        action="store_true",
        help=(
            "Codex only: give the model Codex's code tool, which calls the "
            "computer-use MCP tools from code; appends instructions/hybrid-code.md "
            "to the profile."
        ),
    )
    parser.add_argument(
        "--base-instructions-file",
        default=str(DEFAULT_BASE_INSTRUCTIONS_FILE),
        help="Compact model base instructions copied into the guest.",
    )
    parser.add_argument(
        "--result-dir",
        default="",
        help="Default: runs/single/<run-tag> in this repository.",
    )
    parser.add_argument(
        "--asset-cache",
        default=str(DEFAULT_ASSET_CACHE),
        help="Setup asset download cache, keyed by source URL.",
    )
    parser.add_argument(
        "--offline-assets",
        action="store_true",
        help="Fail setup instead of downloading an asset missing from the cache.",
    )
    parser.add_argument(
        "--timezone", default="UTC",
        help="Guest timezone; also decides the task's current date.",
    )
    parser.add_argument(
        "--run-started-at", default="",
        help="UTC start of the whole run (ISO 8601); fixes the task date across a panel.",
    )
    parser.add_argument(
        "--timeout",
        type=int,
        default=0,
        help=(
            "Optional per-phase wall-clock timeout in seconds; 0 disables "
            "this deadline, not the 1800-second generation no-progress limit."
        ),
    )
    parser.add_argument(
        "--max-model-calls",
        type=int,
        default=0,
        help=(
            "Allow this many successful task-scoped generation responses; "
            "the last response may finish its tools. 0 disables the count limit."
        ),
    )
    parser.add_argument(
        "--retain-container",
        action="store_true",
        help=(
            "Stop but do not remove the task VM container after evaluation, "
            "preserving it for manual inspection."
        ),
    )
    parser.add_argument(
        "--token-log",
        default="",
        help="Host-side gateway JSONL with successful-generation and usage evidence.",
    )
    parser.add_argument("--poll-interval", type=float, default=5.0)
    parser.add_argument(
        "--max-user-simulator-turns",
        type=int,
        default=0,
        help=(
            "Optional extra ASK_USER cap per phase; 0 relies on the normal "
            "per-phase model-call budget, matching the upstream step loop."
        ),
    )
    parser.add_argument("--client-password", default="osworld-public-evaluation")
    parser.add_argument(
        "--volume-size",
        type=int,
        default=40,
        help="Required guest root-volume size class in GiB, within existing image capacity.",
    )
    parser.add_argument(
        "--ram-size",
        default="4G",
        help="Docker guest RAM size, for example 4G or 8G.",
    )
    parser.add_argument("--enable-vnc", action="store_true")
    parser.add_argument("--enable-recording", action="store_true")
    args = parser.parse_args()
    if args.code_mode and args.agent != "codex":
        parser.error("--code-mode applies only to --agent codex")
    profile = AGENT_PROFILES[args.agent]
    args.model = args.model or profile["default_model"]
    args.effort = args.effort or profile["default_effort"]
    return args


def main() -> int:
    args = parse_args()
    anchor_path_args(
        args,
        "artifact_dir",
        "agent_binary",
        "mcp_binary",
        "guest_image",
        "instructions_file",
        "base_instructions_file",
        "result_dir",
        "token_log",
        "asset_cache",
    )
    os.chdir(OSWORLD_ROOT)
    configure_assets(args.asset_cache, offline=args.offline_assets)
    runtime = docker_runtime()
    if not args.gateway:
        bridge = (runtime["bridge_gateways"] or ["172.17.0.1"])[0]
        port = GATEWAY_PORTS[AGENT_PROFILES[args.agent]["gateway"]]
        args.gateway = f"http://{bridge}:{port}"
    run = prepare_run(args)
    stage_local_files(run)
    run.meta = initial_meta(run, runtime)
    run.write_meta()

    env = None
    run.started = time.monotonic()
    try:
        env = start_guest(run)
        base_url = prepare_guest(run, env)
        outcome = run_phases(run, env, base_url)
        return finish_run(run, env, base_url, outcome)
    except Exception as exc:
        return record_failure(run, env, exc)
    finally:
        if env is not None:
            close_guest(run, env)


if __name__ == "__main__":
    raise SystemExit(main())
