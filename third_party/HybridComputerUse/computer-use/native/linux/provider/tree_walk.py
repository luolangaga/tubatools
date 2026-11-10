"""Bounded provider-tree traversal primitives."""

from .foundation import (
    FIND_FIRST_BUDGET,
    MAX_CHILD_FANOUT,
    MAX_ELEMENTS,
    child_at,
    child_count,
    should_enumerate_children,
)


def find_first(root, predicate, budget=None):
    """Return the first matching node without allowing an unbounded tree walk."""
    if budget is None:
        budget = [FIND_FIRST_BUDGET]
    if root is None or budget[0] <= 0:
        return None
    budget[0] -= 1
    if predicate(root):
        return root
    if not should_enumerate_children(root):
        return None
    for index in range(min(child_count(root), MAX_CHILD_FANOUT)):
        if budget[0] <= 0:
            return None
        found = find_first(child_at(root, index), predicate, budget)
        if found is not None:
            return found
    return None


def iter_all(root):
    items = []

    def visit(node):
        if node is None or len(items) >= MAX_ELEMENTS:
            return
        items.append(node)
        if not should_enumerate_children(node):
            return
        for index in range(min(child_count(node), MAX_CHILD_FANOUT)):
            if len(items) >= MAX_ELEMENTS:
                return
            visit(child_at(node, index))

    visit(root)
    return items
