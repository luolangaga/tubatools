"""Managed and popup X11 windows, modal dialogs and window types."""

import re
import subprocess

from .foundation import InventoryResult, OperationFailure
from .desktop_layout import (
    normalize_x11_window_id,
    parse_xwininfo_layout,
    x11_root_window_state,
)


def correlation_root_window_state():
    """Root state for matching AX windows to native windows.

    Shared by every correlation in one operation and dropped with the native
    listing (operation start and after input). Before/after comparisons and
    polls call x11_root_window_state() directly because they need a fresh
    sample.
    """
    if "root" not in _x11_window_inventory_cache:
        _x11_window_inventory_cache["root"] = x11_root_window_state()
    return _x11_window_inventory_cache["root"]


# One parsed `wmctrl -lpG` listing per operation. The listing covers every
# managed window, so one sample answers every pid; dispatch clears it at the
# start of an operation and again once input is emitted. A failed sample is
# not kept, so the next read retries.
_x11_window_inventory_cache = {}


def _x11_managed_windows():
    cached = _x11_window_inventory_cache.get("all")
    if cached is not None:
        return cached
    try:
        result = subprocess.run(
            ["wmctrl", "-lpG"],
            capture_output=True,
            text=True,
            timeout=3,
        )
    except Exception as error:
        return InventoryResult(
            complete=False,
            errors=["native window inventory failed: {}".format(
                type(error).__name__
            )],
        )
    if result.returncode != 0:
        return InventoryResult(
            complete=False,
            errors=["native window inventory command failed"],
        )
    windows = []
    for line in result.stdout.splitlines():
        parts = line.split(None, 8)
        if len(parts) < 8:
            continue
        try:
            window_pid = int(parts[2])
            x, y, width, height = map(int, parts[3:7])
        except (TypeError, ValueError):
            continue
        window_id = normalize_x11_window_id(parts[0])
        if not window_id:
            continue
        windows.append({
            "pid": window_pid,
            "id": window_id,
            "x": x,
            "y": y,
            "width": width,
            "height": height,
            "title": parts[8] if len(parts) > 8 else "",
        })
    inventory = InventoryResult(windows, complete=True)
    _x11_window_inventory_cache["all"] = inventory
    return inventory


def _xprop_text(window_id, *atoms):
    """Return xprop's text for the given atoms of one window, or ""."""
    try:
        result = subprocess.run(
            ["xprop", "-id", window_id] + list(atoms),
            capture_output=True, text=True, timeout=2,
        )
    except Exception:
        return ""
    return result.stdout if result.returncode == 0 else ""


_TRANSIENT_FOR_RE = re.compile(
    r"WM_TRANSIENT_FOR\(WINDOW\): window id # (0x[0-9a-fA-F]+)")


def modal_transient_windows(window_id, pid):
    """Return the modal windows of process ``pid`` transient for ``window_id``.

    The window manager keeps input in such a dialog, so it refuses to
    activate the window behind it. A minimized (hidden) dialog does not
    block. Only the X11 hints count here, because some toolkits never report
    the AT-SPI MODAL state (MuseScore 4's dialogs have no accessible tree).
    """
    target = normalize_x11_window_id(window_id)
    blocking = []
    for window in _x11_managed_windows():
        if int(window.get("pid") or 0) != int(pid or 0) or window.get("id") == target:
            continue
        text = _xprop_text(window["id"], "WM_TRANSIENT_FOR", "_NET_WM_STATE")
        match = _TRANSIENT_FOR_RE.search(text)
        if not match or normalize_x11_window_id(match.group(1)) != target:
            continue
        if "_NET_WM_STATE_MODAL" in text and "_NET_WM_STATE_HIDDEN" not in text:
            blocking.append(window)
    return blocking


def refuse_activation_behind_modal(window_id, pid, title):
    """Raise before input when a modal dialog of the app blocks activation."""
    blocking = modal_transient_windows(window_id, pid)
    if not blocking:
        return
    dialog = str(blocking[0].get("title") or "").strip() or "(untitled)"
    raise OperationFailure(
        'window_blocked: "{}" has a modal dialog of the same application '
        'open, "{}", and the window manager keeps the input there. Answer or '
        'close "{}" first (activate and close take its title). No input was '
        "emitted.".format(str(title or "").strip() or "(untitled)", dialog, dialog),
        failure_code="window_blocked",
        failure_phase="precondition",
        input_events_emitted=False,
        failure_subject="window",
    )

# Children of the root window inspected for override-redirect popups.
MAX_POPUP_CANDIDATES = 64
_ROOT_CHILD_RE = re.compile(
    r"^\s+(0x[0-9a-fA-F]+) .*?(\d+)x(\d+)[+-]-?\d+[+-]-?\d+\s+([+-]-?\d+)([+-]-?\d+)\s*$",
    re.MULTILINE,
)


def _x11_override_redirect_windows(pid):
    """Return viewable override-redirect top-level windows owned by pid.

    Menus, context menus and drop-down lists of Chromium and Electron are
    override-redirect windows: the window manager does not manage them, so
    wmctrl never lists them. Each is read only when it is big enough to be
    a popup, and the result is cached for the operation like the managed
    listing.
    """
    key = ("override_redirect", int(pid))
    cached = _x11_window_inventory_cache.get(key)
    if cached is not None:
        return cached
    try:
        root = subprocess.run(
            ["xwininfo", "-root", "-children"],
            capture_output=True, text=True, timeout=1,
        )
    except Exception:
        return []
    found = []
    matches = list(_ROOT_CHILD_RE.finditer(root.stdout or ""))
    for match in matches[:MAX_POPUP_CANDIDATES]:
        window_id, width, height = match.group(1), int(match.group(2)), int(match.group(3))
        if width < 20 or height < 20:
            continue
        try:
            info = subprocess.run(
                ["xwininfo", "-id", window_id],
                capture_output=True, text=True, timeout=0.5,
            )
        except Exception:
            continue
        layout = parse_xwininfo_layout(info.stdout)
        if not layout or not layout["visible"] or not layout["overrideRedirect"]:
            continue
        try:
            owner = subprocess.run(
                ["xprop", "-id", window_id, "_NET_WM_PID"],
                capture_output=True, text=True, timeout=0.5,
            )
        except Exception:
            continue
        owner_pid = re.search(r"=\s*(\d+)", owner.stdout or "")
        if owner_pid is None or int(owner_pid.group(1)) != int(pid):
            continue
        normalized = normalize_x11_window_id(window_id)
        if normalized:
            found.append({
                "id": normalized,
                "x": layout["x"], "y": layout["y"],
                "width": layout["width"], "height": layout["height"],
                "title": "",
            })
    _x11_window_inventory_cache[key] = found
    return found


def x11_window_inventory_for_pid(pid):
    """Return bounded native windows with explicit enumeration completeness."""
    pid = int(pid or 0)
    if pid <= 0:
        return InventoryResult(
            complete=False,
            errors=["application process identity is unavailable"],
        )
    managed = _x11_managed_windows()
    if not managed.complete:
        return InventoryResult(complete=False, errors=managed.errors)
    return InventoryResult([
        {key: value for key, value in window.items() if key != "pid"}
        for window in managed
        if window["pid"] == pid
    ], complete=True)


def x11_window_type(window_id):
    """Return the EWMH _NET_WM_WINDOW_TYPE suffix of one X11 window, or ""."""
    if not window_id:
        return ""
    try:
        result = subprocess.run(
            ["xprop", "-id", str(window_id), "_NET_WM_WINDOW_TYPE"],
            capture_output=True, text=True, timeout=1,
        )
    except Exception:
        return ""
    # Vendor atoms can contain the standard prefix inside their own name.
    # Match an entire EWMH atom, not that suffix of a vendor extension.
    match = re.search(r"(?<![A-Za-z0-9_])_NET_WM_WINDOW_TYPE_([A-Za-z0-9_]+)", result.stdout or "")
    return match.group(1) if match else ""
