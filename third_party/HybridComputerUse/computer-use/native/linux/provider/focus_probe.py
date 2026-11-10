"""Note the focused node during the snapshot walk to avoid a second walk."""

from .foundation import Atspi, FIND_FIRST_BUDGET


class FocusProbe:
    """Find the focused node during the snapshot walk instead of a second walk.

    focus_and_selection() needs the node that find_first(window, FOCUSED)
    returns. render_tree() visits nodes in the
    same preorder and reads every node's state set anyway, so it can note the
    first FOCUSED node at no extra cost, as long as it has visited exactly the
    nodes find_first() would visit up to that point.

    The walks diverge where render_tree() skips a subtree that find_first()
    enters (node, depth, size or time budget; collapsed menu; per-node
    failure) or enumerates children that find_first() does not (unreadable
    child count). After the first divergence the probe stops looking and
    reports that the answer is unknown, and the caller runs find_first().
    find_first() also gives up after FIND_FIRST_BUDGET nodes; the probe
    reproduces that limit, so "no focus within the budget" is also a
    definite answer.
    """

    SEARCHING = "searching"
    FOUND = "found"
    NONE = "none"
    UNKNOWN = "unknown"

    def __init__(self, budget=FIND_FIRST_BUDGET):
        self.state = self.SEARCHING
        self.budget = budget
        self.facts = None

    def visit(self, facts):
        if self.state != self.SEARCHING:
            return
        if self.budget <= 0:
            self.state = self.NONE
            return
        self.budget -= 1
        if facts.has_state(Atspi.StateType.FOCUSED):
            self.state = self.FOUND
            self.facts = facts

    def skipped(self):
        if self.state == self.SEARCHING:
            self.state = self.UNKNOWN

    def finish(self):
        """Close the walk: an exhausted search found no focused node."""
        if self.state == self.SEARCHING:
            self.state = self.NONE

    @property
    def known(self):
        return self.state in (self.FOUND, self.NONE)
