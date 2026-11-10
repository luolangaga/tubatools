using System.Text.Json.Nodes;
using ComputerUse.Windows.Runtime;

internal static class WindowReferenceReadTests
{
    internal static void Run()
    {
        var existing = new object();
        var bindings = new Dictionary<string, object> { ["current-worker-token"] = existing };
        var projectionCalls = 0;
        int ReadProjection() { projectionCalls++; return 42; }

        foreach (var code in new[] { "stale_window_ref", "window_target_stale" })
        {
            try
            {
                WindowsWindowReferenceRead.Project(
                    "previous-worker-token", bindings, code, ReadProjection, out _);
                throw new InvalidOperationException("An unknown token was accepted.");
            }
            catch (ProviderFailureException failure)
            {
                Check(failure.Code == code && failure.Phase == "precondition",
                    "Unknown tokens preserve the caller's stale error code.");
                Check(!failure.InputEventsEmitted && !failure.MutationUnknown,
                    "Unknown tokens fail before input.");
                Check(code != "stale_window_ref" || failure.FactsComplete == false,
                    "Unknown tokens do not prove window retirement.");
            }
        }
        Check(projectionCalls == 0 && bindings.Count == 1,
            "An unknown token never samples or binds a current native window.");

        var projected = WindowsWindowReferenceRead.Project(
            "current-worker-token", bindings, "stale_window_ref", ReadProjection, out var binding);
        Check(projected == 42 && projectionCalls == 1 && ReferenceEquals(binding, existing),
            "Known tokens retain their exact binding and native projection.");
        projected = WindowsWindowReferenceRead.Project(
            "", bindings, "stale_window_ref", ReadProjection, out binding);
        Check(projected == 42 && projectionCalls == 2 && binding is null,
            "Unbound app discovery still reads current windows.");

        var nativeFailure = new InvalidOperationException("native read failed");
        try
        {
            WindowsWindowReferenceRead.Project<object, int>(
                "current-worker-token", bindings, "stale_window_ref", () => throw nativeFailure, out _);
            throw new InvalidOperationException("Native failure was suppressed.");
        }
        catch (InvalidOperationException failure)
        {
            Check(ReferenceEquals(failure, nativeFailure), "Known-window native failures propagate unchanged.");
        }

        var recoveryCalls = 0;
        var knownRecovery = new JsonObject { ["sourceWindowStatus"] = "gone", ["inventoryComplete"] = true };
        JsonObject ReadRecovery() { recoveryCalls++; return knownRecovery; }
        var unknown = WindowsWindowReferenceRead.Recover(false, ReadRecovery);
        Check(recoveryCalls == 0 && unknown["sourceAppStatus"]?.GetValue<string>() == "alive" &&
              unknown["sourceWindowStatus"]?.GetValue<string>() == "unknown" &&
              unknown["inventoryComplete"]?.GetValue<bool>() == false && !unknown.ContainsKey("currentWindow"),
            "Unknown recovery neither reads inventory nor offers a replacement window.");
        Check(ReferenceEquals(WindowsWindowReferenceRead.Recover(true, ReadRecovery), knownRecovery) && recoveryCalls == 1,
            "Known recovery retains its lifecycle and inventory evidence.");
        Console.WriteLine("Window reference read tests passed (12 checks).");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
