using Microsoft.Win32;

namespace Tubatools.Windows11Optimizer;

/// <summary>
/// Declarative catalog of Windows 11 tweaks. Every value has an explicit Windows default in
/// <see cref="RegistryOp.OffValue"/> so the whole page can be reverted without guessing.
/// </summary>
internal static class TweakCatalog
{
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string PolicyExplorer = @"Software\Policies\Microsoft\Windows\Explorer";
    private const string PoliciesWindows = @"SOFTWARE\Policies\Microsoft\Windows";
    private const string UacPolicy = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string Desktop = @"Control Panel\Desktop";
    private const string WindowMetrics = @"Control Panel\Desktop\WindowMetrics";
    private const string ShellIcons = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
    private const string HideDesktopIcons = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    private const string CabinetState = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState";
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string GameConfig = @"System\GameConfigStore";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";
    private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string Notifications = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string PowerSchemes = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
    private const string WindowsUpdateSettings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    private const string WindowsUpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

    private const string BalancedPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string PowerSaverPlan = "a1841308-3541-4fab-bc81-f71556f20b4a";

    public static IReadOnlyList<TweakDefinition> Tweaks { get; } = BuildTweaks();

    public static IReadOnlyList<TweakAction> Actions { get; } =
    [
        new()
        {
            Id = "restart-explorer", Group = "资源管理器维护", Title = "重启资源管理器",
            Description = "立即重启 explorer.exe，让快捷方式箭头、盾牌等外壳图标更改生效。",
            ButtonText = "重启",
            Run = TweakEngine.RestartExplorerAsync
        },
        new()
        {
            Id = "clear-icon-cache", Group = "资源管理器维护", Title = "清理图标与缩略图缓存",
            Description = "停止资源管理器，删除 iconcache/thumbcache 后重新启动。图标显示异常时使用。",
            ButtonText = "清理", Risky = true,
            Run = TweakEngine.ClearIconCacheAsync
        },
        new()
        {
            Id = "reset-folder-views", Group = "资源管理器维护", Title = "重置文件夹视图",
            Description = "删除 Bags/BagMRU 记录，让所有文件夹恢复 Windows 默认的查看方式。",
            ButtonText = "重置", Risky = true,
            Run = TweakEngine.ResetFolderViewsAsync
        },
        new()
        {
            Id = "pause-updates", Group = "Windows 更新维护", Title = "暂停 Windows 更新 30 天",
            Description = "写入系统更新暂停时间；到期后 Windows 会自动恢复检查更新。",
            ButtonText = "暂停 30 天",
            Run = TweakEngine.PauseWindowsUpdateAsync
        },
        new()
        {
            Id = "resume-updates", Group = "Windows 更新维护", Title = "取消更新暂停",
            Description = "清除暂停时间并恢复正常的更新检查。",
            ButtonText = "恢复更新",
            Run = TweakEngine.ResumeWindowsUpdateAsync
        },
        new()
        {
            Id = "open-windows-update", Group = "系统页面", Title = "打开 Windows 更新设置",
            Description = "跳转到系统自带的 Windows 更新页面。",
            ButtonText = "打开",
            Run = _ =>
            {
                ShellRunner.StartDetached("ms-settings:windowsupdate");
                return Task.FromResult("已打开 Windows 更新设置。");
            }
        },
        new()
        {
            Id = "open-uac", Group = "系统页面", Title = "打开用户账户控制设置",
            Description = "打开 UserAccountControlSettings.exe，手动调整 UAC 通知级别。",
            ButtonText = "打开",
            Run = _ =>
            {
                ShellRunner.StartDetached("UserAccountControlSettings.exe");
                return Task.FromResult("已打开用户账户控制设置。");
            }
        },
        new()
        {
            Id = "open-system-restore", Group = "系统页面", Title = "打开系统还原",
            Description = "在批量修改前创建系统还原点，出问题时可以回滚整个系统。",
            ButtonText = "打开",
            Run = _ =>
            {
                ShellRunner.StartDetached("rstrui.exe");
                return Task.FromResult("已打开系统还原。");
            }
        }
    ];

    public static IReadOnlyList<AppxEntry> AppxApps { get; } =
    [
        new("Microsoft.549981C3F5F10", "Cortana", "小娜语音助手（Windows 11 已停用）"),
        new("Microsoft.BingNews", "资讯", "MSN 新闻资讯"),
        new("Microsoft.BingWeather", "天气", "MSN 天气"),
        new("Microsoft.BingSearch", "Bing 搜索", "任务栏搜索的 Bing 组件"),
        new("Microsoft.GetHelp", "获取帮助", "Windows 帮助应用"),
        new("Microsoft.Getstarted", "使用技巧", "Windows 使用技巧提示"),
        new("Microsoft.MicrosoftOfficeHub", "Microsoft 365（Office）", "Office 集线器，不是桌面版 Office"),
        new("Microsoft.MicrosoftSolitaireCollection", "纸牌游戏", "Microsoft Solitaire Collection"),
        new("Microsoft.Microsoft3DViewer", "3D 查看器", "3D 模型查看器"),
        new("Microsoft.Print3D", "打印 3D", "3D 打印应用"),
        new("Microsoft.MixedReality.Portal", "混合现实门户", "Windows Mixed Reality 门户"),
        new("Microsoft.People", "人脉", "联系人应用"),
        new("Microsoft.PowerAutomateDesktop", "Power Automate", "桌面自动化应用"),
        new("Microsoft.Todos", "Microsoft To Do", "待办事项应用"),
        new("Microsoft.WindowsFeedbackHub", "反馈中心", "向 Microsoft 提交反馈"),
        new("Microsoft.WindowsMaps", "地图", "Windows 地图"),
        new("Microsoft.YourPhone", "手机连接", "Phone Link 手机连接"),
        new("Microsoft.ZuneVideo", "电影和电视", "Microsoft 电影和电视播放器"),
        new("Microsoft.ZuneMusic", "媒体播放器", "Windows Media Player 新版"),
        new("Microsoft.GamingApp", "Xbox", "Xbox 应用，使用 Game Pass 时请保留"),
        new("Microsoft.XboxApp", "Xbox 控制台小帮手", "旧版 Xbox 应用"),
        new("Microsoft.XboxGameOverlay", "Xbox 游戏覆盖层", "Xbox 游戏内覆盖层"),
        new("Microsoft.XboxGamingOverlay", "Xbox 游戏栏", "Win+G 游戏栏"),
        new("Microsoft.XboxSpeechToTextOverlay", "Xbox 语音转文本", "游戏语音转文本覆盖层"),
        new("Microsoft.Xbox.TCUI", "Xbox TCUI", "Xbox 通用界面组件"),
        new("Clipchamp.Clipchamp", "Clipchamp", "Microsoft 视频编辑器"),
        new("MicrosoftTeams", "Microsoft Teams（个人版）", "消费者版 Teams"),
        new("LinkedInForWindows", "LinkedIn", "领英桌面应用"),
        new("Microsoft.OutlookForWindows", "新版 Outlook", "Windows 邮件的新版 Outlook"),
        new("Microsoft.WindowsCommunicationsApps", "邮件和日历", "Windows 邮件与日历"),
        new("Microsoft.SkypeApp", "Skype", "Skype 即时通讯"),
        new("Microsoft.MicrosoftStickyNotes", "便笺", "桌面便笺（常用工具，可选）"),
        new("Microsoft.WindowsAlarms", "闹钟和时钟", "闹钟、计时器（常用工具，可选）"),
        new("Microsoft.WindowsSoundRecorder", "录音机", "Windows 录音机（常用工具，可选）"),
        new("Microsoft.WindowsCamera", "相机", "Windows 相机（常用工具，可选）")
    ];

    public static IReadOnlyList<WingetApp> WingetApps { get; } =
    [
        new("Google.Chrome", "Google Chrome", "浏览器"),
        new("Mozilla.Firefox", "Mozilla Firefox", "浏览器"),
        new("Microsoft.PowerToys", "PowerToys", "Windows 效率工具集"),
        new("Microsoft.VisualStudioCode", "Visual Studio Code", "代码编辑器"),
        new("Microsoft.WindowsTerminal", "Windows Terminal", "现代终端"),
        new("7zip.7zip", "7-Zip", "压缩解压"),
        new("RARLab.WinRAR", "WinRAR", "压缩解压"),
        new("Notepad++.Notepad++", "Notepad++", "文本编辑器"),
        new("VideoLAN.VLC", "VLC", "全能播放器"),
        new("Git.Git", "Git", "版本控制"),
        new("Valve.Steam", "Steam", "游戏平台"),
        new("Discord.Discord", "Discord", "语音聊天"),
        new("Tencent.WeChat", "微信", "即时通讯"),
        new("Tencent.QQ.NT", "QQ（NT）", "即时通讯"),
        new("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", "硬盘健康检测"),
        new("CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark", "硬盘测速")
    ];

    public static string Title(OptimizerTab tab) => tab switch
    {
        OptimizerTab.Overview => "概览",
        OptimizerTab.Explorer => "资源管理器",
        OptimizerTab.Privacy => "隐私与安全",
        OptimizerTab.Performance => "性能与游戏",
        OptimizerTab.Update => "Windows 更新",
        OptimizerTab.Power => "电源与声音",
        OptimizerTab.Notifications => "通知与建议",
        OptimizerTab.Apps => "应用管理",
        _ => "高级工具"
    };

    private static List<TweakDefinition> BuildTweaks()
    {
        var list = new List<TweakDefinition>();

        // ── 资源管理器 ────────────────────────────────────────────────────────────
        list.Add(Toggle("shortcut-arrow", OptimizerTab.Explorer, "快捷方式图标", "去除快捷方式箭头",
            "使用空白图标覆盖快捷方式左下角的小箭头。重启资源管理器后生效。",
            S(RegistryHive.LocalMachine, ShellIcons, "29", @"%SystemRoot%\System32\shell32.dll,-50", null, expand: true))
            with { Recommended = true, NeedsExplorerRestart = true });

        list.Add(Toggle("shortcut-shield", OptimizerTab.Explorer, "快捷方式图标", "去除快捷方式盾牌",
            "隐藏需要管理员权限的快捷方式右下角 UAC 盾牌。重启资源管理器后生效。",
            S(RegistryHive.LocalMachine, ShellIcons, "77", @"%SystemRoot%\System32\shell32.dll,-50", null, expand: true))
            with { Recommended = true, NeedsExplorerRestart = true });

        list.Add(Toggle("shortcut-arrow-hard", OptimizerTab.Explorer, "快捷方式图标", "彻底移除快捷方式箭头（进阶）",
            "删除 lnkfile\\IsShortcut，兼容所有外壳版本，但可能影响固定到任务栏/开始菜单。优先尝试上面的空图标方式。",
            S(RegistryHive.LocalMachine, @"SOFTWARE\Classes\lnkfile", "IsShortcut", null, "", expand: false))
            with { Risky = true, NeedsExplorerRestart = true });

        list.Add(Toggle("show-extensions", OptimizerTab.Explorer, "文件资源管理器", "显示文件扩展名",
            "在文件名后显示 .txt、.exe 等扩展名，便于识别文件类型。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "HideFileExt", 0, 1)) with { Recommended = true });

        list.Add(Toggle("show-hidden-files", OptimizerTab.Explorer, "文件资源管理器", "显示隐藏文件",
            "在资源管理器中显示具有隐藏属性的文件和文件夹。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "Hidden", 1, 2)));

        list.Add(Toggle("show-super-hidden", OptimizerTab.Explorer, "文件资源管理器", "显示受保护的操作系统文件",
            "显示系统文件。普通使用不建议开启，删除系统文件可能导致系统无法启动。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "ShowSuperHidden", 1, 0)) with { Risky = true });

        list.Add(Toggle("compact-mode", OptimizerTab.Explorer, "文件资源管理器", "使用紧凑视图",
            "减小文件列表的行距，在同一屏幕内显示更多项目。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "UseCompactMode", 1, 0)));

        list.Add(Toggle("status-bar", OptimizerTab.Explorer, "文件资源管理器", "显示状态栏",
            "在资源管理器底部显示项目数量和选中项信息。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "ShowStatusBar", 1, 0)) with { DefaultOn = true });

        list.Add(Toggle("full-path-title", OptimizerTab.Explorer, "文件资源管理器", "标题栏显示完整路径",
            "在文件夹窗口标题栏显示完整路径，而不是只显示文件夹名。",
            D(RegistryHive.CurrentUser, CabinetState, "FullPath", 1, 0)));

        list.Add(Toggle("launch-to-this-pc", OptimizerTab.Explorer, "文件资源管理器", "打开时显示“此电脑”",
            "资源管理器启动后直接进入“此电脑”，而不是主页。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "LaunchTo", 1, null)));

        list.Add(Toggle("separate-process", OptimizerTab.Explorer, "文件资源管理器", "在单独进程中打开文件夹",
            "每个文件夹窗口使用独立进程，某个窗口崩溃时不影响其他窗口。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "SeparateProcess", 1, 0)));

        list.Add(Toggle("persist-browsers", OptimizerTab.Explorer, "文件资源管理器", "登录后恢复上次的文件夹",
            "重新登录 Windows 时恢复上次打开的资源管理器窗口。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "PersistBrowsers", 1, 0)));

        list.Add(Toggle("thumbnails", OptimizerTab.Explorer, "文件资源管理器", "显示缩略图而非图标",
            "图片、视频和文档显示内容缩略图。关闭后只显示文件类型图标，速度更快。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "IconsOnly", 0, 1)) with { DefaultOn = true });

        list.Add(Toggle("nav-expand", OptimizerTab.Explorer, "文件资源管理器", "导航窗格展开到当前文件夹",
            "打开文件夹时，导航窗格自动展开并选中当前路径。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "NavPaneExpandToCurrentFolder", 1, 0)) with { DefaultOn = true });

        list.Add(Toggle("nav-all-folders", OptimizerTab.Explorer, "文件资源管理器", "导航窗格显示所有文件夹",
            "在导航窗格中显示控制面板、回收站等所有 shell 文件夹。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "NavPaneShowAllFolders", 1, 0)));

        list.Add(Toggle("info-tips", OptimizerTab.Explorer, "文件资源管理器", "显示文件夹提示信息",
            "鼠标悬停时显示文件大小、修改日期等信息。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "ShowInfoTip", 1, 0)) with { DefaultOn = true });

        list.Add(Toggle("hide-empty-drives", OptimizerTab.Explorer, "文件资源管理器", "隐藏没有媒体的驱动器",
            "隐藏读卡器等当前没有插入介质的驱动器盘符。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "HideDrivesWithNoMedia", 1, 0)));

        list.Add(Toggle("sync-provider-notifications", OptimizerTab.Explorer, "文件资源管理器", "关闭同步提供程序通知",
            "阻止 OneDrive 等同步程序在资源管理器中弹出推广通知。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "ShowSyncProviderNotifications", 0, 1)));

        list.Add(Toggle("recent-files", OptimizerTab.Explorer, "文件资源管理器", "快速访问显示最近文件",
            "在资源管理器主页显示最近使用的文件列表。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "Start_TrackDocs", 1, 0)) with { DefaultOn = true });

        list.Add(Toggle("desktop-this-pc", OptimizerTab.Explorer, "桌面图标", "桌面显示“此电脑”",
            "在桌面显示此电脑图标。", D(RegistryHive.CurrentUser, HideDesktopIcons, "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0, 1)));
        list.Add(Toggle("desktop-user", OptimizerTab.Explorer, "桌面图标", "桌面显示用户文件夹",
            "在桌面显示当前用户的个人文件夹图标。", D(RegistryHive.CurrentUser, HideDesktopIcons, "{59031A47-3F72-44A7-89C5-5595FE6B30EE}", 0, 1)));
        list.Add(Toggle("desktop-network", OptimizerTab.Explorer, "桌面图标", "桌面显示“网络”",
            "在桌面显示网络图标。", D(RegistryHive.CurrentUser, HideDesktopIcons, "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", 0, 1)));
        list.Add(Toggle("desktop-recycle", OptimizerTab.Explorer, "桌面图标", "桌面显示回收站",
            "在桌面显示回收站图标。", D(RegistryHive.CurrentUser, HideDesktopIcons, "{645FF040-5081-101B-9F08-00AA002F954E}", 0, 1)) with { DefaultOn = true });

        list.Add(Toggle("taskbar-left", OptimizerTab.Explorer, "任务栏", "任务栏图标左对齐",
            "把任务栏应用图标从居中改为靠左，接近 Windows 10 的布局。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "TaskbarAl", 0, 1)));

        list.Add(Toggle("taskbar-seconds", OptimizerTab.Explorer, "任务栏", "时钟显示秒",
            "在任务栏时钟中显示秒数。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "ShowSecondsInSystemClock", 1, 0)));

        list.Add(Toggle("taskbar-widgets", OptimizerTab.Explorer, "任务栏", "隐藏小组件按钮",
            "移除任务栏左侧的天气/小组件入口。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "TaskbarDa", 0, 1)));

        list.Add(Toggle("taskbar-chat", OptimizerTab.Explorer, "任务栏", "隐藏聊天按钮",
            "移除任务栏上的 Microsoft Teams 聊天入口。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "TaskbarMn", 0, 1)));

        list.Add(Toggle("taskbar-taskview", OptimizerTab.Explorer, "任务栏", "隐藏任务视图按钮",
            "移除任务栏上的任务视图（虚拟桌面）按钮，Win+Tab 仍然可用。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "ShowTaskViewButton", 0, 1)));

        list.Add(new TweakDefinition
        {
            Id = "taskbar-search", Tab = OptimizerTab.Explorer, Group = "任务栏", Kind = TweakKind.Choice,
            Title = "任务栏搜索样式",
            Description = "选择任务栏搜索框的显示方式。",
            Choices =
            [
                new("隐藏搜索", [D(RegistryHive.CurrentUser, ExplorerAdvanced, "SearchboxTaskbarMode", 0, 1)]),
                new("仅显示图标", [D(RegistryHive.CurrentUser, ExplorerAdvanced, "SearchboxTaskbarMode", 1, 1)], IsDefault: true),
                new("显示搜索框", [D(RegistryHive.CurrentUser, ExplorerAdvanced, "SearchboxTaskbarMode", 2, 1)]),
                new("搜索框和标签", [D(RegistryHive.CurrentUser, ExplorerAdvanced, "SearchboxTaskbarMode", 3, 1)])
            ]
        });

        list.Add(Toggle("start-recommendations", OptimizerTab.Explorer, "开始菜单", "隐藏开始菜单“推荐”区",
            "收起开始菜单中的推荐项目和最近使用提示。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "Start_IrisRecommendations", 0, 1)));

        list.Add(Toggle("start-recent-apps", OptimizerTab.Explorer, "开始菜单", "隐藏最近添加的应用",
            "不在开始菜单中显示“最近添加”的应用列表。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "Start_NotifyNewApps", 0, 1)));

        list.Add(Toggle("start-most-used", OptimizerTab.Explorer, "开始菜单", "隐藏最常用的应用",
            "关闭开始菜单对应用启动次数的跟踪，并隐藏“最常用”列表。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "Start_TrackProgs", 0, 1)));

        list.Add(Toggle("dark-apps", OptimizerTab.Explorer, "主题与透明", "应用使用深色模式",
            "把应用界面切换为深色主题。",
            D(RegistryHive.CurrentUser, Personalize, "AppsUseLightTheme", 0, 1)));

        list.Add(Toggle("dark-system", OptimizerTab.Explorer, "主题与透明", "系统界面使用深色模式",
            "把开始菜单、任务栏等系统界面切换为深色主题。",
            D(RegistryHive.CurrentUser, Personalize, "SystemUsesLightTheme", 0, 1)));

        list.Add(Toggle("transparency", OptimizerTab.Explorer, "主题与透明", "启用透明效果",
            "开启 Mica/亚克力透明效果。关闭可略微降低 GPU 占用。",
            D(RegistryHive.CurrentUser, Personalize, "EnableTransparency", 1, 0)) with { DefaultOn = true });

        list.Add(Toggle("accent-taskbar", OptimizerTab.Explorer, "主题与透明", "任务栏显示主题色",
            "在任务栏和开始菜单上使用 Windows 主题强调色。",
            D(RegistryHive.CurrentUser, Personalize, "ColorPrevalence", 1, 0)));

        // ── 隐私与安全 ────────────────────────────────────────────────────────────
        list.Add(Toggle("telemetry", OptimizerTab.Privacy, "遥测与诊断", "禁用遥测数据收集",
            "把诊断数据策略设为最低级别。Windows 家庭版仍可能发送基本诊断数据，专业版效果更好。",
            D(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, null))
            with { Recommended = true });

        list.Add(Toggle("feedback", OptimizerTab.Privacy, "遥测与诊断", "关闭诊断反馈请求",
            "不再定期弹出“给我们反馈”的提示。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null)));

        list.Add(Toggle("tailored-experiences", OptimizerTab.Privacy, "遥测与诊断", "关闭定制体验",
            "阻止 Windows 使用诊断数据推送个性化提示和广告。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1))
            with { Recommended = true });

        list.Add(Toggle("ceip", OptimizerTab.Privacy, "遥测与诊断", "关闭客户体验改善计划",
            "退出 Windows 客户体验改善计划（CEIP）。",
            D(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable", 0, 1)));

        list.Add(Toggle("wer", OptimizerTab.Privacy, "遥测与诊断", "关闭 Windows 错误报告",
            "阻止 Windows 错误报告收集并上传崩溃信息。",
            D(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, 0)));

        list.Add(Toggle("activity-history", OptimizerTab.Privacy, "遥测与诊断", "禁用活动历史记录",
            "停止记录和上传本机的应用/文件活动历史。",
            D(RegistryHive.LocalMachine, PoliciesWindows + @"\System", "PublishUserActivities", 0, 1),
            D(RegistryHive.LocalMachine, PoliciesWindows + @"\System", "UploadUserActivities", 0, 1))
            with { Recommended = true });

        list.Add(Toggle("advertising-id", OptimizerTab.Privacy, "遥测与诊断", "关闭广告 ID",
            "让应用无法使用广告 ID 向你推送个性化广告。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1),
            D(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1, null))
            with { Recommended = true });

        list.Add(Toggle("location", OptimizerTab.Privacy, "权限与传感器", "关闭定位服务",
            "拒绝所有应用访问位置，并禁用系统定位策略。",
            S(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny", "Allow"),
            D(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1, 0)));

        list.Add(Toggle("camera", OptimizerTab.Privacy, "权限与传感器", "关闭摄像头访问",
            "拒绝所有应用访问摄像头。视频会议前请先恢复。",
            S(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam", "Value", "Deny", "Allow")));

        list.Add(Toggle("microphone", OptimizerTab.Privacy, "权限与传感器", "关闭麦克风访问",
            "拒绝所有应用访问麦克风。语音通话前请先恢复。",
            S(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone", "Value", "Deny", "Allow")));

        list.Add(Toggle("voice-activation", OptimizerTab.Privacy, "权限与传感器", "关闭语音激活",
            "阻止应用在后台监听“你好小娜”等语音激活词。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Speech_OneCore\Settings\VoiceActivation", "EnableVoiceActivation", 0, null),
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Speech_OneCore\Settings\VoiceActivation\UserPreferenceForAllApps", "AgentActivationEnabled", 0, null)));

        list.Add(Toggle("online-speech", OptimizerTab.Privacy, "权限与传感器", "关闭在线语音识别",
            "语音输入只在本地处理，不把录音发送到云端。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0, 1)));

        list.Add(Toggle("inking-personalization", OptimizerTab.Privacy, "权限与传感器", "关闭输入个性化",
            "停止收集手写、键盘输入样本和联系人数据。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, 0),
            D(RegistryHive.CurrentUser, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, 0),
            D(RegistryHive.CurrentUser, @"Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0, 1)));

        list.Add(Toggle("clipboard-sync", OptimizerTab.Privacy, "权限与传感器", "关闭跨设备剪贴板同步",
            "剪贴板内容不再自动上传到 Microsoft 账户。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Clipboard", "CloudClipboardAutomaticUpload", 0, 1)));

        list.Add(Toggle("remote-assistance", OptimizerTab.Privacy, "权限与传感器", "关闭远程协助",
            "阻止他人通过远程协助连接这台电脑。",
            D(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0, 1)));

        list.Add(Toggle("search-highlights", OptimizerTab.Privacy, "搜索与推荐", "关闭搜索亮点",
            "移除搜索框中的节日图标、热门内容等动态推荐。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0, 1)));

        list.Add(Toggle("web-search", OptimizerTab.Privacy, "搜索与推荐", "关闭开始菜单网络搜索",
            "开始菜单搜索只查本机内容，不请求 Bing 网络结果。",
            D(RegistryHive.CurrentUser, PolicyExplorer, "DisableSearchBoxSuggestions", 1, 0),
            D(RegistryHive.CurrentUser, Search, "BingSearchEnabled", 0, 1),
            D(RegistryHive.CurrentUser, Search, "CortanaConsent", 0, 1)));

        list.Add(Toggle("delivery-optimization", OptimizerTab.Privacy, "搜索与推荐", "关闭传递优化对等下载",
            "Windows 更新只从 Microsoft 服务器下载，不从其他电脑上传/下载。",
            D(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, 1)));

        list.Add(Toggle("autoplay", OptimizerTab.Privacy, "搜索与推荐", "禁用自动播放",
            "插入 U 盘、光盘时不再自动运行或弹出选择窗口。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay", 1, 0)));

        list.Add(new TweakDefinition
        {
            Id = "uac-level", Tab = OptimizerTab.Privacy, Group = "用户账户控制", Kind = TweakKind.Choice,
            Title = "UAC 通知级别",
            Description = "控制应用尝试提升权限时的提示方式。修改后需要重新登录或重启。",
            Risky = true, NeedsReboot = true,
            Choices =
            [
                new("始终通知（最安全）",
                [
                    D(RegistryHive.LocalMachine, UacPolicy, "ConsentPromptBehaviorAdmin", 2, 5),
                    D(RegistryHive.LocalMachine, UacPolicy, "PromptOnSecureDesktop", 1, 1)
                ]),
                new("仅应用通知（Windows 默认）",
                [
                    D(RegistryHive.LocalMachine, UacPolicy, "ConsentPromptBehaviorAdmin", 5, 5),
                    D(RegistryHive.LocalMachine, UacPolicy, "PromptOnSecureDesktop", 1, 1)
                ], IsDefault: true),
                new("从不通知（不推荐）",
                [
                    D(RegistryHive.LocalMachine, UacPolicy, "ConsentPromptBehaviorAdmin", 0, 5),
                    D(RegistryHive.LocalMachine, UacPolicy, "PromptOnSecureDesktop", 0, 1)
                ])
            ]
        });

        // ── 性能与游戏 ────────────────────────────────────────────────────────────
        list.Add(Toggle("game-mode", OptimizerTab.Performance, "游戏与显卡", "启用游戏模式",
            "让 Windows 优先把 CPU/GPU 资源分配给前台游戏。",
            D(RegistryHive.CurrentUser, GameBar, "AutoGameModeEnabled", 1, 0),
            D(RegistryHive.CurrentUser, GameBar, "AllowAutoGameMode", 1, 0)) with { DefaultOn = true });

        list.Add(Toggle("game-dvr", OptimizerTab.Performance, "游戏与显卡", "关闭后台录制（Game DVR）",
            "关闭游戏栏后台录制和截图捕获，减少游戏帧率损失。",
            D(RegistryHive.CurrentUser, GameConfig, "GameDVR_Enabled", 0, 1),
            D(RegistryHive.CurrentUser, GameDvr, "AppCaptureEnabled", 0, 1)) with { Recommended = true });

        list.Add(Toggle("game-bar-startup", OptimizerTab.Performance, "游戏与显卡", "关闭游戏栏启动面板",
            "启动游戏时不再自动弹出 Xbox 游戏栏面板。",
            D(RegistryHive.CurrentUser, GameBar, "ShowStartupPanel", 0, 1)));

        list.Add(Toggle("hags", OptimizerTab.Performance, "游戏与显卡", "硬件加速 GPU 计划",
            "启用 HAGS，让 GPU 直接管理显存调度，部分游戏可降低延迟。需要重启系统。",
            D(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1))
            with { NeedsReboot = true, DefaultOn = true });

        list.Add(Toggle("mouse-accel", OptimizerTab.Performance, "游戏与显卡", "关闭鼠标指针加速",
            "移除 Windows 的鼠标加速曲线，FPS 游戏中瞄准更稳定。",
            S(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "0", "1"),
            S(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", "0", "6"),
            S(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", "0", "10")));

        list.Add(Toggle("visual-effects", OptimizerTab.Performance, "界面与响应", "视觉效果设为最佳性能",
            "关闭大部分窗口动画与阴影，优先响应速度。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2, 0)));

        list.Add(Toggle("window-animations", OptimizerTab.Performance, "界面与响应", "关闭窗口动画",
            "最小化、最大化和还原窗口时不再播放动画。",
            S(RegistryHive.CurrentUser, WindowMetrics, "MinAnimate", "0", "1")));

        list.Add(Toggle("taskbar-animations", OptimizerTab.Performance, "界面与响应", "关闭任务栏动画",
            "关闭任务栏按钮的淡入淡出与闪烁动画。",
            D(RegistryHive.CurrentUser, ExplorerAdvanced, "TaskbarAnimations", 0, 1)));

        list.Add(Toggle("aero-peek", OptimizerTab.Performance, "界面与响应", "关闭 Aero Peek 预览",
            "鼠标悬停任务栏时不再显示窗口透明预览。",
            D(RegistryHive.CurrentUser, Dwm, "EnableAeroPeek", 0, 1)));

        list.Add(Toggle("menu-delay", OptimizerTab.Performance, "界面与响应", "加快菜单弹出速度",
            "把菜单显示延迟从 400ms 降到 0ms。",
            S(RegistryHive.CurrentUser, Desktop, "MenuShowDelay", "0", "400")));

        list.Add(Toggle("startup-delay", OptimizerTab.Performance, "界面与响应", "取消启动延迟",
            "删除登录后启动应用的 10 秒延迟，启动项会更快出现。",
            D(RegistryHive.CurrentUser, Explorer + @"\Serialize", "StartupDelayInMSec", 0, null)));

        list.Add(Toggle("background-apps", OptimizerTab.Performance, "界面与响应", "禁止应用后台运行",
            "阻止 UWP 应用在后台常驻，减少内存和电量占用。可能影响通知与后台同步。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, 0),
            D(RegistryHive.CurrentUser, Search, "BackgroundAppGlobalToggle", 0, 1)) with { Recommended = true });

        list.Add(Toggle("power-throttling", OptimizerTab.Performance, "系统服务", "关闭后台节流",
            "禁用 Windows Power Throttling，后台程序不再被强制降频。会增加功耗。",
            D(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, null)));

        list.Add(Toggle("priority-separation", OptimizerTab.Performance, "系统服务", "优化前台程序响应",
            "把 Win32PrioritySeparation 设为 26，让前台程序获得更短的时间片。",
            D(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 26, 2)));

        list.Add(Toggle("prefetch", OptimizerTab.Performance, "系统服务", "禁用预读取（Prefetch）",
            "关闭 Prefetch 预读。SSD 用户可以关闭以减少磁盘写入，机械硬盘建议保持开启。",
            D(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher", 0, 3)));

        list.Add(Toggle("sysmain", OptimizerTab.Performance, "系统服务", "禁用 SysMain 服务",
            "停止 Superfetch/SysMain 服务。SSD 上收益有限，机械硬盘或低内存设备可能变慢。",
            D(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", 4, 2)) with { Risky = true });

        // ── Windows 更新 ──────────────────────────────────────────────────────────
        list.Add(Toggle("wu-drivers", OptimizerTab.Update, "Windows 更新", "不从 Windows 更新安装驱动",
            "系统更新时跳过驱动包，避免 Windows 覆盖显卡/网卡驱动。",
            D(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0, 1)));

        list.Add(Toggle("wu-other-products", OptimizerTab.Update, "Windows 更新", "不同步更新其他 Microsoft 产品",
            "Windows 更新只为 Windows 本身提供更新，不包含 Office 等其他产品。",
            D(RegistryHive.LocalMachine, WindowsUpdateSettings, "AllowMUUpdateService", 0, 1)));

        list.Add(Toggle("wu-restart-notification", OptimizerTab.Update, "Windows 更新", "关闭更新重启提醒",
            "安装更新后不再弹出要求重启的提醒。",
            D(RegistryHive.LocalMachine, WindowsUpdateSettings, "SetAutoRestartNotificationDisable", 1, 0)));

        list.Add(Toggle("wu-no-auto-reboot", OptimizerTab.Update, "Windows 更新", "阻止登录时自动重启",
            "有用户登录时，安装更新后不自动重启电脑。",
            D(RegistryHive.LocalMachine, WindowsUpdatePolicy, "NoAutoRebootWithLoggedOnUsers", 1, null)));

        // ── 电源与声音 ────────────────────────────────────────────────────────────
        list.Add(new TweakDefinition
        {
            Id = "power-plan", Tab = OptimizerTab.Power, Group = "电源", Kind = TweakKind.Choice,
            Title = "电源计划",
            Description = "高性能会提高功耗与发热；节能适合笔记本续航。",
            ExtraBackupTargets =
            [
                new(RegistryHive.LocalMachine, PowerSchemes, "ActivePowerScheme", RegistryValueKind.String)
            ],
            Choices =
            [
                new("平衡", [], IsDefault: true, Apply: ct => SetPowerPlanAsync(BalancedPlan, ct)),
                new("高性能", [], Apply: ct => SetPowerPlanAsync(HighPerformancePlan, ct)),
                new("节能", [], Apply: ct => SetPowerPlanAsync(PowerSaverPlan, ct))
            ],
            ReadChoiceIndex = () => ReadActivePowerPlanIndex()
        });

        list.Add(Toggle("fast-startup", OptimizerTab.Power, "电源", "关闭快速启动",
            "关闭混合关机（Fast Startup）。双系统或更新驱动后异常时可关闭。",
            D(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0, 1)));

        list.Add(new TweakDefinition
        {
            Id = "hibernate", Tab = OptimizerTab.Power, Group = "电源",
            Title = "启用休眠",
            Description = "休眠会把内存写入 hiberfil.sys。关闭可释放与内存等大的磁盘空间。",
            Risky = true, HasDefault = false,
            ExtraBackupTargets =
            [
                new(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", RegistryValueKind.DWord)
            ],
            ReadToggle = () => RegistryAccess.Read(
                new RegistryTarget(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", RegistryValueKind.DWord)) is int value && value != 0,
            ApplyToggle = async (on, ct) =>
            {
                var result = await ShellRunner.RunPowerCfgAsync(on ? "/h on" : "/h off", ct).ConfigureAwait(false);
                if (result.ExitCode != 0) throw new InvalidOperationException(result.Combined.Trim());
            }
        });

        list.Add(Toggle("usb-suspend", OptimizerTab.Power, "电源", "禁用 USB 选择性暂停",
            "防止 USB 设备在空闲时被挂起，解决键鼠/声卡断连。",
            D(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\USB", "DisableSelectiveSuspend", 1, 0)));

        list.Add(Toggle("startup-sound", OptimizerTab.Power, "声音", "关闭 Windows 启动声音",
            "登录时不再播放 Windows 启动音效。",
            D(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation", "DisableStartupSound", 1, 0)));

        list.Add(Toggle("audio-ducking", OptimizerTab.Power, "声音", "通话时不降低其他声音",
            "音频通信活动时不再自动降低音乐/游戏音量。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference", 3, null)));

        // ── 通知与建议 ────────────────────────────────────────────────────────────
        list.Add(Toggle("notifications", OptimizerTab.Notifications, "通知", "关闭所有通知",
            "关闭系统通知横幅与通知中心，包括应用通知。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0, 1)));

        list.Add(Toggle("notification-sound", OptimizerTab.Notifications, "通知", "关闭通知声音",
            "收到通知时不再播放提示音。",
            D(RegistryHive.CurrentUser, Notifications, "NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", 0, 1)));

        list.Add(Toggle("lock-notifications", OptimizerTab.Notifications, "通知", "关闭锁屏通知",
            "锁屏界面上不显示应用通知内容。",
            D(RegistryHive.CurrentUser, Notifications, "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", 0, 1)));

        list.Add(Toggle("suggestions", OptimizerTab.Notifications, "建议与广告", "关闭系统建议与广告",
            "关闭开始菜单、设置、锁屏中的建议、推广和“提示”内容。",
            D(RegistryHive.CurrentUser, ContentDelivery, "ContentDeliveryAllowed", 0, 1),
            D(RegistryHive.CurrentUser, ContentDelivery, "SubscribedContent-338388Enabled", 0, 1),
            D(RegistryHive.CurrentUser, ContentDelivery, "SubscribedContent-338389Enabled", 0, 1),
            D(RegistryHive.CurrentUser, ContentDelivery, "SubscribedContent-338393Enabled", 0, 1),
            D(RegistryHive.CurrentUser, ContentDelivery, "SubscribedContent-353694Enabled", 0, 1),
            D(RegistryHive.CurrentUser, ContentDelivery, "SubscribedContent-353696Enabled", 0, 1),
            D(RegistryHive.CurrentUser, ContentDelivery, "SystemPaneSuggestionsEnabled", 0, 1),
            D(RegistryHive.CurrentUser, ContentDelivery, "SoftLandingEnabled", 0, 1))
            with { Recommended = true });

        list.Add(Toggle("silent-apps", OptimizerTab.Notifications, "建议与广告", "禁止静默安装推荐应用",
            "阻止 Windows 在后台自动安装推广应用和厂商预装应用。",
            D(RegistryHive.CurrentUser, ContentDelivery, "SilentInstalledAppsEnabled", 0, 1),
            D(RegistryHive.CurrentUser, ContentDelivery, "PreInstalledAppsEnabled", 0, 1),
            D(RegistryHive.CurrentUser, ContentDelivery, "OemPreInstalledAppsEnabled", 0, 1)));

        list.Add(Toggle("welcome-experience", OptimizerTab.Notifications, "建议与广告", "关闭 Windows 欢迎体验",
            "首次登录/更新后不再弹出“欢迎使用 Windows”的全屏引导。",
            D(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, null)));

        list.Add(Toggle("finish-setup", OptimizerTab.Notifications, "建议与广告", "关闭“完成设备设置”建议",
            "不再提示继续完成设备设置或推荐服务。",
            D(RegistryHive.CurrentUser, ContentDelivery, "SubscribedContent-310093Enabled", 0, 1)));

        list.Add(Toggle("lockscreen-spotlight", OptimizerTab.Notifications, "建议与广告", "关闭锁屏聚焦",
            "锁屏不再显示 Windows 聚焦图片和推广文案。",
            D(RegistryHive.CurrentUser, ContentDelivery, "RotatingLockScreenEnabled", 0, 1),
            D(RegistryHive.CurrentUser, ContentDelivery, "RotatingLockScreenOverlayEnabled", 0, 1)));

        return list;
    }

    private static TweakDefinition Toggle(
        string id, OptimizerTab tab, string group, string title, string description, params RegistryOp[] ops)
        => new() { Id = id, Tab = tab, Group = group, Title = title, Description = description, Ops = ops };

    private static RegistryOp D(RegistryHive hive, string path, string name, int on, int? off)
        => new(new RegistryTarget(hive, path, name, RegistryValueKind.DWord), on, off);

    private static RegistryOp S(RegistryHive hive, string path, string name, string? on, string? off, bool expand = false)
        => new(new RegistryTarget(hive, path, name, expand ? RegistryValueKind.ExpandString : RegistryValueKind.String), on, off);

    private static async Task SetPowerPlanAsync(string guid, CancellationToken ct)
    {
        var result = await ShellRunner.RunPowerCfgAsync("/setactive " + guid, ct).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Combined.Trim());
    }

    private static int ReadActivePowerPlanIndex()
    {
        var raw = RegistryAccess.Read(new RegistryTarget(RegistryHive.LocalMachine, PowerSchemes, "ActivePowerScheme", RegistryValueKind.String)) as string;
        var active = (raw ?? "").Trim().Trim('{', '}');
        if (string.Equals(active, BalancedPlan, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(active, HighPerformancePlan, StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(active, PowerSaverPlan, StringComparison.OrdinalIgnoreCase)) return 2;
        return -1;
    }
}
