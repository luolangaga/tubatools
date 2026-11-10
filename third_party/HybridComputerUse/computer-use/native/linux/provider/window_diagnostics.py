"""Window labels, input eligibility, and foreground diagnostics."""

from .foundation import (
    Atspi,  # pyright: ignore[reportAttributeAccessIssue]
    DEFAULT_TEXT_LIMIT,
    MAX_CHILD_FANOUT,
    child_at,
    child_count,
    is_interactive_role,
    limit_text,
    node_name,
    node_role,
    safe,
    state_contains,
)
from .ax_records import effective_name
from .x11_windows import app_windows, managed_window_title


DIALOG_ROLES = {"dialog", "alert", "file chooser", "color chooser"}
INFRA_PROBE_NODES = 32
INFRA_PROBE_DEPTH = 3
INFRA_PROBE_FANOUT = 8


def window_label(window, text_limit=DEFAULT_TEXT_LIMIT):
    name = str(node_name(window) or "").strip()
    if name:
        return limit_text(name, text_limit=text_limit)
    app = safe(lambda: window.get_parent())
    if app is not None and node_role(app) == "application":
        managed = managed_window_title(app, window)
        if managed:
            return limit_text(managed, text_limit=text_limit)
    role = str(node_role(window) or "window")
    for index in range(min(child_count(window), 8)):
        child = safe(lambda: window.get_child_at_index(index))
        if child is None:
            continue
        hint = str(effective_name(child) or "").strip()
        if hint:
            return limit_text(
                "(unnamed {} containing {!r})".format(role, hint),
                text_limit=text_limit,
            )
    return "(unnamed {})".format(role)


def window_has_input_content(window):
    if window is None:
        return False
    budget = [INFRA_PROBE_NODES]

    def walk(node, depth):
        if node is None or budget[0] <= 0 or depth > INFRA_PROBE_DEPTH:
            return False
        budget[0] -= 1
        if depth > 0:
            if str(node_name(node) or "").strip():
                return True
            if is_interactive_role(str(node_role(node) or "")):
                return True
        for index in range(min(child_count(node), INFRA_PROBE_FANOUT)):
            if walk(child_at(node, index), depth + 1):
                return True
        return False

    return walk(window, 0)


def is_infrastructure_window(window):
    if window is None or window_has_input_content(window):
        return False
    if not str(node_name(window) or "").strip():
        return True
    return str(node_role(window) or "").strip().lower() in {"desktop", "panel"}


def accepts_user_input(app):
    windows = app_windows(app)
    if not windows:
        return True
    return any(not is_infrastructure_window(window) for _, window in windows)


def foreign_foreground_note(app):
    if app is None:
        return []
    mine = str(node_name(app) or "")
    desktop = safe(lambda: Atspi.get_desktop(0))
    if desktop is None:
        return []
    for index in range(min(child_count(desktop), MAX_CHILD_FANOUT)):
        other = safe(lambda: desktop.get_child_at_index(index))
        if other is None:
            continue
        name = str(node_name(other) or "")
        if not name or name == mine:
            continue
        for window_index in range(min(child_count(other), 12)):
            window = child_at(other, window_index)
            if window is None:
                continue
            is_modal = state_contains(window, Atspi.StateType.MODAL)
            in_front = is_modal or (
                state_contains(window, Atspi.StateType.ACTIVE)
                and state_contains(window, Atspi.StateType.SHOWING)
            )
            if not (
                in_front
                and state_contains(window, Atspi.StateType.VISIBLE)
            ):
                continue
            if is_infrastructure_window(window):
                continue
            title = str(node_name(window) or "").strip() or "(untitled)"
            if is_modal:
                return [
                    "ANOTHER APP IS IN FRONT: the active window is {!r}, owned "
                    "by {!r}, not {!r}. Call AX mode=observe with app={!r} "
                    "before sending input.".format(title, name, mine, name)
                ]
            return [
                "ANOTHER WINDOW MAY BE IN FRONT: {!r}, owned by {!r}, not {!r}. "
                "Confirm the target window before sending input.".format(
                    title, name, mine
                )
            ]
    return []


def modal_diagnostic(window, app):
    if window is None:
        return []
    is_modal = state_contains(window, Atspi.StateType.MODAL)
    role = str(node_role(window) or "").lower()
    if not is_modal and role not in DIALOG_ROLES:
        return []
    title = str(node_name(window) or "").strip() or "(untitled)"
    others = []
    current_app = app
    count = child_count(current_app) if current_app is not None else 0
    for index in range(min(count, MAX_CHILD_FANOUT)):
        other = child_at(current_app, index)
        if other is None or other == window:
            continue
        if state_contains(other, Atspi.StateType.SHOWING):
            name = str(node_name(other) or "").strip()
            if name and name != title:
                others.append(name)
    if not is_modal and not others:
        return []

    behind = ""
    if others:
        behind = " Behind it: {}.".format(", ".join(sorted(set(others))[:3]))

    if is_modal:
        return [
            "MODAL DIALOG: the tree below is {!r}, a modal dialog, not the "
            "application's main window.{} The application will ignore input to "
            "every other window until this dialog is dismissed. Use only refs "
            "from the current dialog, then observe again after it closes.".format(
                title,
                behind,
            )
        ]
    return [
        "DIALOG IN FRONT: the tree below is {!r}, a dialog, not the "
        "application's main window.{} It does not report the MODAL state, so "
        "this is not proof that the app is blocked. Refs from an "
        "earlier main-window observation do not address this dialog.".format(
            title,
            behind,
        )
    ]
