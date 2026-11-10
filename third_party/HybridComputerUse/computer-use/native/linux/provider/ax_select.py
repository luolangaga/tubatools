"""The AX select action: select text, a cell range or an item, then read it back.

Probed on LibreOffice 7.3 (gtk3) with AT-SPI 2:

- Text.set_selection/add_selection select inside one accessible object only.
  A Writer paragraph and an Impress bullet are separate objects, and
  add_selection on a second paragraph replaces the first, so no AT-SPI call
  selects across paragraphs. Writer does extend a selection across
  paragraphs from the keyboard: after set_caret_offset at the start of the
  first paragraph, each ctrl+shift+Down extends to the end of the next
  paragraph, and every covered paragraph then reports its selected span.
- Some providers shift requested text offsets. A changed span is reported
  as an unverified effect, never as successful selection of the request.
- A Calc sheet implements Selection: select_child for each cell index of a
  rectangle builds a real range (ctrl+c copied the 2x2 block, Delete cleared
  it), and get_selected_child reads it back.
- The Start Center thumbnail list implements Selection on its items.

Every route reads the result back from the provider before reporting
success; a route that cannot prove the selection fails instead of guessing.
"""

import time

from . import action_evidence as action_evidence_state
from .foundation import (
    Atspi,
    OBJECT_REPLACEMENT,
    OperationFailure,
    node_name,
    node_role,
    provider_bus_name,
    provider_identity_path,
    provider_object_path,
    safe,
    state_contains,
)
from .ax_actions import send_key
from .ax_node_properties import embedded_editor_nodes
from .ax_records import effective_role
from .input_primitives import require_window_focus

# Each ctrl+shift+Down extends a Writer selection by one paragraph; the
# provider waits up to this long for the next paragraph to report its span.
EXTEND_STEP_SECONDS = 1.0
# Upper bound of keyboard extensions for one select; at most this many
# paragraphs can be covered from rA to rB.
MAX_EXTEND_STEPS = 40
POLL_SECONDS = 0.05
# Cells selected one by one through Selection.select_child.
MAX_SELECTED_CELLS = 2000
SELECTION_SUMMARY_LIMIT = 80


def _failure(message, code="select_unavailable", emitted=False, phase="precondition"):
    return OperationFailure(
        message,
        failure_code=code,
        failure_phase=phase,
        input_events_emitted=emitted,
        failure_subject="element",
    )


def _text_iface(node):
    if node is None:
        return None
    return safe(lambda: node.get_text_iface())


def text_length(node):
    iface = _text_iface(node)
    if iface is None:
        return None
    return safe(lambda: int(Atspi.Text.get_character_count(iface)))


def text_selections(node):
    """Return the node's selected spans as [(start, end)], or None if unreadable."""
    iface = _text_iface(node)
    if iface is None:
        return None
    try:
        count = int(Atspi.Text.get_n_selections(iface))
        spans = []
        for index in range(count):
            span = Atspi.Text.get_selection(iface, index)
            spans.append((int(span.start_offset), int(span.end_offset)))
        return spans
    except Exception:
        return None


def text_between(node, start, end):
    iface = _text_iface(node)
    if iface is None:
        return ""
    return str(safe(lambda: Atspi.Text.get_text(iface, start, end), "") or "")


def _quote(text):
    text = str(text or "").replace("\n", " ")
    if len(text) > SELECTION_SUMMARY_LIMIT:
        text = text[:SELECTION_SUMMARY_LIMIT] + "..."
    return '"' + text.replace('"', '\\"') + '"'


def _same(first, second):
    return (
        first is second
        or (provider_object_path(first) != ""
            and provider_object_path(first) == provider_object_path(second))
    )


def _wait_until(predicate, timeout):
    deadline = time.monotonic() + timeout
    while True:
        if predicate():
            return True
        if time.monotonic() >= deadline:
            return False
        time.sleep(POLL_SECONDS)


def _selection_container(node):
    """Return the parent that manages the node's selection, or None."""
    parent = safe(lambda: node.get_parent())
    if parent is None or safe(lambda: parent.get_selection_iface()) is None:
        return None
    return parent


def _same_selection_item(first, second):
    if first is second:
        return first is not None
    if first is None or second is None:
        return False
    identity = provider_identity_path(first)
    return bool(identity and provider_bus_name(first) == provider_bus_name(second)
                and identity == provider_identity_path(second))


def _selection_item_owner(node):
    """Resolve selectable cell renderers to their owning Selection child.

    A table cell can contain separate icon and text cell renderers. Those
    renderers belong to one anonymous selectable cell; its index, rather
    than the text renderer's index, is owned by the table's Selection API.
    """
    current, chain = node, []
    for _ in range(8):
        parent = safe(current.get_parent)
        index = safe(current.get_index_in_parent)
        if parent is None or index is None or index < 0:
            break
        indexed = safe(lambda: parent.get_child_at_index(index))
        if not _same_selection_item(current, indexed):
            break
        chain.append((current, parent, index))
        if safe(parent.get_selection_iface) is not None:
            return parent, current, index, chain
        if (node_role(current) not in ("table cell", "cell")
                or node_role(parent) not in ("table cell", "cell")
                or node_name(parent)
                or not state_contains(current, Atspi.StateType.SELECTABLE)
                or not state_contains(parent, Atspi.StateType.SELECTABLE)):
            break
        current = parent
    return None


def _selection_chain_current(chain):
    return all(
        _same_selection_item(safe(child.get_parent), parent)
        and safe(child.get_index_in_parent) == index
        and _same_selection_item(
            safe(lambda: parent.get_child_at_index(index)), child)
        for child, parent, index in chain
    )


# ---------------------------------------------------------------------------
# Text inside one element


def select_text_range(node, start, end):
    """Select [start, end) in one text element; return the span read back."""
    iface = _text_iface(node)
    if iface is None:
        raise _failure(
            "select_unavailable: the element has no text interface; no input "
            "was emitted.")
    length = text_length(node)
    if length is None:
        raise _failure(
            "select_unavailable: the element's text length could not be "
            "read; no input was emitted.")
    if not 0 <= start <= end <= length:
        raise _failure(
            "select_invalid_range: {}..{} is outside the element's text "
            "(length {}); no input was emitted.".format(start, end, length),
            code="invalid_argument")
    existing = text_selections(node)
    if existing is None:
        raise _failure("select_unavailable: existing selections could not be read; "
                       "no input was emitted.")
    try:
        accepted = (
            Atspi.Text.set_selection(iface, 0, start, end)
            if existing else Atspi.Text.add_selection(iface, start, end)
        )
    except Exception:
        accepted = False
    if not accepted:
        raise _failure(
            "select_unavailable: the application refused the text selection; "
            "the selection may have changed.", code="effect_unknown",
            emitted=True, phase="emission")
    spans = [None]

    def exact_selection():
        spans[0] = text_selections(node)
        return spans[0] == [(start, end)]

    if not _wait_until(exact_selection, 0.6):
        raise _failure(
            "select_unverified: requested {}..{}, read back {}; the selection "
            "may have changed.".format(start, end, spans[0]),
            code="effect_unknown", emitted=True, phase="readback")
    return spans[0]


def _selection_text_owners(element, content):
    """Map displayed editor text to the native objects owning its offsets."""
    if not content or content.strip(OBJECT_REPLACEMENT):
        return content, [(element, 0, len(content))]
    nodes = embedded_editor_nodes(element)
    if not nodes or len(nodes) != len(content):
        raise _failure(
            "select_unavailable: the embedded editor text could not be read "
            "completely; no input was emitted.")
    lines, owners = [], []
    offset = 0
    for node in nodes:
        iface = _text_iface(node)
        line = safe(lambda: Atspi.Text.get_text(iface, 0, -1))
        if not isinstance(line, str) or OBJECT_REPLACEMENT in line:
            raise _failure(
                "select_unavailable: an embedded text object is unreadable "
                "or contains nested objects; no input was emitted.")
        lines.append(line)
        owners.append((node, offset, offset + len(line)))
        offset += len(line) + 1
    return "\n".join(lines), owners


# ---------------------------------------------------------------------------
# Text across sibling elements (Writer paragraphs)


def _text_run(first, last):
    """Return the sibling text elements from ``first`` to ``last``, or None."""
    parent = safe(lambda: first.get_parent())
    if parent is None or not _same(parent, safe(lambda: last.get_parent())):
        return None
    count = int(safe(lambda: parent.get_child_count(), 0) or 0)
    run = []
    inside = False
    for index in range(count):
        child = safe(lambda i=index: parent.get_child_at_index(i))
        if child is None:
            continue
        if _same(child, first):
            inside = True
        if inside:
            if _text_iface(child) is None:
                return None
            run.append(child)
        if _same(child, last):
            return run if inside else None
    return None


def _covered_to_end(node):
    length = text_length(node)
    if length == 0:
        # Writer reports no span for an empty paragraph; its SELECTED state
        # is the only evidence that the selection covers it.
        return state_contains(node, Atspi.StateType.SELECTED)
    spans = text_selections(node) or []
    return length is not None and any(end >= length for _start, end in spans)


def _run_verified(run):
    """Require complete requested spans and no selected text outside them."""
    parent = safe(run[0].get_parent)
    count = safe(lambda: int(parent.get_child_count()))
    if count is None:
        return False
    found = set()
    for index in range(count):
        child = safe(lambda i=index: parent.get_child_at_index(i))
        if child is None:
            return False
        if _text_iface(child) is None:
            continue
        spans = text_selections(child)
        if spans is None:
            return False
        member = next((i for i, node in enumerate(run) if _same(child, node)), None)
        if member is None:
            if spans or state_contains(child, Atspi.StateType.SELECTED):
                return False
            continue
        found.add(member)
        length = text_length(child)
        if length == 0:
            if not state_contains(child, Atspi.StateType.SELECTED):
                return False
        elif length is None or spans != [(0, length)]:
            return False
    return len(found) == len(run)


def select_text_run(window, first, last):
    """Select from the start of ``first`` through the end of ``last``.

    Returns ``(run, emitted)``. AT-SPI has no cross-object selection, so the
    provider puts the caret at the start of ``first`` and extends with
    ctrl+shift+Down until ``last`` reports its end, then verifies the exact
    range across all text siblings before reporting success.
    """
    run = _text_run(first, last)
    if run is None:
        raise _failure(
            "select_unavailable: to_ref must be a later text sibling of ref "
            "in the same container (for example two paragraphs of one "
            "document); no input was emitted.", code="invalid_argument")
    iface = _text_iface(first)
    require_window_focus(window, "select")
    if not safe(lambda: Atspi.Text.set_caret_offset(iface, 0), False):
        raise _failure(
            "select_unavailable: the caret could not be placed at the start "
            "of ref; the caret or selection may have changed.",
            code="effect_unknown", emitted=True, phase="emission")
    time.sleep(0.15)
    emitted = True
    # Each press covers at least one more paragraph, so press k must have
    # covered run[k]; waiting for exactly that element avoids a timeout per
    # press while still failing at once when Writer stops extending.
    for step in range(min(MAX_EXTEND_STEPS, len(run))):
        try:
            send_key("ctrl+shift+Down")
        except Exception as exc:
            raise _failure(
                "select_unverified: keyboard extension failed; the caret or "
                "selection may have changed.", code="effect_unknown",
                emitted=True, phase="emission") from exc
        if not _wait_until(lambda s=step: _covered_to_end(run[s]),
                           EXTEND_STEP_SECONDS):
            break
        if _covered_to_end(last):
            if _run_verified(run):
                return run, emitted
            break
    raise _failure(
        "select_unverified: the requested paragraph range was not read back "
        "exactly; the selection may be incomplete or extend outside the range.",
        code="effect_unknown", emitted=emitted, phase="readback")


def _run_summary(run):
    """Describe the selection read back from every sibling of the run."""
    parent = safe(run[0].get_parent)
    count = int(safe(lambda: parent.get_child_count(), 0) or 0) if parent is not None else 0
    covered = []
    total = 0
    outside = 0
    for index in range(count):
        child = safe(lambda i=index: parent.get_child_at_index(i))
        if child is None:
            continue
        spans = text_selections(child) or []
        if not spans:
            continue
        if not any(_same(child, node) for node in run):
            outside += 1
            continue
        for start, end in spans:
            total += end - start
            covered.append(text_between(child, start, end))
    summary = "selected {} chars in {} elements, from {} to {}".format(
        total, len(run), _quote(covered[0][:30] if covered else ""),
        _quote(covered[-1][-30:] if covered else ""))
    if outside:
        summary += "; the selection also covers {} element(s) after to_ref".format(
            outside)
    return summary


# ---------------------------------------------------------------------------
# Cells and items


def _table_cell_index(table, node):
    index = safe(lambda: node.get_index_in_parent())
    if index is None:
        return None, None, None
    row = safe(lambda: Atspi.Table.get_row_at_index(table, index))
    col = safe(lambda: Atspi.Table.get_column_at_index(table, index))
    return index, row, col


def select_cells(first, last):
    """Select the rectangle between two cells of one table; return names."""
    container = safe(lambda: first.get_parent())
    if container is None or not _same(container, safe(last.get_parent)):
        raise _failure(
            "select_invalid_range: both cells must belong to the same table; "
            "no input was emitted.", code="invalid_argument")
    table = safe(lambda: container.get_table_iface()) if container is not None else None
    selection = safe(lambda: container.get_selection_iface()) if container is not None else None
    if table is None or selection is None:
        raise _failure(
            "select_unavailable: the cells' table has no table selection "
            "interface; no input was emitted.")
    _, row0, col0 = _table_cell_index(table, first)
    _, row1, col1 = _table_cell_index(table, last)
    if None in (row0, col0, row1, col1) or min(row0, col0, row1, col1) < 0:
        raise _failure(
            "select_unavailable: the cell coordinates could not be read; no "
            "input was emitted.")
    top, bottom = sorted((int(row0), int(row1)))
    left, right = sorted((int(col0), int(col1)))
    if (bottom - top + 1) * (right - left + 1) > MAX_SELECTED_CELLS:
        raise _failure(
            "select_invalid_range: the range has more than {} cells; use the "
            "Name Box or keys for larger ranges.".format(MAX_SELECTED_CELLS),
            code="invalid_argument")
    indices = []
    for row in range(top, bottom + 1):
        for col in range(left, right + 1):
            index = safe(lambda r=row, c=col: Atspi.Table.get_index_at(table, r, c))
            if index is None or index < 0 or index in indices:
                raise _failure("select_unavailable: table cell indices could not "
                               "be resolved; no input was emitted.")
            indices.append(index)
    if not safe(lambda: Atspi.Selection.clear_selection(container), False):
        raise _failure(
            "select_unverified: clearing the previous selection failed; the "
            "selection may have changed.", code="effect_unknown", emitted=True,
            phase="emission")
    for index in indices:
        if not safe(lambda i=index: Atspi.Selection.select_child(container, i), False):
            raise _failure(
                "select_unverified: the table refused a cell; the previous "
                "selection was cleared and part of the range may be selected.",
                code="effect_unknown", emitted=True, phase="emission")
    expected = len(indices)
    if not _wait_until(lambda: _selected_cell_indices(container) == set(indices), 0.6):
        raise _failure(
            "select_unverified: the selected cell identities did not match the "
            "requested rectangle; the selection may have changed.",
            code="effect_unknown", emitted=True, phase="readback")
    first_name = node_name(safe(lambda: Atspi.Table.get_accessible_at(table, top, left)))
    last_name = node_name(safe(lambda: Atspi.Table.get_accessible_at(table, bottom, right)))
    return first_name, last_name, expected


def _selected_cell_indices(container):
    try:
        count = int(Atspi.Selection.get_n_selected_children(container))
        if not 0 <= count <= MAX_SELECTED_CELLS:
            return None
        indices = set()
        for index in range(count):
            child = Atspi.Selection.get_selected_child(container, index)
            if child is None or not _same(child.get_parent(), container):
                return None
            child_index = child.get_index_in_parent()
            if child_index < 0 or child_index in indices:
                return None
            indices.add(child_index)
        return indices
    except Exception:
        return None


def select_item(node):
    """Select one child of a Selection container; return True when read back."""
    owner = _selection_item_owner(node)
    if owner is None:
        raise _failure(
            "select_unavailable: the element's container does not support "
            "selection; no input was emitted.")
    container, selected_item, index, chain = owner
    if not _selection_chain_current(chain):
        raise _failure("select_unavailable: item ownership changed; "
                       "no input was emitted.")
    if not safe(lambda: Atspi.Selection.select_child(container, int(index)), False):
        raise _failure(
            "select_unavailable: the container refused to select the item; "
            "the selection may have changed.", code="effect_unknown",
            emitted=True, phase="emission")
    def selected():
        if not _selection_chain_current(chain):
            return False
        target_selected = state_contains(node, Atspi.StateType.SELECTED)
        owner_selected = bool(safe(lambda: Atspi.Selection.is_child_selected(
            container, int(index)), False))
        if selected_item is not node:
            return target_selected and owner_selected
        return target_selected or owner_selected

    if not _wait_until(selected, 0.6):
        raise _failure(
            "select_unverified: the item did not report itself selected.",
            code="effect_unknown", emitted=True, phase="readback")
    return True


# ---------------------------------------------------------------------------
# Entry point


def perform_select(action_input, window, element, element_record, to_element,
                   notes, action_evidence):
    """Run one select action and describe the read-back selection in notes."""
    # ax_dropdown builds on this module's helpers.
    from .ax_dropdown import (
        DROPDOWN_ROLES, option_name, owning_dropdown, select_dropdown_option)

    if element is None:
        raise RuntimeError("select requires a target ref")
    role = node_role(element)
    text = action_input.get("text")
    emitted = True
    owner = owning_dropdown(element) if text is None and to_element is None else None
    if to_element is not None:
        if role in ("table cell", "cell"):
            first, last, count = select_cells(element, to_element)
            summary = "selected {} cells {}:{}".format(count, first, last)
            action_evidence["route"] = "selection_api"
        else:
            run, emitted = select_text_run(window, element, to_element)
            summary = _run_summary(run)
            action_evidence["route"] = "keyboard_synthesis"
            action_evidence["window_focus_verified"] = True
            action_evidence["input_emitted"] = emitted
    elif text is not None and effective_role(element, role) in DROPDOWN_ROLES or (
            text is None and owner is not None):
        # select on an option of an open popup chooses it in its drop-down.
        action_evidence_state.replace(action_evidence)
        chosen = select_dropdown_option(
            element if text is not None else owner,
            text if text is not None else option_name(element), window)
        summary = "selected option {}".format(_quote(chosen))
        emitted = bool(action_evidence.get("input_emitted"))
        action_evidence["route"] = "semantic_action"
    elif text is not None or _text_iface(element) is not None and role not in (
            "list item", "table cell", "cell", "tree item", "icon"):
        content, owners = _selection_text_owners(
            element, text_between(element, 0, -1))
        if text is None:
            start, end = 0, len(content)
            if not content:
                raise _failure(
                    "select_invalid_range: the element has no text to select; "
                    "no input was emitted.", code="invalid_argument")
        else:
            start = content.find(text)
            if start < 0:
                raise _failure(
                    "select_text_not_found: {} is not in the element's text; "
                    "no input was emitted.".format(_quote(text)),
                    code="invalid_argument")
            if content.find(text, start + 1) >= 0:
                raise _failure(
                    "select_text_ambiguous: {} occurs more than once in the "
                    "element; no input was emitted.".format(_quote(text)),
                    code="invalid_argument")
            end = start + len(text)
        owner = next(((node, offset) for node, offset, limit in owners
                      if offset <= start and end <= limit), None)
        if owner is None:
            raise _failure(
                "select_unavailable: the requested text crosses embedded "
                "text objects; select text within one object or use GUI "
                "keyboard selection. No input was emitted.")
        node, offset = owner
        spans = select_text_range(node, start - offset, end - offset)
        got_start, got_end = spans[0]
        selected_text = text_between(node, got_start, got_end)
        if selected_text != content[start:end]:
            raise _failure(
                "select_unverified: the selected text changed during the "
                "operation; the selection may have changed.",
                code="effect_unknown", emitted=True, phase="readback")
        summary = "selected {}..{} {}".format(
            got_start + offset, got_end + offset,
            _quote(selected_text))
        action_evidence["route"] = "semantic_action"
    else:
        select_item(element)
        summary = "selected {} {}".format(role or "item", _quote(node_name(element)))
        action_evidence["route"] = "selection_api"
    action_evidence["input_emitted"] = emitted
    action_evidence["readback_sampled"] = True
    action_evidence["readback_verified"] = True
    notes.append("[select] " + summary)
