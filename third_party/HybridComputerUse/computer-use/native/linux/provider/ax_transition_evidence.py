"""Cross-channel freshness and public evidence helpers."""

def external_desktop_transition_after_ax_mutation(
    before_root,
    after_root,
    app_windows_before,
    app_windows_after,
):
    """Detect a newly active native window absent from the current AX app.

    ``before_root``/``after_root`` are x11_root_window_state() samples: the
    managed client ids and the active window id.
    """
    if before_root is None or after_root is None:
        return False
    before_active = str(before_root.get("activeWindow") or "").strip()
    after_active = str(after_root.get("activeWindow") or "").strip()
    if not after_active or after_active == before_active:
        return False
    if after_active in (before_root.get("windowIds") or set()):
        return False
    if app_windows_before is None or app_windows_after is None:
        return False
    return app_windows_before == app_windows_after


def public_element_record(record):
    if not isinstance(record, dict):
        return record
    return {
        key: value
        for key, value in record.items()
        if not str(key).startswith("_")
    }
