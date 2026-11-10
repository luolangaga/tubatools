"""Managed windows without a corresponding accessibility top-level window.

Some applications never register on the AT-SPI bus (Blender draws its own
interface), and Qt, Gecko and Chromium applications started before
accessibility was switched on stay off it until they restart. Their
windows are still managed X11 windows. They are listed with a native
window handle, so callers can activate, close and wait for them by title;
reading or operating their content needs GUI.
"""

import os
import time

from .foundation import (
    AX_WINDOW_ACTION_POLL_SECONDS,
    AX_WINDOW_ACTION_TIMEOUT_SECONDS,
    OperationFailure,
    InventoryResult,
    inventory_complete,
    node_pid,
    process_generation,
)
from . import action_evidence
from .x11_windows import (
    app_windows,
    iter_apps,
    strong_window_instance_handles,
    unmapped_window_indices,
    window_handle_for_pid,
)
from .x11_window_inventory import (
    _x11_managed_windows,
    _x11_window_inventory_cache,
    refuse_activation_behind_modal,
    x11_window_type,
)
from .desktop_layout import x11_root_window_state

# Window types of application windows. Docks, desktops, splash screens and
# notifications are not targets of activate or close. "" means the window
# sets no type, which EWMH defines as a normal window.
APPLICATION_WINDOW_TYPES = {"", "NORMAL", "DIALOG"}


def process_name(pid, proc_root="/proc"):
    """Return the process name of pid, or "" when it cannot be read."""
    try:
        with open(os.path.join(proc_root, str(int(pid)), "comm"),
                  encoding="utf-8", errors="replace") as comm:
            return comm.read().strip()
    except (OSError, ValueError):
        return ""


def record_window_coverage(coverage, app, windows, strong=None):
    """Record native handles already owned by AX, retaining uncertain matches.

    Registration on the bus says nothing about window coverage. An empty,
    complete application root owns no windows; an unreadable or ambiguous
    root does not establish which native windows lack a tree.
    """
    pid = node_pid(app)
    if strong is None:
        strong = strong_window_instance_handles(app, windows) if windows else {}
    hidden = unmapped_window_indices(app, windows, strong) if windows else set()
    entry = coverage.setdefault(pid, {"handles": set(), "complete": True})
    entry["handles"].update(strong.values())
    entry["complete"] = entry["complete"] and inventory_complete(windows) and all(
        index in strong or index in hidden for index, _ in windows)


def native_only_windows(managed, accessible_coverage, active_window_id=None,
                        proc_root="/proc"):
    """Return on-screen titled windows not owned by an AX top-level window.

    Each entry has the process id and name, the X11 window id, its title,
    the strong w2 handle derived like the handles of accessible windows, and
    whether the window manager reports it active.
    """
    windows, errors = [], []
    for window in managed:
        pid = int(window.get("pid") or 0)
        title = str(window.get("title") or "").strip()
        width = int(window.get("width") or 0)
        height = int(window.get("height") or 0)
        if pid <= 0 or not title:
            continue
        if width <= 0 or height <= 0:
            continue
        if (int(window.get("x") or 0) + width <= 0
                or int(window.get("y") or 0) + height <= 0):
            continue
        handle = window_handle_for_pid(pid, window.get("id"))
        if not handle:
            continue
        coverage = accessible_coverage.get(pid, {})
        if handle in coverage.get("handles", set()):
            continue
        if x11_window_type(window.get("id")) not in APPLICATION_WINDOW_TYPES:
            continue
        if not coverage.get("complete", True):
            errors.append(
                'The accessibility coverage of window "{}" is uncertain; '
                "its native window could not be matched safely.".format(title))
            continue
        windows.append({
            "pid": pid,
            "id": window["id"],
            "title": title,
            "app": process_name(pid, proc_root) or "pid {}".format(pid),
            "handle": handle,
            "generation": process_generation(pid),
            "active": bool(active_window_id)
            and window["id"] == active_window_id,
        })
    return InventoryResult(windows, complete=not errors, errors=errors)


def native_app_entries(windows):
    """Group native-only windows into apps-listing entries, one per process."""
    entries = {}
    for window in windows:
        entry = entries.get(window["pid"])
        if entry is None:
            entry = entries[window["pid"]] = {
                "name": window["app"],
                "bundleIdentifier": window["app"],
                "pid": window["pid"],
                "providerBusName": "",
                "processGeneration": window["generation"],
                "displayText": '{} [window="{}", no accessibility tree]'.format(
                    window["app"], window["title"]),
                "inputCapable": True,
                "windows": [],
            }
        entry["windows"].append({
            "title": window["title"],
            "role": "window",
            "handle": window["handle"],
            "providerBusName": "",
            "providerObjectPath": "",
            "processGeneration": window["generation"],
            "active": window["active"],
            "inputTarget": True,
            "noAccessibilityTree": True,
        })
    return list(entries.values())


def native_window_for_operation(operation):
    """Return the native-only window a window_action names, or None.

    An accessible window always carries its provider bus name in the
    target; only a target without one can name a native-only window.
    """
    target = operation.get("target") or {}
    if target.get("providerBusName") or target.get("providerWindowBusName"):
        return None
    handle = operation.get("window_handle") or target.get("windowHandle")
    if not handle:
        return None
    managed = _x11_managed_windows()
    apps = iter_apps()
    if not inventory_complete(managed) or not inventory_complete(apps):
        return None
    root = x11_root_window_state()
    coverage = {}
    for app in apps:
        record_window_coverage(coverage, app, app_windows(app))
    candidates = native_only_windows(
        managed, coverage,
        root.get("activeWindow") if root else None)
    for window in candidates:
        if window["handle"] == handle:
            return window
    return None


def _emit(action, window_id):
    """Send the window manager command; return once it was handed over."""
    import subprocess

    try:
        result = subprocess.run(
            ["wmctrl", "-ia" if action == "activate" else "-ic", window_id],
            capture_output=True, text=True, timeout=3,
        )
    except subprocess.TimeoutExpired as exc:
        raise OperationFailure(
            "effect_unknown: the window command timed out after it was "
            "started; it may have reached the window manager.",
            failure_code="effect_unknown",
            failure_phase="emit",
            input_events_emitted=True,
            failure_subject="window",
        ) from exc
    if result.returncode != 0:
        raise OperationFailure(
            "the window manager refused {}: {}; no window command took "
            "effect.".format(
                action, (result.stderr or result.stdout or "wmctrl failed").strip()),
            failure_code="window_action_failed",
            failure_phase="emit",
            input_events_emitted=False,
            failure_subject="window",
        )
    _x11_window_inventory_cache.clear()


def _confirmed(action, window_id):
    deadline = time.monotonic() + AX_WINDOW_ACTION_TIMEOUT_SECONDS
    while True:
        root = x11_root_window_state()
        if action == "activate":
            done = bool(root) and root.get("activeWindow") == window_id
        else:
            done = root is not None and window_id not in root.get("windowIds", set())
        if done or time.monotonic() >= deadline:
            return done
        time.sleep(AX_WINDOW_ACTION_POLL_SECONDS)


def perform_native_window_action(action, window):
    """Activate or close one native-only window and confirm the effect."""
    if action == "activate":
        refuse_activation_behind_modal(window["id"], window["pid"], window["title"])
    _emit(action, window["id"])
    action_evidence.update(route="native_window_command", input_emitted=True)
    if not _confirmed(action, window["id"]):
        raise OperationFailure(
            "effect_unknown: the window command was sent, but the window "
            "manager did not report the window {} within {:.1f}s.".format(
                "active" if action == "activate" else "gone",
                AX_WINDOW_ACTION_TIMEOUT_SECONDS),
            failure_code="effect_unknown",
            failure_phase="postcondition",
            input_events_emitted=True,
            failure_subject="window",
        )
    return {
        "ok": True,
        "notes": [
            "[window] {} confirmed by the window manager; the window has no "
            "accessibility tree.".format(action)
        ],
        "input_events_emitted": True,
        "desktop_state_mutated": True,
        "mutation_unknown": False,
        "appName": window["app"],
        "appPid": window["pid"],
        "actionEvidence": {
            "route": "native_window_command",
            "input_emitted": True,
            "readback_sampled": True,
            "readback_verified": True,
        },
    }
