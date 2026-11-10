namespace ComputerUse.Windows.Runtime;

internal static class UiaSelectionPolicy
{
    internal static string RequireUniqueText(string content, string requested)
    {
        if (requested.Length == 0)
        {
            if (content.Length == 0)
            {
                throw new ProviderFailureException("invalid_request", "precondition",
                    "The target has no text to select; no input was emitted.");
            }
            return content;
        }
        var first = content.IndexOf(requested, StringComparison.Ordinal);
        if (first < 0)
        {
            throw new ProviderFailureException("invalid_request", "precondition",
                "The requested text is absent from the target; no input was emitted.");
        }
        if (content.IndexOf(requested, first + 1, StringComparison.Ordinal) >= 0)
        {
            throw new ProviderFailureException("invalid_request", "precondition",
                "The requested text occurs more than once; no input was emitted.");
        }
        return requested;
    }
}
