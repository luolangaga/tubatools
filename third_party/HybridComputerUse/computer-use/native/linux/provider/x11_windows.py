"""Window enumeration, native window identity and window selection."""

import hashlib
import re
import subprocess
from .foundation import (
    AX_WINDOW_ROLES,
    TOP_LEVEL_CONTAINER_ROLES,
    Atspi,
    InventoryResult,
    MAX_SANE_EXTENT,
    OperationFailure,
    desktop,
    enumerate_children,
    frame,
    inventory_complete,
    inventory_errors,
    node_name,
    node_pid,
    node_role,
    process_generation,
    provider_bus_name,
    safe,
    state_contains,
)
from .node_geometry import extents
from .x11_window_inventory import (
    _x11_override_redirect_windows,
    _x11_window_inventory_cache,
    correlation_root_window_state,
    x11_window_inventory_for_pid,
    x11_window_type,
)
from .a11y_enable import request_full_trees
from .primitives import semantic_text_key


def frame_distance(first, second):
    if not first or not second:
        return None
    return (
        abs(int(first.get("x", 0)) - int(second.get("x", 0)))
        + abs(int(first.get("y", 0)) - int(second.get("y", 0)))
        + abs(int(first.get("width", 0)) - int(second.get("width", 0)))
        + abs(int(first.get("height", 0)) - int(second.get("height", 0)))
    )


def _closest_by_geometry(candidates, native_window):
    """Return the index of the one candidate clearly nearest the native frame."""
    ranked = []
    for index, window in candidates:
        distance = frame_distance(safe(lambda: extents(window)), native_window)
        if distance is not None:
            ranked.append((distance, index))
    ranked.sort()
    if len(ranked) == 1 or (
        len(ranked) > 1 and ranked[0][0] + 8 < ranked[1][0]
    ):
        return ranked[0][1]
    return None


def x11_window_id_for_accessible(app, window):
    """Uniquely correlate one AX top-level window with one managed X11 window."""
    candidates = list(x11_window_inventory_for_pid(node_pid(app)))
    if not candidates:
        return None
    title_key = semantic_text_key(node_name(window))
    if title_key:
        title_matches = [
            candidate for candidate in candidates
            if semantic_text_key(candidate.get("title")) == title_key
        ]
        if len(title_matches) == 1:
            return title_matches[0]["id"]
        if title_matches:
            candidates = title_matches
    bounds = safe(lambda: extents(window))
    if bounds is None and not (
        state_contains(window, Atspi.StateType.SHOWING)
        or state_contains(window, Atspi.StateType.VISIBLE)
    ):
        # A frame with no geometry that is neither showing nor visible has
        # no native window (Krita's Qt "Offscreen" frame). Without this, the
        # sole-candidate fallback below gave it the application's only X11
        # window, two AX windows then claimed one native window, and the
        # real one lost its handle and was listed "(not addressable)".
        return None
    root_state = correlation_root_window_state()
    active_id = root_state.get("activeWindow") if root_state else None
    if (
        state_contains(window, Atspi.StateType.ACTIVE)
        and active_id
        and sum(candidate["id"] == active_id for candidate in candidates) == 1
    ):
        return active_id
    ranked = []
    for candidate in candidates:
        distance = frame_distance(bounds, candidate)
        if distance is not None:
            ranked.append((distance, candidate["id"]))
    ranked.sort()
    if not ranked:
        return candidates[0]["id"] if len(candidates) == 1 else None
    if len(ranked) == 1:
        return ranked[0][1]
    if ranked[0][0] + 8 < ranked[1][0]:
        return ranked[0][1]
    return None


def titled_top_level_window_id(window, native):
    """Bind a top-level child with a non-window role by its exact title.

    Qt reports a top-level QWidget that lacks a window flag (Flameshot's
    "Configuration") as role "filler", yet the window manager manages it as
    an ordinary window. Such children get a handle only on strong evidence:
    a non-empty title equal to exactly one managed window of the process.
    Untitled panels and popups never match, so they stay unaddressed.
    """
    title_key = semantic_text_key(node_name(window))
    if not title_key:
        return None
    matches = [
        window_id for window_id, item in native.items()
        if semantic_text_key(item.get("title")) == title_key
    ]
    return matches[0] if len(matches) == 1 else None


def opaque_window_instance_handle_for_id(app, window_id):
    return window_handle_for_pid(node_pid(app), window_id)


def window_handle_for_pid(pid, window_id):
    """Return the strong w2 handle of one X11 window of process pid."""
    generation = process_generation(pid)
    if not window_id or not pid or not generation:
        return None
    material = "{}|{}|{}".format(
        pid, generation, window_id
    ).encode("utf-8", "replace")
    return "w2:" + hashlib.sha256(material).hexdigest()[:32]


def strong_window_instance_handle_inventory(app, windows=None):
    """Return only one-to-one AX-to-managed-window bindings."""
    if windows is None:
        windows = app_windows(app)
    if not inventory_complete(windows):
        return InventoryResult(
            complete=False,
            errors=inventory_errors(windows),
        )
    native_inventory = x11_window_inventory_for_pid(node_pid(app))
    if not native_inventory.complete:
        return InventoryResult(
            complete=False,
            errors=native_inventory.errors,
        )
    native = {
        item["id"]: item
        for item in native_inventory
        if item.get("id")
    }
    grouped = {}
    for index, window in windows:
        role = str(node_role(window) or "").strip().lower()
        if role not in AX_WINDOW_ROLES and role not in TOP_LEVEL_CONTAINER_ROLES:
            continue
        if role not in AX_WINDOW_ROLES:
            window_id = titled_top_level_window_id(window, native)
            if window_id:
                grouped.setdefault(window_id, []).append((index, window))
            continue
        window_id = x11_window_id_for_accessible(app, window)
        if window_id:
            grouped.setdefault(window_id, []).append((index, window))
    selected = {}
    for window_id, matches in grouped.items():
        if len(matches) == 1:
            selected[matches[0][0]] = opaque_window_instance_handle_for_id(
                app, window_id
            )
            continue
        native_title = semantic_text_key(
            (native.get(window_id) or {}).get("title")
        )
        exact = [
            (index, window)
            for index, window in matches
            if native_title
            and semantic_text_key(node_name(window)) == native_title
        ]
        if len(exact) == 1:
            selected[exact[0][0]] = opaque_window_instance_handle_for_id(
                app, window_id
            )
            continue
        # Several frames with the managed title: Electron keeps hidden
        # BrowserWindows that AT-SPI still lists (Joplin: a 1536x842 main
        # window and two hidden 800x600 ones, all titled "Joplin"). The
        # frame whose extents match the managed geometry is the shown one;
        # equally close frames stay without a handle.
        closest = _closest_by_geometry(exact or matches, native.get(window_id))
        if closest is not None:
            selected[closest] = opaque_window_instance_handle_for_id(
                app, window_id
            )
    # Untitled frames left without a managed window may be popups that the
    # window manager does not manage (Chromium menus); match them by
    # geometry to override-redirect windows of the same process.
    # Only applications with a titled window qualify: desktop panels own
    # untitled frames too, and scanning for them on every listing would
    # cost about 40 ms for nothing.
    claimed = set(selected)
    unmatched = [
        (index, window) for index, window in windows
        if index not in claimed
        and str(node_role(window) or "").strip().lower() in AX_WINDOW_ROLES
        and not semantic_text_key(node_name(window))
    ]
    has_titled_window = any(
        semantic_text_key(node_name(window)) for _, window in windows)
    if unmatched and has_titled_window:
        for popup in _x11_override_redirect_windows(node_pid(app)):
            closest = _closest_by_geometry(unmatched, popup)
            if closest is not None:
                selected[closest] = opaque_window_instance_handle_for_id(
                    app, popup["id"]
                )
                unmatched = [item for item in unmatched if item[0] != closest]
    return InventoryResult({
        index: handle
        for index, handle in selected.items()
        if str(handle or "").startswith("w2:")
    }.items(), complete=True)


def managed_window_title(app, window, strong_handles=None):
    """Return the X11 title of the managed window bound to ``window``, or "".

    Some toolkits leave a top-level AT-SPI frame unnamed while the window
    manager shows a title (GTK3 D-Feet: frame "" vs WM_NAME "d-feet"). The
    title is taken only for a window with a one-to-one strong binding, so it
    names exactly the window the model can address.
    """
    windows = app_windows(app)
    if strong_handles is None:
        strong_handles = strong_window_instance_handles(app, windows)
    index = next(
        (i for i, candidate in windows if candidate is window), None
    )
    if index is None or not strong_handles.get(index):
        return ""
    target = strong_handles[index]
    for item in x11_window_inventory_for_pid(node_pid(app)):
        if opaque_window_instance_handle_for_id(app, item.get("id")) == target:
            # Docks, panels and the desktop also carry X11 titles
            # ("xfce4-panel"); only application windows lend theirs.
            if x11_window_type(item.get("id")) not in TITLED_WINDOW_TYPES:
                return ""
            return str(item.get("title") or "").strip()
    return ""


TITLED_WINDOW_TYPES = {"NORMAL", "DIALOG"}


def accessible_window_title(app, window, strong_handles=None):
    """Return the window's AT-SPI name, or its managed X11 title when unnamed."""
    own = str(node_name(window) or "").strip()
    if own:
        return own
    return managed_window_title(app, window, strong_handles)


def strong_window_instance_handles(app, windows=None):
    return dict(strong_window_instance_handle_inventory(app, windows))


def unmapped_window_indices(app, windows, strong):
    """Return the indices of windows that cannot be on screen.

    A window without a strong native handle is listed as not addressable.
    When every managed window of the process is already bound to another
    window of the application, nothing on screen is left that it could be:
    it is a hidden window the toolkit keeps in its tree (Electron keeps
    unmapped BrowserWindows that AT-SPI reports SHOWING; Qt keeps a
    frameless "Offscreen" frame). Such entries are dropped from the listing.
    A process without any managed window (its windows report another pid)
    keeps every window, because the binding itself is what failed.
    """
    unbound = {index for index, _ in windows if not strong.get(index)}
    if not unbound or not strong:
        return set()
    native = x11_window_inventory_for_pid(node_pid(app))
    if not inventory_complete(native) or not list(native):
        return set()
    bound = set(strong.values())
    for item in native:
        if opaque_window_instance_handle_for_id(app, item.get("id")) not in bound:
            return set()
    return unbound


def window_instance_handle(app, window_index, window, strong_handles=None):
    """Return a stable native handle, or a scoped weak inventory identity.

    Without strong_handles, the app's handles are computed once per operation
    and shared (dropped with the native listing). An incomplete native or AX
    inventory raises observation_incomplete: callers compare the result with
    a stored w2: handle, and a weak fallback would read as "window gone".
    """
    if strong_handles is None:
        key = ("handles", provider_bus_name(app), node_pid(app))
        inventory = _x11_window_inventory_cache.get(key)
        if inventory is None:
            inventory = strong_window_instance_handle_inventory(app)
            if inventory.complete:
                _x11_window_inventory_cache[key] = inventory
        if not inventory.complete:
            raise OperationFailure(
                "observation_incomplete: the native window inventory could "
                "not be read completely; no input was emitted.",
                failure_code="observation_incomplete",
                failure_phase="precondition",
                input_events_emitted=False,
                failure_subject="window",
            )
        strong_handles = dict(inventory)
    strong = strong_handles.get(window_index)
    if strong:
        return strong
    role = str(node_role(window) or "window").strip().lower().replace(" ", "-")
    return "pid{}/win{}/{}".format(node_pid(app) or 0, window_index, role)




def relative_frame(node, window_bounds):
    bounds = extents(node)
    if bounds is None:
        return None
    if window_bounds is None:
        return bounds
    return frame(
        bounds["x"] - window_bounds["x"],
        bounds["y"] - window_bounds["y"],
        bounds["width"],
        bounds["height"],
    )


def window_bounds(app, window_index, window):
    """Read provider geometry, or the exact strongly bound native frame.

    Some accessibility bridges expose child geometry but no top-level
    extents. Native geometry is usable only after the normal one-to-one
    window binding succeeds; a title or an application's sole window is
    not sufficient here. The opaque handle includes process generation.
    """
    if window is None:
        return None
    bounds = extents(window)
    if bounds is not None:
        return bounds
    handle = window_instance_handle(app, window_index, window)
    if not str(handle or "").startswith("w2:"):
        return None
    inventory = x11_window_inventory_for_pid(node_pid(app))
    if not inventory_complete(inventory):
        raise OperationFailure(
            "observation_incomplete: the native window geometry could not "
            "be read completely; no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    matches = [
        item for item in inventory
        if opaque_window_instance_handle_for_id(app, item.get("id")) == handle
    ]
    if len(matches) != 1:
        return None
    item = matches[0]
    if (
        any(not isinstance(item.get(key), (int, float))
            for key in ("x", "y", "width", "height"))
        or not 0 < item["width"] <= MAX_SANE_EXTENT
        or not 0 < item["height"] <= MAX_SANE_EXTENT
        or abs(item["x"]) > MAX_SANE_EXTENT
        or abs(item["y"]) > MAX_SANE_EXTENT
    ):
        return None
    return frame(item["x"], item["y"], item["width"], item["height"])


def iter_apps():
    try:
        root = desktop()
    except Exception as error:
        return InventoryResult(
            complete=False,
            errors=["desktop lookup failed: {}".format(type(error).__name__)],
        )
    if root is None:
        return InventoryResult(
            complete=False,
            errors=["desktop lookup returned no root"],
        )
    children = enumerate_children(root, "desktop")
    request_full_trees(app for _, app in children)
    return InventoryResult(
        [app for _, app in children],
        complete=children.complete,
        errors=children.errors,
    )


def app_windows(app):
    windows = []
    children = enumerate_children(app, "application")
    complete = children.complete
    errors = list(children.errors)
    for index, child in children:
        # Read the role directly rather than through node_role(), which turns
        # a provider failure into an empty role; a failed read must mark the
        # inventory incomplete.
        try:
            role = str(child.get_role_name() or "").lower()
        except Exception as error:
            complete = False
            errors.append(
                "application child {} role failed: {}".format(
                    index, type(error).__name__
                )
            )
            continue
        bounds = extents(child)
        if role in {"frame", "window", "dialog", "alert"} or bounds is not None:
            windows.append((index, child))
    return InventoryResult(windows, complete=complete, errors=errors)


def x11_active_client_size():
    """Return the X11 active window's size without its invisible shadow.

    Used to arbitrate when AT-SPI states cannot tell windows apart. Returns
    ``(width, height)``, or None when unavailable.

    Size is the criterion because position and title are unreliable: GTK4
    reports accessibility extents with a (0,0) origin that does not match
    screen coordinates, and the secondary dialog in front may have no title.
    The X11 size includes the invisible shadow border drawn by GTK; after
    subtracting ``_GTK_FRAME_EXTENTS`` it matches the accessibility size
    exactly.
    """
    try:
        active = subprocess.run(["xdotool", "getactivewindow"],
                                capture_output=True, text=True, timeout=3)
        if active.returncode != 0 or not active.stdout.strip():
            return None
        window_id = active.stdout.strip()
        info = subprocess.run(["xwininfo", "-id", window_id],
                              capture_output=True, text=True, timeout=3).stdout
        width = re.search(r"Width:\s+(\d+)", info)
        height = re.search(r"Height:\s+(\d+)", info)
        if not (width and height):
            return None
        size = [int(width.group(1)), int(height.group(1))]
        extents = subprocess.run(["xprop", "-id", window_id, "_GTK_FRAME_EXTENTS"],
                                 capture_output=True, text=True, timeout=3).stdout
        numbers = [int(n) for n in re.findall(r"\d+", extents)]
        if len(numbers) >= 4:
            left, right, top, bottom = numbers[:4]
            size[0] -= left + right
            size[1] -= top + bottom
        return tuple(size)
    except Exception:
        return None


def x11_preferred_window(candidates):
    """Return the one candidate that X11 considers frontmost, or None.

    The GNOME portal file dialog and its secondary confirmation ("file exists,
    replace?") are two windows of the same application with identical
    AT-SPI states: MODAL and VISIBLE, neither ACTIVE nor SHOWING. Taking the
    first modal window always yields the file chooser while the untitled
    confirmation in front holds focus, which stalls nested save and overwrite
    flows. The confirmation is present in the accessibility tree; only the
    window choice is wrong.

    Without a unique match this returns None and the regular order applies:
    an ambiguous arbiter must not force a decision.
    """
    target = x11_active_client_size()
    if target is None:
        return None
    matched = []
    for index, window in candidates:
        frame = safe(lambda: extents(window))
        if not frame:
            continue
        if (abs(int(frame["width"]) - target[0]) <= 2
                and abs(int(frame["height"]) - target[1]) <= 2):
            matched.append((index, window))
    return matched[0] if len(matched) == 1 else None


def window_candidate_summary(index, window):
    bounds = safe(lambda: extents(window))
    return {
        "index": index,
        "role": node_role(window),
        "title": node_name(window) or "",
        "active": state_contains(window, Atspi.StateType.ACTIVE),
        "showing": state_contains(window, Atspi.StateType.SHOWING),
        "visible": state_contains(window, Atspi.StateType.VISIBLE),
        "modal": state_contains(window, Atspi.StateType.MODAL),
        "bounds": bounds,
    }


def select_main_window(app):
    """Return the selected window plus the evidence used to choose it."""
    windows = app_windows(app)
    if not windows:
        raise RuntimeError(
            "No top-level AT-SPI window is available for " + node_name(app)
        )

    candidates = [window_candidate_summary(index, window)
                  for index, window in windows]

    def selected(index, window, reason):
        for candidate in candidates:
            candidate["selected"] = candidate["index"] == index
        return index, window, {
            "reason": reason,
            "candidateCount": len(candidates),
            "candidates": candidates,
        }

    modal = [
        (index, window)
        for index, window in windows
        if state_contains(window, Atspi.StateType.MODAL)
        and state_contains(window, Atspi.StateType.SHOWING)
    ]
    for index, window in modal:
        if state_contains(window, Atspi.StateType.ACTIVE):
            return selected(index, window, "active_modal")
    if len(windows) > 1:
        preferred = x11_preferred_window(windows)
        if preferred is not None:
            return selected(
                preferred[0], preferred[1],
                "x11_active_client_size_unique_match")
    if modal:
        return selected(modal[0][0], modal[0][1], "showing_modal")
    for index, window in windows:
        if state_contains(window, Atspi.StateType.ACTIVE):
            return selected(index, window, "active")
    for index, window in windows:
        if state_contains(window, Atspi.StateType.SHOWING):
            return selected(index, window, "showing")
    return selected(windows[0][0], windows[0][1], "first_available")


def select_window_by_handle(app, target_window_handle):
    """Resolve one exact logical window without foreground fallback."""
    target = str(target_window_handle or "").strip()
    if not target:
        return select_main_window(app)
    windows = app_windows(app)
    if not windows.complete:
        raise OperationFailure(
            "observation_incomplete: the application window inventory could not "
            "be read completely.",
            failure_code="observation_incomplete",
            failure_phase="observation",
            input_events_emitted=False,
            failure_subject="window",
        )
    strong_inventory = strong_window_instance_handle_inventory(app, windows)
    if not strong_inventory.complete:
        raise OperationFailure(
            "observation_incomplete: the native window inventory could not "
            "be read completely.",
            failure_code="observation_incomplete",
            failure_phase="observation",
            input_events_emitted=False,
            failure_subject="window",
        )
    strong_handles = dict(strong_inventory)
    matched = [
        (index, window)
        for index, window in windows
        if strong_handles.get(index) == target
    ]
    if not matched:
        raise OperationFailure(
            "stale_window_ref: the referenced logical window no longer exists.",
            failure_code="stale_window_ref",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    if len(matched) != 1:
        raise OperationFailure(
            "observation_incomplete: the logical window identity is not unique.",
            failure_code="observation_incomplete",
            failure_phase="observation",
            input_events_emitted=False,
            failure_subject="window",
        )
    index, window = matched[0]
    return index, window, {
        "reason": "exact_logical_window",
        "candidateCount": len(windows),
        "candidates": [window_candidate_summary(i, item) for i, item in windows],
    }


def main_window(app):
    """Return ``(index, window)`` for the top-level window to operate on.

    Order: an active modal window; with several windows, a unique X11
    active-window size match (see x11_preferred_window()); a showing modal
    window; ACTIVE; SHOWING; and finally the first window.

    Modal windows rank above ACTIVE. AT-SPI has no equivalent of macOS's
    kAXFocusedWindowAttribute, so the window must be inferred from states,
    and applications such as LibreOffice report ACTIVE on neither frames nor
    dialogs. Selection would then fall through to SHOWING, where a modal
    dialog later in child order loses to the main window and the caller never
    sees the dialog. MODAL is the strongest signal: by definition it blocks
    interaction with the rest of the application.

    Several modal windows can coexist: a LibreOffice combo-box drop-down is a
    separate top-level window with MODAL, SHOWING and ACTIVE, above a dialog
    that is only MODAL and SHOWING. ACTIVE therefore breaks the tie among
    modal candidates so the drop-down options stay visible.
    """
    index, window, _selection = select_main_window(app)
    return index, window


def window_identity_set(app):
    """Return the identities of the application's current top-level windows.

    Used to detect whether an action opened or closed a window. An identity
    is ``(index, role, title)``. A newly mapped window may receive its real
    title later and so change identity; that does not affect the check,
    because it still counts as newly appeared and only the change matters.
    """
    identity = set()
    for index, window in app_windows(app):
        identity.add((index, node_role(window), node_name(window)))
    return identity
