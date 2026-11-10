#!/usr/bin/env python3
"""Linux provider wire protocol, action evidence, and failure boundary tests."""

import io
import json

from runtime_test_support import *  # noqa: F401,F403


class RuntimeActionEvidenceFailureTests(unittest.TestCase):
    def test_text_delivery_wire_keeps_null_and_zero_without_confirming_effect(self):
        for name, source_count, unknown_index in (
            ("text-first-message-unknown", 1, 0),
            ("text-focus-changed", 1, None),
            ("text-segment-unknown", 7, 1),
        ):
            fixture = json.loads((Path(__file__).parents[1] / "testdata" /
                                  "provider-wire" / (name + ".response.json")).read_text())
            payload = fixture.get("desktop", fixture.get("accessibility"))
            evidence = payload["action_evidence"]
            for encode in (wire._desktop_result, wire._accessibility_result):
                with self.subTest(name=name, encode=encode.__name__):
                    result = encode({"ok": False, "actionEvidence": evidence})
                    self.assertEqual(result["action_evidence"], evidence)
                    self.assertFalse(result["action_evidence"]["effect_verified"])
                    self.assertEqual(result["action_evidence"]["source_utf16_count"],
                                     source_count)
                    self.assertEqual(result["action_evidence"]["unknown_message_index"],
                                     unknown_index)

    def test_text_delivery_wire_distinguishes_missing_span_from_zero(self):
        for source_count in (None, 0):
            evidence = {"sequence_phase": "wake", "effect_verified": False}
            if source_count is not None:
                evidence["source_utf16_count"] = source_count
            for encode in (wire._desktop_result, wire._accessibility_result):
                with self.subTest(source_count=source_count, encode=encode.__name__):
                    result = encode({"ok": False, "actionEvidence": evidence})
                    self.assertEqual(result["action_evidence"], evidence)

    def test_generic_input_exception_preserves_confirmed_events(self):
        action_evidence.replace({"input_emitted": False})
        self.addCleanup(action_evidence.reset)
        error = RuntimeError("transport lost after one click")
        error.events_emitted = True
        response = runtime.unexpected_operation_failure_response(error)
        self.assertTrue(response["input_events_emitted"])
        self.assertTrue(response["mutation_unknown"])
        self.assertEqual(response["failureCode"], "effect_unknown")

    def test_uncertain_semantic_dispatch_matches_shared_wire_fixture(self):
        action_evidence.replace({"input_emitted": False})
        self.addCleanup(action_evidence.reset)
        error = RuntimeError("D-Bus reply lost")
        error.partial_input_possible = True
        response = runtime.unexpected_operation_failure_response(error, object())
        self.assertNotIn("recoveryContext", response)
        result = wire._accessibility_result(response)
        fixture = json.loads((Path(__file__).parents[1] / "testdata" /
                              "provider-wire" / "semantic-reply-lost.response.json")
                             .read_text())
        expected = fixture["accessibility"]
        self.assertEqual(result["error"], expected["error"])
        for key, value in expected["failure"].items():
            self.assertEqual(result["failure"][key], value)
        for key, value in expected["mutation"].items():
            self.assertEqual(result["mutation"][key], value)
        self.assertFalse(result["mutation"]["input_events_emitted"])
        self.assertFalse(result["action_evidence"]["input_emitted"])

    def test_partial_apps_wire_preserves_typed_unknown_coverage(self):
        issues = [{"app": "Busy app", "reason": "AXWindows unavailable"}]
        result = wire._accessibility_result({
            "ok": True, "apps": [{"name": "Editor"}],
            "notes": ["Incomplete: Busy app"], "inventory_issues": issues,
        })
        self.assertEqual(result["inventory_issues"], issues)
        self.assertEqual(result["apps"], [{"name": "Editor"}])
        self.assertEqual(result["notes"], ["Incomplete: Busy app"])

    def test_partial_failure_preserves_runtime_route(self):
        runtime._CURRENT_ACTION_EVIDENCE = {
            "route": "coordinate_synthesis",
            "geometry_sampled": True,
            "focus_verified": True,
            "editable_verified": None,
            "readback_sampled": False,
            "readback_verified": None,
            "input_emitted": False,
        }
        response = runtime.operation_failure_response(runtime.OperationFailure(
            "post emit failure",
            failure_code="post_emit_failure",
            failure_phase="readback",
            input_events_emitted=True,
        ))
        evidence = response["actionEvidence"]
        self.assertEqual(evidence["route"], "coordinate_synthesis")
        self.assertTrue(evidence["geometry_sampled"])
        self.assertTrue(evidence["input_emitted"])
        self.assertFalse(evidence["readback_sampled"])
        self.assertEqual(response["failureCode"], "post_emit_failure")
        self.assertEqual(response["failurePhase"], "readback")
        self.assertNotIn("underlyingFailureCode", response)
        self.assertEqual(response["error"], "post emit failure")
        self.assertNotIn("no action was attempted", response["error"])


class CoordinateActionabilityGateTests(unittest.TestCase):
    def tearDown(self):
        runtime._CURRENT_ACTION_EVIDENCE = None

    def test_missing_second_geometry_sample_is_not_stable(self):
        first = runtime.frame(1, 2, 20, 10)
        samples = iter([first, None])
        with mock.patch.object(
            runtime, "relative_frame", side_effect=lambda *_args: next(samples)
        ), mock.patch.object(runtime.time, "sleep", return_value=None):
            live, count, stable = runtime.wait_until_stable(
                FakeNode(), runtime.frame(0, 0, 100, 100)
            )

        self.assertIsNone(live)
        self.assertEqual(count, 2)
        self.assertFalse(stable)

    def test_missing_followup_geometry_fails_before_input(self):
        record = {"frame": runtime.frame(1, 2, 20, 10)}
        runtime._CURRENT_ACTION_EVIDENCE = {
            "geometry_sampled": False,
            "geometry_stable": None,
            "input_emitted": False,
        }
        with mock.patch.object(
            runtime, "relative_frame", side_effect=[record["frame"], None]
        ), mock.patch.object(runtime.time, "sleep", return_value=None):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.require_stable_coordinate_geometry(
                    FakeNode(), record, runtime.frame(0, 0, 100, 100)
                )

        self.assertEqual(
            raised.exception.failure_code, "observation_incomplete"
        )
        self.assertFalse(raised.exception.input_events_emitted)
        self.assertTrue(
            runtime._CURRENT_ACTION_EVIDENCE["geometry_sampled"]
        )
        self.assertIsNone(
            runtime._CURRENT_ACTION_EVIDENCE["geometry_stable"]
        )

    def test_unstable_geometry_fails_before_input_with_typed_evidence(self):
        record = {"frame": runtime.frame(1, 2, 20, 10)}
        runtime._CURRENT_ACTION_EVIDENCE = {
            "geometry_sampled": False,
            "geometry_stable": None,
            "input_emitted": False,
        }
        with mock.patch.object(
            runtime, "relative_frame", return_value=record["frame"]
        ), mock.patch.object(
            runtime,
            "wait_until_stable",
            return_value=(record["frame"], 3, False),
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.require_stable_coordinate_geometry(
                    FakeNode(), record, runtime.frame(0, 0, 100, 100)
                )
        self.assertEqual(raised.exception.failure_code, "target_unstable")
        self.assertFalse(raised.exception.input_events_emitted)
        self.assertTrue(
            runtime._CURRENT_ACTION_EVIDENCE["geometry_sampled"]
        )
        self.assertFalse(
            runtime._CURRENT_ACTION_EVIDENCE["geometry_stable"]
        )

    def test_wrong_point_owner_fails_after_window_focus_without_input(self):
        runtime._CURRENT_ACTION_EVIDENCE = {
            "window_focus_verified": None,
            "point_owner_verified": None,
            "input_emitted": False,
        }
        with mock.patch.object(
            runtime, "require_window_focus", return_value=None
        ), mock.patch.object(
            runtime, "coordinate_point_owner", return_value=False
        ):
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.require_coordinate_delivery(
                    FakeNode(), 50, 50, "click"
                )
        self.assertEqual(
            raised.exception.failure_code,
            "coordinate_target_unverified",
        )
        self.assertFalse(raised.exception.input_events_emitted)
        self.assertTrue(
            runtime._CURRENT_ACTION_EVIDENCE["window_focus_verified"]
        )
        self.assertFalse(
            runtime._CURRENT_ACTION_EVIDENCE["point_owner_verified"]
        )


class ProviderWireTests(unittest.TestCase):
    def test_target_automation_id_reaches_action_dispatch_unchanged(self):
        fixture = Path(__file__).resolve().parents[1] / "testdata/provider-wire/resolve-and-act.request.json"
        request = json.loads(fixture.read_text())
        with mock.patch.object(wire, "_initialize_provider"), mock.patch.object(
            wire, "perform_provider_request", return_value={"ok": True}
        ) as dispatch:
            response = wire._execute_serialized_request(json.dumps(request))
        self.assertTrue(response["accessibility"]["ok"])
        dispatch.assert_called_once_with("resolve_and_act", request["action"])
        self.assertEqual(dispatch.call_args.args[1]["target"]["automationId"], "name-entry")

    def test_invalid_target_identity_rejected_before_dispatch(self):
        fixture = Path(__file__).resolve().parents[1] / "testdata/provider-wire/resolve-and-act.request.json"
        for field, value in (("automationId", 123), ("automationId", []),
                             ("automationId", False), ("automationID", "name-entry")):
            with self.subTest(field=field, value=value):
                request = json.loads(fixture.read_text())
                request["action"]["target"][field] = value
                with mock.patch.object(wire, "_initialize_provider") as initialize, mock.patch.object(
                    wire, "perform_provider_request"
                ) as dispatch, mock.patch.object(wire, "_write_operator_exception"):
                    response = wire._execute_serialized_request(json.dumps(request))
                initialize.assert_not_called()
                dispatch.assert_not_called()
                self.assertEqual(response["accessibility"]["failure"]["code"], "invalid_provider_request")
                self.assertFalse(any(response["accessibility"].get("mutation", {}).values()))

    def test_optional_ordered_input_fixtures_reject_before_provider_startup(self):
        fixture_root = Path(__file__).resolve().parents[1] / "testdata" / "provider-wire"
        manifest = json.loads((fixture_root / "native-manifest.json").read_text())
        self.assertEqual(manifest["protocol"], wire.PROTOCOL)
        self.assertEqual(manifest["schema_version"], wire.SCHEMA_VERSION)
        self.assertFalse(manifest["ordered_input_platforms"]["linux"])
        rejected = 0
        for fixture in manifest["fixtures"]:
            if fixture["direction"] not in {"request", "invalid_request"}:
                continue
            request = json.loads((fixture_root / fixture["file"]).read_text())
            self.assertEqual(request["protocol"], wire.PROTOCOL)
            self.assertEqual(request["schema_version"], wire.SCHEMA_VERSION)
            self.assertEqual(request["operation"], "ordered_input")
            # The full fixture fails strict envelope validation. Removing only
            # its optional payload also exercises unsupported-operation rejection.
            header_only = dict(request)
            del header_only["ordered_input"]
            for candidate, message in [
                (request, "unknown field(s): ordered_input"),
                (header_only, "unsupported provider operation 'ordered_input'"),
            ]:
                with self.subTest(fixture=fixture["name"], message=message):
                    with self.assertRaises(wire.ProviderRequestError) as raised:
                        wire.validate_request(candidate)
                    self.assertEqual(raised.exception.code, "invalid_provider_request")
                    self.assertIn(message, str(raised.exception))
                    with mock.patch.object(
                        wire, "_initialize_provider"
                    ) as initialize, mock.patch.object(
                        wire, "perform_provider_request"
                    ) as dispatch, mock.patch.object(wire, "_write_operator_exception"):
                        response = wire._execute_serialized_request(json.dumps(candidate))
                    initialize.assert_not_called()
                    dispatch.assert_not_called()
                    self.assertEqual(response["request_id"], request["request_id"])
                    self.assertEqual(response["protocol"], wire.PROTOCOL)
                    failure = response["accessibility"]
                    self.assertFalse(failure["ok"])
                    self.assertEqual(failure["failure"]["code"], "invalid_provider_request")
                    self.assertIn(message, failure["error"])
                    self.assertFalse(any(failure.get("mutation", {}).values()))
            rejected += 1
        self.assertEqual(rejected, 2)

    def test_semantic_write_constraint_is_explicit_and_windows_only(self):
        fixture = Path(__file__).resolve().parents[1] / "testdata/provider-wire/semantic-write-constraint.request.json"
        request = json.loads(fixture.read_text())
        wire.validate_request(request)
        request["action"]["input"]["require_semantic_write"] = False
        wire.validate_request(request)
        request["action"]["input"]["require_semantic_write"] = True
        with self.assertRaises(wire.ProviderRequestError) as error:
            wire.validate_request(request)
        self.assertIn("Windows-only", str(error.exception))
        request["action"]["input"]["require_semantic_write"] = "true"
        with self.assertRaises(wire.ProviderRequestError):
            wire.validate_request(request)

    def test_observation_find_walk_requires_boolean(self):
        def request(value):
            return {
                "protocol": "computer-use-provider",
                "schema_version": 2,
                "request_id": "req-find-walk",
                "operation": "observe",
                "observe": {
                    "protocol_version": 2,
                    "app": "editor",
                    "observation": {"find_walk": value},
                },
            }
        wire.validate_request(request(True))
        with self.assertRaises(wire.ProviderRequestError):
            wire.validate_request(request("yes"))

    def test_observation_capture_snapshot_requires_boolean(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-capture",
            "operation": "resolve_and_act",
            "action": {
                "protocol_version": 2,
                "intent": "keypress",
                "app": "editor",
                "input": {"key": "ENTER"},
                "observation": {"capture_snapshot": "no"},
            },
        }
        with self.assertRaises(wire.ProviderRequestError) as raised:
            wire.validate_request(request)
        self.assertEqual(
            raised.exception.code,
            "invalid_provider_request",
        )

    def test_stdio_worker_initializes_provider_once(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-1",
            "operation": "observe_apps",
            "observe_apps": {"protocol_version": 2},
        }
        stdin = io.StringIO(
            json.dumps(request)
            + "\n"
            + json.dumps({**request, "request_id": "req-2"})
            + "\n"
        )
        stdout = io.StringIO()
        with mock.patch.object(
            wire, "_provider_initialized", False
        ), mock.patch.object(
            wire, "require_desktop_session"
        ) as require_session, mock.patch.object(
            wire.Atspi, "init"
        ) as atspi_init, mock.patch.object(
            wire.Atspi, "set_timeout"
        ) as set_timeout, mock.patch.object(
            wire.ui_events, "start"
        ) as monitor_start, mock.patch.object(
            wire, "enable_session_accessibility",
            return_value={"changed": [], "errors": []},
        ), mock.patch.object(
            wire,
            "perform_provider_request",
            return_value={"ok": True, "apps": []},
        ):
            wire.serve_stdio(stdin, stdout)

        responses = [
            json.loads(line) for line in stdout.getvalue().splitlines()
        ]
        self.assertEqual(
            [response["request_id"] for response in responses],
            ["req-1", "req-2"],
        )
        require_session.assert_called_once_with()
        atspi_init.assert_called_once_with()
        # No startup grace: a new application gets the same bound per call.
        set_timeout.assert_called_once_with(
            wire.ATSPI_CALL_TIMEOUT_MS, wire.ATSPI_CALL_TIMEOUT_MS
        )
        monitor_start.assert_called_once_with()

    def test_shared_invalid_request_corpus_is_rejected(self):
        fixture_path = (
            Path(__file__).resolve().parents[1]
            / "testdata"
            / "provider-wire"
            / "invalid-requests.json"
        )
        fixtures = json.loads(fixture_path.read_text(encoding="utf-8"))
        self.assertTrue(fixtures)
        for fixture in fixtures:
            with self.subTest(fixture=fixture["name"]):
                with self.assertRaises(runtime.ProviderRequestError):
                    runtime.validate_request(fixture["request"])

    def test_resolved_element_drops_private_provider_fields(self):
        record = {
            "index": 0,
            "runtimeId": [0, 1],
            "_providerIdentityConflict": False,
        }
        public = runtime.public_element_record(record)
        self.assertEqual(public, {"index": 0, "runtimeId": [0, 1]})

    def test_resolve_target_response_carries_no_private_fields(self):
        # The host decodes the wire with unknown fields disallowed. A private
        # resolver key in resolved_element failed every until poll on a ref,
        # so each poll became observation_incomplete until the timeout.
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-resolve",
            "operation": "resolve_and_act",
            "action": {
                "protocol_version": 2,
                "intent": "resolve_target",
                "app": "Editor",
                "target": {
                    "selector": 'spin button "Width"',
                    "role": "spin button",
                    "name": "Width",
                    "hasName": True,
                },
            },
        }
        record = {
            "index": 3,
            "runtimeId": [0, 1],
            "runtimeAliases": [[0, 1]],
            "controlType": "spin button",
            "value": "2 cm",
            "_providerIdentityConflict": False,
            "_resolvedNode": object(),
        }
        with mock.patch.object(
            runtime,
            "perform_provider_request",
            return_value={"ok": True, "resolvedElement": record},
        ):
            response = runtime.execute(request)
        resolved = response["accessibility"]["resolved_element"]
        self.assertEqual(
            sorted(key for key in resolved if key.startswith("_")), []
        )
        self.assertEqual(resolved["value"], "2 cm")
        json.dumps(response)

    def test_provider_dispatch_keeps_operation_identity(self):
        payload = {
            "protocol_version": 2,
            "intent": "click",
            "app": "Editor",
        }
        with mock.patch.object(
            runtime,
            "dispatch_provider_operation",
            return_value={"ok": True},
        ) as perform:
            result = runtime.perform_provider_request(
                "resolve_and_act",
                payload,
            )
        self.assertTrue(result["ok"])
        args, kwargs = perform.call_args
        self.assertIs(args[0], payload)
        self.assertEqual(kwargs["operation_kind"], "resolve_and_act")
        self.assertEqual(kwargs["action_intent"], "click")
        self.assertNotIn("tool", kwargs)

    def test_observe_request_uses_wire_envelope(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-observe",
            "operation": "observe",
            "observe": {
                "protocol_version": 2,
                "app": "Editor",
                "window_handle": "w2:document",
                "observation": {
                    "max_tree_nodes": 1200,
                    "include_screenshot": False,
                },
            },
        }
        with mock.patch.object(
            runtime,
            "perform_provider_request",
            return_value={"ok": True, "snapshot": {"windowTitle": "Document"}},
        ) as perform:
            response = runtime.execute(request)
        perform.assert_called_once_with("observe", request["observe"])
        self.assertEqual(response["protocol"], "computer-use-provider")
        self.assertEqual(response["request_id"], "req-observe")
        self.assertEqual(
            response["accessibility"]["snapshot"]["windowTitle"],
            "Document",
        )

    def test_unexpected_dispatch_failure_requires_provider_reset(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-reset",
            "operation": "observe_apps",
            "observe_apps": {"protocol_version": 2},
        }
        with mock.patch.object(
            runtime,
            "perform_provider_request",
            side_effect=RuntimeError("AX bus disconnected"),
        ), mock.patch.object(runtime, "_write_operator_exception"):
            response = runtime.execute(request)

        self.assertTrue(response["provider_reset_required"])
        self.assertFalse(response["accessibility"]["ok"])

    def test_expected_operation_failure_does_not_require_provider_reset(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-no-reset",
            "operation": "observe_apps",
            "observe_apps": {"protocol_version": 2},
        }
        with mock.patch.object(
            runtime, "_CURRENT_ACTION_EVIDENCE", None
        ), mock.patch.object(
            runtime,
            "perform_provider_request",
            side_effect=runtime.OperationFailure(
                "application unavailable",
                failure_code="ax_app_unavailable",
                failure_phase="resolve",
            ),
        ):
            response = runtime.execute(request)

        self.assertNotIn("provider_reset_required", response)
        self.assertEqual(
            response["accessibility"]["failure"]["code"],
            "ax_app_unavailable",
        )

    def test_expected_provider_disconnect_requests_reset(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-expected-reset",
            "operation": "observe_apps",
            "observe_apps": {"protocol_version": 2},
        }
        with mock.patch.object(
            runtime, "_CURRENT_ACTION_EVIDENCE", None
        ), mock.patch.object(
            runtime,
            "perform_provider_request",
            side_effect=runtime.OperationFailure(
                "accessibility application inventory was incomplete",
                failure_code="observation_incomplete",
                failure_phase="resolve",
                failure_subject="app",
                provider_reset_required=True,
            ),
        ):
            response = runtime.execute(request)

        self.assertTrue(response["provider_reset_required"])
        self.assertEqual(
            response["accessibility"]["failure"]["code"],
            "observation_incomplete",
        )

    def test_action_failure_preserves_typed_mutation_facts(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-action",
            "operation": "resolve_and_act",
            "action": {
                "protocol_version": 2,
                "intent": "click",
                "app": "Editor",
                "target": {
                    "selector": 'button "Save"',
                    "role": "button",
                    "name": "Save",
                    "hasName": True,
                },
                "input": {"click_count": 1},
            },
        }
        with mock.patch.object(
            runtime,
            "perform_provider_request",
            return_value={
                "ok": False,
                "error": "post action read failed",
                "failureCode": "effect_unknown",
                "failurePhase": "post_action_observation",
                "failureFactsComplete": False,
                "input_events_emitted": True,
                "mutation_unknown": True,
                "actionEvidence": {
                    "route": "semantic_action",
                    "input_emitted": True,
                },
            },
        ) as perform:
            response = runtime.execute(request)
        perform.assert_called_once_with("resolve_and_act", request["action"])
        result = response["accessibility"]
        self.assertEqual(result["failure"]["code"], "effect_unknown")
        self.assertTrue(result["mutation"]["input_events_emitted"])
        self.assertTrue(result["mutation"]["mutation_unknown"])
        self.assertEqual(
            result["action_evidence"]["route"],
            "semantic_action",
        )

    def test_post_action_failure_exports_typed_recovery_context(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-recovery",
            "operation": "resolve_and_act",
            "action": {
                "protocol_version": 2,
                "intent": "click",
                "app": "Editor",
                "app_identity": {
                    "providerBusName": ":1.42",
                    "processGeneration": "gen-4242",
                },
                "target": {
                    "selector": 'button "Open"',
                    "windowHandle": "w2:source",
                    "providerBusName": ":1.42",
                    "providerObjectPath": "/button/open",
                    "processGeneration": "gen-4242",
                    "providerWindowBusName": ":1.42",
                    "providerWindowObjectPath": "/window/source",
                    "providerWindowProcessGeneration": "gen-4242",
                    "role": "button",
                    "name": "Open",
                    "hasName": True,
                },
                "input": {"click_count": 1},
            },
        }
        recovery_context = {
            "sourceAppStatus": "alive",
            "sourceWindowStatus": "gone",
            "inventoryComplete": True,
            "currentWindow": {
                "windowHandle": "w2:current",
                "title": "Save As",
                "role": "dialog",
            },
        }
        with (
            mock.patch.object(
                runtime,
                "perform_provider_request",
                side_effect=RuntimeError("final snapshot failed"),
            ),
            mock.patch.object(
                response_builder.action_evidence,
                "snapshot",
                return_value={"route": "semantic_action", "input_emitted": True},
            ),
            mock.patch.object(
                response_builder,
                "bounded_recovery_context",
                return_value=recovery_context,
            ) as recover,
            mock.patch.object(wire, "_write_operator_exception"),
        ):
            response = runtime.execute(request)

        result = response["accessibility"]
        self.assertEqual(result["failure"]["code"], "effect_unknown")
        self.assertTrue(result["mutation"]["mutation_unknown"])
        self.assertEqual(result["recovery_context"], recovery_context)
        recover.assert_called_once_with(request["action"])

    def test_stale_observe_exports_read_only_recovery_context(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-stale-observe",
            "operation": "observe",
            "observe": {
                "protocol_version": 2,
                "app": "Editor",
                "app_identity": {
                    "providerBusName": ":1.42",
                    "processGeneration": "gen-4242",
                },
                "window_handle": "w2:source",
            },
        }
        recovery_context = {
            "sourceAppStatus": "alive",
            "sourceWindowStatus": "gone",
            "inventoryComplete": True,
            "currentWindow": {
                "windowHandle": "w2:current",
                "title": "Document",
                "role": "frame",
            },
        }
        with (
            mock.patch.object(
                runtime,
                "perform_provider_request",
                side_effect=runtime.OperationFailure(
                    "stale window",
                    failure_code="stale_window_ref",
                    failure_phase="precondition",
                    failure_facts_complete=True,
                    failure_subject="window",
                ),
            ),
            mock.patch.object(
                response_builder,
                "bounded_recovery_context",
                return_value=recovery_context,
            ) as recover,
        ):
            response = runtime.execute(request)

        result = response["accessibility"]
        self.assertEqual(result["failure"]["code"], "stale_window_ref")
        self.assertNotIn("mutation", result)
        self.assertEqual(result["recovery_context"], recovery_context)
        recover.assert_called_once_with(request["observe"])

    def test_rejects_unknown_top_level_field(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-observe",
            "operation": "observe_apps",
            "observe_apps": {"protocol_version": 2},
            "legacy": True,
        }
        with self.assertRaisesRegex(ValueError, "unknown field"):
            runtime.execute(request)

    def test_rejects_unknown_payload_field(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-observe",
            "operation": "observe",
            "observe": {
                "protocol_version": 2,
                "app": "Editor",
                "legacy_mode": "get_app_state",
            },
        }
        with self.assertRaisesRegex(ValueError, "unknown field"):
            runtime.execute(request)

    def test_rejects_keypress_without_key(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-keypress",
            "operation": "resolve_and_act",
            "action": {
                "protocol_version": 2,
                "intent": "keypress",
                "app": "Editor",
                "window_handle": "w1",
                "input": {},
            },
        }
        with self.assertRaisesRegex(ValueError, "missing required field"):
            runtime.execute(request)

    def test_protocol_mismatch_has_specific_failure_code(self):
        request = {
            "protocol": "not-a-provider-protocol",
            "schema_version": 2,
            "request_id": "req-observe",
            "operation": "observe_apps",
            "observe_apps": {"protocol_version": 2},
        }
        with self.assertRaises(runtime.ProviderRequestError) as raised:
            runtime.execute(request)
        self.assertEqual(
            raised.exception.code,
            "provider_protocol_mismatch",
        )

    def test_rejects_desktop_settle_above_limit(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-desktop-settle",
            "operation": "desktop_execute_batch",
            "desktop": {
                "protocol_version": 2,
                "actions": [{"type": "screenshot"}],
                "settle_ms": 3001,
            },
        }
        with self.assertRaises(runtime.ProviderRequestError) as raised:
            runtime.execute(request)
        self.assertEqual(raised.exception.code, "invalid_provider_request")
        self.assertIn("between 0 and 3000", str(raised.exception))


class RuntimeStartupErrorBoundaryTests(unittest.TestCase):
    def test_startup_traceback_is_operator_only(self):
        completed = subprocess.run(
            [sys.executable, runtime.__file__],
            capture_output=True,
            text=True,
            check=False,
        )
        response = json.loads(completed.stdout)
        result = response["accessibility"]
        self.assertEqual(
            result["failure"]["code"],
            "runtime_startup_failed",
        )
        self.assertNotIn("Traceback", result["error"])
        self.assertIn("Traceback", completed.stderr)

    def test_provider_initialization_failure_requires_reset(self):
        request = {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-startup-reset",
            "operation": "observe_apps",
            "observe_apps": {"protocol_version": 2},
        }
        with mock.patch.object(
            wire, "_provider_initialized", False
        ), mock.patch.object(
            wire, "require_desktop_session"
        ), mock.patch.object(
            wire.Atspi,
            "init",
            side_effect=RuntimeError("AT-SPI unavailable"),
        ), mock.patch.object(wire, "_write_operator_exception"):
            response = wire._execute_serialized_request(
                json.dumps(request)
            )

        self.assertTrue(response["provider_reset_required"])
        self.assertEqual(
            response["accessibility"]["failure"]["code"],
            "runtime_startup_failed",
        )


class ExternalDesktopTransitionTests(unittest.TestCase):
    def test_new_active_native_window_missing_from_ax_inventory_is_external(self):
        before = {"activeWindow": "0x1", "windowIds": {"0x1"}}
        after = {"activeWindow": "0x2", "windowIds": {"0x1", "0x2"}}
        self.assertTrue(
            runtime.external_desktop_transition_after_ax_mutation(
                before,
                after,
                {("0", "frame", "Chrome")},
                {("0", "frame", "Chrome")},
            )
        )

    def test_ax_visible_window_transition_is_not_external(self):
        before = {"activeWindow": "0x1", "windowIds": {"0x1"}}
        after = {"activeWindow": "0x2", "windowIds": {"0x1", "0x2"}}
        self.assertFalse(
            runtime.external_desktop_transition_after_ax_mutation(
                before,
                after,
                {("0", "frame", "Main")},
                {("0", "frame", "Main"), ("1", "dialog", "Save As")},
            )
        )

    def test_existing_window_focus_change_is_not_external(self):
        before = {"activeWindow": "0x1", "windowIds": {"0x1", "0x2"}}
        after = {"activeWindow": "0x2", "windowIds": {"0x1", "0x2"}}
        self.assertFalse(
            runtime.external_desktop_transition_after_ax_mutation(
                before,
                after,
                {("0", "frame", "Main")},
                {("0", "frame", "Main")},
            )
        )


class WindowActionMutationBoundaryTests(unittest.TestCase):
    def test_successful_window_action_exports_typed_mutation(self):
        result = wire._window_result({
            "ok": True,
            "input_events_emitted": True,
            "desktop_state_mutated": True,
            "mutation_unknown": False,
            "actionEvidence": {
                "route": "native_window_command",
                "input_emitted": True,
            },
        })

        self.assertEqual(result["mutation"], {
            "input_events_emitted": True,
            "partial_input_possible": False,
            "desktop_state_mutated": True,
            "mutation_unknown": False,
        })
        self.assertEqual(
            result["action_evidence"]["route"],
            "native_window_command",
        )

    def test_window_failure_exports_recovery_context(self):
        recovery_context = {
            "sourceAppStatus": "alive",
            "sourceWindowStatus": "gone",
            "inventoryComplete": True,
        }
        result = wire._window_result({
            "ok": False,
            "failureCode": "effect_unknown",
            "mutation_unknown": True,
            "recoveryContext": recovery_context,
        })

        self.assertEqual(result["recovery_context"], recovery_context)
        self.assertTrue(result["mutation"]["mutation_unknown"])


class PhaseTimingTests(unittest.TestCase):
    """Phase timing is an operator diagnostic and never reaches responses."""

    def _run(self, value):
        stderr = io.StringIO()
        with mock.patch.dict(os.environ, {"COMPUTER_USE_PROVIDER_TIMING": value}), \
                mock.patch("sys.stderr", stderr):
            phase_timing.start()
            phase_timing.lap("first")
            phase_timing.lap("second")
            phase_timing.finish("observe", "req-1")
        return stderr.getvalue()

    def test_timing_is_silent_by_default(self):
        self.assertEqual(self._run(""), "")

    def test_enabled_timing_writes_one_diagnostic_line(self):
        lines = self._run("1").splitlines()
        self.assertEqual(len(lines), 1)
        record = json.loads(lines[0])
        self.assertEqual(record["stage"], "timing")
        self.assertEqual(record["request_id"], "req-1")
        detail = json.loads(record["diagnostic"])
        self.assertEqual(sorted(detail["phasesMs"]), ["first", "second"])
