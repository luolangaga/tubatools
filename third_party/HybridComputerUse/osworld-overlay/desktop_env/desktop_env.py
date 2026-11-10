# SPDX-License-Identifier: Apache-2.0
# Modified by the Hybrid Computer Use authors from desktop_env/desktop_env.py
# in xlang-ai/OSWorld-V2 (revision d578d2d4). See THIRD_PARTY_NOTICES.md.

from __future__ import annotations

import logging
import os
import re
import shlex
import time
import inspect
from typing import Callable, Any, Optional, Tuple
from typing import List, Dict, Union

import gymnasium as gym

from desktop_env.controllers.python import PythonController
from desktop_env.controllers.setup import SetupController
from desktop_env.cache import isolated_default_cache_dir
from desktop_env.evaluators import metrics, getters
from desktop_env.providers import create_vm_manager_and_provider
from desktop_env.task_base import BaseTask

logger = logging.getLogger("desktopenv.env")

Metric = Callable[[Any, Any], float]
Getter = Callable[[gym.Env, Dict[str, Any]], Any]

MAX_RETRIES = 5


class EnvironmentSetupError(RuntimeError):
    """Raised when environment setup cannot be completed successfully."""


class DesktopEnv(gym.Env):
    """
    DesktopEnv with OpenAI Gym interface. It provides a desktop environment for setting and evaluating desktop automation tasks.
    """
    def __init__(
            self,
            provider_name: str = "vmware",
            region: str = None,
            path_to_vm: str = None,
            snapshot_name: str = "init_state",
            action_space: str = "pyautogui",
            cache_dir: str | None = None,
            screen_size: Tuple[int] = (int(os.environ.get("SCREEN_WIDTH", 1920)), int(os.environ.get("SCREEN_HEIGHT", 1080))),
            headless: bool = False,
            require_a11y_tree: bool = True,
            require_terminal: bool = False,
            os_type: str = "Ubuntu",
            enable_proxy: bool = False,
            client_password: str = "",
            instance_type: str = None,
            volume_size: int | None = None,
            use_public_ip: bool = False,
            force_disable_vnc: bool = False,
            force_disable_recording: bool = False,
            before_task_setup: Callable[[SetupController], None] | None = None,
    ):
        """
        Args:
            provider_name (str): virtualization provider name, default to "vmware"
            region (str): the region for allocate machines, work for cloud services, default to  "us-east-1"
            path_to_vm (str): path to .vmx file
            snapshot_name (str): snapshot name to revert to, default to "init_state"
            action_space (str): "computer_13" | "pyautogui"
            cache_dir (str | None): cache root for setup and evaluator files.
              When omitted, one unique temporary root is created for this
              environment instead of reusing repository state.
            screen_size (Tuple[int]): screen size of the VM
            headless (bool): whether to run the VM in headless mode
            require_a11y_tree (bool): whether to require accessibility tree
            require_terminal (bool): whether to require terminal output
            os_type (str): operating system type, default to "Ubuntu"
            enable_proxy (bool): whether to enable proxy support, default to False
            instance_type (str): AWS instance type (e.g., "t3.medium", "t3.large"), default to None (uses default)
            volume_size (int | None): Desired root volume size in GB; uses provider default when None
            use_public_ip (bool): whether to use the public IPv4 address of AWS instances instead of private IP, default to False
            force_disable_vnc (bool): globally disable VNC for all tasks regardless of per-task settings, default to False
            force_disable_recording (bool): globally disable recording for all tasks regardless of per-task settings, default to False
        """
        self.region = region
        self.provider_name = provider_name
        self.enable_proxy = enable_proxy
        self.force_disable_vnc = force_disable_vnc
        self.force_disable_recording = force_disable_recording
        self.before_task_setup = before_task_setup
        self.volume_size = volume_size
        self.client_password = client_password or "osworld-public-evaluation"

        self.screen_width = screen_size[0]
        self.screen_height = screen_size[1]

        self.server_port = 5000
        self.chromium_port = 9222
        self.vnc_port = 8006
        self.vlc_port = 8080

        self.current_use_proxy = False
        self.manager, self.provider = create_vm_manager_and_provider(provider_name, region, use_proxy=False, use_public_ip=use_public_ip)

        self.os_type = os_type

        # Track whether environment has been used (step/setup) to optimize snapshot revert
        # docker, aws, gcp, azure are always unused as the emulator starts from a clean state
        # vmware, virtualbox are always used as the emulator starts from a dirty state
        if self.provider_name in {"docker", "aws", "gcp", "azure", "aliyun", "volcengine"}:
            self.is_environment_used = False
        elif self.provider_name in {"vmware", "virtualbox"}:
            self.is_environment_used = True
        else:
            raise ValueError(f"Invalid provider name: {self.provider_name}")

        if path_to_vm:
            self.path_to_vm = os.path.abspath(os.path.expandvars(os.path.expanduser(path_to_vm))) \
                if provider_name in {"vmware", "virtualbox"} else path_to_vm
        else:
            if provider_name == "aws":
                self.path_to_vm = self.manager.get_vm_path(os_type=self.os_type, region=region, screen_size=(self.screen_width, self.screen_height), ami_id=snapshot_name, instance_type=instance_type, volume_size=volume_size)
            else:
                self.path_to_vm = self.manager.get_vm_path(os_type=self.os_type, region=region, screen_size=(self.screen_width, self.screen_height), volume_size=volume_size)

        self.snapshot_name = snapshot_name
        self.cache_dir_base: str = (
            cache_dir if cache_dir is not None else isolated_default_cache_dir()
        )
        self.headless = headless
        self.require_a11y_tree = require_a11y_tree
        self.require_terminal = require_terminal

        logger.info("Initializing...")
        self._prepare_volume()
        self._start_emulator()

        self.instruction = None
        assert action_space in ["computer_13", "pyautogui", "claude_computer_use", "autoglm_computer_use"]
        self.action_space = action_space

        # episodic stuffs, like counters, will be updated or reset
        # when calling self.reset()
        self._traj_no: int = -1
        self._step_no: int = 0
        self.action_history: List[Dict[str, any]] = []
        self.task_config = None
        self.user_simulator = None

    def _prepare_volume(self) -> None:
        if self.volume_size is None:
            return
        logger.info(
            "Preparing provider volume for provider=%s size=%sGB os_type=%s",
            self.provider_name,
            self.volume_size,
            self.os_type,
        )
        self.provider.prepare_volume(self.path_to_vm, self.volume_size, self.os_type)

    def _finalize_volume(self) -> None:
        if self.volume_size is None:
            return
        logger.info(
            "Finalizing guest volume for provider=%s size=%sGB os_type=%s",
            self.provider_name,
            self.volume_size,
            self.os_type,
        )
        if not self.setup_controller.ensure_ready(False):
            raise EnvironmentSetupError("Environment control server not ready before volume expansion")
        self.provider.finalize_volume(
            self.path_to_vm,
            self.volume_size,
            self.os_type,
            self.controller,
            self.setup_controller,
            self.client_password,
        )

    def _start_emulator(self):
        try:
            self.provider.start_emulator(self.path_to_vm, self.headless, self.os_type)

            vm_ip_ports = self.provider.get_ip_address(self.path_to_vm).split(':')
            self.vm_ip = vm_ip_ports[0]
            if len(vm_ip_ports) > 1:
                self.server_port = int(vm_ip_ports[1])
                self.chromium_port = int(vm_ip_ports[2])
                self.vnc_port = int(vm_ip_ports[3])
                self.vlc_port = int(vm_ip_ports[4])
            self.controller = PythonController(vm_ip=self.vm_ip, server_port=self.server_port)
            setup_vm_ip = self.vm_ip
            setup_server_port = self.server_port
            setup_chromium_port = self.chromium_port
            setup_vlc_port = self.vlc_port
            if self.provider_name == "docker":
                setup_vm_ip = self.provider.get_service_ip_address()
                setup_server_port = 5000
                setup_chromium_port = 9222
                setup_vlc_port = 8080
            self.setup_controller = SetupController(
                vm_ip=setup_vm_ip,
                control_ip=self.vm_ip,
                server_port=setup_server_port,
                chromium_port=setup_chromium_port,
                vlc_port=setup_vlc_port,
                control_server_port=self.server_port,
                control_chromium_port=self.chromium_port,
                control_vlc_port=self.vlc_port,
                cache_dir=self.cache_dir_base,
                client_password=self.client_password,
                screen_width=self.screen_width,
                screen_height=self.screen_height,
            )
            self._finalize_volume()

        except Exception as e:
            try:
                self.provider.stop_emulator(self.path_to_vm)
            except Exception as stop_err:
                logger.warning(f"Cleanup after interrupt failed: {stop_err}")
            raise

    def _revert_to_snapshot(self):
        # Revert to certain snapshot of the virtual machine, and refresh the path to vm and ip of vm
        # due to the fact it could be changed when implemented by cloud services
        path_to_vm = self.provider.revert_to_snapshot(self.path_to_vm, self.snapshot_name)
        if path_to_vm and not path_to_vm == self.path_to_vm:
            # path_to_vm has to be a new path

            self.manager.delete_vm(self.path_to_vm, self.region)
            self.manager.add_vm(path_to_vm, self.region)
            self.manager.occupy_vm(path_to_vm, os.getpid(), self.region)
            self.path_to_vm = path_to_vm

    def _save_state(self, snapshot_name=None):
        self.provider.save_state(self.path_to_vm, snapshot_name)

    @staticmethod
    def _task_get(task_config: Any, key: str, default: Any = None) -> Any:
        if task_config is None:
            return default
        if hasattr(task_config, "get") and callable(getattr(task_config, "get")):
            try:
                return task_config.get(key, default)
            except TypeError:
                return task_config.get(key)
        return getattr(task_config, key, default)

    @staticmethod
    def _has_custom_setup(task_config: Any) -> bool:
        if isinstance(task_config, BaseTask):
            return task_config.__class__.setup is not BaseTask.setup
        return hasattr(task_config, "setup") and callable(getattr(task_config, "setup"))

    @staticmethod
    def _has_custom_evaluate(task_config: Any) -> bool:
        if isinstance(task_config, BaseTask):
            return task_config.__class__.evaluate is not BaseTask.evaluate
        return hasattr(task_config, "evaluate") and callable(getattr(task_config, "evaluate"))

    def _call_task_setup(self, task_config: Any, use_proxy: bool) -> None:
        setup_fn = task_config.setup
        try:
            sig = inspect.signature(setup_fn)
            params = sig.parameters
        except (TypeError, ValueError):
            setup_fn(self.setup_controller, use_proxy)
            return

        accepts_kwargs = any(p.kind == p.VAR_KEYWORD for p in params.values())
        if "use_proxy" in params or accepts_kwargs:
            setup_fn(self.setup_controller, use_proxy=use_proxy)
        else:
            setup_fn(self.setup_controller)

    def _call_task_evaluate(self, task_config: Any):
        eval_fn = task_config.evaluate
        try:
            sig = inspect.signature(eval_fn)
            params = sig.parameters
        except (TypeError, ValueError):
            result = eval_fn(self)
        else:
            result = eval_fn(self) if params else eval_fn()
        return result if isinstance(result, dict) else float(result)

    def _setup_task(self, task_config: Any, use_proxy: bool) -> Tuple[bool, bool]:
        if self._has_custom_setup(task_config):
            if not self.setup_controller.ensure_ready(use_proxy):
                return False, True
            try:
                self._call_task_setup(task_config, use_proxy)
            except Exception as e:
                raise EnvironmentSetupError(
                    f"Custom task setup failed: {e}"
                ) from e
            return True, True

        success = self.setup_controller.setup(self.config, use_proxy)
        return success, bool(self.config)

    def close(self):
        self.provider.stop_emulator(self.path_to_vm)

    @staticmethod
    def _validate_runtime_timezone_name(timezone_name: str) -> str:
        normalized = str(timezone_name or "").strip()
        if (
            not normalized
            or normalized.startswith("/")
            or not re.fullmatch(
                r"[A-Za-z0-9._+-]+(?:/[A-Za-z0-9._+-]+)*",
                normalized,
            )
            or any(part in {".", ".."} for part in normalized.split("/"))
        ):
            raise EnvironmentSetupError(
                f"Invalid runtime timezone name: {timezone_name!r}"
            )
        return normalized

    def _apply_runtime_timezone(self, timezone_name: str) -> None:
        timezone_name = self._validate_runtime_timezone_name(timezone_name)
        zone_path = f"/usr/share/zoneinfo/{timezone_name}"
        password = shlex.quote(self.client_password or "")
        quoted_timezone = shlex.quote(timezone_name)
        quoted_zone_path = shlex.quote(zone_path)
        script = f"""#!/usr/bin/env bash
set -euo pipefail

PASSWORD={password}
TIMEZONE={quoted_timezone}
ZONE_PATH={quoted_zone_path}

sudo_cmd() {{
  if [ "$(id -u)" -eq 0 ]; then
    "$@"
  elif sudo -n "$@" 2>/dev/null; then
    return 0
  else
    printf '%s\\n' "$PASSWORD" | sudo -S -p '' "$@"
  fi
}}

test -e "$ZONE_PATH"
tmp_timezone="$(mktemp)"
trap 'rm -f "$tmp_timezone"' EXIT
printf '%s\\n' "$TIMEZONE" >"$tmp_timezone"
sudo_cmd ln -snf "$ZONE_PATH" /etc/localtime
sudo_cmd install -m 0644 "$tmp_timezone" /etc/timezone
test "$(readlink -f /etc/localtime)" = "$(readlink -f "$ZONE_PATH")"
test "$(cat /etc/timezone)" = "$TIMEZONE"
"""
        try:
            self.setup_controller.execute(
                ["bash", "-lc", script],
                quiet=True,
                timeout=30,
            )
        except Exception as exc:
            raise EnvironmentSetupError(
                f"Failed to align guest runtime timezone to "
                f"{timezone_name}: {exc}"
            ) from exc

    def _apply_task_runtime_overrides(self, task_config: Any) -> None:
        runtime_timezone = str(
            self._task_get(task_config, "runtime_timezone", "") or ""
        ).strip()
        if runtime_timezone:
            logger.info(
                "Aligning guest runtime timezone to %s before task setup.",
                runtime_timezone,
            )
            self._apply_runtime_timezone(runtime_timezone)

        disable_vnc = bool(self._task_get(task_config, "disable_vnc", False)) or self.force_disable_vnc
        disable_recording = bool(self._task_get(task_config, "disable_recording", False)) or self.force_disable_recording

        self.controller.recording_enabled = not disable_recording

        if disable_vnc:
            logger.info("Task requested VNC shutdown; stopping noVNC/x11vnc for this session.")
            self.setup_controller.disable_remote_desktop()

        if disable_recording:
            logger.info("Task requested recording shutdown; cleaning up any leftover x11grab processes.")
            self.setup_controller.stop_recording_processes()

    def reset(self, task_config: Optional[Any] = None, seed=None, options=None) -> Dict[str, Any]:

        logger.info("Resetting environment...")
        logger.info("Switching task...")
        logger.info("Setting counters...")
        self._traj_no += 1
        self._step_no = 0
        self.action_history.clear()
        setup_succeeded = task_config is None
        last_setup_error: Exception | None = None
        last_failure_reason: str | None = None
        self.setup_attempts = []

        for attempt in range(MAX_RETRIES):
            # Only revert to snapshot if environment has been used (step/setup)
            # This optimization is especially important for cloud providers like AWS
            # where unnecessary snapshot operations are costly and time-consuming

            if task_config is not None:
                # Only consider task proxy requirement if proxy is enabled at system level
                task_proxy = self._task_get(task_config, "proxy", False)
                task_use_proxy = task_proxy and self.enable_proxy
                if not self.enable_proxy and task_proxy:
                    logger.info("Task requires proxy but proxy is disabled at system level, ignoring proxy requirement.")

                if task_use_proxy != self.current_use_proxy:
                    # keep because get_info_from_website depend on this
                    self.current_use_proxy = task_use_proxy

            if self.is_environment_used:
                logger.info("Environment has been used, reverting to snapshot {}...".format(self.snapshot_name))
                self._revert_to_snapshot()
                logger.info("Starting emulator...")
                self._start_emulator()
                logger.info("Emulator started.")
                self.is_environment_used = False
            else:
                logger.info("Environment is clean, skipping snapshot revert (provider: {}).".format(self.provider_name))

            if task_config is not None:
                # Mark the environment as dirty before task-specific setup work so
                # any failure on this path forces a snapshot revert on the next reset.
                self.is_environment_used = True
                use_proxy = self._task_get(task_config, "proxy", False) and self.enable_proxy
                if use_proxy:
                    proxy_ready = self.setup_controller._proxy_setup(self.client_password)
                    if proxy_ready is False:
                        last_failure_reason = "Proxy setup failed before applying task runtime overrides"
                        self.setup_attempts.append({
                            "attempt": attempt + 1,
                            "status": "failed",
                            "reason": last_failure_reason,
                        })
                        logger.error(
                            "Proxy setup failed before applying task runtime overrides, retrying (%d/%d)...",
                            attempt + 1,
                            MAX_RETRIES,
                        )
                        time.sleep(5)
                        continue
                elif not self.setup_controller.ensure_ready(use_proxy):
                    last_failure_reason = "Environment control server not ready before applying task runtime overrides"
                    self.setup_attempts.append({
                        "attempt": attempt + 1,
                        "status": "failed",
                        "reason": last_failure_reason,
                    })
                    logger.error(
                        "Environment control server not ready before applying task runtime overrides, retrying (%d/%d)...",
                        attempt + 1,
                        MAX_RETRIES,
                    )
                    time.sleep(5)
                    continue
                self._set_task_info(task_config)
                self.setup_controller.reset_cache_dir(self.cache_dir)
                self._apply_task_runtime_overrides(task_config)
                logger.info("Setting up environment...")
                before_task_setup = getattr(
                    self,
                    "before_task_setup",
                    None,
                )
                if before_task_setup is not None:
                    try:
                        before_task_setup(self.setup_controller)
                    except Exception as e:
                        last_setup_error = EnvironmentSetupError(
                            f"Pre-task bootstrap failed: {e}"
                        )
                        last_failure_reason = str(last_setup_error)
                        self.setup_attempts.append({
                            "attempt": attempt + 1,
                            "status": "failed",
                            "reason": last_failure_reason,
                        })
                        logger.error(
                            "Pre-task bootstrap failed, retrying "
                            "(%d/%d): %s",
                            attempt + 1,
                            MAX_RETRIES,
                            e,
                        )
                        time.sleep(5)
                        continue
                try:
                    success, used_setup = self._setup_task(task_config, use_proxy)
                except EnvironmentSetupError as e:
                    last_setup_error = e
                    last_failure_reason = str(e)
                    self.setup_attempts.append({
                        "attempt": attempt + 1,
                        "status": "failed",
                        "reason": last_failure_reason,
                    })
                    logger.error(
                        "Environment setup failed with exception, retrying (%d/%d): %s",
                        attempt + 1,
                        MAX_RETRIES,
                        e,
                    )
                    time.sleep(5)
                    continue
                if success:
                    self.setup_attempts.append({
                        "attempt": attempt + 1,
                        "status": "passed",
                        "reason": None,
                    })
                    setup_succeeded = True
                    break
                else:
                    last_failure_reason = "Environment setup returned unsuccessful status"
                    self.setup_attempts.append({
                        "attempt": attempt + 1,
                        "status": "failed",
                        "reason": last_failure_reason,
                    })
                    logger.error(
                        "Environment setup failed, retrying (%d/%d)...",
                        attempt + 1,
                        MAX_RETRIES,
                    )
                    time.sleep(5)
            else:
                setup_succeeded = True
                break

        if not setup_succeeded:
            message = last_failure_reason or "Environment setup failed after retries"
            if last_setup_error is not None:
                raise EnvironmentSetupError(message) from last_setup_error
            raise EnvironmentSetupError(message)

        logger.info("Environment setup complete.")

        # On Windows, hide the Flask server console window so the agent
        # cannot accidentally close it and kill the VM control channel.
        if self.os_type == "Windows":
            self._hide_flask_server_window()

        observation = self._get_obs()
        return observation

    def _hide_flask_server_window(self) -> None:
        """Hide the Flask server console window on Windows VMs.

        The server runs inside a cmd/conhost window titled "main" or
        "main - Shortcut".  We enumerate all visible top-level windows
        and hide any whose title matches.
        """
        ps_script = (
            "Add-Type @'\n"
            "using System; using System.Runtime.InteropServices; using System.Text;\n"
            "public class WinApi {\n"
            "  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);\n"
            "  [DllImport(\"user32.dll\")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);\n"
            "  [DllImport(\"user32.dll\")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int count);\n"
            "  [DllImport(\"user32.dll\")] public static extern bool IsWindowVisible(IntPtr hWnd);\n"
            "  [DllImport(\"user32.dll\")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);\n"
            "}\n"
            "'@;\n"
            "[WinApi]::EnumWindows({\n"
            "  param($h,$l)\n"
            "  $sb = New-Object Text.StringBuilder 256\n"
            "  [WinApi]::GetWindowText($h,$sb,256) | Out-Null\n"
            "  $t = $sb.ToString()\n"
            "  if ($t -match '^main(\\s*-\\s*Shortcut)?$' -and [WinApi]::IsWindowVisible($h)) {\n"
            "    [WinApi]::ShowWindow($h, 0) | Out-Null\n"
            "  }\n"
            "  return $true\n"
            "}, [IntPtr]::Zero)"
        )
        try:
            self.setup_controller.execute(["powershell", "-Command", ps_script])
            logger.info("Flask server window hidden on Windows VM.")
        except Exception as e:
            logger.warning("Failed to hide Flask server window: %s", e)

    def _get_obs(self):
        # We provide screenshot, accessibility_tree (optional), terminal (optional), and instruction.
        # can be customized and scaled
        return {
            "screenshot": self.controller.get_screenshot(),
            "accessibility_tree": self.controller.get_accessibility_tree() if self.require_a11y_tree else None,
            "terminal": self.controller.get_terminal_output() if self.require_terminal else None,
            "instruction": self.instruction
        }

    @property
    def vm_platform(self):
        return self.controller.get_vm_platform()

    @property
    def vm_screen_size(self):
        return self.controller.get_vm_screen_size()

    def _set_task_info(self, task_config: Any):
        """Set task info (proxy logic is handled in reset method)"""
        self.task_config = task_config
        self.task_id: str = self._task_get(task_config, "id")
        self.cache_dir: str = os.path.join(self.cache_dir_base, self.task_id)
        os.makedirs(self.cache_dir, exist_ok=True)
        self.instruction = self._task_get(task_config, "instruction")
        self.config = self._task_get(task_config, "config", []) or []

        user_sim_config = self._task_get(task_config, "user_simulator", None)
        if user_sim_config:
            from desktop_env.user_simulator import create_user_simulator
            self.user_simulator = create_user_simulator(user_sim_config)
            self.user_simulator.reset(instruction=self.instruction)
        else:
            self.user_simulator = None

        evaluator = self._task_get(task_config, "evaluator", None)
        if evaluator:
            self._set_evaluator_info(evaluator)
        else:
            self.evaluator = None
            self.metric = None
            self.metric_conj = "and"
            self.result_getter = None
            self.expected_getter = None
            self.metric_options = {}

    def _set_evaluator_info(self, evaluator: Dict[str, Any]):
        """Set evaluator information from evaluator config"""
        # evaluator dict
        # func -> metric function string, or list of metric function strings
        # conj -> conjunction of multiple metrics if func is a list with length > 1, "and"/"or"
        # result -> result getter config, or list of result getter configs
        # expected (optional) -> expected getter config, or list of expected getter configs
        # options (optional) -> metric options, or list of metric options
        # if func is a str list, then result, expected (if exists), options (if exists) should also be lists of the same length
        # even if one of the metrics does not need expected or options field, it should be included in the list with None
        self.evaluator = evaluator
        func = self.evaluator["func"]
        if isinstance(func, list):
            self.metric = [f if callable(f) else getattr(metrics, f) for f in func]
        else:
            self.metric = func if callable(func) else getattr(metrics, func)
        self.metric_conj = self.evaluator.get("conj", "and")  # take conjunction of multiple metrics
        if "result" in self.evaluator and len(self.evaluator["result"]) > 0:
            self.result_getter = [getattr(getters, "get_{:}".format(res["type"])) for res in
                                  self.evaluator["result"]] \
                if isinstance(self.evaluator["result"], list) \
                else getattr(getters, "get_{:}".format(self.evaluator["result"]["type"]))
        else:
            self.result_getter = [None] * len(self.metric) \
                if isinstance(self.metric, list) \
                else None

        if "expected" in self.evaluator and len(self.evaluator["expected"]) > 0:
            self.expected_getter = [getattr(getters, "get_{:}".format(exp["type"])) if exp else None for exp in
                                    self.evaluator["expected"]] \
                if isinstance(self.evaluator["expected"], list) \
                else getattr(getters, "get_{:}".format(self.evaluator["expected"]["type"]))
        else:
            self.expected_getter = [None] * len(self.metric) \
                if isinstance(self.metric, list) \
                else None
        self.metric_options: Union[List[Dict[str, Any]], Dict[str, Any]] = [opt if opt else {} for opt in
                                                                            self.evaluator["options"]] \
            if isinstance(self.evaluator.get("options", {}), list) \
            else self.evaluator["options"] \
            if "options" in self.evaluator \
            else [{}] * len(self.metric) \
            if isinstance(self.metric, list) \
            else {}

        assert (not isinstance(self.evaluator["func"], list)
                or (len(self.metric) == len(self.result_getter) == len(self.expected_getter) == len(
                    self.metric_options)))

    def step(self, action, pause=2):
        self._step_no += 1
        self.action_history.append(action)

        self.is_environment_used = True

        reward = 0
        done = False
        info = {}
        logger.info(f"Step {self._step_no} in trajectory {self._traj_no} with action: {action}")
        if action in ['WAIT', 'FAIL', 'DONE'] or (type(action) == dict and action['action_type'] in ['WAIT', 'FAIL', 'DONE']):
            if action == 'WAIT' or (type(action) == dict and action.get('action_type') == 'WAIT'):
                time.sleep(pause)
            elif action == 'FAIL' or (type(action) == dict and action.get('action_type') == 'FAIL'):
                done = True
                info = {"fail": True}
            elif action == 'DONE' or (type(action) == dict and action.get('action_type') == 'DONE'):
                done = True
                info = {"done": True}

        if self.action_space == "computer_13":
            # the set of all possible actions defined in the action representation
            self.controller.execute_action(action)
        elif self.action_space == "pyautogui" or self.action_space == "claude_computer_use":
            if action in ['WAIT', 'FAIL', 'DONE'] or (type(action) == dict and action.get('action_type') in ['WAIT', 'FAIL', 'DONE']):
                self.controller.execute_action(action)
            else:
                # the set of all possible python commands insides `pyautogui`
                if type(action) == str:
                    self.controller.execute_python_command(action)
                elif type(action) == dict:
                    self.controller.execute_python_command(action['command'])

        time.sleep(pause)
        observation = self._get_obs()

        return observation, reward, done, info

    def evaluate(self):
        if self.task_config is not None and self._has_custom_evaluate(self.task_config):
            return self._call_task_evaluate(self.task_config)
        return self._evaluate_with_evaluator()

    def _evaluate_with_evaluator(self):
        """
        Evaluate whether the task is successfully completed.
        """

        if not self.evaluator:
            logger.error("No evaluator configured for task %s", self.task_id)
            return 0.0

        postconfig = self.evaluator.get("postconfig", [])
        self.setup_controller.setup(postconfig, self.enable_proxy)
        if postconfig:
            self.is_environment_used = True

        if self.evaluator['func'] == "infeasible":
            if len(self.action_history) > 0:
                last_action = self.action_history[-1]
                if last_action == "FAIL" or (type(last_action) == dict and last_action.get('action_type') == 'FAIL'):
                    return 1
            return 0
        else:
            if len(self.action_history) > 0:
                last_action = self.action_history[-1]
                if last_action == "FAIL" or (type(last_action) == dict and last_action.get('action_type') == 'FAIL'):
                    return 0

        if type(self.metric) == list:
            results = []
            assert len(self.metric) == len(self.result_getter), "The number of metrics and result getters must be the same"
            if "expected" in self.evaluator:
                assert len(self.metric) == len(self.expected_getter), "The number of metrics and expected getters must be the same"

            # By default, historical behavior short-circuits for conj="and"/"or".
            # For fine-grained rewards across multiple checkpoints, set:
            #   - conj: "avg" (average of metrics) OR conj: "sum" (sum of metrics, clamped to [0,1])
            #   - or set short_circuit=false to disable early exits even for conj="and"/"or"
            short_circuit = bool(self.evaluator.get("short_circuit", True))

            for idx, metric in enumerate(self.metric):
                result_state = None
                if self.result_getter[idx] is not None:
                    try:
                        config = self.evaluator["result"][idx]
                        result_state = self.result_getter[idx](self, config)
                    except FileNotFoundError:
                        logger.error("File not found!")
                        # In fine-grained settings, missing files should contribute 0 without crashing/short-circuiting.
                        if self.metric_conj == 'and' and short_circuit:
                            return 0
                        result_state = None

                try:
                    if "expected" in self.evaluator and self.expected_getter and self.evaluator["expected"]:
                        expected_state = self.expected_getter[idx](self, self.evaluator["expected"][idx])
                        metric_val: float = float(metric(result_state, expected_state, **self.metric_options[idx]))
                    else:
                        metric_val: float = float(metric(result_state, **self.metric_options[idx]))
                except Exception as e:
                    logger.error("Metric %s failed with error: %s", getattr(metric, "__name__", str(metric)), e)
                    metric_val = 0.0

                if short_circuit:
                    if self.metric_conj == 'and' and metric_val == 0.0:
                        return 0.0
                    if self.metric_conj == 'or' and metric_val == 1.0:
                        return 1.0

                results.append(metric_val)

            if not results:
                return 0.0

            if self.metric_conj in ('and', 'avg'):
                return sum(results) / len(results)
            if self.metric_conj == 'or':
                return max(results)
            if self.metric_conj == 'sum':
                return min(1.0, sum(results))

            return sum(results) / len(results)
        else:
            try:
                result_state = self.result_getter(self, self.evaluator["result"])
            except FileNotFoundError:
                logger.error("File not found!")
                return 0

            try:
                if "expected" in self.evaluator and self.expected_getter and self.evaluator["expected"]:
                    expected_state = self.expected_getter(self, self.evaluator["expected"])
                    metric: float = float(self.metric(result_state, expected_state, **self.metric_options))
                else:
                    metric: float = float(self.metric(result_state, **self.metric_options))
            except Exception as e:
                logger.error("Metric %s failed with error: %s", getattr(self.metric, "__name__", str(self.metric)), e)
                return 0.0

        return metric

    def render(self, mode='rgb_array'):
        if mode == 'rgb_array':
            return self.controller.get_screenshot()
        else:
            raise ValueError('Unsupported render mode: {}'.format(mode))
