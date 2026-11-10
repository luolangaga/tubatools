"""The AX select action on drop-downs: choose an option and read it back.

A drop-down is a combo box or list box whose options are its descendants.
Choosing an option succeeds only when the drop-down reads back the choice:
the option's SELECTED state, or the toolkit's committed value (GTK, Qt).
"""

from collections import deque
import time

from . import action_evidence
from .foundation import (
    Atspi,
    FIND_FIRST_BUDGET,
    MAX_CHILD_FANOUT,
    OperationFailure,
    node_name,
    node_role,
    provider_object_path,
    safe,
    should_enumerate_children,
    state_contains,
)
from .node_geometry import raw_extents
from .ax_records import gtk_combo_partner
from .ax_node_properties import committed_dropdown_value
from .ax_actions import send_key
from .actionability import application_toolkit
from .ax_select import (
    _failure,
    _quote,
    _same,
    _selection_container,
    _wait_until,
    text_between,
)
from .input_primitives import InputEmissionError, require_window_focus


DROPDOWN_ROLES = ("combo box", "list box")
# Combo box actions that open the popup list: Qt "ShowMenu", GTK "press",
# Chromium "open".
POPUP_OPEN_ACTIONS = ("showmenu", "press", "open")
OPTION_ROLES = ("menu item", "list item", "option", "check menu item",
                "radio menu item", "table cell")
# Nodes read below one drop-down before its option list counts as
# incomplete. Proving a name unique needs the whole list, so the bound
# admits long lists such as time-zone pickers.
MAX_DROPDOWN_OPTIONS = 2000


def _dropdown_options(node):
    """Return all options in tree order, or reject an incomplete scan.

    A prefix cannot prove that an option name is unique or absent. Apply
    the same bound to queued and visited nodes so wide trees stay bounded.
    """
    options = []
    stack = deque([node])
    seen = 0
    while stack:
        current = stack.popleft()
        seen += 1
        role = safe(current.get_role_name)
        if role is None:
            raise _incomplete_options()
        if current is not node and role in OPTION_ROLES:
            if safe(current.get_name) is None:
                raise _incomplete_options()
            options.append(current)
            continue
        count = safe(current.get_child_count)
        if (count is None or count < 0
                or seen + len(stack) + count > MAX_DROPDOWN_OPTIONS):
            raise _incomplete_options()
        for index in range(count):
            child = safe(lambda index=index: current.get_child_at_index(index))
            if child is None:
                raise _incomplete_options()
            stack.append(child)
    return options


def _incomplete_options():
    return _failure(
        "select_options_incomplete: the drop-down options could not be read "
        "completely; a unique choice cannot be established. Inspect the "
        "open list and select the option with GUI.",
        emitted=_dropdown_input_emitted())


def _dropdown_input_emitted():
    return bool((action_evidence.current() or {}).get("input_emitted"))


def _emit_dropdown_action(callback):
    # A lost reply does not prove emission or rejection. Keep that uncertainty
    # separate from requests that the provider explicitly accepted.
    try:
        accepted = callback()
    except OperationFailure as exc:
        if exc.input_events_emitted:
            action_evidence.update(input_emitted=True, route="semantic_action")
        elif not exc.failure_facts_complete:
            exc.partial_input_possible = True
        raise
    except Exception as exc:
        exc.partial_input_possible = True
        raise
    if accepted:
        action_evidence.update(input_emitted=True, route="semantic_action")
    return accepted


def _send_dropdown_key(key):
    try:
        send_key(key)
    except InputEmissionError:
        raise
    except OperationFailure as exc:
        if not exc.failure_facts_complete:
            exc.partial_input_possible = True
        raise
    except Exception as exc:
        exc.partial_input_possible = True
        raise
    action_evidence.update(input_emitted=True, route="keyboard_synthesis")


def _option_selected(option):
    if state_contains(option, Atspi.StateType.SELECTED):
        return True
    container = _selection_container(option)
    index = safe(lambda: option.get_index_in_parent())
    if container is None or index is None or index < 0:
        return False
    return bool(safe(lambda: Atspi.Selection.is_child_selected(
        container, int(index)), False))


def option_name(option):
    name = safe(option.get_name)
    if name is None:
        raise _incomplete_options()
    return str(name).strip()


def _popup_open(combo, option):
    """Return True while the popup list holding ``option`` is on screen."""
    if state_contains(combo, Atspi.StateType.EXPANDABLE):
        return state_contains(combo, Atspi.StateType.EXPANDED)
    # Qt reports no EXPANDED state and keeps the closed list SHOWING with a
    # 0x0 frame; GTK and Chromium do not report the closed list SHOWING.
    container = safe(option.get_parent)
    frame = raw_extents(container) if container is not None else None
    return (frame is not None and frame["width"] > 1 and frame["height"] > 1
            and state_contains(container, Atspi.StateType.SHOWING))


def _focused_index(options):
    focused = [index for index, option in enumerate(options)
               if state_contains(option, Atspi.StateType.FOCUSED)]
    return focused[0] if len(focused) == 1 else None


def _open_popup(combo, option):
    if _popup_open(combo, option):
        return True
    count = int(safe(combo.get_n_actions, 0) or 0)
    names = [str(safe(lambda k=k: combo.get_action_name(k), "") or "").lower()
             for k in range(count)]
    for wanted in POPUP_OPEN_ACTIONS:
        if wanted in names:
            _emit_dropdown_action(lambda: combo.do_action(names.index(wanted)))
            return _wait_until(lambda: _popup_open(combo, option), 1.0)
    return False


def _wait_for_focus_advance(predicate):
    """Observe fast key transitions without a fixed delay for every arrow."""
    deadline = time.monotonic() + 0.6
    delay = 0.001
    while True:
        if predicate():
            return True
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return False
        time.sleep(min(delay, remaining))
        delay = min(delay * 2, 0.02)


def _commit_from_popup(combo, option, window):
    """Make ``option`` the popup's current item and commit it with Return.

    A Qt popup commits its current item, and only the keyboard moves that
    item: the items' Toggle action changes the selection, not the current
    item, and Component.grab_focus is refused. Each arrow checks that the
    old item lost focus and a following item gained it. This avoids scanning
    the whole list for every key while still allowing separators and disabled
    items. A final full scan verifies the unique focus before Return. If the
    option cannot be reached, Escape closes the popup without changing the
    choice. Returns True after Return was sent.
    """
    require_window_focus(window, "select")
    if not _open_popup(combo, option):
        return False
    # Qt recreates popup items on every read, so the option is found again
    # by its unique name (select_dropdown_option checked uniqueness).
    options = _dropdown_options(combo)
    names = [option_name(candidate) for candidate in options]
    target = (names.index(option_name(option))
              if names.count(option_name(option)) == 1 else None)
    current = _focused_index(options)
    for _ in range(len(options)):
        if target is None or current is None or current == target:
            break
        if not state_contains(options[current], Atspi.StateType.FOCUSED):
            break
        direction = 1 if target > current else -1
        _send_dropdown_key("Down" if direction > 0 else "Up")
        next_focus = []

        def advanced():
            if state_contains(options[current], Atspi.StateType.FOCUSED):
                return False
            # Stop at the target: jumping over it cannot choose it. Searching
            # back and forth would repeatedly cross a disabled target.
            for index in range(current + direction, target + direction, direction):
                if state_contains(options[index], Atspi.StateType.FOCUSED):
                    next_focus[:] = [index]
                    return True
            return False

        if not _wait_for_focus_advance(advanced):
            break
        current = next_focus[0]
    if target is None or current != target or _focused_index(options) != target:
        _send_dropdown_key("Escape")
        return False
    _send_dropdown_key("Return")
    return True


def _gecko_content_select(node):
    if node_role(node) != "combo box" or application_toolkit(node) != "gecko":
        return False
    attrs = safe(getattr(node, "get_attributes", None), {}) or {}
    return attrs.get("tag") == "select"


def _gecko_popup_source(popup):
    """Bind a ref in a native popup back to one content combo in its frame."""
    _app, window = _top_level(popup)
    if window is None or not _same(safe(popup.get_parent), window):
        raise _gecko_select_failure()
    if _expanded_frame_combos(window) != [popup]:
        raise _gecko_select_failure()
    stack, matches, seen = deque([window]), [], 0
    while stack:
        node = stack.popleft()
        seen += 1
        if _same(node, popup):
            continue
        if _gecko_content_select(node):
            if _gecko_popup_matches(node, popup, _dropdown_options(node), anchored=True):
                matches.append(node)
            continue
        role = safe(node.get_role_name)
        if role is None:
            raise _incomplete_options()
        if role in ("menu", "menu item", "combo box"):
            continue
        count = safe(node.get_child_count)
        if count is None or count < 0 or seen + len(stack) + count > MAX_DROPDOWN_OPTIONS:
            raise _incomplete_options()
        for index in range(count):
            child = safe(lambda: node.get_child_at_index(index))
            if child is None:
                raise _incomplete_options()
            stack.append(child)
    if len(matches) != 1:
        raise _gecko_select_failure()
    return matches[0]


def _expanded_frame_combos(window):
    # Gecko's native popup is a direct child of the bound frame, separate
    # from the document's combo. Never search another window by option name.
    count = safe(window.get_child_count) if window is not None else None
    if count is None or count < 0 or count > MAX_DROPDOWN_OPTIONS:
        raise _incomplete_options()
    result = []
    for index in range(count):
        child = safe(lambda: window.get_child_at_index(index))
        if child is None:
            raise _incomplete_options()
        role = safe(child.get_role_name)
        if role is None:
            raise _incomplete_options()
        if role == "combo box":
            states = safe(child.get_state_set)
            if states is None:
                raise _incomplete_options()
            if (states.contains(Atspi.StateType.EXPANDED)
                    and states.contains(Atspi.StateType.SHOWING)):
                result.append(child)
    return result


def _named_dropdown_action(node, name):
    count = int(safe(node.get_n_actions, 0) or 0)
    names = [safe(lambda k=k: node.get_action_name(k), "")
             for k in range(count)]
    return (name in names and _emit_dropdown_action(
        lambda: node.do_action(names.index(name))))


def _gecko_popup_matches(combo, popup, options, *, anchored=False):
    rows = _dropdown_options(popup)
    if [option_name(row) for row in rows] != [option_name(o) for o in options]:
        return False
    if not anchored:
        return True
    source = raw_extents(combo)
    menu = safe(rows[0].get_parent) if rows else None
    target = raw_extents(menu) if menu is not None else None
    if source is None or target is None:
        return False
    # An already-open menu must touch this anchor, not merely cover another
    # control that has identical options. Reject unbound/overlapping layouts.
    return (source["x"] < target["x"] + target["width"]
            and target["x"] < source["x"] + source["width"]
            and min(abs(target["y"] - source["y"] - source["height"]),
                    abs(source["y"] - target["y"] - target["height"])) <= 2)


def _gecko_select_failure():
    return _failure(
        "select_unverified: the native drop-down popup could not be bound "
        "to the source control or its commit could not be verified. Inspect "
        "the current choice before retrying with GUI.",
        code="effect_unknown" if _dropdown_input_emitted() else "select_unavailable",
        emitted=_dropdown_input_emitted(), phase="readback")


def _commit_gecko_select(combo, option, options, window):
    """Activate the native popup, then read the document's selected option.

    Gecko's content Selection changes the DOM value without input/change
    events. Its content option action does nothing, even with the popup open.
    The separate native popup's click action commits through the browser.
    Sending Return after content Selection cannot repair the missing events.
    """
    _app, owner = _top_level(combo)
    if owner is None or (window is not None and not _same(owner, window)):
        raise _gecko_select_failure()
    if not (state_contains(option, Atspi.StateType.ENABLED)
            and state_contains(option, Atspi.StateType.SENSITIVE)):
        raise _failure("select_unavailable: the requested option is disabled; "
                       "no input was emitted.")
    opened = _expanded_frame_combos(owner)
    if opened:
        # Reopen through this source to establish ownership, including when
        # two controls contain identical options. Do not adopt an unrelated
        # popup or close an ambiguous set of popups.
        if (len(opened) != 1
                or not _gecko_popup_matches(combo, opened[0], options, anchored=True)
                or not _named_dropdown_action(opened[0], "close")
                or not _wait_until(lambda: not _expanded_frame_combos(owner), 0.6)):
            raise _gecko_select_failure()
    if not _named_dropdown_action(combo, "open"):
        raise _gecko_select_failure()
    if not _wait_until(lambda: bool(_expanded_frame_combos(owner)), 1.0):
        raise _gecko_select_failure()
    opened = _expanded_frame_combos(owner)
    if len(opened) != 1 or not _gecko_popup_matches(combo, opened[0], options):
        raise _gecko_select_failure()
    popup = opened[0]
    matches = [row for row in _dropdown_options(popup)
               if option_name(row) == option_name(option)]
    if (len(matches) != 1
            or not state_contains(matches[0], Atspi.StateType.ENABLED)
            or not state_contains(matches[0], Atspi.StateType.SENSITIVE)):
        raise _gecko_select_failure()
    if not _named_dropdown_action(matches[0], "click"):
        raise _gecko_select_failure()
    if not _wait_until(lambda: not _expanded_frame_combos(owner)
                       and _option_selected(option), 1.0):
        raise _gecko_select_failure()


def _list_selection_first(node):
    """Return True when a closed drop-down's option list should be tried first.

    This holds for an EXPANDABLE combo box that is not EXPANDED and has no
    Selection of its own. Gecko content selects use their native popup
    before reaching this route. Chrome returns False from Selection and
    changes nothing, so its option action follows without a wait. An
    open list keeps the option action first: its Selection may only move the
    highlight.
    """
    return (node_role(node) == "combo box"
            and state_contains(node, Atspi.StateType.EXPANDABLE)
            and not state_contains(node, Atspi.StateType.EXPANDED)
            and safe(node.get_selection_iface) is None)


def select_dropdown_option(node, text, window=None):
    """Choose the option named ``text`` of a closed or open drop-down.

    Browsers expose an HTML <select> as a combo box whose options are menu
    items with a single "select" action; while the list is closed they are
    not showing and carry no refs. The option's own action is tried first
    (Chrome's Selection.select_child returns False), then the combo box's
    own Selection.select_child (GTK sets the active item through it), then
    the option container's Selection.select_child. Gecko content selects
    instead activate their separate native popup before any value mutation. A
    closed list that holds its own selection is tried through its Selection
    first; see _list_selection_first().

    A combo box with committed-value readback (GTK and Qt) succeeds only
    when that value matches the option afterwards; otherwise the
    option's SELECTED state is the evidence. When neither route changes the
    shown choice, the popup's own commit path is used: open it, move its
    current item to the option and press Return (Qt). A LibreOffice
    drop-down (a projected toggle) commits through its separate popup list;
    see select_projected_option(). Returns the chosen option's name.
    """
    if _projected_toggle(node):
        return select_projected_option(node, text)
    if (node_role(node) == "combo box"
            and application_toolkit(node) == "gecko"
            and not _gecko_content_select(node)
            and state_contains(node, Atspi.StateType.EXPANDED)):
        node = _gecko_popup_source(node)
    options = _dropdown_options(node)
    wanted = str(text).strip()
    matches = [option for option in options if option_name(option) == wanted]
    if len(matches) != 1:
        names = [option_name(option) for option in options]
        raise _failure(
            "select_option_not_found: {} {} among the options; no input was "
            "emitted. Options: {}.".format(
                _quote(text),
                "is not" if not matches else "occurs more than once",
                ", ".join(_quote(name) for name in names[:30]) or "none readable"),
            code="invalid_argument")
    option = matches[0]
    shows_choice = committed_dropdown_value(node) is not None

    def committed():
        if shows_choice:
            return committed_dropdown_value(node) == wanted
        return _option_selected(option)

    if committed():
        return wanted
    if _gecko_content_select(node):
        _commit_gecko_select(node, option, options, window)
        return wanted
    index = safe(lambda: option.get_index_in_parent())
    container = _selection_container(option)
    list_tried = False
    if (container is not None and index is not None and index >= 0
            and not shows_choice and _list_selection_first(node)):
        list_tried = True
        # A False return means the list refused and changed nothing.
        if (_emit_dropdown_action(
                lambda: Atspi.Selection.select_child(container, int(index)))
                and _wait_until(committed, 0.6)):
            return wanted
    count = int(safe(option.get_n_actions, 0) or 0)
    names = [str(safe(lambda k=k: option.get_action_name(k), "") or "")
             for k in range(count)]
    if "select" in names:
        _emit_dropdown_action(lambda: option.do_action(names.index("select")))
        if _wait_until(committed, 0.6):
            return wanted
    parent = safe(option.get_parent)
    if (index is not None and index >= 0 and parent is not None
            and safe(node.get_selection_iface) is not None
            and _same(safe(parent.get_parent), node)):
        # GTK commits through the combo box's own Selection, which sets the
        # active item; the popup menu's Selection only highlights an item.
        _emit_dropdown_action(lambda: Atspi.Selection.select_child(node, int(index)))
        if _wait_until(committed, 0.6):
            return wanted
    if (container is not None and index is not None and index >= 0
            and not list_tried):
        _emit_dropdown_action(lambda: Atspi.Selection.select_child(container, int(index)))
        if _wait_until(committed, 0.6):
            return wanted
    if shows_choice:
        if _commit_from_popup(node, option, window) and _wait_until(committed, 1.0):
            return wanted
        raise _failure(
            "select_unverified: the drop-down shows {} after choosing {}; "
            "its choice may have changed. Open it, then press Return on the "
            "option or click it with the GUI.".format(
                _quote(committed_dropdown_value(node)), _quote(text)),
            code="effect_unknown", emitted=_dropdown_input_emitted(), phase="readback")
    if not _dropdown_input_emitted():
        raise _failure(
            "select_unavailable: the option {} has no select action and its "
            "list has no selection interface; no input was emitted. Open the "
            "drop-down and click the option.".format(_quote(text)))
    raise _failure(
        "select_unverified: the option {} did not report itself selected."
        .format(_quote(text)),
        code="effect_unknown", emitted=_dropdown_input_emitted(), phase="readback")


def owning_dropdown(node):
    """Return the drop-down whose popup list holds the option ``node``."""
    if node_role(node) not in OPTION_ROLES:
        return None
    current = node
    for _ in range(3):
        current = safe(current.get_parent)
        if current is None:
            break
        if node_role(current) == "combo box":
            return current
    return projected_list_owner(node)


# LibreOffice (gtk3) draws a drop-down as a toggle button beside a hidden
# combo box and a text; ax_records.gtk_combo_partner() projects the toggle
# as the combo box. The list opens in a separate untitled popup window of
# table cells that has no relation back to the toggle. While the list is
# open the toggle reports CHECKED, and the text shows the current choice.


def _projected_toggle(node):
    return gtk_combo_partner(node, node_role(node)) is not None


def _projected_shown_choice(toggle):
    """Return the text the projected combo's text sibling shows, or ""."""
    parent = safe(toggle.get_parent)
    count = int(safe(parent.get_child_count, 0) or 0) if parent else 0
    texts = [child for child in (
        safe(lambda k=k: parent.get_child_at_index(k)) for k in range(count))
        if child is not None and node_role(child) == "text"]
    return text_between(texts[0], 0, -1).strip() if len(texts) == 1 else ""


def _top_level(node):
    """Return ``(application, top-level window)`` holding ``node``.

    LibreOffice reports no parent for its top-level windows, so the
    application comes from get_application when the walk stops short.
    """
    window = node
    for _ in range(64):
        parent = safe(window.get_parent)
        if parent is None or node_role(parent) == "application":
            break
        window = parent
    else:
        return None, None
    app = parent if parent is not None else safe(
        lambda: Atspi.Accessible.get_application(node))
    return (app, window) if app is not None else (None, None)


def _open_lists(app):
    """Return the untitled showing top-level windows of ``app`` with options."""
    count = int(safe(app.get_child_count, 0) or 0) if app else 0
    windows = (safe(lambda k=k: app.get_child_at_index(k)) for k in range(count))
    return [window for window in windows
            if window is not None and not option_name(window)
            and state_contains(window, Atspi.StateType.SHOWING)
            and _dropdown_options(window)]


def _list_choice(toggle):
    """Read a list-only drop-down's choice: the row its list selects on opening.

    A list-only LibreOffice drop-down (dialog combos such as Line Spacing)
    leaves its text sibling empty, so the choice is readable only while the
    list is open. The list is closed again before returning.
    """
    popup = _open_projected_list(toggle)
    if popup is None:
        return ""
    chosen = [option_name(option) for option in _dropdown_options(popup)
              if state_contains(option, Atspi.StateType.SELECTED)]
    _click_toggle(toggle)
    _wait_until(lambda: not state_contains(toggle, Atspi.StateType.CHECKED), 1.0)
    return chosen[0] if len(chosen) == 1 else ""


def _click_toggle(toggle):
    count = int(safe(toggle.get_n_actions, 0) or 0)
    names = [str(safe(lambda k=k: toggle.get_action_name(k), "") or "").lower()
             for k in range(count)]
    if "click" not in names:
        return False
    return bool(_emit_dropdown_action(lambda: toggle.do_action(names.index("click"))))


def _open_projected_list(toggle):
    """Return the popup window holding the toggle's list, opening it if needed."""
    app, _window = _top_level(toggle)
    if state_contains(toggle, Atspi.StateType.CHECKED):
        lists = _open_lists(app)
        return lists[0] if len(lists) == 1 else None
    before = {provider_object_path(window) for window in _open_lists(app)}
    if not _click_toggle(toggle):
        return None
    opened = []

    def appeared():
        opened[:] = [window for window in _open_lists(app)
                     if provider_object_path(window) not in before]
        return len(opened) == 1
    return opened[0] if _wait_until(appeared, 1.0) else None


def _activate_list_row(option):
    """Move the list cursor to ``option`` and activate that row.

    A GTK tree view commits the row under its cursor when a cell is
    activated. Selection.select_child only highlights a row and leaves the
    cursor, so the old choice would be committed; grab_focus on the cell
    moves the cursor.
    """
    component = safe(option.get_component_iface)
    if component is None or not _emit_dropdown_action(
            lambda: Atspi.Component.grab_focus(component)):
        return False
    if not _wait_until(
            lambda: state_contains(option, Atspi.StateType.FOCUSED), 0.6):
        return False
    count = int(safe(option.get_n_actions, 0) or 0)
    names = [str(safe(lambda k=k: option.get_action_name(k), "") or "").lower()
             for k in range(count)]
    if "activate" not in names:
        return False
    return bool(_emit_dropdown_action(lambda: option.do_action(names.index("activate"))))


def select_projected_option(toggle, text):
    """Choose an option of a LibreOffice drop-down and read the choice back.

    The list is opened by clicking the toggle when it is closed, and closed
    the same way if the option cannot be chosen. Success requires the text
    beside the toggle to show the option afterwards or, for a list-only
    drop-down whose text stays empty, the reopened list to select it.
    """
    wanted = str(text).strip()
    shown = _projected_shown_choice(toggle)
    if not state_contains(toggle, Atspi.StateType.CHECKED) and shown == wanted:
        return wanted
    clicked = not state_contains(toggle, Atspi.StateType.CHECKED)
    popup = _open_projected_list(toggle)
    if popup is None:
        if clicked and state_contains(toggle, Atspi.StateType.CHECKED):
            _click_toggle(toggle)
        raise _failure(
            "select_unavailable: the drop-down list could not be found as one "
            "open popup; no option was chosen. Click the drop-down, then "
            "select the option.",
            code="effect_unknown" if _dropdown_input_emitted() else "select_unavailable",
            emitted=_dropdown_input_emitted(),
            phase="emission" if _dropdown_input_emitted() else "precondition")
    options = _dropdown_options(popup)
    matches = [option for option in options if option_name(option) == wanted]
    if len(matches) != 1:
        _click_toggle(toggle)
        names = [option_name(option) for option in options]
        raise _failure(
            "select_option_not_found: {} {} among the options; the list was "
            "closed without choosing. Options: {}.".format(
                _quote(text),
                "is not" if not matches else "occurs more than once",
                ", ".join(_quote(name) for name in names[:30])
                or "none readable"),
            code="invalid_argument", emitted=_dropdown_input_emitted())
    choice = ""
    if _activate_list_row(matches[0]) and _wait_until(
            lambda: not state_contains(toggle, Atspi.StateType.CHECKED), 1.0):
        if shown:
            _wait_until(
                lambda: _projected_shown_choice(toggle) == wanted, 1.0)
            choice = _projected_shown_choice(toggle)
        else:
            choice = _list_choice(toggle)
        if choice == wanted:
            return wanted
    if state_contains(toggle, Atspi.StateType.CHECKED):
        _click_toggle(toggle)
        _wait_until(
            lambda: not state_contains(toggle, Atspi.StateType.CHECKED), 1.0)
    if not choice:
        choice = _projected_shown_choice(toggle) or _list_choice(toggle)
    raise _failure(
        "select_unverified: the drop-down shows {} after choosing {}; its "
        "choice may have changed. Open it and click the option with the GUI."
        .format(_quote(choice) if choice else "no readable choice",
                _quote(text)),
        code="effect_unknown", emitted=_dropdown_input_emitted(), phase="readback")


def projected_list_owner(option):
    """Return the toggle whose open list holds ``option``, or None.

    The popup has no relation back to its toggle, so the owner is the one
    projected combo of the same application that reports its list open.
    """
    app, window = _top_level(option)
    if app is None or option_name(window):
        return None
    count = int(safe(app.get_child_count, 0) or 0)
    stack = [top for top in (safe(lambda k=k: app.get_child_at_index(k))
                             for k in range(count))
             if top is not None and not _same(top, window)]
    budget = FIND_FIRST_BUDGET
    while stack and budget > 0:
        node = stack.pop()
        budget -= 1
        # The opener is drawn, so subtrees that are not showing are skipped.
        if not state_contains(node, Atspi.StateType.SHOWING):
            continue
        if _projected_toggle(node):
            if state_contains(node, Atspi.StateType.CHECKED):
                return node
            continue
        if should_enumerate_children(node):
            children = int(safe(node.get_child_count, 0) or 0)
            stack.extend(child for child in (
                safe(lambda k=k: node.get_child_at_index(k))
                for k in reversed(range(min(children, MAX_CHILD_FANOUT))))
                if child is not None)
    return None
