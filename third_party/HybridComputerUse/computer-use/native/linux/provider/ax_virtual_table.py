"""Bounded virtual-table discovery and rendering."""

from .foundation import (
    Atspi,
    DEFAULT_TEXT_LIMIT,
    MAX_CHILD_FANOUT,
    MAX_ELEMENTS,
    node_pid,
    node_role,
    process_generation,
    provider_bus_name,
    provider_object_path,
    safe,
)
from .node_geometry import raw_extents
from .ax_node_properties import text_value_with_status
from .ax_records import record_for

# Maximum cells rendered per self-managed table container. A typical viewport
# is about 37 rows x 21 columns = 777 cells, fetched with text in about 0.12 s
# (under 0.2 ms per cell). The cap bounds very wide tables.
MAX_TABLE_CELLS = 1200


def visible_cell_range(node):
    """Return the visible ``(row0, row1, col0, col1)`` of a self-managed table.

    MANAGES_DESCENDANTS containers cannot be enumerated (a Calc sheet reports
    about 1.07 billion children) but can be addressed by coordinates. This
    hit-tests two opposite corners of the container rectangle with
    Component.get_accessible_at_point and maps the hits back to row and
    column ranges; in Calc that is an O(1) pixel-to-cell conversion
    (ScAccessibleSpreadsheet::getAccessibleAtPoint uses GetPosFromPixel).

    Returns None when probing fails, in which case the caller must not
    enumerate the cells.
    """
    table = safe(node.get_table_iface)
    component = safe(node.get_component_iface)
    if table is None or component is None:
        return None
    rect = safe(
        lambda: Atspi.Component.get_extents(component, Atspi.CoordType.SCREEN)
    )
    if rect is None or rect.width <= 0 or rect.height <= 0:
        return None

    corners = [
        (rect.x + 3, rect.y + 3),
        (rect.x + rect.width - 4, rect.y + rect.height - 4),
    ]
    found = []
    for x, y in corners:
        cell = safe(
            lambda x=x, y=y: Atspi.Component.get_accessible_at_point(
                component, x, y, Atspi.CoordType.SCREEN
            )
        )
        if cell is None or cell == node:
            # A hit on the container itself (Chromium tables answer hit
            # tests that way) names no cell.
            return None
        index = safe(cell.get_index_in_parent)
        if index is None:
            return None
        row = safe(lambda: Atspi.Table.get_row_at_index(table, index))
        col = safe(lambda: Atspi.Table.get_column_at_index(table, index))
        if row is None or col is None:
            return None
        found.append((int(row), int(col)))

    if len(found) != 2:
        return None
    rows = sorted(f[0] for f in found)
    cols = sorted(f[1] for f in found)
    return rows[0], rows[1], cols[0], cols[1]


# Child slots read to find the first child in view of a long container.
VISIBLE_SPAN_PROBES = 64


def visible_child_span(node, count, window_bounds):
    """Return ``(first, last)`` indices of the children in view, or None.

    For a container that reports more children than the walk enumerates and
    does not manage its descendants (a web table with thousands of rows).
    Its children are laid out top to bottom, so a binary search on their
    screen frames finds the first one in view; the span then runs while the
    children stay in view, at most MAX_CHILD_FANOUT of them. Returns None
    when the window bounds or a child frame are unknown, or when the order
    does not hold.
    """
    if not window_bounds or count <= 0:
        return None
    top = window_bounds["y"]
    bottom = top + window_bounds["height"]

    def box(index):
        child = safe(lambda: node.get_child_at_index(index))
        return raw_extents(child) if child is not None else None

    low, high, probes = 0, count - 1, 0
    while low < high:
        probes += 1
        if probes > VISIBLE_SPAN_PROBES:
            return None
        middle = (low + high) // 2
        current = box(middle)
        if current is None:
            return None
        if current["y"] + current["height"] <= top:
            low = middle + 1
        else:
            high = middle
    first_box = box(low)
    if (first_box is None or first_box["y"] >= bottom
            or first_box["y"] + first_box["height"] <= top):
        return None
    last = low
    while last + 1 < count and last - low + 1 < MAX_CHILD_FANOUT:
        current = box(last + 1)
        if current is None or current["y"] >= bottom:
            break
        last += 1
    return low, last


def render_visible_cells(
    node, depth, path, window_bounds, records, lines,
    text_limit=DEFAULT_TEXT_LIMIT, max_tree_nodes=MAX_ELEMENTS, indexer=None,
    boxes=False, annotations=None, content_probe=None,
):
    """Render the cells in a self-managed table's viewport; return the count.

    This replaces child enumeration once should_enumerate_children() refuses
    it: enumeration would walk the roughly 1.07 billion children a Calc sheet
    reports, whereas (row, col) addressing is an O(1) lookup in Calc.

    ``content_probe``, when given, receives the viewport span and every cell
    text read here, so that tree reuse can later detect cell changes that
    LibreOffice reports without events (see tree_reuse.ContentProbe).
    """
    reads = []
    def record_omission(reason, count=1):
        if annotations is None:
            return
        annotations.append({
            "parentRuntimeId": path[:],
            "kind": "provider_omission",
            "count": max(1, int(count)),
            "reason": reason,
        })

    span = visible_cell_range(node)
    if content_probe is not None:
        content_probe.add_table(node, span, reads)
    if span is None:
        record_omission("visible_cell_range_unavailable")
        return 0
    row0, row1, col0, col1 = span
    table = safe(node.get_table_iface)
    if table is None:
        record_omission("table_interface_unavailable")
        return 0

    total = (row1 - row0 + 1) * (col1 - col0 + 1)
    probe_budget = min(MAX_TABLE_CELLS, total)
    # The cells' records are rebound to this table below, so the transient
    # re-creation probe (provider_object_path()) runs once, not per cell.
    owner_cache = {provider_object_path(node): False}
    record_budget = max(0, max_tree_nodes - len(records))
    count = 0
    empty = 0
    probed = 0
    missing = 0
    text_unavailable = 0
    for row in range(row0, row1 + 1):
        for col in range(col0, col1 + 1):
            if probed >= probe_budget or count >= record_budget:
                break
            probed += 1
            cell = safe(lambda r=row, c=col: Atspi.Table.get_accessible_at(table, r, c))
            if cell is None:
                reads.append(None)
                missing += 1
                continue
            # Read text only, without falling back to numeric_value: Calc's
            # Value interface returns 0.0 for empty cells, which would make
            # blank cells look like they contain 0 and defeat any search for
            # empty cells.
            value, text_complete = text_value_with_status(
                cell, text_limit=text_limit
            )
            reads.append((value, text_complete))
            if not text_complete:
                text_unavailable += 1
                continue
            if not value:
                # Empty cells are left out of the tree: they are most of a
                # viewport, and an empty cell carries nothing but its RxCy
                # coordinate, which the range summary below already implies.
                #
                # Omitted cells get no ref, so the summary names the verified
                # alternative: move the cell cursor with keypress, then type
                # without a ref, which lands in the cell holding the cursor.
                #
                # The Name Box (`text Value: A1` in the tree) looks more
                # direct but failed verification: set_value changes its text
                # without navigating (the control changes but the application
                # does not act, as with drop-down commits), and click does not
                # give it keyboard focus. Unverified paths are not suggested.
                empty += 1
                continue
            # Use (row, col) as the path suffix: unlike traversal order, it
            # stays the same for a given cell after scrolling.
            cell_path = path + [row, col]
            index = (indexer.index_for(cell_path, node_role(cell), "")
                     if indexer is not None else len(records))
            record = record_for(
                cell, index, cell_path, window_bounds, text_limit=text_limit,
                owner_cache=owner_cache,
            )
            # The cell wrapper may be recreated between AX reads. Bind its
            # identity to the owning table plus row/column, then resolve the
            # current cell through the table interface before each action.
            record["providerIdentityScope"] = "table_owner"
            record["providerBusName"] = provider_bus_name(node)
            record["providerObjectPath"] = provider_object_path(node)
            record["processGeneration"] = process_generation(node_pid(node))
            records.append(record)
            # The record keeps the cell frame. These cells are genuinely
            # visible (they are the viewport content found by coordinate
            # addressing); without a frame, visible-only pruning would treat
            # the whole drop-down or table as invisible and discard it.
            cell_frame = record.get("frame")
            frame_segment = ""
            if boxes and cell_frame is not None:
                frame_segment = " Frame: {{x: {0}, y: {1}, width: {2}, height: {3}}}".format(
                    round(cell_frame["x"]),
                    round(cell_frame["y"]),
                    round(cell_frame["width"]),
                    round(cell_frame["height"]),
                )
            # Use the node's real AT-SPI role rather than a hard-coded "cell":
            # role-based pruning depends on it and would otherwise discard
            # drop-down options as irrelevant.
            cell_role = record["localizedControlType"] or record["controlType"] or "table cell"
            a1 = spreadsheet_a1(row, col)
            lines.append(
                ("\t" * (depth + 2))
                + "{} {} {} (R{}C{}) {}{}".format(
                    index, cell_role, a1, row, col, value, frame_segment
                ).rstrip()
            )
            count += 1
        if probed >= probe_budget or count >= record_budget:
            break

    if missing:
        record_omission("visible_cell_lookup_unavailable", missing)
    if text_unavailable:
        record_omission(
            "visible_cell_text_unavailable", text_unavailable
        )
    if probed < total:
        record_omission("visible_cell_budget", total - probed)

    if count or empty:
        rows = safe(lambda: Atspi.Table.get_n_rows(table), "?")
        cols = safe(lambda: Atspi.Table.get_n_columns(table), "?")
        lines.append(
            ("\t" * (depth + 2))
            + "(cells {}:{} are in view; zero-based rows {}-{} x cols {}-{}: "
              "{} non-empty cell(s) listed, {} empty cell(s) omitted; table "
              "is {}x{})".format(
                  spreadsheet_a1(row0, col0), spreadsheet_a1(row1, col1),
                  row0, row1, col0, col1, count, empty, rows, cols,
              )
        )
    if empty and annotations is not None:
        # The core renders this annotation on the table node; provider lines
        # never reach the caller. Empty cells have no record and no ref, so
        # the note names the route that reaches them: the cell cursor.
        annotations.append({
            "parentRuntimeId": path[:],
            "kind": "cell_summary",
            "count": int(empty),
            "reason": "empty_cells",
            "detail": (
                "{} empty cells in view {}:{} omitted ({} listed; table is "
                "{}x{}); they have no ref: move the cell cursor there with "
                "keypress, then type without a ref".format(
                    empty, spreadsheet_a1(row0, col0),
                    spreadsheet_a1(row1, col1), count, rows, cols,
                )
            ),
        })
    return count


def spreadsheet_a1(row, col):
    row = int(row)
    col = int(col)
    if row < 0 or col < 0:
        raise ValueError("spreadsheet coordinates must be non-negative")
    letters = ""
    number = col + 1
    while number:
        number, remainder = divmod(number - 1, 26)
        letters = chr(ord("A") + remainder) + letters
    return "{}{}".format(letters, row + 1)
