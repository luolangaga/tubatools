namespace TubaWinUi3.Services.AiAgent;

internal static class AiBrowserPaths
{
    internal const string RootEnvironment = "TUBA_AI_BROWSER_ROOT";

    internal static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(RootEnvironment);
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            return Path.Combine(ConfigManager.GetDataDir(), "AiBrowser");
        }
    }

    internal static string StopFlag => Path.Combine(Root, "stop.flag");
    internal static string HostDescriptor => Path.Combine(Root, "host.json");
    internal static string WebView2UserData => Path.Combine(Root, "WebView2");
}
