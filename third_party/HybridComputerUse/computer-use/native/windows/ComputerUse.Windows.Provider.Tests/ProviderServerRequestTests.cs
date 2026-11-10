using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class ProviderServerRequestTests
{
    internal static void Run()
    {
        using (var server = new ProviderServer())
        {
            var constrained = JsonNode.Parse("""
                {"protocol":"computer-use-provider","schema_version":2,
                 "request_id":"semantic-only","operation":"resolve_and_act",
                 "action":{"protocol_version":2,"intent":"set_value","app":"Editor",
                 "element":{"index":1},"input":{"value":"replacement","require_semantic_write":true}}}
                """)!.AsObject();
            ProviderServer.ValidateRequest(constrained);
            constrained["action"]!["input"]!["require_semantic_write"] = "true";
            try
            {
                ProviderServer.ValidateRequest(constrained);
                throw new InvalidDataException("Semantic write constraint must be boolean.");
            }
            catch (ProviderFailureException error) when (error.Code == "invalid_request") { }

            foreach (var emitted in new[] { false, true })
            {
                var textEvidence = new JsonObject
                {
                    ["route"] = "input.window_messages",
                    ["attempted_messages"] = emitted ? 2 : 1,
                    ["returned_messages"] = emitted ? 1 : 0,
                    ["unknown_message_index"] = emitted ? 1 : 0,
                    ["effect_verified"] = false,
                };
                DesktopProvider.FailureForTest = new ProviderFailureException(
                    "desktop_input_failed", "emission", "unknown text completion",
                    inputEventsEmitted: emitted, mutationUnknown: true,
                    actionEvidence: textEvidence);
                try
                {
                    var request = JsonNode.Parse("""
                        {"protocol":"computer-use-provider","schema_version":2,
                         "request_id":"text-unknown","operation":"desktop_execute_batch",
                         "desktop":{"protocol_version":2,"settle_ms":0,
                         "actions":[{"type":"type","text":"abc"}]}}
                        """)!.AsObject();
                    ProviderServer.ValidateRequest(request);
                    var payload = server.Dispatch(request)["desktop"]!.AsObject();
                    if (payload["error"]?.GetValue<string>() != "unknown text completion" ||
                        payload["failure"]?["phase"]?.GetValue<string>() != "emission" ||
                        payload["mutation"]?["input_events_emitted"]?.GetValue<bool>() != emitted ||
                        payload["mutation"]?["mutation_unknown"]?.GetValue<bool>() != true ||
                        payload["action_evidence"]?["unknown_message_index"]?.GetValue<int>() != (emitted ? 1 : 0) ||
                        payload["action_evidence"]?["effect_verified"]?.GetValue<bool>() != false ||
                        !JsonNode.DeepEquals(payload["action_evidence"], textEvidence))
                    {
                        throw new InvalidDataException("GUI failure envelope lost native text delivery evidence.");
                    }
                }
                finally { DesktopProvider.FailureForTest = null; }
            }
            var staleDesktop = JsonNode.Parse(
                """
                {
                  "protocol":"computer-use-provider",
                  "schema_version":2,
                  "request_id":"stale-desktop",
                  "operation":"desktop_execute_batch",
                  "desktop":{
                    "protocol_version":2,
                    "actions":[{"type":"click","x":10,"y":20}],
                    "settle_ms":0,
                    "frame_id":"stale-fixture"
                  }
                }
                """) as JsonObject ??
                throw new InvalidDataException(
                    "stale desktop request must be an object");
            var staleDesktopResponse = server.Dispatch(staleDesktop);
            var staleDesktopPayload = staleDesktopResponse["desktop"] as JsonObject ??
                throw new InvalidDataException("stale desktop payload is missing");
            var staleDesktopFailure =
                staleDesktopPayload["failure"] as JsonObject ??
                throw new InvalidDataException("stale desktop failure is missing");
            var staleDesktopMutation =
                staleDesktopPayload["mutation"] as JsonObject ??
                throw new InvalidDataException("stale desktop mutation is missing");
            var staleDesktopSnapshot =
                staleDesktopPayload["snapshot"] as JsonObject ??
                throw new InvalidDataException(
                    "stale desktop recovery snapshot is missing");
            var staleDesktopResult =
                staleDesktopPayload["gui_result"] as JsonObject ??
                throw new InvalidDataException("stale desktop result is missing");
            if (staleDesktopFailure["code"]?.GetValue<string>() != "stale_layout" ||
                staleDesktopFailure["phase"]?.GetValue<string>() != "precondition" ||
                staleDesktopFailure["facts_complete"]?.GetValue<bool>() != true ||
                staleDesktopMutation["input_events_emitted"]?.GetValue<bool>() !=
                    false ||
                staleDesktopMutation["partial_input_possible"]?.GetValue<bool>() !=
                    false ||
                staleDesktopMutation["mutation_unknown"]?.GetValue<bool>() != false ||
                staleDesktopSnapshot["frameId"]?.GetValue<string>() !=
                    "fresh-fixture" ||
                staleDesktopSnapshot["width"]?.GetValue<int>() != 1920 ||
                staleDesktopSnapshot["height"]?.GetValue<int>() != 1080 ||
                staleDesktopResult["quiescent"]?.GetValue<bool>() != true)
            {
                throw new InvalidDataException(
                    "Stale desktop failure did not return a fresh zero-input recovery snapshot.");
            }

            var staleAfterInput = JsonNode.Parse(
                """
                {
                  "protocol":"computer-use-provider",
                  "schema_version":2,
                  "request_id":"stale-after-input",
                  "operation":"desktop_execute_batch",
                  "desktop":{
                    "protocol_version":2,
                    "actions":[{"type":"keypress","keys":["ESC"]},{"type":"click","x":10,"y":20}],
                    "settle_ms":0,
                    "frame_id":"stale-after-input-fixture"
                  }
                }
                """) as JsonObject ??
                throw new InvalidDataException(
                    "stale-after-input request must be an object");
            var staleAfterInputResponse = server.Dispatch(staleAfterInput);
            var staleAfterInputPayload =
                staleAfterInputResponse["desktop"] as JsonObject ??
                throw new InvalidDataException(
                    "stale-after-input payload is missing");
            var staleAfterInputFailure =
                staleAfterInputPayload["failure"] as JsonObject ??
                throw new InvalidDataException(
                    "stale-after-input failure is missing");
            var staleAfterInputSnapshot =
                staleAfterInputPayload["snapshot"] as JsonObject ??
                throw new InvalidDataException(
                    "stale-after-input recovery snapshot is missing");
            if (staleAfterInputFailure["code"]?.GetValue<string>() !=
                    "effect_unknown" ||
                staleAfterInputFailure["underlying_code"]?.GetValue<string>() !=
                    "stale_layout" ||
                staleAfterInputSnapshot["frameId"]?.GetValue<string>() !=
                    "fresh-fixture")
            {
                throw new InvalidDataException(
                    "Mid-batch stale layout did not preserve uncertainty with a fresh snapshot.");
            }

            var internalFailure = JsonNode.Parse(
                """
                {
                  "protocol":"computer-use-provider",
                  "schema_version":2,
                  "request_id":"internal-reset",
                  "operation":"observe_apps",
                  "observe_apps":{"protocol_version":2}
                }
                """) as JsonObject ??
                throw new InvalidDataException("internal failure request must be an object");
            var response = server.Dispatch(internalFailure);
            if (response["provider_reset_required"]?.GetValue<bool>() != true)
            {
                throw new InvalidDataException(
                    "Unexpected provider exceptions must require provider reset.");
            }

            var evidenceFailure = JsonNode.Parse(
                """
                {
                  "protocol":"computer-use-provider",
                  "schema_version":2,
                  "request_id":"evidence-failure",
                  "operation":"resolve_and_act",
                  "action":{
                    "protocol_version":2,
                    "intent":"set_value",
                    "app":"EvidenceFailure",
                    "target":{
                      "selector":"entry \"Name\"",
                      "hasName":false
                    },
                    "input":{"value":"Ada"}
                  }
                }
                """) as JsonObject ??
                throw new InvalidDataException("evidence failure request must be an object");
            var evidenceResponse = server.Dispatch(evidenceFailure);
            var accessibility = evidenceResponse["accessibility"] as JsonObject ??
                throw new InvalidDataException("evidence failure payload is missing");
            var failure = accessibility["failure"] as JsonObject ??
                throw new InvalidDataException("evidence failure object is missing");
            var mutation = accessibility["mutation"] as JsonObject ??
                throw new InvalidDataException("evidence mutation object is missing");
            var evidence = accessibility["action_evidence"] as JsonObject ??
                throw new InvalidDataException("action evidence is missing");
            if (failure["code"]?.GetValue<string>() != "effect_unknown" ||
                failure["underlying_code"]?.GetValue<string>() != "native_write_failed" ||
                mutation["input_events_emitted"]?.GetValue<bool>() != true ||
                mutation["mutation_unknown"]?.GetValue<bool>() != true ||
                evidence["input_emitted"]?.GetValue<bool>() != true)
            {
                throw new InvalidDataException(
                    "Post-emission failure evidence is not monotonic.");
            }
            if (evidenceResponse["provider_reset_required"] is not null)
            {
                throw new InvalidDataException(
                    "Typed operation failures must not force a provider reset.");
            }

            var activationFailure = JsonNode.Parse(
                """
                {
                  "protocol":"computer-use-provider",
                  "schema_version":2,
                  "request_id":"activation-failure",
                  "operation":"resolve_and_act",
                  "action":{
                    "protocol_version":2,
                    "intent":"keypress",
                    "app":"ActivationFailure",
                    "window_handle":"w2:fixture",
                    "input":{"key":"ALT+ENTER"}
                  }
                }
                """) as JsonObject ??
                throw new InvalidDataException(
                    "activation failure request must be an object");
            var activationResponse = server.Dispatch(activationFailure);
            var activationAccessibility =
                activationResponse["accessibility"] as JsonObject ??
                throw new InvalidDataException(
                    "activation failure payload is missing");
            var activationFailureFacts =
                activationAccessibility["failure"] as JsonObject ??
                throw new InvalidDataException(
                    "activation failure facts are missing");
            var activationMutation =
                activationAccessibility["mutation"] as JsonObject ??
                throw new InvalidDataException(
                    "activation mutation facts are missing");
            if (activationFailureFacts["code"]?.GetValue<string>() !=
                    "window_activation_failed" ||
                activationFailureFacts["phase"]?.GetValue<string>() != "emission" ||
                activationFailureFacts["facts_complete"]?.GetValue<bool>() != true ||
                activationMutation["input_events_emitted"]?.GetValue<bool>() != false ||
                activationMutation["partial_input_possible"]?.GetValue<bool>() !=
                    false ||
                activationMutation["mutation_unknown"]?.GetValue<bool>() != false)
            {
                throw new InvalidDataException(
                    "Pre-input activation failure was incorrectly reported as effect_unknown.");
            }

            var dispatchOutcomeUnknown = JsonNode.Parse(
                """
                {
                  "protocol":"computer-use-provider",
                  "schema_version":2,
                  "request_id":"dispatch-outcome-unknown",
                  "operation":"resolve_and_act",
                  "action":{
                    "protocol_version":2,
                    "intent":"click",
                    "app":"DispatchOutcomeUnknown",
                    "target":{
                      "selector":"button \"Run\"",
                      "hasName":false
                    },
                    "input":{"click_count":1},
                    "observation":{"capture_snapshot":false}
                  }
                }
                """) as JsonObject ??
                throw new InvalidDataException(
                    "dispatch outcome request must be an object");
            var dispatchResponse = server.Dispatch(dispatchOutcomeUnknown);
            var dispatchAccessibility =
                dispatchResponse["accessibility"] as JsonObject ??
                throw new InvalidDataException(
                    "dispatch outcome payload is missing");
            var dispatchFailure =
                dispatchAccessibility["failure"] as JsonObject ??
                throw new InvalidDataException(
                    "dispatch outcome failure facts are missing");
            var dispatchMutation =
                dispatchAccessibility["mutation"] as JsonObject ??
                throw new InvalidDataException(
                    "dispatch outcome mutation facts are missing");
            var dispatchEvidence =
                dispatchAccessibility["action_evidence"] as JsonObject ??
                throw new InvalidDataException(
                    "dispatch outcome action evidence is missing");
            if (dispatchFailure["code"]?.GetValue<string>() != "effect_unknown" ||
                dispatchFailure["underlying_code"]?.GetValue<string>() !=
                    "action_dispatch_outcome_unknown" ||
                dispatchFailure["phase"]?.GetValue<string>() != "emission" ||
                dispatchFailure["facts_complete"]?.GetValue<bool>() != false ||
                dispatchMutation["input_events_emitted"]?.GetValue<bool>() != false ||
                dispatchMutation["partial_input_possible"]?.GetValue<bool>() != true ||
                dispatchMutation["mutation_unknown"]?.GetValue<bool>() != true ||
                dispatchEvidence["input_emitted"]?.GetValue<bool>() != false ||
                !dispatchAccessibility["error"]!.GetValue<string>().Contains(
                    "do not repeat the same request",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Ambiguous UIA dispatch failure was mislabeled as an observation failure.");
            }

            Exception? cleanupDiagnostic = null;
            Exception? preservedFailure = null;
            try
            {
                using var cleanup = new FailurePreservingDisposable(
                    new ThrowingDisposable(),
                    error => cleanupDiagnostic = error);
                throw UiaMutationFailureAttribution.FromUnexpected(
                    new TimeoutException("Synthetic UIA transaction timeout."),
                    inputEmitted: false,
                    actionEvidence: new JsonObject
                    {
                        ["route"] = "uia.pattern.invoke",
                        ["geometry_sampled"] = false,
                        ["readback_sampled"] = false,
                        ["input_emitted"] = false,
                    });
            }
            catch (Exception error)
            {
                preservedFailure = error;
            }
            if (preservedFailure is not ProviderFailureException {
                    Code: "action_dispatch_outcome_unknown",
                    MutationUnknown: true
                } ||
                cleanupDiagnostic is not TimeoutException)
            {
                throw new InvalidDataException(
                    "Cleanup failure replaced the primary unknown mutation result.");
            }

            var disabledEvidence = new JsonObject
            {
                ["route"] = "uia.pattern.invoke",
                ["geometry_sampled"] = false,
                ["readback_sampled"] = false,
                ["input_emitted"] = false,
            };
            var disabledFailure = UiaMutationFailureAttribution.FromUnexpected(
                new FlaUI.Core.Exceptions.ElementNotEnabledException(),
                inputEmitted: false,
                actionEvidence: disabledEvidence);
            if (disabledFailure is not {
                    Code: "target_not_enabled",
                    Phase: "precondition",
                    InputEventsEmitted: false,
                    MutationUnknown: false,
                    FactsComplete: true
                } ||
                !ReferenceEquals(disabledFailure.ActionEvidence, disabledEvidence))
            {
                throw new InvalidDataException(
                    "A disabled UIA target was not attributed as a complete zero-input precondition failure.");
            }

            var disabledAfterInputFailure =
                UiaMutationFailureAttribution.FromUnexpected(
                    new FlaUI.Core.Exceptions.ElementNotEnabledException(),
                    inputEmitted: true,
                    actionEvidence: disabledEvidence);
            if (disabledAfterInputFailure is not {
                    Code: "post_action_observation_failed",
                    Phase: "post_action_observation",
                    InputEventsEmitted: true,
                    MutationUnknown: true
                })
            {
                throw new InvalidDataException(
                    "A disabled-target exception after input incorrectly erased mutation uncertainty.");
            }

            var staleElement = JsonNode.Parse(
                """
                {
                  "protocol":"computer-use-provider",
                  "schema_version":2,
                  "request_id":"stale-element",
                  "operation":"resolve_and_act",
                  "action":{
                    "protocol_version":2,
                    "intent":"click",
                    "app":"StaleElement",
                    "target":{
                      "selector":"button \"Save\"",
                      "providerObjectPath":"runtime:1.2.3",
                      "hasName":false
                    },
                    "input":{"click_count":1}
                  }
                }
                """) as JsonObject ??
                throw new InvalidDataException("stale element request must be an object");
            var staleResponse = server.Dispatch(staleElement);
            var staleAccessibility = staleResponse["accessibility"] as JsonObject ??
                throw new InvalidDataException("stale element payload is missing");
            var staleFailure = staleAccessibility["failure"] as JsonObject ??
                throw new InvalidDataException("stale element failure is missing");
            var staleMutation = staleAccessibility["mutation"] as JsonObject ??
                throw new InvalidDataException("stale element mutation is missing");
            if (staleFailure["code"]?.GetValue<string>() != "stale_element_ref" ||
                staleFailure["phase"]?.GetValue<string>() != "precondition" ||
                staleFailure["facts_complete"]?.GetValue<bool>() != true ||
                staleMutation["input_events_emitted"]?.GetValue<bool>() != false ||
                staleMutation["partial_input_possible"]?.GetValue<bool>() != false ||
                staleMutation["mutation_unknown"]?.GetValue<bool>() != false)
            {
                throw new InvalidDataException(
                    "Stale element failure does not satisfy beta59 no-input facts.");
            }
        }
    }
}
