"""Linux provider module: ax actions."""

import math
import time
import unicodedata
from .foundation import (
    Atspi,
    Gdk,
    MAX_SCROLL_PAGES,
    OperationFailure,
    has_editable_text_iface,
    has_text_iface,
    node_role,
    safe,
    state_contains,
)


from .input_primitives import (
    emit_keyboard_event,
    emit_mouse_event,
    require_window_focus,
)

from .tree_walk import iter_all
from .ax_records import combo_edit_target, combo_edit_identity
from .input_method import InputMethodText


def resolve_combo_editor(element, record):
    """Keep click on the toggle; bind text editing to its recorded partner."""
    if record is None:
        return element, False
    expected = record.get("editTarget")
    projected = (record.get("controlType") == "combo box"
                 and node_role(element) == "toggle button")
    if not projected and expected is None:
        if (record.get("controlType") == "combo box"
                and not state_contains(element, Atspi.StateType.EDITABLE)):
            raise OperationFailure(
                "This combobox has no verified text editor; click it and select "
                "an option. No text input was emitted.",
                failure_code="unsupported_action", failure_phase="precondition",
                input_events_emitted=False, failure_subject="element")
        return element, False
    editor = combo_edit_target(element)
    identity = combo_edit_identity(editor)
    if expected is None or identity != expected:
        raise OperationFailure(
            "The combobox has no unique unchanged editable partner; no text "
            "input was emitted. Refresh AX observe/find, or click the combobox "
            "and select an option.",
            failure_code="stale_element_ref" if expected else "unsupported_action",
            failure_phase="precondition", input_events_emitted=False,
            failure_subject="element")
    return editor, True


KEY_ALIASES = {
    "return": "Return",
    "enter": "Return",
    "tab": "Tab",
    "escape": "Escape",
    "esc": "Escape",
    "backspace": "BackSpace",
    "back_space": "BackSpace",
    "delete": "Delete",
    "space": "space",
    "left": "Left",
    "up": "Up",
    "right": "Right",
    "down": "Down",
    "arrowleft": "Left",
    "arrowup": "Up",
    "arrowright": "Right",
    "arrowdown": "Down",
    "home": "Home",
    "end": "End",
    "pageup": "Page_Up",
    "page_up": "Page_Up",
    "prior": "Page_Up",
    "pagedown": "Page_Down",
    "page_down": "Page_Down",
    "next": "Page_Down",
    "slash": "slash",
    "forward_slash": "slash",
}

MODIFIER_KEYS = {
    "ctrl": "Control_L",
    "control": "Control_L",
    "control_l": "Control_L",
    "ctrl_l": "Control_L",
    "shift": "Shift_L",
    "shift_l": "Shift_L",
    "alt": "Alt_L",
    "alt_l": "Alt_L",
    "super": "Super_L",
    "super_l": "Super_L",
    "win": "Super_L",
    "cmd": "Super_L",
}


def normalize_key_expression(key):
    text = str(key or "").strip()
    if not text.startswith("<"):
        return text
    modifiers = []
    while text.startswith("<"):
        end = text.find(">")
        if end <= 1:
            break
        modifiers.append(text[1:end])
        text = text[end + 1:]
    if not text:
        return str(key or "").strip()
    return "+".join(modifiers + [text])


# Gdk.keyval_from_name returns VoidSymbol, not 0, for an unknown name.
GDK_VOID_SYMBOL = 0xFFFFFF


def keysym(name):
    """Resolve a key name to an X11 keysym, or 0 when it has none.

    Punctuation is accepted as the character itself (",", ".", "/") or by
    its keysym name in any case ("COMMA", "plus"), and named keys in any
    case ("INSERT", "caps_lock"), because callers write keys the way they
    appear on the keyboard.
    """
    candidates = [name]
    if len(name) > 1:
        candidates.append("_".join(part.capitalize() for part in name.split("_")))
        # Punctuation keysyms are lowercase names ("comma", "plus").
        candidates.append(name.lower())
    for candidate in candidates:
        sym = Gdk.keyval_from_name(candidate)
        if sym and sym != GDK_VOID_SYMBOL:
            return sym
    if len(name) == 1:
        return Gdk.unicode_to_keyval(ord(name))
    return 0


def keycode(name):
    """Resolve a key name to an X11 hardware keycode.

    Atspi.generate_keyboard_event(keyval, keystring, synth_type) expects a
    hardware keycode as its first argument in PRESS, RELEASE and
    PRESSRELEASE modes; only SYM mode accepts a keysym.

    A keysym passed there is not rejected but truncated to its low 8 bits,
    which emits an unrelated key:

        Escape    keysym 65307 -> 27  -> emits 'r'
        Return    keysym 65293 -> 13  -> emits '4'
        Delete    keysym 65535 -> 255 -> emits XF86RFKill
        Control_L keysym 65507 -> 227 -> the modifier is never pressed

    Silently injecting the wrong key is more dangerous than an error, so
    every resolution failure raises.
    """
    if Gdk is None:
        raise RuntimeError("Gdk unavailable; cannot resolve keycode for: " + name)
    sym = keysym(name)
    if not sym:
        raise RuntimeError("Unsupported key: " + name)
    keymap = Gdk.Keymap.get_default()
    ok, entries = keymap.get_entries_for_keyval(sym)
    if not ok or not entries:
        raise RuntimeError("No hardware keycode mapped for key: " + name)
    return int(entries[0].keycode)


def parse_key(key):
    """Parse a key expression into (modifier keycodes, normalized main key).

    No events are sent. Parsing must be possible before delivery because
    taking focus has side effects (it interrupts the user): a misspelled key
    must fail before the window is raised, not after.
    """
    normalized_expression = normalize_key_expression(key)
    parts = [part for part in normalized_expression.split("+") if part]
    if not parts:
        raise RuntimeError("Unsupported key: " + str(key))
    main = parts[-1]
    codes = []
    for modifier in parts[:-1]:
        name = MODIFIER_KEYS.get(modifier.lower())
        if name is None:
            # Reject unknown modifiers: skipping them would silently turn a
            # typo such as "ctrl+shft+s" into a bare "s", which is confusing
            # and very hard to diagnose.
            raise RuntimeError(
                "Unsupported modifier: {}. Use CTRL, SHIFT, ALT, or SUPER; "
                "Control_L/Ctrl_L and GTK forms such as <Control>s are also accepted."
                .format(modifier))
        codes.append(keycode(name))
    # A bare modifier ("SHIFT") is a key of its own.
    normalized = KEY_ALIASES.get(main.lower()) or MODIFIER_KEYS.get(
        main.lower(), main
    )
    # A single-character key may use STRING only without modifiers. STRING
    # means "insert this text" and bypasses modifiers already pressed, so
    # ctrl+a would degrade into typing a literal 'a'.
    if not (len(normalized) == 1 and not codes):
        keycode(normalized)  # Fail here if the main key cannot be resolved.
    return codes, normalized


def send_key(key):
    codes, normalized = parse_key(key)
    if len(normalized) == 1 and ord(normalized) >= 128 and not codes:
        # A literal Unicode key has the same keymap race as a text payload.
        return send_text(normalized)
    pressed = []
    emitted = False
    primary_error = None
    try:
        for code in codes:
            emitted = emit_keyboard_event(
                code,
                None,
                Atspi.KeySynthType.PRESS,
                "modifier press for {!r}".format(key),
                emitted,
            )
            pressed.append(code)
        if len(normalized) == 1 and not pressed:
            emitted = emit_keyboard_event(
                0,
                normalized,
                Atspi.KeySynthType.STRING,
                "text key {!r}".format(normalized),
                emitted,
            )
        else:
            emitted = emit_keyboard_event(
                keycode(normalized),
                None,
                Atspi.KeySynthType.PRESSRELEASE,
                "key {!r}".format(normalized),
                emitted,
            )
    except Exception as exc:
        primary_error = exc

    # Release every pressed modifier even if one release fails. Stopping after
    # the first failed release can leave the remaining modifiers stuck.
    release_error = None
    for code in reversed(pressed):
        try:
            emitted = emit_keyboard_event(
                code,
                None,
                Atspi.KeySynthType.RELEASE,
                "modifier release for {!r}".format(key),
                emitted,
            )
        except Exception as exc:
            if release_error is None:
                release_error = exc
    if primary_error is not None:
        raise primary_error
    if release_error is not None:
        raise release_error


# Keep the public keyboard-input bound; input method delivery also has a
# whole-payload deadline below the provider timeout. On the key event path,
# 500 x NON_ASCII_KEY_INTERVAL_SECONDS = 17.5 s stays inside the 30 s
# provider timeout.
MAX_SYNTHESIZED_NON_ASCII_CHARACTERS = 500

# Key event path only: pause after each synthesized non-ASCII character.
# libatspi types a character without a keycode in the current layout by
# rebinding a spare keycode to its keysym. A burst of such characters reuses
# that keycode before the application has read the previous mapping, so one
# STRING event with "Grüße 你好" can arrive as "Gr好好e 好好". 20 ms still
# lost characters occasionally; 35 ms did not.
NON_ASCII_KEY_INTERVAL_SECONDS = 0.035

# Key event path only. AT-SPI STRING sends LF and Tab as control keysyms:
# terminals receive the raw bytes, but GTK text views drop them. Enter and
# Tab keys reach both.
TEXT_CONTROL_KEYS = {"\n": "Return", "\t": "Tab"}

INPUT_METHOD_TEXT_NOTE = (
    "The text contained non-ASCII characters, line feeds or tabs, so it was "
    "composed through the desktop input method.")
KEY_EVENT_TEXT_NOTE = (
    "No supported desktop input method was active, so the text was typed as "
    "key events: non-ASCII characters one at a time, each line feed as an "
    "Enter key and each tab as a Tab key. Enter and Tab keep their normal "
    "effect in the focused control, for example submitting a form or moving "
    "focus.")


def key_event_runs(text):
    """Split text into the ordered key event stream, before any input.

    Returns ("text", ascii_run), ("char", non_ascii_character) and
    ("key", keycode) items. Raises when a character has no key event form,
    so that the call fails before it emits anything.
    """
    runs = []
    ascii_run = ""
    for char in text:
        if char in TEXT_CONTROL_KEYS:
            kind, value = "key", keycode(TEXT_CONTROL_KEYS[char])
        elif char == "\0" or unicodedata.category(char) == "Cs":
            # STRING is a C string: it stops at NUL and cannot encode a lone
            # surrogate. Other control characters keep their STRING keysyms.
            raise OperationFailure(
                "No supported desktop input method is active, and key events "
                "cannot type the character U+{:04X}. No input was emitted. "
                "Remove the character and type the text again.".format(ord(char)),
                failure_code="unsupported_action",
                failure_phase="precondition",
                input_events_emitted=False,
            )
        elif ord(char) >= 128:
            kind, value = "char", char
        else:
            ascii_run += char
            continue
        if ascii_run:
            runs.append(("text", ascii_run))
            ascii_run = ""
        runs.append((kind, value))
    if ascii_run:
        runs.append(("text", ascii_run))
    return runs


def send_text(text):
    """Type text at the keyboard focus.

    Returns a note naming the route when the text needed more than plain
    ASCII key events, otherwise None.
    """
    text = str(text)
    non_ascii = sum(1 for char in text if ord(char) >= 128)
    if non_ascii > MAX_SYNTHESIZED_NON_ASCII_CHARACTERS:
        raise OperationFailure(
            "text_too_long_for_keyboard: {} non-ASCII characters exceed the "
            "{} that keyboard synthesis can type in one call; no input was "
            "emitted. Split the text into shorter type steps, or type into a "
            "ref of an editable text field, which inserts the whole text "
            "through the accessibility API.".format(
                non_ascii, MAX_SYNTHESIZED_NON_ASCII_CHARACTERS),
            failure_code="invalid_argument",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    def ascii_text(value):
        return emit_keyboard_event(
            0, value, Atspi.KeySynthType.STRING, "text payload")

    if not (non_ascii or "\n" in text or "\t" in text):
        if text:
            ascii_text(text)
        return None
    try:
        composer = InputMethodText()
    except OperationFailure:
        # No IBus Simple input context. Opening the context emits nothing.
        composer = None
    if composer is not None:
        # Keep ASCII and Unicode in one ordered stream. Mixing X key events
        # with asynchronous input-method commits can reorder adjacent text.
        # AT-SPI STRING drops literal LF and Tab; compose their codepoints too.
        composer.send(text, send_key, ascii_text)
        return INPUT_METHOD_TEXT_NOTE
    emitted = False
    for kind, value in key_event_runs(text):
        if kind == "key":
            emitted = emit_keyboard_event(
                value, None, Atspi.KeySynthType.PRESSRELEASE,
                "text control key", emitted)
            continue
        emitted = emit_keyboard_event(
            0, value, Atspi.KeySynthType.STRING, "text payload", emitted)
        if kind == "char":
            time.sleep(NON_ASCII_KEY_INTERVAL_SECONDS)
    return KEY_EVENT_TEXT_NOTE


def find_editable_text(root):
    """Return the one editable control that should receive text, or None.

    Taking the first node in tree order that implements EditableText is not
    enough. In applications such as gedit, early nodes are often hidden
    placeholders that implement EditableText without the EDITABLE state;
    Atspi.EditableText.insert_text() on them still returns True while the
    character count stays the same, so the text is silently lost and the
    tool reports success.

    The node must therefore be both EDITABLE and FOCUSED, and exactly one
    candidate must exist. Without a clear focus owner nothing is guessed
    from visibility or tree order; the caller uses the keyboard path, which
    is reported as unverified.
    """
    focused = []
    for node in iter_all(root):
        if not (has_editable_text_iface(node) and has_text_iface(node)):
            continue
        if not state_contains(node, Atspi.StateType.EDITABLE):
            continue
        if state_contains(node, Atspi.StateType.FOCUSED):
            focused.append(node)
    if len(focused) == 1:
        return focused[0]
    return None


def read_all_text(text_iface):
    """Return the control's full text, or None if it cannot be read.

    None distinguishes an unreadable control from an empty one.
    """
    if text_iface is None:
        return None
    count = safe(lambda: Atspi.Text.get_character_count(text_iface))
    if count is None:
        return None
    count = int(count)
    if count <= 0:
        return ""
    return safe(lambda: Atspi.Text.get_text(text_iface, 0, count))


def text_insertion_point(text_iface):
    """Return (offset, selection) for inserting text.

    Priority: start of a non-empty selection, then the caret, then the end.
    When a selection is returned, the caller must delete it first: typing on
    a real keyboard replaces the selection, and `type_text` should behave
    the same way.

    The end is used only when the caret cannot be read (some controls do not
    implement caret queries).
    """
    count = safe(lambda: Atspi.Text.get_character_count(text_iface))
    count = int(count) if count is not None else 0

    selections = int(safe(lambda: Atspi.Text.get_n_selections(text_iface), 0) or 0)
    for index in range(max(selections, 0)):
        span = safe(lambda index=index: Atspi.Text.get_selection(text_iface, index))
        if span is None:
            continue
        start = int(safe(lambda: span.start_offset, 0) or 0)
        end = int(safe(lambda: span.end_offset, 0) or 0)
        if end > start:
            return start, (start, end)

    caret = safe(lambda: Atspi.Text.get_caret_offset(text_iface))
    if caret is not None and 0 <= int(caret) <= count:
        return int(caret), None
    return count, None


def insert_text_outcome_on_node(node, text):
    """Write text and distinguish verified, confirmed-no-change, and unknown."""
    if node is None:
        return "no_change", 0, 0, False
    editable = safe(node.get_editable_text_iface)
    text_iface = safe(node.get_text_iface)
    if editable is None or text_iface is None:
        return "no_change", 0, 0, False
    payload = str(text)
    if not payload:
        count = int(safe(lambda: Atspi.Text.get_character_count(text_iface), 0) or 0)
        return "verified", count, count, False

    original_text = read_all_text(text_iface)
    if original_text is None:
        return "no_change", 0, 0, False
    offset, selection = text_insertion_point(text_iface)
    selection_deleted = False
    if selection is not None:
        accepted = safe(
            lambda: Atspi.EditableText.delete_text(
                editable, selection[0], selection[1]),
            False,
        )
        after_delete = read_all_text(text_iface)
        expected_after_delete = (
            original_text[:selection[0]] + original_text[selection[1]:]
        )
        if after_delete is None and accepted:
            return "effect_unknown", len(original_text), len(original_text), True
        if after_delete != expected_after_delete:
            if after_delete is not None and after_delete != original_text:
                return (
                    "effect_unknown",
                    len(original_text),
                    len(after_delete),
                    True,
                )
            return "no_change", len(original_text), (
                len(after_delete) if after_delete is not None else len(original_text)
            ), False
        if not accepted and after_delete == original_text:
            return "no_change", len(original_text), len(original_text), False
        selection_deleted = after_delete != original_text

    # Deleting the selection changed the content, so the baseline for exact
    # verification must be read again after the deletion.
    before_text = read_all_text(text_iface)
    if before_text is None:
        return (
            "effect_unknown" if selection_deleted else "no_change",
            len(original_text),
            len(original_text),
            selection_deleted,
        )
    before = len(before_text)
    offset = max(0, min(offset, before))

    accepted = safe(
        # AT-SPI positions count characters; insert_text length counts UTF-8
        # bytes. A character count silently truncates multibyte input.
        lambda: Atspi.EditableText.insert_text(
            editable, offset, payload, len(payload.encode("utf-8"))),
        False,
    )
    # Neither insert_text's return value nor the character count can be
    # trusted: the wrong control can return True, and wrong text can keep the
    # same length. Read the full content back and check the exact insertion.
    after_text = read_all_text(text_iface)
    if after_text is None:
        return "effect_unknown", before, before, True
    expected = before_text[:offset] + payload + before_text[offset:]
    if after_text == expected:
        return "verified", before, len(after_text), True
    if after_text != before_text:
        return "effect_unknown", before, len(after_text), True
    if selection_deleted:
        return "effect_unknown", before, len(after_text), True
    return "no_change", before, len(after_text), bool(accepted)


def focus_target_element(window, element, element_record, what, timeout=1.0):
    """Focus one resolved element and verify that exact target owns focus."""
    if element is None or element_record is None:
        raise RuntimeError("{} requires a resolvable target ref".format(what))
    require_window_focus(window, what)

    def target_is_focused():
        return state_contains(element, Atspi.StateType.FOCUSED)

    def not_focusable(message):
        # A classified precondition failure: an unclassified exception reached
        # the caller as an opaque "input_failed" runtime error, so the model
        # could not tell that the element itself refuses keyboard focus (for
        # example a LibreOffice toolbar item group) and retried the same call.
        return OperationFailure(
            message + " Target a focusable child such as its text field or "
            "combo box, or click the element first.",
            failure_code="target_not_focusable",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="element",
        )

    component = safe(element.get_component_iface)
    if component is None:
        if target_is_focused():
            return
        raise not_focusable(
            "{} could not focus the requested element; no keyboard input was "
            "emitted.".format(what)
        )
    accepted = safe(lambda: Atspi.Component.grab_focus(component), False)
    if not accepted and target_is_focused():
        return
    if not accepted:
        raise not_focusable(
            "{} could not focus the requested element; no keyboard input was "
            "emitted.".format(what)
        )
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if target_is_focused():
            return
        time.sleep(0.02)
    if node_role(element) == "table cell":
        # Spreadsheet cells accept grab_focus and move the cell cursor, but
        # the cell objects never report FOCUSED, so the cursor position
        # cannot be confirmed. Selecting the cell does not move the cursor
        # either. A cell has no focusable child, so name the routes that do
        # work: the cell cursor moved by keys, or set_value for a number.
        raise OperationFailure(
            "{} requested focus for the cell, but the cell did not report "
            "focus within {:.1f}s, so the cell cursor position is unconfirmed; "
            "no keyboard input was emitted. Move the cell cursor there with "
            "keypress (or click the cell with GUI), then type without a ref. "
            "For a number, set_value on the cell ref writes it directly."
            .format(what, timeout),
            failure_code="target_not_focusable",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="element",
        )
    raise not_focusable(
        "{} requested focus for the element, but that exact target did not report "
        "focus within {:.1f}s; no keyboard input was emitted.".format(what, timeout)
    )


def set_element_value_outcome(node, value):
    """Set a value without converting unavailable readback into safe failure."""
    value_iface = safe(node.get_value_iface) if node is not None else None
    if value_iface is not None:
        try:
            numeric = float(value)
        except (TypeError, ValueError):
            numeric = None
        if numeric is not None:
            before = safe(lambda: Atspi.Value.get_current_value(value_iface))
            try:
                accepted = bool(Atspi.Value.set_current_value(value_iface, numeric))
            except Exception:
                accepted = False
            if accepted:
                after = safe(lambda: Atspi.Value.get_current_value(value_iface))
                if after is None:
                    return "effect_unknown"
                if math.isclose(float(after), numeric, rel_tol=1e-9, abs_tol=1e-9):
                    return "verified"
                if before is not None and not math.isclose(
                        float(after), float(before), rel_tol=1e-9, abs_tol=1e-9):
                    return "effect_unknown"
    if node is not None and has_editable_text_iface(node):
        editable = safe(node.get_editable_text_iface)
        if editable is not None:
            payload = str(value)
            text_iface = safe(node.get_text_iface)
            before = read_all_text(text_iface)
            safe(
                lambda: Atspi.EditableText.set_text_contents(editable, payload),
                False,
            )
            # Like insert_text, set_text_contents returns True for controls
            # that ignore the write. Only an exact readback of the requested
            # value counts as verified; any other value must not be described
            # as "the control now holds it".
            after = read_all_text(text_iface)
            if after is None:
                return "effect_unknown"
            if after == payload:
                return "verified"
            if after != before:
                return "effect_unknown"
            return "no_change"
    return "no_change"


# Retry count and interval for application resolution; see resolve_app().
# Enumeration can fail transiently while an application rebuilds its
# windows, and treating "unreadable" as "absent" sends the agent down an
# entirely wrong path.
RESOLVE_APP_ATTEMPTS = 3
RESOLVE_APP_RETRY_SECONDS = 0.3


# Wheel clicks per scrolled "page". One click usually scrolls three lines,
# so five clicks is about 15 lines. This is an approximation that depends on
# the application's scroll settings, so notes must call it approximate and
# must not imply that pages=1 equals exactly one Page_Down.
WHEEL_CLICKS_PER_PAGE = 5


def scroll_element(direction, pages, point=None):
    """Scroll with the mouse wheel at `point`, or send Page keys to the focus.

    Wheel scrolling acts at a position: on the scrollable ancestor under the
    pointer, which is not necessarily the requested element itself. That is
    the meaning of "scroll here", but not quite "scroll this control", and
    notes must say so.

    Horizontal scrolling still uses Left/Right keys because the horizontal
    wheel buttons (b6/b7) have not been verified.

    Return the route actually taken ("wheel" or "keys") so the note can
    report it accurately.
    """
    pages = float(pages or 1)
    if not math.isfinite(pages) or pages <= 0 or pages > MAX_SCROLL_PAGES:
        raise RuntimeError(
            "pages must be greater than 0 and at most {}".format(MAX_SCROLL_PAGES))
    repeat = max(1, int(math.ceil(pages)))
    horizontal = direction in ("left", "right")
    if point is not None and not horizontal:
        x, y = point
        emitted = emit_mouse_event(x, y, "abs", False)
        event = "b4c" if direction == "up" else "b5c"
        for _ in range(repeat * WHEEL_CLICKS_PER_PAGE):
            emitted = emit_mouse_event(x, y, event, emitted)
            time.sleep(0.03)
        return "wheel"

    key = "Page_Down"
    if direction == "up":
        key = "Page_Up"
    elif direction == "left":
        key = "Left"
    elif direction == "right":
        key = "Right"
    for _ in range(repeat):
        send_key(key)
        time.sleep(0.04)
    return "keys"


# Every action note carries two orthogonal labels, because they describe
# different things and merging them would lose information on both sides.
# The label constants are defined in ax_dispatch.
#
# Addressing axis: how the target was located, which determines how to
# verify it.
#   [a11y]      through an element of the accessibility tree
#   [gui]       through screen coordinates, without the tree
#   [keyboard]  through the current input focus, without either channel
#
# Execution axis: how the located action was delivered.
#   [semantic]   AT-SPI semantic action or editable-text API
#   [synthesis]  global XTEST input synthesis
#
# The axes combine. The most important cell is `[a11y][synthesis]`: a click
# synthesized at coordinates taken from the tree. With only an execution
# label it would read as pure synthesis, and "the agent never used the tree"
# could not be told apart from "it did, and fell back after the semantic
# action failed".
