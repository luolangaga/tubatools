"""Scale between a top-level window's AT-SPI extents and X11 root pixels.

With a desktop scale (XSETTINGS Gdk/WindowScalingFactor, GDK_SCALE,
QT_SCALE_FACTOR or a Chromium device scale factor), GTK, Qt, Gecko and
Chromium's browser views report AT-SPI extents in logical pixels, while
input synthesis, screenshots and the X11 window list use root pixels.
The factor is measured per window: the window's AT-SPI frame multiplied by
a candidate factor must match the native client rectangle, or the window
manager frame around it, in origin and size. No toolkit or application
name is consulted, and a window without such evidence keeps factor 1.
"""

import re
import subprocess

from .foundation import (
    node_pid, provider_bus_name, provider_object_path, safe,
)
from .node_geometry import raw_extents
from .x11_window_inventory import (
    _x11_window_inventory_cache,
    x11_window_inventory_for_pid,
)
from .desktop_layout import normalize_x11_window_id, parse_xwininfo_layout

MIN_SCALE = 1.25
MAX_SCALE = 4.0
# Window decorations are drawn by the window manager in root pixels and are
# not scaled, so a frame that includes them is larger than client * factor.
MAX_DECORATION = (64, 128)
MAX_SIBLING_WINDOWS = 32
_FRAME_EXTENTS_RE = re.compile(
    r"_NET_FRAME_EXTENTS\(CARDINAL\)\s*=\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)")


def native_window_rects(window_id):
    """Return the client and window-manager frame rectangles of one window."""
    key = ("native_rects", window_id)
    if key in _x11_window_inventory_cache:
        return _x11_window_inventory_cache[key]
    rects = None
    try:
        info = subprocess.run(["xwininfo", "-id", window_id],
                              capture_output=True, text=True, timeout=1)
        extents = subprocess.run(["xprop", "-id", window_id, "_NET_FRAME_EXTENTS"],
                                 capture_output=True, text=True, timeout=1)
    except Exception:
        info = extents = None
    client = parse_xwininfo_layout(info.stdout) if info and info.returncode == 0 else None
    if client is not None:
        match = _FRAME_EXTENTS_RE.search(extents.stdout if extents else "")
        left, right, top, bottom = map(int, match.groups()) if match else (0, 0, 0, 0)
        rects = {
            "client": client,
            "frame": dict(x=client["x"] - left, y=client["y"] - top,
                          width=client["width"] + left + right,
                          height=client["height"] + top + bottom),
        }
    _x11_window_inventory_cache[key] = rects
    return rects


def _scaled_match(bounds, scale, native):
    tolerance = scale + 1
    return all(abs(bounds[key] * scale - native[key]) <= tolerance
               for key in ("x", "y", "width", "height"))


def _candidate_scale(bounds, candidate):
    """Return 1.0, a verified factor, or None for one native window."""
    width, height = candidate.get("width", 0), candidate.get("height", 0)
    if width <= 0 or height <= 0:
        return None
    extra_w, extra_h = MAX_DECORATION
    if (0 <= bounds["width"] - width <= extra_w
            and 0 <= bounds["height"] - height <= extra_h):
        return 1.0
    scale = round(width / bounds["width"] * 4) / 4
    if not MIN_SCALE <= scale <= MAX_SCALE:
        return None
    rects = native_window_rects(normalize_x11_window_id(candidate.get("id")))
    if rects and any(_scaled_match(bounds, scale, rects[kind])
                     for kind in ("client", "frame")):
        return scale
    return None


def measured_window_scale(window, bounds=None):
    """Return the factor measured for one AT-SPI window, or None."""
    key = ("scale", provider_bus_name(window),
           provider_object_path(window) or id(window))
    if key in _x11_window_inventory_cache:
        return _x11_window_inventory_cache[key]
    _x11_window_inventory_cache[key] = None
    bounds = raw_extents(window) if bounds is None else bounds
    application = safe(getattr(window, "get_application", None))
    candidates = list(x11_window_inventory_for_pid(node_pid(application))) if bounds else []
    scales = {_candidate_scale(bounds, candidate) for candidate in candidates} - {None}
    # A verified factor wins over a window whose size merely allows factor 1.
    verified = scales - {1.0}
    if len(verified) == 1:
        result = verified.pop()
    else:
        result = 1.0 if scales == {1.0} else None
    _x11_window_inventory_cache[key] = result
    return result


def window_scale(window, bounds=None):
    """Return the root pixels per AT-SPI pixel for a top-level window.

    A popup that is not a managed window (menus are override-redirect)
    has no native rectangle of its own and uses the factor measured on
    the application's other windows when they agree. Scale is set per
    process by every toolkit that reports logical extents.
    """
    scale = measured_window_scale(window, bounds)
    if scale is not None:
        return scale
    application = safe(getattr(window, "get_application", None))
    count = safe(getattr(application, "get_child_count", None)) or 0
    scales = set()
    for index in range(min(count, MAX_SIBLING_WINDOWS)):
        sibling = safe(lambda: application.get_child_at_index(index))
        if sibling is not None and sibling is not window:
            scales.add(measured_window_scale(sibling))
    scales.discard(None)
    return scales.pop() if len(scales) == 1 else 1.0

