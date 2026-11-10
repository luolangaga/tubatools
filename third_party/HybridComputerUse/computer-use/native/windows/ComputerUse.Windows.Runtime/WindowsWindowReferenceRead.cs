using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class WindowsWindowReferenceRead
{
    internal static TProjection Project<TBinding, TProjection>(
        string opaque,
        IReadOnlyDictionary<string, TBinding> bindings,
        string staleFailureCode,
        Func<TProjection> readProjection,
        out TBinding? binding) where TBinding : class
    {
        binding = null;
        // Tokens belong to one worker. Native reads cannot recover an unknown
        // token after that worker has exited.
        if (!string.IsNullOrEmpty(opaque) && !bindings.TryGetValue(opaque, out binding))
        {
            const string message = "The exact window reference is stale; no input was emitted.";
            throw staleFailureCode == "stale_window_ref"
                ? WindowsProviderFailures.StaleWindow(message, factsComplete: false)
                : new ProviderFailureException(staleFailureCode, "precondition", message);
        }
        return readProjection();
    }

    internal static JsonObject Recover(bool bindingKnown, Func<JsonObject> readKnownWindow)
    {
        if (bindingKnown) { return readKnownWindow(); }
        return new JsonObject
        {
            ["sourceAppStatus"] = "alive",
            ["sourceWindowStatus"] = "unknown",
            ["inventoryComplete"] = false,
        };
    }
}
