"""Host checks that run before a task guest is created."""

from __future__ import annotations

import os

import docker


def docker_runtime() -> dict:
    """Describe the Docker daemon the provider will use.

    ``docker.from_env`` honours ``DOCKER_HOST`` and otherwise uses the default
    socket, exactly as the OSWorld provider does.
    """
    client = docker.from_env(timeout=30)
    try:
        info = client.info()
        bridge = client.networks.get("bridge").attrs.get("IPAM", {}).get("Config", [])
    finally:
        client.close()
    return {
        "docker_host": os.environ.get("DOCKER_HOST") or "default",
        "docker_root": info.get("DockerRootDir"),
        "server_version": info.get("ServerVersion"),
        "bridge_gateways": [entry.get("Gateway") for entry in bridge if entry.get("Gateway")],
    }
