using System.Text.Json.Nodes;
using ComputerUse.Windows.Runtime;

internal static class FailureWireFixtures
{
    internal static void Write(string output)
    {
        var fixtures = new JsonObject();
        void Add(string name, Exception error, string operation = "resolve_and_act") =>
            fixtures[name] = ProviderServer.TestFailureEnvelope(error, operation);
        JsonObject Evidence(bool emitted = false) => new()
        {
            ["route"] = "input.send_input.keyboard", ["geometry_sampled"] = false,
            ["geometry_stable"] = true, ["window_focus_verified"] = false,
            ["target_focus_verified"] = false, ["point_owner_verified"] = false,
            ["enabled"] = true, ["editable"] = true, ["focus_verified"] = false,
            ["editable_verified"] = true, ["readback_sampled"] = false,
            ["readback_verified"] = false, ["input_emitted"] = emitted,
        };
        Add("focus", new ProviderFailureException("focus_verification_failed", "emission", "focus uncertain",
            focusPreparation: FocusPreparationState.RequestAttemptedOutcomeUnknown, actionEvidence: Evidence()));
        Add("pointer", new ProviderFailureException("pointer_preparation_failed", "emission", "activation uncertain",
            pointerPreparation: PointerPreparationState.ActivationAttemptedOutcomeUnknown, actionEvidence: Evidence()));
        Add("emission_overrides_preparation", new ProviderFailureException("pointer_preparation_failed", "emission", "input emitted",
            pointerPreparation: PointerPreparationState.ActivationAttemptedOutcomeUnknown, actionEvidence: Evidence(true)));
        Add("dispatch_unknown", new ProviderFailureException("action_dispatch_outcome_unknown", "emission", "dispatch uncertain",
            mutationUnknown: true, actionEvidence: Evidence()));
        Add("precondition", new ProviderFailureException("target_not_enabled", "precondition", "disabled",
            factsComplete: true, subject: "element", actionEvidence: Evidence()));
        Add("reset", new InvalidOperationException("unexpected failure"));
        Add("window", new ProviderFailureException("window_activation_failed", "emission", "activation failed",
            actionEvidence: Evidence()), "window_action");
        Add("recovery", new ProviderFailureException("post_action_observation_failed", "post_action_observation", "read failed",
            inputEventsEmitted: true, recoveryContext: new JsonObject
            {
                ["sourceAppStatus"] = "alive", ["sourceWindowStatus"] = "gone", ["inventoryComplete"] = true,
            }), "window_action");
        Add("desktop", new ProviderFailureException("native_input_failed", "emission", "input failed",
            inputEventsEmitted: true, dispatchedActions: 2, dispatchedInputActions: 1), "desktop_execute_batch");
        Add("desktop_stale", new ProviderFailureException("stale_layout", "precondition", "layout changed"), "desktop_execute_batch");
        Add("observe_stale", new ProviderFailureException("stale_window_ref", "precondition", "gone",
            factsComplete: true, subject: "window", recoveryContext: new JsonObject
            {
                ["sourceAppStatus"] = "alive", ["sourceWindowStatus"] = "gone", ["inventoryComplete"] = true,
            }), "observe");
        // The production mapping from a sampled window lifecycle to the
        // observe recovery envelope that the Go decoder and close proof read.
        foreach (var state in new[] { WindowLifecycleStatus.Alive, WindowLifecycleStatus.Gone, WindowLifecycleStatus.Unknown })
        {
            Add("observe_lifecycle_" + state.ToString().ToLowerInvariant(),
                WindowsProviderFailures.StaleWindow("The exact window is unavailable for observation.",
                    new JsonObject
                    {
                        ["sourceAppStatus"] = "alive",
                        ["sourceWindowStatus"] = WindowsWindowIdentityPolicy.RecoveryStatus(state),
                        ["inventoryComplete"] = true,
                    }, factsComplete: state != WindowLifecycleStatus.Unknown), "observe");
        }
        Add("ambiguous_app", new ProviderFailureException("ax_app_ambiguous", "precondition", "two processes",
            factsComplete: true, subject: "app", candidates: new JsonArray(
                new JsonObject { ["name"] = "explorer", ["providerBusName"] = "uia3:pid:6800", ["processGeneration"] = "1" },
                new JsonObject { ["name"] = "explorer", ["providerBusName"] = "uia3:pid:7300", ["processGeneration"] = "2" })),
            "observe");
        File.WriteAllText(output, fixtures.ToJsonString(new() { WriteIndented = true }) + "\n");
        Console.WriteLine($"Wrote {fixtures.Count} production failure envelopes to {output}");
    }
}
