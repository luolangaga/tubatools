namespace TubaWinUi3.Services.Plugins;

/// <summary>
/// 插件安全模式：一个进程级总闸门，开启后已安装插件一律不加载、不执行启动钩子。
///
/// 为什么需要它：插件与宿主同进程、同权限运行，坏插件能把整个工具箱拖到打不开
/// （加载期异常、启动钩子死循环、后台线程崩溃）。用户此前只能手动删数据目录才能恢复。
///
/// 进入方式：
/// 1. 命令行 <see cref="CommandLineArg"/>（快捷方式、命令行工具可直接使用）；
/// 2. 插件页手动「以安全模式重启」——写入 <see cref="SettingKey"/> 后重启；
/// 3. 启动时弹窗询问：上次没有正常退出（见 <see cref="PluginSessionMarker"/>）时，
///    在主窗口出来后、加载任何插件之前询问用户是否跳过插件启动。
///    比静默跳过更尊重用户：他可能只是自己用任务管理器结束了进程。
///
/// 安全模式本身是持久开关（<see cref="SettingKey"/>），否则下次重启就失效了。
/// </summary>
public static class PluginSafeMode
{
    public const string CommandLineArg = "--safe-mode";
    public const string SettingKey = "PluginsSafeMode";

    /// <summary>当前进程是否处于安全模式。启动期一次性确定，运行期不再变。</summary>
    public static bool IsActive { get; private set; }

    /// <summary>本次是自动/询问进入而非命令行指定——横幅据此说明原因。</summary>
    public static bool EnteredAutomatically { get; private set; }

    /// <summary>上次是否异常退出（启动早期由 <see cref="BeginSession"/> 记录，供弹窗判断）。</summary>
    public static bool LastExitWasUnclean { get; private set; }

    /// <summary>
    /// 启动早期调用一次（必须在任何插件加载之前）。只做记录与命令行判定，不弹窗：
    /// 此时主窗口还没建起来，没有 XamlRoot 可承载对话框。
    /// 返回命令行/上次选择是否已经要求安全模式。
    /// </summary>
    internal static bool Initialize(IEnumerable<string> commandLine)
    {
        LastExitWasUnclean = PluginSessionMarker.BeginSession();
        var fromCommandLine = LaunchIntent.HasSwitch(commandLine, CommandLineArg);
        IsActive = fromCommandLine || AppSettings.GetBool(SettingKey);
        if (!IsActive) return false;

        EnteredAutomatically = !fromCommandLine;
        Log(fromCommandLine ? "按命令行开关进入安全模式" : "按上次的选择继续使用安全模式");
        return true;
    }

    /// <summary>
    /// 上次异常退出时询问用户是否以安全模式启动。必须在主窗口创建后、插件加载前调用。
    /// 用户选择安全模式时写入持久开关并立即生效，返回 true。
    /// </summary>
    internal static async Task<bool> AskOnUncleanExitAsync(Microsoft.UI.Xaml.XamlRoot? root)
    {
        if (IsActive || !LastExitWasUnclean || root is null) return IsActive;

        var accepted = false;
        try
        {
            var dialog = new Controls.FluentContentDialog
            {
                XamlRoot = root,
                RequestedTheme = ThemeService.CurrentElementTheme,
                Title = LocalizationService.L("SafeMode_PromptTitle", "上次没有正常退出"),
                Content = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    Text = LocalizationService.L("SafeMode_PromptBody",
                        "工具箱上次可能是在异常状态下结束的（崩溃、被强制结束，或某个插件把界面卡住了）。\n\n" +
                        "要以安全模式启动吗？安全模式会跳过全部已安装插件，便于确认问题是否来自插件。你的插件不会被卸载，之后随时可以恢复。")
                },
                PrimaryButtonText = LocalizationService.L("SafeMode_PromptYes", "以安全模式启动"),
                CloseButtonText = LocalizationService.L("SafeMode_PromptNo", "正常启动"),
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary
            };
            dialog.PrimaryButtonClick += (_, _) => accepted = true;
            accepted = await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(30)) && accepted;
        }
        catch (Exception ex)
        {
            Log("恢复提示弹窗失败：" + ex.Message);
            return false;
        }

        if (!accepted) return false;
        IsActive = true;
        EnteredAutomatically = true;
        SetForNextLaunch(true);
        Log("用户选择以安全模式启动");
        return true;
    }

    /// <summary>本次启动已走到正常退出点：记下「干净退出」，下次不再弹恢复提示。</summary>
    internal static void MarkCleanExit()
    {
        PluginSessionMarker.MarkClean();
        // 安全模式是持久开关，不因干净退出自动关闭——用户在插件页或横幅上显式关闭。
        Log("干净退出");
    }

    /// <summary>设置「下次启动」是否使用安全模式（当前进程的 <see cref="IsActive"/> 不变）。</summary>
    public static void SetForNextLaunch(bool enabled) => AppSettings.Set(SettingKey, enabled);

    /// <summary>关闭安全模式（下次启动恢复正常加载）。</summary>
    public static void DisableForNextLaunch() => AppSettings.Set(SettingKey, false);

    private static void Log(string message)
    {
        try { LogService.Info(LogService.Plugin, "[安全模式] " + message); } catch { }
    }
}
