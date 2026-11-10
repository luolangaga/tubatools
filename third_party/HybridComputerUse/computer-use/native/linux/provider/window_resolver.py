"""Linux logical/native window resolution and actions."""

import subprocess
import time
from .foundation import (
    AX_WINDOW_ACTION_POLL_SECONDS,
    AX_WINDOW_ACTION_TIMEOUT_SECONDS,
    AX_WINDOW_ROLES,
    OperationFailure,
    inventory_complete,
    node_name,
    node_pid,
    node_role,
    process_generation,
    provider_bus_name,
    provider_object_path,
)
from .node_geometry import extents
from . import action_evidence
from .ax_records import effective_role
from .x11_windows import (
    app_windows,
    relative_frame,
    strong_window_instance_handle_inventory,
    titled_top_level_window_id,
    x11_window_id_for_accessible,
)
from .x11_window_inventory import (
    _x11_window_inventory_cache,
    refuse_activation_behind_modal,
    x11_window_inventory_for_pid,
)
from .desktop_layout import x11_root_window_state


def perform_ax_window_action(tool, app, window, element_record):
    """Run activate or close on one top-level window and confirm the effect.

    Every check before the command fails closed with no command emitted; once
    wmctrl starts, an unconfirmed effect is reported as effect_unknown.
    """
    handle, window_id = _window_action_preflight(
        tool, app, window, element_record
    )
    app_name = str(node_name(app) or "").strip()
    app_pid = node_pid(app)
    _emit_native_window_command(tool, window_id)
    observed, windowless = _await_window_transition(
        tool, app, app_pid, window_id
    )
    if not observed:
        raise OperationFailure(
            "effect_unknown: the native window command was emitted, but a "
            "complete X11 inventory did not confirm that the exact target "
            "window disappeared.",
            failure_code="effect_unknown",
            failure_phase="postcondition",
            input_events_emitted=True,
        )
    return {
        "note": (
            "[a11y][window] {} requested; transition_observed={} "
            "windowless_after_close={}."
        ).format(tool, str(observed).lower(), str(windowless).lower()),
        "windowless": windowless,
        "app_name": app_name,
        "app_pid": app_pid,
        "closed_handle": handle,
    }


def _window_action_preflight(tool, app, window, element_record):
    """Return (handle, window_id) of the exact native window to command."""
    role = str((element_record or {}).get("controlType") or "").strip().lower()
    path = list((element_record or {}).get("runtimeId") or [])
    if role not in AX_WINDOW_ROLES and window is not None:
        # A top-level container (Qt "filler") is a window; see
        # top_level_window_role().
        role = str(effective_role(window) or "").strip().lower()
    if role not in AX_WINDOW_ROLES or len(path) != 1:
        raise OperationFailure(
            "{} requires a top-level AX window target; no window command was "
            "emitted.".format(tool),
            failure_code="window_target_invalid",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    windows = app_windows(app)
    if not windows.complete:
        raise OperationFailure(
            "observation_incomplete: the target window inventory could not be "
            "read completely before the native window command; no command was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    strong_inventory = strong_window_instance_handle_inventory(app, windows)
    if not strong_inventory.complete:
        raise OperationFailure(
            "observation_incomplete: the native window inventory could not be "
            "read completely before the window command; no command was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    strong_handles = dict(strong_inventory)
    handle = strong_handles.get(int(path[0]))
    expected_handle = str(
        (element_record or {}).get("_windowHandle") or ""
    ).strip()
    if not str(handle or "").startswith("w2:"):
        raise OperationFailure(
            "{} requires a one-to-one managed-window ref; no window command "
            "was emitted.".format(tool),
            failure_code="window_identity_ambiguous",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    if expected_handle and handle != expected_handle:
        raise OperationFailure(
            "stale_window_ref reason=native_window_instance_changed; "
            "no window command was emitted.",
            failure_code="stale_window_ref",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    window_id = x11_window_id_for_accessible(app, window) or (
        titled_top_level_window_id(
            window,
            {item["id"]: item for item in x11_window_inventory_for_pid(node_pid(app))
             if item.get("id")},
        )
    )
    if not window_id:
        raise OperationFailure(
            "stale_window_ref reason=native_window_instance_changed; "
            "no window command was emitted.",
            failure_code="stale_window_ref",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    root_state = x11_root_window_state()
    if root_state is None:
        raise OperationFailure(
            "observation_incomplete: the native window inventory could not be "
            "read before the window command; no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    if window_id not in root_state.get("windowIds", set()):
        raise OperationFailure(
            "stale_window_ref reason=native_window_instance_disappeared; "
            "no window command was emitted.",
            failure_code="stale_window_ref",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    if tool == "activate":
        refuse_activation_behind_modal(window_id, node_pid(app), node_name(window))
    return handle, window_id


def _emit_native_window_command(tool, window_id):
    try:
        result = subprocess.run(
            ["wmctrl", "-ia" if tool == "activate" else "-ic", window_id],
            capture_output=True,
            text=True,
            timeout=3,
        )
    except subprocess.TimeoutExpired as exc:
        action_evidence.update(
            route="native_window_command",
            input_emitted=True,
        )
        raise OperationFailure(
            "effect_unknown: the native window command process timed out after "
            "it was started, so the command may already have reached the window "
            "manager; inspect fresh state before retrying.",
            failure_code="effect_unknown",
            failure_phase="emit",
            input_events_emitted=True,
            failure_subject="window",
            failure_facts_complete=False,
        ) from exc
    if result.returncode != 0:
        raise RuntimeError(
            "AX {} native window command failed: {}".format(
                tool,
                (result.stderr or result.stdout or "wmctrl failed").strip(),
            )
        )
    action_evidence.update(
        route="native_window_command",
        input_emitted=True,
    )
    _x11_window_inventory_cache.clear()


def _await_window_transition(tool, app, app_pid, window_id):
    """Poll until the command's effect is observed or the deadline passes.

    Returns (observed, windowless). Activation is observed when the window is
    active; close is observed only when the exact native window is gone.
    """
    deadline = time.monotonic() + AX_WINDOW_ACTION_TIMEOUT_SECONDS
    observed = False
    windowless = False
    while True:
        root_state = x11_root_window_state()
        current_windows = app_windows(app)
        if tool == "activate":
            observed = bool(
                root_state and root_state.get("activeWindow") == window_id
            )
        else:
            native_complete = root_state is not None
            native_gone = bool(
                native_complete
                and window_id not in root_state.get("windowIds", set())
            )
            native_app_windows = x11_window_inventory_for_pid(app_pid)
            native_app_windowless = bool(
                native_app_windows.complete
                and len(native_app_windows) == 0
            )
            ax_complete = inventory_complete(current_windows)
            windowless = (
                native_app_windowless
                or (ax_complete and len(current_windows) == 0)
            )
            # This action targets a one-to-one native window identity. An AX
            # wrapper may disappear or rebuild while the native window remains
            # open, so AX disappearance alone cannot prove that close
            # succeeded. Only a complete X11 root inventory proving the exact
            # native window ID absent closes this identity.
            observed = native_gone
        if observed or time.monotonic() >= deadline:
            break
        time.sleep(AX_WINDOW_ACTION_POLL_SECONDS)
    return observed, windowless


def resolve_window_action_target(app, target):
    """Resolve a wN target from the logical/native window inventory only.

    A window ref already identifies one top-level native window. Its action
    truth must not depend on reading the window's descendant accessibility
    tree, which can be incomplete for unrelated content.
    """
    target_handle = str(target.get("windowHandle") or "").strip()
    if not target_handle:
        raise OperationFailure(
            "invalid_target: the window ref has no logical window identity; "
            "no input was emitted.",
            failure_code="invalid_target",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    windows = app_windows(app)
    if not windows.complete:
        raise OperationFailure(
            "observation_incomplete: the application window inventory could not "
            "be read completely; no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    strong_inventory = strong_window_instance_handle_inventory(app, windows)
    if not strong_inventory.complete:
        raise OperationFailure(
            "observation_incomplete: the native window inventory could not be "
            "read completely; no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    strong_handles = dict(strong_inventory)
    matches = [
        (index, candidate)
        for index, candidate in windows
        if strong_handles.get(index) == target_handle
    ]
    if not matches:
        raise OperationFailure(
            "stale_window_ref: the referenced logical window no longer exists; "
            "no input was emitted.",
            failure_code="stale_window_ref",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    if len(matches) != 1:
        raise OperationFailure(
            "observation_incomplete: the logical window identity is not unique; "
            "no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    index, window = matches[0]
    bounds = extents(window)
    role = str(effective_role(window) or target.get("providerWindowRole") or "")
    record = {
        "index": 0,
        "runtimeId": [index],
        "controlType": role,
        "localizedControlType": role,
        "name": str(node_name(window) or target.get("providerWindowName") or ""),
        "description": "",
        "placeholder": "",
        "states": "",
        "actions": [],
        "frame": relative_frame(window, bounds),
        "providerBusName": provider_bus_name(window),
        "providerObjectPath": provider_object_path(window),
        "processGeneration": process_generation(node_pid(window)),
        "_windowHandle": target_handle,
    }
    return window, record, window, bounds
