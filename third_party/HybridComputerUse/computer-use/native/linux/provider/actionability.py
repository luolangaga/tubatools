"""Action choice, geometry stability and coordinate-delivery checks."""

import math
import time
from .foundation import (
    Atspi,
    CLICK_COVERED_ACTIONS,
    NON_SELF_ACTIONS,
    OperationFailure,
    frame,
    node_name,
    node_role,
    normalized_action,
    safe,
)
from .node_geometry import screen_transform, extents

from .x11_windows import (
    relative_frame,
    x11_window_id_for_accessible,
)
from .desktop_layout import (
    desktop_bounds,
    desktop_layout_state,
    frame_intersection,
)

from .gui_capture import (
    gui_model_dimensions,
)
from . import action_evidence
from .input_primitives import require_window_focus

from .element_resolver import (
    same_frame,
)


def preferred_action_index(node):
    # Use CLICK_COVERED_ACTIONS directly rather than a local copy: the same
    # set decides which action a click invokes and which actions are hidden
    # as redundant, so two copies would drift apart.
    preferred_exact = CLICK_COVERED_ACTIONS
    if safe(node.get_action_iface) is None:
        return None
    count = int(safe(node.get_n_actions, 0) or 0)
    fallback = None
    for index in range(count):
        name = str(safe(lambda i=index: node.get_action_name(i), "") or "")
        description = str(safe(lambda i=index: node.get_action_description(i), "") or "")
        lower = (name or description).lower()
        if lower in preferred_exact:
            return index
        if normalized_action(lower) in NON_SELF_ACTIONS:
            continue
        if fallback is None and (
            "activate" in lower or "click" in lower or "press" in lower
        ):
            fallback = index
    return fallback


ACTION_REPLY_TIMEOUT = "reply_timeout"


def invoke_action(node, index):
    """Invoke one AT-SPI action; return True, False or ACTION_REPLY_TIMEOUT.

    A DoAction call that times out without a reply was still delivered: the
    toolkit runs the action inside the handler and replies when it returns.
    Qt does so for a menu item or button that opens a modal dialog
    (QDialog::exec runs the dialog's event loop before DoAction returns),
    so Okular's "Go to Page..." reply arrived only after the dialog closed.
    Treating that as "no action" made the click fall back to a coordinate
    click that the new dialog then blocked, and the result said no input
    was emitted while the dialog was open.
    """
    if index is None:
        return False
    try:
        return bool(node.do_action(int(index)))
    except Exception as error:
        if is_reply_timeout(error):
            return ACTION_REPLY_TIMEOUT
        return False


def is_reply_timeout(error):
    """Return whether ``error`` is libatspi's D-Bus no-reply timeout."""
    return "did not receive a reply" in str(
        getattr(error, "message", "") or error).lower()


def do_action_by_index(node, index):
    """Invoke the action; return True, False or ACTION_REPLY_TIMEOUT (truthy)."""
    return invoke_action(node, index)


def application_toolkit(node):
    """Return the toolkit name of the node's application, lowercased.

    Qt reports its toolkit only on the application object; its windows and
    controls return an empty toolkit name.
    """
    app = safe(getattr(node, "get_application", None))
    name = safe(getattr(app, "get_toolkit_name", None), "")
    return str(name or "").strip().lower()


def action_only_sets_state(node, index):
    """Return whether the click entry changes a state flag but not the app.

    Qt's AT-SPI bridge exposes a single `Toggle` action on checkable buttons
    (check box, radio button, checkable tool button) and on item-view cells.
    For a button it calls QAbstractButton::toggle(), which flips the checked
    state and emits toggled() but not clicked(); for a cell it does not
    select or activate the row. An application that listens for clicked()
    or for the current row never hears about it: VLC's preferences category
    buttons turn checked while the page stays put, and its Simple/All radio
    and its preferences tree do not switch either, although each control
    reports the action as done. A real pointer click emits every signal the
    widget emits for a user, so these elements need one.

    Only Qt's `toggle` qualifies. GTK's `click` and `toggle` emit the
    widget's own activation signals, and GTK2 menu `click` opens the menu or
    runs the command (both verified on GIMP 2.10), so they stay semantic.
    """
    # Check the toolkit first: it needs no action-table read, which the
    # LibreOffice ATK bridge handles badly (see node_actions()).
    if index is None or application_toolkit(node) != "qt":
        return False
    name = str(safe(lambda: node.get_action_name(int(index)), "") or "")
    return normalized_action(name) == "toggle"


def desktop_frame_point(x, y, native_bounds=None):
    """Convert an X root point to pixels of the delivered full-desktop frame.

    Notes about AX input synthesis state their point in the coordinate space
    of the GUI frame, which AX full/screenshot images share, so that a point
    the model reads can be reused as GUI x/y. This is the inverse of
    screen_absolute_point.
    """
    native_bounds = native_bounds or desktop_bounds()
    if native_bounds is None:
        return None
    dimensions = gui_model_dimensions(native_bounds)
    scale_x = float(native_bounds["width"]) / dimensions["width"]
    scale_y = float(native_bounds["height"]) / dimensions["height"]
    frame_x = (x - native_bounds["x"] + 0.5) / scale_x - 0.5
    frame_y = (y - native_bounds["y"] + 0.5) / scale_y - 0.5
    return (
        min(dimensions["width"] - 1, max(0.0, frame_x)),
        min(dimensions["height"] - 1, max(0.0, frame_y)),
    )


def frame_point_text(x, y):
    """`at (X, Y) in full-desktop frame pixels`, or a plain statement when
    the desktop bounds are unavailable."""
    point = desktop_frame_point(x, y)
    if point is None:
        return "at an unreported point"
    return "at ({:.0f}, {:.0f}) in full-desktop frame pixels".format(*point)


# An element is stable once two consecutive samples report the same bounding
# box.
STABLE_SAMPLE_SECONDS = 0.08

# Maximum wait for an element to stop moving. One second covers most one-off
# relayouts without letting a permanently moving element (progress bar,
# animation) delay a click by seconds.
STABLE_TIMEOUT_SECONDS = 1.0


def wait_until_stable(element, window_bounds, first=None):
    """Wait for an element to stop moving before acting on it.

    Returns ``(final_frame, samples, settled)``. Pass ``first`` when the frame
    has already been sampled, to avoid a redundant read.

    Only stability is checked, not enablement.
    Enablement is unreliable over AT-SPI: Nautilus file icons expose neither ENABLED nor
    SENSITIVE (only SHOWING, VISIBLE and FOCUSABLE) yet offer working "open"
    and "menu" actions, so an ENABLED gate would reject usable targets. A
    "receives events" hit test is skipped for the same reason: it would block
    about half of valid LibreOffice clicks.
    """
    if element is None:
        return None, 0, True
    previous = first if first is not None else safe(
        lambda: relative_frame(element, window_bounds)
    )
    samples = 1
    if previous is None:
        return None, samples, False
    deadline = time.monotonic() + STABLE_TIMEOUT_SECONDS
    while True:
        time.sleep(STABLE_SAMPLE_SECONDS)
        current = safe(lambda: relative_frame(element, window_bounds))
        samples += 1
        if current is None:
            return None, samples, False
        if same_frame(previous, current):
            return current, samples, True
        previous = current
        if time.monotonic() >= deadline:
            # The sampler reports instability. Coordinate routes fail closed
            # in require_stable_coordinate_geometry(); semantic provider
            # actions do not depend on geometry.
            return current, samples, False


def current_geometry(element, element_record, window_bounds):
    """Replace the record's frame with the live node's current frame.

    The target is re-resolved at action time. The
    live node is already resolved, so coordinates must come from it rather
    than from the snapshot record cached by the Go side; otherwise semantic
    actions and synthesized input could target different positions within one
    call. Desktop UIs have no page-load event, and animation, asynchronous
    loading or another process can move an element between tool calls.

    Returns ``(record, movement_note)``. If the live frame is unavailable, the
    old record is kept for diagnostics only; coordinate fallbacks reject it in
    require_stable_coordinate_geometry() and never emit input at stale
    coordinates.
    """
    if element is None or not element_record:
        return element_record, None
    live = safe(lambda: relative_frame(element, window_bounds))
    if live is None:
        return element_record, None
    old = element_record.get("frame")
    updated = dict(element_record)
    updated["frame"] = live
    if not old:
        return updated, None
    dx = live["x"] - old["x"]
    dy = live["y"] - old["y"]
    if abs(dx) <= 2 and abs(dy) <= 2:
        return updated, None
    return updated, (
        "The element MOVED since the snapshot. This action used its CURRENT "
        "position, not the one in the tree you read. Any coordinate you took "
        "from an earlier image is stale."
    )


def require_stable_coordinate_geometry(element, element_record, window_bounds):
    """Return a fresh stable frame for a coordinate fallback or fail closed."""
    action_evidence.update(geometry_sampled=element is not None)
    if element is None or not element_record:
        return element_record, 0
    first = safe(lambda: relative_frame(element, window_bounds))
    if first is None:
        action_evidence.update(geometry_stable=None)
        raise OperationFailure(
            "observation_incomplete: live target geometry is unavailable; "
            "no coordinate input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="element",
        )
    live, samples, settled = wait_until_stable(
        element, window_bounds, first=first
    )
    if live is None:
        action_evidence.update(geometry_stable=None)
        raise OperationFailure(
            "observation_incomplete: live target geometry could not be sampled; "
            "no coordinate input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="element",
        )
    if not settled:
        action_evidence.update(geometry_stable=False)
        raise OperationFailure(
            "target_unstable: target geometry did not stabilize before the "
            "coordinate-action deadline; no input was emitted.",
            failure_code="target_unstable",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    updated = dict(element_record)
    updated["frame"] = live
    action_evidence.update(geometry_stable=True)
    return updated, samples


def coordinate_point_owner(window, x, y):
    """Return true/false/unknown for exact native ownership of a screen point."""
    app = safe(lambda: Atspi.Accessible.get_application(window))
    target_id = (
        x11_window_id_for_accessible(app, window) if app is not None else None
    )
    layout = desktop_layout_state(desktop_bounds())
    if not target_id or layout is None:
        return None
    if layout.get("activeWindow") != target_id:
        return False
    containing = []
    for candidate in layout.get("windows", []):
        candidate_frame = frame(
            candidate.get("x", 0) + layout["desktop"].get("screenX", 0),
            candidate.get("y", 0) + layout["desktop"].get("screenY", 0),
            candidate.get("width", 0),
            candidate.get("height", 0),
        )
        if (
            candidate_frame["width"] > 0
            and candidate_frame["height"] > 0
            and candidate_frame["x"] <= x <
                candidate_frame["x"] + candidate_frame["width"]
            and candidate_frame["y"] <= y <
                candidate_frame["y"] + candidate_frame["height"]
        ):
            containing.append(candidate.get("id"))
    if not containing:
        return False
    return containing[-1] == target_id


def _deepest_accessible_at(window, x, y, max_depth=32):
    """Return the deepest accessible under a screen point, or None."""
    node = window
    hit = None
    origins = {}
    for _ in range(max_depth):
        component = safe(node.get_component_iface)
        if component is None:
            break
        scale, dx, dy = screen_transform(node, origins)
        child = safe(lambda: Atspi.Component.get_accessible_at_point(
            component, int(round(x / scale - dx)), int(round(y / scale - dy)),
            Atspi.CoordType.SCREEN))
        if child is None or _same_object(child, node):
            break
        hit = node = child
    return hit


def _same_object(first, second):
    if first is second:
        return True
    first_path = str(safe(lambda: first.path, "") or "")
    return bool(first_path) and first_path == str(
        safe(lambda: second.path, "") or "")


def _same_frame_on_screen(first, second):
    first_frame, second_frame = extents(first), extents(second)
    return (
        first_frame is not None
        and second_frame is not None
        and all(first_frame[key] == second_frame[key]
                for key in ("x", "y", "width", "height"))
    )


def point_hits_element(window, element, x, y):
    """Return whether a pointer event at (x, y) would reach ``element``.

    The point owner check covers top-level windows only; this hit test
    checks the element itself, so an overlapping popup, a scrolled-away row
    or a stale frame stops the click. The deepest hit may be the element, a
    descendant (a label inside a button), or an object with the element's
    role, name and screen frame: Qt creates a new accessible object for an
    item-view cell on every query, so the cell a hit test returns is never
    the same object as the one resolved earlier.
    """
    hit = _deepest_accessible_at(window, x, y)
    if hit is None:
        return False
    node = hit
    for _ in range(8):
        if _same_object(node, element):
            return True
        node = safe(node.get_parent)
        if node is None:
            break
    return (
        node_role(hit) == node_role(element)
        and node_name(hit) == node_name(element)
        and _same_frame_on_screen(hit, element)
    )


def require_coordinate_delivery(window, x, y, what):
    require_window_focus(window, what)
    action_evidence.update(window_focus_verified=True)
    owner = coordinate_point_owner(window, x, y)
    if owner is True:
        action_evidence.update(point_owner_verified=True)
        return
    action_evidence.update(point_owner_verified=owner)
    reason = "unknown" if owner is None else "different_window"
    raise OperationFailure(
        "coordinate_target_unverified: point owner is {}; no input was "
        "emitted.".format(reason),
        failure_code="coordinate_target_unverified",
        failure_phase="precondition",
        input_events_emitted=False,
    )


def screen_point(window_bounds, element=None, x=None, y=None):
    if element is not None:
        f = element.get("frame")
        if f is not None and window_bounds is not None:
            element_on_screen = frame(
                window_bounds["x"] + f["x"],
                window_bounds["y"] + f["y"],
                f["width"],
                f["height"],
            )
            visible = frame_intersection(element_on_screen, window_bounds)
            desktop = desktop_bounds()
            if desktop is None:
                raise RuntimeError(
                    "full desktop bounds are unavailable; refusing accessibility "
                    "coordinate fallback because the element's visible screen region "
                    "cannot be validated"
                )
            visible = frame_intersection(visible, desktop)
            if visible is None:
                raise RuntimeError(
                    "element_offscreen: the element frame does not intersect the target "
                    "window and full desktop; scroll it into view before coordinate input"
                )
            return (
                visible["x"] + visible["width"] / 2,
                visible["y"] + visible["height"] / 2,
            )
    if x is None or y is None or window_bounds is None:
        raise RuntimeError("coordinate action requires window bounds and x/y")
    # Window-relative x/y input is used only by AX coordinate fallbacks; GUI
    # input never takes this path.
    left, top = window_bounds["x"], window_bounds["y"]
    right = left + max(window_bounds["width"] - 1, 0)
    bottom = top + max(window_bounds["height"] - 1, 0)
    return (
        min(max(left + float(x), left), right),
        min(max(top + float(y), top), bottom),
    )


def screen_absolute_point(x, y, bounds=None, native_bounds=None):
    """Translate one delivered-image coordinate into the X root coordinate.

    The public GUI coordinate space is the returned full-desktop image, whose
    top-left pixel is always (0,0). The delivered image may be smaller than the
    native virtual desktop, so the runtime scales against the two exact extents
    and then adds the native desktop origin. It never adds an application-window
    offset and never clamps the point.
    """
    if x is None or y is None:
        raise RuntimeError("GUI coordinate action requires full-desktop image x/y")
    try:
        x = float(x)
        y = float(y)
    except (TypeError, ValueError):
        raise RuntimeError("GUI coordinates must be finite numbers")
    if not math.isfinite(x) or not math.isfinite(y):
        raise RuntimeError("GUI coordinates must be finite numbers")
    native_bounds = (
        desktop_bounds()
        if native_bounds is None and bounds is None
        else native_bounds or bounds
    )
    if native_bounds is None:
        raise RuntimeError(
            "full desktop bounds are unavailable; refusing screen-image input "
            "because gui cannot validate where it would be delivered"
        )
    if bounds is None:
        dimensions = gui_model_dimensions(native_bounds)
        bounds = frame(0, 0, dimensions["width"], dimensions["height"])
    right = max(bounds["width"] - 1, 0)
    bottom = max(bounds["height"] - 1, 0)
    if x < 0 or x > right or y < 0 or y > bottom:
        raise RuntimeError(
            "screen-image point ({:.0f}, {:.0f}) is outside the full desktop "
            "image bounds {{0,0,{:.0f},{:.0f}}}; read coordinates from "
            "gui".format(
                x, y, bounds["width"], bounds["height"])
        )
    scale_x = float(native_bounds["width"]) / float(bounds["width"])
    scale_y = float(native_bounds["height"]) / float(bounds["height"])
    native_x = native_bounds["x"] + min(
        float(native_bounds["width"]) - 1,
        max(0.0, (x + 0.5) * scale_x - 0.5),
    )
    native_y = native_bounds["y"] + min(
        float(native_bounds["height"]) - 1,
        max(0.0, (y + 0.5) * scale_y - 0.5),
    )
    return native_x, native_y
