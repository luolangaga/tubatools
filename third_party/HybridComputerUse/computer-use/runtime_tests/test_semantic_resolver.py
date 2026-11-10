#!/usr/bin/env python3
"""Linux provider semantic target and continuity resolver tests."""

from runtime_test_support import *  # noqa: F401,F403
from runtime_tests.test_semantic_rebinding import SemanticRebindingCases
from runtime_tests.test_provider_bound_resolution import ProviderBoundResolutionCases


class SemanticRuntimeResolutionTests(ProviderBoundResolutionCases, SemanticRebindingCases, AtspiPatchedTestCase):
    def setUp(self):
        super().setUp()
        self.nodes_by_path = {}
        self.windows = []
        self._render = mock.patch.object(
            runtime,
            "render_tree",
            side_effect=self._render_tree,
        )
        self._render.start()
        self.addCleanup(self._render.stop)
        self._resolve = mock.patch.object(
            runtime,
            "find_element_at_snapshot_path_with_bounds",
            side_effect=lambda _app, record, _bounds: self.nodes_by_path.get(
                tuple(record.get("runtimeId") or [])),
        )
        self._resolve.start()
        self.addCleanup(self._resolve.stop)
        self._windows = mock.patch.object(
            runtime,
            "app_windows",
            side_effect=lambda _app: self.windows,
        )
        self._windows.start()
        self.addCleanup(self._windows.stop)
        self._extents = mock.patch.object(
            runtime,
            "extents",
            return_value=runtime.frame(0, 0, 100, 100),
        )
        self._extents.start()
        self.addCleanup(self._extents.stop)
        # No native window matches these fake windows: every handle is weak.
        self._native = mock.patch.object(
            runtime,
            "strong_window_instance_handle_inventory",
            return_value=runtime.InventoryResult([], complete=True),
        )
        self._native.start()
        self.addCleanup(self._native.stop)

    @staticmethod
    def _record_for(node, index, path):
        return {
            "index": index,
            "runtimeId": list(path),
            "automationId": "",
            "name": runtime.limit_text(
                runtime.effective_name(node),
                text_limit=runtime.DEFAULT_TEXT_LIMIT,
            ),
            "controlType": runtime.node_role(node),
            "localizedControlType": runtime.node_role(node),
            "className": "",
            "value": "",
            "nativeWindowHandle": 0,
            "frame": None,
            "actions": list(getattr(node, "_actions", [])),
            "states": str(getattr(node, "_states_text", "")),
            "description": str(getattr(node, "_description", "")),
            "placeholder": str(getattr(node, "_placeholder", "")),
            "textAttributes": None,
        }

    def _render_tree(self, root, _bounds, root_path, **kwargs):
        records = []
        indexer = kwargs.get("indexer")

        def visit(node, path):
            index = (
                indexer.index_for(
                    path, runtime.node_role(node), runtime.effective_name(node))
                if indexer is not None
                else len(records)
            )
            record = self._record_for(node, index, path)
            records.append(record)
            self.nodes_by_path[tuple(path)] = node
            for child_index, child in enumerate(node.children):
                visit(child, path + [child_index])

        visit(root, list(root_path))
        return records, [], []

    @staticmethod
    def _node(
        name, role, description="", actions=(), states="", atspi_states=()
    ):
        node = _NamedNode(name=name, role=role, states=atspi_states)
        node._description = description
        node._actions = list(actions)
        node._states_text = states
        return node

    @staticmethod
    def _provider(node, path, bus=":1.42", pid=4242):
        node.path = path
        node.app = type("ProviderApp", (), {"bus_name": bus})()
        node.get_process_id = lambda: pid
        return node

    def test_unique_target_resolves_with_current_path_and_stable_index(self):
        save = self._node("Save", "push button", actions=("click",))
        window = self._node("Editor", "frame")
        window.children = [save]
        self.windows = [(3, window)]
        target = {
            "selector": 'push button "Save" [a=click]',
            "role": "push button",
            "name": "Save",
            "hasName": True,
            "predicates": [{"kind": "action", "value": "click"}],
        }

        node, record, note, matched_window, _matched_bounds = (
            runtime.resolve_semantic_target(
            None,
            3,
            window,
            runtime.frame(0, 0, 100, 100),
            target,
            {
                "3.0": {
                    "index": 17,
                    "role": "push button",
                    "name": "Save",
                }
            },
            {},
        ))

        self.assertIs(node, save)
        self.assertIs(matched_window, window)
        self.assertEqual(record["runtimeId"], [3, 0])
        self.assertEqual(record["index"], 17)
        self.assertIn("fresh=true unique=true", note)

    def test_live_action_resolution_never_uses_working_tree_pruning(self):
        save = self._node("Save", "push button", actions=("click",))
        window = self._node("Editor", "frame")
        window.children = [save]
        self.windows = [(0, window)]

        def canonical_only(root, _bounds, root_path, **kwargs):
            self.assertFalse(kwargs.get("prune"))
            record = self._record_for(save, 7, list(root_path) + [0])
            self.nodes_by_path[tuple(record["runtimeId"])] = save
            return [record], [], []

        with mock.patch.object(
            runtime, "render_tree", side_effect=canonical_only
        ):
            node, _record, _note, _window, _bounds = (
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": 'push button "Save" [a=click]',
                        "role": "push button",
                        "name": "Save",
                        "hasName": True,
                        "predicates": [{
                            "kind": "action",
                            "value": "click",
                        }],
                    },
                    None,
                    {},
                )
            )
        self.assertIs(node, save)

    def test_name_match_outranks_description_fallback(self):
        by_description = self._node(
            "", "push button", description="Save")
        by_name = self._node("Save", "push button")
        window = self._node("Editor", "frame")
        window.children = [by_description, by_name]
        self.windows = [(0, window)]

        node, _record, _note, _window, _bounds = runtime.resolve_semantic_target(
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

        self.assertIs(node, by_name)

    def test_working_tree_folded_name_resolves_live_cr_lf_and_tab(self):
        save = self._node("Save\r\n\tAs", "push button")
        window = self._node("Editor", "frame")
        window.children = [save]
        self.windows = [(0, window)]

        node, _record, _note, _window, _bounds = (
            runtime.resolve_semantic_target(
                None,
                0,
                window,
                runtime.frame(0, 0, 100, 100),
                {
                    "selector": 'push button "Save As"',
                    "role": "push button",
                    "name": "Save As",
                    "hasName": True,
                },
                None,
                {},
            )
        )

        self.assertIs(node, save)

    def test_working_tree_folded_name_resolves_live_description_fallback(self):
        operations = self._node(
            "",
            "push button",
            description="Show\r\n\toperations",
        )
        window = self._node("Editor", "frame")
        window.children = [operations]
        self.windows = [(0, window)]

        node, _record, _note, _window, _bounds = (
            runtime.resolve_semantic_target(
                None,
                0,
                window,
                runtime.frame(0, 0, 100, 100),
                {
                    "selector": 'push button "Show operations"',
                    "role": "push button",
                    "name": "Show operations",
                    "hasName": True,
                },
                None,
                {},
            )
        )

        self.assertIs(node, operations)

    def test_names_differing_only_by_folded_whitespace_are_ambiguous(self):
        window = self._node("Editor", "frame")
        window.children = [
            self._node("Open\r\nFile", "push button"),
            self._node("Open\tFile", "push button"),
        ]
        self.windows = [(0, window)]

        with self.assertRaises(runtime.OperationFailure) as raised:
            runtime.resolve_semantic_target(
                None,
                0,
                window,
                runtime.frame(0, 0, 100, 100),
                {
                    "selector": 'push button "Open File"',
                    "role": "push button",
                    "name": "Open File",
                    "hasName": True,
                },
                None,
                {},
            )

        self.assertEqual(
            raised.exception.failure_code, "semantic_target_ambiguous")
        self.assertFalse(raised.exception.input_events_emitted)

    def test_description_and_placeholder_predicates_fold_whitespace(self):
        search = self._node(
            "Search",
            "entry",
            description="Search\r\n\tcurrent document",
        )
        search._placeholder = "Type\r\n\tquery"
        window = self._node("Editor", "frame")
        window.children = [search]
        self.windows = [(0, window)]

        node, _record, _note, _window, _bounds = (
            runtime.resolve_semantic_target(
                None,
                0,
                window,
                runtime.frame(0, 0, 100, 100),
                {
                    "selector": (
                        'entry "Search" [desc="Search current document"] '
                        '[placeholder="Type query"]'
                    ),
                    "role": "entry",
                    "name": "Search",
                    "hasName": True,
                    "predicates": [
                        {
                            "kind": "desc",
                            "value": "Search current document",
                        },
                        {
                            "kind": "placeholder",
                            "value": "Type query",
                        },
                    ],
                },
                None,
                {},
            )
        )

        self.assertIs(node, search)

    def test_missing_target_fails_before_input(self):
        window = self._node("Editor", "frame")
        self.windows = [(0, window)]
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
            raised.exception.failure_code, "semantic_target_not_found")
        self.assertEqual(raised.exception.failure_phase, "precondition")
        self.assertFalse(raised.exception.input_events_emitted)

    def test_any_provider_omission_prevents_not_found_claim(self):
        window = self._node("Editor", "frame")
        self.windows = [(0, window)]
        with mock.patch.object(
            runtime,
            "render_tree",
            return_value=(
                [],
                [],
                [{
                    "kind": "provider_omission",
                    "reason": "visible_cell_budget",
                    "count": 1,
                }],
            ),
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.resolve_semantic_target(
                    None,
                    0,
                    window,
                    runtime.frame(0, 0, 100, 100),
                    {
                        "selector": 'push button "Save" [a=click]',
                        "role": "push button",
                        "name": "Save",
                        "hasName": True,
                        "predicates": [{
                            "kind": "action",
                            "value": "click",
                        }],
                    },
                    None,
                    {},
                )

        self.assertEqual(
            raised.exception.failure_code, "observation_incomplete"
        )
        self.assertFalse(raised.exception.input_events_emitted)

    def test_semantic_scan_does_not_render_full_records_for_nonmatches(self):
        target = self._node("Save", "push button")
        window = self._node("Editor", "frame")
        window.children = [
            self._node("Paragraph {}".format(index), "paragraph")
            for index in range(250)
        ] + [target]
        self.windows = [(0, window)]

        with mock.patch.object(
            runtime,
            "render_tree",
            side_effect=AssertionError("semantic fallback used full render"),
        ), mock.patch.object(
            runtime,
            "record_for",
            side_effect=AssertionError("semantic fallback built full records"),
        ) as record_for:
            node, _record, _note, _window, _bounds = (
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
            )
        self.assertIs(node, target)
        record_for.assert_not_called()

    def test_window_scoped_target_ignores_duplicate_in_sibling_window(self):
        app = FakeNode()
        app.get_process_id = lambda: 0
        main_save = self._node("Save", "push button")
        dialog_save = self._node("Save", "push button")
        main = self._node(
            "Editor", "frame", atspi_states=(STATE.SHOWING,))
        dialog = self._node(
            "Preferences", "dialog", atspi_states=(STATE.SHOWING,))
        main.children = [main_save]
        dialog.children = [dialog_save]
        self.windows = [(0, main), (1, dialog)]

        node, record, note, matched_window, _bounds = (
            runtime.resolve_semantic_target(
                app,
                0,
                main,
                runtime.frame(0, 0, 100, 100),
                {
                    "selector": 'push button "Save"',
                    "windowHandle": "pid0/win1/dialog",
                    "role": "push button",
                    "name": "Save",
                    "hasName": True,
                },
                None,
                {},
            )
        )

        self.assertIs(node, dialog_save)
        self.assertIs(matched_window, dialog)
        self.assertEqual(record["runtimeId"], [1, 0])
        self.assertIn("window_scope=verified", note)
        self.assertNotIn("pid0/win1/dialog", note)
        self.assertIn("1 visible app-owned window(s)", note)

    def test_unreadable_native_inventory_is_not_a_vanished_window(self):
        # A wmctrl failure used to give an empty handle map, so the stored w2:
        # handle matched no window and the target was reported stale.
        self._native.stop()
        app = FakeNode()
        app.get_process_id = lambda: 0
        main = self._node(
            "Editor", "frame", atspi_states=(STATE.SHOWING,))
        self.windows = [(0, main)]
        with mock.patch.object(
            runtime,
            "strong_window_instance_handle_inventory",
            return_value=runtime.InventoryResult(
                complete=False, errors=["wmctrl timed out"]
            ),
        ), self.assertRaises(runtime.OperationFailure) as raised:
            runtime.resolve_semantic_target(
                app,
                0,
                main,
                runtime.frame(0, 0, 100, 100),
                {
                    "selector": 'push button "Save"',
                    "windowHandle": "w2:" + "0" * 32,
                    "role": "push button",
                    "name": "Save",
                    "hasName": True,
                },
                None,
                {},
            )
        self._native.start()

        self.assertEqual(raised.exception.failure_code, "observation_incomplete")
        self.assertFalse(raised.exception.input_events_emitted)

    def test_window_handles_are_computed_once_per_operation(self):
        app = FakeNode()
        app.get_process_id = lambda: 0
        window = self._node("Editor", "frame")
        with mock.patch.object(
            runtime,
            "strong_window_instance_handle_inventory",
            return_value=runtime.InventoryResult(
                [(0, "w2:" + "a" * 32)], complete=True
            ),
        ) as handles:
            first = runtime.window_instance_handle(app, 0, window)
            second = runtime.window_instance_handle(app, 0, window)
            runtime._x11_window_inventory_cache.clear()
            third = runtime.window_instance_handle(app, 0, window)

        self.assertEqual(first, "w2:" + "a" * 32)
        self.assertEqual(second, first)
        self.assertEqual(third, first)
        self.assertEqual(handles.call_count, 2)

    def test_window_scoped_target_fails_if_origin_window_disappeared(self):
        app = FakeNode()
        app.get_process_id = lambda: 0
        main = self._node(
            "Editor", "frame", atspi_states=(STATE.SHOWING,))
        self.windows = [(0, main)]

        with self.assertRaises(runtime.OperationFailure) as raised:
            runtime.resolve_semantic_target(
                app,
                0,
                main,
                runtime.frame(0, 0, 100, 100),
                {
                    "selector": 'push button "Save"',
                    "windowHandle": "pid0/win1/dialog",
                    "role": "push button",
                    "name": "Save",
                    "hasName": True,
                },
                None,
                {},
            )

        self.assertEqual(raised.exception.failure_code, "stale_element_ref")
        self.assertEqual(raised.exception.failure_phase, "precondition")
        self.assertFalse(raised.exception.input_events_emitted)


class SemanticTargetDecodeTests(unittest.TestCase):
    def test_target_without_role_name_or_predicates_is_invalid(self):
        with self.assertRaises(runtime.OperationFailure) as raised:
            element_resolver._decode_semantic_target({"selector": "  "})
        self.assertEqual(raised.exception.failure_code, "semantic_target_invalid")
        self.assertIn("reason=empty", str(raised.exception))
        self.assertFalse(raised.exception.input_events_emitted)

    def test_provider_bound_target_needs_the_full_provider_identity(self):
        with self.assertRaises(runtime.OperationFailure) as raised:
            element_resolver._decode_semantic_target({
                "role": "push button",
                "locatorKind": "Provider_Bound",
                "providerBusName": ":1.4",
                "providerObjectPath": "/obj/7",
            })
        self.assertIn(
            "provider_bound_identity_incomplete", str(raised.exception))

    def test_provider_requests_need_all_three_identity_parts(self):
        locator = element_resolver._decode_semantic_target({
            "role": "push button",
            "providerBusName": ":1.4",
            "providerObjectPath": "/obj/7",
            "processGeneration": "pid:4:start:9",
            "providerWindowBusName": ":1.4",
            "providerWindowObjectPath": "/win/1",
        })
        self.assertEqual(locator.locator_kind, "semantic")
        self.assertTrue(locator.provider_requested)
        self.assertFalse(locator.provider_window_requested)


class UniqueSemanticMatchTests(unittest.TestCase):
    def test_name_matches_outrank_description_matches(self):
        by_name = [({"name": "Save"}, "w", "b", None)]
        by_description = [({"name": "x"}, "w", "b", None)] * 3
        self.assertIs(
            element_resolver._unique_semantic_match(
                by_name, by_description, False, "Save", 1),
            by_name[0],
        )

    def test_unique_match_in_an_incomplete_scan_is_not_proof(self):
        with self.assertRaises(runtime.OperationFailure) as raised:
            element_resolver._unique_semantic_match(
                [({"name": "Save"}, "w", "b", None)], [], True, "Save", 1)
        self.assertEqual(raised.exception.failure_code, "observation_incomplete")

    def test_ambiguity_reports_at_most_five_candidates(self):
        matches = [({"name": "Save"}, "w", "b", {"ref": i}) for i in range(7)]
        with self.assertRaises(runtime.OperationFailure) as raised:
            element_resolver._unique_semantic_match(
                matches, [], False, "Save", 2)
        self.assertEqual(
            raised.exception.failure_code, "semantic_target_ambiguous")
        self.assertEqual(raised.exception.candidate_count, 7)
        self.assertEqual(len(raised.exception.candidates), 5)
