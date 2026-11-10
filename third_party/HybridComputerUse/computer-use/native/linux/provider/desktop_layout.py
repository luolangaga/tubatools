"""Desktop bounds, X11 root window state and the visible window layout."""

import hashlib
import json
import re
import subprocess

from .foundation import (
    GUI_MAX_LAYOUT_WINDOWS,
    Gdk,
    MAX_SANE_EXTENT,
    frame,
)


def desktop_bounds():
    """Return the full virtual desktop in X root coordinates.

    AT-SPI's SCREEN coordinates and XTEST both use the X root window coordinate
    space. GUI exposes the captured image with a (0,0) top-left and translates
    those image pixels back through this origin before XTEST emission.
    """
    if Gdk is None:
        return None
    try:
        screen = Gdk.Screen.get_default()
        if screen is None:
            return None
        monitor_bounds = []
        monitor_count = int(screen.get_n_monitors())
        for monitor_index in range(monitor_count):
            geometry = screen.get_monitor_geometry(monitor_index)
            if (
                geometry is None
                or geometry.width <= 0
                or geometry.height <= 0
                or geometry.width > MAX_SANE_EXTENT
                or geometry.height > MAX_SANE_EXTENT
                or abs(geometry.x) > MAX_SANE_EXTENT
                or abs(geometry.y) > MAX_SANE_EXTENT
            ):
                continue
            monitor_bounds.append(geometry)
        if monitor_bounds:
            left = min(geometry.x for geometry in monitor_bounds)
            top = min(geometry.y for geometry in monitor_bounds)
            right = max(
                geometry.x + geometry.width for geometry in monitor_bounds
            )
            bottom = max(
                geometry.y + geometry.height for geometry in monitor_bounds
            )
            if right > left and bottom > top:
                return frame(left, top, right - left, bottom - top)
        root = screen.get_root_window()
        if root is None:
            return None
        width = int(root.get_width())
        height = int(root.get_height())
        if width <= 0 or height <= 0:
            return None
        return frame(0, 0, width, height)
    except Exception:
        return None


def normalize_x11_window_id(value):
    try:
        return hex(int(str(value).strip(), 0))
    except (TypeError, ValueError):
        return None


def parse_x11_root_layout_properties(text):
    clients = re.search(
        r"^_NET_CLIENT_LIST_STACKING[^:]*:\s*window id #\s*(.*)$",
        str(text or ""),
        re.MULTILINE,
    )
    active = re.search(
        r"^_NET_ACTIVE_WINDOW[^:]*:\s*window id #\s*(.*)$",
        str(text or ""),
        re.MULTILINE,
    )
    if clients is None or active is None:
        return None
    window_ids = [
        normalize_x11_window_id(value)
        for value in re.findall(r"0x[0-9a-fA-F]+", clients.group(1))
    ]
    window_ids = [value for value in window_ids if value is not None]
    active_ids = re.findall(r"0x[0-9a-fA-F]+", active.group(1))
    active_id = normalize_x11_window_id(active_ids[0]) if active_ids else None
    if active_id == "0x0":
        active_id = None
    return window_ids, active_id


def parse_xwininfo_layout(text):
    output = str(text or "")
    title_match = re.search(
        r'^xwininfo:\s+Window id:\s+\S+(?:\s+"(.*)")?\s*$',
        output,
        re.MULTILINE,
    )
    mapped = re.search(r"^\s*Map State:\s*(\S+)\s*$", output, re.MULTILINE)
    x = re.search(r"^\s*Absolute upper-left X:\s*(-?\d+)\s*$",
                  output, re.MULTILINE)
    y = re.search(r"^\s*Absolute upper-left Y:\s*(-?\d+)\s*$",
                  output, re.MULTILINE)
    width = re.search(r"^\s*Width:\s*(\d+)\s*$", output, re.MULTILINE)
    height = re.search(r"^\s*Height:\s*(\d+)\s*$", output, re.MULTILINE)
    override = re.search(
        r"^\s*Override Redirect State:\s*(\S+)\s*$",
        output,
        re.MULTILINE,
    )
    if not (mapped and x and y and width and height):
        return None
    return {
        "visible": mapped.group(1) == "IsViewable",
        "x": int(x.group(1)),
        "y": int(y.group(1)),
        "width": int(width.group(1)),
        "height": int(height.group(1)),
        "title": title_match.group(1) if title_match and title_match.group(1) else "",
        "overrideRedirect": (
            override.group(1) == "yes" if override is not None else False),
    }


def x11_root_window_state(timeout=0.25):
    """Return managed window IDs and active window with one bounded X11 query."""
    try:
        root = subprocess.run(
            ["xprop", "-root", "_NET_CLIENT_LIST_STACKING", "_NET_ACTIVE_WINDOW"],
            capture_output=True,
            text=True,
            timeout=timeout,
        )
    except Exception:
        return None
    if root.returncode != 0:
        return None
    properties = parse_x11_root_layout_properties(root.stdout)
    if properties is None:
        return None
    window_ids, active_id = properties
    if len(window_ids) > GUI_MAX_LAYOUT_WINDOWS:
        return None
    return {"windowIds": set(window_ids), "activeWindow": active_id}


def frame_intersects(first, second):
    if first is None or second is None:
        return False
    return (
        first["width"] > 0
        and first["height"] > 0
        and first["x"] < second["x"] + second["width"]
        and first["x"] + first["width"] > second["x"]
        and first["y"] < second["y"] + second["height"]
        and first["y"] + first["height"] > second["y"]
    )


def frame_intersection(first, second):
    if not frame_intersects(first, second):
        return None
    left = max(first["x"], second["x"])
    top = max(first["y"], second["y"])
    right = min(first["x"] + first["width"], second["x"] + second["width"])
    bottom = min(first["y"] + first["height"], second["y"] + second["height"])
    return frame(left, top, right - left, bottom - top)


def desktop_layout_state(bounds):
    """Return coordinate-relevant X11 desktop state without reading pixels.

    Titles, contents, caret state, clocks, and animations are excluded. The
    virtual desktop, visible managed top-level windows in stacking order, their
    geometry, and the active window are the facts that invalidate coordinates.
    """
    if bounds is None:
        return None
    try:
        root = subprocess.run(
            ["xprop", "-root", "_NET_CLIENT_LIST_STACKING", "_NET_ACTIVE_WINDOW"],
            capture_output=True,
            text=True,
            timeout=3,
        )
        if root.returncode != 0:
            return None
        properties = parse_x11_root_layout_properties(root.stdout)
        if properties is None:
            return None
        window_ids, active_id = properties
        if len(window_ids) > GUI_MAX_LAYOUT_WINDOWS:
            return None
        visible = []
        for window_id in window_ids:
            info = subprocess.run(
                ["xwininfo", "-id", window_id],
                capture_output=True,
                text=True,
                timeout=3,
            )
            if info.returncode != 0:
                return None
            parsed = parse_xwininfo_layout(info.stdout)
            if parsed is None:
                return None
            window_frame = frame(
                parsed["x"], parsed["y"], parsed["width"], parsed["height"])
            if not parsed["visible"] or not frame_intersects(window_frame, bounds):
                continue
            visible.append({
                "id": window_id,
                "x": parsed["x"] - int(round(bounds["x"])),
                "y": parsed["y"] - int(round(bounds["y"])),
                "width": parsed["width"],
                "height": parsed["height"],
                "title": parsed["title"],
                "overrideRedirect": parsed["overrideRedirect"],
            })
        return {
            "desktop": {
                "x": 0,
                "y": 0,
                "width": int(round(bounds["width"])),
                "height": int(round(bounds["height"])),
                "screenX": int(round(bounds["x"])),
                "screenY": int(round(bounds["y"])),
            },
            "activeWindow": active_id,
            "windows": visible,
        }
    except Exception:
        return None


def desktop_layout_frame_id(layout):
    if layout is None:
        return None
    coordinate_layout = {
        "desktop": layout.get("desktop"),
        "activeWindow": layout.get("activeWindow"),
        "windows": [
            {
                "id": window.get("id"),
                "x": window.get("x"),
                "y": window.get("y"),
                "width": window.get("width"),
                "height": window.get("height"),
                "overrideRedirect": bool(window.get("overrideRedirect")),
            }
            for window in layout.get("windows", [])
        ],
    }
    encoded = json.dumps(
        coordinate_layout,
        ensure_ascii=True,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("ascii")
    return "layout-v1-" + hashlib.sha256(encoded).hexdigest()
