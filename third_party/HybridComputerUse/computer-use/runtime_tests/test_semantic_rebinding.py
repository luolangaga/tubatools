#!/usr/bin/env python3
"""Semantic resolver cases for provider window churn, weak window handles, and current-revision rebinding."""

from runtime_test_support import *  # noqa: F401,F403


class SemanticRebindingCases:
    """Cases of SemanticRuntimeResolutionTests; mixed into it so test ids keep its name."""

    def test_semantic_ref_does_not_recover_after_provider_window_churn(self):
        save = self._node("Save", "push button", actions=("click",))
        replacement = self._node("Preferences", "dialog")
        replacement.children = [save]
        self.windows = [(1, replacement)]
        target = {
            "selector": 'push button "Save"',
            "locatorKind": "semantic",
            "windowHandle": "w2:stable-window",
            "role": "push button",
            "name": "Save",
            "hasName": True,
            "providerBusName": ":1.42",
            "providerObjectPath": "/old/window",
            "processGeneration": "99123",
            "providerWindowBusName": ":1.42",
            "providerWindowObjectPath": "/old/window",
            "providerWindowProcessGeneration": "99123",
        }
        with mock.patch.object(
            runtime, "provider_bus_name", return_value=":1.42"
        ), mock.patch.object(
            runtime, "provider_object_path", return_value="/new/window"
        ), mock.patch.object(
            runtime, "node_pid", return_value=4242
        ), mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "window_instance_handle",
            return_value="w2:stable-window",
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    1,
                    replacement,
                    runtime.frame(0, 0, 100, 100),
                    target,
                    None,
                    {},
                )
        self.assertEqual(
            raised.exception.failure_code, "observation_incomplete")
        self.assertFalse(raised.exception.input_events_emitted)

    def test_exact_provider_element_survives_top_level_wrapper_rebuild(self):
        save = self._provider(
            self._node("Save", "push button", actions=("click",)),
            "/element/save",
        )
        replacement = self._provider(
            self._node("Editor", "frame"),
            "/window/rebuilt",
        )
        replacement.children = [save]
        self.windows = [(1, replacement)]
        target = {
            "selector": 'push button "Save"',
            "locatorKind": "semantic",
            "windowHandle": "w2:stable-window",
            "role": "push button",
            "name": "Save",
            "hasName": True,
            "providerBusName": ":1.42",
            "providerObjectPath": "/element/save",
            "processGeneration": "99123",
            "providerWindowBusName": ":1.17",
            "providerWindowObjectPath": "/window/old",
            "providerWindowProcessGeneration": "99123",
        }
        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "window_instance_handle",
            return_value="w2:stable-window",
        ), mock.patch.object(
            runtime,
            "record_for",
            side_effect=lambda node, index, path, _bounds, **_kwargs: (
                self._record_for(node, index, path)
            ),
        ):
            node, _record, _note, matched_window, _bounds = (
                runtime.resolve_semantic_target(
                    None,
                    1,
                    replacement,
                    runtime.frame(0, 0, 100, 100),
                    target,
                    None,
                    {},
                )
            )

        self.assertIs(node, save)
        self.assertIs(matched_window, replacement)

    def test_exact_provider_element_recovers_without_strong_window_handle(self):
        save = self._provider(
            self._node("Save", "push button", actions=("click",)),
            "/element/save",
        )
        replacement = self._provider(
            self._node("Editor", "dialog"),
            "/window/rebuilt",
        )
        replacement.children = [save]
        self.windows = [(4, replacement)]
        target = {
            "selector": 'push button "Save"',
            "locatorKind": "semantic",
            "windowHandle": "pid4242/win1/dialog",
            "role": "push button",
            "name": "Save",
            "hasName": True,
            "providerBusName": ":1.42",
            "providerObjectPath": "/element/save",
            "processGeneration": "99123",
            "providerWindowBusName": ":1.42",
            "providerWindowObjectPath": "/window/old",
            "providerWindowProcessGeneration": "99123",
        }
        original_scan = runtime.scan_provider_identity
        with mock.patch.object(
            runtime,
            "scan_provider_identity",
            wraps=original_scan,
        ) as provider_scan, mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "window_instance_handle",
            return_value="pid4242/win4/dialog",
        ), mock.patch.object(
            runtime,
            "record_for",
            side_effect=lambda node, index, path, _bounds, **_kwargs: (
                self._record_for(node, index, path)
            ),
        ):
            node, _record, _note, matched_window, _bounds = (
                runtime.resolve_semantic_target(
                    None,
                    4,
                    replacement,
                    runtime.frame(0, 0, 100, 100),
                    target,
                    None,
                    {},
                )
            )

        self.assertIs(node, save)
        self.assertIs(matched_window, replacement)
        self.assertEqual(provider_scan.call_count, 1)

    def test_provider_window_churn_does_not_rebind_across_windows(self):
        save = self._provider(
            self._node("Save", "push button", actions=("click",)),
            "/new/save",
        )
        sibling = self._provider(
            self._node("Other", "dialog"),
            "/new/window",
        )
        sibling.children = [save]
        self.windows = [(1, sibling)]
        target = {
            "selector": 'push button "Save"',
            "locatorKind": "semantic",
            "windowHandle": "w2:origin",
            "role": "push button",
            "name": "Save",
            "hasName": True,
            "providerBusName": ":1.42",
            "providerObjectPath": "/old/save",
            "processGeneration": "99123",
            "providerWindowBusName": ":1.42",
            "providerWindowObjectPath": "/old/window",
            "providerWindowProcessGeneration": "99123",
        }
        with mock.patch.object(
            runtime, "provider_bus_name", return_value=":1.42"
        ), mock.patch.object(
            runtime, "provider_object_path", return_value="/new/window"
        ), mock.patch.object(
            runtime, "node_pid", return_value=4242
        ), mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime, "window_instance_handle", return_value="w2:sibling"
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    1,
                    sibling,
                    runtime.frame(0, 0, 100, 100),
                    target,
                    None,
                    {},
                )
        self.assertEqual(
            raised.exception.failure_code, "stale_element_ref")
        self.assertFalse(raised.exception.input_events_emitted)

    def test_weak_window_handle_does_not_enable_semantic_rebinding(self):
        save = self._provider(
            self._node("Save", "push button", actions=("click",)),
            "/new/save",
        )
        replacement = self._provider(
            self._node("Preferences", "dialog"),
            "/new/window",
        )
        replacement.children = [save]
        self.windows = [(4, replacement)]
        target = {
            "selector": 'push button "Save"',
            "locatorKind": "semantic",
            "windowHandle": "pid4242/win1/dialog",
            "role": "push button",
            "name": "Save",
            "hasName": True,
            "providerBusName": ":1.42",
            "providerObjectPath": "/old/save",
            "processGeneration": "99123",
            "providerWindowBusName": ":1.42",
            "providerWindowObjectPath": "/old/window",
            "providerWindowProcessGeneration": "99123",
            "providerWindowRole": "dialog",
            "providerWindowName": "Preferences",
        }
        with mock.patch.object(
            runtime, "provider_bus_name", return_value=":1.42"
        ), mock.patch.object(
            runtime, "provider_object_path", return_value="/new/window"
        ), mock.patch.object(
            runtime, "node_pid", return_value=4242
        ), mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "window_instance_handle",
            return_value="pid4242/win4/dialog",
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    4,
                    replacement,
                    runtime.frame(0, 0, 100, 100),
                    target,
                    None,
                    {},
                )
        self.assertEqual(
            raised.exception.failure_code, "stale_element_ref")
        self.assertFalse(raised.exception.input_events_emitted)

    def test_weak_window_semantics_fail_closed_when_ambiguous(self):
        first = self._provider(
            self._node("Preferences", "dialog"), "/new/window/first"
        )
        second = self._provider(
            self._node("Preferences", "dialog"), "/new/window/second"
        )
        first.children = [
            self._provider(
                self._node("Save", "push button"), "/new/save/first"
            )
        ]
        second.children = [
            self._provider(
                self._node("Save", "push button"), "/new/save/second"
            )
        ]
        self.windows = [(4, first), (7, second)]
        target = {
            "selector": 'push button "Save"',
            "windowHandle": "pid4242/win1/dialog",
            "role": "push button",
            "name": "Save",
            "hasName": True,
            "providerBusName": ":1.42",
            "providerObjectPath": "/old/save",
            "processGeneration": "99123",
            "providerWindowBusName": ":1.42",
            "providerWindowObjectPath": "/old/window",
            "providerWindowProcessGeneration": "99123",
            "providerWindowRole": "dialog",
            "providerWindowName": "Preferences",
        }
        with mock.patch.object(
            runtime, "provider_bus_name", return_value=":1.42"
        ), mock.patch.object(
            runtime, "provider_object_path", return_value="/new/window"
        ), mock.patch.object(
            runtime, "node_pid", return_value=4242
        ), mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "window_instance_handle",
            side_effect=lambda _app, index, _window: (
                "pid4242/win{}/dialog".format(index)
            ),
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    4,
                    first,
                    runtime.frame(0, 0, 100, 100),
                    target,
                    None,
                    {},
                )
        self.assertEqual(
            raised.exception.failure_code, "stale_element_ref")
        self.assertFalse(raised.exception.input_events_emitted)

    def test_ambiguous_target_fails_before_input(self):
        window = self._node("Editor", "frame")
        window.children = [
            self._node("OK", "push button"),
            self._node("OK", "push button"),
        ]
        self.windows = [(0, window)]
        with self.assertRaises(runtime.OperationFailure) as raised:
            runtime.resolve_semantic_target(
                None,
                0,
                window,
                runtime.frame(0, 0, 100, 100),
                {
                    "selector": 'push button "OK"',
                    "role": "push button",
                    "name": "OK",
                    "hasName": True,
                },
                None,
                {},
            )

        self.assertEqual(
            raised.exception.failure_code, "semantic_target_ambiguous")
        self.assertEqual(raised.exception.failure_phase, "precondition")
        self.assertFalse(raised.exception.input_events_emitted)

    def test_provider_bound_ref_never_falls_back_to_same_name_peer(self):
        replacement = self._provider(
            self._node("Format", "menu"), "/new/format"
        )
        window = self._provider(
            self._node("Editor", "frame"), "/window"
        )
        window.children = [replacement]
        self.windows = [(0, window)]

        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(runtime, "record_for") as record_for:
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": 'menu "Format"',
                        "locatorKind": "provider_bound",
                        "role": "menu",
                        "name": "Format",
                        "hasName": True,
                        "providerBusName": ":1.42",
                        "providerObjectPath": "/old/format",
                        "processGeneration": "99123",
                    },
                    None,
                    {},
                )

        self.assertEqual(
            raised.exception.failure_code,
            "stale_element_ref",
        )
        self.assertFalse(raised.exception.input_events_emitted)

    def test_ordinary_semantic_ref_does_not_bind_replacement_provider(self):
        old_path = "/old/ok"
        replacement = self._provider(
            self._node("OK", "push button"), "/new/ok"
        )
        window = self._provider(
            self._node("Question", "dialog"), "/window"
        )
        window.children = [replacement]
        self.windows = [(0, window)]
        target = {
            "selector": 'push button "OK"',
            "locatorKind": "semantic",
            "role": "push button",
            "name": "OK",
            "hasName": True,
            "providerBusName": ":1.42",
            "providerObjectPath": old_path,
            "processGeneration": "99123",
        }
        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    target,
                    None,
                    {},
                )
        self.assertEqual(
            raised.exception.failure_code, "stale_element_ref"
        )
        self.assertIn(
            "provider_identity_replaced", str(raised.exception)
        )
        self.assertFalse(raised.exception.input_events_emitted)

    def test_current_revision_semantic_ref_rebinds_same_path_in_strong_window(self):
        replacement = self._provider(
            self._node("Save", "push button", actions=("click",)),
            "/new/save",
        )
        window = self._provider(
            self._node("Editor", "frame"), "/new/window"
        )
        window.children = [replacement]
        self.windows = [(0, window)]
        target = {
            "selector": 'push button "Save"',
            "locatorKind": "semantic",
            "allowProviderRebind": True,
            "windowHandle": "w2:stable-window",
            "role": "push button",
            "name": "Save",
            "hasName": True,
            "providerBusName": ":1.42",
            "providerObjectPath": "/old/save",
            "processGeneration": "99123",
            "runtimeId": [0, 0],
        }
        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "window_instance_handle",
            return_value="w2:stable-window",
        ):
            node, record, _note, matched_window, _bounds = (
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    target,
                    None,
                    {},
                )
            )
        self.assertIs(node, replacement)
        self.assertIs(matched_window, window)
        self.assertEqual(record["runtimeId"], [0, 0])
        self.assertEqual(record["providerObjectPath"], "/new/save")

    def test_current_revision_semantic_ref_rejects_changed_runtime_path(self):
        replacement = self._provider(
            self._node("Save", "push button", actions=("click",)),
            "/new/save",
        )
        window = self._provider(
            self._node("Editor", "frame"), "/new/window"
        )
        window.children = [
            self._provider(self._node("Spacer", "panel"), "/spacer"),
            replacement,
        ]
        self.windows = [(0, window)]
        target = {
            "selector": 'push button "Save"',
            "locatorKind": "semantic",
            "allowProviderRebind": True,
            "windowHandle": "w2:stable-window",
            "role": "push button",
            "name": "Save",
            "hasName": True,
            "providerBusName": ":1.42",
            "providerObjectPath": "/old/save",
            "processGeneration": "99123",
            "runtimeId": [0, 0],
        }
        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "window_instance_handle",
            return_value="w2:stable-window",
        ):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    target,
                    None,
                    {},
                )
        self.assertEqual(caught.exception.failure_code, "stale_element_ref")
        self.assertFalse(caught.exception.input_events_emitted)

    def test_live_semantic_resolution_rejects_same_path_replacement(self):
        replacement = self._provider(
            self._node("OK", "push button", actions=("click",)), "/new/ok"
        )
        window = self._provider(
            self._node("Question", "dialog"), "/window"
        )
        window.children = [replacement]
        app = self._node("Editor", "application")
        app.children = [window]
        self.windows = [(0, window)]
        target = {
            "selector": 'push button "OK"',
            "locatorKind": "semantic",
            "windowHandle": "w2:question",
            "role": "push button",
            "name": "OK",
            "hasName": True,
            "providerBusName": ":1.42",
            "providerObjectPath": "/old/ok",
            "processGeneration": "99123",
            "runtimeId": [0, 0],
        }

        with mock.patch.object(
            runtime, "app_windows", return_value=[(0, window)]
        ), mock.patch.object(
            runtime, "provider_bus_name", return_value=":1.42"
        ), mock.patch.object(
            runtime, "provider_object_path",
            side_effect=lambda node: node.path,
        ), mock.patch.object(
            runtime, "node_pid", return_value=4242
        ), mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime, "window_instance_handle", return_value="w2:question"
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    app,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    target,
                    None,
                    {},
                )
        self.assertEqual(raised.exception.failure_code, "stale_element_ref")
        self.assertIn("provider_identity_replaced", str(raised.exception))
        self.assertFalse(raised.exception.input_events_emitted)
