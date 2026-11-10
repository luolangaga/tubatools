# SPDX-License-Identifier: Apache-2.0
# Modified by the Hybrid Computer Use authors from desktop_env/providers/volume.py
# in xlang-ai/OSWorld-V2 (revision d578d2d4). See THIRD_PARTY_NOTICES.md.

from __future__ import annotations

import json
import logging
import math
from pathlib import Path
import shlex
import subprocess
import time
from typing import Any

import requests


logger = logging.getLogger("desktopenv.providers.volume")


def qemu_image_virtual_size_gib(path: str | Path) -> int:
    image = Path(path).resolve()
    if not image.is_file():
        raise FileNotFoundError(image)
    try:
        result = subprocess.run(
            ["qemu-img", "info", "--output=json", str(image)],
            check=True,
            capture_output=True,
            text=True,
            timeout=30,
        )
        virtual_size = int(json.loads(result.stdout)["virtual-size"])
    except (
        FileNotFoundError,
        KeyError,
        OSError,
        subprocess.SubprocessError,
        TypeError,
        ValueError,
        json.JSONDecodeError,
    ) as exc:
        raise RuntimeError(
            f"Could not inspect guest image virtual size for {image}: {exc}"
        ) from exc
    if virtual_size <= 0:
        raise RuntimeError(
            f"Guest image reports an invalid virtual size: {virtual_size}"
        )
    return math.ceil(virtual_size / (1024 ** 3))


def validate_guest_image_capacity(
    path: str | Path,
    required_size_gib: int | str | None,
) -> int:
    required = normalize_volume_size(required_size_gib)
    capacity = qemu_image_virtual_size_gib(path)
    if required is not None and required > capacity:
        raise ValueError(
            "Guest image virtual capacity is smaller than the requested root "
            f"volume: requested={required}GiB image={capacity}GiB "
            f"path={Path(path).resolve()}"
        )
    return capacity


def normalize_volume_size(volume_size: int | str | None) -> int | None:
    if volume_size is None:
        return None
    requested_size = int(volume_size)
    if requested_size <= 0:
        raise ValueError("volume_size must be positive")
    return requested_size


def expand_guest_volume(
        *,
        os_type: str,
        controller: Any,
        setup_controller: Any,
        client_password: str,
        expected_size_gib: int | None = None,
        timeout: int = 600,
) -> None:
    os_type_norm = (os_type or "Ubuntu").strip().lower()
    if os_type_norm == "windows":
        _expand_windows_guest_volume(setup_controller, timeout=timeout)
        return
    _expand_linux_guest_volume(
        setup_controller,
        client_password=client_password,
        expected_size_gib=expected_size_gib,
        timeout=timeout,
    )


def _run_setup_command(setup_controller: Any, command: list[str], *, timeout: int) -> dict[str, Any]:
    payload = json.dumps({"command": command, "shell": False, "timeout": timeout})
    headers = {"Content-Type": "application/json"}
    last_error = ""
    for attempt in range(3):
        try:
            response = requests.post(
                setup_controller.http_server + "/setup/execute",
                headers=headers,
                data=payload,
                timeout=timeout + 30,
            )
            if response.status_code == 200:
                result = response.json()
                if int(result.get("returncode", -1)) == 0:
                    return result
                raise RuntimeError(
                    f"returncode={result.get('returncode')} "
                    f"stdout={result.get('output', '')!r} stderr={result.get('error', '')!r}"
                )
            last_error = f"status={response.status_code} response={response.text}"
        except Exception as exc:
            last_error = str(exc)
        if attempt < 2:
            time.sleep(5)
    raise RuntimeError(f"Setup command failed after retries: {last_error}")


def _expand_linux_guest_volume(
        setup_controller: Any,
        *,
        client_password: str,
        expected_size_gib: int | None,
        timeout: int,
) -> None:
    password = shlex.quote(client_password or "")
    expected_size = int(expected_size_gib or 0)
    script = f"""#!/usr/bin/env bash
set -euo pipefail

PASSWORD={password}
EXPECTED_SIZE_GIB={expected_size}
sudo_cmd() {{
  if [ "$(id -u)" -eq 0 ]; then
    "$@"
  else
    sudo -n "$@" 2>/dev/null && return 0
    for candidate in "$PASSWORD" "osworld-public-evaluation" "password"; do
      [ -n "$candidate" ] || continue
      printf '%s\\n' "$candidate" | sudo -S -p '' "$@" && return 0
      sudo -k >/dev/null 2>&1 || true
    done
    return 1
  fi
}}

ROOT_SOURCE="$(findmnt -no SOURCE / || true)"
ROOT_FSTYPE="$(findmnt -no FSTYPE / || true)"
if [ -z "$ROOT_SOURCE" ]; then
  echo "Could not determine root filesystem source" >&2
  exit 1
fi

ROOT_DEVICE="$(readlink -f "$ROOT_SOURCE" || printf '%s' "$ROOT_SOURCE")"
if [ ! -b "$ROOT_DEVICE" ]; then
  echo "Root source is not a block device: $ROOT_SOURCE" >&2
  exit 0
fi

if ! command -v growpart >/dev/null 2>&1; then
  sudo_cmd apt-get update -qq
  sudo_cmd env DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends cloud-guest-utils parted
fi

ROOT_DISK="$(lsblk -ndo PKNAME "$ROOT_DEVICE" | head -n 1 || true)"
ROOT_PART=""
ROOT_DEVICE_NAME="$(basename "$ROOT_DEVICE")"
if [ -r "/sys/class/block/$ROOT_DEVICE_NAME/partition" ]; then
  ROOT_PART="$(cat "/sys/class/block/$ROOT_DEVICE_NAME/partition" || true)"
fi
if [ -z "$ROOT_PART" ]; then
  ROOT_PART="$(lsblk -ndo PARTN "$ROOT_DEVICE" 2>/dev/null | head -n 1 || true)"
fi
if [ -z "$ROOT_PART" ]; then
  ROOT_PART="$(printf '%s' "$ROOT_DEVICE_NAME" | sed -n 's/^.*[^0-9]\\([0-9][0-9]*\\)$/\\1/p')"
fi
if [ -n "$ROOT_DISK" ] && [ -n "$ROOT_PART" ] && command -v growpart >/dev/null 2>&1; then
  sudo_cmd growpart "/dev/$ROOT_DISK" "$ROOT_PART" || true
  sudo_cmd partprobe "/dev/$ROOT_DISK" || true
  sudo_cmd udevadm settle || true
fi

case "$ROOT_FSTYPE" in
  ext2|ext3|ext4)
    sudo_cmd resize2fs "$ROOT_DEVICE" || true
    ;;
  xfs)
    sudo_cmd xfs_growfs / || true
    ;;
  btrfs)
    sudo_cmd btrfs filesystem resize max / || true
    ;;
  *)
    echo "No filesystem resize handler for root fstype: $ROOT_FSTYPE" >&2
    ;;
esac

ROOT_SIZE_BYTES="$(findmnt -bno SIZE /)"
if [ "$EXPECTED_SIZE_GIB" -gt 0 ]; then
  # The virtual disk includes the partition table, EFI/swap partitions and
  # filesystem metadata. Verify the requested disk class with bounded 3 GiB
  # overhead instead of treating virtual disk bytes as root filesystem bytes.
  MIN_SIZE_BYTES=$(( (EXPECTED_SIZE_GIB - 3) * 1024 * 1024 * 1024 ))
  if [ "$ROOT_SIZE_BYTES" -lt "$MIN_SIZE_BYTES" ]; then
    echo "Root filesystem expansion verification failed: size=$ROOT_SIZE_BYTES expected_at_least=$MIN_SIZE_BYTES" >&2
    exit 88
  fi
fi

df -BG /
lsblk -o NAME,SIZE,FSTYPE,MOUNTPOINT "$ROOT_DEVICE" || true
"""
    result = _run_setup_command(setup_controller, ["bash", "-lc", script], timeout=timeout)
    logger.info("Linux guest volume expansion completed: %s", result.get("output", ""))


def _expand_windows_guest_volume(setup_controller: Any, *, timeout: int) -> None:
    ps_script = (
        "$ErrorActionPreference = 'Stop'; "
        "$partition = Get-Partition -DriveLetter C; "
        "$supported = Get-PartitionSupportedSize -DriveLetter C; "
        "if ($supported.SizeMax -gt $partition.Size) { "
        "Resize-Partition -DriveLetter C -Size $supported.SizeMax "
        "}; "
        "Get-Volume -DriveLetter C | Format-List DriveLetter,Size,SizeRemaining"
    )
    setup_controller.execute(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps_script],
        quiet=True,
        timeout=timeout,
    )
    logger.info("Windows guest volume expansion command completed")
