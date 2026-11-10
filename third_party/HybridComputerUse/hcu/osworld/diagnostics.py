"""Guest diagnostics collected when the agent disappears or a run fails."""

from __future__ import annotations

import time

from hcu.osworld.guest import request_execute, safe_tag


def collect_guest_infra_diagnostic(base_url: str) -> dict:
    command = r"""
set +e
echo '=== root_mount ==='
findmnt -no SOURCE,FSTYPE,OPTIONS /
echo '=== root_write_probe ==='
probe=/tmp/.osworld-infra-write-probe-$$
if printf ok >"$probe" 2>/tmp/osworld-write-probe.err; then
  echo writable
  rm -f "$probe"
else
  echo not_writable
  cat /tmp/osworld-write-probe.err
fi
echo '=== memory ==='
cat /proc/meminfo
echo '=== disk ==='
df -hT
echo '=== processes ==='
ps -eo pid,ppid,state,etimes,rss,cmd --sort=-rss | head -n 100
echo '=== kernel_dmesg ==='
dmesg --ctime 2>&1 | tail -n 300
echo '=== kernel_journal ==='
journalctl -k --no-pager -n 300 2>&1
exit 0
"""
    try:
        result = request_execute(
            base_url,
            ["bash", "-lc", command],
            timeout=60,
        )
        output = str(result.get("output", ""))
        transport_error = None
    except Exception as exc:
        output = ""
        transport_error = f"{type(exc).__name__}: {exc}"

    lowered = output.lower()
    classifications = []
    mount_line = ""
    if "=== root_mount ===" in output:
        mount_line = output.split("=== root_mount ===", 1)[1].splitlines()[1:2]
        mount_line = mount_line[0] if mount_line else ""
    mount_options = (
        mount_line.rsplit(None, 1)[-1].split(",")
        if mount_line.strip()
        else []
    )
    root_is_read_only = "ro" in {
        option.strip().casefold() for option in mount_options
    }
    disk_full = (
        "no space left on device" in lowered
        or any(
            line.rstrip().endswith("100% /")
            for line in output.splitlines()
        )
    )
    if (
        root_is_read_only
        or "ext4-fs error" in lowered
        or "aborting journal" in lowered
        or "remounting filesystem read-only" in lowered
    ):
        classifications.append("guest_root_filesystem_failure")
    elif disk_full:
        classifications.append("guest_disk_full")
    elif "not_writable" in lowered:
        classifications.append("guest_write_failure")
    if (
        "out of memory" in lowered
        or "oom-kill" in lowered
        or "killed process" in lowered
    ):
        classifications.append("guest_oom_kill")
    if transport_error:
        classifications.append("guest_control_transport_failure")
    if not classifications:
        classifications.append("unclassified_guest_process_loss")

    return {
        "classifications": classifications,
        "transport_error": transport_error,
        "output": output,
        "collected_at": time.strftime(
            "%Y-%m-%dT%H:%M:%SZ",
            time.gmtime(),
        ),
    }


def parse_systemd_show(output: str) -> dict[str, str]:
    properties = {}
    in_show = False
    for line in output.splitlines():
        if line == "=== systemctl_show ===":
            in_show = True
            continue
        if line.startswith("=== ") and line.endswith(" ==="):
            in_show = False
            continue
        if not in_show or "=" not in line:
            continue
        key, value = line.split("=", 1)
        if key and key.replace("_", "").isalnum():
            properties[key] = value
    return properties


def collect_workload_unit_diagnostic(
    base_url: str,
    unit_name: str,
) -> dict:
    normalized = safe_tag(unit_name)[:200]
    if not normalized or normalized != unit_name:
        raise ValueError(f"Invalid workload unit name: {unit_name!r}")
    command = r"""
set +e
unit=$1
echo '=== workload_unit ==='
printf '%s\n' "$unit"
echo '=== systemctl_show ==='
systemctl --user show "$unit" --no-pager --all 2>&1
show_rc=$?
echo '=== systemctl_show_result ==='
printf 'systemctl_show_rc=%s\n' "$show_rc"
echo '=== unit_journal ==='
journalctl --user --unit "$unit" --no-pager -n 300 -o short-precise 2>&1
journal_rc=$?
echo '=== unit_journal_result ==='
printf 'unit_journal_rc=%s\n' "$journal_rc"
exit 0
"""
    try:
        result = request_execute(
            base_url,
            ["bash", "-lc", command, "bash", normalized],
            timeout=60,
        )
        output = str(result.get("output", ""))
        transport_error = None
    except Exception as exc:
        output = ""
        transport_error = f"{type(exc).__name__}: {exc}"

    properties = parse_systemd_show(output)
    result_value = properties.get("Result", "").casefold()
    active_state = properties.get("ActiveState", "").casefold()
    exec_status = properties.get("ExecMainStatus", "")
    oom_killed = properties.get("OOMKilled", "").casefold() == "yes"
    classifications = []
    if oom_killed or result_value == "oom-kill":
        classifications.append("guest_workload_oom")
    elif (
        active_state == "failed"
        or result_value not in {"", "success"}
        or exec_status not in {"", "0"}
    ):
        classifications.append("guest_workload_unit_failure")
    if transport_error:
        classifications.append("guest_workload_diagnostic_transport_failure")
    if not properties and not transport_error:
        classifications.append("guest_workload_unit_unavailable")

    return {
        "unit_name": normalized,
        "properties": properties,
        "classifications": classifications,
        "transport_error": transport_error,
        "output": output,
        "collected_at": time.strftime(
            "%Y-%m-%dT%H:%M:%SZ",
            time.gmtime(),
        ),
    }
