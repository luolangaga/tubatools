"""Child enumeration of the AX snapshot walk and its omission accounting."""

from dataclasses import dataclass

from .foundation import Atspi, HARD_CHILD_CAP, should_enumerate_children
from .node_facts import NodeFacts
from .ax_records import closed_combo_popups
from .ax_virtual_table import visible_child_span


@dataclass(eq=False)
class ChildWalk:
    """State render_tree shares with the helpers that read a node's children.

    The dict and list fields are render_tree's own objects, so the counts and
    annotations these helpers add show in the walk's omission report.
    """

    window_bounds: object
    max_child_fanout: int
    focus_probe: object
    content_probe: object
    snapshot_scope: dict
    dropped: dict
    annotations: list
    provider_probe_errors: dict
    no_child_count_hint: object
    unknown_child_count: object


def null_children_annotation(path, count, total):
    """Report null child references on their parent.

    The read is incomplete, so this stays a coverage gap; the detail tells
    the agent that reading again does not reveal those children.
    """
    return {
        "parentRuntimeId": path[:],
        "kind": "provider_omission",
        "count": int(count),
        "reason": "null_reference",
        "detail": (
            "{} of {} child references returned no object; AX cannot read "
            "or address that part of the window. Use GUI there.".format(
                count, max(count, int(total or 0)))
        ),
    }


def record_provider_errors(walk, errors):
    for field in errors:
        walk.provider_probe_errors[field] = (
            walk.provider_probe_errors.get(field, 0) + 1
        )


def strict_child_count(walk, node, facts=None):
    if facts is None:
        facts = NodeFacts(node)
    raw, error = facts.strict("child_count")
    if error is None:
        try:
            return max(0, int(raw or 0))
        except Exception as exc:
            error = type(exc).__name__
    record_provider_errors(walk, {"child_count": error})
    return walk.unknown_child_count


def child_entries(walk, node, path, count_hint, facts=None):
    """Return a lazy bounded child iterator plus enumerability metadata."""
    count = (
        strict_child_count(walk, node, facts)
        if count_hint is walk.no_child_count_hint
        else count_hint
    )
    if facts is None:
        facts = NodeFacts(node)
    if count is walk.unknown_child_count:
        if facts.has_state(Atspi.StateType.MANAGES_DESCENDANTS):
            return iter(()), False, None
        if walk.focus_probe is not None:
            # find_first() treats an unreadable count as zero children;
            # this walk tries to enumerate them, so the orders diverge.
            walk.focus_probe.skipped()

        def unknown_children():
            consecutive_errors = 0
            reached_cap = True
            for child_index in range(walk.max_child_fanout):
                try:
                    child = node.get_child_at_index(child_index)
                except IndexError:
                    reached_cap = False
                    break
                except Exception as exc:
                    record_provider_errors(
                        walk, {"child_at": type(exc).__name__}
                    )
                    consecutive_errors += 1
                    yield child_index, None
                    if consecutive_errors >= 8:
                        reached_cap = False
                        break
                    continue
                consecutive_errors = 0
                if child is None:
                    reached_cap = False
                    break
                yield child_index, child
            if reached_cap:
                record_provider_errors(
                    walk, {"unknown_fanout": "bounded"}
                )

        return unknown_children(), True, None

    if should_enumerate_children(node, count, facts=facts):
        fanout = min(count, walk.max_child_fanout)
        if count > fanout:
            excess = count - fanout
            walk.dropped["count"] += excess
            walk.dropped["fanout"] += excess
        indices = range(fanout)
    else:
        span = None
        if (count > HARD_CHILD_CAP
                and not facts.has_state(Atspi.StateType.MANAGES_DESCENDANTS)):
            # Too many children to enumerate, but each one is a real
            # child: read those in view and report the rest.
            span = visible_child_span(node, count, walk.window_bounds)
        if span is None:
            return iter(()), False, count
        indices = range(span[0], span[1] + 1)
        outside = count - len(indices)
        walk.dropped["count"] += outside
        walk.dropped["outside_view"] += outside

    def known_children():
        null_count = 0
        null_annotation = None
        for child_index in indices:
            try:
                child = node.get_child_at_index(child_index)
            except Exception as exc:
                record_provider_errors(
                    walk, {"child_at": type(exc).__name__}
                )
                yield child_index, None
                continue
            if child is None:
                # Report missing objects where slots are read, so every
                # traversal path receives only accessibles or None.
                # Keep yielding the slot for budget and cursor accounting.
                null_count += 1
                updated = null_children_annotation(path, null_count, len(indices))
                if null_annotation is None:
                    null_annotation = updated
                    walk.annotations.append(null_annotation)
                else:
                    null_annotation.update(updated)
            yield child_index, child

    return known_children(), True, len(indices)


def closed_popup_indices(walk, node, facts, child_count_hint):
    """Return the child indices of a closed combo box's popups.

    The popups are skipped like a closed nested window and counted as
    filtered; see closed_combo_popups(). They still enter the content
    probe, so that opening one invalidates a reused tree.
    """
    if child_count_hint == 0:
        return frozenset()
    closed = closed_combo_popups(node, facts, walk.snapshot_scope)
    for _index, popup_facts in closed:
        walk.dropped["count"] += 1
        walk.dropped["filtered"] += 1
        if walk.focus_probe is not None:
            walk.focus_probe.skipped()
        if walk.content_probe is not None:
            walk.content_probe.add_node(popup_facts.node, popup_facts)
    return frozenset(index for index, _facts in closed)
