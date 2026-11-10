"""Low-level Linux focus and input emission primitives."""

import subprocess
import time
from .foundation import (
    Atspi,
    FOCUS_GRAB_CANDIDATES,
    node_name,
    node_pid,
    safe,
    state_contains,
)
from .tree_walk import iter_all
from .x11_windows import (
    app_windows,
    iter_apps,
    x11_window_id_for_accessible,
)
from .desktop_layout import x11_root_window_state


def window_is_active(window):
    return window is not None and state_contains(window, Atspi.StateType.ACTIVE)


def x11_holds_focus(window):
    app = safe(lambda: Atspi.Accessible.get_application(window))
    target_pid = node_pid(app) if app is not None else 0
    if not target_pid:
        return False
    try:
        active = subprocess.run(
            ["xdotool", "getactivewindow"],
            capture_output=True,
            text=True,
            timeout=3,
        )
        if active.returncode != 0 or not active.stdout.strip():
            return False
        prop = subprocess.run(
            ["xprop", "-id", active.stdout.strip(), "_NET_WM_PID"],
            capture_output=True,
            text=True,
            timeout=3,
        )
    except Exception:
        return False
    if prop.returncode != 0:
        return False
    digits = "".join(
        char for char in prop.stdout if char.isdigit() or char == " "
    ).split()
    return bool(digits) and int(digits[-1]) == int(target_pid)


def window_in_foreground(window):
    """Return whether the window already holds focus, without raising it."""
    return window_is_active(window) or x11_holds_focus(window)


def x11_window_id(window):
    """Return the managed X11 window of one AX top-level window, or None."""
    app = safe(lambda: Atspi.Accessible.get_application(window))
    if app is None:
        return None
    return safe(lambda: x11_window_id_for_accessible(app, window))


def x11_active_window_id():
    state = x11_root_window_state()
    return state.get("activeWindow") if state else None


def request_window_activation(window_id):
    """Ask the window manager to activate a window (EWMH _NET_ACTIVE_WINDOW)."""
    try:
        result = subprocess.run(
            ["wmctrl", "-i", "-a", window_id],
            capture_output=True,
            timeout=3,
        )
    except Exception:
        return False
    return result.returncode == 0


def _wait_until(predicate, timeout):
    deadline = time.time() + timeout
    while True:
        if predicate():
            return True
        if time.time() >= deadline:
            return False
        time.sleep(0.05)


def focus_window(window, timeout=1.0):
    """Bring the window to the foreground and verify it from X11 state.

    The window manager is asked first because it activates the window of
    any toolkit at once. A child's grab_focus is only the fallback: Qt
    accepts it on every child without activating the window, so trying
    each child would cost one full timeout per child.
    """
    if window is None:
        return False
    if window_is_active(window):
        return True

    def in_foreground():
        return window_is_active(window) or x11_holds_focus(window)

    window_id = x11_window_id(window)
    if window_id:
        def activated():
            return (
                window_is_active(window)
                or x11_active_window_id() == window_id
            )

        # Compare the exact window, not the process: keystrokes go to the
        # X11 active window, which can be another window of the same app.
        if activated():
            return True
        if request_window_activation(window_id) and _wait_until(
            activated, timeout
        ):
            return True
        # The window manager can refuse, for example while a modal child of
        # the same app stays on top and keeps the input.
        if in_foreground():
            return True
    elif in_foreground():
        return True

    def grab(node):
        component = safe(node.get_component_iface)
        if component is None:
            return False
        return bool(safe(lambda: Atspi.Component.grab_focus(component), False))

    focusable = [
        node
        for node in iter_all(window)
        if state_contains(node, Atspi.StateType.FOCUSABLE)
    ]
    focusable.sort(
        key=lambda node: state_contains(node, Atspi.StateType.FOCUSED),
        reverse=True,
    )
    for node in focusable[:FOCUS_GRAB_CANDIDATES]:
        if not grab(node):
            continue
        if _wait_until(in_foreground, timeout):
            return True
        # The toolkit took the grab without activating the window; the
        # other candidates would do the same.
        break
    title = str(node_name(window) or "").strip()
    if title and not window_id:
        safe(
            lambda: subprocess.run(
                ["wmctrl", "-a", title],
                capture_output=True,
                timeout=3,
            )
        )
        if _wait_until(in_foreground, timeout):
            return True
    return in_foreground()


def active_accessible_window():
    try:
        for app in iter_apps():
            for _, window in app_windows(app):
                if state_contains(window, Atspi.StateType.ACTIVE):
                    return node_name(app), node_name(window)
    except Exception:
        return None, None
    return None, None


def require_window_focus(window, what):
    if focus_window(window):
        return
    app_name, window_name = active_accessible_window()
    if app_name is None:
        culprit = (
            " No window in the accessibility tree currently reports ACTIVE, so "
            "input focus is held by something the tree cannot see - most often a "
            "native dialog. Call gui with actions=[] to inspect the full desktop."
        )
    else:
        culprit = " Input focus is currently held by '{}' in '{}'.".format(
            window_name or "(untitled window)",
            app_name,
        )
    raise RuntimeError(
        "Refusing to synthesize {}: could not bring the target window to the "
        "foreground. Input synthesis is global and would be delivered to "
        "whichever window currently holds focus.{}".format(what, culprit)
    )


class InputEmissionError(RuntimeError):
    def __init__(self, description, events_emitted):
        super().__init__(
            "AT-SPI input emission failed while sending {}".format(description)
        )
        self.events_emitted = bool(events_emitted)


def require_input_emission(result, description, events_emitted=False):
    if result is False:
        raise InputEmissionError(description, events_emitted)
    return True


def emit_mouse_event(x, y, event, events_emitted=False):
    result = Atspi.generate_mouse_event(int(round(x)), int(round(y)), event)
    return require_input_emission(
        result,
        "mouse event {!r}".format(event),
        events_emitted,
    )


def emit_keyboard_event(
    keyval,
    keystring,
    synth_type,
    description,
    events_emitted=False,
):
    result = Atspi.generate_keyboard_event(keyval, keystring, synth_type)
    return require_input_emission(result, description, events_emitted)


def best_effort_mouse_release(x, y, event):
    try:
        return Atspi.generate_mouse_event(int(round(x)), int(round(y)), event) is not False
    except Exception:
        return False
