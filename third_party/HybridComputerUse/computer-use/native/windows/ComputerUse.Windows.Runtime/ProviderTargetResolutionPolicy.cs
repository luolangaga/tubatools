using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class ProviderTargetResolutionPolicy
{
    internal static bool AllowsSemanticProviderRebind(JsonObject? target)
    {
        if (target is null ||
            !string.Equals(
                target["locatorKind"]?.GetValue<string>(),
                "semantic",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(
                target["selector"]?.GetValue<string>()))
        {
            return false;
        }
        return target["allowProviderRebind"] is JsonValue value &&
            value.TryGetValue<bool>(out var allowed) &&
            allowed;
    }
}
