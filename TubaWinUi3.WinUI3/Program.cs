using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Settings;

namespace TubaWinUi3;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // The Windows computer-use provider runs as a hidden mode of this executable so it
        // shares the app runtime. It must not initialize XAML or any UI service.
        if (Services.AiAgent.ComputerUseProviderHost.IsInvocation())
        {
            Environment.ExitCode = Services.AiAgent.ComputerUseProviderHost.Run();
            return;
        }

        // Browser automation MCP is also a hidden mode: it must not initialize XAML or any UI
        // service, because the AI component starts it as a console stdio server.
        if (Services.AiAgent.BrowserUseMcpProxy.IsInvocation(args))
        {
            Environment.ExitCode = Services.AiAgent.BrowserUseMcpProxy.Run();
            return;
        }

        StartXaml();
    }

    // Kept out of line so the provider-only path does not touch WinUI types.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartXaml()
    {
        // These process-wide switches lock at Application.Start; App() is already too late.
        // Keep an explicit list so SDK updates do not silently opt into unrelated changes.
        XamlOptionalChanges.EnableChange(XamlChangeId.IconNoGridOptimization);
        XamlOptionalChanges.EnableChange(XamlChangeId.OptimizeApplyStyles);
        XamlOptionalChanges.EnableChange(XamlChangeId.DefaultStyleOptimizations);

        // Reuse WinUI's generated COM, dispatcher and App initialization for every launch mode.
        XamlGeneratedProgram.XamlGeneratedMain();
    }
}
