"""Window candidates and provider-bound identity resolution for semantic targets."""

import time

from .foundation import (
    Atspi,
    MAX_ELEMENTS,
    OperationFailure,
    TREE_DEADLINE_SECONDS,
    inventory_complete,
    node_pid,
    process_generation,
    provider_bus_name,
    provider_object_path,
    state_contains,
)
from .recovery import recovery_window_candidate
from .ax_node_properties import positive_int
from .x11_windows import (
    app_windows,
    window_instance_handle,
    window_bounds,
)
from .ref_continuity import (
    deduplicate_provider_records,
    provider_identity_matches_target,
    provider_identity_record,
    resolve_provider_runtime_paths,
    resolve_table_cell_from_owner,
    scan_provider_identity,
)


def _provider_window_candidates(app, window_index, window, locator, observation):
    """Narrow the candidates to the target's provider window.

    Returns (candidates, inventory_complete, window_recovered, scan_hint).
    The scan hint is set when the window was found through its element.
    """
    selector = locator.selector
    target_window_handle = locator.target_window_handle
    provider_bus = locator.provider_bus
    provider_path = locator.provider_path
    provider_generation = locator.provider_generation
    provider_window_bus = locator.provider_window_bus
    provider_window_path = locator.provider_window_path
    provider_window_generation = locator.provider_window_generation
    provider_requested = locator.provider_requested
    candidates = [(window_index, window)]
    provider_window_recovered = False
    provider_scan_hint = None
    seen_windows = {window_index}
    window_inventory = app_windows(app)
    candidate_inventory_complete = inventory_complete(window_inventory)
    for candidate_index, candidate_window in window_inventory:
        if candidate_index in seen_windows:
            continue
        seen_windows.add(candidate_index)
        candidates.append((candidate_index, candidate_window))
    all_candidates = candidates
    exact_provider_windows = [
        (candidate_index, candidate_window)
        for candidate_index, candidate_window in all_candidates
        if provider_bus_name(candidate_window) == provider_window_bus
        and provider_object_path(candidate_window) == provider_window_path
        and process_generation(node_pid(candidate_window))
        == provider_window_generation
    ]
    logical_window_matches = []
    if target_window_handle:
        logical_window_matches = [
            (candidate_index, candidate_window)
            for candidate_index, candidate_window in all_candidates
            if window_instance_handle(
                app, candidate_index, candidate_window
            ) == target_window_handle
            and provider_bus_name(candidate_window) == provider_window_bus
            and process_generation(node_pid(candidate_window))
            == provider_window_generation
        ]
    # A strong logical window is backed by a one-to-one native window
    # identity. Toolkits may rebuild the provider top-level object while
    # leaving that native window and the exact provider element alive.
    # In that case, allow the later exact-element identity scan to decide
    # continuity without requiring the obsolete wrapper bus/path.
    if (
        not logical_window_matches
        and target_window_handle.startswith("w2:")
        and provider_requested
    ):
        logical_window_matches = [
            (candidate_index, candidate_window)
            for candidate_index, candidate_window in all_candidates
            if window_instance_handle(
                app, candidate_index, candidate_window
            ) == target_window_handle
            and process_generation(node_pid(candidate_window))
            == provider_generation
        ]
    if (
        not exact_provider_windows
        and not logical_window_matches
        and provider_requested
    ):
        if not candidate_inventory_complete:
            raise OperationFailure(
                "observation_incomplete: the accessibility window inventory "
                "could not be read completely; no input was emitted.",
                failure_code="observation_incomplete",
                failure_phase="precondition",
                input_events_emitted=False,
                failure_subject="window",
            )
        if target_window_handle.startswith("w2:"):
            raise OperationFailure(
                "stale_element_ref reason=originating_window_disappeared "
                "selector={!r}; no input was emitted.".format(selector),
                failure_code="stale_element_ref",
                failure_phase="precondition",
                input_events_emitted=False,
                failure_subject="window",
                replacement_window=recovery_window_candidate(
                    app, window_inventory
                ),
            )
        provider_scan_hint = scan_provider_identity(
            all_candidates,
            provider_bus,
            provider_path,
            provider_generation,
            max_nodes=positive_int(
                observation.get("max_tree_nodes"), MAX_ELEMENTS
            ),
            deadline=time.monotonic() + TREE_DEADLINE_SECONDS,
        )
        if provider_scan_hint:
            owner_indexes = {
                path[0]
                for _node, path in provider_scan_hint
                if path
            }
            logical_window_matches = [
                (candidate_index, candidate_window)
                for candidate_index, candidate_window in all_candidates
                if candidate_index in owner_indexes
            ]
            provider_window_recovered = bool(logical_window_matches)
        elif not provider_scan_hint.complete:
            raise OperationFailure(
                "observation_incomplete: the provider identity could not be "
                "checked completely; no input was emitted.",
                failure_code="observation_incomplete",
                failure_phase="precondition",
                input_events_emitted=False,
                failure_subject="element",
            )
    if exact_provider_windows:
        candidates = exact_provider_windows
    elif logical_window_matches:
        # Provider object paths may change while the same one-to-one native
        # window remains alive. Semantic recovery stays inside that exact
        # stable window and still requires one unique canonical match.
        candidates = logical_window_matches
        provider_window_recovered = True
    else:
        if not candidate_inventory_complete:
            raise OperationFailure(
                "observation_incomplete: the accessibility window inventory "
                "could not be read completely; no input was emitted.",
                failure_code="observation_incomplete",
                failure_phase="precondition",
                input_events_emitted=False,
                failure_subject="window",
            )
        raise OperationFailure(
            "stale_element_ref reason=provider_window_disappeared "
            "selector={!r}; no input was emitted.".format(selector),
            failure_code="stale_element_ref",
            failure_phase="precondition",
            input_events_emitted=False,
            replacement_window=recovery_window_candidate(
                app, window_inventory
            ),
        )
    return (
        candidates,
        candidate_inventory_complete,
        provider_window_recovered,
        provider_scan_hint,
    )


def _logical_window_candidates(app, window_index, window, locator):
    """Narrow the candidates to the visible window with the target's handle."""
    selector = locator.selector
    target_window_handle = locator.target_window_handle
    candidates = [(window_index, window)]
    seen_windows = {window_index}
    window_inventory = app_windows(app)
    candidate_inventory_complete = inventory_complete(window_inventory)
    for candidate_index, candidate_window in window_inventory:
        if candidate_index in seen_windows:
            continue
        if not (
            state_contains(candidate_window, Atspi.StateType.SHOWING)
            or state_contains(candidate_window, Atspi.StateType.ACTIVE)
            or state_contains(candidate_window, Atspi.StateType.MODAL)
        ):
            continue
        seen_windows.add(candidate_index)
        candidates.append((candidate_index, candidate_window))
    candidates = [
        (candidate_index, candidate_window)
        for candidate_index, candidate_window in candidates
        if window_instance_handle(
            app, candidate_index, candidate_window
        ) == target_window_handle
    ]
    if not candidates:
        if not candidate_inventory_complete:
            raise OperationFailure(
                "observation_incomplete: the accessibility window inventory "
                "could not be read completely; no input was emitted.",
                failure_code="observation_incomplete",
                failure_phase="precondition",
                input_events_emitted=False,
                failure_subject="window",
            )
        raise OperationFailure(
            "stale_element_ref reason=originating_window_disappeared "
            "selector={!r}; no input was emitted.".format(selector),
            failure_code="stale_element_ref",
            failure_phase="precondition",
            input_events_emitted=False,
            replacement_window=recovery_window_candidate(
                app, window_inventory
            ),
        )
    return candidates, candidate_inventory_complete


def _resolve_provider_bound_target(
    app,
    window_index,
    window,
    bounds,
    target,
    locator,
    candidates,
    candidate_inventory_complete,
    provider_scan_hint,
    observation,
    text_limit,
):
    """Resolve a provider-bound ref by its exact provider identity only."""
    target_window_handle = locator.target_window_handle
    provider_bus = locator.provider_bus
    provider_path = locator.provider_path
    provider_generation = locator.provider_generation
    if not candidate_inventory_complete:
        raise OperationFailure(
            "observation_incomplete: the accessibility window inventory "
            "could not be read completely; no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="window",
        )
    provider_scan = provider_scan_hint or resolve_provider_runtime_paths(
        app, target, candidates
    )
    if not provider_scan:
        max_nodes = positive_int(
            observation.get("max_tree_nodes"), MAX_ELEMENTS
        )
        provider_scan = scan_provider_identity(
            candidates,
            provider_bus,
            provider_path,
            provider_generation,
            max_nodes=max_nodes,
            deadline=time.monotonic() + TREE_DEADLINE_SECONDS,
        )
        provider_scan = resolve_table_cell_from_owner(
            provider_scan, target
        )
    # A bus + object-path + process-generation tuple is already a unique
    # provider address. Once that exact object is found, unrelated sibling
    # read failures cannot make it ambiguous. Completeness is still
    # required before declaring the provider identity absent.
    if not provider_scan.complete and not provider_scan:
        raise OperationFailure(
            "observation_incomplete: the provider identity could not be "
            "checked completely; no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="element",
        )
    if not provider_scan:
        raise OperationFailure(
            "stale_element_ref reason=provider_identity_disappeared; "
            "no input was emitted.",
            failure_code="stale_element_ref",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    records = []
    nodes_by_path = {}
    bounds_by_window = {}
    windows_by_index = {
        candidate_index: candidate_window
        for candidate_index, candidate_window in candidates
    }
    for matched_node, path in provider_scan:
        matched_window = windows_by_index.get(path[0])
        matched_bounds = (
            bounds
            if path[0] == window_index and matched_window is window
            else window_bounds(app, path[0], matched_window)
        )
        if matched_window is None or matched_bounds is None:
            raise OperationFailure(
                "observation_incomplete: the provider target window could "
                "not be sampled completely; no input was emitted.",
                failure_code="observation_incomplete",
                failure_phase="precondition",
                input_events_emitted=False,
                failure_subject="element",
            )
        record = provider_identity_record(
            matched_node, path, matched_bounds, text_limit
        )
        records.append(record)
        nodes_by_path[tuple(path)] = matched_node
        bounds_by_window[path[0]] = matched_bounds
    logical_window = target_window_handle or "provider-window"
    merged = deduplicate_provider_records(records, logical_window)
    if len(merged) != 1:
        raise OperationFailure(
            "stale_element_ref reason=provider_identity_changed; "
            "no input was emitted.",
            failure_code="stale_element_ref",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    record = merged[0]
    if not provider_identity_matches_target(record, target):
        raise OperationFailure(
            "stale_element_ref reason=provider_identity_changed; "
            "no input was emitted.",
            failure_code="stale_element_ref",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    resolved_path = tuple(record.get("runtimeId") or [])
    matched_node = nodes_by_path.get(resolved_path)
    matched_window = windows_by_index.get(resolved_path[0])
    matched_bounds = bounds_by_window.get(resolved_path[0])
    if matched_node is None or matched_window is None:
        raise OperationFailure(
            "observation_incomplete: the provider target could not be "
            "re-read consistently; no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="element",
        )
    return (
        matched_node,
        record,
        "[semantic-resolution] provider_identity=true unique=true; "
        "resolved immediately before input.",
        matched_window,
        matched_bounds,
    )


def _resolve_prior_provider_identity(
    app,
    window_index,
    window,
    bounds,
    target,
    locator,
    candidates,
    provider_scan_hint,
    observation,
    text_limit,
):
    """Resolve a semantic ref through the provider object it last named.

    Returns None when semantic matching should decide instead: the object is
    gone or changed and the target allows a provider rebind.
    """
    provider_bus = locator.provider_bus
    provider_path = locator.provider_path
    provider_generation = locator.provider_generation
    locator_kind = locator.locator_kind
    allow_provider_rebind = locator.allow_provider_rebind
    max_nodes = positive_int(
        observation.get("max_tree_nodes"), MAX_ELEMENTS
    )
    provider_scan = provider_scan_hint or resolve_provider_runtime_paths(
        app, target, candidates
    )
    if not provider_scan:
        provider_scan = scan_provider_identity(
            candidates,
            provider_bus,
            provider_path,
            provider_generation,
            max_nodes=max_nodes,
            deadline=time.monotonic() + TREE_DEADLINE_SECONDS,
        )
    if not provider_scan.complete and not provider_scan:
        raise OperationFailure(
            "observation_incomplete: the prior provider identity could not "
            "be checked completely; no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="element",
        )
    if not provider_scan:
        if not allow_provider_rebind:
            raise OperationFailure(
                "stale_element_ref reason=provider_identity_replaced; "
                "no input was emitted.",
                failure_code="stale_element_ref",
                failure_phase="precondition",
                input_events_emitted=False,
            )
    else:
        matched_node, resolved_path = provider_scan[0]
        windows_by_index = {
            candidate_index: candidate_window
            for candidate_index, candidate_window in candidates
        }
        matched_window = windows_by_index.get(resolved_path[0])
        matched_bounds = (
            bounds
            if resolved_path[0] == window_index and matched_window is window
            else window_bounds(app, resolved_path[0], matched_window)
        )
        if matched_window is None or matched_bounds is None:
            raise OperationFailure(
                "observation_incomplete: the prior provider target window could "
                "not be sampled completely; no input was emitted.",
                failure_code="observation_incomplete",
                failure_phase="precondition",
                input_events_emitted=False,
                failure_subject="element",
            )
        record = provider_identity_record(
            matched_node, resolved_path, matched_bounds, text_limit
        )
        if not provider_identity_matches_target(record, target) and not (
            allow_provider_rebind and locator_kind == "semantic"
        ):
            raise OperationFailure(
                "stale_element_ref reason=provider_identity_changed; "
                "no input was emitted.",
                failure_code="stale_element_ref",
                failure_phase="precondition",
                input_events_emitted=False,
            )
        if provider_identity_matches_target(record, target):
            return (
                matched_node,
                record,
                "[semantic-resolution] provider_identity=true unique=true; "
                "resolved immediately before input.",
                matched_window,
                matched_bounds,
            )
    return None
