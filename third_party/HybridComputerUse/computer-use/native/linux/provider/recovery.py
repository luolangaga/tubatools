"""Recovery hints derived from a complete current window inventory."""

from .foundation import (
    Atspi,  # pyright: ignore[reportAttributeAccessIssue]
    inventory_complete,
    node_name,
    node_pid,
    node_role,
    process_generation,
    provider_bus_name,
    provider_object_path,
    state_value,
)
from .ref_continuity import canonical_model_role
from .x11_windows import (
    app_windows,
    iter_apps,
    strong_window_instance_handle_inventory,
)


def _visible_window_inventory(windows):
    visible = []
    complete = True
    for index, window in windows:
        states = [
            state_value(window, Atspi.StateType.SHOWING),
            state_value(window, Atspi.StateType.ACTIVE),
            state_value(window, Atspi.StateType.MODAL),
        ]
        if any(value is True for value in states):
            visible.append((index, window))
        elif any(value is None for value in states):
            complete = False
    return visible, complete


def _current_window_candidate(visible, strong_handles):
    strong_visible = [
        item for item in visible
        if str(strong_handles.get(item[0]) or "").startswith("w2:")
    ]
    tiers = [
        [
            item for item in strong_visible
            if state_value(item[1], Atspi.StateType.MODAL) is True
            and state_value(item[1], Atspi.StateType.SHOWING) is True
        ],
        [
            item for item in strong_visible
            if state_value(item[1], Atspi.StateType.ACTIVE) is True
        ],
        strong_visible,
    ]
    return next((tier[0] for tier in tiers if len(tier) == 1), None)


def _window_identity(window):
    return (
        provider_bus_name(window),
        provider_object_path(window),
        process_generation(node_pid(window)),
    )


def _window_dto(index, window, strong_handles):
    handle = strong_handles.get(index)
    if not str(handle or "").startswith("w2:"):
        return {}
    return {
        "windowHandle": handle,
        "title": str(node_name(window) or "").strip(),
        "role": canonical_model_role(node_role(window)),
        "providerBusName": provider_bus_name(window),
        "providerObjectPath": provider_object_path(window),
        "processGeneration": process_generation(node_pid(window)),
    }


def _source_app_identity(operation):
    identity = operation.get("app_identity") or {}
    provider_bus = str(identity.get("providerBusName") or "").strip()
    generation = str(identity.get("processGeneration") or "").strip()
    if provider_bus and generation:
        return provider_bus, generation

    target = operation.get("target") or operation.get("element") or {}
    provider_bus = str(target.get("providerBusName") or "").strip()
    generation = str(target.get("processGeneration") or "").strip()
    if provider_bus and generation:
        return provider_bus, generation

    provider_bus = str(target.get("providerWindowBusName") or "").strip()
    generation = str(
        target.get("providerWindowProcessGeneration") or ""
    ).strip()
    if provider_bus and generation:
        return provider_bus, generation
    return None


def _source_window_identity(operation):
    target = operation.get("target") or operation.get("element") or {}
    handle = str(
        operation.get("window_handle")
        or target.get("windowHandle")
        or ""
    ).strip()
    provider_bus = str(
        target.get("providerWindowBusName")
        or (
            target.get("providerBusName")
            if str(target.get("role") or "").strip().lower()
            in {"frame", "window", "dialog", "alert", "file chooser", "color chooser"}
            else ""
        )
        or ""
    ).strip()
    provider_path = str(
        target.get("providerWindowObjectPath")
        or (
            target.get("providerObjectPath")
            if provider_bus
            else ""
        )
        or ""
    ).strip()
    generation = str(
        target.get("providerWindowProcessGeneration")
        or (
            target.get("processGeneration")
            if provider_bus
            else ""
        )
        or ""
    ).strip()
    provider_identity = (
        (provider_bus, provider_path, generation)
        if provider_bus and provider_path and generation
        else None
    )
    return handle, provider_identity


def bounded_recovery_context(operation):
    """Read current app/window inventory without inferring action success."""
    context = {
        "sourceAppStatus": "unknown",
        "sourceWindowStatus": "unknown",
        "inventoryComplete": False,
    }
    source_app_identity = _source_app_identity(operation or {})
    if source_app_identity is None:
        return context

    try:
        applications = iter_apps()
    except Exception:
        return context
    app_inventory_complete = inventory_complete(applications)
    matches = [
        app
        for app in applications
        if (
            provider_bus_name(app),
            process_generation(node_pid(app)),
        ) == source_app_identity
    ]
    if len(matches) != 1:
        if app_inventory_complete and not matches:
            context.update({
                "sourceAppStatus": "exited",
                "sourceWindowStatus": "gone",
                "inventoryComplete": True,
            })
        return context

    app = matches[0]
    context["sourceAppStatus"] = "alive"
    try:
        windows = app_windows(app)
    except Exception:
        return context
    window_inventory_complete = inventory_complete(windows)
    strong_inventory = strong_window_instance_handle_inventory(app, windows)
    native_inventory_complete = bool(strong_inventory.complete)
    strong_handles = dict(strong_inventory) if native_inventory_complete else {}
    visible, visibility_complete = _visible_window_inventory(windows)
    context["inventoryComplete"] = bool(
        app_inventory_complete
        and window_inventory_complete
        and native_inventory_complete
        and visibility_complete
    )

    source_handle, source_provider_identity = _source_window_identity(
        operation or {}
    )
    source_matches = []
    for index, window in windows:
        handle_matches = bool(
            source_handle
            and str(source_handle).startswith("w2:")
            and strong_handles.get(index) == source_handle
        )
        provider_matches = bool(
            source_provider_identity
            and _window_identity(window) == source_provider_identity
        )
        if handle_matches or provider_matches:
            source_matches.append((index, window))
    if len(source_matches) == 1:
        context["sourceWindowStatus"] = "present"
    elif context["inventoryComplete"] and not source_matches and (
        source_handle or source_provider_identity
    ):
        context["sourceWindowStatus"] = "gone"

    if context["inventoryComplete"]:
        selected = _current_window_candidate(visible, strong_handles)
        current_window = (
            _window_dto(*selected, strong_handles)
            if selected is not None
            else {}
        )
        if current_window:
            context["currentWindow"] = current_window
    return context


def recovery_window_candidate(app, windows):
    """Return one unambiguous current window for recovery, never as a rebind."""
    if not inventory_complete(windows):
        return {}
    strong_inventory = strong_window_instance_handle_inventory(app, windows)
    if not strong_inventory.complete:
        return {}
    strong_handles = dict(strong_inventory)
    visible, visibility_complete = _visible_window_inventory(windows)
    if not visibility_complete:
        return {}
    selected = _current_window_candidate(visible, strong_handles)
    if selected is None:
        return {}
    return _window_dto(*selected, strong_handles)
