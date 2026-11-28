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

        // 非打包版自动提权必须早于 XAML/WinUI 初始化：放在 App.OnLaunched 时，首个普通权限进程
        // 已付完整个 WinUI + App 构造的启动成本，提权后的第二个进程再付一遍（冷启动近乎双倍）。
        // 这里只做轻量判定，命中「要开主窗口的普通启动」立即 runas 重启并退出；无窗口辅助模式
        // 与未知参数由允许清单放行，仍走 App.OnLaunchedCore 的兜底判定。不得在此引用 App/WinUI 类型。
        if (Services.LaunchElevation.TryElevateBeforeXaml(args))
            return;

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
