using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Services;
using TubaWinUi3.Services.FileLock;
using TubaWinUi3.ShellIntegration;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>
/// 「文件占用查看」（对标 PowerToys File Locksmith）。
/// 输入/选择目标路径 → 扫描持有句柄的进程 → 展开看具体持有的路径，并可结束占用进程（二次确认）。
/// 页面默认**不自动扫描**：句柄枚举有秒级开销，且受保护进程会如实计入跳过数，自动跑容易让人误解；
/// 仅当经 --file-lock &lt;路径&gt;（资源管理器右键菜单）带参打开时，预填路径并自动扫描一次。
/// </summary>
public sealed partial class FileLockPage : Page, ILocalizablePage
{
    // 语义状态色（跟随系统主题，经 ThemeColors 取官方 SystemFillColor* 语义色）
    private static Color AccentRed => ThemeColors.AccentRed;
    private static Color AccentGreen => ThemeColors.AccentGreen;
    private static Color AccentOrange => ThemeColors.AccentOrange;

    /// <summary>PID 列宽（表头与数据行必须一致，否则两边的星号列宽度不同、整行错位）。</summary>
    private const double PidColumnWidth = 80;

    /// <summary>
    /// 表头左内边距 = 列表内边距 12 + ExpanderHeaderBorderThickness 1 + ExpanderHeaderPadding 16。
    /// 数据行在 Expander 表头里，多出后面两段缩进，表头要补平才对得齐。
    /// </summary>
    private const double HeaderPaddingLeft = 29;

    private const double ListPaddingRight = 12;

    private CancellationTokenSource? _cts;
    private bool _isPageAlive = true;
    private FileLockScanResult? _lastResult;
    private FrameworkElement? _actionMirror;
    private Grid? _firstRowGrid;
    private bool _headerWidthSynced;

    /// <summary>--file-lock 带入、等待 Loaded 后预填并自动扫描的路径。</summary>
    private string? _pendingAutoScanPath;
    private bool _promptChecked;
    private bool _shellMenuToggleBusy;
    private bool _adminRestartBusy;

    public FileLockPage()
    {
        InitializeComponent();

        HeaderBorder.Background = new SolidColorBrush(ThemeColors.HeaderBg);
        ListBorder.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);
        RefreshActionHeaderMirror();

        // 代码构建的画刷不会随主题自动刷新，切换主题后重建表头占位并按缓存结果重渲染
        ActualThemeChanged += (_, _) =>
        {
            HeaderBorder.Background = new SolidColorBrush(ThemeColors.HeaderBg);
            ListBorder.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);
            RefreshActionHeaderMirror();
            if (_lastResult is { } result) RenderResult(result);
        };

        Loaded += OnPageLoaded;
        UpdateAdminBarTexts();
        InitShellMenuSection();

        // 页面销毁时取消在跑的扫描，避免结果回到已经不在可视树上的控件上。
        Unloaded += (_, _) =>
        {
            _isPageAlive = false;
            _cts?.Cancel();
        };
    }

    /// <summary>经右键菜单 --file-lock &lt;路径&gt; 打开时拿到目标路径（Loaded 后自动扫描一次）。</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string path && !string.IsNullOrWhiteSpace(path))
            _pendingAutoScanPath = path;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_pendingAutoScanPath is { } path)
        {
            _pendingAutoScanPath = null;
            PathBox.Text = path;
            _ = RunScanAsync();   // 不 await：让首问弹窗不被秒级句柄枚举拖住
        }

        if (_promptChecked) return;
        _promptChecked = true;
        await MaybePromptShellMenuAsync();
    }

    /// <summary>语言切换后重算自绘文案（打了 Uid 的控件由 WinUI3Localizer 自动刷新）。</summary>
    public void ApplyLocalization()
    {
        // 行内按钮的文案是自绘的，必须重建整份结果；RenderResult 同时覆盖
        // 失败提示 / 「没有占用」提示 / 摘要三种分支的本地化。
        RefreshActionHeaderMirror();
        if (_lastResult is { } result) RenderResult(result);

        UpdateAdminBarTexts();
        RefreshShellMenuTexts();
    }

    // ---------- 右键菜单集成 ----------

    private void InitShellMenuSection()
    {
        ShellMenuToggle.OnContent = L("FileLock_MenuToggleOn", "已开启");
        ShellMenuToggle.OffContent = L("FileLock_MenuToggleOff", "未开启");
        ShellMenuToggle.IsEnabled = FileLockShellMenuService.IsSupported;
        _shellMenuToggleBusy = true;
        ShellMenuToggle.IsOn = FileLockShellMenuService.IsEnabled();
        _shellMenuToggleBusy = false;
        RefreshShellMenuTexts();
    }

    /// <summary>本页两种模式的说明与语言同步（已开启时把当前语言的菜单文案写给注册表/标记文件）。</summary>
    private void RefreshShellMenuTexts()
    {
        ShellMenuDescText.Text = FileLockShellMenuService.Mode switch
        {
            FileLockShellMenuContract.MenuMode.ModernMenu =>
                L("FileLock_MenuDescModern", "Windows 11 新版右键菜单：在资源管理器中右键文件或文件夹即可直接检测其占用。"),
            FileLockShellMenuContract.MenuMode.ClassicRegistry =>
                L("FileLock_MenuDescClassic", "经典右键菜单：Windows 11 上位于「显示更多选项」，开启后立即生效。"),
            _ => L("FileLock_MenuDescUnsupported", "微软商店（MSIX）版的新版右键菜单需要 Windows 11，当前系统不支持。"),
        };

        if (!FileLockShellMenuService.IsEnabled()) return;

        // 语言切换后重写菜单文案（标记文件第一/二行、注册表 MUIVerb）；失败静默保留旧语言。
        FileLockShellMenuService.Enable(
            L("FileLock_ContextVerb", "用图吧工具箱检测文件占用"),
            L("FileLock_ContextToolTip", "查看是哪个进程占用了此文件"));
    }

    private void ShellMenuToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_shellMenuToggleBusy) return;
        ApplyShellMenuToggle(ShellMenuToggle.IsOn);
    }

    private void ApplyShellMenuToggle(bool enabled)
    {
        _shellMenuToggleBusy = true;
        ShellMenuToggle.IsEnabled = false;
        try
        {
            var error = enabled
                ? FileLockShellMenuService.Enable(
                    L("FileLock_ContextVerb", "用图吧工具箱检测文件占用"),
                    L("FileLock_ContextToolTip", "查看是哪个进程占用了此文件"))
                : FileLockShellMenuService.Disable();

            if (error is not null)
            {
                ShowError(string.Format(
                    enabled
                        ? L("FileLock_MenuEnableFailed", "添加右键菜单失败：{0}")
                        : L("FileLock_MenuDisableFailed", "移除右键菜单失败：{0}"),
                    error));
                ShellMenuToggle.IsOn = !enabled;   // 失败回滚（busy 标志吞掉这次 Toggled）
            }
        }
        finally
        {
            _shellMenuToggleBusy = false;
            ShellMenuToggle.IsEnabled = FileLockShellMenuService.IsSupported;
        }
    }

    /// <summary>首次打开本工具页询问一次是否加入右键菜单（先写标记防重复，仿 GameMonitorBackendService）。</summary>
    private async Task MaybePromptShellMenuAsync()
    {
        if (AppSettings.GetBool("FileLockShellMenuPrompted", false)) return;
        AppSettings.Set("FileLockShellMenuPrompted", true);

        if (!FileLockShellMenuService.IsSupported) return;
        if (FileLockShellMenuService.IsEnabled()) return;   // 旧版本/其他入口已开启：不再打扰

        var dialog = new ContentDialog
        {
            Title = L("FileLock_MenuPromptTitle", "添加右键菜单？"),
            Content = L("FileLock_MenuPromptBody", "要在资源管理器的右键菜单中加入「用图吧工具箱检测文件占用」吗？之后可在此页面随时开关。"),
            PrimaryButtonText = L("FileLock_MenuPromptEnable", "开启"),
            CloseButtonText = L("FileLock_MenuPromptSkip", "暂不开启"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        var confirmed = false;
        dialog.PrimaryButtonClick += (_, _) => confirmed = true;

        if (!await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(10))) return;
        if (!confirmed || !_isPageAlive) return;

        _shellMenuToggleBusy = true;
        ShellMenuToggle.IsOn = true;
        _shellMenuToggleBusy = false;
        ApplyShellMenuToggle(true);
    }

    private async void RestartExplorerBtn_Click(object sender, RoutedEventArgs e)
    {
        RestartExplorerBtn.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    ExplorerShellService.Restart();
                }
                catch (Exception ex)
                {
                    DispatcherQueue.TryEnqueue(() => ShowError(ex.Message));
                }
            });
        }
        finally
        {
            if (_isPageAlive) RestartExplorerBtn.IsEnabled = true;
        }
    }

    // ---------- 管理员提权重启 ----------

    private void UpdateAdminBarTexts()
    {
        AdminBar.Title = L("FileLock_AdminBarTitle", "当前未以管理员身份运行");
        AdminBar.Message = L("FileLock_AdminBarMessage", "部分受保护进程的占用可能看不到，以管理员身份重启可完整检测。");
        AdminBar.IsOpen = !App.IsRunningAsAdmin();
    }

    /// <summary>
    /// 以管理员身份重启并回到本工具、续扫当前路径。
    /// 打包版经 PowerShell 载体提权（提权进程会丢失包身份，App.TryRelaunchElevated 会带上恢复标记）；
    /// 非打包版直接 runas。成功后必须走 App.RequestExit()（保留完整收尾清理）。
    /// </summary>
    private async void RestartAsAdminBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_adminRestartBusy) return;
        _adminRestartBusy = true;
        RestartAsAdminBtn.IsEnabled = false;
        RestartAsAdminRing.Visibility = Visibility.Visible;
        RestartAsAdminRing.IsActive = true;

        try
        {
            var arguments = new List<string>();
            var path = PathBox.Text.Trim();
            if (path.Length > 0)
            {
                arguments.Add(FileLockShellMenuContract.FileLockArg);
                arguments.Add(path);
            }

            var ok = false;
            var detail = string.Empty;
            await Task.Run(() => { ok = App.TryRelaunchElevated(arguments, out detail); });

            if (ok)
            {
                App.RequestExit();
                return;
            }

            var dialog = new ContentDialog
            {
                Title = L("FileLock_AdminFailedTitle", "无法提权重启"),
                Content = string.Format(L("FileLock_AdminFailedMessage", "提权请求未成功：{0}"), detail),
                CloseButtonText = L("FileLock_Close", "关闭"),
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (_isPageAlive)
            {
                _adminRestartBusy = false;
                RestartAsAdminBtn.IsEnabled = true;
                RestartAsAdminRing.IsActive = false;
                RestartAsAdminRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// 表头「操作」那格塞一份透明度 0 的按钮副本占位。
    /// 表头与数据行是两个独立的 Grid，Auto 列各按自己的内容算宽度：表头只有「操作」两个字，
    /// 数据行是两个带文字的按钮——两边宽度不同会把星号列挤得不一致，整行错位。
    /// 用同一套文案与内边距的隐形副本占位，宽度天然一致，换语言/改字号都自动同步，
    /// 也就不需要维护一个「按钮到底多宽」的魔法数字。
    /// </summary>
    private void RefreshActionHeaderMirror()
    {
        if (_actionMirror is not null) ActionHeaderCell.Children.Remove(_actionMirror);

        var (mirror, _, _) = BuildActions();
        mirror.Opacity = 0;                 // 仍占布局空间（不是 Collapsed），宽度就是按钮的宽度
        mirror.IsHitTestVisible = false;
        ActionHeaderCell.Children.Insert(0, mirror);
        _actionMirror = mirror;
    }

    /// <summary>
    /// 让表头与数据行共用同一段可用宽度。
    /// 数据行在 Expander 的表头里，右侧被展开/折叠箭头占掉一段，可用宽度比表头窄；
    /// 两边星号列一宽一窄，列名就会整体右移（「PID」跑到绿色徽章右边就是这个问题）。
    /// 这里按实测差值把表头右侧留白补上，宽度追平后四列自然对齐——箭头多宽、换什么主题、
    /// 系统缩放多少都不需要写死数字。
    /// </summary>
    private void SyncHeaderWidth()
    {
        if (_headerWidthSynced || _firstRowGrid is null) return;

        double rowWidth = _firstRowGrid.ActualWidth;
        double headerWidth = HeaderGrid.ActualWidth;
        if (rowWidth <= 0 || headerWidth <= 0) return;   // 还没完成布局，等下一次 LayoutUpdated

        double surplus = headerWidth - rowWidth;
        if (surplus > 0.5)
            HeaderBorder.Padding = new Thickness(HeaderPaddingLeft, 6, ListPaddingRight + surplus, 6);

        _headerWidthSynced = true;
    }

    private void OnPageLayoutUpdated(object? sender, object e)
    {
        SyncHeaderWidth();
        if (_headerWidthSynced) LayoutUpdated -= OnPageLayoutUpdated;
    }

    private static string L(string key, string fallback) => LocalizationService.L(key, fallback);

    // ---------- 路径选择 ----------

    private void PickFileBtn_Click(object sender, RoutedEventArgs e)
    {
        // 过滤器文案也是给用户看的（原生对话框的「文件类型」下拉），随语言走；
        // 键与格式沿用设置页（SettingsPage 的 AllFilesFilter 用法），"名称\0模式\0\0"。
        var filter = L("Settings_AllFilesFilter", "所有文件") + "\0*.*\0\0";
        var picked = Win32Dialogs.PickOpen(filter, L("FileLock_PickFile.Text", "选择文件"));
        if (!string.IsNullOrWhiteSpace(picked)) PathBox.Text = picked;
    }

    private async void PickFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        var picked = await Win32Dialogs.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(picked)) PathBox.Text = picked;
    }

    // ---------- 扫描 ----------

    private async void ScanBtn_Click(object sender, RoutedEventArgs e) => await RunScanAsync();

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async Task RunScanAsync()
    {
        string path = PathBox.Text.Trim();
        if (path.Length == 0)
        {
            ShowError(L("FileLock_ErrorNoPath", "请先输入或选择要检查的文件 / 目录路径"));
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        ErrorBar.IsOpen = false;
        SummaryText.Visibility = Visibility.Collapsed;
        HeaderBorder.Visibility = Visibility.Collapsed;
        ListBorder.Visibility = Visibility.Collapsed;
        ListContainer.Children.Clear();
        SetScanning(true);

        // Progress<T> 在创建线程（UI 线程）上回调，这里再显式走 DispatcherQueue，双保险。
        var progress = new Progress<string>(text =>
        {
            if (!_isPageAlive) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isPageAlive) ProgressText.Text = text;
            });
        });

        try
        {
            var result = await FileLockService.ScanAsync(path, token, progress);
            if (!_isPageAlive) return;

            _lastResult = result;
            RenderResult(result);
        }
        catch (OperationCanceledException)
        {
            if (_isPageAlive)
                ShowError(L("FileLock_Cancelled", "扫描已取消"));
        }
        catch (Exception ex)
        {
            if (_isPageAlive)
                ShowError(string.Format(L("FileLock_ErrorFailed", "扫描失败：{0}"), ex.Message));
        }
        finally
        {
            if (_isPageAlive) SetScanning(false);
        }
    }

    private void SetScanning(bool scanning)
    {
        LoadingPanel.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = scanning;
        ProgressPanel.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ScanProgressBar.IsIndeterminate = scanning;
        if (!scanning) ProgressText.Text = "";

        ScanBtn.IsEnabled = !scanning;
        PickFileBtn.IsEnabled = !scanning;
        PickFolderBtn.IsEnabled = !scanning;
        CancelBtn.IsEnabled = scanning;
        PathBox.IsEnabled = !scanning;
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = L("FileLock_Failed", "操作失败");
        ErrorBar.Message = message;
        ErrorBar.Severity = InfoBarSeverity.Error;
        ErrorBar.IsOpen = true;
    }

    // ---------- 渲染 ----------

    private void RenderResult(FileLockScanResult result)
    {
        ListContainer.Children.Clear();
        _firstRowGrid = null;

        if (!result.Succeeded)
        {
            ShowError(result.Error switch
            {
                FileLockScanError.EmptyPath => L("FileLock_ErrorNoPath", "请先输入或选择要检查的文件 / 目录路径"),
                FileLockScanError.NotFound => L("FileLock_ErrorNotFound", "找不到该文件或目录，请检查路径是否正确。"),
                FileLockScanError.ResolveFailed => string.Format(
                    L("FileLock_ErrorResolve", "无法解析该路径（{0}）"),
                    FileLockErrorText.Describe(result.Failure, result.FailureCode)),
                _ => string.Format(
                    L("FileLock_ErrorFailed", "扫描失败：{0}"),
                    FileLockErrorText.Describe(result.Failure, result.FailureCode))
            });
            return;
        }

        if (result.Entries.Count == 0)
        {
            // 关键：即使一个占用者都没找到，也必须带上截断/跳过的说明——
            // 否则「没人占用」会被当成结论，而它可能只是「没扫全」。
            SummaryText.Text = L("FileLock_NoResult", "没有进程占用该路径。") + BuildCaveats(result);
            SummaryText.Foreground = new SolidColorBrush(HasCaveats(result) ? AccentOrange : ThemeColors.PrimaryText);
            SummaryText.Visibility = Visibility.Visible;
            return;
        }

        foreach (var entry in result.Entries)
            ListContainer.Children.Add(CreateRow(entry));

        // 表头与数据行的宽度对齐依赖实际布局，等这一轮布局跑完再校正一次。
        _headerWidthSynced = false;
        LayoutUpdated -= OnPageLayoutUpdated;
        LayoutUpdated += OnPageLayoutUpdated;

        UpdateSummary(result);
        HeaderBorder.Visibility = Visibility.Visible;
        ListBorder.Visibility = Visibility.Visible;
    }

    private static bool HasCaveats(FileLockScanResult result)
        => result.Truncated || result.SkippedProcesses > 0 || result.GuardedHandles > 0;

    /// <summary>结果不完整时的补充说明（截断 / 受保护进程 / 卡死放弃的句柄）。</summary>
    private static string BuildCaveats(FileLockScanResult result)
    {
        var text = "";
        if (result.Truncated)
            text += " " + L("FileLock_SummaryTruncated", "扫描被中断，结果可能不完整。");
        if (result.SkippedProcesses > 0)
            text += " " + string.Format(
                L("FileLock_SummarySkipped", "有 {0} 个受保护进程无法查询，可能遗漏。"),
                result.SkippedProcesses);
        if (result.GuardedHandles > 0)
            text += " " + string.Format(
                L("FileLock_SummaryGuarded", "有 {0} 个句柄因查询卡死被跳过，可能遗漏。"),
                result.GuardedHandles);
        return text;
    }

    /// <summary>摘要必须如实反映截断 / 跳过 / 保守放弃的数量，不能只报命中数。</summary>
    private void UpdateSummary(FileLockScanResult result)
    {
        SummaryText.Text = string.Format(
            L("FileLock_Summary", "共 {0} 个进程占用，涉及 {1} 个路径。"),
            result.Entries.Count,
            result.TotalLockedPaths) + BuildCaveats(result);

        SummaryText.Visibility = Visibility.Visible;
        SummaryText.Foreground = new SolidColorBrush(HasCaveats(result) ? AccentOrange : ThemeColors.PrimaryText);
    }

    private Border CreateRow(FileLockEntry entry)
    {
        var nameText = new TextBlock
        {
            Text = entry.ProcessName,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // 只放数值：表头已经写了 PID，重复的「PID」前缀会把数字挤开、和表头对不齐。
        // 左内边距为 0，让数字左边缘与表头「PID」严格对齐（右侧留白是给胶囊背景的）。
        var pidBadge = new Border
        {
            Padding = new Thickness(0, 2, 8, 2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(40, 74, 222, 128)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = entry.ProcessId.ToString(),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentGreen)
            }
        };
        ToolTipService.SetToolTip(pidBadge, $"PID {entry.ProcessId}");

        // 进程 exe 路径（非管理员运行时，受保护进程取不到，给明确提示而不是一个光秃秃的「—」）
        bool hasExePath = !string.IsNullOrEmpty(entry.ProcessPath);
        var pathText = new TextBlock
        {
            Text = hasExePath ? entry.ProcessPath : L("FileLock_ExeUnavailable", "—（需管理员权限）"),
            FontSize = 12,
            Foreground = new SolidColorBrush(hasExePath ? ThemeColors.DimText : AccentOrange),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTipService.SetToolTip(pathText, hasExePath
            ? entry.ProcessPath
            : L("FileLock_ExeUnavailableTip", "读不到该进程的 exe 路径。以管理员身份运行本工具通常能读到（受保护的系统进程除外）。"));

        // 操作栏：在资源管理器中定位该文件 + 结束进程（都带文字标签，图标按钮容易被忽略）
        var (actions, openBtn, killBtn) = BuildActions();
        openBtn.Click += (_, _) => OpenInExplorer(entry);
        killBtn.Click += async (_, _) => await KillEntryAsync(entry);

        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PidColumnWidth) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // 操作列 Auto = 两个按钮的实际宽度；表头用隐形副本占同样的宽度（见 RefreshActionHeaderMirror），
        // 于是表头/数据行的星号列宽度一致、四列对齐，按钮右边缘也紧贴展开箭头。
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(nameText); Grid.SetColumn(nameText, 0);
        header.Children.Add(pidBadge); Grid.SetColumn(pidBadge, 1);
        header.Children.Add(pathText); Grid.SetColumn(pathText, 2);
        header.Children.Add(actions); Grid.SetColumn(actions, 3);
        _firstRowGrid ??= header;   // 供 SyncHeaderWidth 与表头比对可用宽度

        // 内容：先摆明「是哪个 exe 占的」，再列出它持有的具体路径
        // （扫目录时下面往往是若干 .log 之类的子文件，光看它们认不出是哪个程序）。
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = L("FileLock_ProcessImage", "进程 exe"),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        content.Children.Add(new TextBlock
        {
            Text = hasExePath ? entry.ProcessPath : L("FileLock_ExeUnavailable", "—（需管理员权限）"),
            FontSize = 12,
            Foreground = new SolidColorBrush(hasExePath ? ThemeColors.SecondaryText : AccentOrange),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(12, 0, 0, 6)
        });

        content.Children.Add(new TextBlock
        {
            Text = string.Format(
                L("FileLock_ColLocked", "持有路径（{0}）"),
                entry.LockedPaths.Count),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        foreach (var lockedPath in entry.LockedPaths)
        {
            content.Children.Add(new TextBlock
            {
                Text = lockedPath,
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(12, 0, 0, 0)
            });
        }

        var expander = new Expander
        {
            Header = header,
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = false
        };

        return new Border
        {
            Padding = new Thickness(12, 4, 12, 4),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = expander
        };
    }

    // ---------- 行内操作 ----------

    /// <summary>
    /// 两个行内动作按钮。数据行与表头占位共用这一份构造，保证宽度一致。
    /// </summary>
    private static (StackPanel Panel, Button Open, Button Kill) BuildActions()
    {
        var openBtn = MakeActionButton("\uE8B7", L("FileLock_OpenLocation", "打开位置"),
            ThemeColors.SecondaryText, L("FileLock_OpenLocationTip", "在资源管理器中打开该文件"));
        var killBtn = MakeActionButton("\uE894", L("FileLock_Kill", "结束进程"),
            AccentRed, L("FileLock_KillTip", "尝试结束该占用进程（需二次确认）"));

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(openBtn);
        panel.Children.Add(killBtn);
        return (panel, openBtn, killBtn);
    }

    /// <summary>操作栏按钮：小图标 + 文字标签（纯图标按钮容易被当成装饰而忽略）。</summary>
    private static Button MakeActionButton(string glyph, string label, Color foreground, string tooltip)
    {
        var button = new Button
        {
            Padding = new Thickness(8, 2, 8, 2),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(foreground),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontSize = 12 },
                    new TextBlock { Text = label, FontSize = 12 }
                }
            }
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    /// <summary>
    /// 在资源管理器中定位该进程持有的文件（一个进程可能持有多个路径，取第一个）。
    /// 写法与 ErrorReportService / StartupManagerTool 一致：explorer.exe /select 经
    /// CreateProcessW 传 Unicode 命令行，中文路径不会被代码页搞乱。
    /// </summary>
    private void OpenInExplorer(FileLockEntry entry)
    {
        string? path = entry.LockedPaths.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        if (path is null)
        {
            ShowError(L("FileLock_OpenFailed", "无法打开所在位置：该进程没有可定位的持有路径"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError(string.Format(L("FileLock_OpenFailed", "无法打开所在位置：{0}"), ex.Message));
        }
    }

    // ---------- 结束进程 ----------

    private async Task KillEntryAsync(FileLockEntry entry)
    {
        // 本程序自身绝不能被结束：走 Process.Kill 会跳过 App.RequestExit() 的收尾，
        // 而 FPS 的 ETW 内核会话不随进程终止回收，残留会让下次启动的帧率采集失效。
        // 目标路径指到程序自己持有的文件时（日志、配置、WebView2 用户目录）就会命中这条。
        if (entry.ProcessId == Environment.ProcessId)
        {
            ErrorBar.Title = L("FileLock_KillFailed", "结束进程失败");
            ErrorBar.Message = L("FileLock_KillSelf", "不能结束图吧工具箱自身进程。");
            ErrorBar.Severity = InfoBarSeverity.Warning;
            ErrorBar.IsOpen = true;
            return;
        }

        bool confirmed = false;
        var dialog = new ContentDialog
        {
            Title = L("FileLock_KillConfirmTitle", "结束进程"),
            Content = string.Format(
                L("FileLock_KillConfirmBody", "确定要结束 {0} (PID {1}) 吗？未保存的数据会丢失。"),
                entry.ProcessName,
                entry.ProcessId),
            PrimaryButtonText = L("FileLock_Kill", "结束进程"),
            CloseButtonText = L("FileLock_Cancel.Text", "取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.PrimaryButtonClick += (_, _) => confirmed = true;

        // 复用统一的并发守卫：同一时刻只允许一个 ContentDialog，失败一律按「未确认」处理。
        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(5));
        if (!confirmed || !_isPageAlive) return;

        // 对话框期间目标可能已退出、PID 可能被系统复用：动手前核对身份，避免误杀无关进程。
        if (FileLockService.VerifyProcessIdentity(entry.ProcessId, entry.ProcessName, entry.ProcessPath, entry.StartTimeUtc)
            != ProcessIdentityCheck.Match)
        {
            ErrorBar.Title = L("FileLock_KillFailed", "结束进程失败");
            ErrorBar.Message = L("FileLock_KillStale", "该进程已退出或 PID 已被复用，为避免误杀，请重新扫描后再试。");
            ErrorBar.Severity = InfoBarSeverity.Warning;
            ErrorBar.IsOpen = true;
            return;
        }

        if (!PortViewerService.KillProcess(entry.ProcessId, out var error))
        {
            ErrorBar.Title = L("FileLock_KillFailed", "结束进程失败");
            ErrorBar.Message = string.Format(
                L("FileLock_KillFailedMsg", "无法结束进程 {0} (PID {1})：{2}"),
                entry.ProcessName,
                entry.ProcessId,
                error);
            ErrorBar.Severity = InfoBarSeverity.Error;
            ErrorBar.IsOpen = true;
            return;
        }

        await RunScanAsync();   // 重新扫描，让占用列表反映最新状态
    }
}
