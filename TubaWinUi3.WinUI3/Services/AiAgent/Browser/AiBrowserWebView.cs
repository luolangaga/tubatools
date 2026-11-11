using Microsoft.Web.WebView2.Core;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// A dedicated WebView2 environment for AI browsing. The profile is intentionally separate
/// from both the user's daily browser and the app's tutorial/report WebView2 data.
/// </summary>
internal static class AiBrowserWebView
{
    private static readonly Lazy<Task<CoreWebView2Environment>> Environment = new(
        () => CoreWebView2Environment.CreateWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: AiBrowserPaths.WebView2UserData,
            options: new CoreWebView2EnvironmentOptions()).AsTask());

    internal static Task<CoreWebView2Environment> GetEnvironmentAsync() => Environment.Value;
}
