using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class FocusPreparationTests
{
    internal static void Run()
    {
        CheckSuccess(deferred: false);
        CheckSuccess(deferred: true);
        foreach (var scenario in new[]
        {
            "unfocused", "request_throws", "verification_throws",
            "late_completion", "same_name_replacement",
        })
        {
            CheckFailure(scenario);
        }
        CheckNonFocusFailures();
    }

    private static void CheckSuccess(bool deferred)
    {
        var clock = TimeSpan.Zero;
        var focusCalls = 0;
        var keys = 0;
        var reads = 0;
        ExactFocusSettlement.RequestAndVerify(
            () => focusCalls++,
            () =>
            {
                reads++;
                return !deferred || clock >= TimeSpan.FromMilliseconds(50);
            },
            () => clock,
            value => clock += value);
        keys++;
        Check(focusCalls == 1 && keys == 1,
            "Verified exact focus must permit the following key exactly once.");
        Check(deferred
                ? reads == 4 && clock == TimeSpan.FromMilliseconds(60)
                : reads == 1 && clock == TimeSpan.Zero,
            "Successful focus must retain the existing settlement behavior.");
    }

    private static void CheckFailure(string scenario)
    {
        var original = new FocusObject("same name");
        var replacement = new FocusObject("same name");
        FocusObject? focused = null;
        Action? completeLater = null;
        var clock = TimeSpan.Zero;
        var focusCalls = 0;
        var keys = 0;
        var reads = 0;
        var delays = 0;
        ProviderFailureException? failure = null;
        try
        {
            ExactFocusSettlement.RequestAndVerify(
                () =>
                {
                    focusCalls++;
                    if (scenario == "request_throws")
                    {
                        focused = replacement;
                        throw new InvalidOperationException("request failed after side effect");
                    }
                    if (scenario == "late_completion")
                    {
                        completeLater = () => focused = original;
                    }
                    if (scenario == "same_name_replacement")
                    {
                        focused = replacement;
                    }
                },
                () =>
                {
                    reads++;
                    if (scenario == "verification_throws")
                    {
                        throw new InvalidOperationException("original object retired");
                    }
                    return ReferenceEquals(focused, original);
                },
                () => clock,
                value => { clock += value; delays++; });
            keys++;
        }
        catch (ProviderFailureException error)
        {
            failure = error;
        }
        Check(failure is
            {
                Code: "focus_verification_failed",
                Phase: "emission",
                InputEventsEmitted: false,
                MutationUnknown: true,
                FactsComplete: false,
                FocusPreparation: FocusPreparationState.RequestAttemptedOutcomeUnknown,
            }, $"{scenario}: focus failure must preserve known zero-key truth.");
        Check(focusCalls == 1 && keys == 0,
            $"{scenario}: failure must never repeat focus or send keys.");
        if (scenario == "request_throws")
        {
            Check(reads == 0 && delays == 0,
                "A throwing focus request must not start verification or recovery.");
        }
        else if (scenario == "verification_throws")
        {
            Check(reads == 1 && delays == 0,
                "A throwing verification must stop without further reads.");
        }
        else
        {
            Check(reads == 31 && delays == 30 &&
                clock == ExactFocusSettlement.Budget,
                $"{scenario}: failed verification must preserve bounded sampling.");
        }
        var sealedReads = reads;
        var sealedDelays = delays;
        foreach (var intent in new[] { "type", "keypress", "set_value" })
        {
            CheckEnvelope(failure!, intent);
        }
        if (scenario == "late_completion")
        {
            Check(completeLater is not null && focused is null,
                "Deferred request must remain pending when the failure is returned.");
            clock += TimeSpan.FromMilliseconds(1);
            completeLater!();
            Check(ReferenceEquals(focused, original),
                "Focus may complete after the failure response with no keys.");
        }
        if (scenario is "same_name_replacement" or "request_throws")
        {
            Check(original.Name == replacement.Name &&
                ReferenceEquals(focused, replacement) &&
                !ReferenceEquals(focused, original),
                "Replacement focus is a side effect, never exact-target success.");
        }
        Check(reads == sealedReads && delays == sealedDelays &&
            focusCalls == 1 && keys == 0,
            $"{scenario}: failure response and late completion must not resume work.");
    }

    internal static void CheckEnvelope(ProviderFailureException failure, string intent)
    {
        using var server = new ProviderServer();
        var request = Request("FocusPreparationFailure", intent);
        UiaProvider.FocusPreparationFailureForTest = failure;
        JsonObject response;
        try
        {
            response = server.Dispatch(request);
        }
        finally
        {
            UiaProvider.FocusPreparationFailureForTest = null;
        }
        // Test the actual serialized contract, including absent versus false.
        var wire = JsonNode.Parse(response.ToJsonString())!;
        var payload = wire["accessibility"]!;
        var facts = payload["failure"]!;
        var mutation = (JsonObject)payload["mutation"]!;
        Check(payload["ok"]!.GetValue<bool>() == false &&
            facts["code"]!.GetValue<string>() == "focus_verification_failed" &&
            facts["phase"]!.GetValue<string>() == "emission" &&
            facts["facts_complete"]!.GetValue<bool>() == false &&
            facts["underlying_code"] is null,
            "Focus failure must not be rewritten as generic effect_unknown.");
        Check(mutation["input_events_emitted"]!.GetValue<bool>() == false &&
            mutation["partial_input_possible"]!.GetValue<bool>() == false &&
            mutation["mutation_unknown"]!.GetValue<bool>() &&
            mutation["focus_preparation"]!.GetValue<string>() ==
                "request_attempted_outcome_unknown" &&
            !mutation.ContainsKey("desktop_state_mutated"),
            "Focus uncertainty must not imply possible keys or zero desktop effects.");
        Check(payload["snapshot"] is null &&
            payload["recovery_context"] is null &&
            wire["provider_reset_required"] is null,
            "Focus failure must return no recovery observation or provider reset.");
        var message = payload["error"]!.GetValue<string>();
        Check(message.Contains("No requested keys were emitted.", StringComparison.Ordinal) &&
            message.Contains("may change later", StringComparison.Ordinal) &&
            message.Contains("Stop; do not replay or observe.", StringComparison.Ordinal) &&
            !message.Contains("verify current", StringComparison.OrdinalIgnoreCase),
            "Focus failure must state known keys, possible late focus, and stop.");
    }

    private static void CheckNonFocusFailures()
    {
        using var server = new ProviderServer();
        foreach (var (app, code, phase, emitted, unknown) in new[]
        {
            ("EvidenceFailure", "effect_unknown", "post_action_observation", true, true),
            ("DispatchOutcomeUnknown", "effect_unknown", "emission", false, true),
            ("ActivationFailure", "window_activation_failed", "emission", false, false),
            ("StaleElement", "stale_element_ref", "precondition", false, false),
        })
        {
            var response = server.Dispatch(Request(app, "keypress"));
            var payload = response["accessibility"]!;
            var failure = payload["failure"]!;
            var mutation = (JsonObject)payload["mutation"]!;
            Check(failure["code"]!.GetValue<string>() == code &&
                failure["phase"]!.GetValue<string>() == phase &&
                failure["facts_complete"]!.GetValue<bool>() == !unknown &&
                mutation["input_events_emitted"]!.GetValue<bool>() == emitted &&
                (unknown && !emitted
                    ? !mutation.ContainsKey("desktop_state_mutated")
                    : mutation["desktop_state_mutated"]?.GetValue<bool>() == emitted) &&
                mutation["partial_input_possible"]!.GetValue<bool>() == unknown &&
                mutation["mutation_unknown"]!.GetValue<bool>() == unknown &&
                !mutation.ContainsKey("focus_preparation"),
                $"{app}: non-focus failure semantics must remain unchanged.");
        }
    }

    private static JsonObject Request(string app, string intent)
    {
        var inputField = intent switch
        {
            "type" => "text",
            "keypress" => "key",
            _ => "value",
        };
        return new JsonObject
        {
            ["protocol"] = "computer-use-provider",
            ["schema_version"] = 2,
            ["request_id"] = "focus-preparation-" + intent,
            ["operation"] = "resolve_and_act",
            ["action"] = new JsonObject
            {
                ["protocol_version"] = 2,
                ["intent"] = intent,
                ["app"] = app,
                ["target"] = new JsonObject
                {
                    ["selector"] = "entry \"Name\"",
                    ["hasName"] = false,
                },
                ["input"] = new JsonObject { [inputField] = "A" },
            },
        };
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private sealed class FocusObject(string name)
    {
        internal string Name { get; } = name;
    }
}
