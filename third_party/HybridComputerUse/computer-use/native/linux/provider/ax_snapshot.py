"""Linux provider module: ax snapshot."""

import time
from .foundation import (
    AX_WINDOW_ROLES,
    Atspi,
    BUDGET_PRESSURE_RATIO,
    DEFAULT_TEXT_LIMIT,
    MAX_CHILD_FANOUT,
    FIND_WALK_DEADLINE_SECONDS,
    FIND_WALK_MAX_CHILD_FANOUT,
    FIND_WALK_MAX_RECORDS,
    FIND_WALK_MAX_RENDERED_CHARS,
    MAX_DEPTH,
    MAX_ELEMENTS,
    MAX_RENDERED_CHARS,
    TREE_DEADLINE_SECONDS,
    is_interactive_role,
    node_role,
    process_generation,
    provider_bus_name,
    provider_object_path,
    safe,
    state_contains,
)
from .node_visibility import MENU_ROLES, showing_state
from .node_facts import (
    NodeFacts,
    strict_prune_probe,
)

from .ax_node_properties import (
    render_element_line,
)
from .ax_records import (
    record_for,
)
from .ax_virtual_table import (
    render_visible_cells,
)
from .find_walk import TEXT_ONLY_ROLES, candidate_record, repeats_parent_text
from .snapshot_children import (
    ChildWalk,
    child_entries,
    closed_popup_indices,
    record_provider_errors,
    strict_child_count,
)


FIND_WALK_LIMITS = {
    "max_tree_nodes": FIND_WALK_MAX_RECORDS,
    "max_rendered_chars": FIND_WALK_MAX_RENDERED_CHARS,
    "max_child_fanout": FIND_WALK_MAX_CHILD_FANOUT,
    "deadline_seconds": FIND_WALK_DEADLINE_SECONDS,
}


def render_tree(root, window_bounds, root_path, text_limit=DEFAULT_TEXT_LIMIT,
                boxes=True, indexer=None,
                max_tree_nodes=MAX_ELEMENTS, max_tree_depth=MAX_DEPTH, prune=True,
                max_rendered_chars=MAX_RENDERED_CHARS,
                deadline_seconds=TREE_DEADLINE_SECONDS,
                max_child_fanout=MAX_CHILD_FANOUT,
                focus_probe=None, content_probe=None, find=None):
    """Render one window subtree; return ``(records, lines, annotations)``.

    ``focus_probe``, when given, is a FocusProbe that records the first
    FOCUSED node in the order find_first() would visit, from state sets this
    walk reads anyway; see FocusProbe.

    ``content_probe``, when given, is a tree_reuse.ContentProbe that records
    the frame and state set of every kept node and the cells read from
    self-managed tables, from values this walk reads anyway.

    ``find``, when given, is a find_walk.FindWalk. Pruning, budgets and
    traversal are unchanged, but a kept node gets a record only when it can
    match the query or is the ancestor of one that can (see
    find_walk.FindFilter); the other kept nodes still count against the
    node budget. The walk skips what ``find.resume_after`` says an earlier
    walk searched, and records in ``find.stop_path`` the first node it did
    not visit because a budget or the deadline ran out.
    """
    records = []
    lines = []
    annotations = []
    # Facts shared by all nodes of this snapshot, such as the process id of
    # each application bus name; see NodeFacts.pid().
    snapshot_scope = {}
    root_facts = NodeFacts(root, snapshot_scope)
    no_child_count_hint = object()
    unknown_child_count = object()
    provider_probe_errors = {}

    # Count each omission cause separately: the report must give the remedy
    # that matches the cause, which a merged count cannot do.
    dropped = {"count": 0, "chars": 0, "time": 0, "depth": 0,
               "budget": 0, "filtered": 0, "pressure": 0,
               "render_error": 0, "fanout": 0, "outside_view": 0}
    budget = {"chars": 0}
    # Find walk: kept nodes without a record (`light`), and the kept
    # ancestors of the node being visited that have no record yet.
    light = {"count": 0}
    pending_ancestors = []
    deadline = time.monotonic() + deadline_seconds if deadline_seconds else None
    pressure_at = max(1, int(max_tree_nodes * BUDGET_PRESSURE_RATIO))
    walk = ChildWalk(
        window_bounds=window_bounds, max_child_fanout=max_child_fanout,
        focus_probe=focus_probe, content_probe=content_probe,
        snapshot_scope=snapshot_scope, dropped=dropped,
        annotations=annotations, provider_probe_errors=provider_probe_errors,
        no_child_count_hint=no_child_count_hint,
        unknown_child_count=unknown_child_count,
    )

    def kept():
        return len(records) + light["count"]

    def note_stop(path):
        if find is not None:
            find.note_stop(path)

    def emit(record, path, render_depth, node=None, facts=None):
        """Index, store and render a kept node's record."""
        # Indices must survive across snapshots, so they cannot be traversal
        # ordinals such as len(records): inserting one element would shift
        # every later index. See StableIndexer.
        #
        # Assign the index only once the node is known to be kept. Assigning
        # it before pruning would consume indices for discarded nodes that
        # never reach records or refs; pruned nodes are not in `known`, so
        # they would take fresh indices on every snapshot and the numbers
        # would grow steadily over a session.
        record["index"] = (indexer.index_for(path, record["controlType"],
                                             record["name"],
                                             record.get("nameHash", ""))
                           if indexer is not None else len(records))
        records.append(record)
        if content_probe is not None and node is not None:
            content_probe.add_node(node, facts)
        rendered = render_element_line(record, render_depth, boxes=boxes)
        lines.append(rendered)
        budget["chars"] += len(rendered) + 1

    def emit_pending():
        """Give the kept ancestors of a find candidate their records.

        They are emitted in order before the candidate, which keeps the
        records in pre-order: nothing in an ancestor's subtree was emitted
        before its first candidate.
        """
        for entry in pending_ancestors:
            if entry["emitted"]:
                continue
            record = entry["record"] or record_for(
                entry["node"], None, entry["path"], window_bounds,
                text_limit=text_limit, facts=entry["facts"],
            )
            light["count"] -= 1
            emit(record, entry["path"], entry["render_depth"])
            entry["emitted"] = len(records)

    def is_structural_filler(record):
        """Return whether the record is a pure layout container.

        Such a container has no semantics, no state and nothing to act on.
        """
        return not (
            record["name"]
            or record["description"]
            or record["actions"]
            or record["value"]
            or record["placeholder"]
            or record["states"]
            or is_interactive_role(record["controlType"])
        )


    def visit_impl(node, depth, path, render_depth=0, parent_text=""):
        """Visit one node; `depth` drives the budget, `render_depth` indents.

        The two must differ because pruned nodes still recurse into their
        children. Indenting by `depth` would leave children at their pruned
        parent's level and open gaps in the tree (on Nautilus, indentation
        jumped from level 1 to level 6). `render_depth` advances only when a
        node is actually rendered.
        """
        if node is None:
            return
        if find is not None and find.relation(path) == "searched":
            return
        if kept() >= max_tree_nodes:
            if node is not None:
                dropped["count"] += 1
                dropped["budget"] += 1
                note_stop(path)
            if focus_probe is not None:
                focus_probe.skipped()
            return
        if depth > max_tree_depth:
            dropped["count"] += 1
            dropped["depth"] += 1
            if focus_probe is not None:
                focus_probe.skipped()
            return
        # The character budget is separate from the node budget; see
        # MAX_RENDERED_CHARS.
        if budget["chars"] >= max_rendered_chars:
            dropped["count"] += 1
            dropped["chars"] += 1
            note_stop(path)
            if focus_probe is not None:
                focus_probe.skipped()
            return
        # The time budget is separate from both; see TREE_DEADLINE_SECONDS.
        # The root is exempt: an empty tree has no addressable element, and
        # the caller could not even tell which window it observed.
        if deadline is not None and records and time.monotonic() >= deadline:
            dropped["count"] += 1
            dropped["time"] += 1
            note_stop(path)
            if focus_probe is not None:
                focus_probe.skipped()
            return

        child_count_hint = no_child_count_hint
        facts = (
            root_facts if node is root and depth == 0
            else NodeFacts(node, snapshot_scope)
        )
        if focus_probe is not None:
            focus_probe.visit(facts)

        # Pruning keeps only nodes with an interactive role that are visible
        # on screen. Only the node's own line is dropped and its children are
        # still visited: intermediate containers are often the parents of
        # valuable controls, so cutting the subtree would backfire.
        #
        # The decision reads only the fields it uses (strict_prune_probe)
        # instead of building the full record first: most nodes are pruned,
        # and building full records for them (about 8 ms each on GIMP) can
        # push a tree past the Go layer's 30 s timeout. States, actions,
        # value, placeholder and automationId are read only for kept nodes.
        if prune and depth > 0:
            # A find walk decides a childless text leaf that repeats its
            # parent's text from role, name and child count: the rule below
            # drops it whatever its frame and description hold. On web
            # pages these leaves are about half of all nodes.
            if find is not None and repeats_parent_text(
                    facts, parent_text, text_limit):
                dropped["count"] += 1
                dropped["filtered"] += 1
                return
            probe, probe_errors = strict_prune_probe(
                node, window_bounds, text_limit, facts=facts,
                lazy_description=find is not None,
            )
            record_provider_errors(walk, probe_errors)
            child_count_hint = probe["child_count"]
            if (
                child_count_hint is None
                and "child_count" in probe_errors
            ):
                child_count_hint = unknown_child_count
            probe_frame = probe["frame"]
            probe_role = probe["role"]
            # A window nested in this window that is neither SHOWING nor
            # VISIBLE is closed, and so is everything in it. Krita keeps
            # dozens of closed Qt dockers as such frames; their subtrees held
            # about half of the window's nodes and records (Qt reports
            # SHOWING on their combo-popup items), so the budgets ran out
            # before the menu bar and neither observe nor find saw
            # "Settings". Skip the whole subtree and count it as filtered.
            if (
                probe_role in AX_WINDOW_ROLES
                and not probe_errors
                and not facts.has_state(Atspi.StateType.SHOWING)
                and not facts.has_state(Atspi.StateType.VISIBLE)
            ):
                dropped["count"] += 1
                dropped["filtered"] += 1
                if focus_probe is not None:
                    focus_probe.skipped()
                return
            # Keep every visible named node, even with a non-interactive role.
            # The toggle button of LibreOffice's line-spacing combo has no
            # name and can be identified only through its parent, the
            # `Line Spacing` panel. A pure role allowlist would drop that
            # panel: the target stays in the tree but can no longer be
            # identified, which breaks the whole dialog flow. A retention
            # metric that only checks whether the target is present cannot
            # see this failure.
            probe_name = probe["name"]
            keeps = bool(probe_errors) or (
                probe_frame is not None and (
                    is_interactive_role(probe_role)
                    or bool(probe_name)
                    or bool(probe["description"])
                )
            )
            # Drop text-only leaves whose text repeats the parent's verbatim.
            #
            # Keeping every visible named node suits desktop dialogs (see the
            # Line Spacing case above), but on web pages every text node is
            # named, and a large share (about 40% on chrome://flags) repeats
            # the parent's text word for word at the same byte cost.
            #
            # Only childless text roles are dropped: a container may still be
            # the only way to identify an unnamed control.
            if (keeps and probe_role in TEXT_ONLY_ROLES and probe_name
                    and parent_text and probe_name == parent_text
                    and child_count_hint == 0):
                keeps = False
            if not keeps:
                dropped["count"] += 1
                dropped["filtered"] += 1
                closed_popups = closed_popup_indices(
                    walk, node, facts, child_count_hint)
                children, _, _ = child_entries(
                    walk, node, path, child_count_hint, facts)
                for child_index, child in children:
                    if child is None or child_index in closed_popups:
                        continue
                    # This node is not rendered, so render_depth stays and the
                    # children take its place.
                    visit(child, depth + 1,
                          path + [child_index], render_depth, parent_text)
                return

        # A find walk builds the record only for a node that can match the
        # query; see find_walk.
        if find is not None and depth > 0:
            record, candidate = candidate_record(
                find, node, facts, path, window_bounds, text_limit,
                probe_role if prune else None, probe_name if prune else "")
        else:
            candidate = True
            record = record_for(
                node, None, path, window_bounds, text_limit=text_limit,
                facts=facts,
            )
        closed_popups = closed_popup_indices(walk, node, facts, child_count_hint)

        # Under budget pressure, keep named, actionable and valued nodes by
        # dropping structural containers first. Only the container's own line
        # is dropped and its children are still visited, because a dropped
        # container is often the parent of valuable controls. A node without
        # a record here is named, so it is never such a container.
        if (
            prune
            and kept() >= pressure_at
            and record is not None
            and is_structural_filler(record)
            and depth > 0
        ):
            dropped["count"] += 1
            dropped["pressure"] += 1
            children, _, _ = child_entries(
                walk, node, path, child_count_hint, facts)
            for child_index, child in children:
                if child is None or child_index in closed_popups:
                    continue
                visit(child, depth + 1,
                      path + [child_index], render_depth, "")
            return

        entry = None
        if candidate:
            if find is not None:
                emit_pending()
            emit(record, path, render_depth, node, facts)
            role = (record["localizedControlType"] or record["controlType"]
                    or "element")
            child_text = str(record.get("name") or record.get("value") or "")
        else:
            # Kept, but without a record unless a descendant matches.
            light["count"] += 1
            entry = {"node": node, "facts": facts, "path": path[:],
                     "render_depth": render_depth, "record": record,
                     "emitted": 0}
            if record is not None:
                role = (record["localizedControlType"]
                        or record["controlType"] or "element")
                child_text = str(
                    record.get("name") or record.get("value") or "")
            else:
                role = probe_role or "element"
                child_text = probe_name

        # Collapsed menu: keep the node itself, which is still the semantic
        # target of a plain click, but do not descend into its items. Role
        # and state were already read for this node, so this check costs no
        # further provider round trip.
        # GAIL never reports SHOWING on the items of an open GTK2 menu, so
        # use the corrected value; see menu_children_popped_up().
        if (
            depth > 0
            and role.lower() in MENU_ROLES
            and (record.get("showing") if record is not None
                 else facts.showing()) is not True
        ):
            pending = (
                strict_child_count(walk, node, facts)
                if child_count_hint is no_child_count_hint
                else child_count_hint
            )
            if pending is unknown_child_count:
                pending = 0
            if pending and focus_probe is not None:
                focus_probe.skipped()
            if pending:
                annotations.append({
                    "parentRuntimeId": path[:],
                    "kind": "hidden_children",
                    "count": int(pending),
                    "reason": "collapsed_menu",
                })
                lines.append(
                    ("\t" * (render_depth + 2))
                    + "(hidden-items={}; open menu, then observe)".format(
                        pending)
                )
            return

        children, children_enumerable, planned_children = child_entries(
            walk, node, path, child_count_hint, facts
        )
        if not children_enumerable:
            if entry is not None:
                # The cells below are records; their table needs one too.
                pending_ancestors.append(entry)
                emit_pending()
                pending_ancestors.pop()
            # Huge or self-managing containers cannot be enumerated; address
            # the cells in the current viewport by coordinate instead. The
            # renderer counts its budget in records, so kept nodes without
            # one are taken off.
            rendered = render_visible_cells(
                node, render_depth, path, window_bounds, records, lines,
                text_limit=text_limit,
                max_tree_nodes=max_tree_nodes - light["count"],
                boxes=boxes,
                # Pass the indexer explicitly: the cell renderer is a
                # module-level function and cannot see this local. Without
                # it, cells fall back to traversal ordinals whose indices are
                # not stable across snapshots. This is the rendering path for
                # LibreOffice Calc sheets, the fallback once a self-managing
                # container refuses enumeration.
                indexer=indexer,
                annotations=annotations,
                content_probe=content_probe,
            )
            if rendered == 0:
                # If cell addressing fails too, say so, so the model does not
                # assume the container is empty.
                lines.append(
                    ("\t" * (render_depth + 2))
                    + "(contents not enumerated: {} manages its own descendants "
                      "and cell addressing failed)".format(role)
                )
            return

        # Check the node budget before each child_at() call. visit() also
        # checks it and returns at once, but the loop itself would not stop:
        # for a container that misreports a billion children, the child_at()
        # round trips that are bound to be discarded would alone take tens of
        # hours. Unlike should_enumerate_children(), which relies on the
        # container declaring MANAGES_DESCENDANTS, this guard relies on no
        # declaration.
        before_kept = kept()
        before_lines = len(lines)
        child_iterator = iter(children)
        child_slots_seen = 0
        if entry is not None:
            pending_ancestors.append(entry)
        try:
            while True:
                if kept() >= max_tree_nodes:
                    if focus_probe is not None:
                        focus_probe.skipped()
                    remaining = (
                        max(1, planned_children - child_slots_seen)
                        if planned_children is not None
                        else 1
                    )
                    dropped["count"] += remaining
                    dropped["budget"] += remaining
                    # Children are enumerated in index order.
                    note_stop(path + [child_slots_seen])
                    break
                try:
                    child_index, child = next(child_iterator)
                except StopIteration:
                    break
                child_slots_seen += 1
                if child is None or child_index in closed_popups:
                    continue
                # This node was rendered: indent its children one level
                # deeper and pass its text down so they can detect verbatim
                # repetition.
                visit(child, depth + 1, path + [child_index],
                      render_depth + 1, child_text)
        finally:
            if entry is not None:
                pending_ancestors.pop()

        # If a menu rendered none of its children, report that on the menu
        # itself.
        #
        # The collapsed-menu check above requires the menu itself to lack
        # SHOWING, which GIMP (GAIL) does not satisfy: the File menu is
        # SHOWING on the menu bar while its 39 items are not. Every item is
        # then pruned, and the agent sees a bare `menu "File"` line that looks
        # exactly like an empty menu, so it cannot find Export or Save As
        # although they are one click away. The global omission count at the
        # end of the tree does not help, because it does not say which menu
        # hid items.
        #
        # This check therefore looks at the outcome instead of a state bit:
        # if the menu produced no rendered line while the provider reported
        # children, state how many items are hidden.
        if (child_slots_seen and kept() == before_kept
                and len(lines) == before_lines
                and role.lower() in MENU_ROLES):
            lines.append(
                ("\t" * (render_depth + 2))
                + "(hidden-items={}; open menu, then observe)".format(
                    child_slots_seen)
            )
            annotations.append({
                "parentRuntimeId": path[:],
                "kind": "hidden_children",
                "count": int(child_slots_seen),
                "reason": "provider_children_not_rendered",
            })

    def visit(node, depth, path, render_depth=0, parent_text=""):
        records_before = len(records)
        lines_before = len(lines)
        chars_before = budget["chars"]
        light_before = light["count"]
        try:
            return visit_impl(
                node, depth, path, render_depth, parent_text
            )
        except Exception:
            # Roll back any partial records/lines from this node's subtree. Stable
            # index numbers consumed during the failed attempt may remain unused,
            # but no hidden actionable ref is published.
            del records[records_before:]
            del lines[lines_before:]
            budget["chars"] = chars_before
            light["count"] = light_before
            for entry in pending_ancestors:
                if entry["emitted"] > records_before:
                    entry["emitted"] = 0
            if focus_probe is not None:
                focus_probe.skipped()
            if node is None:
                return
            dropped["count"] += 1
            dropped["render_error"] += 1
            if kept() >= max_tree_nodes:
                dropped["budget"] += 1
                return
            role = str(safe(lambda: node_role(node), "element") or "element")
            deadline_expired = (
                deadline is not None and time.monotonic() >= deadline)
            collapsed_menu = (
                depth > 0
                and role.lower() in MENU_ROLES
                and showing_state(node) is not True
            )
            children = []
            children_allowed = False
            if not deadline_expired and not collapsed_menu:
                children, children_allowed, _ = child_entries(
                    walk, node, path, no_child_count_hint)
            if deadline_expired:
                child_status = "subtree not traversed because the time budget expired"
            elif collapsed_menu:
                child_status = (
                    "subtree not traversed because the menu is collapsed"
                )
            elif not children_allowed:
                child_status = (
                    "subtree not traversed because the container is not safely enumerable"
                )
            else:
                child_status = "eligible healthy children follow"
            diagnostic = (
                ("\t" * (render_depth + 1))
                + "(accessibility node unavailable; no public ref was issued; {})"
                .format(child_status)
            )
            if budget["chars"] < max_rendered_chars:
                remaining = max_rendered_chars - budget["chars"]
                diagnostic = diagnostic[:remaining]
                if diagnostic:
                    lines.append(diagnostic)
                    budget["chars"] += len(diagnostic) + 1
            if deadline_expired:
                dropped["time"] += 1
                return
            if not children_allowed:
                return
            child_iterator = iter(children)
            child_slots_seen = 0
            while True:
                if kept() >= max_tree_nodes:
                    dropped["count"] += 1
                    dropped["budget"] += 1
                    note_stop(path + [child_slots_seen])
                    break
                try:
                    child_index, child = next(child_iterator)
                except StopIteration:
                    break
                child_slots_seen += 1
                if child is None:
                    continue
                visit(
                    child,
                    depth + 1,
                    path + [child_index],
                    render_depth,
                    parent_text,
                )

    visit(root, 0, root_path)
    if find is not None:
        find.records_omitted = light["count"]
    if focus_probe is not None:
        focus_probe.finish()
    if dropped["count"]:
        # The omission summary keeps causes, counts and recovery paths but
        # does not repeat the whole tutorial in every snapshot: the tool
        # description carries the full semantics, and this summary reports
        # what this particular snapshot is missing.
        reasons = []
        if dropped["time"]:
            reasons.append(
                "{} not visited: time budget exhausted ({:.0f}s); tree is partial; "
                "raising node limits does not fix this".format(
                    dropped["time"], deadline_seconds))
        if dropped["chars"]:
            reasons.append(
                "{} not rendered: the server-owned character budget was exhausted "
                "({} chars); use AX mode=find or a rooted observation for the needed "
                "branch".format(
                    dropped["chars"], max_rendered_chars))
        if dropped["budget"]:
            reasons.append(
                "{} not traversed: the server-owned node budget filled up "
                "({} canonical records); use AX mode=find or a rooted observation "
                "instead of requesting a larger whole-window tree".format(
                    dropped["budget"], max_tree_nodes))
        if dropped["depth"]:
            reasons.append(
                "{} subtree root(s) not traversed: the server-owned depth budget "
                "({}) was reached. Their descendants are absent from this snapshot; "
                "AX mode=find cannot discover nodes the provider did not expose".format(
                    dropped["depth"], max_tree_depth))
        if dropped["filtered"]:
            reasons.append(
                "{} filtered: not interactable and/or not on screen "
                "(rendered {}/{}); AX mode=find searches canonical records hidden "
                "only by the working projection, and a rooted observation can return "
                "local context".format(
                    dropped["filtered"], kept(), max_tree_nodes))
        if dropped["pressure"]:
            reasons.append(
                "{} unnamed layout containers dropped after {:.0%} node pressure; "
                "children remain listed".format(
                    dropped["pressure"], BUDGET_PRESSURE_RATIO))
        if dropped["render_error"]:
            reasons.append(
                "{} node(s) lost to isolated per-node exception; no stable ref; "
                "sibling and eligible-child traversal continued; budget-stopped roots "
                "reported separately".format(
                    dropped["render_error"]))
        if dropped["outside_view"]:
            reasons.append(
                "{} child root(s) outside the view not read: a container "
                "reported more children than the walk enumerates, so only "
                "those in view were read; scroll to read others".format(
                    dropped["outside_view"]))
        if dropped["fanout"]:
            reasons.append(
                "{} child root(s) not traversed because a container exceeded "
                "MAX_CHILD_FANOUT={}; omitted subtrees are absent from this snapshot"
                .format(dropped["fanout"], max_child_fanout))
        lines.append("({} node(s) omitted — {})".format(
            dropped["count"], "; ".join(reasons) if reasons else "cause not recorded"))
        annotations.append({
            "parentRuntimeId": root_path[:],
            "kind": "provider_omission",
            "count": int(dropped["count"]),
            "reason": ",".join(
                key for key in (
                    "time", "chars", "budget", "depth", "filtered",
                    "pressure", "render_error", "fanout", "outside_view",
                ) if dropped[key]
            ),
            "detail": "; ".join(reasons) if reasons else "cause not recorded",
        })
    if provider_probe_errors:
        error_count = sum(provider_probe_errors.values())
        annotations.append({
            "parentRuntimeId": root_path[:],
            "kind": "provider_omission",
            "count": int(error_count),
            "reason": "provider_probe_error",
            "detail": (
                "accessibility provider reads were incomplete; missing "
                "descendants are unknown"
            ),
        })
    window_bus = provider_bus_name(root)
    window_path = provider_object_path(root)
    window_generation = process_generation(root_facts.pid())
    window_role = str(root_facts.value("role", "") or "")
    window_name = str(root_facts.value("name", "") or "")
    for record in records:
        record["providerWindowBusName"] = window_bus
        record["providerWindowObjectPath"] = window_path
        record["providerWindowProcessGeneration"] = window_generation
        record["providerWindowRole"] = window_role
        record["providerWindowName"] = window_name

    return records, lines, annotations
