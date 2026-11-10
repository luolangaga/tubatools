"""Resolution of snapshot refs and semantic targets to live nodes."""

from dataclasses import dataclass

from .foundation import (
    Atspi,
    DEFAULT_TEXT_LIMIT,
    OperationFailure,
    child_at,
    node_role,
    safe,
)
from .node_geometry import extents
from .primitives import name_fingerprint

from .ax_node_properties import (
    accessible_id,
    parse_text_limit,
)
from .ax_records import (
    effective_name,
    effective_role,
)

from .x11_windows import (
    main_window,
    relative_frame,
)
from .primitives import semantic_text_key
from .semantic_window_candidates import (
    _logical_window_candidates,
    _provider_window_candidates,
    _resolve_prior_provider_identity,
    _resolve_provider_bound_target,
)
from .semantic_scan import (
    _lightweight_semantic_scan,
    _render_semantic_scan,
)


def resolve_path(app, path):
    if not path:
        return None
    node = app
    for index in path:
        node = child_at(node, int(index))
        if node is None:
            return None
    return node


def same_frame(record_frame, node_frame):
    if record_frame is None or node_frame is None:
        return False
    for key in ("x", "y", "width", "height"):
        if abs(float(record_frame.get(key, 0)) - float(node_frame.get(key, 0))) > 3:
            return False
    return True


def record_name_matches(node, record):
    """Return whether the node's name equals the name stored in ``record``.

    Both sides must be measured the same way. Record names are truncated by
    limit_text() while live names are complete, so a direct comparison would
    fail silently and quietly weaken the identity check. Truncated records
    carry ``nameHash``, a fingerprint of the full name, which avoids both
    truncation mismatches and false matches on a shared prefix.
    """
    live = effective_name(node)
    fingerprint = record.get("nameHash")
    if fingerprint:
        return name_fingerprint(live) == fingerprint
    return live == str(record.get("name") or "")


def record_still_matches(node, record, window_bounds):
    """Return whether a node resolved by path is still the snapshot element.

    ``runtimeId`` is a positional path of child indices, so once the tree
    changes the same path can point at a different control. A ref to a
    context-menu item such as "Rename…" can, for example, resolve to a toolbar
    toggle after the menu closes, and the action would still report success.
    Silently acting on the wrong control is the worst failure mode: it is
    undetectable and can be destructive ("Move to Trash" may sit next to
    "Rename…"). Failing resolution so the caller re-reads state is preferable.

    The check uses only identifiers the snapshot actually recorded:

    - automationId, if present, must match (the strongest, a stable id
      provided by the toolkit);
    - otherwise a recorded name requires role and name to match;
    - otherwise the role must match, the node must still be unnamed, and a
      recorded frame must be unchanged, because an unnamed control can only
      be identified by position.
    """
    if node is None:
        return False
    target_id = str(record.get("automationId") or "")
    if target_id:
        return accessible_id(node) == target_id
    target_role = str(record.get("controlType") or "")
    if target_role and effective_role(node) != target_role:
        return False
    target_name = str(record.get("name") or "")
    if target_name:
        return record_name_matches(node, record)
    if effective_name(node):
        # The snapshot recorded no name but the node now has one, so it is a
        # different element. Use effective_name() here as well: a borrowed
        # name counts as a name, as it does when record_for() stores records.
        return False
    if record.get("frame") is None:
        return True
    return same_frame(record.get("frame"), relative_frame(node, window_bounds))


def find_element_at_snapshot_path(app, record):
    """Resolve only the original snapshot path; never rebind to a same-name peer."""
    if not record:
        return None
    _, window = main_window(app)
    return find_element_at_snapshot_path_with_bounds(
        app, record, extents(window))


def find_element_at_snapshot_path_with_bounds(app, record, window_bounds):
    """Resolve a snapshot path using the bounds of the window that owns it."""
    if not record:
        return None
    paths = [record.get("runtimeId") or []]
    paths.extend(record.get("runtimeAliases") or [])
    seen_paths = set()
    for path in paths:
        path_key = tuple(path)
        if path_key in seen_paths:
            continue
        seen_paths.add(path_key)
        node = resolve_path(app, path)
        if node is not None and record_still_matches(
            node, record, window_bounds
        ):
            return node
    # Self-managed tables expose visible cells through Table.get_accessible_at
    # instead of child enumeration. render_visible_cells gives those records a
    # stable virtual suffix [row, col], so the action resolver must understand
    # the same address type. Otherwise the tool returns usable-looking indices
    # and refs that always fail as stale.
    if str(record.get("controlType") or "").lower() == "table cell":
        for path in paths:
            if len(path) < 3:
                continue
            container = resolve_path(app, path[:-2])
            table = (
                safe(container.get_table_iface)
                if container is not None
                else None
            )
            if table is None:
                continue
            try:
                row, col = int(path[-2]), int(path[-1])
            except (TypeError, ValueError):
                continue
            node = safe(
                lambda: Atspi.Table.get_accessible_at(table, row, col))
            if (
                node is not None
                and record_still_matches(node, record, window_bounds)
            ):
                return node
    return None


@dataclass(frozen=True)
class _SemanticLocator:
    """The fields of a semantic target that resolution reads."""

    selector: str
    role: str
    name_key: str
    has_name: bool
    predicates: list
    target_window_handle: str
    provider_bus: str
    provider_path: str
    provider_generation: str
    provider_window_bus: str
    provider_window_path: str
    provider_window_generation: str
    locator_kind: str
    allow_provider_rebind: bool
    provider_requested: bool
    provider_window_requested: bool


def resolve_semantic_target(
    app, window_index, window, bounds, target, known_refs, operation
):
    """Resolve one live semantic target across visible app-owned windows.

    The steps run in a fixed order and each may fail closed before input:
    decode the locator, choose the candidate windows, try the exact provider
    identity, scan the candidates for live matches, then require exactly one.
    """
    observation = operation.get("observation") or {}
    locator = _decode_semantic_target(target)
    text_limit = parse_text_limit(
        observation.get("text_limit"), DEFAULT_TEXT_LIMIT)
    candidates = [(window_index, window)]
    candidate_inventory_complete = True
    provider_window_recovered = False
    provider_scan_hint = None
    if locator.provider_window_requested:
        (
            candidates,
            candidate_inventory_complete,
            provider_window_recovered,
            provider_scan_hint,
        ) = _provider_window_candidates(
            app, window_index, window, locator, observation
        )
    elif locator.target_window_handle:
        (
            candidates,
            candidate_inventory_complete,
        ) = _logical_window_candidates(app, window_index, window, locator)

    if locator.locator_kind == "provider_bound":
        return _resolve_provider_bound_target(
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
        )

    if locator.locator_kind == "semantic" and locator.provider_requested:
        resolved = _resolve_prior_provider_identity(
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
        )
        if resolved is not None:
            return resolved

    candidate_count = len(candidates)
    needs_provider_action_scan = any(
        str(predicate.get("kind") or "") == "action"
        for predicate in locator.predicates
    )
    if not needs_provider_action_scan:
        provider_matches = []
        by_name, by_description, scan_incomplete = _lightweight_semantic_scan(
            app,
            window_index,
            window,
            bounds,
            target,
            locator,
            candidates,
            observation,
            text_limit,
        )
    else:
        (
            provider_matches,
            by_name,
            by_description,
            scan_incomplete,
        ) = _render_semantic_scan(
            app,
            window_index,
            window,
            bounds,
            target,
            locator,
            candidates,
            known_refs,
            observation,
            text_limit,
        )

    resolved = _unique_provider_match(app, provider_matches)
    if resolved is not None:
        return resolved
    record, matched_window, matched_bounds, _candidate = _unique_semantic_match(
        by_name,
        by_description,
        scan_incomplete,
        locator.selector,
        candidate_count,
    )
    node = _live_node_for_semantic_match(
        app, target, locator, record, matched_bounds
    )
    note = _semantic_resolution_note(
        target, locator, candidate_count, provider_window_recovered
    )
    return node, record, note, matched_window, matched_bounds


def _decode_semantic_target(target):
    """Read the locator fields and reject a target that names nothing."""
    selector = str(target.get("selector") or "")
    role = str(target.get("role") or "").strip()
    name = str(target.get("name") or "")
    name_key = semantic_text_key(name)
    has_name = bool(target.get("hasName"))
    predicates = target.get("predicates") or []
    target_window_handle = str(target.get("windowHandle") or "").strip()
    provider_bus = str(target.get("providerBusName") or "").strip()
    provider_path = str(target.get("providerObjectPath") or "").strip()
    provider_generation = str(target.get("processGeneration") or "").strip()
    provider_window_bus = str(
        target.get("providerWindowBusName") or ""
    ).strip()
    provider_window_path = str(
        target.get("providerWindowObjectPath") or ""
    ).strip()
    provider_window_generation = str(
        target.get("providerWindowProcessGeneration") or ""
    ).strip()
    locator_kind = str(
        target.get("locatorKind") or "semantic"
    ).strip().lower()
    allow_provider_rebind = bool(target.get("allowProviderRebind", False))
    provider_requested = bool(
        provider_bus and provider_path and provider_generation
    )
    provider_window_requested = bool(
        provider_window_bus
        and provider_window_path
        and provider_window_generation
    )
    if not role and not has_name and not predicates:
        raise OperationFailure(
            "semantic_target_invalid reason=empty selector={!r}; "
            "no input was emitted.".format(selector),
            failure_code="semantic_target_invalid",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    if locator_kind == "provider_bound" and not provider_requested:
        raise OperationFailure(
            "semantic_target_invalid reason=provider_bound_identity_incomplete; "
            "no input was emitted.",
            failure_code="semantic_target_invalid",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    return _SemanticLocator(
        selector=selector,
        role=role,
        name_key=name_key,
        has_name=has_name,
        predicates=predicates,
        target_window_handle=target_window_handle,
        provider_bus=provider_bus,
        provider_path=provider_path,
        provider_generation=provider_generation,
        provider_window_bus=provider_window_bus,
        provider_window_path=provider_window_path,
        provider_window_generation=provider_window_generation,
        locator_kind=locator_kind,
        allow_provider_rebind=allow_provider_rebind,
        provider_requested=provider_requested,
        provider_window_requested=provider_window_requested,
    )


def _unique_provider_match(app, provider_matches):
    """Return the resolution for one exact provider match, or None."""
    if len(provider_matches) > 1:
        raise OperationFailure(
            "provider_identity_ambiguous; no input was emitted.",
            failure_code="semantic_target_ambiguous",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    if len(provider_matches) == 1:
        record, matched_window, matched_bounds = provider_matches[0]
        node = find_element_at_snapshot_path_with_bounds(
            app, record, matched_bounds
        )
        if node is not None:
            note = (
                "[semantic-resolution] provider_identity=true unique=true "
                "resolved immediately before input."
            )
            return (
                node,
                record,
                note,
                matched_window,
                matched_bounds,
            )
    return None


def _unique_semantic_match(
    by_name, by_description, scan_incomplete, selector, candidate_count
):
    """Return the one match; name matches outrank description matches."""
    matches = by_name if by_name else by_description
    if not matches:
        if scan_incomplete:
            raise OperationFailure(
                "observation_incomplete reason=semantic_target_scan_incomplete; "
                "no input was emitted.",
                failure_code="observation_incomplete",
                failure_phase="precondition",
                input_events_emitted=False,
                failure_subject="element",
            )
        raise OperationFailure(
            "semantic_target_not_found selector={!r}; searched {} visible "
            "top-level window(s) owned by the application and found no matching "
            "live element; no input was emitted.".format(
                selector, candidate_count),
            failure_code="semantic_target_not_found",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    if len(matches) != 1:
        candidates_payload = [
            match[3] for match in matches if match[3] is not None
        ][:5]
        raise OperationFailure(
            "semantic_target_ambiguous selector={!r} matches at least {} live "
            "elements; exactly one is required and no input was emitted.".format(
                selector, len(matches)),
            failure_code="semantic_target_ambiguous",
            failure_phase="precondition",
            input_events_emitted=False,
            candidates=candidates_payload,
            candidate_count=len(matches),
        )
    if scan_incomplete:
        raise OperationFailure(
            "observation_incomplete reason=semantic_target_scan_incomplete; "
            "no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="element",
        )
    return matches[0]


def _live_node_for_semantic_match(app, target, locator, record, matched_bounds):
    """Return the live node of the match, checking rebind lineage first."""
    allow_provider_rebind = locator.allow_provider_rebind
    if target.get("editTarget") is not None and target["editTarget"] != record.get("editTarget"):
        raise OperationFailure(
            "stale_element_ref reason=composite_edit_target_replaced; no input "
            "was emitted. Refresh AX observe/find and use the new ref.",
            failure_code="stale_element_ref", failure_phase="precondition",
            input_events_emitted=False, failure_subject="element",
        )
    provider_requested = locator.provider_requested
    if allow_provider_rebind and provider_requested:
        permitted_paths = [target.get("runtimeId") or []]
        permitted_paths.extend(target.get("runtimeAliases") or [])
        live_path = list(record.get("runtimeId") or [])
        if live_path not in permitted_paths:
            raise OperationFailure(
                "stale_element_ref reason=provider_identity_replaced_outside_"
                "runtime_lineage; no input was emitted.",
                failure_code="stale_element_ref",
                failure_phase="precondition",
                input_events_emitted=False,
            )
    node = record.pop("_resolvedNode", None)
    if node is None:
        node = find_element_at_snapshot_path_with_bounds(
            app, record, matched_bounds)
    if node is None:
        raise OperationFailure(
            "stale_element_ref reason=live_semantic_record_changed_before_input "
            "no input was emitted; refresh AX mode=observe/find and use the "
            "newly returned public ref.",
            failure_code="stale_element_ref",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    return node


def _semantic_resolution_note(
    target, locator, candidate_count, provider_window_recovered
):
    selector = locator.selector
    target_window_handle = locator.target_window_handle
    scope_note = " window_scope=verified" if target_window_handle else ""
    source_note = ""
    if target.get("sourceIndex") is not None:
        source_note = " source=current_snapshot_index index={}".format(
            target.get("sourceIndex")
        )
    note = (
        "[semantic-resolution] fresh=true unique=true selector={!r}; searched "
        "{} visible app-owned window(s){}{}{} and resolved inside the action "
        "runtime immediately before input."
    ).format(
        selector,
        candidate_count,
        scope_note,
        source_note,
        (
            " provider_window_recovered=true"
            if provider_window_recovered
            else ""
        ),
    )
    return note
