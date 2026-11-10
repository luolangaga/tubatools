"""Dispatch of provider operations to AX and GUI handlers."""

from .foundation import (
    Atspi,
    DEFAULT_TEXT_LIMIT,
    MAX_DEPTH,
    MAX_ELEMENTS,
    OperationFailure,
    node_pid,
    node_role,
    process_generation,
    provider_bus_name,
    provider_object_path,
    safe,
    state_value,
)


from .ax_select import perform_select

from .ax_node_properties import (
    parse_text_limit,
    positive_int,
)

from .x11_windows import (
    main_window,
    select_window_by_handle,
    window_identity_set,
    window_bounds,
)
from .x11_window_inventory import _x11_window_inventory_cache
from .desktop_layout import x11_root_window_state

from .app_resolver import app_window_inventory, list_apps_inventory, windowless_snapshot
from .native_windows import native_window_for_operation, perform_native_window_action
from .element_resolver import (
    find_element_at_snapshot_path,
    resolve_semantic_target,
)
from .window_resolver import (
    perform_ax_window_action,
    resolve_window_action_target,
)

from .snapshot import (
    build_snapshot,
)

from .gui_capture import (
    capture_window_pixels,
    persistent_pixel_change,
    pixel_change_note,
)
from .gui_dispatch import finalize_gui_response, perform_gui
from . import action_evidence as action_evidence_state
from .phase_timing import lap
from . import ui_events

from .actionability import (
    current_geometry,
)

from .atspi_inventory import (
    resolve_operation_app,
    wait_for_ui_to_settle,
)

GUI_CHANNEL = "[gui]"

_TRANSIENT_WINDOW_ROLES = {
    "alert",
    "alert dialog",
    "dialog",
    "file chooser",
    "color chooser",
}


def _verify_expected_window(window, expectation):
    if not expectation:
        return
    expected_role = str(expectation.get("role") or "").strip().lower()
    current_role = str(node_role(window) or "").strip().lower()
    common_matches = (
        provider_bus_name(window)
        == str(expectation.get("providerBusName") or "").strip()
        and process_generation(node_pid(window))
        == str(expectation.get("processGeneration") or "").strip()
    )
    transient = (
        expected_role in _TRANSIENT_WINDOW_ROLES
        or current_role in _TRANSIENT_WINDOW_ROLES
    )
    transient_matches = (
        provider_object_path(window)
        == str(expectation.get("providerObjectPath") or "").strip()
        and current_role == expected_role
    )
    if not common_matches or (transient and not transient_matches):
        raise OperationFailure(
            "stale_target: the batch logical window changed before keyboard "
            "input; no input was emitted.",
            failure_code="stale_target",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )


from .ax_target_notes import (
    resolved_note,
)
from .ax_snapshot_diagnostics import (
    content_host_diagnostic,
    snapshot_diagnostics,
)
from .ax_transition_evidence import (
    external_desktop_transition_after_ax_mutation,
)
from .ax_input_delivery import (
    A11Y_CHANNEL,
    _perform_click,
    _perform_keypress,
    _perform_scroll,
    _perform_set_value,
    _perform_type,
)


def perform_provider_request(operation_kind, payload):
    """Dispatch one provider operation that validate_request() accepted."""
    action_intent = None
    if operation_kind == "resolve_and_act":
        action_intent = payload.get("intent")
    elif operation_kind == "window_action":
        action_intent = payload.get("action")
    return dispatch_provider_operation(
        payload,
        operation_kind=operation_kind,
        action_intent=action_intent,
    )


def dispatch_provider_operation(
    operation,
    operation_kind,
    action_intent=None,
):
    action_input = operation.get("input") or {}
    observation = operation.get("observation") or {}
    action_evidence_state.reset()
    # Native-window facts are stable only within one MCP operation. Reusing a
    # prior call's inventory can hide a newly opened dialog or retain a closed
    # window, so each operation starts with a fresh bounded inventory while
    # sharing that sample across its internal resolution steps.
    _x11_window_inventory_cache.clear()
    # A pid can be reused by a new process between operations; its start
    # time is what tells the two apart, so it is read again per operation.
    process_generation.cache_clear()
    if operation_kind == "observe_apps":
        inventory = list_apps_inventory()
        return {
            "ok": True,
            "text": inventory["text"],
            "apps": inventory["apps"],
            "notes": inventory["notes"],
            "inventory_issues": inventory.get("inventory_issues", []),
        }
    if operation_kind == "window_list":
        return app_window_inventory(
            operation.get("app", ""),
            app_identity=operation.get("app_identity"),
            target_window_handle=operation.get("window_handle"),
        )
    if operation_kind == "desktop_execute_batch":
        return _execute_desktop_batch(operation)
    if operation_kind == "window_action":
        native = native_window_for_operation(operation)
        if native is not None:
            return perform_native_window_action(action_intent, native)
        target = operation.get("target") or {}
        if (not operation.get("app_identity")
                and not target.get("providerBusName")
                and not target.get("providerWindowBusName")
                and (operation.get("window_handle") or target.get("windowHandle"))):
            # A native ref has no AX application identity. Once its native
            # validation fails, app-name lookup cannot recover that identity.
            raise OperationFailure(
                "invalid_target: the native window ref cannot be verified; "
                "list windows again. No input was emitted.",
                failure_code="invalid_target",
                failure_phase="precondition",
                input_events_emitted=False,
                failure_subject="window",
            )
    if operation_kind == "observe":
        return _observe_operation(operation, observation)
    if (
        operation_kind == "resolve_and_act"
        and action_intent == "resolve_target"
    ):
        return _resolve_target_operation(operation)
    return _perform_ax_action(
        operation,
        operation_kind,
        action_intent,
        action_input,
        observation,
    )


def _execute_desktop_batch(operation):
    try:
        response = perform_gui(
            operation.get("actions"),
            operation.get("settle_ms", 800),
            operation.get("frame_id"),
        )
    except Exception as exc:
        response = {
            "ok": False,
            "error": str(exc),
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
            "effect_unknown": False,
            "quiescent": None,
        }
    return finalize_gui_response(response)


def _observe_operation(operation, observation):
    snapshot = build_snapshot(
        operation.get("app", ""),
        text_limit=parse_text_limit(observation.get("text_limit"), DEFAULT_TEXT_LIMIT),
        max_tree_nodes=positive_int(observation.get("max_tree_nodes"), MAX_ELEMENTS),
        max_tree_depth=positive_int(observation.get("max_tree_depth"), MAX_DEPTH),
        prune=observation.get("prune", True),
        boxes=bool(observation.get("boxes", False)),
        known_refs=operation.get("known_refs"),
        app_identity=operation.get("app_identity"),
        target_window_handle=operation.get("window_handle"),
        # An unchanged window may return its previous tree; see
        # tree_reuse. Post-action snapshots below always walk.
        reuse_tree=True,
        find_walk=observation.get("find_walk") is True,
        find_query=observation.get("find_query"),
        find_resume_after=observation.get("find_resume_after"),
    )
    response = {"ok": True, "snapshot": snapshot}
    lap("observe_snapshot")
    # Modal notes come first: they determine how the rest of the tree
    # should be read.
    elements = snapshot.get("elements") or []
    if observation.get("find_walk") is True:
        # A find walk returns only candidate records, so the tree-shape
        # diagnostics below would misread its sparse record list.
        notes = snapshot.pop("modalNotes", [])
        if notes:
            response["notes"] = notes
        return response
    shell_notes = snapshot_diagnostics(elements)
    # The two diagnostics describe degrees of the same problem and are
    # never emitted together: snapshot_diagnostics covers a tree reduced to
    # its window frame, content_host_diagnostic a complete shell whose
    # content plane is missing. Once the former fires, the latter would
    # only repeat its conclusion.
    diagnostics = snapshot.pop("modalNotes", []) + shell_notes
    if not shell_notes:
        diagnostics += content_host_diagnostic(elements)
    if diagnostics:
        response["notes"] = diagnostics
    return response


def _resolve_target_operation(operation):
    """Resolve a semantic target without acting on it."""
    app = resolve_operation_app(operation)
    target_window_handle = operation.get("window_handle")
    if target_window_handle:
        window_index, window, _window_selection = select_window_by_handle(
            app, target_window_handle
        )
        _verify_expected_window(
            window,
            operation.get("expected_window"),
        )
    else:
        window_index, window = main_window(app)
    bounds = window_bounds(app, window_index, window)
    semantic_target = operation.get("target")
    if not isinstance(semantic_target, dict):
        raise OperationFailure(
            "semantic_target_invalid reason=missing_target; no input was emitted.",
            failure_code="semantic_target_invalid",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    (
        _element,
        element_record,
        _semantic_note,
        _window,
        _bounds,
    ) = resolve_semantic_target(
        app,
        window_index,
        window,
        bounds,
        semantic_target,
        operation.get("known_refs"),
        operation,
    )
    return {
        "ok": True,
        "resolvedElement": element_record,
    }


def _perform_ax_action(
    operation,
    operation_kind,
    action_intent,
    action_input,
    observation,
):
    """Resolve the target, emit one AX action, and observe its effect.

    The order is fixed: resolve, sample the before-state, act, then settle
    and read back. Each lap() marks the end of one of those phases.
    """
    app = resolve_operation_app(operation)
    # One xprop round: the managed client list and the active window are all
    # the external-transition check reads. Window geometry is not needed.
    root_window_before = x11_root_window_state()
    lap("resolve_app_and_root_window")
    (
        element,
        element_record,
        semantic_note,
        window,
        bounds,
    ) = _resolve_action_target(app, operation, operation_kind, action_input)
    lap("resolve_target")
    pixel_bounds = bounds
    # Capture pixels once before the action and twice after it; only a change
    # present in both later captures counts. Semantic targets are resolved
    # across all visible app-owned windows first, so pixel capture and later
    # focus actions bind to the window that actually matched rather than the
    # one main_window() picked before resolution.
    # Without a post-action snapshot there is no pixel note, so skip the sample.
    pixels_before = (
        safe(lambda: capture_window_pixels(pixel_bounds))
        if observation.get("capture_snapshot") is not False
        else None
    )
    lap("pixels_before")
    # Refresh the geometry from the live node right after resolving it;
    # otherwise semantic actions would use the live node while synthesized
    # input used the snapshot geometry.
    element_record, moved_note = current_geometry(element, element_record, bounds)
    # Record the top-level window set before acting. wait_for_ui_to_settle()
    # compares against it to detect opened or closed windows and decide
    # whether to wait longer for focus to settle.
    windows_before = safe(lambda: window_identity_set(app))
    lap("geometry_and_windows_before")
    # Dispatch queued AT-SPI events now, so the settle wait counts only events
    # the application emits after the input.
    ui_events.pump()
    # Every action reports the route it actually took and whether the result
    # was verified. A fresh accessibility tree in the response looks like
    # confirmation but is only a snapshot, not evidence that the action took
    # effect.
    notes = []
    action_evidence = _new_action_evidence()
    action_evidence_state.replace(action_evidence)
    _record_target_notes_and_state(
        notes,
        action_evidence,
        element,
        element_record,
        semantic_note,
        moved_note,
    )

    if operation_kind == "window_action":
        windowless_response = _perform_window_action(
            action_intent, app, window, element_record, notes, action_evidence
        )
        if windowless_response is not None:
            return windowless_response
    elif action_intent == "click":
        element_record = _perform_click(
            action_input,
            window,
            bounds,
            element,
            element_record,
            notes,
            action_evidence,
        )
    elif action_intent == "scroll":
        element_record = _perform_scroll(
            action_input,
            window,
            bounds,
            element,
            element_record,
            notes,
            action_evidence,
        )
    elif action_intent == "type":
        _perform_type(
            action_input, window, element, element_record, notes, action_evidence
        )
    elif action_intent == "keypress":
        _perform_keypress(
            action_input, window, element, element_record, notes, action_evidence
        )
    elif action_intent == "set_value":
        _perform_set_value(
            action_input, window, element, element_record, notes, action_evidence
        )
    elif action_intent == "select":
        to_element = _resolve_select_end(app, operation, action_input, window, bounds)
        perform_select(
            action_input, window, element, element_record, to_element,
            notes, action_evidence,
        )
    else:
        raise RuntimeError(
            'unsupportedProviderAction("{}")'.format(action_intent)
        )

    lap("action")
    # The input may have opened or closed native windows. Reads after this
    # point (settle, post-action snapshot, recovery) must not bind windows
    # against the native inventory sampled before the input.
    _x11_window_inventory_cache.clear()
    if observation.get("capture_snapshot") is False:
        return _delivery_only_response(action_evidence, element_record, notes)
    return _observe_after_action(
        operation,
        operation_kind,
        observation,
        app,
        bounds,
        element_record,
        notes,
        action_evidence,
        root_window_before,
        windows_before,
        pixels_before,
    )


def _resolve_action_target(app, operation, operation_kind, action_input):
    """Return (element, element_record, semantic_note, window, bounds).

    A target names either a snapshot element or a semantic locator, never
    both. Every failure here happens before any input is emitted.
    """
    element_record = operation.get("element")
    semantic_target = operation.get("target")
    semantic_note = None
    if element_record is not None and semantic_target is not None:
        raise OperationFailure(
            "semantic_target_invalid reason=both_numeric_and_semantic_targets; "
            "no input was emitted.",
            failure_code="semantic_target_invalid",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    if semantic_target is not None and operation_kind == "window_action":
        (
            element,
            element_record,
            window,
            bounds,
        ) = resolve_window_action_target(app, semantic_target)
    else:
        target_window_handle = operation.get("window_handle")
        if target_window_handle:
            window_index, window, _window_selection = select_window_by_handle(
                app, target_window_handle
            )
            _verify_expected_window(
                window,
                operation.get("expected_window"),
            )
        else:
            window_index, window = main_window(app)
        bounds = operation.get("window_bounds") or window_bounds(
            app, window_index, window)
    if semantic_target is not None and operation_kind != "window_action":
        (
            element,
            element_record,
            semantic_note,
            window,
            bounds,
        ) = resolve_semantic_target(
            app,
            window_index,
            window,
            bounds,
            semantic_target,
            operation.get("known_refs"),
            operation,
        )
    elif semantic_target is None and action_input.get("window_centre"):
        element = None
    elif semantic_target is None:
        element = find_element_at_snapshot_path(app, element_record)
        if element_record is not None and element is None:
            raise OperationFailure(
                "stale_element_ref reason=path_changed_or_element_disappeared "
                "no input was emitted. Refresh AX mode=observe/find and use the "
                "newly returned public ref.",
                failure_code="stale_element_ref",
                failure_phase="precondition",
                input_events_emitted=False,
            )
    return element, element_record, semantic_note, window, bounds


def _resolve_select_end(app, operation, action_input, window, bounds):
    """Resolve the select range end (to_ref) in the target's window, or None."""
    to_target = action_input.get("to_target")
    if to_target is None:
        return None
    window_path = provider_object_path(window)
    window_index = None
    for index in range(int(safe(app.get_child_count, 0) or 0)):
        child = safe(lambda i=index: app.get_child_at_index(i))
        if child is not None and provider_object_path(child) == window_path:
            window_index = index
            break
    if window_index is None:
        raise OperationFailure(
            "stale_element_ref reason=select_window_disappeared; no input was "
            "emitted.",
            failure_code="stale_element_ref",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    to_element, _record, _note, to_window, _bounds = resolve_semantic_target(
        app, window_index, window, bounds, to_target,
        operation.get("known_refs"), operation,
    )
    if provider_object_path(to_window) != window_path:
        raise OperationFailure(
            "select_invalid_range: to_ref is in a different window than ref; "
            "no input was emitted.",
            failure_code="invalid_argument",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    return to_element


def _new_action_evidence():
    return {
        "route": "unknown",
        "geometry_sampled": False,
        "geometry_stable": None,
        "window_focus_verified": None,
        "target_focus_verified": None,
        "point_owner_verified": None,
        "enabled": None,
        "editable": None,
        "focus_verified": None,
        "editable_verified": None,
        "readback_sampled": False,
        "readback_verified": None,
        "input_emitted": False,
    }


def _record_target_notes_and_state(
    notes,
    action_evidence,
    element,
    element_record,
    semantic_note,
    moved_note,
):
    if semantic_note:
        notes.append(semantic_note)
    # The resolved-target note precedes all action notes: first what was
    # targeted, then what was done.
    resolved = resolved_note(element, element_record)
    if resolved:
        notes.append(A11Y_CHANNEL + " " + resolved)
    if moved_note:
        # A11Y_CHANNEL has no trailing space; without the separator this would
        # render as `[a11y]The element MOVED`.
        notes.append(A11Y_CHANNEL + " " + moved_note)
    if element is not None:
        # A present ENABLED/EDITABLE state is useful positive evidence. Its
        # absence is not a reliable negative across AT-SPI providers (GTK file
        # items and several LibreOffice nodes omit these states while still
        # accepting input), so preserve that case as unknown.
        if state_value(element, Atspi.StateType.ENABLED) is True:
            action_evidence["enabled"] = True
        if state_value(element, Atspi.StateType.EDITABLE) is True:
            action_evidence["editable"] = True
        if state_value(element, Atspi.StateType.DEFUNCT) is True:
            raise OperationFailure(
                "stale_element_ref reason=target_defunct; no input was emitted.",
                failure_code="stale_element_ref",
                failure_phase="precondition",
                input_events_emitted=False,
            )


def _perform_window_action(
    action_intent, app, window, element_record, notes, action_evidence
):
    """Run a native window command; return the response if no window is left."""
    window_result = perform_ax_window_action(
        action_intent, app, window, element_record
    )
    action_evidence["route"] = "native_window_command"
    action_evidence["input_emitted"] = True
    action_evidence["readback_sampled"] = True
    action_evidence["readback_verified"] = True
    notes.append(window_result["note"])
    if window_result["windowless"]:
        return {
            "ok": True,
            "snapshot": windowless_snapshot(
                window_result["app_name"],
                window_result["app_pid"],
                window_result["closed_handle"],
            ),
            "input_events_emitted": True,
            "desktop_state_mutated": True,
            "mutation_unknown": False,
            "actionEvidence": action_evidence,
            "notes": notes,
        }
    return None


def _delivery_only_response(action_evidence, element_record, notes):
    response = {
        "ok": True,
        "actionEvidence": action_evidence,
        "input_events_emitted": bool(action_evidence.get("input_emitted")),
        "desktop_state_mutated": bool(action_evidence.get("input_emitted")),
        "mutation_unknown": False,
        "observationSkipped": True,
    }
    if element_record is not None:
        response["resolvedElement"] = element_record
    if notes:
        response["notes"] = notes
    return response


def _observe_after_action(
    operation,
    operation_kind,
    observation,
    app,
    bounds,
    element_record,
    notes,
    action_evidence,
    root_window_before,
    windows_before,
    pixels_before,
):
    """Settle, read the window back, and report what changed."""
    settle_note = wait_for_ui_to_settle(app, windows_before)
    lap("settle_wait")
    if settle_note:
        notes.append(settle_note)
    # Compare pixels only after the UI settles: right after the action the UI
    # may still be repainting, so an earlier diff would show an intermediate
    # frame rather than the effect.
    target_window_handle = (operation.get("window_handle")
                            or (operation.get("target") or {}).get("windowHandle"))
    selected_window = _post_action_window(app, target_window_handle)
    after_bounds = safe(lambda: window_bounds(app, *selected_window[:2]))
    after_pixel_bounds = after_bounds
    pixels_after_one = safe(lambda: capture_window_pixels(after_pixel_bounds))
    lap("after_bounds_and_pixels_after_one")
    after_snapshot = build_snapshot(
        operation.get("app", ""),
        text_limit=parse_text_limit(
            observation.get("text_limit"), DEFAULT_TEXT_LIMIT),
        max_tree_nodes=positive_int(
            observation.get("max_tree_nodes"), MAX_ELEMENTS),
        max_tree_depth=positive_int(
            observation.get("max_tree_depth"), MAX_DEPTH),
        prune=observation.get("prune", True),
        boxes=bool(observation.get("boxes", False)),
        known_refs=operation.get("known_refs"),
        app_identity=operation.get("app_identity"),
        selected_window=selected_window,
    )
    lap("post_action_snapshot")
    # Modal notes matter most right after an action opens a modal dialog,
    # because the caller's element list predates the dialog. Insert them first
    # so they are read before any action note.
    notes[:0] = after_snapshot.pop("modalNotes", [])
    # Run the content-host check on the post-action snapshot as well: an
    # action that brings a content host to the front (a browser window, a
    # webview tab) is exactly when the caller must learn that this channel
    # cannot see the content, and it often will not observe again separately.
    notes += content_host_diagnostic(after_snapshot.get("elements") or [])
    root_window_after = x11_root_window_state()
    app_windows_after = safe(lambda: window_identity_set(app))
    lap("diagnostics_root_and_windows_after")
    response = {
        "ok": True,
        "snapshot": after_snapshot,
        "actionEvidence": action_evidence,
        "externalDesktopTransition": (
            external_desktop_transition_after_ax_mutation(
                root_window_before,
                root_window_after,
                windows_before,
                app_windows_after,
            )
        ),
    }
    if operation_kind == "window_action":
        response.update({
            "input_events_emitted": True,
            "desktop_state_mutated": True,
            "mutation_unknown": False,
        })
    if element_record is not None:
        response["resolvedElement"] = element_record
    if (target_window_handle
            and after_snapshot.get("windowHandle") != target_window_handle):
        # A new dialog or a surviving sibling is not the original pixel region.
        pixels_before = None
    # Take the second post-action capture after building the snapshot. That
    # work spaces the two captures far enough apart for a 1 Hz caret blink to
    # toggle, so the blink is not reported as a persistent change.
    change_note = pixel_change_note(persistent_pixel_change(
        pixels_before, pixels_after_one,
        safe(lambda: capture_window_pixels(after_pixel_bounds))))
    lap("pixels_after_two_and_diff")
    if change_note:
        notes.append(change_note)
    if notes:
        response["notes"] = notes
    return response


def _post_action_window(app, target_window_handle):
    """Keep the action's window unless it closed or a visible modal took over."""
    if not target_window_handle:
        index, window = main_window(app)
        return index, window, {}
    try:
        bound = select_window_by_handle(app, target_window_handle)
    except OperationFailure as exc:
        if exc.failure_code != "stale_window_ref":
            raise
        # A confirmed disappearance permits a fresh observation of the app.
        # An incomplete inventory must not be mistaken for a closed window.
        return select_window_by_handle(app, None)
    preferred = select_window_by_handle(app, None)
    window = preferred[1]
    if (state_value(window, Atspi.StateType.MODAL) is True
            and (state_value(window, Atspi.StateType.SHOWING) is True
                 or state_value(window, Atspi.StateType.VISIBLE) is True)):
        return preferred
    return bound
