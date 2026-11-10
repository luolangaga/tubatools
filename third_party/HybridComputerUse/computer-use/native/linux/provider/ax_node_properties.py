"""Linux provider module: AX node properties and tree-line rendering."""

import re
from .foundation import (
    Atspi,
    CLICK_COVERED_ACTIONS,
    DEFAULT_TEXT_LIMIT,
    NON_SELF_ACTIONS,
    OBJECT_REPLACEMENT,
    UNIVERSAL_ACTIONS,
    child_count,
    limit_text,
    node_name,
    node_role,
    normalized_action,
    safe,
    state_contains,
)

from .primitives import quoted


def node_actions(node):
    """Read the node's action table once and derive both rendering facts.

    Return (names of actions that click does not cover, whether an
    invocable click entry exists).

    The table must be read in a single pass. Queried repeatedly, the
    LibreOffice ATK bridge logs

        (soffice): CRITICAL: impl_get_NActions:
                   assertion 'ATK_IS_ACTION (user_data)' failed

    and under dense querying the application exits entirely (soffice
    disappeared after three or four dialog round trips). Rendering already
    reads the table once per node; reading it again for the
    `[has-click-action]` mark through preferred_action_index() would double
    the load on a bridge known to be fragile. Action names and descriptions
    are therefore read in the same pass.
    """
    # Check for the Action interface before asking for the action count.
    # get_n_actions() on an object without it triggers the ATK assertion
    # above, and most nodes (panels, fillers, labels, other containers) lack
    # the interface.
    if safe(node.get_action_iface) is None:
        return [], False
    count = int(safe(node.get_n_actions, 0) or 0)
    names = []
    has_click_entry = False
    for index in range(count):
        name = str(safe(lambda i=index: node.get_action_name(i), "") or "")
        description = str(
            safe(lambda i=index: node.get_action_description(i), "") or ""
        )
        label = name or description
        lower = label.strip().lower()
        # Same criteria as preferred_action_index(): an exact match or the
        # activate/click/press substring fallback. The two must agree, or the
        # mark would misreport what click can invoke.
        if lower in CLICK_COVERED_ACTIONS or (
            normalized_action(lower) not in NON_SELF_ACTIONS
            and ("activate" in lower or "click" in lower or "press" in lower)
        ):
            has_click_entry = True
        if not label or label in names:
            continue
        if lower in CLICK_COVERED_ACTIONS:
            continue
        # Skip actions that every node has, so is_structural_filler still
        # recognizes filler nodes.
        if normalized_action(lower) in UNIVERSAL_ACTIONS:
            continue
        names.append(label)
    return names, has_click_entry


def accessible_id(node):
    getter = getattr(node, "get_accessible_id", None)
    return str(safe(getter, "") or "") if callable(getter) else ""


def text_value_with_status(node, text_limit=DEFAULT_TEXT_LIMIT):
    """Return text plus whether the provider read completed reliably."""
    try:
        text_iface = node.get_text_iface()
    except Exception:
        return "", False
    if text_iface is None:
        return "", True
    try:
        count = int(Atspi.Text.get_character_count(text_iface) or 0)
        if count <= 0:
            return "", True
        end_offset = count if text_limit is None else min(count, text_limit + 1)
        value = str(
            Atspi.Text.get_text(text_iface, 0, end_offset) or ""
        )
    except Exception:
        return "", False
    return limit_text(value, text_limit=text_limit), True


def text_value(node, text_limit=DEFAULT_TEXT_LIMIT):
    value, _complete = text_value_with_status(
        node, text_limit=text_limit
    )
    return value


def numeric_value(node):
    value_iface = safe(node.get_value_iface)
    if value_iface is None:
        return ""
    current = safe(lambda: Atspi.Value.get_current_value(value_iface))
    if current is None:
        return ""
    return str(current)


# Maximum nodes scanned below a dropdown to find the selected option. Option
# lists usually hold a few to a few dozen entries, so 200 is enough, and the
# cap keeps the cost of this path bounded.
SELECTION_SCAN_BUDGET = 200


def committed_dropdown_value(node):
    """Read a toolkit's committed combo value, or None for option readback.

    GTK's combo Selection reports the active item; its popup Selection can
    instead report a highlighted item. Qt's AT-SPI bridge exposes the combo's
    Value text as its accessible name, including when an accessible label
    was set. Other providers' names can be labels, even when they match an
    option. An unreadable committed value must not fall back to popup state.
    """
    from .actionability import application_toolkit

    if node_role(node) != "combo box":
        return None
    toolkit = application_toolkit(node)
    if toolkit == "qt":
        return str(node_name(node) or "").strip()
    if toolkit == "gtk":
        selection = safe(node.get_selection_iface)
        count = safe(lambda: Atspi.Selection.get_n_selected_children(selection))
        if count == 1:
            selected = safe(lambda: Atspi.Selection.get_selected_child(selection, 0))
            if selected is not None:
                return str(node_name(selected) or "").strip()
        return ""
    return None


def selected_option_name(node):
    """Return the name of the dropdown's selected option, or "".

    Without it, dropdown values are invisible in the tree: Chrome's print
    dialog shows the combo boxes "Margins", "Destination" and "Scale" with
    names but no values, so the agent can neither decide whether a change is
    needed nor confirm that one worked.

    The value is not on the Value interface (Chrome combo boxes do not
    implement it) or the Text interface (it reads back as U+FFFC). It is the
    SELECTED state of a descendant menu item, which is readable even while
    the dropdown is closed; opening every dropdown first would be far too
    costly.

    Some dropdowns expose no selection (Chrome's "Layout" combo box, for
    example). In that case return an empty string rather than guess: a
    missing value is better than an invented one.

    GTK and Qt need committed-value readback because their popup's SELECTED
    state can describe a highlighted or toggled item without committing it.
    """
    if node is None:
        return ""
    committed = committed_dropdown_value(node)
    if committed is not None:
        return committed
    stack = [node]
    seen = 0
    while stack and seen < SELECTION_SCAN_BUDGET:
        current = stack.pop()
        seen += 1
        if current is not node:
            is_selected = state_contains(current, Atspi.StateType.SELECTED)
            if is_selected:
                name = str(node_name(current) or "").strip()
                if name:
                    return name
        for index in range(min(child_count(current), 100)):
            child = safe(lambda: current.get_child_at_index(index))
            if child is not None:
                stack.append(child)
    return ""


# Maximum embedded objects read to build an editor's value.
EMBEDDED_EDITOR_LINE_LIMIT = 200


def embedded_editor_nodes(node):
    """Return the text owners of an editor made of embedded objects.

    Chromium exposes a contenteditable editor (CodeMirror in Electron apps
    such as Joplin) as an EDITABLE entry whose Text is one U+FFFC per line;
    each line is an embedded child reached through Hypertext. The lines are
    joined with newlines. Non-editable containers (a paragraph holding a
    link) are left alone: their children render on their own.
    """
    text_iface = safe(node.get_text_iface)
    if text_iface is None:
        return []
    count = int(safe(lambda: Atspi.Text.get_character_count(text_iface), 0) or 0)
    if count <= 0 or count > EMBEDDED_EDITOR_LINE_LIMIT:
        return []
    own = str(safe(lambda: Atspi.Text.get_text(text_iface, 0, count), "") or "")
    # The state read comes last: only nodes whose text is all embedded
    # objects pay for it, so ordinary records keep one read per field.
    if not own or own.strip(OBJECT_REPLACEMENT):
        return []
    if not state_contains(node, Atspi.StateType.EDITABLE):
        return []
    hypertext = safe(node.get_hypertext_iface)
    if hypertext is None:
        return []
    nodes = []
    links = int(safe(lambda: Atspi.Hypertext.get_n_links(hypertext), 0) or 0)
    for index in range(min(links, EMBEDDED_EDITOR_LINE_LIMIT)):
        link = safe(lambda index=index: Atspi.Hypertext.get_link(hypertext, index))
        embedded = safe(lambda: Atspi.Hyperlink.get_object(link, 0)) if link else None
        if embedded is not None:
            nodes.append(embedded)
    return nodes


def embedded_editor_text(node, text_limit=DEFAULT_TEXT_LIMIT):
    """Render the same embedded text owners used for text selection."""
    lines = [text_value(child, text_limit=None)
             for child in embedded_editor_nodes(node)]
    return limit_text("\n".join(lines), text_limit=text_limit)


def element_value(node, text_limit=DEFAULT_TEXT_LIMIT, role=None):
    """Return the node's text or numeric value; see selected_option_name().

    ``role`` passes a role the caller already read, so it is not read from
    the provider a second time.
    """
    direct = text_value(node, text_limit=text_limit) or numeric_value(node)
    if direct:
        return direct
    embedded = embedded_editor_text(node, text_limit=text_limit)
    if embedded:
        return embedded
    # Only dropdown roles pay for the descendant scan.
    if role is None:
        role = node_role(node)
    role = str(role or "")
    if role in ("combo box", "list box"):
        return limit_text(selected_option_name(node), text_limit=text_limit)
    return direct


def positive_int(value, fallback):
    if isinstance(value, bool):
        return fallback
    if isinstance(value, float) and not value.is_integer():
        return fallback
    try:
        integer = int(value)
    except (TypeError, ValueError):
        return fallback
    return integer if integer > 0 else fallback


def parse_text_limit(value, fallback=DEFAULT_TEXT_LIMIT):
    if isinstance(value, str) and value.lower() == "max":
        return None
    return positive_int(value, fallback)


# Decision-relevant states, rendered only on their non-default side. Marking
# every node enabled or not-focused would drown the signal; checked,
# expanded, selected and focused are what the agent decides on.
NOTABLE_STATES = (
    ("CHECKED", "checked"),
    ("EXPANDED", "expanded"),
    ("SELECTED", "selected"),
    ("FOCUSED", "focused"),
)


def plain_text_from_rich_text(value):
    """Convert a Qt rich-text tooltip to plain text.

    Qt stores tooltips as complete HTML documents with CSS in `<head>`;
    unconverted, a handful of them (VLC's preferences) can consume most of an
    observation budget while carrying one sentence of information each.

    Only text starting with `<html`, the Qt rich-text marker, is converted.
    Ordinary text may legitimately contain angle brackets (code, templates,
    math), and stripping it would alter the data the agent reads.
    """
    text = str(value or "")
    if not text.lstrip().lower().startswith("<html"):
        return text
    # `<head>` holds only CSS, which would leak as
    # `p, li { white-space: pre-wrap; }` after tag stripping, so the whole
    # block is dropped instead of stripping tags one by one.
    text = re.sub(r"(?is)<head>.*?</head>", " ", text)
    text = re.sub(r"(?is)<style.*?</style>", " ", text)
    text = re.sub(r"(?i)<br\s*/?>", " ", text)
    text = re.sub(r"(?s)<[^>]+>", "", text)
    for entity, char in (("&amp;", "&"), ("&lt;", "<"), ("&gt;", ">"),
                         ("&quot;", '"'), ("&nbsp;", " ")):
        text = text.replace(entity, char)
    return " ".join(text.split())


def node_description(node, text_limit=DEFAULT_TEXT_LIMIT, facts=None):
    """Return the control's description (AT-SPI `get_description`).

    GTK usually fills it with the tooltip, and it is often the only readable
    label. In Nautilus the toolbar buttons `Go back`, `Go forward`, `Search`
    and `Show list` have empty names, and three `toggle button Menu`
    controls with identical names differ only by description
    (`Show operations`, `View options`). Without it, core file-manager
    actions such as back and forward render as a bare `push button`, and the
    agent can only guess pixel coordinates.

    GTK's convention is the opposite of macOS, which puts the readable label
    in AXTitle, so Linux has to read this extra field.

    The description is rendered separately instead of replacing the name,
    because the name is part of element identity (matching uses role +
    name) and rewriting it would break matching across snapshots.
    """
    getter = getattr(node, "get_description", None)
    if not callable(getter):
        raw = ""
    elif facts is not None:
        raw = str(facts.value("description", "") or "")
    else:
        raw = str(safe(getter, "") or "")
    return limit_text(plain_text_from_rich_text(raw), text_limit=text_limit)


PLACEHOLDER_ROLES = {
    "combo box",
    "entry",
    "password text",
    "search box",
    "text",
    "text area",
    "text box",
    "text field",
}


def role_supports_placeholder(role):
    normalized = str(role or "").strip().lower().replace("_", " ")
    return (
        normalized in PLACEHOLDER_ROLES
        or normalized.replace(" ", "") in {
            "searchbox",
            "textarea",
            "textbox",
            "textfield",
        }
    )


def placeholder_text(node, text_limit=DEFAULT_TEXT_LIMIT, role=None):
    """Return the control's placeholder (AT-SPI `placeholder-text` attribute).

    Equivalent to macOS `placeholderValue`. It must be rendered apart from
    real content: a placeholder looks like content although the control is
    empty, so mixing the two makes the agent skip input it still needs or
    read the hint as data.

    `get_attributes()` is an optional and expensive AT-SPI call and must not
    be probed on every panel, shape or table cell; Impress exited when its
    whole tree was scanned this way after a document structure change.
    Placeholders exist only on text-input roles, so the role is checked first
    and the attributes are read once for the nodes that can carry one.
    """
    effective_role = node_role(node) if role is None else role
    if not role_supports_placeholder(effective_role):
        return ""
    getter = getattr(node, "get_attributes", None)
    attributes = safe(getter) if callable(getter) else {}
    for key in ("placeholder-text", "placeholder"):
        value = attributes.get(key)
        if value:
            return limit_text(str(value), text_limit=text_limit)
    return ""


TEXT_ATTRIBUTES_OF_INTEREST = (
    "justification", "size", "weight", "style", "underline", "strikethrough",
    "family-name", "fg-color", "bg-color", "line-height", "indent",
    "left-margin", "right-margin", "text-decoration",
)


def text_attributes(node):
    """Read selected text-format properties without claiming an empty result."""
    iface = safe(node.get_text_iface)
    if iface is None:
        return None
    raw = safe(lambda: Atspi.Text.get_default_attributes(iface))
    if not raw:
        return None
    try:
        table = dict(raw)
    except Exception:
        return None
    return {key: table[key] for key in TEXT_ATTRIBUTES_OF_INTEREST if key in table}


def render_element_line(record, render_depth, boxes=True):
    """Render one element record as a tree line.

    Grammar:

        <indent><index> <role> "<name>" [<states>] [desc="..."]
            [placeholder="..."] [actions=a,b] {x,y,w,h}: "<value>"

    - All free text is quoted, so a name cannot be confused with a field
      separator.
    - Extra attributes go in square brackets, such as `[checked]` and
      `[level=1]`.
    - Geometry is compacted to `{x,y,w,h}`: 14 characters where
      `Frame: {x: 687, y: 23, width: 64, height: 46}` takes 45, with no loss
      of information. Geometry is 35-50% of a rendered tree, so this saves
      about two thirds of it.
    - `boxes=False` omits geometry entirely. Actions take coordinates from the stored record, not from this
      line.
    - The value comes last, after a colon, as in
      `- textbox: Enter your name`.
    - Empty segments are omitted.
    - The index leads the line; the Go side keys rendered lines by it.
    """
    role = record["localizedControlType"] or record["controlType"] or "element"
    title = record["name"] or record["automationId"] or ""
    parts = ["{} {}".format(record["index"], role)]
    if title:
        parts.append(quoted(title))
    state_seg = record.get("states", "")
    if state_seg:
        parts.append(state_seg.strip())
    if record.get("description") and record["description"] != title:
        # A separate segment, not merged into the name, which is part of
        # element identity. GTK often puts the only readable label here,
        # especially on icon-only buttons.
        parts.append("[desc=" + quoted(record["description"]) + "]")
    if record.get("placeholder") and record["placeholder"] != title:
        # A placeholder is not content: the control is actually empty, so it
        # must never be merged into the value.
        parts.append("[placeholder=" + quoted(record["placeholder"]) + "]")
    if record["actions"]:
        parts.append("[actions=" + ",".join(record["actions"]) + "]")
    if boxes and record["frame"] is not None:
        f = record["frame"]
        parts.append("{{{0},{1},{2},{3}}}".format(
            round(f["x"]), round(f["y"]), round(f["width"]), round(f["height"])))
    line = ("\t" * (render_depth + 1)) + " ".join(parts)
    if record["value"] and record["value"] != title:
        line += ": " + quoted(record["value"])
    return line


def state_segment(node, has_click_action=False, borrowed_name=False,
                  facts=None):
    """Render notable states as compact marks such as `[checked expanded]`.

    Report only states whose presence is meaningful, and never infer meaning
    from an absent state. In particular, a
    missing ENABLED state is not rendered as `[disabled]`: Nautilus file
    icons set neither ENABLED nor SENSITIVE (only SHOWING, VISIBLE and
    FOCUSABLE) yet expose working `open` and `menu` actions. Toolkits set
    ENABLED/SENSITIVE inconsistently on Linux, and nothing reliably
    distinguishes a disabled control from a toolkit that does not report the
    state. A missing signal is better than a false one that makes the agent
    skip a usable target.
    """
    marks = []
    for name, label in NOTABLE_STATES:
        state = getattr(Atspi.StateType, name, None)
        if state is None:
            continue
        present = (
            facts.has_state(state)
            if facts is not None
            else state_contains(node, state)
        )
        if present:
            marks.append(label)
    # `has-click-action` is a capability, not an AT-SPI state, but it belongs
    # in the same group: the agent should see at a glance whether the element
    # can be clicked semantically.
    #
    # The mark is needed because node_actions() hides click-like actions
    # (otherwise every clickable node would list `click` as another action,
    # and the model would alternate between two tools). With them hidden, a
    # node with a semantic click and a node without an Action interface
    # would render identically, and the agent would fall back to coordinates
    # for both. Clickable nodes are the minority, so marking them is far
    # cheaper than marking the rest.
    #
    # The mark must use the same criteria as the entry point that the click
    # tool invokes (preferred_action_index), or it would mislead.
    if has_click_action:
        # The wording promises an invocable action, not an effect: in
        # Nautilus, GIMP and VLC such actions can report success and do
        # nothing. A name like `clickable` would read as a guarantee.
        marks.append("has-click-action")
    # Mark names borrowed from LABELLED_BY because they may not be unique:
    # LibreOffice's Position and Size dialog has two `Position Y` labels, one
    # per tab. When a selector then matches several elements, this mark
    # explains why.
    if borrowed_name:
        marks.append("labelled")
    if not marks:
        return ""
    return " [" + " ".join(marks) + "]"
