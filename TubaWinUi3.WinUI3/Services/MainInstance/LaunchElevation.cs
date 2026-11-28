using System.Diagnostics;
using System.Security.Principal;

namespace TubaWinUi3.Services;

/// <summary>
/// 非打包版自动提权（XAML 前置闸门）。
///
/// 旧实现把判定放在 App.OnLaunched：首个普通权限进程已经付完 WinUI/XAML 初始化 + App 构造
/// （设置加载/遥测/本地化/通知注册/内置工具注册）的全部启动成本，runas 重启后第二个进程
/// 再付一遍——冷启动时间近乎翻倍。现在前移到 Program.Main（Application.Start 之前）：
/// 命中「要开主窗口的普通启动」就立即 runas 重启并退出，首个进程只付 .NET 运行时的毫秒级成本。
///
/// 约束：
/// - 本类不得引用 App 或任何 Microsoft.UI.Xaml 类型（引用即触发 WinUI 程序集加载，前置就白做了）；
/// - 允许清单判定：只有命令行全部由已知「要开主窗口」的开关组成才前置提权；
///   无窗口辅助模式（--copy-path / MCP / CLI / 探测子进程等）与未知参数一律放行，
///   交给 App.OnLaunchedCore 的原有兜底判定，绝不能误弹 UAC；
/// - MSIX 打包上下文保持普通权限启动（需要管理员时由用户在工具页面主动重启提权）；
/// - TUBA_SCREENSHOT_NOELEVATE=1 的截图/测试旁路保持不变；
/// - 用户取消 UAC 时 ShellExecute 同步抛异常，按旧行为吞掉（本次启动不开窗口）。
/// </summary>
internal static class LaunchElevation
{
    internal static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Program.Main 在 XAML 初始化前调用。返回 true = 本进程应立即退出
    /// （已 runas 重启提权子进程；重启失败/用户取消 UAC 也一样退出，与旧行为一致）。
    /// </summary>
    internal static bool TryElevateBeforeXaml(string[] args)
    {
        if (!ShouldElevateBeforeXaml(RuntimeHelper.IsPackagedContext, IsRunningAsAdmin(),
                Environment.GetEnvironmentVariable("TUBA_SCREENSHOT_NOELEVATE"), args))
            return false;

        RelaunchElevated();
        return true;
    }

    /// <summary>纯判定（可单测）：是否需要 XAML 前置自动提权。args 不含 argv[0]。</summary>
    internal static bool ShouldElevateBeforeXaml(bool isPackagedContext, bool isAdmin, string? noElevateEnv, IReadOnlyList<string> args)
        => !isPackagedContext && !isAdmin && noElevateEnv != "1" && IsPlainWindowLaunch(args);

    /// <summary>
    /// 允许清单：args（不含 argv[0]）是否全部由「要开主窗口」的启动开关组成。
    /// 导航开关与 <see cref="LaunchIntent"/> 同源；--safe-mode 是插件安全模式的快捷方式入口，同样开主窗口。
    /// 缺值的 --open-builtin / --file-lock 按 LaunchIntent.Parse 退化为普通启动，仍开窗口，因此放行。
    /// 其余任何 token（无窗口模式、Toast 激活标记、未知参数）都返回 false，走原有启动路径。
    /// </summary>
    internal static bool IsPlainWindowLaunch(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var token = Normalize(args[i]);
            if (Eq(token, LaunchIntent.OpenBuiltinArg) || Eq(token, LaunchIntent.FileLockArg))
            {
                i++; // 消费紧随的值 token（缺值时循环自然结束）
                continue;
            }

            if (Eq(token, LaunchIntent.ActiveInterceptArg)
                || Eq(token, LaunchIntent.AutoStartArg)
                || Eq(token, LaunchIntent.HideUiArg)
                || Eq(token, Plugins.PluginSafeMode.CommandLineArg))
                continue;

            return false;
        }

        return true;
    }

    /// <summary>
    /// runas 重启自身并保留原始命令行参数（--open-builtin / --file-lock 等提权后继续执行）。
    /// 不追加 --takeover：调用方进程尚未创建主窗口、也未持有单实例互斥体，随即退出，无接管问题。
    /// </summary>
    internal static void RelaunchElevated()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            var originalArgs = string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            Process.Start(new ProcessStartInfo(exePath)
            {
                Arguments = originalArgs,
                Verb = "runas",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static string Normalize(string token) => token.Trim().Trim('"');

    private static bool Eq(string token, string flag) => string.Equals(token, flag, StringComparison.OrdinalIgnoreCase);
}