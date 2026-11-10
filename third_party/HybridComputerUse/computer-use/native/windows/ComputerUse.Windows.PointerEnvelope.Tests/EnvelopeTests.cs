using System.Text.Json.Nodes;
using ComputerUse.Windows.Runtime;

if (args is ["--emit-wire-fixtures", var output])
{
    FailureWireFixtures.Write(output);
    return 0;
}
if (args is ["--native-manifest", var fixtureRoot])
{
    NativeManifestTests.Run(fixtureRoot);
    return 0;
}

const string message = "No requested pointer input was emitted. Window state may have changed. Stop; do not replay or observe.";
var passed = 0;
var failed = 0;
void Check(bool condition, string detail)
{
    if (!condition) { throw new InvalidOperationException(detail); }
}
void Run(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error.Message}"); }
}
JsonObject Payload(ProviderFailureException error) =>
    (JsonObject)ProviderServer.TestEnvelope(error)["accessibility"]!;
ProviderFailureException Pointer(
    bool input = false, bool evidenceInput = false, bool unknown = true, bool facts = false) =>
    new("pointer_preparation_failed", "emission", message,
        inputEventsEmitted: input, mutationUnknown: unknown,
        actionEvidence: new JsonObject
        {
            ["route"] = "input.send_input.wheel", ["input_emitted"] = evidenceInput,
        },
        factsComplete: facts,
        recoveryContext: new JsonObject { ["must_not_escape"] = true },
        pointerPreparation: PointerPreparationState.ActivationAttemptedOutcomeUnknown);

void CheckPointer(JsonObject payload, string expectedMessage = message)
{
    Check(payload["failure"]!["code"]!.GetValue<string>() == "pointer_preparation_failed", "Code was replaced");
    Check(payload["failure"]!["phase"]!.GetValue<string>() == "emission", "Wrong phase");
    Check(payload["failure"]!["facts_complete"]!.GetValue<bool>() == false, "Facts became complete");
    Check(payload["error"]!.GetValue<string>() == expectedMessage, "Preparation message was replaced");
    var mutation = (JsonObject)payload["mutation"]!;
    Check(mutation.Count == 4, "Unexpected mutation fields");
    Check(mutation["input_events_emitted"]!.GetValue<bool>() == false, "Invented input");
    Check(mutation["partial_input_possible"]!.GetValue<bool>() == false, "Invented partial input");
    Check(mutation["mutation_unknown"]!.GetValue<bool>(), "Lost preparation uncertainty");
    Check(mutation["pointer_preparation"]!.GetValue<string>() == "activation_attempted_outcome_unknown", "Missing pointer state");
    Check(!mutation.ContainsKey("desktop_state_mutated"), "Invented desktop false");
    Check(!payload.ContainsKey("recovery_context"), "Recovery escaped");
    Check(!payload["failure"]!.AsObject().ContainsKey("underlying_code"), "Unexpected rewrite");
}

Run("pointer-envelope-exact", () => CheckPointer(Payload(Pointer())));
Run("action-catch-preserves-evidence-only-emission", () =>
{
    var error = new ProviderFailureException("dispatch_failed", "emission", "evidence",
        factsComplete: true,
        actionEvidence: new JsonObject { ["input_emitted"] = true });
    var rebuilt = UiaMutationFailureAttribution.Preserve(error, false,
        new JsonObject { ["input_emitted"] = false, ["point_owner_verified"] = true });
    var payload = Payload(rebuilt);
    Check(rebuilt.InputEventsEmitted &&
        rebuilt.ActionEvidence!["input_emitted"]!.GetValue<bool>(), "Rebuild erased input evidence");
    Check(payload["mutation"]!["input_events_emitted"]!.GetValue<bool>() &&
        payload["mutation"]!["mutation_unknown"]!.GetValue<bool>() &&
        !payload["failure"]!["facts_complete"]!.GetValue<bool>(), "Envelope invented zero side effects");
    Check(rebuilt.ActionEvidence!["point_owner_verified"]!.GetValue<bool>(), "Lost hit evidence");
});
Run("pointer-tag-overrides-complete-facts", () =>
    CheckPointer(Payload(Pointer(unknown: false, facts: true))));
Run("pointer-does-not-mutate-exception-evidence", () =>
{
    var error = Pointer();
    var before = error.ActionEvidence!.ToJsonString();
    _ = Payload(error);
    Check(error.ActionEvidence.ToJsonString() == before && error.RecoveryContext!.Count == 1,
        "Input evidence mutated");
});
foreach (var source in new[] { "flag", "evidence", "both" })
{
    Run("emitted-input-wins-" + source, () =>
    {
        var payload = Payload(Pointer(input: source != "evidence", evidenceInput: source != "flag"));
        var mutation = (JsonObject)payload["mutation"]!;
        Check(mutation["input_events_emitted"]!.GetValue<bool>(), "Emission was erased");
        Check(mutation["partial_input_possible"]!.GetValue<bool>(), "Emission uncertainty erased");
        Check(mutation["desktop_state_mutated"]!.GetValue<bool>(), "Emission became desktop false");
        Check(!mutation.ContainsKey("pointer_preparation"), "Zero-input tag survived emission");
        Check(payload["failure"]!["code"]!.GetValue<string>() == "effect_unknown", "Wrong emitted code");
        Check(!payload.ContainsKey("recovery_context"), "Tagged failure leaked recovery");
    });
}
Run("untagged-uncertain-dispatch", () =>
{
    var payload = Payload(new("dispatch_unknown", "emission", "unknown", mutationUnknown: true));
    var mutation = (JsonObject)payload["mutation"]!;
    Check(mutation["partial_input_possible"]!.GetValue<bool>(), "Genuine partial uncertainty erased");
    Check(mutation["mutation_unknown"]!.GetValue<bool>(), "Uncertainty erased");
    Check(!mutation.ContainsKey("desktop_state_mutated"), "Unknown became desktop false");
    Check(!mutation.ContainsKey("pointer_preparation"), "Invented preparation tag");
});
Run("untagged-zero-input-preflight", () =>
{
    var payload = Payload(new("target_offscreen", "precondition", "hidden", factsComplete: true));
    Check(payload["failure"]!["facts_complete"]!.GetValue<bool>(), "Preflight became uncertain");
    Check(payload["mutation"]!["desktop_state_mutated"]!.GetValue<bool>() == false, "Lost known zero state");
    Check(payload["mutation"]!["partial_input_possible"]!.GetValue<bool>() == false, "Invented partial");
});
Run("existing-focus-contract", () =>
{
    var payload = Payload(new("focus_verification_failed", "emission", "focus stop",
        factsComplete: true, recoveryContext: new JsonObject { ["forbidden"] = true },
        focusPreparation: FocusPreparationState.RequestAttemptedOutcomeUnknown));
    var mutation = (JsonObject)payload["mutation"]!;
    Check(payload["failure"]!["code"]!.GetValue<string>() == "focus_verification_failed", "Focus code changed");
    Check(payload["error"]!.GetValue<string>() == "focus stop", "Focus message changed");
    Check(!payload["failure"]!["facts_complete"]!.GetValue<bool>(), "Focus facts complete");
    Check(mutation["focus_preparation"]!.GetValue<string>() == "request_attempted_outcome_unknown", "Focus tag changed");
    Check(!mutation.ContainsKey("desktop_state_mutated") && !mutation.ContainsKey("pointer_preparation"), "Focus mutation changed");
    Check(!mutation["partial_input_possible"]!.GetValue<bool>() && mutation["mutation_unknown"]!.GetValue<bool>(), "Focus uncertainty changed");
    Check(!payload.ContainsKey("recovery_context"), "Focus recovery escaped");
});
Run("focus-emission-evidence-still-wins", () =>
{
    var payload = Payload(new("focus_verification_failed", "emission", "focus",
        actionEvidence: new JsonObject { ["input_emitted"] = true },
        focusPreparation: FocusPreparationState.RequestAttemptedOutcomeUnknown));
    Check(payload["mutation"]!["input_events_emitted"]!.GetValue<bool>(), "Focus emission erased");
    Check(!payload["mutation"]!.AsObject().ContainsKey("focus_preparation"), "False focus zero-input tag");
});
Run("desktop-uncertainty-does-not-claim-false", () =>
{
    var response = ProviderServer.TestEnvelope(
        new("dispatch_unknown", "emission", "unknown", mutationUnknown: true), "desktop_execute_batch");
    var payload = response["desktop"]!;
    Check(!payload["mutation"]!.AsObject().ContainsKey("desktop_state_mutated"), "Top-level desktop false");
    Check(!payload["gui_result"]!.AsObject().ContainsKey("desktop_state_mutated"), "GUI desktop false");
    Check(payload["gui_result"]!["partial_input_possible"]!.GetValue<bool>(), "GUI partial erased");
});
foreach (var dispatchAttempted in new[] { false, true })
{
    Run(dispatchAttempted ? "helper-zero-sendinput-to-envelope" : "helper-predispatch-to-envelope", () =>
    {
        Exception error = dispatchAttempted
            ? new ProviderFailureException("send_input_failed", "emission", "sent=0",
                actionEvidence: new JsonObject { ["input_emitted"] = false })
            : new InvalidOperationException("Point read failed after activation");
        Check(PointerPreparationFailureAttribution.HasKnownZeroRequestedInput(
            error, dispatchAttempted, false), "Known zero input was not recognized");
        var evidence = new JsonObject
        {
            ["route"] = "input.send_input.wheel",
            ["input_emitted"] = false,
        };
        var attributed = PointerPreparationFailureAttribution.FromZeroInputError(error, evidence);
        Check(attributed.PointerPreparation == PointerPreparationState.ActivationAttemptedOutcomeUnknown,
            "Production helper omitted preparation tag");
        Check(attributed.FactsComplete == false && attributed.MutationUnknown &&
            !attributed.InputEventsEmitted, "Production helper lost zero-input/uncertainty facts");
        Check(attributed.Message.Contains("Stop; do not replay or observe.", StringComparison.Ordinal),
            "Production helper lost stop instruction");
        var payload = Payload(attributed);
        CheckPointer(payload, attributed.Message);
        Check(JsonNode.DeepEquals(payload["action_evidence"], evidence), "Action evidence changed");
    });
}
foreach (var scenario in new[] { "ambiguous", "unknown", "partial", "evidence", "local-input" })
{
    Run("helper-rejects-zero-" + scenario, () =>
    {
        Exception error = scenario switch
        {
            "ambiguous" => new InvalidOperationException("Dispatch outcome unknown"),
            "unknown" => new ProviderFailureException("dispatch_unknown", "emission", "unknown",
                mutationUnknown: true),
            "partial" => new ProviderFailureException("send_input_partial", "emission", "sent=1",
                inputEventsEmitted: true, mutationUnknown: true),
            "evidence" => new ProviderFailureException("dispatch_failed", "emission", "evidence",
                actionEvidence: new JsonObject { ["input_emitted"] = true }),
            _ => new ProviderFailureException("send_input_failed", "emission", "sent=0"),
        };
        var localInput = scenario == "local-input";
        Check(!PointerPreparationFailureAttribution.HasKnownZeroRequestedInput(error, true, localInput),
            "Unknown/emitted input became known zero");
        if (scenario is "unknown" or "partial" or "evidence")
        {
            Check(!PointerPreparationFailureAttribution.HasKnownZeroRequestedInput(error, false, false),
                "Exception evidence was ignored before dispatch");
        }
        // Exercise the existing uncertainty envelope, not a copy of Actions catch logic.
        var preserved = error is ProviderFailureException failure && !localInput
            ? failure
            : UiaMutationFailureAttribution.FromUnexpected(error, localInput,
                new JsonObject { ["input_emitted"] = localInput });
        var payload = Payload(preserved);
        var mutation = payload["mutation"]!.AsObject();
        var emitted = scenario is "partial" or "evidence" or "local-input";
        Check(mutation["input_events_emitted"]!.GetValue<bool>() == emitted, "Emission evidence lost");
        Check(mutation["partial_input_possible"]!.GetValue<bool>() &&
            mutation["mutation_unknown"]!.GetValue<bool>(), "Uncertainty was cleared");
        Check(!mutation.ContainsKey("pointer_preparation"), "Invented zero-input preparation tag");
        Check(mutation.ContainsKey("desktop_state_mutated") == emitted, "False desktop claim");
        Check(!payload["failure"]!["facts_complete"]!.GetValue<bool>(), "Unknown facts became complete");
    });
}
Console.WriteLine($"linked envelope: passed={passed} failed={failed}; no native UIA/VM");
return failed == 0 ? 0 : 1;
