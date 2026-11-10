"""Canonical AX records and stable local indexing."""

# Portions of StableIndexer: Copyright (c) Microsoft Corporation, Apache-2.0.
# See licenses/Apache-2.0.txt and licenses/Playwright-NOTICE.txt.

from .foundation import (
    Atspi,  # pyright: ignore[reportAttributeAccessIssue]
    DEFAULT_TEXT_LIMIT,
    TOP_LEVEL_CONTAINER_ROLES,
    has_editable_text_iface,
    has_text_iface,
    limit_text,
    node_name,
    node_pid,
    node_role,
    process_generation,
    provider_bus_name,
    provider_identity_path,
    provider_object_path,
    safe,
    state_contains,
)
from .node_facts import NodeFacts
from .primitives import name_fingerprint
from .ax_node_properties import (
    accessible_id,
    element_value,
    node_actions,
    node_description,
    placeholder_text,
    state_segment,
    text_attributes,
)


class StableIndexer:
    """Assign element indices that survive across snapshots.

    An index is not a
    traversal ordinal but a cached id per element, reissued only when the
    element's role or name changes. An element whose role, name and full-name
    hash are unchanged keeps the same index in every snapshot.

    The provider keeps no per-element state across snapshots (its process can
    be restarted), so the Go side, which keeps the previous snapshot, passes
    in its `path -> (index, role, name)` mapping instead.

    Without stable indices, a tree reordered by an opening dialog makes an old
    index act on a different control without any error. With them, controls
    of the main window get their previous indices back once the dialog
    closes, so an old reference is either still correct or reported missing.
    """

    def __init__(self, known):
        # known: {"0.1.2": {"index": 7, "role": "push button", "name": "Save"}}
        self.known = known or {}
        self.next_free = 0
        for entry in self.known.values():
            try:
                self.next_free = max(self.next_free, int(entry.get("index", 0)) + 1)
            except (TypeError, ValueError):
                pass
        self.used = set()

    def index_for(self, path, role, name, name_hash=""):
        key = ".".join(str(p) for p in path)
        entry = self.known.get(key)
        if (
            entry is not None
            and entry.get("role") == role
            and entry.get("name") == name
            and str(entry.get("nameHash") or "") == str(name_hash or "")
        ):
            index = entry.get("index")
            if isinstance(index, int) and index not in self.used:
                self.used.add(index)
                return index
        while self.next_free in self.used:
            self.next_free += 1
        index = self.next_free
        self.used.add(index)
        self.next_free += 1
        return index


def labelled_by_name(node):
    """Borrow a name from the AT-SPI LABELLED_BY relation for an unnamed node.

    Toolkits may expose a control's label through a relation rather than its
    own name. The accessible-name computation includes labelled-by, so the
    returned name comes from provider evidence. The rendered line carries a
    `labelled` mark because multiple controls may share the same label.

    Reading relations requires a D-Bus call, so this fallback queries them
    only for nodes without their own name. The pruning predicate does not
    consult relations; a node whose only useful property is a borrowed name
    can therefore be omitted from the working projection.
    """
    relations = safe(lambda: node.get_relation_set(), None)
    if not relations:
        return ""
    names = []
    for relation in relations:
        kind = safe(lambda: relation.get_relation_type())
        if kind != Atspi.RelationType.LABELLED_BY:
            continue
        count = safe(lambda: relation.get_n_targets(), 0) or 0
        for index in range(count):
            target = safe(lambda: relation.get_target(index))
            if target is None:
                continue
            text = str(node_name(target) or "").strip()
            if text:
                names.append(text)
    # Labels often end with a colon (`Position Y:`). Strip it: it carries no
    # information and is awkward to write in selectors.
    return " ".join(names).rstrip(":").strip()


def effective_name(node):
    """Return the node's own name, or one borrowed from LABELLED_BY.

    Every name comparison must use this single definition. If a record holds
    a borrowed name such as `Position Y` while matching reads node_name(),
    which returns an empty string for the same node, the match fails silently
    and identity falls back to the weakest criterion, role plus screen
    position, so the reference follows whatever element moves into place.
    """
    own = node_name(node)
    if str(own or "").strip():
        return own
    role = node_role(node)
    combo = gtk_combo_partner(node, role)
    if combo is not None:
        return combo_label(combo, node)[0]
    return labelled_by_name(node) or content_name(node, role)


# Item roles whose accessible name comes from their content (ARIA
# name-from-content). Containers and controls with their own label are left
# alone: naming a panel after its first label would mislabel whole regions.
NAME_FROM_CONTENT_ROLES = {"list item", "tree item", "table row"}
NAME_FROM_CONTENT_LABEL_ROLES = {"label", "static"}
NAME_FROM_CONTENT_MAX_NODES = 24
NAME_FROM_CONTENT_MAX_DEPTH = 4


def content_name(node, role=None):
    """Name an unnamed item row from its visible labels, or return "".

    GTK3 GtkListBoxRow (GNOME Settings sidebar, D-Feet bus list) and similar
    rows expose no accessible name; the visible label is a descendant. Such
    rows rendered as ``listitem [addressable=false]`` next to their text, so
    the row a user clicks had no ref. The walk is bounded, reads only label
    descendants with the VISIBLE state, and joins them in tree order; hidden
    subtitles ("Control how you connect to the Internet") are skipped.
    """
    if role is None:
        role = node_role(node)
    if str(role or "") not in NAME_FROM_CONTENT_ROLES:
        return ""
    parts = []
    budget = [NAME_FROM_CONTENT_MAX_NODES]

    def visit(current, depth):
        if depth > NAME_FROM_CONTENT_MAX_DEPTH or budget[0] <= 0:
            return
        count = int(safe(lambda: current.get_child_count(), 0) or 0)
        for index in range(count):
            if budget[0] <= 0:
                return
            budget[0] -= 1
            child = safe(lambda i=index: current.get_child_at_index(i))
            if child is None:
                continue
            child_role = node_role(child)
            if child_role in NAME_FROM_CONTENT_ROLES:
                continue
            if child_role in NAME_FROM_CONTENT_LABEL_ROLES:
                if not state_contains(child, Atspi.StateType.VISIBLE):
                    continue
                text = str(node_name(child) or "").strip()
                if text:
                    parts.append(text)
                continue
            visit(child, depth + 1)

    visit(node, 1)
    return " ".join(parts)


def gtk_combo_partner(node, role):
    """Return the hidden combo box paired with a visible popup opener.

    LibreOffice's gtk3 backend (7.3) draws a drop-down as three siblings: an
    editable ``text``, a ``combo box`` and a ``toggle button``. Only the
    toggle button is drawn. The combo box and the text report neither SHOWING
    nor a frame (origin INT_MIN), so tree pruning drops them, and the combo's
    own ``press`` action does not open the list. In the Writer Paragraph
    dialog, a find for role "combo box" therefore returned nothing and the
    line-spacing control rendered as an unnamed ``button``.

    The toggle button is what a user clicks, so record_for() projects it as
    the combo box; clicking it opens the list. Toolbar combos (font name,
    size) keep their visible text sibling, which carries the current value.
    Some GTK backends expose that opener as a named, childless combo box
    with a click action. Its hidden sibling still owns the label and the
    visible text sibling still owns the committed value.
    """
    native_combo = role == "combo box"
    if native_combo:
        if (not state_contains(node, Atspi.StateType.SHOWING)
                or int(safe(node.get_child_count, 0) or 0) != 0
                or not any(str(safe(lambda i=i: node.get_action_name(i), "")
                                   or "").lower() == "click"
                           for i in range(int(safe(node.get_n_actions, 0) or 0)))):
            return None
    elif role != "toggle button" or str(node_name(node) or "").strip():
        return None
    parent = safe(lambda: node.get_parent())
    if parent is None:
        return None
    count = int(safe(lambda: parent.get_child_count(), 0) or 0)
    if count < 2 or count > 3:
        return None
    combo = None
    for index in range(count):
        sibling = safe(lambda i=index: parent.get_child_at_index(i))
        if sibling is None or provider_object_path(sibling) == provider_object_path(node):
            continue
        sibling_role = node_role(sibling)
        if sibling_role == "combo box" and combo is None:
            combo = sibling
        elif sibling_role != "text":
            return None
    if native_combo and combo is not None and state_contains(
            combo, Atspi.StateType.SHOWING):
        return None
    return combo


def combo_edit_target(toggle):
    """Find the unique displayed editor in a projected combo control.

    List-only combos can retain an unmapped text sibling that still reports
    EDITABLE and EditableText. It is a placeholder, not the displayed control.
    """
    if gtk_combo_partner(toggle, node_role(toggle)) is None:
        return None
    parent = safe(lambda: toggle.get_parent())
    count = int(safe(lambda: parent.get_child_count(), 0) or 0)
    candidates = []
    for index in range(count):
        child = safe(lambda i=index: parent.get_child_at_index(i))
        if child is None or node_role(child) != "text":
            continue
        if (state_contains(child, Atspi.StateType.EDITABLE)
                and state_contains(child, Atspi.StateType.SHOWING)
                and not state_contains(child, Atspi.StateType.DEFUNCT)
                and has_editable_text_iface(child) and has_text_iface(child)
                and provider_bus_name(child) == provider_bus_name(toggle)
                and node_pid(child) == node_pid(toggle)
                and provider_object_path(safe(lambda: child.get_parent()))
                == provider_object_path(parent)):
            candidates.append(child)
    return candidates[0] if len(candidates) == 1 else None


def combo_edit_identity(node):
    if node is None:
        return None
    identity = {
        "providerBusName": provider_bus_name(node),
        "providerObjectPath": provider_object_path(node),
        "processGeneration": process_generation(node_pid(node)),
    }
    return identity if all(identity.values()) else None


def combo_label(combo, toggle):
    """Name a projected combo box: its own name, its label, or its group.

    Returns ``(name, source)``. LibreOffice names few combos itself, but most
    dialog combos carry a LABELLED_BY relation ("Alignment:"). The
    line-spacing combo has neither; its nearest named ancestor is the
    "Line Spacing" panel, so the combo borrows that name and records the
    source as ``ancestor``. The walk stops at the first container that is not
    a layout panel, so a toolbar or window title is never borrowed.
    """
    own = str(node_name(combo) or "").strip()
    if own:
        return own, "provider"
    borrowed = labelled_by_name(combo)
    if borrowed:
        return borrowed, "labelled_by"
    ancestor = safe(lambda: toggle.get_parent())
    for _ in range(6):
        if ancestor is None or node_role(ancestor) not in ("panel", "filler"):
            break
        name = str(node_name(ancestor) or "").strip()
        if name:
            return name, "ancestor"
        ancestor = safe(lambda a=ancestor: a.get_parent())
    return "", ""


# Roles of the popup that a combo box owns as a child: a list (Qt, Gecko), a
# menu (GTK, Chromium) or a window wrapping either.
COMBO_POPUP_ROLES = {"list", "list box", "menu", "window", "tree", "tree table"}
# A combo box has its popup, an optional entry and an optional button as
# children; reading more would only cost round trips on a misreporting node.
COMBO_CHILD_SCAN_LIMIT = 8


def closed_combo_popups(node, facts, scope=None):
    """Return ``[(index, facts)]`` for the popups of a closed combo box.

    The options of a closed combo box are not on screen and must not be
    rendered or addressed. The popup is closed when the combo box is not
    SHOWING (a combo on a hidden page), is EXPANDABLE but not EXPANDED, or
    when the popup itself is not SHOWING or has no on-screen extents. The
    last test is the only one that works for Qt, which reports neither
    EXPANDABLE nor EXPANDED on a combo box and keeps SHOWING on the popup
    list and its items while the popup is closed; only the list's extents
    (0x0 at the origin) tell. Other children, such as the entry of an
    editable combo box, are never returned.
    """
    if str(facts.value("role", "") or "") != "combo box":
        return []
    owner_closed = (
        not facts.has_state(Atspi.StateType.SHOWING)
        or (
            facts.has_state(Atspi.StateType.EXPANDABLE)
            and not facts.has_state(Atspi.StateType.EXPANDED)
        )
    )
    closed = []
    for index in range(min(facts.child_count(), COMBO_CHILD_SCAN_LIMIT)):
        child = safe(lambda i=index: node.get_child_at_index(i))
        if child is None:
            continue
        child_facts = NodeFacts(child, scope)
        if str(child_facts.value("role", "") or "") not in COMBO_POPUP_ROLES:
            continue
        if (
            owner_closed
            or not child_facts.has_state(Atspi.StateType.SHOWING)
            or child_facts.screen_frame() is None
        ):
            closed.append((index, child_facts))
    return closed


def effective_role(node, role=None):
    """Return the role every record and identity check uses for ``node``.

    It equals the provider role except for the toggle button of a LibreOffice
    gtk3 combo, which is projected as ``combo box`` (gtk_combo_partner()).
    Rendering and live resolution must agree, or a ref to the combo would be
    rejected as a role change before every click.
    """
    if role is None:
        role = node_role(node)
    role = str(role or "")
    if gtk_combo_partner(node, role) is not None:
        return "combo box"
    return top_level_window_role(node, editable_text_role(node, role))


def top_level_window_role(node, role):
    """Project a container that is a direct child of the application to ``window``.

    Every direct child of an AT-SPI application object is a top-level window;
    the role is the toolkit's choice. The server issues a window ref (wN) only
    from a root record with a window role, so with "filler" the window had no
    wN and every element in it reported owner_identity_incomplete.
    """
    if role not in TOP_LEVEL_CONTAINER_ROLES:
        return role
    parent = safe(lambda: node.get_parent())
    if parent is None or node_role(parent) != "application":
        return role
    return "window"


def editable_text_role(node, role):
    """Project an editable AT-SPI ``text`` node to ``entry`` or ``text area``.

    AT-SPI uses role ``text`` for static text and for input fields alike.
    GTK2/GTK3 (GtkEntry, GtkTextView), Qt (QLineEdit, QPlainTextEdit) and
    LibreOffice dialog fields all report ``text``; labels use ``label``. The
    field differs only by the EDITABLE state and the Text interface.
    Without the projection the gedit document, the LibreOffice Find field
    and GTK file name entries were rendered as static text: no ref, no value,
    and a find for role ``textbox`` returned nothing.

    MULTI_LINE marks a text area; anything else is a single-line entry. The
    editor of a projected LibreOffice combo keeps its role: the combo box
    already exposes it as its edit target.
    """
    if role != "text":
        return role
    states = safe(node.get_state_set)
    if states is None or not states.contains(Atspi.StateType.EDITABLE):
        return role
    # KTextEditor (Kate) exposes Text without EditableText; keyboard input
    # still edits it, so the Text interface is enough.
    if not (has_editable_text_iface(node) or has_text_iface(node)):
        return role
    parent = safe(lambda: node.get_parent())
    if parent is not None:
        count = int(safe(lambda: parent.get_child_count(), 0) or 0)
        if 2 <= count <= 3:
            for index in range(count):
                sibling = safe(lambda i=index: parent.get_child_at_index(i))
                if sibling is not None and node_role(sibling) == "combo box":
                    return role
    if states.contains(Atspi.StateType.MULTI_LINE):
        return "text area"
    return "entry"


def semantic_relations(node, text_limit=DEFAULT_TEXT_LIMIT):
    """Return bounded relation facts for retained nodes only.

    Relations are evidence, not a replacement address. Targets stay descriptive
    because AT-SPI relation objects do not expose the provider traversal path
    used by StableIndexer. Go therefore uses them only for conservative
    duplicate-label pruning, never for action resolution.
    """
    wanted = {}
    for enum_name, output_name in (
        ("LABELLED_BY", "labelled-by"),
        ("LABEL_FOR", "label-for"),
        ("CONTROLLER_FOR", "controller-for"),
        ("MEMBER_OF", "member-of"),
    ):
        value = getattr(Atspi.RelationType, enum_name, None)
        if value is not None:
            wanted[value] = output_name
    try:
        relations = node.get_relation_set()
    except Exception:
        return {}, False
    if relations is None:
        return {}, False
    if not relations:
        return {}, True
    out = {}
    complete = True
    for relation in relations:
        kind = safe(lambda: relation.get_relation_type())
        label = wanted.get(kind)
        if not label:
            continue
        count = min(int(safe(lambda: relation.get_n_targets(), 0) or 0), 16)
        targets = []
        for index in range(count):
            try:
                target = relation.get_target(index)
            except Exception:
                complete = False
                continue
            if target is None:
                complete = False
                continue
            targets.append({
                "role": str(safe(lambda: node_role(target), "") or ""),
                "name": limit_text(
                    str(
                        safe(lambda: node_name(target), "")
                        or safe(
                            lambda: element_value(
                                target,
                                text_limit=min(text_limit, 160),
                            ),
                            "",
                        )
                        or ""
                    ),
                    text_limit=min(text_limit, 160),
                ),
                "providerBusName": provider_bus_name(target),
                "providerObjectPath": provider_object_path(target),
                "processGeneration": process_generation(node_pid(target)),
            })
        if targets:
            out[label] = targets
    return out, complete


def record_for(node, index, path, window_bounds, text_limit=DEFAULT_TEXT_LIMIT,
               facts=None, owner_cache=None):
    """Build the canonical record of one node.

    ``facts`` carries the reads that the traversal already made for this
    node (see NodeFacts); every field read here goes through it so that no
    field is read from the provider twice in one snapshot.
    """
    if facts is None:
        facts = NodeFacts(node)
    bounds = facts.relative_frame(window_bounds)
    role = str(facts.value("role", "") or "")
    # Read the action table once for both fields; see node_actions() about the
    # LibreOffice ATK bridge.
    actions, has_click_action = node_actions(node)
    own_name = str(facts.value("name", "") or "")
    relations, relations_complete = semantic_relations(
        node, text_limit=text_limit
    )
    labelled_names = [
        str(target.get("name") or "").strip()
        for target in relations.get("labelled-by", [])
        if str(target.get("name") or "").strip()
    ]
    combo = gtk_combo_partner(node, role)
    role = top_level_window_role(node, editable_text_role(node, role))
    if combo is not None:
        # See gtk_combo_partner(): the drawn toggle button stands for the
        # hidden combo box, so it takes the combo's role and label.
        role = "combo box"
        full_name, name_source = combo_label(combo, node)
        borrowed = bool(full_name)
    elif str(own_name or "").strip():
        full_name = own_name
        name_source = "provider"
        borrowed = False
    else:
        full_name = " ".join(labelled_names).rstrip(":").strip()
        name_source = "labelled_by" if full_name else ""
        borrowed = bool(full_name)
        if not full_name:
            full_name = content_name(node, role)
            name_source = "content" if full_name else ""
    name = limit_text(full_name, text_limit=text_limit)
    record = {
        "index": index,
        "runtimeId": path[:],
        "automationId": accessible_id(node),
        "providerBusName": provider_bus_name(node),
        "providerObjectPath": provider_identity_path(
            node, transient=facts.has_state(Atspi.StateType.TRANSIENT),
            owner_cache=(owner_cache if owner_cache is not None
                         else facts.scope_cache("transient_owner"))),
        "processGeneration": process_generation(facts.pid()),
        "showing": facts.showing(),
        "visible": facts.state_flag(Atspi.StateType.VISIBLE),
        "name": name,
        "controlType": role,
        "localizedControlType": role,
        "className": str(safe(node.get_toolkit_name, "") or ""),
        "value": element_value(node, text_limit=text_limit, role=role),
        "nativeWindowHandle": 0,
        "frame": bounds,
        "actions": actions,
        "states": state_segment(
            node, has_click_action=has_click_action, borrowed_name=borrowed,
            facts=facts,
        ),
        "description": node_description(
            node, text_limit=text_limit, facts=facts
        ),
        "placeholder": placeholder_text(
            node, text_limit=text_limit, role=role
        ),
        # Text attributes travel with the record instead of a separate walk:
        # rendering already visits every node and Go keeps the pre-action
        # snapshot, so comparing the two costs no extra AT-SPI calls. Reading
        # them from the focused text node does not work because the
        # LibreOffice root pane also reports FOCUSED, and a separate full-tree
        # scan is slow and could miss the target paragraph.
        "textAttributes": text_attributes(node),
        "relations": relations,
        "relationReadComplete": relations_complete,
        "nameSource": name_source,
    }
    if combo is not None:
        editor = combo_edit_target(node)
        identity = combo_edit_identity(editor)
        record["setValue"] = identity is not None
        if identity is not None:
            record["editTarget"] = identity
            record["value"] = element_value(editor, text_limit=text_limit)
    # When the name is truncated, also store a hash of the full name.
    #
    # Identity checks compare the live full name, which never equals the
    # truncated recorded name, so without the hash a lower text_limit would
    # silently weaken identity to role plus screen position. Comparing only
    # the truncated prefix would be worse: two elements sharing a prefix
    # would match. The hash restores exact full-name equality at the cost of
    # 16 characters on truncated records.
    if str(full_name or "") != name:
        record["nameHash"] = name_fingerprint(full_name)
    return record
