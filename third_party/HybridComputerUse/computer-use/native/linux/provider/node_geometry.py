"""Screen frames of accessible nodes, corrected to X root pixels."""

from .foundation import (
    AX_WINDOW_ROLES,
    Atspi,
    MAX_SANE_EXTENT,
    frame,
    node_role,
    provider_bus_name,
    provider_object_path,
    safe,
)


def sane_rect_frame(rect):
    if (
        rect is None
        or rect.width <= 0
        or rect.height <= 0
        or rect.width > MAX_SANE_EXTENT
        or rect.height > MAX_SANE_EXTENT
    ):
        return None
    # Unrendered GTK controls report origins near INT_MIN with plausible sizes
    # (often 1x1), so a size check alone lets coordinates such as -2147483648
    # into the element tree, where coordinate clicks and drags would land at
    # meaningless positions.
    if abs(rect.x) > MAX_SANE_EXTENT or abs(rect.y) > MAX_SANE_EXTENT:
        return None
    return frame(rect.x, rect.y, rect.width, rect.height)


def raw_extents(node):
    component = safe(node.get_component_iface)
    if component is None:
        return None
    rect = safe(lambda: Atspi.Component.get_extents(component, Atspi.CoordType.SCREEN))
    return sane_rect_frame(rect)


def embedded_host_panels(node, parent):
    """Find an embedding edge omitted by the embedded root's Parent reply."""
    if parent is None:
        return None, None
    if node_role(parent) == "panel":
        return parent, safe(getattr(parent, "get_parent", None))
    if node_role(parent) not in AX_WINDOW_ROLES:
        return None, None
    target = (provider_bus_name(node), provider_object_path(node))
    if not target[1]:
        return None, None
    host = embedded_host_search([parent], target)
    if host is None and safe(getattr(parent, "get_parent", None)) is None:
        # Some bridges answer the root pane's Parent with a frame twin that
        # is not in the application's window list. Search the windows the
        # application does list for the panels that hold this root pane.
        host = embedded_host_search(attached_windows(parent), target)
    return host or (None, None)


def embedded_host_search(windows, target):
    pending = [(window, []) for window in windows]
    matches = []
    visited = 0
    while pending and visited < 64:
        current, ancestors = pending.pop()
        visited += 1
        count = safe(current.get_child_count)
        if count is None or count > 32:
            return None
        for index in range(count):
            child = safe(lambda: current.get_child_at_index(index))
            if child is None:
                return None
            if (provider_bus_name(child), provider_object_path(child)) == target:
                if ancestors:
                    matches.append((current, ancestors[-1]))
            elif len(ancestors) < 3 and node_role(child) == "panel":
                pending.append((child, ancestors + [current]))
    return matches[0] if len(matches) == 1 and not pending else None


def attached_windows(detached, limit=32):
    application = safe(getattr(detached, "get_application", None))
    count = safe(getattr(application, "get_child_count", None))
    if count is None or count > limit:
        return []
    windows = []
    for index in range(count):
        window = safe(lambda: application.get_child_at_index(index))
        if window is not None and node_role(window) in AX_WINDOW_ROLES:
            windows.append(window)
    return windows


IDENTITY_TRANSFORM = (1.0, 0, 0)
# Roles whose subtree a browser may report in device pixels while the
# browser's own views around it use logical pixels.
WEB_DOCUMENT_ROLES = {"document web", "document frame"}


def screen_transform(node, cache=None, remaining=32, role=None, bounds=None):
    """Return (scale, dx, dy) mapping a node's AT-SPI extents to root pixels.

    Root pixels are ((raw + d) * scale). The scale comes from the node's
    top-level window (window_scale); ``bounds`` are the node's raw extents
    when the caller has already read them. Chromium reports web content in device
    pixels inside browser views in logical pixels: a web document whose
    frame is exactly its parent's frame times the scale starts a subtree
    with scale 1.

    The offset corrects an embedded tree anchored to its host's outer
    container: some bridges put a root pane at the outer GTK panel's origin
    although its equally sized host panel starts below a menu strip. The
    host and its outer panel describe the same rectangle except for that
    strip. Derive the translation from those three rectangles, never an app
    name or a fixed decoration size. Cache only within one snapshot or hit
    test.
    """
    if node is None or remaining <= 0:
        return IDENTITY_TRANSFORM
    cache = {} if cache is None else cache
    key = (provider_bus_name(node), provider_object_path(node))
    if not key[1]:
        key = id(node)
    if key in cache:
        return cache[key]
    cache[key] = IDENTITY_TRANSFORM  # Bound malformed parent cycles.
    role = node_role(node) if role is None else role
    if role == "application":
        return cache[key]
    parent = safe(getattr(node, "get_parent", None))
    # Qt may report a top-level window as a filler or panel, so any child of
    # the application is a top-level window here.
    top_level = parent is not None and node_role(parent) == "application"
    if role in AX_WINDOW_ROLES or top_level:
        if top_level or parent is None:
            from .window_scale import window_scale
            transform = (window_scale(node, bounds), 0, 0)
        else:
            transform = (screen_transform(parent, cache, remaining - 1)[0], 0, 0)
        cache[key] = transform
        return transform
    scale, dx, dy = screen_transform(parent, cache, remaining - 1)
    if role == "root pane":
        host_node, outer = embedded_host_panels(node, parent)
        if (host_node is not None and node_role(host_node) == "panel"
                and outer is not None and node_role(outer) == "panel"):
            child_frame, host, container = (
                raw_extents(node), raw_extents(host_node), raw_extents(outer))
            if child_frame and host and container:
                inset_x = host["x"] - container["x"]
                inset_y = host["y"] - container["y"]
                same_size = all(child_frame[k] == host[k]
                                for k in ("width", "height"))
                outer_origin = all(child_frame[k] == container[k]
                                   for k in ("x", "y"))
                inset = (inset_x >= 0 and inset_y >= 0
                         and (inset_x > 0 or inset_y > 0)
                         and host["x"] + host["width"] ==
                         container["x"] + container["width"]
                         and host["y"] + host["height"] ==
                         container["y"] + container["height"])
                if same_size and outer_origin and inset:
                    dx, dy = dx + inset_x, dy + inset_y
    elif scale != 1 and role in WEB_DOCUMENT_ROLES:
        own, outer = raw_extents(node), raw_extents(parent)
        if own and outer and all(
                abs(outer[k] * scale - own[k]) <= scale + 1
                for k in ("x", "y", "width", "height")):
            scale, dx, dy = IDENTITY_TRANSFORM
    cache[key] = (scale, dx, dy)
    return cache[key]


def corrected_screen_frame(node, bounds, cache=None, role=None):
    if bounds is None:
        return None
    scale, dx, dy = screen_transform(node, cache, role=role, bounds=bounds)
    return frame((bounds["x"] + dx) * scale, (bounds["y"] + dy) * scale,
                 bounds["width"] * scale, bounds["height"] * scale)


def extents(node):
    return corrected_screen_frame(node, raw_extents(node))
