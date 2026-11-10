using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class CanonicalResolutionRecord
{
    internal static bool NameMatches(
        JsonObject candidate,
        JsonObject? target,
        JsonObject? observed)
    {
        var requestedName = ExpectedProviderName(
            target,
            observed,
            out var hasName);
        return !hasName || string.Equals(
            OptionalString(candidate, "name", string.Empty),
            requestedName,
            StringComparison.Ordinal);
    }

    internal static string ExpectedProviderName(
        JsonObject? target,
        JsonObject? observed,
        out bool hasName)
    {
        if (target?["hasProviderObservedName"]?.GetValue<bool>() == true)
        {
            hasName = true;
            return OptionalString(
                target,
                "providerObservedName",
                string.Empty);
        }
        var requestedName = OptionalString(
            target,
            "name",
            OptionalString(observed, "name", string.Empty));
        hasName = target?["hasName"]?.GetValue<bool>() ??
            !string.IsNullOrEmpty(requestedName);
        return requestedName;
    }

    internal static bool RoleMatches(
        JsonObject candidate,
        JsonObject? target,
        JsonObject? observed)
    {
        var actual = OptionalString(candidate, "controlType", string.Empty);
        if (target?["providerRoleAliases"] is JsonArray aliases &&
            aliases.Count > 0)
        {
            return aliases
                .OfType<JsonValue>()
                .Any(value =>
                    value.TryGetValue<string>(out var alias) &&
                    string.Equals(
                        actual,
                        alias,
                        StringComparison.OrdinalIgnoreCase));
        }
        var providerRole = OptionalString(
            observed,
            "controlType",
            string.Empty);
        if (!string.IsNullOrEmpty(providerRole))
        {
            return string.Equals(
                actual,
                providerRole,
                StringComparison.OrdinalIgnoreCase);
        }
        var modelRole = OptionalString(target, "role", string.Empty);
        return string.IsNullOrEmpty(modelRole) ||
            modelRole == "generic" ||
            string.Equals(
                actual,
                modelRole,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool PredicatesMatch(
        JsonObject candidate,
        JsonObject? target,
        JsonObject? observed)
    {
        var predicates = target?["predicates"] as JsonArray ??
            observed?["predicates"] as JsonArray;
        if (predicates is null)
        {
            return true;
        }
        foreach (var node in predicates.OfType<JsonObject>())
        {
            var kind = OptionalString(node, "kind", string.Empty);
            var value = OptionalString(node, "value", string.Empty);
            switch (kind)
            {
                case "state":
                    if (!StateMatches(candidate, value))
                    {
                        return false;
                    }
                    break;
                case "desc":
                    if (!string.Equals(
                            OptionalString(
                                candidate,
                                "description",
                                string.Empty),
                            value,
                            StringComparison.Ordinal))
                    {
                        return false;
                    }
                    break;
                case "placeholder":
                    return false;
                case "action":
                    if (!ActionMatches(candidate, value))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    internal static bool TryTreePath(
        JsonObject candidate,
        out int[] path)
    {
        if (candidate["runtimeId"] is not JsonArray source ||
            source.Count == 0)
        {
            path = Array.Empty<int>();
            return false;
        }
        path = new int[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            if (source[index] is not JsonValue value ||
                !value.TryGetValue<int>(out var childIndex) ||
                childIndex < 0)
            {
                path = Array.Empty<int>();
                return false;
            }
            path[index] = childIndex;
        }
        // The caller bounds the root index against the current logical roots.
        return true;
    }

    internal static bool ProviderIdentityMatches(
        JsonObject candidate,
        string liveProviderObjectPath)
    {
        var observedProviderObjectPath = OptionalString(
            candidate,
            "providerObjectPath",
            string.Empty);
        if (string.IsNullOrEmpty(observedProviderObjectPath))
        {
            return OptionalString(candidate, "providerIdentityScope", string.Empty) ==
                "snapshot_only" && string.IsNullOrEmpty(liveProviderObjectPath);
        }
        return observedProviderObjectPath.StartsWith(
                "runtime:",
                StringComparison.Ordinal) &&
            string.Equals(
                observedProviderObjectPath,
                liveProviderObjectPath,
                StringComparison.Ordinal);
    }

    private static bool StateMatches(JsonObject candidate, string value)
    {
        var normalized = Normalize(value);
        if (normalized == "hasclickaction")
        {
            return ActionMatches(candidate, "invoke") ||
                ActionMatches(candidate, "select") ||
                ActionMatches(candidate, "toggle");
        }
        var states = OptionalString(candidate, "states", string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return states.Any(state =>
            string.Equals(
                Normalize(state),
                normalized,
                StringComparison.Ordinal));
    }

    private static bool ActionMatches(JsonObject candidate, string value)
    {
        var expected = Normalize(value) switch
        {
            "invoke" => "invoke",
            "toggle" => "toggle",
            "select" => "select",
            "expand" or "collapse" => "expand",
            "scrollintoview" => "scrollintoview",
            "scroll" => "scroll",
            _ => string.Empty,
        };
        if (string.IsNullOrEmpty(expected) ||
            candidate["actions"] is not JsonArray actions)
        {
            return false;
        }
        return actions
            .OfType<JsonValue>()
            .Any(value =>
                value.TryGetValue<string>(out var action) &&
                string.Equals(
                    Normalize(action),
                    expected,
                    StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        return value
            .Trim()
            .ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string OptionalString(
        JsonObject? source,
        string name,
        string fallback)
    {
        if (source?[name] is JsonValue value &&
            value.TryGetValue<string>(out var result))
        {
            return result;
        }
        return fallback;
    }
}
