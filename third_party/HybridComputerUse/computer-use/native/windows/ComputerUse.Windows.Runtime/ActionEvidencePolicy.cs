using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

// Evidence flags state what the provider checked after the action, never
// what it assumed before it.
internal static class ActionEvidencePolicy
{
    // desktop_state_mutated is true only after a readback showed a changed
    // state and false only when no input was emitted. Emitted input without
    // such a check leaves the field out: the effect is unverified, and the
    // emitted-input fact already invalidates earlier observations.
    internal static JsonObject MutationFacts(
        bool inputEventsEmitted,
        bool? desktopStateMutated)
    {
        var facts = new JsonObject
        {
            ["input_events_emitted"] = inputEventsEmitted,
            ["partial_input_possible"] = false,
            ["mutation_unknown"] = false,
        };
        var mutated = inputEventsEmitted ? desktopStateMutated : false;
        if (mutated.HasValue)
        {
            facts["desktop_state_mutated"] = mutated.Value;
        }
        return facts;
    }

    // Keyboard input went to the target only if the target's process still
    // owns the foreground window when it is read after the input. The Active
    // flag sampled while the window was resolved says nothing about that.
    internal static bool KeyboardFocusVerified(
        int targetProcessId,
        int? foregroundProcessIdAfterInput) =>
        foregroundProcessIdAfterInput == targetProcessId;

    // A matching readback proves a change only when the value read before
    // the write differed; an unchanged or unreadable prior value leaves the
    // change unverified.
    internal static bool? VerifiedChange(bool valueDiffered) =>
        valueDiffered ? true : null;
}
