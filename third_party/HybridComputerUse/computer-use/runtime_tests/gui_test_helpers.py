#!/usr/bin/env python3
"""Capture doubles shared by the Linux provider GUI test modules."""

from runtime_test_support import *  # noqa: F401,F403


def captured(value, matches_bounds=True):
    """Return a CapturedGUISnapshot double; ``pixels`` is its comparison sample."""
    if isinstance(value, Exception):
        return value
    value = dict(value)
    result = mock.Mock(spec=runtime.CapturedGUISnapshot)
    result.pixels = value.pop("pixels", None)
    result.has_image = bool(value.get("screenshotPngBase64"))
    result.frame_id = value.get("frameId")
    result.matches_bounds.return_value = matches_bounds
    result.encode.return_value = value
    return result


def patch_gui_capture(*, return_value=None, side_effect=None):
    """Mock raw capture explicitly; existing dispatch cases supply wire fixtures."""
    if side_effect is not None:
        return mock.patch.object(runtime, "capture_gui_snapshot",
                                 side_effect=[captured(value) for value in side_effect])
    return mock.patch.object(runtime, "capture_gui_snapshot",
                             return_value=captured(return_value))
