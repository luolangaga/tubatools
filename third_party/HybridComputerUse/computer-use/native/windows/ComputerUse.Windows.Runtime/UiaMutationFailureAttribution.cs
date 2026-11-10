using System.Text.Json.Nodes;
using FlaUI.Core.Exceptions;

namespace ComputerUse.Windows.Runtime;

internal static class UiaMutationFailureAttribution
{
    internal static bool HasEmittedInput(
        ProviderFailureException? error, bool inputEmitted)
    {
        return inputEmitted || error?.InputEventsEmitted == true ||
            (error?.ActionEvidence?["input_emitted"] is JsonValue value &&
                value.TryGetValue<bool>(out var emitted) && emitted);
    }

    internal static ProviderFailureException Preserve(
        ProviderFailureException error,
        bool inputEmitted,
        JsonObject actionEvidence)
    {
        var emitted = HasEmittedInput(error, inputEmitted);
        // Native emission evidence describes the failing message and its
        // uncertain suffix. Keep it when adding AX preparation/readback facts.
        if (error.ActionEvidence is { } nativeEvidence)
        {
            foreach (var field in nativeEvidence)
            {
                if (field.Key == "route" &&
                    actionEvidence["route"]?.GetValue<string>() is { Length: > 0 } actionRoute &&
                    actionRoute != "unknown" && actionRoute != field.Value?.GetValue<string>())
                {
                    actionEvidence["emission_route"] = field.Value?.DeepClone();
                    continue;
                }
                actionEvidence[field.Key] = field.Value?.DeepClone();
            }
        }
        actionEvidence["input_emitted"] = emitted;
        return new ProviderFailureException(
            error.Code, error.Phase, error.Message,
            emitted, emitted || error.MutationUnknown,
            actionEvidence: actionEvidence,
            factsComplete: error.FactsComplete,
            subject: error.Subject,
            recoveryContext: error.RecoveryContext,
            focusPreparation: error.FocusPreparation,
            pointerPreparation: error.PointerPreparation,
            candidates: error.Candidates);
    }

    internal static ProviderFailureException FromUnexpected(
        Exception error,
        bool inputEmitted,
        JsonObject actionEvidence)
    {
        if (inputEmitted)
        {
            return new ProviderFailureException(
                "post_action_observation_failed",
                "post_action_observation",
                error.Message,
                inputEventsEmitted: true,
                mutationUnknown: true,
                actionEvidence: actionEvidence);
        }

        if (error is ElementNotEnabledException)
        {
            return new ProviderFailureException(
                "target_not_enabled",
                "precondition",
                "The target is not enabled for this action.",
                inputEventsEmitted: false,
                mutationUnknown: false,
                actionEvidence: actionEvidence,
                factsComplete: true);
        }

        return new ProviderFailureException(
            "action_dispatch_outcome_unknown",
            "emission",
            error.Message,
            inputEventsEmitted: false,
            mutationUnknown: true,
            actionEvidence: actionEvidence);
    }
}
