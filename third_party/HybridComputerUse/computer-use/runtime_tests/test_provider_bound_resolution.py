#!/usr/bin/env python3
"""Semantic resolver cases for provider-bound identity, duplicate provider records, and incomplete scans."""

from runtime_test_support import *  # noqa: F401,F403


class ProviderBoundResolutionCases:
    """Cases of SemanticRuntimeResolutionTests; mixed into it so test ids keep its name."""

    def test_provider_bound_identity_change_never_reuses_old_ref(self):
        replacement = self._provider(
            self._node("Saved", "toggle button"), "/same/control"
        )
        window = self._provider(
            self._node("Editor", "frame"), "/window"
        )
        window.children = [replacement]
        self.windows = [(0, window)]

        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "record_for",
            side_effect=lambda node, index, path, bounds, text_limit: {
                **self._record_for(node, index, path),
                "providerBusName": ":1.42",
                "providerObjectPath": node.path,
                "processGeneration": "99123",
                "showing": True,
                "visible": True,
            },
        ), mock.patch.object(
            runtime, "render_tree"
        ) as render_tree:
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": 'push button "Save"',
                        "locatorKind": "provider_bound",
                        "role": "push button",
                        "name": "Save",
                        "hasName": True,
                        "providerBusName": ":1.42",
                        "providerObjectPath": "/same/control",
                        "processGeneration": "99123",
                    },
                    None,
                    {},
                )

        self.assertEqual(
            raised.exception.failure_code, "stale_element_ref"
        )
        self.assertIn("identity_changed", str(raised.exception))
        self.assertNotIn("/same/control", str(raised.exception))
        render_tree.assert_not_called()

    def test_provider_bound_record_read_failure_is_incomplete(self):
        save = self._provider(
            self._node("Save", "push button"), "/same/control"
        )
        window = self._provider(
            self._node("Editor", "frame"), "/window"
        )
        window.children = [save]
        self.windows = [(0, window)]

        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime, "record_for", side_effect=RuntimeError("transient")
        ), mock.patch.object(runtime, "render_tree") as render_tree:
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": 'push button "Save"',
                        "locatorKind": "provider_bound",
                        "role": "push button",
                        "name": "Save",
                        "hasName": True,
                        "providerBusName": ":1.42",
                        "providerObjectPath": "/same/control",
                        "processGeneration": "99123",
                    },
                    None,
                    {},
                )

        self.assertEqual(
            raised.exception.failure_code, "observation_incomplete"
        )
        render_tree.assert_not_called()

    def test_provider_bound_match_survives_unrelated_incomplete_branch(self):
        target_node = self._provider(
            self._node("Format", "menu", actions=("click",)),
            "/target/format",
        )
        broken = self._provider(
            self._node("Broken", "group"),
            "/broken",
        )
        window = self._provider(
            self._node("Editor", "frame"),
            "/window",
        )

        def enumerate_children(node, _context):
            if node is window:
                return runtime.InventoryResult(
                    [(0, target_node), (1, broken)],
                    complete=True,
                )
            if node is broken:
                return runtime.InventoryResult(
                    [],
                    complete=False,
                    errors=["unrelated branch failed"],
                )
            return runtime.InventoryResult([], complete=True)

        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime, "enumerate_children", side_effect=enumerate_children
        ), mock.patch.object(
            runtime,
            "record_for",
            side_effect=lambda node, index, path, bounds, text_limit: {
                **self._record_for(node, index, path),
                "providerBusName": ":1.42",
                "providerObjectPath": node.path,
                "processGeneration": "99123",
                "showing": True,
                "visible": True,
            },
        ):
            resolved, record, _note, _window, _bounds = (
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
                        "providerObjectPath": "/target/format",
                        "processGeneration": "99123",
                    },
                    None,
                    {},
                )
            )

        self.assertIs(resolved, target_node)
        self.assertEqual(record["name"], "Format")

    def test_provider_bound_ref_uses_verified_runtime_path_before_tree_scan(self):
        target_node = self._provider(
            self._node("Format", "menu", actions=("click",)),
            "/target/format",
        )
        window = self._provider(
            self._node("Editor", "frame"),
            "/window",
        )
        window.children = [target_node]
        app = self._node("Office", "application")
        app.children = [window]
        self.windows = [(0, window)]

        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "scan_provider_identity",
            side_effect=AssertionError("verified path should avoid a tree scan"),
        ), mock.patch.object(
            runtime,
            "record_for",
            side_effect=lambda node, index, path, bounds, text_limit: {
                **self._record_for(node, index, path),
                "providerBusName": ":1.42",
                "providerObjectPath": node.path,
                "processGeneration": "99123",
                "showing": True,
                "visible": True,
            },
        ):
            resolved, record, _note, _window, _bounds = (
                runtime.resolve_semantic_target(
                    app,
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
                        "providerObjectPath": "/target/format",
                        "processGeneration": "99123",
                        "runtimeId": [0, 0],
                    },
                    None,
                    {},
                )
            )

        self.assertIs(resolved, target_node)
        self.assertEqual(record["name"], "Format")

    def test_description_only_exact_provider_identity_resolves(self):
        description = "Open the contents of your desktop in a folder"
        target_node = self._provider(
            self._node(
                "",
                "list item",
                description=description,
                actions=("click",),
            ),
            "/places/desktop",
        )
        window = self._provider(
            self._node("Insert Image", "dialog"),
            "/window",
        )
        window.children = [target_node]
        app = self._node("Office", "application")
        app.children = [window]
        self.windows = [(0, window)]

        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "scan_provider_identity",
            side_effect=AssertionError("verified path should avoid a tree scan"),
        ), mock.patch.object(
            runtime,
            "record_for",
            side_effect=lambda node, index, path, bounds, text_limit: {
                **self._record_for(node, index, path),
                "providerBusName": ":1.42",
                "providerObjectPath": node.path,
                "processGeneration": "99123",
                "showing": True,
                "visible": True,
            },
        ):
            resolved, record, _note, _window, _bounds = (
                runtime.resolve_semantic_target(
                    app,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": (
                            'list item [desc="Open the contents of your '
                            'desktop in a folder"]'
                        ),
                        "locatorKind": "semantic",
                        "role": "list item",
                        "hasName": False,
                        "predicates": [{
                            "kind": "desc",
                            "value": description,
                        }],
                        "providerBusName": ":1.42",
                        "providerObjectPath": "/places/desktop",
                        "processGeneration": "99123",
                        "runtimeId": [0, 0],
                    },
                    None,
                    {},
                )
            )

        self.assertIs(resolved, target_node)
        self.assertEqual(record["name"], "")
        self.assertEqual(record["description"], description)

    def test_description_only_provider_identity_rejects_changed_description(self):
        target_node = self._provider(
            self._node(
                "",
                "list item",
                description="Open a different folder",
                actions=("click",),
            ),
            "/places/desktop",
        )
        window = self._provider(
            self._node("Insert Image", "dialog"),
            "/window",
        )
        window.children = [target_node]
        app = self._node("Office", "application")
        app.children = [window]
        self.windows = [(0, window)]

        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime,
            "record_for",
            side_effect=lambda node, index, path, bounds, text_limit: {
                **self._record_for(node, index, path),
                "providerBusName": ":1.42",
                "providerObjectPath": node.path,
                "processGeneration": "99123",
                "showing": True,
                "visible": True,
            },
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    app,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": (
                            'list item [desc="Open the contents of your '
                            'desktop in a folder"]'
                        ),
                        "locatorKind": "semantic",
                        "role": "list item",
                        "hasName": False,
                        "predicates": [{
                            "kind": "desc",
                            "value": (
                                "Open the contents of your desktop in a folder"
                            ),
                        }],
                        "providerBusName": ":1.42",
                        "providerObjectPath": "/places/desktop",
                        "processGeneration": "99123",
                        "runtimeId": [0, 0],
                    },
                    None,
                    {},
                )

        self.assertEqual(
            raised.exception.failure_code, "stale_element_ref"
        )
        self.assertFalse(raised.exception.input_events_emitted)

    def test_provider_bound_virtual_cell_uses_table_runtime_path(self):
        cell = self._provider(
            self._node("C2", "table cell", actions=("click",)),
            "/sheet/cell/c2",
        )
        table = self._provider(
            self._node("Sheet", "table"),
            "/sheet/table",
        )
        window = self._provider(
            self._node("Workbook", "frame"),
            "/window",
        )
        window.children = [table]
        app = self._node("Office", "application")
        app.children = [window]
        table.get_table_iface = lambda: object()
        self.windows = [(0, window)]

        table_api = mock.Mock()
        table_api.get_accessible_at.return_value = cell
        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ), mock.patch.object(
            runtime.Atspi, "Table", table_api, create=True
        ), mock.patch.object(
            runtime,
            "scan_provider_identity",
            side_effect=AssertionError("virtual cell should avoid a tree scan"),
        ), mock.patch.object(
            runtime,
            "record_for",
            side_effect=lambda node, index, path, bounds, text_limit: {
                **self._record_for(node, index, path),
                "providerBusName": ":1.42",
                "providerObjectPath": node.path,
                "processGeneration": "99123",
                "showing": True,
                "visible": True,
            },
        ):
            resolved, record, _note, _window, _bounds = (
                runtime.resolve_semantic_target(
                    app,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": 'table cell "C2"',
                        "locatorKind": "provider_bound",
                        "role": "table cell",
                        "providerRoleAliases": ["cell", "table cell"],
                        "name": "C2",
                        "hasName": True,
                        "providerIdentityScope": "table_owner",
                        "providerBusName": ":1.42",
                        "providerObjectPath": "/sheet/table",
                        "processGeneration": "99123",
                        "runtimeId": [0, 0, 1, 2],
                    },
                    None,
                    {},
                )
            )

        self.assertIs(resolved, cell)
        self.assertEqual(record["name"], "C2")
        table_api.get_accessible_at.assert_called_once()

    def test_exact_provider_duplicate_merge_is_order_independent(self):
        base = {
            "index": 1,
            "runtimeId": [0, 1],
            "providerBusName": ":1.42",
            "providerObjectPath": "/same/control",
            "processGeneration": "99123",
            "controlType": "push button",
            "name": "Save",
            "description": "",
            "value": "",
            "placeholder": "",
            "states": "focused",
            "actions": ["click"],
            "showing": True,
            "visible": True,
        }
        alias = {
            **base,
            "index": 2,
            "runtimeId": [0, 2],
            "states": "selected",
            "description": "Save document",
        }

        first = runtime.deduplicate_provider_records(
            [base, alias], "w1"
        )[0]
        second = runtime.deduplicate_provider_records(
            [alias, base], "w1"
        )[0]

        for field in (
            "runtimeId", "runtimeAliases", "name", "description",
            "states", "actions", "showing", "visible",
        ):
            self.assertEqual(first[field], second[field], field)
        self.assertEqual(first["states"], "focused selected")

    def test_exact_provider_duplicate_incompatible_roles_are_unknown(self):
        records = [
            {
                "index": 1,
                "runtimeId": [0, 1],
                "providerBusName": ":1.42",
                "providerObjectPath": "/same/control",
                "processGeneration": "99123",
                "controlType": "push button",
                "name": "Save",
            },
            {
                "index": 2,
                "runtimeId": [0, 2],
                "providerBusName": ":1.42",
                "providerObjectPath": "/same/control",
                "processGeneration": "99123",
                "controlType": "entry",
                "name": "Save",
            },
        ]

        merged = runtime.deduplicate_provider_records(records, "w1")[0]

        self.assertTrue(merged["_providerIdentityConflict"])

    def test_conflicting_provider_duplicate_cannot_prove_unique_target(self):
        first = self._provider(
            self._node("Save", "push button"), "/same/control"
        )
        second = self._provider(
            self._node("Save", "entry"), "/same/control"
        )
        window = self._node("Editor", "frame")
        window.children = [first, second]
        self.windows = [(0, window)]

        with mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": 'push button "Save"',
                        "role": "push button",
                        "name": "Save",
                        "hasName": True,
                    },
                    None,
                    {},
                )

        self.assertEqual(
            raised.exception.failure_code,
            "observation_incomplete",
        )

    def test_exact_provider_aliases_do_not_create_semantic_ambiguity(self):
        first = self._provider(
            self._node("Format", "menu"), "/same/format"
        )
        second = self._provider(
            self._node("Format", "menu"), "/same/format"
        )
        window = self._node("Editor", "frame")
        window.children = [first, second]
        self.windows = [(0, window)]

        with mock.patch.object(
            runtime,
            "window_instance_handle",
            return_value="w2:stable-window",
        ), mock.patch.object(
            runtime, "process_generation", return_value="99123"
        ):
            node, record, _note, _window, _bounds = (
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": 'menu "Format"',
                        "role": "menu",
                        "name": "Format",
                        "hasName": True,
                    },
                    None,
                    {},
                )
            )

        self.assertIs(node, first)
        self.assertEqual(
            record["runtimeAliases"],
            [[0, 0], [0, 1]],
        )

    def test_incomplete_semantic_scan_cannot_prove_unique(self):
        save = self._node("Save", "push button")
        window = self._node("Editor", "frame")
        window.children = [save]
        self.windows = [(0, window)]

        original_enumerate = runtime.enumerate_children

        def incomplete_children(node, label):
            result = original_enumerate(node, label)
            if node is window:
                return runtime.InventoryResult(
                    result.items,
                    complete=False,
                    errors=["provider child omitted"],
                )
            return result

        with mock.patch.object(
            runtime,
            "enumerate_children",
            side_effect=incomplete_children,
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": 'push button "Save"',
                        "role": "push button",
                        "name": "Save",
                        "hasName": True,
                    },
                    None,
                    {},
                )

        self.assertEqual(
            raised.exception.failure_code,
            "observation_incomplete",
        )

    def test_ambiguity_candidates_are_bounded(self):
        window = self._node("Editor", "frame")
        window.children = [
            self._node("Format", "menu")
            for _ in range(8)
        ]
        self.windows = [(0, window)]

        with mock.patch.object(
            runtime,
            "window_instance_handle",
            return_value="w2:stable-window",
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": 'menu "Format"',
                        "role": "menu",
                        "name": "Format",
                        "hasName": True,
                    },
                    None,
                    {},
                )

        self.assertEqual(
            raised.exception.failure_code,
            "semantic_target_ambiguous",
        )
        self.assertEqual(raised.exception.candidate_count, 8)
        self.assertEqual(len(raised.exception.candidates), 5)
        response = runtime.operation_failure_response(raised.exception)
        self.assertEqual(response["candidateCount"], 8)
        self.assertTrue(response["candidatesTruncated"])
        self.assertEqual(len(response["candidates"]), 5)

    def test_unscoped_target_does_not_search_sibling_window(self):
        main = self._node("Editor", "frame")
        save = self._node("Save", "push button")
        dialog = self._node(
            "Preferences",
            "dialog",
            atspi_states=(STATE.SHOWING,),
        )
        dialog.children = [save]
        self.windows = [(0, main), (1, dialog)]

        with self.assertRaises(runtime.OperationFailure) as raised:
            runtime.resolve_semantic_target(
                None,
                0,
                main,
                runtime.frame(0, 0, 100, 100),
                {
                    "selector": 'push button "Save"',
                    "role": "push button",
                    "name": "Save",
                    "hasName": True,
                },
                None,
                {},
            )

        self.assertEqual(
            raised.exception.failure_code, "semantic_target_not_found")
        self.assertEqual(raised.exception.failure_phase, "precondition")
        self.assertFalse(raised.exception.input_events_emitted)
