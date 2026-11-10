# SPDX-License-Identifier: Apache-2.0
# Modified by the Hybrid Computer Use authors from desktop_env/providers/docker/provider.py
# in xlang-ai/OSWorld-V2 (revision d578d2d4). See THIRD_PARTY_NOTICES.md.

import logging
import fcntl
import json
import os
from pathlib import Path
import subprocess
import time
import uuid
import docker
import requests

from desktop_env.providers.base import Provider
from desktop_env.providers.volume import (
    expand_guest_volume,
    normalize_volume_size,
    validate_guest_image_capacity,
)
logger = logging.getLogger("desktopenv.providers.docker.DockerProvider")
logger.setLevel(logging.INFO)

WAIT_TIME = 3
RETRY_INTERVAL = 1
VM_READY_TIMEOUT = int(
    os.getenv("OSWORLD_VM_READY_TIMEOUT_SECONDS", "600")
)
VM_START_ATTEMPTS = int(
    os.getenv("OSWORLD_VM_START_ATTEMPTS", "2")
)
CONTAINER_CREATE_ATTEMPTS = int(
    os.getenv("OSWORLD_CONTAINER_CREATE_ATTEMPTS", "5")
)
DOCKER_API_TIMEOUT = 300
CONTAINER_RECONCILE_TIMEOUT = 60
DEFAULT_CLEANUP_LOCK = (
    os.getenv(
        "OSWORLD_CONTAINER_CLEANUP_LOCK",
        "/tmp/osworld-v2/container-cleanup.lock",
    )
)
DEFAULT_CLEANUP_LOCK_TIMEOUT = 1800.0
DEFAULT_CREATE_LOCK = (
    os.getenv(
        "OSWORLD_CONTAINER_CREATE_LOCK",
        "/tmp/osworld-v2/container-create.lock",
    )
)
DEFAULT_CREATE_LOCK_TIMEOUT = 300.0


class ContainerCleanupDeferredError(RuntimeError):
    pass


class VMReadinessError(TimeoutError):
    def __init__(self, diagnostic: dict):
        self.diagnostic = diagnostic
        super().__init__(
            "VM failed to become ready: "
            + json.dumps(diagnostic, ensure_ascii=False, sort_keys=True)
        )


class DockerNetworkConflictError(RuntimeError):
    pass


class DockerProvider(Provider):
    def __init__(self, region: str):
        super().__init__(region)
        self.client = docker.from_env(timeout=DOCKER_API_TIMEOUT)
        self.server_port = None
        self.vnc_port = None
        self.chromium_port = None
        self.vlc_port = None
        self.container = None
        self.last_cleanup_status = None
        self.volume_expansion_required = False
        self.startup_attempts = []
        self.environment = {
            "DISK_SIZE": "32G",
            "RAM_SIZE": os.getenv("OSWORLD_DOCKER_RAM_SIZE", "4G"),
            "CPU_CORES": os.getenv("OSWORLD_DOCKER_CPU_CORES", "4"),
        }
        cpu_model = os.getenv("OSWORLD_QEMU_CPU_MODEL", "").strip()
        if cpu_model:
            self.environment["CPU_MODEL"] = cpu_model
        if os.getenv("OSWORLD_DISABLE_KVM", "").strip().lower() in {
            "1",
            "true",
            "yes",
        }:
            self.environment["KVM"] = "N"

    def _assert_default_bridge_route_unique(self) -> None:
        network = self.client.networks.get("bridge")
        ipam = network.attrs.get("IPAM", {}).get("Config", [])
        subnets = [
            str(config.get("Subnet", "")).strip()
            for config in ipam
            if str(config.get("Subnet", "")).strip()
        ]
        if not subnets:
            raise DockerNetworkConflictError(
                "Docker default bridge has no inspectable IPAM subnet"
            )

        conflicts = {}
        for subnet in subnets:
            result = subprocess.run(
                ["ip", "-o", "route", "show", "exact", subnet],
                check=True,
                capture_output=True,
                text=True,
                timeout=10,
            )
            routes = [
                line.strip()
                for line in result.stdout.splitlines()
                if line.strip()
            ]
            devices = set()
            for route in routes:
                fields = route.split()
                if "dev" in fields:
                    index = fields.index("dev")
                    if index + 1 < len(fields):
                        devices.add(fields[index + 1])
            if len(devices) > 1:
                conflicts[subnet] = {
                    "devices": sorted(devices),
                    "routes": routes,
                }
        if conflicts:
            raise DockerNetworkConflictError(
                "Docker bridge subnet has conflicting host routes: "
                + json.dumps(conflicts, sort_keys=True)
                + ". Remove the stale route or point DOCKER_HOST at a daemon whose bridge does not collide."
            )

    def prepare_volume(self, path_to_vm: str, volume_size: int | None, os_type: str):
        requested_size = normalize_volume_size(volume_size)
        if requested_size is None:
            return
        source_size = validate_guest_image_capacity(
            path_to_vm,
            requested_size,
        )
        self.volume_size = requested_size
        self.image_virtual_size_gib = source_size
        # The qcow virtual size does not prove that the guest partition and
        # filesystem consume that space. The official image is a 50G qcow with
        # a roughly 29G root filesystem, so explicit sizing must always run the
        # idempotent in-guest growth step.
        self.volume_expansion_required = True
        logger.info(
            "Configured Docker root target size: %sG (image capacity: %sG)",
            requested_size,
            source_size,
        )

    def finalize_volume(
            self,
            path_to_vm: str,
            volume_size: int | None,
            os_type: str,
            controller,
            setup_controller,
            client_password: str,
    ):
        if normalize_volume_size(volume_size) is None:
            return
        if not self.volume_expansion_required:
            logger.info(
                "Skipping guest volume expansion because source image already "
                "satisfies requested size"
            )
            return
        expand_guest_volume(
            os_type=os_type,
            controller=controller,
            setup_controller=setup_controller,
            client_password=client_password,
            expected_size_gib=self.volume_size,
        )

    def _wait_for_vm_ready(self, timeout: int = VM_READY_TIMEOUT):
        """Wait for VM to be ready by checking screenshot endpoint."""
        start_time = time.monotonic()
        last_probe = "not attempted"

        def check_screenshot():
            nonlocal last_probe
            try:
                response = requests.get(
                    f"http://localhost:{self.server_port}/screenshot",
                    timeout=(10, 10)
                )
                last_probe = (
                    f"HTTP {response.status_code}, "
                    f"{len(response.content)} bytes"
                )
                return response.status_code == 200
            except Exception as exc:
                last_probe = f"{type(exc).__name__}: {exc}"
                return False

        while time.monotonic() - start_time < timeout:
            if check_screenshot():
                return True
            logger.info("Checking if virtual machine is ready...")
            time.sleep(RETRY_INTERVAL)

        diagnostic = {
            "timeout_seconds": timeout,
            "elapsed_seconds": round(time.monotonic() - start_time, 1),
            "last_screenshot_probe": last_probe,
            "ports": {
                "server": self.server_port,
                "vnc": self.vnc_port,
                "chromium": self.chromium_port,
                "vlc": self.vlc_port,
            },
        }
        if self.container is not None:
            try:
                self.container.reload()
                diagnostic["container_id"] = self.container.id
                diagnostic["container_name"] = self.container.name
                diagnostic["container_status"] = self.container.status
                diagnostic["container_state"] = self.container.attrs.get(
                    "State"
                )
            except Exception as exc:
                diagnostic["container_inspect_error"] = (
                    f"{type(exc).__name__}: {exc}"
                )
            try:
                logs = self.container.logs(tail=300)
                diagnostic["container_logs_tail"] = logs.decode(
                    "utf-8",
                    errors="replace",
                )[-12000:]
            except Exception as exc:
                diagnostic["container_logs_error"] = (
                    f"{type(exc).__name__}: {exc}"
                )
        raise VMReadinessError(diagnostic)

    def _recover_timed_out_container(self, name: str, original_error):
        deadline = time.monotonic() + CONTAINER_RECONCILE_TIMEOUT
        last_error = original_error
        while time.monotonic() < deadline:
            try:
                container = self.client.containers.get(name)
                container.reload()
                if container.status == "created":
                    try:
                        container.start()
                    except requests.exceptions.ReadTimeout as exc:
                        last_error = exc
                    container.reload()
                if container.status in {"running", "restarting"}:
                    logger.warning(
                        "Recovered Docker container %s after API timeout",
                        name,
                    )
                    return container
                if container.status in {"exited", "dead"}:
                    raise RuntimeError(
                        f"Container {name} reached status "
                        f"{container.status} after Docker API timeout"
                    )
            except docker.errors.NotFound:
                pass
            except requests.exceptions.ReadTimeout as exc:
                last_error = exc
            time.sleep(RETRY_INTERVAL)
        raise last_error

    def _refresh_allocated_ports(self) -> None:
        self.container.reload()
        mappings = self.container.attrs["NetworkSettings"]["Ports"]

        def host_port(container_port: int) -> int:
            bindings = mappings.get(f"{container_port}/tcp")
            if not bindings:
                raise RuntimeError(
                    f"Docker did not publish container port {container_port}"
                )
            return int(bindings[0]["HostPort"])

        self.vnc_port = host_port(8006)
        self.server_port = host_port(5000)
        self.chromium_port = host_port(9222)
        self.vlc_port = host_port(8080)

    @staticmethod
    def _is_host_port_conflict(exc: Exception) -> bool:
        message = str(getattr(exc, "explanation", "") or exc).casefold()
        return (
            "address already in use" in message
            or "port is already allocated" in message
            or "failed to bind host port" in message
        )

    def _remove_failed_created_container(self, container_name: str) -> None:
        try:
            container = self.client.containers.get(container_name)
            container.reload()
            if container.status not in {"running", "restarting"}:
                container.remove(force=True)
        except docker.errors.NotFound:
            return

    def _create_container(self, path_to_vm: str, devices: list[str]):
        lock_path = Path(
            os.getenv("OSWORLD_CONTAINER_CREATE_LOCK", DEFAULT_CREATE_LOCK)
        )
        lock_path.parent.mkdir(parents=True, exist_ok=True)
        lock_timeout = float(
            os.getenv(
                "OSWORLD_CONTAINER_CREATE_LOCK_TIMEOUT_SECONDS",
                str(DEFAULT_CREATE_LOCK_TIMEOUT),
            )
        )
        started = time.monotonic()
        with lock_path.open("a+", encoding="utf-8") as lock_handle:
            while True:
                try:
                    fcntl.flock(
                        lock_handle.fileno(),
                        fcntl.LOCK_EX | fcntl.LOCK_NB,
                    )
                    break
                except BlockingIOError:
                    if time.monotonic() - started >= lock_timeout:
                        raise TimeoutError(
                            "Timed out waiting for the OSWorld container "
                            "creation lock"
                        )
                    time.sleep(RETRY_INTERVAL)
            try:
                for attempt in range(1, CONTAINER_CREATE_ATTEMPTS + 1):
                    container_name = f"osworld-{uuid.uuid4().hex}"
                    try:
                        return self.client.containers.run(
                            "happysixd/osworld-docker",
                            name=container_name,
                            environment=self.environment,
                            cap_add=["NET_ADMIN"],
                            devices=devices,
                            volumes={
                                os.path.abspath(path_to_vm): {
                                    "bind": "/System.qcow2",
                                    "mode": "ro",
                                }
                            },
                            ports={
                                8006: None,
                                5000: None,
                                9222: None,
                                8080: None,
                            },
                            detach=True,
                        )
                    except requests.exceptions.ReadTimeout as exc:
                        return self._recover_timed_out_container(
                            container_name,
                            exc,
                        )
                    except docker.errors.APIError as exc:
                        if (
                            not self._is_host_port_conflict(exc)
                            or attempt == CONTAINER_CREATE_ATTEMPTS
                        ):
                            raise
                        self._remove_failed_created_container(container_name)
                        logger.warning(
                            "Docker host-port allocation conflict on attempt "
                            "%d/%d; retrying with a fresh container",
                            attempt,
                            CONTAINER_CREATE_ATTEMPTS,
                        )
                        time.sleep(RETRY_INTERVAL)
            finally:
                fcntl.flock(lock_handle.fileno(), fcntl.LOCK_UN)

    def _remove_current_container(self) -> None:
        container = self.container
        if container is None:
            return
        container_id = getattr(container, "id", None)
        container_name = getattr(container, "name", None)
        lock_path = Path(
            os.getenv("OSWORLD_CONTAINER_CLEANUP_LOCK", DEFAULT_CLEANUP_LOCK)
        )
        lock_path.parent.mkdir(parents=True, exist_ok=True)
        lock_timeout = float(
            os.getenv(
                "OSWORLD_CONTAINER_CLEANUP_LOCK_TIMEOUT_SECONDS",
                str(DEFAULT_CLEANUP_LOCK_TIMEOUT),
            )
        )
        started = time.monotonic()

        with lock_path.open("a+", encoding="utf-8") as lock_handle:
            while True:
                try:
                    fcntl.flock(
                        lock_handle.fileno(),
                        fcntl.LOCK_EX | fcntl.LOCK_NB,
                    )
                    break
                except BlockingIOError:
                    if time.monotonic() - started >= lock_timeout:
                        self.last_cleanup_status = {
                            "state": "deferred",
                            "reason": "cleanup_lock_timeout",
                            "container_id": container_id,
                            "container_name": container_name,
                            "wait_seconds": round(
                                time.monotonic() - started,
                                1,
                            ),
                        }
                        raise ContainerCleanupDeferredError(
                            "Container cleanup lock timed out; cleanup deferred"
                        )
                    time.sleep(1)

            try:
                try:
                    container.stop()
                except docker.errors.NotFound:
                    pass
                try:
                    container.remove()
                except docker.errors.NotFound:
                    pass
                self.container = None
                self.last_cleanup_status = {
                    "state": "completed",
                    "container_id": container_id,
                    "container_name": container_name,
                    "duration_seconds": round(
                        time.monotonic() - started,
                        1,
                    ),
                }
            except Exception as exc:
                self.last_cleanup_status = {
                    "state": "failed",
                    "container_id": container_id,
                    "container_name": container_name,
                    "error": f"{type(exc).__name__}: {exc}",
                    "duration_seconds": round(
                        time.monotonic() - started,
                        1,
                    ),
                }
                raise
            finally:
                fcntl.flock(lock_handle.fileno(), fcntl.LOCK_UN)

    def start_emulator(self, path_to_vm: str, headless: bool, os_type: str):
        self._assert_default_bridge_route_unique()
        devices = []
        if (
            os.path.exists("/dev/kvm")
            and self.environment.get("KVM") != "N"
        ):
            devices.append("/dev/kvm")
            logger.info("KVM device found, using hardware acceleration")
        else:
            self.environment["KVM"] = "N"
            logger.warning(
                "KVM device not found, running without hardware acceleration "
                "(will be slower)"
            )

        self.startup_attempts = []
        for attempt in range(1, VM_START_ATTEMPTS + 1):
            try:
                self.container = self._create_container(path_to_vm, devices)
                self._refresh_allocated_ports()
                logger.info(
                    "Started container with ports - VNC: %s, Server: %s, "
                    "Chrome: %s, VLC: %s",
                    self.vnc_port,
                    self.server_port,
                    self.chromium_port,
                    self.vlc_port,
                )
                self._wait_for_vm_ready()
                self.startup_attempts.append({
                    "attempt": attempt,
                    "status": "ready",
                    "container_id": self.container.id,
                    "container_name": self.container.name,
                })
                return
            except VMReadinessError as exc:
                self.startup_attempts.append({
                    "attempt": attempt,
                    "status": "readiness_timeout",
                    "diagnostic": exc.diagnostic,
                })
                self._remove_current_container()
                if attempt == VM_START_ATTEMPTS:
                    raise VMReadinessError({
                        "attempts": self.startup_attempts,
                    }) from exc
                logger.warning(
                    "VM readiness attempt %d/%d failed; starting one fresh VM",
                    attempt,
                    VM_START_ATTEMPTS,
                )
                time.sleep(WAIT_TIME)
            except Exception:
                self._remove_current_container()
                raise

    def get_ip_address(self, path_to_vm: str) -> str:
        if not all([self.server_port, self.chromium_port, self.vnc_port, self.vlc_port]):
            raise RuntimeError("VM not started - ports not allocated")
        return f"localhost:{self.server_port}:{self.chromium_port}:{self.vnc_port}:{self.vlc_port}"

    def get_service_ip_address(self) -> str:
        if self.container is None:
            raise RuntimeError("VM not started - container unavailable")
        self.container.reload()
        networks = self.container.attrs["NetworkSettings"]["Networks"]
        for network in networks.values():
            address = str(network.get("IPAddress") or "").strip()
            if address:
                return address
        raise RuntimeError("Docker container has no reachable bridge address")

    def save_state(self, path_to_vm: str, snapshot_name: str):
        raise NotImplementedError("Snapshots not available for Docker provider")

    def revert_to_snapshot(self, path_to_vm: str, snapshot_name: str):
        self.stop_emulator(path_to_vm)

    def stop_emulator(self, path_to_vm: str, region=None, *args, **kwargs):
        # Note: region parameter is ignored for Docker provider
        # but kept for interface consistency with other providers
        if self.container:
            logger.info("Stopping VM...")
            try:
                self._remove_current_container()
                time.sleep(WAIT_TIME)
            except Exception as e:
                logger.error(f"Error stopping container: {e}")
            finally:
                self.server_port = None
                self.vnc_port = None
                self.chromium_port = None
                self.vlc_port = None
