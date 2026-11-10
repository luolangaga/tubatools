"""Application and full-desktop snapshot construction."""

import hashlib
import pickle
import time
from copy import deepcopy
from dataclasses import dataclass, field
from .foundation import (
    Atspi,
    DEFAULT_TEXT_LIMIT,
    GUI_SNAPSHOT_LAYOUT_ATTEMPTS,
    MAX_DEPTH,
    MAX_ELEMENTS,
    child_count,
    has_text_iface,
    limit_text,
    node_name,
    node_pid,
    node_role,
    process_generation,
    provider_bus_name,
    provider_object_path,
    safe,
    state_contains,
)

from .ax_snapshot import FIND_WALK_LIMITS, render_tree
from .focus_probe import FocusProbe
from .find_walk import FindFilter, FindWalk
from .tree_reuse import (
    ContentProbe,
    RecordingIndexer,
    TreeReuseCache,
    window_fingerprint,
)
from . import ui_events

from .x11_windows import (
    accessible_window_title,
    app_windows,
    iter_apps,
    main_window,
    select_window_by_handle,
    strong_window_instance_handles,
    unmapped_window_indices,
    window_instance_handle,
    window_bounds,
)
from .desktop_layout import (
    desktop_bounds,
    desktop_layout_frame_id,
    desktop_layout_state,
)

from .gui_capture import (
    capture_gui_pixels,
    encoded_screenshot,
    gui_model_dimensions,
    pixbuf_sample,
    scale_to_model,
    zoom_pixels,
)

from .atspi_inventory import (
    resolve_app,
    resolve_app_identity,
)
from .primitives import quoted
from .phase_timing import lap, note
from .tree_walk import find_first
from .window_diagnostics import (
    foreign_foreground_note,
    modal_diagnostic,
    window_label,
)


def element_reference(role, name):
    """Render an element reference; the single renderer for this form.

    Uses the same grammar as renderElementReference on the Go side. The hard
    contract is round-tripping: the returned string, pasted verbatim into an
    ``expect`` target, must parse back to the same reference. Names are
    therefore always quoted (``entry "Address and search bar"``) and unnamed
    elements are written as ``role="entry"``. An unquoted ``role name``
    string spans both fields and cannot be split, so the tool would reject
    text it printed itself.
    """
    role = (role or "").strip()
    name = (name or "").strip()
    if not role and not name:
        return "an element with neither a role nor a name"
    if not name:
        return "role=" + quoted(role)
    if not role:
        return quoted(name)
    return role + " " + quoted(name)


def is_focused(node):
    return state_contains(node, Atspi.StateType.FOCUSED)


def same_provider_node(first, second):
    """Return whether two accessibles name the same provider object."""
    first_path = provider_object_path(first)
    return bool(first_path) and (
        first_path == provider_object_path(second)
        and provider_bus_name(first) == provider_bus_name(second)
    )


def focused_node(app_pid, walked_window=None, focus_probe=None):
    """Return the focused node of the application's main window, or None.

    The focused node is the first FOCUSED node of ``main_window()`` in
    find_first() order. When ``walked_window`` is that same window and the
    snapshot walk already settled the answer (see FocusProbe), the walk's
    answer is used and the window is not searched again.
    """
    for app in iter_apps():
        if node_pid(app) != app_pid:
            continue
        _, win = main_window(app)
        if (
            focus_probe is not None
            and focus_probe.known
            and walked_window is not None
            and same_provider_node(win, walked_window)
        ):
            return focus_probe.facts.node if focus_probe.facts else None
        return find_first(win, is_focused)
    return None


def focus_summary_of(focused, text_limit=DEFAULT_TEXT_LIMIT):
    if focused is None:
        return None
    role = node_role(focused)
    name = limit_text(node_name(focused), text_limit=text_limit)
    return element_reference(role, name)


def selected_text_of(focused, text_limit=DEFAULT_TEXT_LIMIT):
    if focused is None or not has_text_iface(focused):
        return None
    text_iface = safe(focused.get_text_iface)
    selections = safe(lambda: Atspi.Text.get_text_selections(text_iface), [])
    if selections:
        selection = selections[0]
        end_offset = selection.end_offset
        if text_limit is not None:
            end_offset = min(end_offset, selection.start_offset + text_limit + 1)
        value = Atspi.Text.get_text(
            text_iface, selection.start_offset, end_offset
        )
        return limit_text(value, text_limit=text_limit)
    return None


def focus_and_selection(
    app_pid, text_limit=DEFAULT_TEXT_LIMIT, walked_window=None,
    focus_probe=None,
):
    """Return ``(focusedSummary, selectedText)`` from one focus lookup.

    A failure while locating focus makes both values None; a failure while
    reading one of them makes only that value None.
    """
    try:
        focused = focused_node(
            app_pid, walked_window=walked_window, focus_probe=focus_probe
        )
    except Exception:
        return None, None
    try:
        summary = focus_summary_of(focused, text_limit=text_limit)
    except Exception:
        summary = None
    try:
        selected = selected_text_of(focused, text_limit=text_limit)
    except Exception:
        selected = None
    return summary, selected


def top_level_window_inventory(app, selected_index=None):
    windows = app_windows(app)
    strong_handles = strong_window_instance_handles(app, windows)
    hidden = unmapped_window_indices(app, windows, strong_handles)
    inventory = []
    for index, window in windows:
        if index in hidden and index != selected_index:
            continue
        inventory.append({
            "title": accessible_window_title(app, window, strong_handles),
            "role": str(node_role(window) or "window").strip().lower(),
            "handle": strong_handles.get(index) or (
                "weak:pid{}/win{}/{}".format(
                    node_pid(app) or 0,
                    index,
                    str(node_role(window) or "window").strip().lower().replace(
                        " ", "-"
                    ),
                )
            ),
            "providerBusName": provider_bus_name(window),
            "providerObjectPath": provider_object_path(window),
            "providerProcessGeneration": process_generation(node_pid(window)),
            "selected": index == selected_index,
            "active": state_contains(window, Atspi.StateType.ACTIVE),
            "modal": state_contains(window, Atspi.StateType.MODAL),
            "showing": state_contains(window, Atspi.StateType.SHOWING),
        })
    return inventory


# Window trees kept for reuse by later requests of this provider process.
TREES = TreeReuseCache(ui_events.MONITOR)

# Omission reasons that make a walk unfit for reuse: a walk cut by the time
# budget or by provider read errors depends on transient conditions, so a
# later request must walk again rather than inherit the gaps.
TRANSIENT_OMISSIONS = ("time", "render_error", "provider_probe_error")


def note_reuse_decision(bus, key=None, known_refs=None):
    """Record why the last lookup did or did not reuse a tree (timing only)."""
    monitor = TREES.monitor
    last_event = monitor.last_event_at(bus)
    note("treeReuse", {
        "decision": TREES.last_decision,
        "probeMs": (
            None if TREES.last_probe_seconds is None
            else round(TREES.last_probe_seconds * 1000, 1)),
        "entries": len(TREES),
        "cacheKB": TREES.size_bytes // 1024,
        "key": [str(part) for part in (key or ())[3:6]],
        "knownRefs": len(known_refs or {}),
        "monitorActive": monitor.active,
        "events": monitor.mark(bus),
        "lastEventAgeMs": (
            None if last_event is None
            else round((monitor.clock() - last_event) * 1000)),
        "registeredAgeMs": (
            None if monitor.registered_at is None
            else round((monitor.clock() - monitor.registered_at) * 1000)),
    })


def walk_is_reusable(annotations):
    for item in annotations or []:
        if item.get("kind") != "provider_omission":
            continue
        reasons = str(item.get("reason") or "").split(",")
        if any(reason in TRANSIENT_OMISSIONS for reason in reasons):
            return False
    return True


def utc_timestamp():
    """Return the sampling time in UTC with millisecond precision.

    Snapshots carry their own timestamp because a desktop snapshot can be
    tens of seconds stale without the caller noticing; unlike DOM changes in
    a browser, desktop changes usually come with no observable event.
    """
    now = time.time()
    return "{}.{:03d}Z".format(
        time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime(now)), int((now % 1) * 1000))


# Roles that show the application is busy. Progress bars and busy indicators
# are toolkit-independent controls.
BUSY_ROLES = {"progress bar", "busy indicator"}


def busy_signal(window, records):
    """Report whether the application explicitly signals that it is busy.

    An asynchronous or long-running operation looks like "nothing changed" in
    the tree, which reads the same as content cut by the budget; this signal
    tells the caller whether to wait or to observe differently.

    Only presence carries meaning: a missing BUSY state or progress bar does
    not prove that the operation finished, because toolkits set these states
    inconsistently over AT-SPI.
    """
    busy_state = getattr(Atspi.StateType, "BUSY", None)
    window_busy = bool(
        busy_state is not None and state_contains(window, busy_state))
    indicators = sum(
        1 for record in records
        if str(record.get("controlType") or "").strip().lower() in BUSY_ROLES)
    return {"windowBusy": window_busy, "progressElements": indicators}


def snapshot_digest(window_handle, lines, focused, selected):
    """Return a content fingerprint of a snapshot.

    Equal digests mean two observations are textually identical. Screenshots
    are deliberately excluded: pixels vary between frames (caret blinking,
    anti-aliasing), so including them would make digests never match. The
    digest covers the whole semantic channel: root window identity, every
    tree line, focus and selection.
    """
    payload = "\n".join(
        [str(window_handle or "")] + [str(line) for line in (lines or [])]
        + [str(focused or ""), str(selected or "")])
    return hashlib.sha1(payload.encode("utf-8", "replace")).hexdigest()[:12]


def build_snapshot(
    query,
    text_limit=DEFAULT_TEXT_LIMIT,
    max_tree_nodes=MAX_ELEMENTS,
    max_tree_depth=MAX_DEPTH,
    prune=True,
    boxes=False,
    known_refs=None,
    app_identity=None,
    target_window_handle=None,
    reuse_tree=False,
    find_walk=False,
    find_query=None,
    find_resume_after=None,
    selected_window=None,
):
    """Build an application snapshot.

    ``reuse_tree=True`` lets an unchanged window return the tree of an
    earlier walk instead of walking it again; see tree_reuse for the checks.
    Every walk, reused or not, may be reused later. Post-action snapshots
    pass False so that the tree after an action is always read fresh.

    ``selected_window`` is an internal selection from the current operation.
    Post-action reads share this selection with their pixel captures so a
    foreground change cannot select a different sibling between the two reads.

    ``find_walk=True`` walks with the AX find caps (FIND_WALK_LIMITS)
    instead of the render budget; see FIND_WALK_MAX_RECORDS. The snapshot
    then holds records only for nodes that can match ``find_query`` (the
    wire ``find_query`` object) and their ancestors; see find_walk. A walk
    that ran out of budget or time reports the first node it did not visit
    as ``findResume``; passing it back as ``find_resume_after`` continues
    there. A find walk is never reused or stored for reuse: its records
    depend on the query.

    The snapshot carries no image. AX observe=full/screenshot returns the
    full-desktop GUI frame, which the Go core captures through the desktop
    batch after this read.

    ``boxes`` defaults to False;
    geometry takes 35-50% of the rendered tree. Frames are window-relative
    and mainly serve debugging. Element-targeted clicks read coordinates from the records
    internally, so leaving frames out of the rendering costs no AX
    capability. GUI coordinates must come from the GUI tool's full-desktop
    image, never from these frames.
    """
    app = (
        resolve_app_identity(app_identity)
        if app_identity is not None
        else resolve_app(query)
    )
    lap("snapshot.resolve_app")
    window_index, window, window_selection = (
        selected_window if selected_window is not None
        else select_window_by_handle(app, target_window_handle)
    )
    lap("snapshot.select_window")
    bounds = window_bounds(app, window_index, window)
    handle = window_instance_handle(app, window_index, window)
    title = window_label(window, text_limit=text_limit)
    app_bus = provider_bus_name(app)
    reuse_key = (
        app_bus,
        provider_object_path(window),
        window_index,
        handle,
        text_limit,
        max_tree_nodes,
        max_tree_depth,
        bool(prune),
        bool(boxes),
        bool(find_walk),
    )
    fingerprint = window_fingerprint(
        bounds, title, child_count(window), window_selection
    )
    find = (
        FindWalk(FindFilter.from_wire(find_query), find_resume_after)
        if find_walk else None
    )
    if find is not None:
        reuse_tree = False
    cached = (
        TREES.lookup(app_bus, reuse_key, fingerprint, known_refs)
        if reuse_tree
        else None
    )
    if reuse_tree:
        note_reuse_decision(app_bus, reuse_key, known_refs)
    if cached is not None:
        # The tree is stored pickled: unpickling gives each request its own
        # copy, faster than deepcopy, and the pickle length is the entry's
        # size for the cache budget.
        records, lines, annotations = pickle.loads(cached[0])
        focus_probe = cached[1]
        lap("snapshot.reuse_tree")
    else:
        walk = TREES.begin_walk(app_bus) if find is None else None
        indexer = RecordingIndexer(known_refs)
        focus_probe = FocusProbe()
        content_probe = ContentProbe(text_limit) if find is None else None
        limits = (
            FIND_WALK_LIMITS if find_walk
            else {"max_tree_nodes": max_tree_nodes}
        )
        records, lines, annotations = render_tree(
            window,
            bounds,
            [window_index],
            text_limit=text_limit,
            max_tree_depth=max_tree_depth,
            prune=prune,
            boxes=boxes,
            indexer=indexer,
            focus_probe=focus_probe,
            content_probe=content_probe,
            find=find,
            **limits,
        )
        if walk is not None and walk_is_reusable(annotations):
            try:
                stored = pickle.dumps(
                    (records, lines, annotations), pickle.HIGHEST_PROTOCOL)
            except Exception:
                stored = None
            if stored is not None:
                TREES.store(
                    reuse_key, fingerprint, walk, indexer.calls,
                    (stored, focus_probe), len(stored), content_probe,
                )
        lap("snapshot.render_tree")
    pid = node_pid(app)
    focused, selected = focus_and_selection(
        pid, text_limit=text_limit, walked_window=window,
        focus_probe=focus_probe,
    )
    lap("snapshot.handle_title_focus")
    snapshot = {
        "app": {
            "name": node_name(app),
            "bundleIdentifier": node_name(app),
            "pid": pid,
            "providerBusName": provider_bus_name(app),
            "processGeneration": process_generation(pid),
        },
        "windowTitle": title,
        # A title does not identify a window; see window_instance_handle().
        "windowHandle": handle,
        "windows": top_level_window_inventory(app, window_index),
        "windowBounds": bounds,
        "windowSelection": window_selection,
        # Freshness and completeness are basic metadata for a stateful
        # protocol. sampledAt is when this snapshot was sampled; snapshotId is
        # its content fingerprint, which the Go side compares for equivalence.
        "sampledAt": utc_timestamp(),
        "snapshotId": snapshot_digest(handle, lines, focused, selected),
        "busy": busy_signal(window, records),
        "treeLines": lines,
        "focusedSummary": focused,
        "selectedText": selected,
        "elements": records,
        "annotations": annotations,
        # Return path -> (index, role, name) to Go; passing it back unchanged
        # with the next request keeps indices stable across snapshots. See
        # StableIndexer.
        "refs": {
            ".".join(str(p) for p in r["runtimeId"]): {
                "index": r["index"], "role": r["controlType"], "name": r["name"],
                "nameHash": r.get("nameHash", ""),
            }
            for r in records
        },
        # Modal notes travel with every snapshot, not only with observations:
        # post-action snapshots need them most, because the action may be what
        # opened the dialog. The Go appSnapshot has no such field and
        # encoding/json ignores it; the dispatcher pops it into the notes.
        "modalNotes": modal_diagnostic(window, app) + foreign_foreground_note(app),
    }
    if find is not None and find.candidates_only:
        # Refs minted from these records must be bound to provider objects:
        # a selector that is unique among candidates need not be unique in
        # the window.
        snapshot["findCandidatesOnly"] = True
    if find is not None and find.stop_path is not None:
        snapshot["findResume"] = find.stop_path
    lap("snapshot.inventory_and_notes")
    return snapshot


@dataclass(frozen=True)
class CapturedGUISnapshot:
    """Call-local frame; owned pixels and capture metadata never change.

    Only encoding is lazy. Nothing may write to the privately owned pixbuf.
    Returned dictionaries are copies so response edits cannot alter the cache.
    """

    _metadata: dict
    _pixbuf: object
    _bounds: object = None
    _encoded: object = field(default=None, init=False, repr=False, compare=False)
    _encoding_error: object = field(default=None, init=False, repr=False, compare=False)
    _sample: object = field(default=None, init=False, repr=False, compare=False)

    def __post_init__(self):
        object.__setattr__(self, "_metadata", deepcopy(self._metadata))
        object.__setattr__(self, "_bounds", deepcopy(self._bounds))

    @property
    def has_image(self):
        return self._pixbuf is not None

    @property
    def frame_id(self):
        return self._metadata.get("frameId")

    def matches_bounds(self, bounds):
        return self._bounds == bounds

    @property
    def pixels(self):
        """Exact native comparison sample of the grab behind the delivered image."""
        if self._sample is None and self.has_image:
            object.__setattr__(self, "_sample", pixbuf_sample(self._pixbuf))
        return self._sample

    def zoom(self, geometry):
        """Encode a zoom image cut from this grab; see zoom_geometry."""
        if not self.has_image:
            return None
        return zoom_pixels(self._pixbuf, geometry)

    def encode(self):
        if self._encoding_error is not None:
            raise RuntimeError(self._encoding_error)
        if self._encoded is None:
            try:
                screenshot = encoded_screenshot(
                    scale_to_model(self._pixbuf, self._bounds)
                ) if self.has_image else None
            except Exception as exc:
                object.__setattr__(self, "_encoding_error", str(exc)[:512])
                raise
            data = screenshot.get("data") if screenshot else None
            encoded = dict(self._metadata, **{
                "screenshotPngBase64": data,
                "screenshotMimeType": screenshot.get("mimeType") if screenshot else None,
                "screenshotEncoding": screenshot.get("encoding") if screenshot else None,
            })
            object.__setattr__(self, "_encoded", encoded)
        return deepcopy(self._encoded)


def capture_gui_snapshot(bounds=None):
    """Bracket raw desktop capture with layout samples, without encoding."""
    if bounds is None:
        bounds = desktop_bounds()
    pixbuf = None
    layout = None
    # Each attempt's closing sample also opens the next attempt, so A attempts
    # cost A+1 layout queries. Identity is the coordinate frame id, which
    # ignores window titles: a title change cannot move a coordinate.
    before_layout = desktop_layout_state(bounds)
    for _ in range(GUI_SNAPSHOT_LAYOUT_ATTEMPTS):
        pixbuf = capture_gui_pixels(bounds)
        after_layout = desktop_layout_state(bounds)
        if (
            before_layout is not None
            and after_layout is not None
            and desktop_layout_frame_id(before_layout)
            == desktop_layout_frame_id(after_layout)
        ):
            layout = after_layout
            break
        before_layout = after_layout
    dimensions = gui_model_dimensions(bounds)
    return CapturedGUISnapshot({
        "sampledAt": utc_timestamp(),
        "coordinateSpace": "screen_image_px",
        "width": dimensions["width"] if dimensions is not None else None,
        "height": dimensions["height"] if dimensions is not None else None,
        "frameId": desktop_layout_frame_id(layout),
        "desktopLayout": layout,
    }, pixbuf, bounds)
