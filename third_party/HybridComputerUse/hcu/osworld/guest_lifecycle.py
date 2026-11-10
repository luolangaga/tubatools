"""Start, prepare and close the QEMU guest for one task run."""

from __future__ import annotations

import json
import logging
import os
import shlex
import time

from hcu.osworld.guest import (
    GUEST_GEMINI_ARCHIVE,
    GUEST_GEMINI_CLI,
    GUEST_GEMINI_NODE,
    GUEST_GEMINI_PACKAGE,
    GUEST_MCP,
    request_execute,
    transient_workload_command,
)
from hcu.osworld.runtime import (
    align_guest_run_clock,
    install_guest_runtime,
    public_mcp_probe_script,
    public_mcp_probe_config,
    validate_guest_current_date,
    validate_provider_mount_isolation,
)
from hcu.osworld.staging import GUEST_AGENT_BINARIES, TaskRun, runtime_uploads
from hcu.osworld.task import task_value
from hcu.osworld.usage import check_gateway_budget
from desktop_env.cache import run_scoped_cache_dir
from desktop_env.desktop_env import DesktopEnv


def start_guest(run: TaskRun) -> DesktopEnv:
    """Create the QEMU guest; the agent runtime is installed before setup."""
    args = run.args
    meta = run.meta
    task_platform = str(task_value(run.task, "platform", "linux")).lower()
    os_type = "Windows" if task_platform.startswith("win") else "Ubuntu"
    if os_type != "Ubuntu":
        raise ValueError(
            "The OSWorld runner supports Ubuntu tasks only"
        )

    task_volume_size = task_value(run.task, "volume_size")
    volume_size = args.volume_size
    if task_volume_size is not None:
        volume_size = max(volume_size, int(task_volume_size))
    os.environ["OSWORLD_DOCKER_RAM_SIZE"] = args.ram_size
    upload_rows, executable_paths = runtime_uploads(run)
    unpack = (
        {GUEST_GEMINI_ARCHIVE: GUEST_GEMINI_PACKAGE}
        if args.agent == "gemini-cli"
        else {}
    )

    def bootstrap_runtime(setup_controller) -> None:
        install_guest_runtime(
            setup_controller,
            client_password=args.client_password,
            upload_rows=upload_rows,
            executable_paths=tuple(executable_paths),
            unpack=unpack,
        )

    env = DesktopEnv(
        provider_name="docker",
        path_to_vm=run.guest_image,
        action_space="pyautogui",
        cache_dir=run_scoped_cache_dir(run.result_dir, "desktop-env"),
        headless=True,
        require_a11y_tree=False,
        os_type=os_type,
        client_password=args.client_password,
        volume_size=volume_size,
        force_disable_vnc=not args.enable_vnc,
        force_disable_recording=not args.enable_recording,
        before_task_setup=bootstrap_runtime,
    )
    meta["provider_mounts"] = validate_provider_mount_isolation(
        env.provider.container,
        run.guest_image,
    )
    meta["container_name"] = env.provider.container.name
    meta["vm_startup_attempts"] = getattr(
        env.provider,
        "startup_attempts",
        [],
    )
    meta["vm_runtime_environment"] = dict(
        getattr(env.provider, "environment", {})
    )
    meta["volume_size_gb"] = volume_size
    meta["ram_size"] = args.ram_size
    meta["status"] = "setup"
    run.write_meta()
    return env


def prepare_guest(run: TaskRun, env: DesktopEnv) -> str:
    """Run task setup and the guest preflight checks; return the guest URL."""
    args = run.args
    meta = run.meta
    align_guest_run_clock(
        env.setup_controller,
        expected_date=run.current_date,
        timezone_name=run.run_timezone,
    )
    env.reset(task_config=run.task)
    reset_finished_at = time.monotonic()
    meta["setup_attempts"] = list(
        getattr(env, "setup_attempts", [])
    )
    meta["container_name"] = env.provider.container.name
    meta["vm_startup_attempts"] = getattr(
        env.provider,
        "startup_attempts",
        [],
    )
    base_url = env.setup_controller.http_server
    guest_date_probe = request_execute(
        base_url,
        ["date", "+%Y-%m-%d"],
        timeout=20,
    )
    meta["guest_current_date_after_setup"] = validate_guest_current_date(
        expected_date=run.current_date,
        command_output=guest_date_probe.get("output", ""),
    )
    env.setup_controller.assert_root_writable()
    meta["guest_root_health_after_setup"] = "writable"
    workload_preflight = request_execute(
        base_url,
        transient_workload_command(
            ["/usr/bin/true"],
            unit_name=f"agent-preflight-{run.run_tag}",
            wait=True,
        ),
        timeout=30,
    )
    meta["workload_cgroup_preflight"] = {
        "status": "passed",
        "output": str(
            workload_preflight.get("output") or ""
        ).strip(),
    }

    gateway_base = run.agent_endpoint.rsplit("/t/", 1)[0]
    health = request_execute(
        base_url,
        [
            "curl",
            "-fsS",
            "--max-time",
            "10",
            gateway_base + "/healthz",
        ],
        timeout=20,
    )
    meta["gateway_health"] = health.get("output", "").strip()
    check_gateway_budget(meta["gateway_health"], args.max_model_calls)

    agent_version = (
        f"{shlex.quote(GUEST_GEMINI_NODE)} {shlex.quote(GUEST_GEMINI_CLI)}"
        if args.agent == "gemini-cli"
        else shlex.quote(GUEST_AGENT_BINARIES[args.agent])
    )
    version_commands = [
        f"{agent_version} --version",
        f"{shlex.quote(GUEST_MCP)} --version",
    ]
    versions = request_execute(
        base_url,
        [
            "bash",
            "-lc",
            " && ".join(version_commands),
        ],
    )
    meta["guest_versions"] = versions.get("output", "").strip().splitlines()

    mcp_probe = request_execute(
        base_url,
        [
            "/usr/bin/python3",
            "-c",
            public_mcp_probe_script(),
            json.dumps(public_mcp_probe_config(
                run.config_path.read_text(), args.agent, run.mcp_public_tools,
            )),
        ],
        timeout=180,
    )
    meta["mcp_probe"] = mcp_probe.get("output", "").strip()
    initial_wait_remaining = max(
        0.0,
        60.0 - (time.monotonic() - reset_finished_at),
    )
    if initial_wait_remaining:
        time.sleep(initial_wait_remaining)
    env.controller.start_recording()
    run.recording_started = True
    meta["status"] = "agent_running"
    run.write_meta()
    return base_url


def close_guest(run: TaskRun, env: DesktopEnv) -> None:
    """Stop recording, then remove or retain the task container."""
    if run.recording_started:
        try:
            env.controller.end_recording(
                str(run.result_dir / "recording.mp4")
            )
        except Exception:
            logging.exception("Failed to stop task recording")
    if run.args.retain_container:
        container = getattr(
            getattr(env, "provider", None),
            "container",
            None,
        )
        retain_error = None
        if container is not None:
            try:
                container.stop(timeout=30)
                retain_status = "retained_stopped"
            except Exception as exc:
                retain_status = "retain_stop_failed"
                retain_error = f"{type(exc).__name__}: {exc}"
                logging.exception(
                    "Failed to stop retained task container"
                )
        else:
            retain_status = "retain_container_missing"
        cleanup_status = {
            "status": retain_status,
            "reason": "requested",
            "container_name": getattr(container, "name", None),
            "error": retain_error,
        }
    else:
        try:
            env.close()
        except Exception:
            logging.exception("Failed to close task environment")
        cleanup_status = getattr(
            getattr(env, "provider", None),
            "last_cleanup_status",
            None,
        )
    if cleanup_status is not None:
        run.meta["container_cleanup"] = cleanup_status
        run.write_meta(ensure_ascii=False)
