using System.Text.Json.Nodes;

namespace TubaWinUi3.Services.AiAgent;

internal sealed record AiBrowserSetupResult(bool Ok, bool Running, string Url, string Title, string Error)
{
    internal string ProfilePath => AiBrowserInfo.ProfilePath;
}

/// <summary>UI-facing status/open/repair operations for the single built-in WebView2 engine.</summary>
internal static class AiBrowserSetupService
{
    internal static AiBrowserSetupResult GetStatus()
    {
        var status = AiBrowserHostService.LocalStatus();
        return FromStatus(status);
    }

    internal static async Task<AiBrowserSetupResult> EnsureAsync(CancellationToken ct)
    {
        await AiBrowserHostService.OpenWindowAsync(ct);
        return GetStatus();
    }

    internal static async Task<AiBrowserSetupResult> RepairAsync(CancellationToken ct)
    {
        await AiBrowserHostService.RecyclePageAsync(ct);
        return GetStatus();
    }

    internal static Task CloseAsync(CancellationToken ct) =>
        AiBrowserHostService.CloseWindowAsync(stopControl: true, ct);

    private static AiBrowserSetupResult FromStatus(JsonObject status)
    {
        var ready = status["ready"]?.GetValue<bool>() == true;
        var url = status["url"]?.GetValue<string>() ?? "";
        var title = status["title"]?.GetValue<string>() ?? "";
        return new AiBrowserSetupResult(true, ready, url, title, "");
    }
}
