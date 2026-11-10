"""Per-snapshot cached reads of one AT-SPI node and strict pruning probes."""

from .foundation import (
    Atspi,  # pyright: ignore[reportAttributeAccessIssue]
    frame,
    is_interactive_role,
    limit_text,
    node_pid,
    provider_bus_name,
)
from .node_geometry import corrected_screen_frame, sane_rect_frame
from .node_visibility import popped_up_menu_item


class NodeFacts:
    """Provider reads of one node, each performed at most once per snapshot.

    Every read below is a D-Bus round trip of 30-60 us: the libatspi client
    cache is not consulted in this process, because it only answers while
    atspi_event_main() runs or after set_cache_mask(), and the provider does
    neither. The pruning probe, the enumeration guards, the record builder
    and the focus lookup used to read the same fields again, which made a
    Calc snapshot about three times slower than one pass over the tree.

    An instance lives for one snapshot only. Values are never carried across
    requests, because the provider cannot tell which of them went stale.

    `strict()` keeps provider failures distinct from empty values, as the
    pruning probe requires. `value()` and `has_state()` follow the semantics
    of `safe()` and `state_contains()`, as the record builder requires.
    Looking up a missing method raises in both cases, as it did before.
    """

    __slots__ = ("node", "_reads", "_scope")

    def __init__(self, node, scope=None):
        self.node = node
        self._reads = {}
        # Facts shared by every node of one snapshot; see pid().
        self._scope = scope if scope is not None else {}

    def scope_cache(self, name):
        """Return a dict shared by every node of this snapshot."""
        return self._scope.setdefault(name, {})

    def showing(self):
        """Return SHOWING as true/false/unknown, correcting GAIL popup items.

        See menu_children_popped_up(). The parent is read only for a VISIBLE
        menu-role node without SHOWING, which is rare outside GTK2 menus.
        """
        showing = self.state_flag(Atspi.StateType.SHOWING)
        if showing is False and popped_up_menu_item(
            self.node,
            str(self.value("role", "") or ""),
            self.has_state(Atspi.StateType.VISIBLE),
            self.screen_frame() is not None,
            self.scope_cache("menu_popup"),
        ):
            return True
        return showing

    def _operation(self, field):
        node = self.node
        if field == "child_count":
            return node.get_child_count
        if field == "states":
            return node.get_state_set
        if field == "component":
            return node.get_component_iface
        if field == "role":
            return node.get_role_name
        if field == "name":
            return node.get_name
        if field == "description":
            return node.get_description
        if field == "extents":
            component, _error = self.strict("component")
            return lambda: Atspi.Component.get_extents(
                component, Atspi.CoordType.SCREEN
            )
        raise KeyError(field)

    def strict(self, field):
        """Return ``(value, error_name)``; read the provider at most once."""
        cached = self._reads.get(field)
        if cached is None:
            operation = self._operation(field)
            try:
                cached = (operation(), None)
            except Exception as exc:
                cached = (None, type(exc).__name__)
            self._reads[field] = cached
        return cached

    def value(self, field, default=None):
        value, error = self.strict(field)
        if error is not None or value is None:
            return default
        return value

    def has_state(self, state):
        """Return state_contains() for this node."""
        state_set = self.value("states")
        if state_set is None:
            return False
        try:
            return bool(state_set.contains(state))
        except Exception:
            return False

    def state_flag(self, state):
        """Return state_value() for this node: true, false or unknown."""
        try:
            state_set, error = self.strict("states")
        except Exception:
            return None
        if error is not None or state_set is None:
            return None
        try:
            return bool(state_set.contains(state))
        except Exception:
            return None

    def child_count(self):
        """Return child_count() for this node: 0 when unreadable."""
        return int(self.value("child_count", 0) or 0)

    def screen_frame(self):
        """Return extents() for this node."""
        component = self.value("component")
        if component is None:
            return None
        return corrected_screen_frame(
            self.node, sane_rect_frame(self.value("extents")),
            self.scope_cache("embedded_origins"), role=self.value("role", ""))

    def relative_frame(self, window_bounds):
        bounds = self.screen_frame()
        if bounds is None or window_bounds is None:
            return bounds
        return frame(
            bounds["x"] - window_bounds["x"],
            bounds["y"] - window_bounds["y"],
            bounds["width"],
            bounds["height"],
        )

    def pid(self):
        """Return node_pid() for this node, read once per application.

        libatspi answers get_process_id() by asking the bus daemon for the
        process that owns the node's bus name, so every node of one
        application returns the same value. Reading it for each record cost
        one round trip per kept node.
        """
        bus = provider_bus_name(self.node)
        if not bus:
            return node_pid(self.node)
        pids = self._scope.setdefault("pid_by_bus", {})
        pid = pids.get(bus)
        if pid is None:
            pid = node_pid(self.node)
            if pid:
                pids[bus] = pid
        return pid


def strict_prune_probe(node, window_bounds, text_limit, facts=None,
                       lazy_description=False):
    """Read pruning inputs without converting provider errors into empty values.

    Child count and frame are read first. A node without a frame and without
    a read error is pruned whatever its role, name or description, so those
    three fields are read only for nodes that may be kept. Every field that
    is read stays in `facts` for the record builder.

    With ``lazy_description`` the description is read only when role and
    name do not already keep the node; the pruning decision is the same.
    """
    if facts is None:
        facts = NodeFacts(node)
    values = {}
    errors = {}

    def read(field, default=None):
        value, error = facts.strict(field)
        if error is not None:
            errors["frame" if field == "extents" else field] = error
            return default
        return value

    raw_count = read("child_count")
    if raw_count is None:
        values["child_count"] = None
    else:
        try:
            values["child_count"] = max(0, int(raw_count or 0))
        except Exception as exc:
            errors["child_count"] = type(exc).__name__
            values["child_count"] = None

    component = read("component")
    if component is None:
        values["frame"] = None
    else:
        bounds = sane_rect_frame(read("extents"))
        if bounds is not None and window_bounds is not None:
            bounds = frame(
                bounds["x"] - window_bounds["x"],
                bounds["y"] - window_bounds["y"],
                bounds["width"],
                bounds["height"],
            )
        values["frame"] = bounds

    if values["frame"] is None and not errors:
        values["role"] = values["name"] = values["description"] = ""
        return values, errors

    values["role"] = str(read("role", "") or "")
    values["name"] = limit_text(
        str(read("name", "") or ""),
        text_limit=text_limit,
    )
    if lazy_description and (
            values["name"] or is_interactive_role(values["role"])):
        values["description"] = ""
        return values, errors
    values["description"] = limit_text(
        str(read("description", "") or ""),
        text_limit=text_limit,
    )
    return values, errors
