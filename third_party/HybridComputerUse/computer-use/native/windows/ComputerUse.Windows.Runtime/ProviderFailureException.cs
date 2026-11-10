using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed class ProviderFailureException : Exception
{
    internal ProviderFailureException(
        string code,
        string phase,
        string message,
        bool inputEventsEmitted = false,
        bool mutationUnknown = false,
        int dispatchedActions = 0,
        int dispatchedInputActions = 0,
        JsonObject? actionEvidence = null,
        bool? factsComplete = null,
        string subject = "",
        JsonObject? recoveryContext = null,
        FocusPreparationState? focusPreparation = null,
        PointerPreparationState? pointerPreparation = null,
        JsonArray? candidates = null)
        : base(message)
    {
        Code = code;
        Phase = phase;
        InputEventsEmitted = inputEventsEmitted;
        MutationUnknown = mutationUnknown;
        DispatchedActions = dispatchedActions;
        DispatchedInputActions = dispatchedInputActions;
        ActionEvidence = actionEvidence;
        FactsComplete = factsComplete;
        Subject = subject;
        RecoveryContext = recoveryContext;
        FocusPreparation = focusPreparation;
        PointerPreparation = pointerPreparation;
        Candidates = candidates;
    }

    internal string Code { get; }
    internal string Phase { get; }
    internal bool InputEventsEmitted { get; }
    internal bool MutationUnknown { get; }
    internal int DispatchedActions { get; }
    internal int DispatchedInputActions { get; }
    internal JsonObject? ActionEvidence { get; }
    internal bool? FactsComplete { get; }
    internal string Subject { get; }
    internal JsonObject? RecoveryContext { get; }
    internal FocusPreparationState? FocusPreparation { get; }
    internal PointerPreparationState? PointerPreparation { get; }
    // Application processes an ambiguous app query matched.
    internal JsonArray? Candidates { get; }
}
