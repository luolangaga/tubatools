"""Reuse of an unchanged window tree across observe requests.

An observe or find on an unchanged window used to walk the whole tree
again (about 0.5 s on Calc). A tree walked earlier is returned again only
if all of these hold:

1. The application emitted no AT-SPI event since just before that walk
   began (ui_events.EventMonitor; events are a dirty bit only).
2. The walk began after the listener had time to reach every application
   (REGISTRATION_GRACE_SECONDS), and the application has delivered at
   least one event to this process, which shows that its events reach
   the listener at all.
3. The request asks for the same window, application process and
   rendering parameters.
4. A cheap fingerprint read fresh for this request is unchanged: the
   selected window's frame, title and child count, and every top-level
   window's role, title, states and frame. LibreOffice emits no event when
   its window is resized or moved, and Chrome none when it is resized.
5. Re-reading the walk's kept nodes gives the same frame and state set,
   and re-reading the visible cells of every self-managed table gives the
   same cells. These are the changes a desktop survey found to arrive
   without any event: Chrome emits none when a page scrolls (frames and
   SHOWING change), and LibreOffice Calc emits events only for the focused
   cell, so a recalculation or a macro that changes other visible cells is
   silent. The re-read costs about 2 ms per 50 kept nodes plus about
   0.13 ms per visible cell, against about 0.5 s for a Calc walk.
6. The tree is younger than SNAPSHOT_REUSE_MAX_AGE_SECONDS, a backstop
   for change classes that none of the above detects.
7. Assigning element indices with the caller's current known refs gives
   the same indices as the cached walk. Indices depend on the refs the
   caller passes, so the earlier walk's calls to StableIndexer are
   recorded and replayed against the new refs.

When any check fails, the caller walks the tree again. Nothing is ever
updated from event payloads.

Entries that can never pass again are dropped as soon as the monitor sees
why: an event from their application (which includes the registry
announcing that the application left the desktop, and a window closing),
or the backstop age. The remaining entries share a byte budget and are
evicted least recently used first. Dropping or evicting an entry costs
only a walk.
"""

import time

from .ax_records import StableIndexer
from .ax_virtual_table import visible_cell_range
from .foundation import Atspi, limit_text, safe
from .node_geometry import extents

# Backstop age of a reused tree. Events, the fingerprint and the content
# re-read decide validity; this bound only limits how long a change class
# that none of them detects could stay hidden. The desktop survey found no
# such class once the re-read was added, and model turns are about 8 s apart
# at the median and 70 s at p90, so the backstop is set well above that.
SNAPSHOT_REUSE_MAX_AGE_SECONDS = 600.0

# Byte budget of all stored trees, measured as the pickled size of each
# entry. A Calc or Impress tree is 50-100 KB and the node budget caps one at
# about 400 KB, so the budget holds hundreds of windows; correctness, not
# eviction, decides reuse.
MAX_CACHE_BYTES = 64 * 1024 * 1024

# Approximate bytes per item kept outside the pickle: a recorded
# StableIndexer call, or a node or cell of the content probe.
UNPICKLED_ITEM_BYTES = 128


def state_signature(state_set):
    """Return a comparable form of an AT-SPI state set, or None."""
    if state_set is None:
        return None
    try:
        return tuple(sorted(int(state) for state in state_set.get_states()))
    except Exception:
        return None


def node_signature(node, facts=None):
    """Return the frame and state set of ``node``.

    During the walk ``facts`` supplies values the walk already read, so
    recording costs no provider call; revalidation reads them again.
    """
    if facts is not None:
        return facts.screen_frame(), state_signature(facts.value("states"))
    return extents(node), state_signature(safe(node.get_state_set))


def cell_text(cell, text_limit):
    """Read a cell the way text_value_with_status() reports it.

    One get_text() call instead of a character count plus get_text(); the
    result equals text_value_with_status() whenever both reads succeed. Any
    difference in a failure case only makes the entry look changed, which
    costs a walk.
    """
    try:
        text_iface = cell.get_text_iface()
        if text_iface is None:
            return "", True
        return limit_text(
            str(Atspi.Text.get_text(text_iface, 0, -1) or ""),
            text_limit=text_limit), True
    except Exception:
        return "", False


def visible_cell_values(node, count, text_limit):
    """Return the viewport span and the first ``count`` cell texts.

    Cells are read in the order render_visible_cells() reads them, so equal
    values mean the rendered cells are equal.
    """
    span = visible_cell_range(node)
    if span is None:
        return None, ()
    table = safe(node.get_table_iface)
    if table is None:
        return span, ()
    row0, row1, col0, col1 = span
    values = []
    for row in range(row0, row1 + 1):
        for col in range(col0, col1 + 1):
            if len(values) >= count:
                return span, tuple(values)
            cell = safe(
                lambda r=row, c=col: Atspi.Table.get_accessible_at(table, r, c))
            values.append(
                None if cell is None else cell_text(cell, text_limit))
    return span, tuple(values)


class ContentProbe:
    """What a walk read that can change without an AT-SPI event.

    The walk records, for every kept node, the frame and state set it read,
    and for every self-managed table the viewport span and the text of each
    cell it probed. unchanged() reads the same facts again.
    """

    def __init__(self, text_limit):
        self.text_limit = text_limit
        self.nodes = []
        self.tables = []

    def __len__(self):
        return len(self.nodes) + sum(len(values) for _n, _s, values in self.tables)

    def add_node(self, node, facts):
        self.nodes.append((node, node_signature(node, facts)))

    def add_table(self, node, span, values):
        """Record a table; ``values`` is filled while its cells are read."""
        self.tables.append((node, span, values))

    def unchanged(self):
        try:
            for node, signature in self.nodes:
                if node_signature(node) != signature:
                    return False
            for node, span, values in self.tables:
                if visible_cell_values(node, len(values), self.text_limit) != (
                        span, tuple(values)):
                    return False
        except Exception:
            return False
        return True


class RecordingIndexer(StableIndexer):
    """StableIndexer that records every assignment for later replay."""

    def __init__(self, known):
        super().__init__(known)
        self.calls = []

    def index_for(self, path, role, name, name_hash=""):
        index = super().index_for(path, role, name, name_hash)
        self.calls.append((tuple(path), role, name, name_hash, index))
        return index


def replay_matches(calls, known_refs):
    """Return whether ``known_refs`` reproduces the recorded indices.

    A walk that failed on a node still consumed indices for that node,
    and those calls are recorded too, so the replay sees the same state.
    """
    indexer = StableIndexer(known_refs)
    for path, role, name, name_hash, index in calls:
        if indexer.index_for(list(path), role, name, name_hash) != index:
            return False
    return True


def window_fingerprint(bounds, title, child_count, window_selection):
    """Return the cheap facts that must match before a tree is reused."""
    candidates = tuple(
        (
            candidate.get("index"),
            candidate.get("role"),
            candidate.get("title"),
            candidate.get("active"),
            candidate.get("showing"),
            candidate.get("visible"),
            candidate.get("modal"),
            tuple(sorted((candidate.get("bounds") or {}).items())),
        )
        for candidate in (window_selection or {}).get("candidates", [])
    )
    return (
        tuple(sorted((bounds or {}).items())),
        title,
        child_count,
        candidates,
    )


class TreeEntry:
    __slots__ = (
        "key", "bus", "fingerprint", "token", "walked_at", "index_calls",
        "tree", "size", "probe",
    )

    def __init__(self, key, bus, fingerprint, token, walked_at, index_calls,
                 tree, size, probe):
        self.key = key
        self.bus = bus
        self.fingerprint = fingerprint
        self.token = token
        self.walked_at = walked_at
        self.index_calls = index_calls
        self.tree = tree
        self.size = size
        self.probe = probe


class TreeReuseCache:
    """Window trees, each with the event token taken before its walk.

    Entries are kept in least-recently-used order within a byte budget.
    """

    def __init__(self, monitor, clock=time.monotonic,
                 max_age=SNAPSHOT_REUSE_MAX_AGE_SECONDS,
                 max_bytes=MAX_CACHE_BYTES):
        self.monitor = monitor
        self.clock = clock
        self.max_age = max_age
        self.max_bytes = max_bytes
        self._entries = {}
        self._bytes = 0
        # Outcome of the last lookup, reported by phase timing only.
        self.last_decision = None
        self.last_probe_seconds = None

    def __len__(self):
        return len(self._entries)

    @property
    def size_bytes(self):
        return self._bytes

    def _drop(self, key):
        entry = self._entries.pop(key, None)
        if entry is not None:
            self._bytes -= entry.size

    def prune(self):
        """Drop every entry that can never pass the checks again.

        An event from the entry's application after its walk began (the
        monitor counts an application leaving the desktop and its windows
        closing as such events) or the backstop age make an entry useless.
        Call after a pump, so that queued events are counted.
        """
        now = self.clock()
        for key in [
            key for key, entry in self._entries.items()
            if not self.monitor.unchanged_since(entry.bus, entry.token)
            or now - entry.walked_at > self.max_age
        ]:
            self._drop(key)

    def begin_walk(self, bus):
        """Drain queued events and return a token for a walk, or None.

        None means the backlog could not be drained, so events emitted
        before the walk may still be queued (or dropped by the bus); such a
        walk is never stored for reuse.
        """
        if not self.monitor.pump():
            return None
        self.prune()
        return bus, self.monitor.mark(bus), self.clock()

    def lookup(self, bus, key, fingerprint, known_refs):
        """Return the cached tree for ``key`` if every reuse check passes.

        The caller must read the fingerprint before calling, so that the
        pump here dispatches every event the application emitted before it
        answered those reads (D-Bus keeps one sender's messages in order).
        """
        self.last_probe_seconds = None
        self.last_decision = self._decide(bus, key, fingerprint, known_refs)
        if self.last_decision != "reused":
            return None
        # Least recently used order: a hit moves the entry to the end.
        entry = self._entries.pop(key)
        self._entries[key] = entry
        return entry.tree

    def _decide(self, bus, key, fingerprint, known_refs):
        """Return "reused" or the name of the first reuse check that failed."""
        if not self.monitor.active:
            return "monitor_inactive"
        if not self.monitor.pump():
            return "backlog"
        entry = self._entries.get(key)
        if entry is not None and not self.monitor.unchanged_since(
                bus, entry.token):
            decision = "events_since_walk"
        elif entry is not None and self.clock() - entry.walked_at > self.max_age:
            decision = "too_old"
        else:
            decision = None
        # Every pump is a chance to drop entries that can never be valid
        # again, including this one when it failed a check above.
        self.prune()
        if decision is not None:
            return decision
        if entry is None:
            return "no_entry"
        # An application that has never delivered an event to this
        # process gives no evidence that its events reach the listener.
        if self.monitor.last_event_at(bus) is None:
            return "no_event_seen"
        if not self.monitor.trusted_after(entry.walked_at):
            return "registration_grace"
        if entry.fingerprint != fingerprint:
            return "fingerprint"
        if not replay_matches(entry.index_calls, known_refs):
            return "known_refs"
        if entry.probe is not None:
            started = self.clock()
            unchanged = entry.probe.unchanged()
            self.last_probe_seconds = self.clock() - started
            if not unchanged:
                self._drop(key)
                return "content_changed"
            # The re-read is itself a set of reads from the application, so
            # every event it emitted before answering is now queued.
            if not self.monitor.pump():
                return "backlog"
            if not self.monitor.unchanged_since(bus, entry.token):
                self._drop(key)
                return "events_since_walk"
        return "reused"

    def store(self, key, fingerprint, walk, index_calls, tree, size,
              probe=None):
        """Store a walk begun with begin_walk(); ``size`` is the tree's bytes."""
        if walk is None or not self.monitor.active:
            return
        bus, token, walked_at = walk
        self._drop(key)
        size = int(size) + UNPICKLED_ITEM_BYTES * (
            len(index_calls) + (len(probe) if probe is not None else 0))
        if size > self.max_bytes:
            return
        self._entries[key] = TreeEntry(
            key, bus, fingerprint, token, walked_at, list(index_calls),
            tree, size, probe)
        self._bytes += size
        while self._bytes > self.max_bytes:
            self._drop(next(iter(self._entries)))
