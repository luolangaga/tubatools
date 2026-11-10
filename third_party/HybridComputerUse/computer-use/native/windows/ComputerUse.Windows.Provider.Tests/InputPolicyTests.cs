using System.Drawing;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class InputPolicyTests
{
    internal static void Run()
    {
        var axSettingsChord = WindowsKeyMapping.ResolveChord(
            new[] { "CTRL+," });
        if (!axSettingsChord.SequenceEqual(new ushort[] { 0x11, 0xBC }))
        {
            throw new InvalidDataException(
                "AX chord encoding did not resolve CTRL+COMMA.");
        }

        var toggleStates = new Dictionary<string, string>
        {
            ["On"] = "checked",
            ["Off"] = "unchecked",
            ["Indeterminate"] = "mixed",
        };
        foreach (var (state, expected) in toggleStates)
        {
            var actual = WindowsToggleState.Canonicalize(state);
            if (actual != expected)
            {
                throw new InvalidDataException(
                    $"Toggle state {state} serialized as {actual}, expected {expected}.");
            }
        }

        var windowBounds = new System.Drawing.Rectangle(100, 100, 800, 600);
        var zeroPointerDispatch = new ProviderFailureException(
            "input_dispatch_failed", "emission", "SendInput delivered zero events.");
        var evidenceOnlyDispatch = UiaMutationFailureAttribution.Preserve(
            new ProviderFailureException("dispatch_failed", "emission", "input in evidence",
                actionEvidence: new JsonObject { ["input_emitted"] = true }),
            false, new JsonObject { ["input_emitted"] = false });
        if (!evidenceOnlyDispatch.InputEventsEmitted ||
            evidenceOnlyDispatch.ActionEvidence!["input_emitted"]!.GetValue<bool>() != true)
        {
            throw new InvalidDataException("Action failure rebuild erased emitted input evidence.");
        }
        var nativeEvidence = new JsonObject
        {
            ["route"] = "input.window_messages",
            ["attempted_messages"] = 1,
            ["returned_messages"] = 0,
            ["unknown_message_index"] = 0,
            ["effect_verified"] = false,
        };
        var unknownText = UiaMutationFailureAttribution.Preserve(
            new ProviderFailureException("desktop_input_failed", "emission", "unknown message",
                inputEventsEmitted: false, mutationUnknown: true, actionEvidence: nativeEvidence),
            false, new JsonObject { ["route"] = "input.window_messages", ["focus_verified"] = true });
        if (unknownText.InputEventsEmitted || !unknownText.MutationUnknown ||
            unknownText.ActionEvidence?["route"]?.GetValue<string>() != "input.window_messages" ||
            unknownText.ActionEvidence?["unknown_message_index"]?.GetValue<int>() != 0 ||
            unknownText.ActionEvidence?["attempted_messages"]?.GetValue<int>() != 1 ||
            unknownText.ActionEvidence?["focus_verified"]?.GetValue<bool>() != true ||
            nativeEvidence.ContainsKey("input_emitted"))
        {
            throw new InvalidDataException("AX wrapping erased native uncertainty or changed its source evidence.");
        }
        var selectedThenUnknown = UiaMutationFailureAttribution.Preserve(
            new ProviderFailureException("desktop_input_failed", "emission", "unknown replacement",
                inputEventsEmitted: false, mutationUnknown: true, actionEvidence: nativeEvidence),
            true, new JsonObject { ["route"] = "input.send_input_and_window_messages.document_replace" });
        if (!selectedThenUnknown.InputEventsEmitted || !selectedThenUnknown.MutationUnknown ||
            selectedThenUnknown.ActionEvidence?["returned_messages"]?.GetValue<int>() != 0 ||
            selectedThenUnknown.ActionEvidence?["route"]?.GetValue<string>() !=
                "input.send_input_and_window_messages.document_replace" ||
            selectedThenUnknown.ActionEvidence?["emission_route"]?.GetValue<string>() != "input.window_messages")
        {
            throw new InvalidDataException("Earlier selection input must survive a first-message replacement failure.");
        }
        if (!PointerPreparationFailureAttribution.HasKnownZeroRequestedInput(
                zeroPointerDispatch, true, false) ||
            !PointerPreparationFailureAttribution.HasKnownZeroRequestedInput(
                new InvalidOperationException("hit failed"), false, false))
        {
            throw new InvalidDataException("Known zero pointer delivery lost activation uncertainty.");
        }
        foreach (var ambiguousPointerFailure in new Exception[]
        {
            new InvalidOperationException("dispatch completion unknown"),
            new ProviderFailureException("partial", "emission", "partial", inputEventsEmitted: true),
            new ProviderFailureException("unknown", "emission", "unknown", mutationUnknown: true),
            new ProviderFailureException("evidence", "emission", "evidence",
                actionEvidence: new JsonObject { ["input_emitted"] = true }),
        })
        {
            if (PointerPreparationFailureAttribution.HasKnownZeroRequestedInput(
                    ambiguousPointerFailure, true, false))
            {
                throw new InvalidDataException("Uncertain pointer delivery was changed into zero delivery.");
            }
        }
        var zeroPointerPreparation = PointerPreparationFailureAttribution.FromZeroInputError(
            zeroPointerDispatch, new JsonObject { ["input_emitted"] = false });
        if (zeroPointerPreparation.InputEventsEmitted ||
            !zeroPointerPreparation.MutationUnknown ||
            zeroPointerPreparation.FactsComplete != false ||
            zeroPointerPreparation.PointerPreparation !=
                PointerPreparationState.ActivationAttemptedOutcomeUnknown ||
            zeroPointerPreparation.Code != "pointer_preparation_failed")
        {
            throw new InvalidDataException("Zero pointer failure did not preserve preparation facts.");
        }
        foreach (var unknownRead in new[]
        {
            WindowsVisibilityRead.Read(() => (false, false)),
            WindowsVisibilityRead.Read(() => (false, true)),
            WindowsVisibilityRead.Read(() => throw new InvalidOperationException("client read failure")),
        })
        {
            if (unknownRead.IsOffscreen is not null ||
                unknownRead.EffectiveVisibility(true, windowBounds, windowBounds) is not null)
            {
                throw new InvalidDataException("Unknown visibility became definite via focus or default value.");
            }
            var inputCount = 0;
            try
            {
                _ = WindowsPointerPreflight.Verify(
                    () => unknownRead.IsOffscreen,
                    () => true, () => windowBounds, () => { }, _ => true, _ => true);
                inputCount++;
            }
            catch (PointerPreflightException error) when (error.Code == "visibility_unknown") { }
            if (inputCount != 0) { throw new InvalidDataException("Unknown visibility authorized pointer input."); }
        }
        if (!WindowsUiaVisibility.IsEffectivelyVisible(
                false,
                false,
                System.Drawing.Rectangle.Empty,
                windowBounds))
        {
            throw new InvalidDataException(
                "A UIA element reported on-screen was marked invisible.");
        }
        if (!WindowsUiaVisibility.IsEffectivelyVisible(
                true,
                true,
                new System.Drawing.Rectangle(120, 140, 400, 240),
                windowBounds))
        {
            throw new InvalidDataException(
                "A focused, bounded element intersecting its window was marked invisible.");
        }
        if (WindowsUiaVisibility.IsEffectivelyVisible(
                true,
                false,
                new System.Drawing.Rectangle(120, 140, 400, 240),
                windowBounds))
        {
            throw new InvalidDataException(
                "An unfocused UIA-offscreen element was marked visible.");
        }
        if (WindowsUiaVisibility.IsEffectivelyVisible(
                true,
                true,
                new System.Drawing.Rectangle(1000, 1000, 400, 240),
                windowBounds))
        {
            throw new InvalidDataException(
                "A focused element outside its owning window was marked visible.");
        }
        if (WindowsUiaVisibility.IsEffectivelyVisible(
                true,
                true,
                System.Drawing.Rectangle.Empty,
                windowBounds))
        {
            throw new InvalidDataException(
                "A focused element without geometry was marked visible.");
        }

        var guiSettingsChord = WindowsKeyMapping.ResolveChord(
            new[] { "CTRL", "," });
        if (!guiSettingsChord.SequenceEqual(axSettingsChord))
        {
            throw new InvalidDataException(
                "AX and GUI chord forms resolved differently.");
        }
        if (!WindowsKeyMapping.ResolveChord(
                new[] { "CTRL", "SHIFT", "PERIOD" })
            .SequenceEqual(new ushort[] { 0x11, 0x10, 0xBE }))
        {
            throw new InvalidDataException(
                "Named OEM punctuation did not resolve.");
        }
        try
        {
            _ = WindowsKeyMapping.ResolveChord(new[] { "CTRL+NOT_A_REAL_KEY" });
            throw new InvalidDataException("Unsupported key was accepted.");
        }
        catch (InvalidOperationException error)
            when (error.Message == "unsupported_key:NOT_A_REAL_KEY")
        {
        }

        var currentWindowExpectation = JsonNode.Parse(
            """
            {
              "windowHandle":"w2:fixture",
              "providerBusName":"win32:pid:42",
              "providerObjectPath":"w2:fixture",
              "processGeneration":"123",
              "role":"window",
              "name":"Old title"
            }
            """) as JsonObject ??
            throw new InvalidDataException("window expectation must be an object");
        WindowsWindowExpectation.ValidateCurrent(
            currentWindowExpectation,
            "w2:fixture",
            "w2:fixture",
            "win32:pid:42",
            "w2:fixture",
            "123",
            "window");
        try
        {
            WindowsWindowExpectation.ValidateCurrent(
                currentWindowExpectation,
                "w2:fixture",
                "w2:fixture",
                "win32:pid:42",
                "w2:fixture",
                "replacement-generation",
                "window");
            throw new InvalidDataException(
                "Stale expected window identity was accepted.");
        }
        catch (ProviderFailureException error)
            when (error.Code == "stale_window_ref" &&
                  error.Phase == "precondition" &&
                  !error.InputEventsEmitted &&
                  !error.MutationUnknown)
        {
        }
    }
}
