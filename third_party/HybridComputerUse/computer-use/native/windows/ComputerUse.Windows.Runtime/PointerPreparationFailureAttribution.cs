using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class PointerPreparationFailureAttribution
{
    internal static bool HasKnownZeroRequestedInput(
        Exception error, bool dispatchAttempted, bool inputEmitted)
    {
        if (UiaMutationFailureAttribution.HasEmittedInput(
                error as ProviderFailureException, inputEmitted)) { return false; }
        if (error is ProviderFailureException failure)
        {
            if (failure.MutationUnknown)
            {
                return false;
            }
            // Native SendInput may prove zero delivery after activation.
            return true;
        }
        return !dispatchAttempted;
    }

    internal static ProviderFailureException FromZeroInputError(
        Exception error, JsonObject actionEvidence)
    {
        Console.Error.WriteLine(
            "[provider] Pointer preparation failed after activation attempt: " +
            $"{error.GetType().Name} HRESULT=0x{error.HResult:X8}");
        return new ProviderFailureException(
            "pointer_preparation_failed",
            "emission",
            "Window activation was attempted, but the requested pointer input " +
            "was not emitted. Focus or window state may have changed. " +
            "Stop; do not replay or observe.",
            inputEventsEmitted: false,
            mutationUnknown: true,
            actionEvidence: actionEvidence,
            factsComplete: false,
            pointerPreparation:
                PointerPreparationState.ActivationAttemptedOutcomeUnknown);
    }
}
