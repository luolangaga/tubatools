#!/usr/bin/env python3
"""Shared fixtures and dependency-aware patch helpers for Linux provider tests."""

import json

import os

from pathlib import Path

import subprocess

import sys

import tempfile

import unittest

from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import runtime as runtime_entrypoint  # noqa: E402

from native.linux.provider import (  # noqa: E402
    a11y_enable,
    actionability,
    action_evidence,
    app_resolver,
    atspi_inventory,
    ax_dispatch,
    ax_dropdown,
    ax_input_delivery,
    ax_actions,
    ax_node_properties,
    ax_records,
    ax_select,
    ax_snapshot,
    snapshot_children,
    focus_probe,
    ax_snapshot_diagnostics,
    ax_target_notes,
    ax_transition_evidence,
    ax_virtual_table,
    element_resolver,
    find_walk,
    semantic_window_candidates,
    semantic_scan,
    foundation,
    node_geometry,
    node_visibility,
    node_facts,
    gui_capture,
    gui_dispatch,
    gui_input,
    input_primitives,
    input_method,
    phase_timing,
    ref_continuity,
    recovery,
    response_builder,
    snapshot,
    tree_reuse,
    ui_events,
    native_windows,
    window_diagnostics,
    window_resolver,
    window_scale,
    wire,
    wire_request,
    x11_windows,
    desktop_layout,
    x11_window_inventory,
)

PROVIDER_MODULES = (
    foundation,
    node_geometry,
    node_visibility,
    a11y_enable,
    node_facts,
    action_evidence,
    phase_timing,
    app_resolver,
    x11_windows,
    desktop_layout,
    x11_window_inventory,
    atspi_inventory,
    ax_node_properties,
    ax_records,
    ax_snapshot,
    snapshot_children,
    focus_probe,
    ax_virtual_table,
    gui_capture,
    gui_dispatch,
    snapshot,
    tree_reuse,
    ui_events,
    ref_continuity,
    recovery,
    element_resolver,
    find_walk,
    semantic_window_candidates,
    semantic_scan,
    window_resolver,
    window_scale,
    native_windows,
    actionability,
    gui_input,
    input_primitives,
    input_method,
    ax_actions,
    ax_select,
    ax_dropdown,
    ax_dispatch,
    ax_input_delivery,
    ax_snapshot_diagnostics,
    ax_target_notes,
    ax_transition_evidence,
    window_diagnostics,
    response_builder,
    wire,
    wire_request,
)


class RuntimeTestSurface:
    """Test facade that patches a name in every provider module that imports it."""

    def __init__(self):
        object.__setattr__(self, "_originals", {})

    def _targets(self, name):
        modules = (runtime_entrypoint,) + PROVIDER_MODULES
        for module in modules:
            if name in vars(module):
                value = vars(module)[name]
                return [
                    candidate
                    for candidate in modules
                    if vars(candidate).get(name, object()) is value
                ]
        return []

    def __getattr__(self, name):
        targets = self._targets(name)
        if targets:
            return getattr(targets[0], name)
        raise AttributeError(name)

    def __setattr__(self, name, value):
        originals = object.__getattribute__(self, "_originals")
        if name not in originals:
            originals[name] = [
                (module, getattr(module, name))
                for module in self._targets(name)
            ]
        targets = [module for module, _value in originals[name]]
        if not targets:
            setattr(runtime_entrypoint, name, value)
            return
        for module in targets:
            setattr(module, name, value)

    def __delattr__(self, name):
        originals = object.__getattribute__(self, "_originals")
        entries = originals.pop(name, [])
        for target, value in entries:
            setattr(target, name, value)


runtime = RuntimeTestSurface()


def _provider_target_for_test(target):
    if not isinstance(target, dict):
        return None
    result = dict(target)
    result.pop("sourceIndex", None)
    return result


def perform_provider_operation(operation):
    """Exercise behavior cases using only provider request fields."""
    intent = operation["intent"]
    if intent == "observe_apps":
        return runtime.perform_provider_request(
            "observe_apps",
            {"protocol_version": 2},
        )
    if intent == "observe":
        return runtime.perform_provider_request(
            "observe",
            {
                "protocol_version": 2,
                "app": operation.get("app", ""),
                "app_identity": operation.get("app_identity"),
                "window_handle": operation.get("window_handle", ""),
                "observation": {
                    key: value
                    for key, value in {
                        "text_limit": operation.get("text_limit"),
                        "max_tree_nodes": operation.get("max_tree_nodes"),
                        "max_tree_depth": operation.get("max_tree_depth"),
                        "prune": operation.get("prune"),
                        "boxes": operation.get("boxes"),
                        "include_screenshot": operation.get("include_screenshot"),
                    }.items()
                    if value is not None
                },
            },
        )
    if intent == "desktop_execute_batch":
        return runtime.perform_provider_request(
            "desktop_execute_batch",
            {
                "protocol_version": 2,
                "actions": operation.get("actions") or [],
                "settle_ms": operation.get("settle_ms", 800),
                "frame_id": operation.get("frame_id"),
            },
        )
    if intent == "window_list":
        return runtime.perform_provider_request(
            "window_list",
            {
                "protocol_version": 2,
                "app": operation.get("app", ""),
                "app_identity": operation.get("app_identity"),
                "window_handle": operation.get("window_handle", ""),
            },
        )
    if intent in {"activate", "close"}:
        return runtime.perform_provider_request(
            "window_action",
            {
                "protocol_version": 2,
                "action": intent,
                "app": operation.get("app", ""),
                "app_identity": operation.get("app_identity"),
                "window_handle": operation.get("window_handle", ""),
                "target": _provider_target_for_test(operation.get("target")),
                "observation": {},
            },
        )
    return runtime.perform_provider_request(
        "resolve_and_act",
        {
            "protocol_version": 2,
            "intent": intent,
            "app": operation.get("app", ""),
            "app_identity": operation.get("app_identity"),
            "window_handle": operation.get("window_handle", ""),
            "element": operation.get("element"),
            "target": _provider_target_for_test(operation.get("target")),
            "input": {
                key: value
                for key, value in {
                    "x": operation.get("x"),
                    "y": operation.get("y"),
                    "click_count": operation.get("click_count"),
                    "mouse_button": operation.get("mouse_button"),
                    "click_method": operation.get("click_method"),
                    "action": operation.get("action"),
                    "direction": operation.get("direction"),
                    "pages": operation.get("pages"),
                    "text": operation.get("text"),
                    "key": operation.get("key"),
                    "value": operation.get("value"),
                    "window_centre": operation.get("window_centre"),
                }.items()
                if value is not None
            },
            "window_bounds": operation.get("window_bounds"),
            "observation": {},
        },
    )


STATE = runtime.Atspi.StateType


def effective_runtime_source():
    return "".join(
        Path(module.__file__).read_text(encoding="utf-8")
        for module in PROVIDER_MODULES
    )


class FakeStateSet:
    def __init__(self, states):
        self._states = set(states)

    def contains(self, state):
        return state in self._states

    def get_states(self):
        return list(self._states)


class FakeSpan:
    def __init__(self, start, end):
        self.start_offset = start
        self.end_offset = end


class FakeTextIface:
    """Fake Text/EditableText interface.

    `honest=False` reproduces controls whose insert_text returns True without
    changing the character count. `caret=None` models controls that do not
    implement caret queries.
    """

    def __init__(self, text="", honest=True, accept=True, caret=None, selection=None):
        self.text = text
        self.honest = honest
        self.accept = accept
        self.caret = caret
        self.selection = selection
        self.inserts = []
        self.deletes = []
        self.set_calls = []

    def insert(self, offset, payload):
        self.inserts.append((offset, payload))
        if not self.accept:
            return False
        if self.honest:
            self.text = self.text[:offset] + payload + self.text[offset:]
        return True

    def delete(self, start, end):
        self.deletes.append((start, end))
        if self.honest:
            self.text = self.text[:start] + self.text[end:]
        return True

    def set_contents(self, payload):
        self.set_calls.append(payload)
        if not self.accept:
            return False
        if self.honest:
            self.text = payload
        return True


class FakeExtents:
    def __init__(self, x, y, width, height):
        self.x = x
        self.y = y
        self.width = width
        self.height = height


class FakeComponent:
    def __init__(self, window=None, grabs=False, extents=None):
        self.window = window
        self.grabs = grabs
        self.extents = extents
        self.grab_calls = 0

    def grab_focus(self):
        self.grab_calls += 1
        if not self.grabs:
            return False
        if self.window is not None:
            self.window.states.add(STATE.ACTIVE)
        return True


class FakeNode:
    def __init__(
        self,
        states=(),
        text=None,
        editable=True,
        children=(),
        component=None,
        extents=None,
        value=None,
    ):
        self.states = set(states)
        self._text = text
        self._editable = editable
        self.children = list(children)
        self.component = component
        self.extents = extents
        self.value = value

    # --- AT-SPI Accessible surface used by the provider ---
    def get_state_set(self):
        return FakeStateSet(self.states)

    def get_child_count(self):
        return len(self.children)

    def get_child_at_index(self, index):
        return self.children[index]

    def get_text_iface(self):
        return self._text

    def get_hypertext_iface(self):
        # Tests set `links` on a node to model AT-SPI Hypertext embeds.
        return self if getattr(self, "links", None) is not None else None

    def get_editable_text_iface(self):
        return self._text if (self._text is not None and self._editable) else None

    def get_component_iface(self):
        return self.component

    def get_value_iface(self):
        return self.value

    def get_name(self):
        return ""

    def get_role_name(self):
        # Real AT-SPI nodes always implement this. Without it node_role()
        # raises AttributeError during attribute lookup, which safe() cannot
        # catch because it only wraps the call.
        return ""


class FakeAtspiText:
    @staticmethod
    def get_character_count(iface):
        return len(iface.text)

    @staticmethod
    def get_text(iface, start, end):
        # AT-SPI reads to the end of the text when end is -1.
        return iface.text[start:] if end == -1 else iface.text[start:end]

    @staticmethod
    def get_caret_offset(iface):
        if iface.caret is None:
            raise RuntimeError("caret not supported")
        return iface.caret

    @staticmethod
    def get_n_selections(iface):
        return 1 if iface.selection else 0

    @staticmethod
    def get_selection(iface, index):
        start, end = iface.selection
        return FakeSpan(start, end)


class FakeAtspiEditableText:
    @staticmethod
    def insert_text(iface, offset, payload, length):
        return iface.insert(offset, payload)

    @staticmethod
    def delete_text(iface, start, end):
        return iface.delete(start, end)

    @staticmethod
    def set_text_contents(iface, payload):
        return iface.set_contents(payload)


class FakeAtspiComponent:
    @staticmethod
    def grab_focus(component):
        return component.grab_focus()

    @staticmethod
    def get_extents(component, coord_type):
        return component.extents


class FakeValueIface:
    def __init__(self, value, honest=True):
        self.current = float(value)
        self.honest = honest
        self.set_calls = []


class FakeAtspiValue:
    @staticmethod
    def get_current_value(iface):
        return iface.current

    @staticmethod
    def set_current_value(iface, value):
        iface.set_calls.append(value)
        if iface.honest:
            iface.current = float(value)
        return True


class FakeCoordType:
    SCREEN = "screen"


RELATION = runtime.Atspi.RelationType


class FakeAtspiHypertext:
    @staticmethod
    def get_n_links(iface):
        return len(iface.links)

    @staticmethod
    def get_link(iface, index):
        return iface.links[index]


class FakeAtspiHyperlink:
    @staticmethod
    def get_object(link, index):
        return link


class FakeAtspi:
    """Keep the real StateType enum; replace only calls that reach the a11y bus."""

    StateType = STATE
    # Like StateType, RelationType stays the real enum: labelled_by_name()
    # compares against it, and a fake value would test a nonexistent branch.
    RelationType = RELATION
    CoordType = FakeCoordType
    Text = FakeAtspiText
    EditableText = FakeAtspiEditableText
    Component = FakeAtspiComponent
    Value = FakeAtspiValue
    Hypertext = FakeAtspiHypertext
    Hyperlink = FakeAtspiHyperlink


class AtspiPatchedTestCase(unittest.TestCase):
    def setUp(self):
        self._real_atspi = runtime.Atspi
        runtime.Atspi = FakeAtspi
        self.addCleanup(self._restore)
        # The provider clears its per-operation native cache at the start of
        # each operation; tests that call helpers directly start clean too.
        runtime._x11_window_inventory_cache.clear()
        self.addCleanup(runtime._x11_window_inventory_cache.clear)

    def _restore(self):
        runtime.Atspi = self._real_atspi


class _LyingNode(FakeNode):
    """A node whose reported child count differs from its real children.

    Real toolkits do this: a Calc sheet reports about 2.1 billion children
    because its accessible range is the whole sheet, and the Nautilus sidebar
    declares itself self-managed with only 12. Guards must rely on the
    reported count alone, since the real count is what they must not query.
    """

    def __init__(self, reported, **kwargs):
        super().__init__(**kwargs)
        self.reported = reported

    def get_child_count(self):
        return self.reported


class _TreeNode(FakeNode):
    """A node complete enough for tree rendering.

    FakeNode covers only the accessibility interface getters, while
    record_for() also reads role, toolkit, accessible id, attributes and more.
    """

    def __init__(self, role="panel", name="", x=0, y=0, w=10, h=10, kids=(),
                 description=""):
        super().__init__(
            states=(STATE.SHOWING, STATE.VISIBLE),
            children=kids,
            component=FakeComponent(extents=FakeExtents(x, y, w, h)),
        )
        self.role = role
        self.title = name
        self.description = description

    def get_role_name(self):
        return self.role

    def get_name(self):
        return self.title

    def get_description(self):
        return self.description

    def get_toolkit_name(self):
        return "gtk"

    def get_accessible_id(self):
        return ""

    def get_action_iface(self):
        return None

    def get_n_actions(self):
        return 0

    def get_table_iface(self):
        return None

    def get_attributes(self):
        return {}

    def get_process_id(self):
        return 1


class _ProbeFailureNode(_TreeNode):
    def __init__(self, failing_field, **kwargs):
        super().__init__(**kwargs)
        self.failing_field = failing_field

    def _maybe_fail(self, field):
        if self.failing_field == field:
            raise RuntimeError("provider {} probe failed".format(field))

    def get_component_iface(self):
        self._maybe_fail("component")
        return super().get_component_iface()

    def get_role_name(self):
        self._maybe_fail("role")
        return super().get_role_name()

    def get_name(self):
        self._maybe_fail("name")
        return super().get_name()

    def get_description(self):
        self._maybe_fail("description")
        return super().get_description()

    def get_child_count(self):
        self._maybe_fail("child_count")
        return super().get_child_count()


class _FakeClock:
    """Fake clock whose sleep advances time, keeping tests deterministic and fast."""

    def __init__(self):
        self.now = 0.0

    def monotonic(self):
        return self.now

    def sleep(self, seconds):
        self.now += seconds


class _WindowNode(_TreeNode):
    def __init__(self, role, name, active=False, modal=False):
        super().__init__(role=role, name=name)
        if active:
            self.states.add(STATE.ACTIVE)
        if modal:
            self.states.add(STATE.MODAL)


class _ScriptedApp(_TreeNode):
    """Application node whose top-level windows change as the fake clock advances.

    timeline is [(start_time, [windows...]), ...] in ascending order; reads use
    the last entry that has started, making window transitions deterministic.
    """

    def __init__(self, clock, timeline):
        super().__init__(role="application", name="app")
        self.clock = clock
        self.timeline = timeline

    def _windows(self):
        current = []
        for at, windows in self.timeline:
            if self.clock.now >= at:
                current = windows
        return current

    def get_child_count(self):
        return len(self._windows())

    def get_child_at_index(self, index):
        return self._windows()[index]


class _NoSleep:
    """Stand-in time module: sleep returns at once and the clock stays at zero."""

    @staticmethod
    def sleep(_seconds):
        return None

    @staticmethod
    def time():
        return 0.0

    @staticmethod
    def monotonic():
        return 0.0


class _FakeRelation:
    def __init__(self, kind, targets):
        self.kind = kind
        self.targets = list(targets)

    def get_relation_type(self):
        return self.kind

    def get_n_targets(self):
        return len(self.targets)

    def get_target(self, index):
        return self.targets[index]


class _NamedNode(FakeNode):
    """Fake node with a name and optional LABELLED_BY relations."""

    def __init__(self, name="", role="", relations=(), **kwargs):  # noqa: D107
        super().__init__(**kwargs)
        self._name = name
        self._role = role
        self._relations = list(relations)

    def get_name(self):
        return self._name

    def get_role_name(self):
        return self._role

    def get_relation_set(self):
        return self._relations

    def get_description(self):
        return getattr(self, "_description", "")

    def get_attributes(self):
        placeholder = getattr(self, "_placeholder", "")
        return {"placeholder-text": placeholder} if placeholder else {}


__all__ = [name for name in globals() if not name.startswith("__")]
