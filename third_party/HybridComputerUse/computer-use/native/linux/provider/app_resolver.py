"""Linux AT-SPI application discovery and inventory."""

from .foundation import (
    Atspi,
    OperationFailure,
    inventory_complete,
    inventory_errors,
    node_name,
    node_pid,
    node_role,
    process_generation,
    provider_bus_name,
    safe,
    provider_object_path,
    state_contains,
)
from .atspi_inventory import (
    matches_app_bootstrap_query,
    resolve_app,
    resolve_app_identity,
)
from .snapshot import snapshot_digest, utc_timestamp
from .a11y_enable import invisible_window_notes, untracked_window_notes
from .native_windows import native_app_entries, native_only_windows, record_window_coverage
from .x11_windows import (
    accessible_window_title,
    app_windows,
    iter_apps,
    opaque_window_instance_handle_for_id,
    strong_window_instance_handles,
    window_instance_handle,
    unmapped_window_indices,
)
from .x11_window_inventory import (
    _x11_managed_windows,
    correlation_root_window_state,
)
from .window_diagnostics import is_infrastructure_window



def app_top_level_windows(app, windows, active_handle):
    """List an application's top-level windows for window addressing.

    Only windows with a strong native handle are addressable: the handle is
    what later activate/close/keyboard calls verify, so a window without one
    is listed with an empty handle and never receives a ref. The active flag
    comes from the window manager's _NET_ACTIVE_WINDOW when it is readable,
    because AT-SPI ACTIVE can lag or stay set on a window that lost focus.
    """
    strong = strong_window_instance_handles(app, windows)
    listed = []
    for index, window in windows:
        handle = strong.get(index) or ""
        if active_handle is None:
            active = state_contains(window, Atspi.StateType.ACTIVE)
        else:
            active = bool(handle) and handle == active_handle
        listed.append({
            "title": accessible_window_title(app, window, strong),
            "role": str(node_role(window) or "window").strip().lower(),
            "handle": handle,
            "providerBusName": provider_bus_name(window),
            "providerObjectPath": provider_object_path(window),
            "processGeneration": process_generation(node_pid(window)),
            "active": active,
            "inputTarget": not is_infrastructure_window(window),
        })
    return listed


def list_apps_inventory():
    entries = []
    inventory_issues = []
    seen_identities = set()
    unreadable = 0
    root_state = correlation_root_window_state()
    active_window_id = root_state.get("activeWindow") if root_state else None
    apps = iter_apps()
    # Titles of every AT-SPI top-level window, by process; see
    # untracked_window_notes.
    accessible_titles = {}
    accessible_coverage = {}
    # Toolkit names of registrations without windows, by process; see
    # invisible_window_notes.
    windowless_toolkits = {}
    for app in sorted(
        apps,
        key=lambda item: (node_name(item).lower(), node_pid(item)),
    ):
        pid = node_pid(app)
        bus = provider_bus_name(app)
        generation = process_generation(pid)
        identity = (str(bus or "").strip(), str(generation or "").strip())
        if all(identity) and identity in seen_identities:
            continue
        if all(identity):
            seen_identities.add(identity)
        windows = app_windows(app)
        accessible_titles.setdefault(pid, set()).update(
            str(node_name(window) or "").strip() for _, window in windows)
        if not inventory_complete(windows):
            # A busy or hung application does not answer within the AT-SPI
            # call timeout. Its windows cannot be listed, but the listing
            # must not read as complete.
            unreadable += 1
            inventory_issues.append({
                "app": node_name(app) or "unnamed application",
                "reason": "The accessibility window inventory did not answer completely; missing windows remain unknown.",
            })
        if not windows:
            record_window_coverage(accessible_coverage, app, windows)
            windowless_toolkits.setdefault(pid, set()).add(
                str(safe(app.get_toolkit_name, "") or "").strip().lower())
            continue
        strong = strong_window_instance_handles(app, windows)
        record_window_coverage(accessible_coverage, app, windows, strong)
        hidden = unmapped_window_indices(app, windows, strong)
        if hidden:
            windows = [item for item in windows if item[0] not in hidden]
            strong = {index: handle for index, handle in strong.items()
                      if index not in hidden}
        titles = [
            accessible_window_title(app, window, strong) or "untitled"
            for _, window in windows
        ]
        title = titles[0]
        name = node_name(app)
        # None means the window manager did not report an active window, so
        # app_top_level_windows falls back to AT-SPI state.
        active_handle = (
            opaque_window_instance_handle_for_id(app, active_window_id) or ""
            if root_state is not None
            else None
        )
        listed_windows = app_top_level_windows(app, windows, active_handle)
        # Flag entries that cannot receive input directly in the listing:
        # desktop, panel and window-manager entries otherwise look like real
        # applications, and resolve_app rejects actions against them (see
        # infrastructure_app_error). This is accepts_user_input() over the
        # windows already probed for the listing.
        input_capable = any(window["inputTarget"] for window in listed_windows)
        suffix = "" if input_capable else (
            ", NOT an input target: every window is untitled and has no "
            "interactive content — desktop infrastructure, action tools will "
            "refuse it")
        entries.append({
            "name": name,
            "pid": pid,
            "title": title,
            "other_windows": titles[1:],
            "suffix": suffix,
            "input_capable": input_capable,
            "provider_bus_name": bus,
            "process_generation": generation,
            "windows": listed_windows,
        })
    name_counts = {}
    for entry in entries:
        name_counts[entry["name"]] = name_counts.get(entry["name"], 0) + 1
    lines = []
    for entry in entries:
        pid = (
            ", pid={}".format(entry["pid"])
            if name_counts[entry["name"]] > 1
            else ""
        )
        other_windows = (
            ", other_windows=" + " | ".join(entry["other_windows"])
            if entry["other_windows"]
            else ""
        )
        display_text = (
            '{} [window="{}"{}{}{}]'.format(
                entry["name"],
                entry["title"],
                pid,
                other_windows,
                entry["suffix"],
            )
        )
        entry["display_text"] = display_text
        lines.append(display_text)
    notes = []
    if not inventory_complete(apps):
        inventory_issues.append({
            "reason": "The accessibility bus did not list every application.",
        })
        notes.append(
            "Incomplete: the accessibility bus did not list every application."
        )
    if unreadable:
        notes.append(
            "Incomplete: {} application(s) did not answer the accessibility "
            "bus; their windows may be missing above.".format(unreadable)
        )
    managed = _x11_managed_windows()
    native_entries = []
    if inventory_complete(apps) and inventory_complete(managed):
        accessible_pids = {node_pid(app) for app in apps}
        # A registered application can expose no windows, or expose only
        # some of them. Bind the remaining native windows independently.
        native_windows = native_only_windows(
            managed, accessible_coverage, active_window_id)
        inventory_issues.extend({"reason": error} for error in inventory_errors(native_windows))
        native_entries = native_app_entries(native_windows)
        lines.extend(entry["displayText"] for entry in native_entries)
        notes.extend(invisible_window_notes(
            managed, accessible_pids,
            windowless_toolkits={
                pid: toolkits for pid, toolkits in windowless_toolkits.items()
                if not any(accessible_titles.get(pid) or ())
            }))
        if not unreadable:
            # A window listed above with a native handle needs no second
            # mention; the note covers only windows still left out.
            listed = {window["id"] for window in native_windows}
            notes.extend(untracked_window_notes(
                [window for window in managed if window.get("id") not in listed],
                accessible_titles))
    return {
        "text": "\n".join(lines),
        "notes": notes,
        "inventory_issues": inventory_issues,
        "apps": [
            {
                "name": entry["name"],
                "bundleIdentifier": entry["name"],
                "pid": entry["pid"],
                "providerBusName": entry["provider_bus_name"],
                "processGeneration": entry["process_generation"],
                "displayText": entry["display_text"],
                "inputCapable": entry["input_capable"],
                "windows": entry["windows"],
            }
            for entry in entries
        ] + native_entries,
    }


def app_window_inventory(query, app_identity=None, target_window_handle=None):
    """Return every current top-level window title for one resolved AX app.

    A request by name alone lists the windows of every application that
    matches the name; see app_name_window_inventory.
    """
    if app_identity is None and not str(target_window_handle or "").strip():
        return app_name_window_inventory(query)
    app = (
        resolve_app_identity(app_identity)
        if app_identity is not None
        else resolve_app(query)
    )
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
    target = str(target_window_handle or "").strip()
    titles = []
    matched = 0
    for index, window in windows:
        if target and window_instance_handle(app, index, window) != target:
            continue
        matched += 1
        title = str(node_name(window) or "").strip()
        if title:
            titles.append(title)
    if target and matched == 0:
        raise OperationFailure(
            "stale_window_ref: the referenced logical window no longer exists.",
            failure_code="stale_window_ref",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    return {
        "ok": True,
        "appName": str(node_name(app) or "").strip(),
        "appPid": node_pid(app),
        "windowTitles": titles,
    }


def _incomplete_inventory(subject):
    return OperationFailure(
        "observation_incomplete: the application window inventory could not "
        "be read completely.",
        failure_code="observation_incomplete",
        failure_phase="observation",
        input_events_emitted=False,
        failure_subject=subject,
    )


def app_name_window_inventory(query):
    """Return the window titles of every application that matches query.

    A title wait asks whether an application of that name shows such a
    window, so it is not bound to one process: the application may have been
    closed and launched again, or run as several processes with one name.
    When the desktop inventory was read completely and no application
    matches, the result lists no titles; that answers the wait (the title is
    absent) instead of failing the lookup. An unreadable inventory or window
    list fails as observation_incomplete, because a missed window would turn
    into a false absence.
    """
    inventory = iter_apps()
    if not inventory_complete(inventory):
        raise _incomplete_inventory("app")
    titles = []
    names = []
    pids = []
    for app in inventory:
        if not matches_app_bootstrap_query(app, query):
            continue
        windows = app_windows(app)
        if not windows.complete:
            raise _incomplete_inventory("window")
        names.append(str(node_name(app) or "").strip())
        pids.append(node_pid(app))
        for _, window in windows:
            title = str(node_name(window) or "").strip()
            if title:
                titles.append(title)
    return {
        "ok": True,
        "appName": names[0] if len(set(names)) == 1 else "",
        "appPid": pids[0] if len(pids) == 1 else 0,
        "windowTitles": titles,
    }


def windowless_snapshot(app_name, app_pid, closed_handle):
    sampled_at = utc_timestamp()
    lines = ["No top-level AX windows remain for this application after close."]
    return {
        "app": {
            "name": app_name or "closed application",
            "bundleIdentifier": app_name or "closed application",
            "pid": int(app_pid or 0),
        },
        "windowAbsent": True,
        "closedWindowHandle": closed_handle,
        "windows": [],
        "sampledAt": sampled_at,
        "snapshotId": snapshot_digest(
            "windowless:" + str(closed_handle or ""), lines, "", ""
        ),
        "treeLines": lines,
        "elements": [],
        "refs": {},
    }
