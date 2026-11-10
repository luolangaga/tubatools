"""Lightweight and rendered scans that match semantic targets to live nodes."""

from typing import Any
import time

from .foundation import (
    MAX_DEPTH,
    MAX_ELEMENTS,
    TREE_DEADLINE_SECONDS,
    safe,
)
from .ax_snapshot import render_tree
from .ax_node_properties import positive_int
from .ax_records import StableIndexer
from .x11_windows import window_bounds, window_instance_handle
from .ref_continuity import (
    ambiguity_candidate,
    deduplicate_provider_records,
    scan_semantic_target,
    semantic_role_matches,
    semantic_target_predicates_match,
)
from .primitives import semantic_text_key


def _lightweight_semantic_scan(
    app,
    window_index,
    window,
    bounds,
    target,
    locator,
    candidates,
    observation,
    text_limit,
):
    """Match the target without rendering full records for non-matches.

    Returns (by_name, by_description, scan_incomplete).
    """
    name_key = locator.name_key
    has_name = locator.has_name
    target_window_handle = locator.target_window_handle
    by_name = []
    by_description = []
    scan_incomplete = False
    lightweight_candidates = []
    for candidate_index, candidate_window in candidates:
        candidate_bounds = (
            bounds
            if candidate_index == window_index and candidate_window is window
            else window_bounds(app, candidate_index, candidate_window)
        )
        if candidate_bounds is None:
            scan_incomplete = True
            continue
        lightweight_candidates.append(
            (candidate_index, candidate_window, candidate_bounds)
        )
    semantic_scan = scan_semantic_target(
        lightweight_candidates,
        target,
        max_nodes=positive_int(
            observation.get("max_tree_nodes"), MAX_ELEMENTS
        ),
        deadline=time.monotonic() + TREE_DEADLINE_SECONDS,
        text_limit=text_limit,
    )
    if not semantic_scan.complete:
        scan_incomplete = True
    grouped: dict[int, dict[str, Any]] = {}
    for record, candidate_window, candidate_bounds in semantic_scan:
        runtime_id = list(record.get("runtimeId") or [])
        candidate_index = runtime_id[0] if runtime_id else window_index
        window_group: dict[str, Any] = grouped.setdefault(
            candidate_index,
            {
                "window": candidate_window,
                "bounds": candidate_bounds,
                "records": [],
            },
        )
        window_group["records"].append(record)
    semantic_records = []
    for candidate_index, window_group in grouped.items():
        logical_window = (
            target_window_handle
            or safe(
                lambda: window_instance_handle(
                    app, candidate_index, window_group["window"]
                ),
                "",
            )
            or "window-index:{}".format(candidate_index)
        )
        records = deduplicate_provider_records(
            window_group["records"], logical_window
        )
        if any(
            record.get("_providerIdentityConflict")
            for record in records
        ):
            scan_incomplete = True
        for record in records:
            semantic_records.append(
                (
                    record,
                    window_group["window"],
                    window_group["bounds"],
                    records,
                )
            )
    for (
        record,
        candidate_window,
        candidate_bounds,
        sibling_records,
    ) in semantic_records:
        match_group = "name"
        if has_name and (
            semantic_text_key(record.get("name")) != name_key
        ):
            match_group = "description"
        destination = (
            by_name if match_group == "name" else by_description
        )
        candidate_payload = None
        if len(destination) < 5:
            candidate_index = int(
                (record.get("runtimeId") or [window_index])[0]
            )
            candidate_payload = ambiguity_candidate(
                record,
                sibling_records,
                app,
                candidate_index,
                candidate_window,
            )
        destination.append(
            (
                record,
                candidate_window,
                candidate_bounds,
                candidate_payload,
            )
        )
    return by_name, by_description, scan_incomplete


def _render_semantic_scan(
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
):
    """Match the target against fully rendered canonical records.

    Action predicates need the rendered records. Returns
    (provider_matches, by_name, by_description, scan_incomplete).
    """
    role = locator.role
    name_key = locator.name_key
    has_name = locator.has_name
    target_window_handle = locator.target_window_handle
    provider_bus = locator.provider_bus
    provider_path = locator.provider_path
    provider_generation = locator.provider_generation
    provider_requested = locator.provider_requested
    indexer = StableIndexer(known_refs)
    provider_matches = []
    by_name = []
    by_description = []
    scan_incomplete = False
    for candidate_index, candidate_window in candidates:
        candidate_bounds = (
            bounds
            if candidate_index == window_index and candidate_window is window
            else window_bounds(app, candidate_index, candidate_window)
        )
        if candidate_bounds is None:
            scan_incomplete = True
            continue
        records, _lines, annotations = render_tree(
            candidate_window,
            candidate_bounds,
            [candidate_index],
            text_limit=text_limit,  # pyright: ignore[reportArgumentType]
            max_tree_nodes=positive_int(
                observation.get("max_tree_nodes"), MAX_ELEMENTS),
            max_tree_depth=positive_int(
                observation.get("max_tree_depth"), MAX_DEPTH),
            # Model-facing projection must never decide action truth. Find,
            # refs and actions all resolve against canonical provider records.
            prune=False,
            boxes=False,
            indexer=indexer,
        )
        # Any provider omission means the canonical scan did not prove the
        # target absent. The reason taxonomy is diagnostic-only and must not
        # decide action truth.
        if any(
            annotation.get("kind") == "provider_omission"
            for annotation in annotations
        ):
            scan_incomplete = True
        logical_window = (
            target_window_handle
            or safe(
                lambda: window_instance_handle(
                    app, candidate_index, candidate_window
                ),
                "",
            )
            or "window-index:{}".format(candidate_index)
        )
        records = deduplicate_provider_records(
            records,
            logical_window,
        )
        if any(
            record.get("_providerIdentityConflict")
            for record in records
        ):
            scan_incomplete = True
        for record in records:
            if (
                provider_requested
                and str(record.get("providerBusName") or "") == provider_bus
                and str(record.get("providerObjectPath") or "") == provider_path
                and str(record.get("processGeneration") or "")
                == provider_generation
            ):
                provider_matches.append(
                    (record, candidate_window, candidate_bounds)
                )
            live_role = str(record.get("controlType") or "")
            if not semantic_role_matches(
                live_role,
                role,
                target.get("providerRoleAliases"),
            ):
                continue

            match_group = "name"
            if has_name:
                live_name = str(record.get("name") or "")
                if semantic_text_key(live_name) == name_key:
                    match_group = "name"
                else:
                    live_description = str(record.get("description") or "")
                    if semantic_text_key(live_description) != name_key:
                        continue
                    match_group = "description"

            if not semantic_target_predicates_match(record, target):
                continue
            destination = (
                by_name if match_group == "name" else by_description
            )
            candidate_payload = None
            if len(destination) < 5:
                candidate_payload = ambiguity_candidate(
                    record,
                    records,
                    app,
                    candidate_index,
                    candidate_window,
                )
            destination.append(
                (
                    record,
                    candidate_window,
                    candidate_bounds,
                    candidate_payload,
                )
            )
    return provider_matches, by_name, by_description, scan_incomplete
