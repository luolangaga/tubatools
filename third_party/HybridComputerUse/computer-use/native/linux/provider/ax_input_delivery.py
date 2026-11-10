"""Delivery of AX click, scroll, type, keypress, and set_value actions, with channel-tagged notes."""

import math
import time

from .foundation import (
    Atspi,
    OperationFailure,
    has_editable_text_iface,
    has_text_iface,
    node_name,
    node_role,
    safe,
    state_contains,
)
from .ax_actions import (
    WHEEL_CLICKS_PER_PAGE,
    find_editable_text,
    focus_target_element,
    insert_text_outcome_on_node,
    parse_key,
    resolve_combo_editor,
    scroll_element,
    send_key,
    send_text,
    set_element_value_outcome,
)
from .gui_input import send_mouse_click
from .input_primitives import (
    require_window_focus,
    window_in_foreground,
)
from .actionability import (
    action_only_sets_state,
    ACTION_REPLY_TIMEOUT,
    do_action_by_index,
    point_hits_element,
    preferred_action_index,
    require_coordinate_delivery,
    require_stable_coordinate_geometry,
    frame_point_text,
    screen_point,
)
from .ax_target_notes import (
    PREFER_ELEMENT_INDEX,
    UNVERIFIED_SEMANTIC,
    UNVERIFIED_SYNTHESIS,
    addressing_channel,
)
from .ax_snapshot_diagnostics import is_dropdown_item


A11Y_CHANNEL = "[a11y]"
KEY_CHANNEL = "[keyboard]"
SEMANTIC = "[semantic] "
SYNTHESIS = "[synthesis] "


def _perform_click(
    action_input,
    window,
    bounds,
    element,
    element_record,
    notes,
    action_evidence,
):
    """Click by the requested method and return the refreshed element record."""
    click_method = (action_input.get("click_method") or "auto").lower()
    click_count = max(1, int(action_input.get("click_count", 1) or 1))
    if click_method == "accessibility":
        _accessibility_click(
            action_input, element, click_count, notes, action_evidence
        )
    elif click_method == "app_post":
        raise RuntimeError("click_method 'app_post' is not supported on Linux")
    elif click_method == "sky_click":
        raise RuntimeError("click_method 'sky_click' is not supported on Linux")
    elif click_method == "global":
        element_record = _global_click(
            action_input,
            window,
            bounds,
            element,
            element_record,
            click_count,
            notes,
            action_evidence,
        )
    elif click_method == "auto":
        element_record = _auto_click(
            action_input,
            window,
            bounds,
            element,
            element_record,
            click_count,
            notes,
            action_evidence,
        )
    else:
        raise RuntimeError("Invalid click_method '{}'".format(click_method))
    return element_record


def _accessibility_click(
    action_input, element, click_count, notes, action_evidence
):
    if element is None:
        raise RuntimeError("click_method 'accessibility' requires a target ref")
    if action_input.get("mouse_button", "left") != "left":
        raise RuntimeError(
            "click_method 'accessibility' only supports mouse_button 'left'"
        )
    if click_count != 1:
        raise RuntimeError(
            "click_method 'accessibility' cannot represent click_count > 1; "
            "use click_method 'auto' or 'global' for a real multi-click"
        )
    if not do_action_by_index(element, preferred_action_index(element)):
        raise RuntimeError(
            "click_method 'accessibility' could not click the requested element"
        )
    action_evidence["route"] = "semantic_action"
    action_evidence["input_emitted"] = True
    notes.append(
        A11Y_CHANNEL
        + SEMANTIC
        + "Invoked the element's AT-SPI accessibility action. "
        + UNVERIFIED_SEMANTIC
    )


def _global_click(
    action_input,
    window,
    bounds,
    element,
    element_record,
    click_count,
    notes,
    action_evidence,
):
    element_record, _ = require_stable_coordinate_geometry(
        element, element_record, bounds
    )
    x, y = screen_point(
        bounds,
        element_record,
        action_input.get("x"),
        action_input.get("y"),
    )
    require_coordinate_delivery(window, x, y, "click")
    send_mouse_click(
        x, y, action_input.get("mouse_button", "left"), click_count
    )
    action_evidence["route"] = "coordinate_synthesis"
    action_evidence["geometry_sampled"] = True
    action_evidence["geometry_stable"] = True
    action_evidence["window_focus_verified"] = True
    action_evidence["point_owner_verified"] = True
    action_evidence["focus_verified"] = True
    action_evidence["input_emitted"] = True
    notes.append(
        addressing_channel(element_record)
        + SYNTHESIS
        + "Synthesized {} coordinate click(s) {} after bringing the window "
        "to the foreground. {}{}"
        .format(
            click_count,
            frame_point_text(x, y),
            UNVERIFIED_SYNTHESIS,
            # Omit PREFER_ELEMENT_INDEX when the caller already named
            # an element: this route is usually taken because the
            # semantic action failed (several applications report
            # AT-SPI action success without any effect), so
            # coordinates are the only remaining option.
            "" if element_record else PREFER_ELEMENT_INDEX,
        )
    )
    return element_record


def _auto_click(
    action_input,
    window,
    bounds,
    element,
    element_record,
    click_count,
    notes,
    action_evidence,
):
    """Prefer the element's AT-SPI action; fall back to a coordinate click."""
    semantic_click = (
        element is not None
        and action_input.get("mouse_button", "left") == "left"
        and click_count == 1
        and not is_dropdown_item(element, window)
    )
    action_index = preferred_action_index(element) if semantic_click else None
    if (
        action_index is not None
        and node_role(element) in {"entry", "text", "password text"}
        and state_contains(element, Atspi.StateType.EDITABLE)
        and has_editable_text_iface(element)
        and has_text_iface(element)
        and str(safe(lambda: element.get_action_name(action_index), "")
                or "").strip().lower() == "activate"
    ):
        # Entry.activate means submit/Enter, not editor focus. In a freshly
        # opened dialog it can leave focus on an outer row, consuming the
        # start of a following untargeted type action outside the editor.
        try:
            focus_target_element(
                window, element, element_record, "editable text click")
        except Exception as error:
            raise OperationFailure(
                "The editor focus request may have changed focus, but target "
                "focus could not be confirmed. No keyboard or pointer input "
                "was emitted. Observe before sending more input. "
                + str(error),
                failure_code="effect_unknown",
                failure_phase="readback",
                input_events_emitted=True,
                failure_subject="element",
            ) from error
        action_evidence["route"] = "semantic_focus"
        action_evidence["input_emitted"] = True
        action_evidence["window_focus_verified"] = True
        action_evidence["target_focus_verified"] = True
        notes.append("Focused the editable text through AT-SPI and verified "
                     "that exact target owns focus; no keyboard or pointer "
                     "events were synthesized.")
        return element_record
    handled = False
    state_only_note = ""
    if semantic_click:
        if action_only_sets_state(element, action_index):
            pointer_record, refused = _pointer_click_for_state_only_action(
                window, bounds, element, element_record, notes,
                action_evidence)
            if pointer_record is not None:
                return pointer_record
            state_only_note = (
                " This element's only AT-SPI action is a toolkit toggle that "
                "changes its checked state without the click the application "
                "listens for, and a real pointer click was not safe ({}), so "
                "the application may not respond even though the state "
                "changes.".format(refused)
            )
        handled = do_action_by_index(element, action_index)
    if handled:
        action_evidence["route"] = "semantic_action"
        action_evidence["input_emitted"] = True
        reply_note = ""
        if handled == ACTION_REPLY_TIMEOUT:
            reply_note = (
                " The application did not reply before the timeout, which "
                "toolkits do while a modal dialog the action opened is "
                "running; the action was delivered."
            )
        notes.append(
            A11Y_CHANNEL
            + SEMANTIC
            + "Invoked the element's AT-SPI accessibility action. "
            + UNVERIFIED_SEMANTIC
            + state_only_note
            + reply_note
        )
    else:
        if element is not None and node_role(element) == "menu":
            raise OperationFailure(
                "semantic_action_unavailable role='menu' name={!r}; "
                "the menu exposed no usable AT-SPI action, and automatic "
                "coordinate fallback is disabled because duplicated or stale "
                "menu geometry can activate a different command. No input was "
                "emitted. Open the menu with keyboard navigation or use gui "
                "after observing a fresh full-desktop frame.".format(
                    node_name(element)
                ),
                failure_code="semantic_action_unavailable",
                failure_phase="precondition",
                input_events_emitted=False,
            )
        element_record, _ = require_stable_coordinate_geometry(
            element, element_record, bounds
        )
        x, y = screen_point(
            bounds,
            element_record,
            action_input.get("x"),
            action_input.get("y"),
        )
        require_coordinate_delivery(window, x, y, "click")
        send_mouse_click(
            x,
            y,
            action_input.get("mouse_button", "left"),
            click_count,
        )
        action_evidence["route"] = "coordinate_synthesis"
        action_evidence["geometry_sampled"] = True
        action_evidence["geometry_stable"] = True
        action_evidence["window_focus_verified"] = True
        action_evidence["point_owner_verified"] = True
        action_evidence["focus_verified"] = True
        action_evidence["input_emitted"] = True
        if is_dropdown_item(element, window):
            # Explain why the semantic route was skipped. Otherwise the
            # note reads as if the element had no action; it has one,
            # but that action closes the drop-down without committing
            # the value and leaves nothing to verify.
            reason = (
                "This element is an item inside a drop-down popup, where the "
                "AT-SPI action closes the popup without committing the value "
                "and leaves nothing to verify against, so this went straight to "
                "{} coordinate click(s) {}. ".format(
                    click_count, frame_point_text(x, y))
            )
        else:
            reason = (
                "No usable AT-SPI action was available, so this fell back to a "
                "{} coordinate click(s) {} after bringing the window to the "
                "foreground. ".format(click_count, frame_point_text(x, y))
            )
        notes.append(
            addressing_channel(element_record)
            + SYNTHESIS
            + reason
            + UNVERIFIED_SYNTHESIS
        )
    return element_record


def _pointer_click_for_state_only_action(
    window, bounds, element, element_record, notes, action_evidence
):
    """Click a state-only-action element with the pointer when that is safe.

    Return ``(record, None)`` after the click, or ``(None, reason)`` when a
    guard refused it before any input; the caller then keeps the semantic
    action. The guards are those of every coordinate click (stable live
    geometry, a visible on-screen point, the window in the foreground and
    owning the point) plus a hit test that the point reaches this element.
    """
    # The pointer route never raises the window: a state-only action is the
    # fallback, and taking focus from the user to avoid it is not worth it.
    if not window_in_foreground(window):
        return None, "the window is not in the foreground"
    evidence_before = dict(action_evidence)
    try:
        record, _ = require_stable_coordinate_geometry(
            element, element_record, bounds)
        x, y = screen_point(bounds, record, None, None)
        require_coordinate_delivery(window, x, y, "click")
        hits = point_hits_element(window, element, x, y)
    except (OperationFailure, RuntimeError) as refused:
        action_evidence.clear()
        action_evidence.update(evidence_before)
        reason = getattr(refused, "failure_code", None) or str(refused)
        return None, reason.split(":")[0].strip()
    if not hits:
        action_evidence.clear()
        action_evidence.update(evidence_before)
        return None, "another element is under its centre"
    send_mouse_click(x, y, "left", 1)
    action_evidence.update(
        route="coordinate_synthesis",
        geometry_sampled=True,
        geometry_stable=True,
        window_focus_verified=True,
        point_owner_verified=True,
        focus_verified=True,
        input_emitted=True,
    )
    notes.append(
        A11Y_CHANNEL
        + SYNTHESIS
        + "Clicked the element with the pointer {} after checking that "
        "the window is in front "
        "and the point is on this element. Its only AT-SPI action is a "
        "toolkit toggle that changes the checked state without the click the "
        "application listens for, so a real click was needed. {}".format(
            frame_point_text(x, y), UNVERIFIED_SYNTHESIS)
    )
    return record, None


def _perform_scroll(
    action_input,
    window,
    bounds,
    element,
    element_record,
    notes,
    action_evidence,
):
    direction = action_input.get("direction", "down")
    pages = action_input.get("pages", 1)
    point = None
    if action_input.get("window_centre") and bounds is not None:
        point = (
            bounds["x"] + bounds["width"] / 2,
            bounds["y"] + bounds["height"] / 2,
        )
    elif element_record and element_record.get("frame"):
        element_record, _ = require_stable_coordinate_geometry(
            element, element_record, bounds
        )
        point = screen_point(bounds, element_record, None, None)
    uses_wheel = point is not None and direction not in ("left", "right")
    if uses_wheel:
        require_coordinate_delivery(window, point[0], point[1], "scroll")
    else:
        require_window_focus(window, "scroll")
    route = scroll_element(direction, pages, point)
    if route == "wheel":
        action_evidence["route"] = "coordinate_synthesis"
        action_evidence["geometry_sampled"] = True
        action_evidence["geometry_stable"] = True if element is not None else None
        action_evidence["window_focus_verified"] = True
        action_evidence["point_owner_verified"] = True
        action_evidence["focus_verified"] = True
        action_evidence["input_emitted"] = True
        notes.append(
            A11Y_CHANNEL
            + SYNTHESIS
            + "Scrolled with {} wheel notch(es) {} — {}. Two caveats. First, the wheel acts on the "
            "scrollable ancestor under that point, which is not necessarily the "
            "element you named. Second, one page is APPROXIMATED as {} notches; "
            "the real distance is whatever the application's scroll settings say, "
            "so do not treat pages=1 as exactly one Page_Down. This also moves the "
            "real pointer. {}".format(
                int(math.ceil(float(pages or 1))) * WHEEL_CLICKS_PER_PAGE,
                frame_point_text(*point),
                # State where the position came from: without a target ref
                # the wheel lands at the window centre, and claiming a
                # targeted scroll would mislead every later inference.
                "no target ref was given, so this is the centre of the window "
                "— the wheel lands on whatever scrollable sits there"
                if action_input.get("window_centre")
                else "the position came from the target ref, so this scroll IS "
                     "targeted",
                WHEEL_CLICKS_PER_PAGE,
                UNVERIFIED_SYNTHESIS,
            )
        )
    else:
        action_evidence["route"] = "keyboard_synthesis"
        action_evidence["window_focus_verified"] = True
        action_evidence["focus_verified"] = True
        action_evidence["input_emitted"] = True
        notes.append(
            KEY_CHANNEL
            + SYNTHESIS
            + "Scrolled by synthesizing page keys after bringing the window to the "
            "foreground. NOTE: no target ref directed this scroll — the keys "
            "go to whatever widget currently holds focus inside the window. This "
            "route is used when the element has no usable geometry, and for "
            "horizontal scrolling, whose wheel buttons this project has not "
            "measured. If the wrong region scrolled, focus that region first "
            "(click it) and scroll again. {}".format(UNVERIFIED_SYNTHESIS)
        )
    return element_record


def _perform_type(
    action_input, window, element, element_record, notes, action_evidence
):
    element, combo_editor = resolve_combo_editor(element, element_record)
    # A caller-supplied element_index is a real target, not a hint. Focus and
    # verify that exact node before any keyboard fallback.
    if element_record is not None:
        if not combo_editor:
            focus_target_element(
                window, element, element_record, "targeted type_text")
            action_evidence["window_focus_verified"] = True
            action_evidence["target_focus_verified"] = True
        (
            write_outcome,
            before_chars,
            after_chars,
            semantic_input_emitted,
        ) = insert_text_outcome_on_node(element, action_input.get("text", ""))
    else:
        (
            write_outcome,
            before_chars,
            after_chars,
            semantic_input_emitted,
        ) = insert_text_outcome_on_node(
            find_editable_text(window), action_input.get("text", "")
        )
    if semantic_input_emitted:
        action_evidence["route"] = "editable_text_api"
        action_evidence["input_emitted"] = True
        action_evidence["readback_sampled"] = True
    if write_outcome == "effect_unknown":
        raise OperationFailure(
            "effect_unknown: the AT-SPI editable-text API may have changed "
            "the control, but exact readback was unavailable; automatic "
            "keyboard fallback was not attempted.",
            failure_code="effect_unknown",
            failure_phase="readback",
            input_events_emitted=semantic_input_emitted,
            failure_subject="element",
        )
    if write_outcome == "verified":
        action_evidence["route"] = "editable_text_api"
        action_evidence["editable_verified"] = True
        action_evidence["readback_verified"] = True
        action_evidence["readback_sampled"] = True
        action_evidence["input_emitted"] = True
        notes.append(
            A11Y_CHANNEL
            + SEMANTIC
            + "Wrote the text through the AT-SPI editable-text API and read it back "
            "to confirm it landed ({} -> {} characters). This confirms the control "
            "changed; if the control belongs to a dialog, the application may still "
            "require OK/Apply before the value takes effect.".format(
                before_chars, after_chars
            )
        )
    else:
        if combo_editor:
            raise OperationFailure(
                "The combobox editor did not confirm the text insertion; "
                "keyboard fallback was not attempted.",
                failure_code="effect_unknown" if semantic_input_emitted else "unsupported_action",
                failure_phase="readback" if semantic_input_emitted else "precondition",
                input_events_emitted=semantic_input_emitted,
                failure_subject="element")
        if element_record is None:
            require_window_focus(window, "type")
            action_evidence["window_focus_verified"] = True
        else:
            # The semantic write attempt may itself move focus. Re-establish
            # and verify the exact target immediately before global synthesis.
            focus_target_element(
                window, element, element_record,
                "targeted type_text keyboard fallback")
            action_evidence["window_focus_verified"] = True
            action_evidence["target_focus_verified"] = True
        text_route = send_text(action_input.get("text", ""))
        action_evidence["route"] = "keyboard_synthesis"
        action_evidence["focus_verified"] = True
        action_evidence["editable_verified"] = None
        action_evidence["input_emitted"] = True
        notes.append(
            KEY_CHANNEL
            + SYNTHESIS
            + "The AT-SPI editable-text write did not land, so this fell back to "
            "global key synthesis after {}. {}".format(
                "verifying the requested element held focus"
                if element_record is not None
                else "bringing the window to the foreground",
                UNVERIFIED_SYNTHESIS,
            )
            + (" " + text_route if text_route else "")
        )


def _perform_keypress(
    action_input, window, element, element_record, notes, action_evidence
):
    # Parse the key before taking focus, so a misspelled key fails without
    # first pulling the user's window to the foreground.
    parse_key(action_input.get("key", ""))
    if element_record is not None:
        focus_target_element(
            window, element, element_record, "targeted press_key")
        action_evidence["window_focus_verified"] = True
        action_evidence["target_focus_verified"] = True
    else:
        require_window_focus(window, "keypress")
        action_evidence["window_focus_verified"] = True
    send_key(action_input.get("key", ""))
    action_evidence["route"] = "keyboard_synthesis"
    action_evidence["focus_verified"] = True
    action_evidence["input_emitted"] = True
    notes.append(
        KEY_CHANNEL
        + SYNTHESIS
        + "Synthesized '{}' after {}. {}".format(
            action_input.get("key", ""),
            "verifying the requested element held focus"
            if element_record is not None
            else "bringing the window to the foreground",
            UNVERIFIED_SYNTHESIS,
        )
    )


def _perform_set_value(
    action_input, window, element, element_record, notes, action_evidence
):
    if element is None:
        raise RuntimeError("unknown target ref")
    element, combo_editor = resolve_combo_editor(element, element_record)
    value = action_input.get("value", "")
    value_outcome = set_element_value_outcome(element, value)
    if value_outcome == "effect_unknown":
        action_evidence["route"] = "value_api"
        action_evidence["input_emitted"] = True
        action_evidence["readback_sampled"] = True
        raise OperationFailure(
            "effect_unknown: the AT-SPI value API may have changed the "
            "control, but exact readback was unavailable; automatic "
            "keyboard fallback was not attempted.",
            failure_code="effect_unknown",
            failure_phase="readback",
            input_events_emitted=True,
            failure_subject="element",
        )
    if value_outcome != "verified":
        if combo_editor:
            raise OperationFailure(
                "The combobox editor did not confirm the value write; keyboard "
                "fallback was not attempted. Read the control before retrying.",
                failure_code="effect_unknown", failure_phase="readback",
                input_events_emitted=True, failure_subject="element")
        # A refused semantic write does not mean the control is read-only:
        # Blink text inputs, for example, implement neither the AT-SPI
        # Value nor the EditableText interface. As click and type_text do,
        # fall back to synthesis: focus, select all, then type. The note
        # must state that this path has no read-back confirmation and that
        # keyboard synthesis is global, so delivery is not guaranteed.
        focus_target_element(
            window, element, element_record,
            "targeted set_value keyboard fallback",
        )
        send_key("ctrl+a")
        time.sleep(0.05)
        text_route = send_text(value)
        action_evidence["route"] = "keyboard_synthesis"
        action_evidence["window_focus_verified"] = True
        action_evidence["target_focus_verified"] = True
        action_evidence["focus_verified"] = True
        action_evidence["readback_verified"] = False
        action_evidence["input_emitted"] = True
        notes.append(
            addressing_channel(element_record)
            + SYNTHESIS
            + "The AT-SPI value API refused this element (Blink and some toolkits "
            "do not implement it on text inputs), so this fell back to focusing "
            "the control, selecting all, and typing. There is no read-back "
            "confirmation on this path, and keyboard synthesis is global — verify "
            "from the tree that the control now holds what you wanted. {}".format(
                UNVERIFIED_SYNTHESIS)
            + (" " + text_route if text_route else "")
        )
    else:
        action_evidence["route"] = "value_api"
        action_evidence["readback_verified"] = True
        action_evidence["readback_sampled"] = True
        action_evidence["input_emitted"] = True
        notes.append(
            A11Y_CHANNEL
            + SEMANTIC
            + "Wrote the value through the AT-SPI API and read it back to confirm the "
            "control now holds it. Note the limit of that check: it confirms the "
            "CONTROL changed, not that the application adopted the value. Dialogs "
            "commonly keep control state separate from document state and only commit "
            "on OK/Apply — verify the actual effect (document content, a re-read of "
            "the relevant element) rather than trusting this line."
        )
