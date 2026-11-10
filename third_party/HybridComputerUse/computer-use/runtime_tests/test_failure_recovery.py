#!/usr/bin/env python3
"""Operation guard cases for failure facts, bounded window recovery, and expected window identity."""

from runtime_test_support import *  # noqa: F401,F403


class FailureRecoveryCases:
    """Cases of PerformOperationGuardTests; mixed into it so test ids keep its name."""

    def test_operation_failure_response_reports_complete_no_input_facts(self):
        exc = runtime.OperationFailure(
            "stale",
            failure_code="stale_element_ref",
            failure_phase="precondition",
            input_events_emitted=False,
        )
        response = runtime.operation_failure_response(exc)
        self.assertFalse(response["input_events_emitted"])
        self.assertFalse(response["partial_input_possible"])
        self.assertFalse(response["mutation_unknown"])
        self.assertTrue(response["failureFactsComplete"])
        self.assertEqual(response["failureSubject"], "element")

    def test_observation_incomplete_marks_failure_facts_incomplete(self):
        response = runtime.operation_failure_response(runtime.OperationFailure(
            "window inventory unavailable",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            failure_subject="window",
        ))
        self.assertFalse(response["failureFactsComplete"])

    def test_runtime_failure_after_input_preserves_mutation_unknown(self):
        runtime._CURRENT_ACTION_EVIDENCE = {
            "route": "keyboard_synthesis",
            "input_emitted": True,
        }
        self.addCleanup(setattr, runtime, "_CURRENT_ACTION_EVIDENCE", None)

        response = runtime.unexpected_operation_failure_response(
            RuntimeError("snapshot failed")
        )

        self.assertEqual(response["failureCode"], "effect_unknown")
        self.assertTrue(response["input_events_emitted"])
        self.assertTrue(response["mutation_unknown"])
        self.assertFalse(response["failureFactsComplete"])

    def test_operation_failure_after_input_rewrites_precondition_claim(self):
        runtime._CURRENT_ACTION_EVIDENCE = {
            "route": "semantic_action",
            "input_emitted": True,
        }
        self.addCleanup(setattr, runtime, "_CURRENT_ACTION_EVIDENCE", None)

        response = runtime.operation_failure_response(
            runtime.OperationFailure(
                "stale_app_ref; no action was attempted",
                failure_code="stale_app_ref",
                failure_phase="precondition",
                input_events_emitted=False,
            )
        )

        self.assertEqual(response["failureCode"], "effect_unknown")
        self.assertEqual(
            response["failurePhase"], "post_action_observation"
        )
        self.assertEqual(
            response["underlyingFailureCode"], "stale_app_ref"
        )
        self.assertTrue(response["input_events_emitted"])
        self.assertTrue(response["mutation_unknown"])
        self.assertNotIn("no action was attempted", response["error"])

    def test_observation_failure_subjects_are_layered(self):
        cases = [
            (
                "observation_incomplete",
                "accessibility application inventory could not be read",
                "app",
            ),
            (
                "observation_incomplete",
                "application window inventory could not be read",
                "window",
            ),
            (
                "observation_incomplete",
                "provider identity could not be checked",
                "element",
            ),
        ]
        for code, message, expected in cases:
            with self.subTest(message=message):
                exc = runtime.OperationFailure(
                    message,
                    failure_code=code,
                    failure_phase="precondition",
                )
                self.assertEqual(exc.failure_subject, expected)

    def test_replacement_window_is_machine_readable(self):
        replacement = {
            "windowHandle": "w2:0123456789abcdef0123456789abcdef",
            "title": "Question",
            "role": "dialog",
        }
        exc = runtime.OperationFailure(
            "stale",
            failure_code="stale_element_ref",
            failure_phase="precondition",
            replacement_window=replacement,
        )
        response = runtime.operation_failure_response(exc)
        self.assertEqual(response["failureSubject"], "element")
        self.assertEqual(response["replacementWindow"], replacement)

    def test_recovery_window_rejects_ambiguous_visible_candidates(self):
        first = FakeNode()
        second = FakeNode()
        windows = runtime.InventoryResult([(0, first), (1, second)])
        with (
            mock.patch.object(recovery, "state_value", return_value=True),
            mock.patch.object(
                recovery,
                "strong_window_instance_handle_inventory",
                return_value=runtime.InventoryResult(
                    [(0, "w2:first"), (1, "w2:second")],
                    complete=True,
                ),
            ),
        ):
            self.assertEqual(
                runtime.recovery_window_candidate(FakeNode(), windows),
                {},
            )

    def test_bounded_recovery_reports_gone_source_and_one_current_window(self):
        app = FakeNode()
        current = FakeNode(states={STATE.SHOWING})
        metadata = {
            app: (":1.42", "", 4242, "Editor", "application"),
            current: (":1.42", "/window/current", 4242, "Save As", "dialog"),
        }
        operation = {
            "app_identity": {
                "providerBusName": ":1.42",
                "processGeneration": "gen-4242",
            },
            "target": {
                "windowHandle": "w2:source",
                "providerWindowBusName": ":1.42",
                "providerWindowObjectPath": "/window/source",
                "providerWindowProcessGeneration": "gen-4242",
            },
        }
        with (
            mock.patch.object(
                recovery,
                "iter_apps",
                return_value=runtime.InventoryResult([app], complete=True),
            ),
            mock.patch.object(
                recovery,
                "app_windows",
                return_value=runtime.InventoryResult([(1, current)], complete=True),
            ),
            mock.patch.object(
                recovery,
                "provider_bus_name",
                side_effect=lambda node: metadata[node][0],
            ),
            mock.patch.object(
                recovery,
                "provider_object_path",
                side_effect=lambda node: metadata[node][1],
            ),
            mock.patch.object(
                recovery,
                "node_pid",
                side_effect=lambda node: metadata[node][2],
            ),
            mock.patch.object(
                recovery,
                "node_name",
                side_effect=lambda node: metadata[node][3],
            ),
            mock.patch.object(
                recovery,
                "node_role",
                side_effect=lambda node: metadata[node][4],
            ),
            mock.patch.object(
                recovery,
                "process_generation",
                side_effect=lambda pid: "gen-{}".format(pid),
            ),
            mock.patch.object(
                recovery,
                "strong_window_instance_handle_inventory",
                return_value=runtime.InventoryResult(
                    [(1, "w2:current")],
                    complete=True,
                ),
            ),
        ):
            context = recovery.bounded_recovery_context(operation)

        self.assertEqual(context["sourceAppStatus"], "alive")
        self.assertEqual(context["sourceWindowStatus"], "gone")
        self.assertTrue(context["inventoryComplete"])
        self.assertEqual(context["currentWindow"], {
            "windowHandle": "w2:current",
            "title": "Save As",
            "role": "dialog",
            "providerBusName": ":1.42",
            "providerObjectPath": "/window/current",
            "processGeneration": "gen-4242",
        })
        self.assertNotIn("replacementWindow", context)

    def test_bounded_recovery_ignores_visible_root_without_strong_handle(self):
        app = FakeNode()
        current = FakeNode(states={STATE.SHOWING, STATE.ACTIVE})
        residual = FakeNode(states={STATE.SHOWING, STATE.MODAL})
        metadata = {
            app: (":1.42", "", 4242, "Editor", "application"),
            current: (":1.42", "/window/current", 4242, "Document", "frame"),
            residual: (":1.42", "/window/residual", 4242, "Closed", "dialog"),
        }
        operation = {
            "app_identity": {
                "providerBusName": ":1.42",
                "processGeneration": "gen-4242",
            },
            "window_handle": "w2:source",
        }
        with (
            mock.patch.object(
                recovery,
                "iter_apps",
                return_value=runtime.InventoryResult([app], complete=True),
            ),
            mock.patch.object(
                recovery,
                "app_windows",
                return_value=runtime.InventoryResult(
                    [(0, current), (1, residual)],
                    complete=True,
                ),
            ),
            mock.patch.object(
                recovery,
                "provider_bus_name",
                side_effect=lambda node: metadata[node][0],
            ),
            mock.patch.object(
                recovery,
                "provider_object_path",
                side_effect=lambda node: metadata[node][1],
            ),
            mock.patch.object(
                recovery,
                "node_pid",
                side_effect=lambda node: metadata[node][2],
            ),
            mock.patch.object(
                recovery,
                "node_name",
                side_effect=lambda node: metadata[node][3],
            ),
            mock.patch.object(
                recovery,
                "node_role",
                side_effect=lambda node: metadata[node][4],
            ),
            mock.patch.object(
                recovery,
                "process_generation",
                side_effect=lambda pid: "gen-{}".format(pid),
            ),
            mock.patch.object(
                recovery,
                "strong_window_instance_handle_inventory",
                return_value=runtime.InventoryResult(
                    [(0, "w2:current")],
                    complete=True,
                ),
            ),
        ):
            context = recovery.bounded_recovery_context(operation)

        self.assertEqual(context["sourceWindowStatus"], "gone")
        self.assertTrue(context["inventoryComplete"])
        self.assertEqual(
            context["currentWindow"]["windowHandle"],
            "w2:current",
        )

    def test_read_only_stale_observe_includes_recovery_context(self):
        exc = runtime.OperationFailure(
            "stale window",
            failure_code="stale_window_ref",
            failure_phase="precondition",
            failure_facts_complete=True,
        )
        recovery_context = {
            "sourceAppStatus": "alive",
            "sourceWindowStatus": "gone",
            "inventoryComplete": True,
        }
        with (
            mock.patch.object(
                response_builder.action_evidence,
                "snapshot",
                return_value={},
            ),
            mock.patch.object(
                response_builder,
                "bounded_recovery_context",
                return_value=recovery_context,
            ),
        ):
            response = runtime.operation_failure_response(
                exc,
                recovery_operation={
                    "app_identity": {
                        "providerBusName": ":1.42",
                        "processGeneration": "gen-4242",
                    },
                    "window_handle": "w2:source",
                },
            )

        self.assertEqual(response["failureCode"], "stale_window_ref")
        self.assertFalse(response["input_events_emitted"])
        self.assertFalse(response["mutation_unknown"])
        self.assertEqual(response["recoveryContext"], recovery_context)

    def test_bounded_recovery_reports_exited_only_from_complete_inventory(self):
        operation = {
            "app_identity": {
                "providerBusName": ":1.42",
                "processGeneration": "gen-4242",
            },
        }
        with mock.patch.object(
            recovery,
            "iter_apps",
            return_value=runtime.InventoryResult([], complete=True),
        ):
            context = recovery.bounded_recovery_context(operation)

        self.assertEqual(context, {
            "sourceAppStatus": "exited",
            "sourceWindowStatus": "gone",
            "inventoryComplete": True,
        })

    def test_bounded_recovery_reports_present_without_guessing_current_window(self):
        app = FakeNode()
        source = FakeNode(states={STATE.SHOWING})
        peer = FakeNode(states={STATE.SHOWING})
        operation = {
            "app_identity": {
                "providerBusName": ":1.42",
                "processGeneration": "gen-4242",
            },
            "target": {
                "windowHandle": "w2:source",
                "providerWindowBusName": ":1.42",
                "providerWindowObjectPath": "/window/source",
                "providerWindowProcessGeneration": "gen-4242",
            },
        }
        with (
            mock.patch.object(
                recovery,
                "iter_apps",
                return_value=runtime.InventoryResult([app], complete=True),
            ),
            mock.patch.object(
                recovery,
                "app_windows",
                return_value=runtime.InventoryResult(
                    [(0, source), (1, peer)],
                    complete=True,
                ),
            ),
            mock.patch.object(
                recovery,
                "provider_bus_name",
                side_effect=lambda node: ":1.42",
            ),
            mock.patch.object(
                recovery,
                "provider_object_path",
                side_effect=lambda node: (
                    "/window/source" if node is source else "/window/peer"
                ),
            ),
            mock.patch.object(
                recovery,
                "node_pid",
                return_value=4242,
            ),
            mock.patch.object(
                recovery,
                "process_generation",
                return_value="gen-4242",
            ),
            mock.patch.object(
                recovery,
                "strong_window_instance_handle_inventory",
                return_value=runtime.InventoryResult(
                    [(0, "w2:source"), (1, "w2:peer")],
                    complete=True,
                ),
            ),
        ):
            context = recovery.bounded_recovery_context(operation)

        self.assertEqual(context["sourceAppStatus"], "alive")
        self.assertEqual(context["sourceWindowStatus"], "present")
        self.assertTrue(context["inventoryComplete"])
        self.assertNotIn("currentWindow", context)

    def test_bounded_recovery_keeps_absence_unknown_when_inventory_incomplete(self):
        operation = {
            "app_identity": {
                "providerBusName": ":1.42",
                "processGeneration": "gen-4242",
            },
        }
        with mock.patch.object(
            recovery,
            "iter_apps",
            return_value=runtime.InventoryResult([], complete=False),
        ):
            context = recovery.bounded_recovery_context(operation)

        self.assertEqual(context, {
            "sourceAppStatus": "unknown",
            "sourceWindowStatus": "unknown",
            "inventoryComplete": False,
        })

    def test_bounded_recovery_does_not_prove_gone_when_native_inventory_fails(self):
        app = FakeNode()
        current = FakeNode(states={STATE.SHOWING})
        operation = {
            "app_identity": {
                "providerBusName": ":1.42",
                "processGeneration": "gen-4242",
            },
            "target": {"windowHandle": "w2:source"},
        }
        with (
            mock.patch.object(
                recovery,
                "iter_apps",
                return_value=runtime.InventoryResult([app], complete=True),
            ),
            mock.patch.object(
                recovery,
                "app_windows",
                return_value=runtime.InventoryResult([(0, current)], complete=True),
            ),
            mock.patch.object(recovery, "provider_bus_name", return_value=":1.42"),
            mock.patch.object(recovery, "node_pid", return_value=4242),
            mock.patch.object(
                recovery,
                "process_generation",
                return_value="gen-4242",
            ),
            mock.patch.object(
                recovery,
                "strong_window_instance_handle_inventory",
                return_value=runtime.InventoryResult([], complete=False),
            ),
        ):
            context = recovery.bounded_recovery_context(operation)

        self.assertFalse(context["inventoryComplete"])
        self.assertEqual(context["sourceWindowStatus"], "unknown")
        self.assertNotIn("currentWindow", context)

    def test_bounded_recovery_does_not_choose_window_when_visibility_is_unknown(self):
        app = FakeNode()
        unknown = FakeNode()
        visible = FakeNode(states={STATE.SHOWING})
        operation = {
            "app_identity": {
                "providerBusName": ":1.42",
                "processGeneration": "gen-4242",
            },
        }
        with (
            mock.patch.object(
                recovery,
                "iter_apps",
                return_value=runtime.InventoryResult([app], complete=True),
            ),
            mock.patch.object(
                recovery,
                "app_windows",
                return_value=runtime.InventoryResult(
                    [(0, unknown), (1, visible)],
                    complete=True,
                ),
            ),
            mock.patch.object(recovery, "provider_bus_name", return_value=":1.42"),
            mock.patch.object(recovery, "node_pid", return_value=4242),
            mock.patch.object(
                recovery,
                "process_generation",
                return_value="gen-4242",
            ),
            mock.patch.object(
                recovery,
                "strong_window_instance_handle_inventory",
                return_value=runtime.InventoryResult(
                    [(0, "w2:unknown"), (1, "w2:visible")],
                    complete=True,
                ),
            ),
            mock.patch.object(
                recovery,
                "state_value",
                side_effect=lambda node, _state: (
                    None if node is unknown else True
                ),
            ),
        ):
            context = recovery.bounded_recovery_context(operation)

        self.assertFalse(context["inventoryComplete"])
        self.assertNotIn("currentWindow", context)

    def test_expected_transient_window_rejects_provider_replacement(self):
        window = FakeNode()
        expectation = {
            "windowHandle": "w2:source",
            "providerBusName": ":1.42",
            "providerObjectPath": "/dialog/old",
            "processGeneration": "gen-4242",
            "role": "file chooser",
            "name": "Save As",
        }
        with (
            mock.patch.object(runtime, "provider_bus_name", return_value=":1.42"),
            mock.patch.object(runtime, "provider_object_path", return_value="/window/document"),
            mock.patch.object(runtime, "node_pid", return_value=4242),
            mock.patch.object(runtime, "process_generation", return_value="gen-4242"),
            mock.patch.object(runtime, "node_role", return_value="frame"),
            mock.patch.object(runtime, "node_name", return_value="Document"),
        ):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime._verify_expected_window(window, expectation)
        self.assertEqual(caught.exception.failure_code, "stale_target")
        self.assertFalse(caught.exception.input_events_emitted)

    def test_expected_transient_window_rejects_same_role_new_provider_owner(self):
        window = FakeNode()
        expectation = {
            "windowHandle": "w2:source",
            "providerBusName": ":1.42",
            "providerObjectPath": "/dialog/old",
            "processGeneration": "gen-4242",
            "role": "dialog",
            "name": "Question",
        }
        with (
            mock.patch.object(runtime, "provider_bus_name", return_value=":1.42"),
            mock.patch.object(
                runtime,
                "provider_object_path",
                return_value="/dialog/later",
            ),
            mock.patch.object(runtime, "node_pid", return_value=4242),
            mock.patch.object(runtime, "process_generation", return_value="gen-4242"),
            mock.patch.object(runtime, "node_role", return_value="dialog"),
            mock.patch.object(runtime, "node_name", return_value="Question"),
        ):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime._verify_expected_window(window, expectation)
        self.assertEqual(caught.exception.failure_code, "stale_target")
        self.assertFalse(caught.exception.input_events_emitted)

    def test_expected_native_window_allows_provider_object_rebuild(self):
        window = FakeNode()
        expectation = {
            "windowHandle": "w2:source",
            "providerBusName": ":1.42",
            "providerObjectPath": "/window/old",
            "processGeneration": "gen-4242",
            "role": "frame",
            "name": "Old title",
        }
        with (
            mock.patch.object(runtime, "provider_bus_name", return_value=":1.42"),
            mock.patch.object(runtime, "provider_object_path", return_value="/window/new"),
            mock.patch.object(runtime, "node_pid", return_value=4242),
            mock.patch.object(runtime, "process_generation", return_value="gen-4242"),
            mock.patch.object(runtime, "node_role", return_value="frame"),
            mock.patch.object(runtime, "node_name", return_value="New title"),
        ):
            runtime._verify_expected_window(window, expectation)

    def test_expected_transient_window_allows_title_only_change(self):
        window = FakeNode()
        expectation = {
            "windowHandle": "w2:source",
            "providerBusName": ":1.42",
            "providerObjectPath": "/dialog/options",
            "processGeneration": "gen-4242",
            "role": "dialog",
            "name": "Options - General",
        }
        with (
            mock.patch.object(runtime, "provider_bus_name", return_value=":1.42"),
            mock.patch.object(
                runtime,
                "provider_object_path",
                return_value="/dialog/options",
            ),
            mock.patch.object(runtime, "node_pid", return_value=4242),
            mock.patch.object(runtime, "process_generation", return_value="gen-4242"),
            mock.patch.object(runtime, "node_role", return_value="dialog"),
            mock.patch.object(runtime, "node_name", return_value="Options - View"),
        ):
            runtime._verify_expected_window(window, expectation)

    def test_recovery_failure_cannot_mask_effect_unknown(self):
        with (
            mock.patch.object(
                response_builder.action_evidence,
                "snapshot",
                return_value={"input_emitted": True},
            ),
            mock.patch.object(
                response_builder,
                "bounded_recovery_context",
                side_effect=RuntimeError("inventory failed"),
            ),
        ):
            response = response_builder.unexpected_operation_failure_response(
                RuntimeError("snapshot failed"),
                recovery_operation={"app": "Editor"},
            )

        self.assertEqual(response["failureCode"], "effect_unknown")
        self.assertTrue(response["mutation_unknown"])
        self.assertEqual(response["recoveryContext"], {
            "sourceAppStatus": "unknown",
            "sourceWindowStatus": "unknown",
            "inventoryComplete": False,
        })

    def test_operation_failure_after_input_includes_recovery_context(self):
        recovery_context = {
            "sourceAppStatus": "alive",
            "sourceWindowStatus": "present",
            "inventoryComplete": True,
        }
        with (
            mock.patch.object(
                response_builder.action_evidence,
                "snapshot",
                return_value={"input_emitted": True},
            ),
            mock.patch.object(
                response_builder,
                "bounded_recovery_context",
                return_value=recovery_context,
            ),
        ):
            response = response_builder.operation_failure_response(
                runtime.OperationFailure(
                    "observation_incomplete",
                    failure_code="observation_incomplete",
                    failure_phase="observation",
                ),
                recovery_operation={"app": "Editor"},
            )

        self.assertEqual(response["failureCode"], "effect_unknown")
        self.assertTrue(response["mutation_unknown"])
        self.assertEqual(response["recoveryContext"], recovery_context)

    def test_explicit_failure_subject_overrides_message_inference(self):
        exc = runtime.OperationFailure(
            "observation_incomplete: provider text that mentions window inventory",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            failure_subject="element",
        )
        self.assertEqual(exc.failure_subject, "element")
