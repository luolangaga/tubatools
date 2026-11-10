"""Start one OSWorld task guest and run task setup without an agent."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import time


from hcu.paths import DEFAULT_ASSET_CACHE, OSWORLD_ROOT, anchor_path_args
from hcu.osworld.preflight import docker_runtime
from hcu.osworld.task import task_value
from hcu.osworld.task_loader import load_task_config
from desktop_env.file_source import configure as configure_assets
from desktop_env.cache import run_scoped_cache_dir
from desktop_env.desktop_env import DesktopEnv
from desktop_env.providers.volume import validate_guest_image_capacity


def parse_evaluator_fixture(value: str) -> tuple[Path, str]:
    local_value, separator, guest_path = value.partition("=")
    if not separator:
        raise argparse.ArgumentTypeError(
            "Evaluator fixture must use LOCAL_PATH=GUEST_PATH."
        )
    local_path = Path(local_value).expanduser().resolve()
    if not local_path.is_file():
        raise argparse.ArgumentTypeError(
            f"Evaluator fixture file does not exist: {local_path}"
        )
    if not guest_path.startswith("/"):
        raise argparse.ArgumentTypeError(
            f"Evaluator fixture guest path must be absolute: {guest_path}"
        )
    return local_path, guest_path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--task-id", required=True)
    parser.add_argument(
        "--benchmark-version",
        choices=("v1", "v2"),
        default="v2",
        help="Task/evaluator generation used to resolve the task definition.",
    )
    parser.add_argument(
        "--task-domain",
        help="Optional V1 task domain; UUID lookup is automatic when unique.",
    )
    parser.add_argument("--guest-image", type=Path, required=True)
    parser.add_argument("--result-dir", type=Path, required=True)
    parser.add_argument("--health-url", action="append", default=[])
    parser.add_argument(
        "--run-evaluator",
        action="store_true",
        help="Run the task evaluator after setup without starting an agent.",
    )
    parser.add_argument(
        "--evaluator-fixture",
        action="append",
        default=[],
        type=parse_evaluator_fixture,
        metavar="LOCAL_PATH=GUEST_PATH",
        help=(
            "Verifier-only file upload performed after setup and before the "
            "evaluator. Requires --run-evaluator."
        ),
    )
    parser.add_argument(
        "--asset-cache", type=Path, default=DEFAULT_ASSET_CACHE,
        help="Setup asset download cache, keyed by source URL.",
    )
    parser.add_argument(
        "--offline-assets", action="store_true",
        help="Fail setup instead of downloading an asset missing from the cache.",
    )
    parser.add_argument("--volume-size", type=int, default=40)
    parser.add_argument("--ram-size", default="4G")
    parser.add_argument(
        "--retain-container",
        action="store_true",
        help=(
            "Stop but do not remove the task container after setup, preserving "
            "its filesystem for later inspection."
        ),
    )
    parser.add_argument(
        "--client-password",
        default="osworld-public-evaluation",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    anchor_path_args(args, "guest_image", "result_dir", "asset_cache")
    os.chdir(OSWORLD_ROOT)
    if args.evaluator_fixture and not args.run_evaluator:
        raise ValueError("--evaluator-fixture requires --run-evaluator")
    image = args.guest_image.resolve()
    result_dir = args.result_dir.resolve()
    if not image.is_file():
        raise FileNotFoundError(f"Guest image not found: {image}")

    task = load_task_config(
        None,
        task_id=args.task_id,
        base_dir=str(OSWORLD_ROOT / "evaluation_examples"),
        domain=args.task_domain,
        eval_version=args.benchmark_version,
        prefer_class=args.benchmark_version == "v2",
    )
    configure_assets(args.asset_cache, offline=args.offline_assets)
    task_volume_size = task_value(task, "volume_size")
    volume_size = args.volume_size
    if task_volume_size is not None:
        volume_size = max(volume_size, int(task_volume_size))
    image_virtual_size_gib = validate_guest_image_capacity(
        image,
        volume_size,
    )
    result_dir.mkdir(parents=True, exist_ok=False)

    started = time.monotonic()
    env = None
    os.environ["OSWORLD_DOCKER_RAM_SIZE"] = args.ram_size
    report = {
        "task_id": args.task_id,
        "benchmark_version": args.benchmark_version,
        "task_domain": args.task_domain,
        "guest_image": str(image),
        "guest_image_virtual_size_gib": image_virtual_size_gib,
        "volume_size_gb": volume_size,
        "ram_size": args.ram_size,
        "health_urls": args.health_url,
        "run_evaluator": args.run_evaluator,
        "evaluator_fixtures": [
            {
                "local_path": str(local_path),
                "guest_path": guest_path,
            }
            for local_path, guest_path in args.evaluator_fixture
        ],
        "docker_runtime": docker_runtime(),
        "asset_cache": str(args.asset_cache),
        "offline_assets": args.offline_assets,
        "status": "initializing",
    }
    try:
        env = DesktopEnv(
            provider_name="docker",
            path_to_vm=str(image),
            action_space="pyautogui",
            cache_dir=run_scoped_cache_dir(
                result_dir,
                "desktop-env",
            ),
            headless=True,
            require_a11y_tree=False,
            os_type="Ubuntu",
            client_password=args.client_password,
            volume_size=volume_size,
            force_disable_vnc=True,
            force_disable_recording=True,
        )
        report["container_name"] = env.provider.container.name
        report["vm_startup_attempts"] = getattr(
            env.provider,
            "startup_attempts",
            [],
        )
        report["vm_runtime_environment"] = dict(
            getattr(env.provider, "environment", {})
        )
        report["status"] = "setup"
        (result_dir / "report.json").write_text(
            json.dumps(report, indent=2),
            encoding="utf-8",
        )

        env.reset(task_config=task)
        report["setup_attempts"] = list(
            getattr(env, "setup_attempts", [])
        )
        report["container_name"] = env.provider.container.name
        report["vm_startup_attempts"] = getattr(
            env.provider,
            "startup_attempts",
            [],
        )
        env.setup_controller.assert_root_writable()
        report["guest_root_health_after_setup"] = "writable"
        for url in args.health_url:
            env.setup_controller.wait_for_http(url, timeout=60)

        if args.run_evaluator:
            if args.evaluator_fixture:
                env.setup_controller._upload_file_setup([
                    {
                        "local_path": str(local_path),
                        "path": guest_path,
                    }
                    for local_path, guest_path in args.evaluator_fixture
                ])
            evaluator_started = time.monotonic()
            evaluator_result = env.evaluate()
            report["evaluator_result"] = json.loads(
                json.dumps(evaluator_result, default=str)
            )
            report["evaluator_duration_sec"] = round(
                time.monotonic() - evaluator_started,
                1,
            )

        screenshot = env.controller.get_screenshot()
        if screenshot:
            (result_dir / "final.png").write_bytes(screenshot)
        report["status"] = "passed"
        report["duration_sec"] = round(time.monotonic() - started, 1)
        (result_dir / "report.json").write_text(
            json.dumps(report, indent=2),
            encoding="utf-8",
        )
        print(json.dumps(report))
        return 0
    except Exception as exc:
        report["status"] = "failed"
        report["error"] = f"{type(exc).__name__}: {exc}"
        report["duration_sec"] = round(time.monotonic() - started, 1)
        (result_dir / "report.json").write_text(
            json.dumps(report, indent=2),
            encoding="utf-8",
        )
        raise
    finally:
        if env is not None:
            if args.retain_container:
                container = env.provider.container
                try:
                    container.stop(timeout=30)
                    retained_status = "stopped"
                    retained_error = None
                except Exception as exc:
                    retained_status = "stop_failed"
                    retained_error = f"{type(exc).__name__}: {exc}"
                report["container_retained"] = True
                report["container_name"] = container.name
                report["retained_container_status"] = retained_status
                report["retained_container_stop_error"] = retained_error
                (result_dir / "report.json").write_text(
                    json.dumps(report, indent=2),
                    encoding="utf-8",
                )
            else:
                env.close()


if __name__ == "__main__":
    raise SystemExit(main())
