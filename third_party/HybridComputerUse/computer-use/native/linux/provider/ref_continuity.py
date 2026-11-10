"""Linux provider ref continuity and semantic matching."""

import time
from .foundation import (
    Atspi,
    InventoryResult,
    OperationFailure,
    child_at,
    provider_identity_path,
    split_transient_item_path,
    enumerate_children,
    node_pid,
    node_role,
    process_generation,
    provider_bus_name,
    provider_object_path,
    safe,
    state_contains,
    state_value,
)
from .node_visibility import showing_state

from .ax_node_properties import (
    accessible_id,
    node_actions,
    node_description,
    placeholder_text,
)
from .ax_records import (
    effective_name,
    effective_role,
    record_for,
)

from .x11_windows import relative_frame, window_instance_handle
from .primitives import semantic_text_key


def semantic_target_predicates_match(record, target):
    states = {
        word.lower()
        for word in str(record.get("states") or "").strip(" []").split()
    }
    actions = {
        str(action).lower()
        for action in (record.get("actions") or [])
    }
    for predicate in target.get("predicates") or []:
        kind = str(predicate.get("kind") or "")
        value = str(predicate.get("value") or "")
        if kind == "state":
            if value.lower() not in states:
                return False
        elif kind == "desc":
            if (
                semantic_text_key(record.get("description"))
                != semantic_text_key(value)
            ):
                return False
        elif kind == "placeholder":
            if (
                semantic_text_key(record.get("placeholder"))
                != semantic_text_key(value)
            ):
                return False
        elif kind == "action":
            if value.lower() not in actions:
                return False
        else:
            raise OperationFailure(
                "semantic_target_invalid reason=unknown_predicate kind={!r}; "
                "no input was emitted.".format(kind),
                failure_code="semantic_target_invalid",
                failure_phase="precondition",
                input_events_emitted=False,
            )
    return True


MODEL_ROLE_ALIASES = {
    "push button": "button",
    "toggle button": "button",
    "check box": "checkbox",
    "entry": "textbox",
    "spin button": "spinbutton",
    "combo box": "combobox",
    "list item": "listitem",
    "tree item": "treeitem",
    "page tab": "tab",
    "page tab list": "tablist",
    "tool bar": "toolbar",
    "menu item": "menuitem",
    "radio button": "radio",
    "scroll bar": "scrollbar",
    "document web": "document",
    "tree table": "treegrid",
    "table cell": "cell",
}


def semantic_role_matches(live_role, requested_role, provider_aliases=None):
    live = str(live_role or "").strip().lower()
    requested = str(requested_role or "").strip().lower()
    if not requested:
        return True
    aliases = {
        str(alias or "").strip().lower()
        for alias in (provider_aliases or [])
        if str(alias or "").strip()
    }
    return (
        live == requested
        or live in aliases
        or MODEL_ROLE_ALIASES.get(live, live) == requested
    )


def complete_provider_identity_key(record, logical_window):
    bus = str(record.get("providerBusName") or "").strip()
    path = str(record.get("providerObjectPath") or "").strip()
    generation = str(record.get("processGeneration") or "").strip()
    logical_window = str(logical_window or "").strip()
    if not (bus and path and generation and logical_window):
        return None
    return logical_window, generation, bus, path


def canonical_model_role(role):
    normalized = str(role or "").strip().lower()
    return MODEL_ROLE_ALIASES.get(normalized, normalized)


def provider_roles_compatible(first, second):
    left = canonical_model_role(first)
    right = canonical_model_role(second)
    return bool(left and right and left == right)


def merge_positive_states(first, second):
    """Raw state strings contain positive facts only; merge them as a set."""
    values = {
        token
        for source in (first, second)
        for token in str(source or "").strip(" []").split()
        if token
    }
    return " ".join(sorted(values))


def merge_optional_bool(first, second):
    if first is None:
        return second
    if second is None:
        return first
    if bool(first) != bool(second):
        return None
    return bool(first)


def semantic_field_choice(values):
    """Choose a stable, information-rich value independent of traversal order."""
    candidates = {
        str(value or "").strip()
        for value in values
        if str(value or "").strip()
    }
    if not candidates:
        return "", False
    normalized = {}
    for candidate in candidates:
        normalized.setdefault(semantic_text_key(candidate), []).append(candidate)
    chosen_key = sorted(
        normalized,
        key=lambda item: (-len(item), item.casefold(), item),
    )[0]
    chosen = sorted(
        normalized[chosen_key],
        key=lambda item: (-len(item), item.casefold(), item),
    )[0]
    return chosen, len(normalized) > 1


def deduplicate_provider_records(records, logical_window):
    """Coalesce only exact complete provider identities in one logical window."""
    canonical = []
    by_identity = {}
    for source in records:
        record = dict(source)
        identity = complete_provider_identity_key(record, logical_window)
        if identity is None:
            canonical.append(record)
            continue
        existing = by_identity.get(identity)
        if existing is None:
            record["runtimeAliases"] = [
                list(record.get("runtimeId") or [])
            ]
            record["_providerIdentityConflict"] = False
            by_identity[identity] = record
            canonical.append(record)
            continue
        alias = list(record.get("runtimeId") or [])
        if alias and alias not in existing["runtimeAliases"]:
            existing["runtimeAliases"].append(alias)
            existing["runtimeAliases"].sort()
        if not provider_roles_compatible(
            existing.get("controlType"),
            record.get("controlType"),
        ):
            existing["_providerIdentityConflict"] = True
        existing["states"] = merge_positive_states(
            existing.get("states"),
            record.get("states"),
        )
        existing["actions"] = sorted(set(
            existing.get("actions") or []
        ) | set(record.get("actions") or []))
        existing["showing"] = merge_optional_bool(
            existing.get("showing"), record.get("showing")
        )
        existing["visible"] = merge_optional_bool(
            existing.get("visible"), record.get("visible")
        )
        for field in ("name", "value", "description", "placeholder"):
            chosen, conflict = semantic_field_choice(
                (existing.get(field), record.get(field))
            )
            existing[field] = chosen
            if conflict and field in {"name", "description"}:
                existing["_providerIdentityConflict"] = True
        current_path = list(existing.get("runtimeId") or [])
        candidate_path = list(record.get("runtimeId") or [])
        if candidate_path and (
            not current_path or tuple(candidate_path) < tuple(current_path)
        ):
            existing["runtimeId"] = candidate_path
            existing["index"] = record.get("index", existing.get("index"))
    return canonical


def _provider_field(node, field):
    if field == "bus":
        return str(node.app.bus_name or "")
    if field == "path":
        return provider_identity_path(node)
    if field == "generation":
        return process_generation(int(node.get_process_id() or 0))
    raise ValueError(field)


def scan_provider_identity(candidates, provider_bus, provider_path,
                           provider_generation, max_nodes, deadline):
    """Locate an exact provider identity without building full semantic records."""
    matches = []
    complete = True
    errors = []
    visited = 0
    item_owner = split_transient_item_path(provider_path)
    stack = []
    for window_index, window in reversed(list(candidates)):
        stack.append((window, [window_index]))
    while stack:
        if visited >= max_nodes or time.monotonic() >= deadline:
            complete = False
            errors.append("provider identity scan budget exhausted")
            break
        node, path = stack.pop()
        visited += 1
        try:
            bus = _provider_field(node, "bus")
            # The raw path is enough for every node but the owner of a
            # re-created item (see provider_object_path()); reading the full
            # identity of every scanned node would cost a state read each.
            object_path = str(node.path or "")
            generation = _provider_field(node, "generation")
        except Exception as error:
            complete = False
            errors.append(
                "provider identity read failed: {}".format(type(error).__name__)
            )
            bus = object_path = generation = ""
        if bus == provider_bus and generation == provider_generation:
            if object_path == provider_path:
                # The tuple is unique within one provider process
                # generation. Resolving it does not require proving that
                # every unrelated AX branch is readable, and stopping here
                # avoids an O(tree) scan for an already-addressed object.
                return InventoryResult([(node, path)], complete=True)
            if item_owner and object_path == item_owner[0]:
                item = child_at(node, item_owner[1])
                if (
                    item is not None
                    and provider_identity_path(item) == provider_path
                ):
                    return InventoryResult(
                        [(item, path + [item_owner[1]])], complete=True)
        children = enumerate_children(node, "provider node")
        if not children.complete:
            complete = False
            errors.extend(children.errors)
        for child_index, child in reversed(children.items):
            stack.append((child, path + [child_index]))
    return InventoryResult(matches, complete=complete, errors=errors)


def resolve_provider_runtime_path(app, path, target, candidate_indexes):
    """Resolve a snapshot path, then prove that it is still the exact provider object."""
    try:
        normalized = [int(index) for index in (path or [])]
    except (TypeError, ValueError):
        return InventoryResult(
            complete=False,
            errors=["provider runtime path was malformed"],
        )
    if not normalized or normalized[0] not in candidate_indexes:
        return InventoryResult([], complete=True)

    # Self-managed tables expose cells through Table.get_accessible_at rather
    # than as enumerable children. The last two path components are the
    # zero-based row and column emitted by render_visible_cells().
    requested_role = canonical_model_role(target.get("role"))
    identity_scope = str(
        target.get("providerIdentityScope") or "self"
    ).strip().lower()
    node = None
    if (
        identity_scope == "table_owner"
        and requested_role in {"cell", "gridcell"}
        and len(normalized) >= 3
    ):
        container = app
        try:
            for index in normalized[:-2]:
                container = container.get_child_at_index(index)
                if container is None:
                    break
            owner_identity = (
                _provider_field(container, "bus"),
                _provider_field(container, "path"),
                _provider_field(container, "generation"),
            ) if container is not None else ("", "", "")
            expected = (
                str(target.get("providerBusName") or "").strip(),
                str(target.get("providerObjectPath") or "").strip(),
                str(target.get("processGeneration") or "").strip(),
            )
            if owner_identity != expected:
                return InventoryResult([], complete=True)
            table = (
                container.get_table_iface()
                if container is not None
                else None
            )
            if table is not None:
                node = Atspi.Table.get_accessible_at(
                    table, normalized[-2], normalized[-1]
                )
        except Exception as error:
            return InventoryResult(
                complete=False,
                errors=[
                    "provider table path read failed: {}".format(
                        type(error).__name__
                    )
                ],
            )
    if node is None:
        node = app
        for index in normalized:
            try:
                node = node.get_child_at_index(index)
            except Exception as error:
                return InventoryResult(
                    complete=False,
                    errors=[
                        "provider runtime path read failed: {}".format(
                            type(error).__name__
                        )
                    ],
                )
            if node is None:
                return InventoryResult([], complete=True)

    try:
        identity = (
            _provider_field(node, "bus"),
            _provider_field(node, "path"),
            _provider_field(node, "generation"),
        )
    except Exception as error:
        return InventoryResult(
            complete=False,
            errors=[
                "provider runtime identity read failed: {}".format(
                    type(error).__name__
                )
            ],
        )
    expected = (
        str(target.get("providerBusName") or "").strip(),
        str(target.get("providerObjectPath") or "").strip(),
        str(target.get("processGeneration") or "").strip(),
    )
    if identity_scope != "table_owner" and identity != expected:
        return InventoryResult([], complete=True)
    return InventoryResult([(node, normalized)], complete=True)


def resolve_table_cell_from_owner(provider_scan, target):
    """Resolve a virtual table cell after locating its exact owner table."""
    runtime_id = target.get("runtimeId") or []
    if (
        str(target.get("providerIdentityScope") or "").strip().lower()
        != "table_owner"
        or len(runtime_id) < 3
    ):
        return provider_scan
    try:
        row, col = int(runtime_id[-2]), int(runtime_id[-1])
    except (TypeError, ValueError):
        return InventoryResult(
            complete=False,
            errors=["virtual table cell coordinates were malformed"],
        )
    resolved = []
    complete = provider_scan.complete
    errors = list(provider_scan.errors)
    for owner, owner_path in provider_scan:
        try:
            table = owner.get_table_iface()
            cell = (
                Atspi.Table.get_accessible_at(table, row, col)
                if table is not None
                else None
            )
        except Exception as error:
            complete = False
            errors.append(
                "provider table cell read failed: {}".format(
                    type(error).__name__
                )
            )
            continue
        if cell is not None:
            resolved.append((cell, list(owner_path) + [row, col]))
    return InventoryResult(resolved, complete=complete, errors=errors)


def resolve_provider_runtime_paths(app, target, candidates):
    paths = []
    primary = target.get("runtimeId") or []
    if primary:
        paths.append(primary)
    paths.extend(target.get("runtimeAliases") or [])
    if not paths:
        return InventoryResult([], complete=True)
    candidate_indexes = {index for index, _window in candidates}
    complete = True
    errors = []
    for path in paths:
        result = resolve_provider_runtime_path(
            app, path, target, candidate_indexes
        )
        if result:
            return result
        if not result.complete:
            complete = False
            errors.extend(result.errors)
    return InventoryResult([], complete=complete, errors=errors)


def semantic_probe_record(node, target, path):
    """Read only fields required to reject or accept one semantic candidate."""
    try:
        live_role = str(node.get_role_name() or "")
    except Exception as error:
        return None, "role read failed: {}".format(type(error).__name__)
    live_role = effective_role(node, live_role)
    if not semantic_role_matches(
        live_role,
        target.get("role"),
        target.get("providerRoleAliases"),
    ):
        return None, None

    record = {
        "runtimeId": list(path),
        "controlType": live_role,
        "name": "",
        "description": "",
        "placeholder": "",
        "states": "",
        "actions": [],
    }
    if target.get("hasName"):
        try:
            record["name"] = str(effective_name(node) or "")
        except Exception as error:
            return None, "name read failed: {}".format(type(error).__name__)
        if (
            semantic_text_key(record["name"])
            != semantic_text_key(target.get("name"))
        ):
            record["description"] = node_description(node)
            if (
                semantic_text_key(record["description"])
                != semantic_text_key(target.get("name"))
            ):
                return None, None

    predicate_kinds = {
        str(predicate.get("kind") or "")
        for predicate in target.get("predicates") or []
    }
    if "desc" in predicate_kinds and not record["description"]:
        record["description"] = node_description(node)
    if "placeholder" in predicate_kinds:
        record["placeholder"] = placeholder_text(node)
    if "state" in predicate_kinds:
        requested_states = []
        for predicate in target.get("predicates") or []:
            if str(predicate.get("kind") or "") != "state":
                continue
            state_name = str(predicate.get("value") or "").strip().upper()
            state = getattr(Atspi.StateType, state_name, None)
            if state is not None and state_contains(node, state):
                requested_states.append(state_name.lower())
        record["states"] = " ".join(requested_states)
    if "action" in predicate_kinds:
        try:
            record["actions"], _ = node_actions(node)
        except Exception as error:
            return None, "action read failed: {}".format(type(error).__name__)
    if not semantic_target_predicates_match(record, target):
        return None, None
    return record, None


def scan_semantic_target(candidates, target, max_nodes, deadline, text_limit):
    """Find semantic candidates without constructing records for non-matches."""
    matches = []
    complete = True
    errors = []
    visited = 0
    provider_roles = {}
    stack = []
    for window_index, window, window_bounds in reversed(list(candidates)):
        stack.append((window, [window_index], window, window_bounds))
    while stack:
        if visited >= max_nodes or time.monotonic() >= deadline:
            complete = False
            errors.append("semantic scan budget exhausted")
            break
        node, path, owner_window, owner_bounds = stack.pop()
        visited += 1
        probe, error = semantic_probe_record(node, target, path)
        provider_key = (
            provider_bus_name(node),
            provider_object_path(node),
            process_generation(node_pid(node)),
        )
        if all(provider_key):
            live_role = canonical_model_role(node_role(node))
            previous_role = provider_roles.get(provider_key)
            if (
                previous_role
                and live_role
                and previous_role != live_role
            ):
                complete = False
                errors.append("provider identity exposed incompatible roles")
            elif live_role:
                provider_roles[provider_key] = live_role
        if error:
            complete = False
            errors.append(error)
        elif probe is not None:
            try:
                record = dict(probe)
                record.update({
                    "index": 0,
                    "runtimeId": list(path),
                    "automationId": accessible_id(node),
                    "providerBusName": provider_bus_name(node),
                    "providerObjectPath": provider_identity_path(node),
                    "processGeneration": process_generation(node_pid(node)),
                    "showing": showing_state(node),
                    "visible": state_value(node, Atspi.StateType.VISIBLE),
                    "localizedControlType": probe["controlType"],
                    "className": str(
                        safe(getattr(node, "get_toolkit_name", None), "") or ""
                    ),
                    "value": "",
                    "nativeWindowHandle": 0,
                    "frame": relative_frame(node, owner_bounds),
                    "textAttributes": None,
                    "relations": {},
                })
                record["_resolvedNode"] = node
                matches.append((record, owner_window, owner_bounds))
            except Exception as record_error:
                complete = False
                errors.append(
                    "matched record read failed: {}".format(
                        type(record_error).__name__
                    )
                )
        children = enumerate_children(node, "semantic node")
        if not children.complete:
            complete = False
            errors.extend(children.errors)
        for child_index, child in reversed(children.items):
            stack.append(
                (child, path + [child_index], owner_window, owner_bounds)
            )
    return InventoryResult(matches, complete=complete, errors=errors)


def provider_identity_record(node, path, window_bounds, text_limit):
    try:
        return record_for(
            node,
            0,
            list(path),
            window_bounds,
            text_limit=text_limit,
        )
    except Exception as error:
        raise OperationFailure(
            "observation_incomplete: the matched accessibility object could not "
            "be sampled completely; no input was emitted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="element",
        ) from error


def provider_identity_matches_target(record, target):
    if record.get("_providerIdentityConflict"):
        return False
    requested_role = str(target.get("role") or "")
    if requested_role and not semantic_role_matches(
        record.get("controlType"),
        requested_role,
        target.get("providerRoleAliases"),
    ):
        return False
    if target.get("hasName"):
        expected = semantic_text_key(target.get("name"))
        actual = semantic_text_key(record.get("name"))
        if actual != expected:
            return False
    return semantic_target_predicates_match(record, target)


def semantic_record_path(record, records):
    by_path = {
        tuple(item.get("runtimeId") or []): item
        for item in records
    }
    path = list(record.get("runtimeId") or [])
    parts = []
    for depth in range(1, len(path) + 1):
        item = by_path.get(tuple(path[:depth]))
        if item is None:
            continue
        role = MODEL_ROLE_ALIASES.get(
            str(item.get("controlType") or "").strip().lower(),
            str(item.get("controlType") or "element").strip().lower(),
        )
        name = semantic_text_key(
            item.get("name") or item.get("description")
        )
        part = role
        if name:
            part += ' "{}"'.format(name.replace('"', '\\"'))
        if not parts or parts[-1] != part:
            parts.append(part)
    return " / ".join(parts)


def ambiguity_candidate(record, records, app, window_index, window):
    return {
        "role": str(record.get("controlType") or ""),
        "name": str(record.get("name") or ""),
        "description": str(record.get("description") or ""),
        "path": semantic_record_path(record, records),
        "windowHandle": safe(
            lambda: window_instance_handle(app, window_index, window),
            "",
        ),
        "providerBusName": str(
            record.get("providerBusName") or ""
        ),
        "providerObjectPath": str(
            record.get("providerObjectPath") or ""
        ),
        "processGeneration": str(
            record.get("processGeneration") or ""
        ),
    }
