using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class WindowsWindowExpectation
{
    internal static void ValidateCurrent(
        JsonObject? expected,
        string requestWindowHandle,
        string currentWindowHandle,
        string providerBusName,
        string providerObjectPath,
        string processGeneration,
        string role)
    {
        if (expected is null)
        {
            return;
        }

        var matches =
            Matches(expected, "windowHandle", requestWindowHandle) &&
            string.Equals(
                requestWindowHandle,
                currentWindowHandle,
                StringComparison.Ordinal) &&
            Matches(expected, "providerBusName", providerBusName) &&
            Matches(
                expected,
                "processGeneration",
                processGeneration) &&
            Matches(expected, "role", role);
        var expectedObjectPath =
            expected["providerObjectPath"]?.GetValue<string>() ?? string.Empty;
        if (!string.IsNullOrEmpty(expectedObjectPath))
        {
            matches = matches &&
                string.Equals(
                    expectedObjectPath,
                    providerObjectPath,
                    StringComparison.Ordinal);
        }

        if (!matches)
        {
            throw new ProviderFailureException(
                "stale_window_ref",
                "precondition",
                "The bound window identity changed; no input was emitted.");
        }
    }

    private static bool Matches(
        JsonObject expected,
        string field,
        string actual)
    {
        return string.Equals(
            expected[field]?.GetValue<string>(),
            actual,
            StringComparison.Ordinal);
    }
}
