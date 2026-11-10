"""Diagnostics derived from an accessibility snapshot."""

from .foundation import node_name, node_role


def snapshot_diagnostics(records):
    """Detect a snapshot that contains only top-level/container structure.

    This observation is intentionally diagnostic rather than causal.  A sparse tree
    can mean delayed, collapsed, pruned, toolkit-limited, or genuinely sparse UI;
    the snapshot alone cannot identify which mechanism produced it.
    """
    if not records:
        return []
    for record in records:
        role = str(record.get("controlType") or "").lower()
        if role in {"frame", "window", "dialog", "alert", "application"}:
            continue
        if record.get("name") or record.get("actions") or record.get("value"):
            return []
    return [
        "ACCESSIBILITY CONTENT UNCERTAIN: this snapshot contains no named, valued, or "
        "actionable accessibility node beyond top-level window/container records "
        "({} element(s) total). That observation does NOT prove the visible window is "
        "empty, that element-targeted actions are globally impossible, or that a "
        "renderer failed to export accessibility; the tree may be delayed, pruned, "
        "collapsed, toolkit-limited, or genuinely sparse. Inspect the full-desktop "
        "screenshot, open the relevant UI, and sample again before concluding that the "
        "content is unavailable.".format(len(records))
    ]


# Content-plane roles carry the application's content itself rather than the
# shell around it. Any role starting with "document" (document web, document
# frame, document text, document spreadsheet, document presentation, document
# email) also counts, matching the is_interactive_role convention.
CONTENT_PLANE_ROLES = {
    "canvas",
    "drawing area",
    "terminal",
    "image map",
    "tree table",
    "web view",
    "page",
}

# Container roles for embedded content: places where content may appear, not
# content themselves. An empty container only makes it more likely that the
# snapshot missed the content root; it does not prove the content is absent.
EMBEDDED_CONTAINER_ROLES = {
    "scroll pane",
    "viewport",
    "internal frame",
    "embedded",
    "layered pane",
    "split pane",
}


def is_content_plane_role(role):
    normalized = str(role or "").strip().lower()
    if normalized.startswith("document"):
        return True
    return normalized in CONTENT_PLANE_ROLES


def content_host_diagnostic(records):
    """Flag host-like structure without a distinctive content root.

    The result is deliberately uncertain: a sampled/pruned tree cannot prove that
    content is absent or attribute the observation to a renderer/toolkit.

    The criteria use role semantics only, never application or process names:
    embedded content hosts span browsers, Electron, PDF viewers and embedded
    webviews, so a name list could never be complete. A frame is flagged when

      1. its role is frame (a top-level application window; dialogs, menus
         and popups do not host a content plane);
      2. its subtree has no distinctive content-root role; and
      3. its subtree shows a structural sign of hosted content, either
         a. a tabbed document interface (page tab list or page tab): tabs
            belong to documents, so tabs without a document mean missing
            content; or
         b. an embedded-content container (scroll pane, viewport, embedded,
            internal frame, ...) that came back with no children.

    Condition 3 keeps the false-positive rate down: windows such as
    calculators or settings panels have no content plane, and conditions 1
    and 2 alone would flag them too.
    """
    if not records:
        return []

    def path_of(record):
        return tuple(record.get("runtimeId") or ())

    def role_of(record):
        return str(record.get("controlType") or "").strip().lower()

    def is_descendant(record, root_path):
        path = path_of(record)
        return len(path) > len(root_path) and path[:len(root_path)] == root_path

    for frame_record in records:
        if role_of(frame_record) != "frame":
            continue
        root_path = path_of(frame_record)
        subtree = [r for r in records if is_descendant(r, root_path)] if root_path else [
            r for r in records if r is not frame_record
        ]
        if not subtree:
            # A frame with no children is an empty shell, which
            # snapshot_diagnostics reports.
            continue
        # Only distinctive content roots can clear a frame-wide warning. Generic
        # text/table/list roles also occur in toolbars, sidebars, menus and status
        # panes, so their mere presence elsewhere is not evidence that the hosted
        # content plane is represented.
        if any(is_content_plane_role(role_of(r)) for r in subtree):
            continue
        evidence = None
        for record in subtree:
            role = role_of(record)
            if role in {"page tab list", "page tab"}:
                evidence = ("a tabbed document interface (role={}) with no document "
                            "behind any tab".format(role))
                break
        if evidence is None:
            for record in subtree:
                role = role_of(record)
                if role not in EMBEDDED_CONTAINER_ROLES:
                    continue
                child_path = path_of(record)
                if child_path and any(is_descendant(r, child_path) for r in records):
                    continue
                evidence = ("an embedded-content container (role={}) that came back "
                            "with nothing inside it".format(role))
                break
        if evidence is None:
            continue
        title = str(frame_record.get("name") or "").strip() or "(untitled)"
        return [
            "CONTENT PLANE UNCERTAIN: {!r} contains {}, but this sampled accessibility "
            "tree has no distinctive content root (for example document-*, canvas, "
            "web view, page, terminal, or tree table). Ordinary text/table/list nodes "
            "elsewhere in the frame are not enough to prove that the hosted content "
            "itself is represented. This is an uncertain diagnostic, not proof that "
            "the window is empty or that a renderer does not export accessibility; "
            "the content may be collapsed, lazy, pruned, delayed, toolkit-limited, or "
            "visually absent. Inspect the full-desktop screenshot, open/render the "
            "relevant content, and sample again. GUI coordinates come only from gui's "
            "full-desktop screenshot; this accessibility tree retains geometry "
            "internally and does not expose per-node rectangles.".format(title, evidence)
        ]
    return []


def is_dropdown_item(node, window):
    """Return whether ``node`` is an item inside a drop-down popup.

    A semantic action on such an item closes the popup without committing the
    value. In LibreOffice 7.3 drop-downs, for example, ``do_action`` on a
    ``table cell`` item returns True and closes the popup while the control
    keeps its old value; a coordinate click commits the new one.

    The failure cannot be detected afterwards: the popup disappears together
    with the item, so the tree always changes and a generic "nothing changed,
    retry" check never fires, and LibreOffice dialog combo boxes are
    placeholder nodes with no frame or text, so the value cannot be read back.
    The semantic route must therefore be avoided before the call.

    The check is deliberately narrow and matches only a ``table cell`` inside
    a popup window:

    - Menu items are excluded: ``do_action`` works on them (for example the
      Nautilus context-menu item "Rename…"), so matching them would block a
      working path.
    - Table cells in main windows are excluded; Calc cells use the same role
      but are not drop-down items and do not have this problem.
    """
    if node is None or window is None:
        return False
    if node_role(node) != "table cell":
        return False
    # On X11 a drop-down popup is a separate untitled top-level window with
    # role `window`; main windows are `frame` and dialogs `dialog` or `alert`,
    # and those have names.
    return node_role(window) == "window" and not node_name(window)
