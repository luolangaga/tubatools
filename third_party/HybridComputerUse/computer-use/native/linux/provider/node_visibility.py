"""Showing state of accessible nodes, corrected for items of open menus."""

from .foundation import (
    Atspi,
    node_role,
    provider_object_path,
    safe,
    state_contains,
    state_value,
)
from .node_geometry import extents


# Collapsed menus are not descended into. With the default 1200-node budget,
# a full LibreOffice menu bar (about 780 nodes) takes the whole budget and
# leaves no room for table cells. The rule applies only to menu roles: other
# intermediate containers (panel, scroll pane) often lack SHOWING on
# LibreOffice, and filtering them would empty the tree.
MENU_ROLES = {
    "menu",
    "menu bar",
    "menu item",
    "check menu item",
    "radio menu item",
    "popup menu",
}


# Menu roles that can hold a submenu. GAIL gives a submenu holder the role
# `menu`; other toolkits use `menu item` with children.
SUBMENU_HOLDER_ROLES = {"menu", "menu item"}


def menu_children_popped_up(menu, cache=None):
    """Return whether the items of ``menu`` are on screen in an open popup.

    GAIL (the GTK2 accessibility module that GIMP 2.10 uses) removes SHOWING
    from every item whose parent menu item lacks SELECTED, and it never sets
    SELECTED on a menu-bar menu, not even while its popup is mapped
    (gailmenuitem.c, gail_menu_item_ref_state_set). The items of an open GIMP
    menu are therefore VISIBLE with real extents but never SHOWING, and an
    item that is plainly on screen was reported as not showing.

    The menu shell's Selection interface still names the open menu: the menu
    bar selects "Edit" exactly while its popup is up, opened by a semantic
    action, a pointer click or a mnemonic, and an open menu selects the
    submenu that is popped up. Extents are no evidence, because GAIL keeps the
    last allocation after the popup closes. A menu's items are on screen when
    the menu is the selected child of a shell that is itself on screen.

    ``cache`` is shared across one read so that each shell is asked once.
    """
    if menu is None:
        return False
    cache = {} if cache is None else cache
    key = ("open", provider_object_path(menu) or id(menu))
    if key in cache:
        return cache[key]
    cache[key] = False
    result = False
    if node_role(menu) in SUBMENU_HOLDER_ROLES:
        shell = safe(getattr(menu, "get_parent", None))
        shell_role = node_role(shell) if shell is not None else ""
        if shell_role == "menu bar":
            shell_open = state_contains(shell, Atspi.StateType.SHOWING)
        elif shell_role in SUBMENU_HOLDER_ROLES:
            shell_open = menu_children_popped_up(shell, cache)
        else:
            shell_open = False
        result = shell_open and _shell_selects(shell, menu, cache)
    cache[key] = result
    return result


def _shell_selects(shell, child, cache):
    """Return whether the menu shell's Selection includes ``child``."""
    key = ("selected", provider_object_path(shell) or id(shell))
    selected = cache.get(key)
    if selected is None:
        selected = []
        selection = safe(getattr(shell, "get_selection_iface", None))
        if selection is not None:
            count = int(safe(selection.get_n_selected_children, 0) or 0)
            # A menu shell selects at most one item; the bound only protects
            # against a provider that misreports the count.
            for index in range(min(max(count, 0), 4)):
                item = safe(
                    lambda index=index: selection.get_selected_child(index))
                if item is not None:
                    selected.append(item)
        cache[key] = selected
    child_path = provider_object_path(child)
    return any(
        item is child or (child_path and provider_object_path(item) == child_path)
        for item in selected
    )


def popped_up_menu_item(node, role, visible, has_frame, cache):
    """Return whether a menu-role node lacking SHOWING is on screen.

    See menu_children_popped_up(). Only VISIBLE nodes with an on-screen frame
    qualify: a hidden item, or a separator GTK never allocated, stays hidden.
    The parent is read only after the cheap checks pass.
    """
    if role not in MENU_ROLES or not visible or not has_frame:
        return False
    parent = safe(getattr(node, "get_parent", None))
    return parent is not None and menu_children_popped_up(parent, cache)


def showing_state(node, cache=None):
    """Return SHOWING as true/false/unknown, correcting GAIL popup items."""
    showing = state_value(node, Atspi.StateType.SHOWING)
    if showing is False and popped_up_menu_item(
        node,
        node_role(node),
        state_contains(node, Atspi.StateType.VISIBLE),
        extents(node) is not None,
        cache,
    ):
        return True
    return showing
