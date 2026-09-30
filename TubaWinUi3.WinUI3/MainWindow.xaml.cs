using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using Windows.UI;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3;

public sealed partial class MainWindow : Window
{
    private bool _syncingNavSelection;
    private bool _navFromSidebar;
    private bool _suppressSearch;
    private bool _searchDismissed;
    private readonly ObservableCollection<SearchResult> _searchResults = [];
    private readonly DispatcherQueueTimer _searchDebounceTimer;
    private Flyout? _downloadFlyout;
    private int _lastBadgeCount;
    private bool _refreshCategoriesInFlight;
    private bool _refreshCategoriesPending;

    /// <summary>用户明确要退出程序（托盘菜单「退出」）——关闭拦截必须放行，不再隐藏到托盘。</summary>
    private bool _exitRequested;

    /// <summary>当前正在执行的内置工具名称（入口页在 ExecuteAsync 前设置），供独立窗口标题使用。</summary>
    public static string? ActiveToolName { get; set; }

    public Image? GetBackgroundImage() => BackgroundImg;

    public void ShowUpdateBanner(Models.UpdateInfo update, bool autoDownload)
    {
        if (autoDownload)
        {
            UpdateBanner.ShowDownloading();
            var item = UpdateService.AutoDownloadUpdate(update);
            if (item is not null)
            {
                item.PropertyChanged += (s, e) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (s is not DownloadItem di) return;
                        switch (e.PropertyName)
                        {
                            case nameof(DownloadItem.State):
                                if (di.State == DownloadItemState.Completed)
                                    UpdateBanner.ShowDownloadComplete();
                                else if (di.State == DownloadItemState.Failed)
                                    UpdateBanner.ShowDownloadFailed(di.ErrorMessage ?? LocalizationService.L("Common_UnknownError", "未知错误"));
                                break;
                            case nameof(DownloadItem.Progress):
                                if (di.Progress is not null && di.Progress.TotalBytes > 0)
                                    UpdateBanner.ShowDownloadProgress(di.Progress.Percentage);
                                break;
                        }
                    });
                };
            }
            else
            {
                UpdateBanner.ShowUpdateAvailable(update);
            }
        }
        else
        {
            UpdateBanner.ShowUpdateAvailable(update);
        }
    }

    public void ShowUpdateAlreadyDownloaded(Models.UpdateInfo update)
    {
        UpdateBanner.ShowUpdateAvailable(update);
        UpdateBanner.ShowDownloadComplete();
    }

    private DispatcherTimer? _toolUpdateToastTimer;

    public void ShowToolUpdateToast(string toolName)
    {
        ToolUpdateToast.Title = LocalizationService.L("MainWindow_ToolUpdateDoneTitle", "工具更新完成");
        ToolUpdateToast.Message = string.Format(LocalizationService.L("MainWindow_ToolUpdateDoneMessage", "「{0}」已更新到最新版本"), toolName);
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
        ToolUpdateToast.IsOpen = true;
        StartToastAutoClose();
    }

    public void ShowToolUpdateProgressToast(string toolName)
    {
        ToolUpdateToast.Title = LocalizationService.L("MainWindow_ToolUpdateProgressTitle", "正在更新工具");
        ToolUpdateToast.Message = string.Format(LocalizationService.L("MainWindow_ToolUpdateProgressMessage", "「{0}」正在同步更新..."), toolName);
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
        ToolUpdateToast.IsOpen = true;
    }

    public void ShowToolUpdateFailedToast(string toolName, string error)
    {
        ToolUpdateToast.Title = LocalizationService.L("MainWindow_ToolUpdateFailedTitle", "工具更新失败");
        ToolUpdateToast.Message = string.Format(LocalizationService.L("MainWindow_ToolUpdateFailedMessage", "「{0}」更新失败：{1}"), toolName, error);
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
        ToolUpdateToast.IsOpen = true;
        StartToastAutoClose();
    }

    private void StartToastAutoClose()
    {
        _toolUpdateToastTimer?.Stop();
        _toolUpdateToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toolUpdateToastTimer.Tick += (s, e) =>
        {
            ToolUpdateToast.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _toolUpdateToastTimer.Start();
    }

#pragma warning disable CS0414
    private bool _initialized;
#pragma warning restore CS0414

    public MainWindow()
    {
        InitializeComponent();

        SearchListView.ItemsSource = _searchResults;

        // AutoSuggestBox 会先于实例处理 Escape（置 Handled），键盘导航必须用 handledEventsToo 注册
        SearchBox.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(SearchBox_KeyDown), handledEventsToo: true);

        _searchDebounceTimer = DispatcherQueue.CreateTimer();
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(100);
        _searchDebounceTimer.Tick += OnSearchDebounceTick;

        // 标题栏定制一律走 SafeTitleBar：部分 Windows 10 版本 AppWindow.TitleBar 为 null，
        // 构造函数里的空引用会以 STOWED_EXCEPTION_80004003 让进程闪退（见 SafeTitleBar 注释）
        SafeTitleBar.ApplyExtendedTall(this, AppTitleBar);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        try
        {
            if (File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 设置窗口图标失败（已忽略）: {ex.Message}");
        }

        ApplyTitleBarTheme(ElementTheme.Default);

        BackdropService.ApplyBackdrop(this);
        BackdropService.BackdropChanged += OnBackdropChanged;

        WindowSizeService.ApplySavedWindowSize(this);

        Closed += MainWindow_Closed;
        AppWindow.Changed += AppWindow_Changed;
        // 系统关闭入口（标题栏 ×、Alt+F4、系统菜单）走 AppWindow.Closing，可取消：
        // 「关闭时最小化到系统托盘」在这里把关闭改写成隐藏。Window.Closed 里的
        // Handled 分支保留给编程式 Close()（App.MainWindow?.Close()，后台工具页面在用），
        // 两条路径共用同一个判据，先到先得。
        AppWindow.Closing += MainWindow_Closing;
        NavFrame.Navigated += NavFrame_Navigated;
        NavView.ItemInvoked += NavView_ItemInvoked;

        if (RuntimeHelper.IsMsixPackaged)
        {
            NavView.MenuItems.Remove(CommunityNavItem);
        }

        SplashVersionText.Text = UpdateService.CurrentVersion.ToString();
        NavView.Loaded += NavView_Loaded;
        LocalizationService.LanguageChanged += OnLanguageChanged;
        _ = InitializeAfterSplashAsync();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.Loaded -= NavView_Loaded;
        ApplyLocalizedShellText();
    }

    private void OnLanguageChanged()
    {
        ApplyLocalizedShellText();
        // 工具元数据/标签随语言切换（Tools_<lang>.json 覆盖），必须先失效再重建导航与页面
        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateTagsCache();
        RefreshToolCategories();
        if (NavFrame.Content is ILocalizablePage page)
            page.ApplyLocalization();
    }

    /// <summary>刷新 shell 上由代码赋值、不随 Uid 自动更新的文本（窗口标题、按钮无障碍名、内建设置项）。</summary>
    private void ApplyLocalizedShellText()
    {
        var title = LocalizationService.L("App_Title", "图吧工具箱CE");
        Title = title;
        AppTitleBar.Title = title;

        AutomationProperties.SetName(AiQuickButton, LocalizationService.L("MainWindow_AiButton", "AI 助手"));
        AutomationProperties.SetName(NewbieTutorialButton, LocalizationService.L("MainWindow_NewbieTutorialButton", "新手教程"));
        AutomationProperties.SetName(DownloadQueueButton, LocalizationService.L("MainWindow_DownloadQueueButton", "下载队列"));

        // NavigationView 内建「设置」项文本由框架按系统语言渲染，必须手动覆写为目标语言
        if (NavView.SettingsItem is NavigationViewItem settingsItem)
            settingsItem.Content = LocalizationService.L("MainWindow_NavSettings", "设置");
    }

    private async Task InitializeAfterSplashAsync()
    {
        _initialized = true;

        NavigateToDefaultPage();

        // 仅应用本地自定义背景（品牌壁纸彩蛋已移除：不再自动检测主板品牌、下载或加载壁纸）
        ApplyBackground();

        _ = Task.Run(async () =>
        {
            try
            {
                _ = ToolCatalog.ToolsRoot;
                var categories = ToolCatalog.GetCategories().ToList();

                if (!ToolCatalog.IsCacheReady)
                {
                    await ToolCatalog.GetAllToolsAsync();
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    PopulateCategories(categories);
                    ApplyNavLayoutMode();

                    // 仅在「默认页 = 动态分类」时补一次导航：首次导航时分类菜单
                    // 尚未填充无法选中，需要填充后重新定位；默认页为静态项（如"全部
                    // 工具"）时首次导航已生效，跳过以免 HomePage 重复实例化
                    var defaultPage = AppSettings.Get("DefaultPage") ?? "all";
                    if (categories.Any(c => c.Equals(defaultPage, StringComparison.OrdinalIgnoreCase)))
                        NavigateToDefaultPage();

                    NavLayoutModeService.NavLayoutModeChanged += OnNavLayoutModeChanged;
                });
            }
            catch { }
        });

        DownloadQueueService.Initialize(DispatcherQueue);
        DownloadQueueService.QueueChanged += OnDownloadQueueChanged;
        ToolUpdateService.Initialize(DispatcherQueue);
        UpdateDownloadBadge();

        AppSettings.SettingChanged += OnBackgroundSettingChanged;

        await FadeOutSplashAsync();
    }

    private async Task FadeOutSplashAsync()
    {
        var storyboard = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(350);
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        storyboard.Children.Add(CreateSplashAnimation("Opacity", 1.0, 0.0, duration, easing));
        storyboard.Children.Add(CreateSplashAnimation("(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 1.0, 0.95, duration, easing));
        storyboard.Children.Add(CreateSplashAnimation("(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 1.0, 0.95, duration, easing));

        var tcs = new TaskCompletionSource();
        storyboard.Completed += (_, _) => tcs.TrySetResult();
        storyboard.Begin();
        await tcs.Task;

        SplashOverlay.Visibility = Visibility.Collapsed;
    }

    private DoubleAnimation CreateSplashAnimation(string targetProperty, double from, double to, TimeSpan duration, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, SplashOverlay);
        Storyboard.SetTargetProperty(animation, targetProperty);
        return animation;
    }

    private void OnBackgroundSettingChanged(string key)
    {
        if (key == "BackgroundImagePath" || key == "BackgroundOpacity")
            DispatcherQueue.TryEnqueue(ApplyBackground);
    }

    private void ApplyBackground()
    {
        var bmp = BackgroundService.LoadBackgroundImage();
        if (bmp is not null)
        {
            BackgroundImg.Source = bmp;
            BackgroundImg.Opacity = BackgroundService.GetBackgroundOpacity();
            BackgroundImg.Visibility = Visibility.Visible;
        }
        else
        {
            BackgroundImg.Source = null;
            BackgroundImg.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSplashStatus(string text)
    {
        DispatcherQueue.TryEnqueue(() => SplashStatusText.Text = text);
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        AppTitleBar.IsBackButtonVisible = NavFrame.CanGoBack;

        if (_navFromSidebar)
        {
            _navFromSidebar = false;
            return;
        }

        _syncingNavSelection = true;

        if (e.SourcePageType == typeof(SettingsPage))
        {
            NavView.SelectedItem = NavView.SettingsItem;
        }
        else
        {
            var targetTag = ResolvePageTag(e.SourcePageType, e.Parameter);
            if (targetTag is not null)
            {
                foreach (var item in NavView.MenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag is string t && t == targetTag)
                    {
                        NavView.SelectedItem = navItem;
                        break;
                    }
                }
            }
        }

        _syncingNavSelection = false;
    }

    private static string? ResolvePageTag(Type pageType, object? parameter)
    {
        if (pageType == typeof(SettingsPage)) return "settings";
        if (pageType == typeof(FavoritesPage)) return "favorites";
        if (pageType == typeof(HardwarePage)) return "hardware";
        if (pageType == typeof(BuiltinToolsPage)) return "builtin";
        if (pageType == typeof(CommunityToolsPage)) return "community";

        if (pageType == typeof(HomePage))
        {
            if (parameter is string category) return category;
            return "all";
        }
        return null;
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPointProperties props = e.GetCurrentPoint(null).Properties;
        var frame = NavFrame;

        if (props.IsXButton1Pressed)
        {
            if (frame.CanGoBack)
            {
                frame.GoBack();
                e.Handled = true;
            }
        }
        else if (props.IsXButton2Pressed)
        {
            if (frame.CanGoForward)
            {
                frame.GoForward();
                e.Handled = true;
            }
        }
    }

    /// <summary>这次关闭是否应改写成「隐藏到托盘」（判据集中在 CloseToTrayService，单测覆盖）。</summary>
    private bool ShouldHideToTray()
        => CloseToTrayService.ShouldHideOnClose(
            CloseToTrayService.IsEnabled,
            App.IsLiteMode,
            _exitRequested || App.IsExiting,
            CloseToTrayService.IsSessionEnding);

    /// <summary>系统关闭入口（标题栏 ×、Alt+F4、系统菜单）：取消关闭，改为隐藏到系统托盘。</summary>
    private void MainWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!ShouldHideToTray()) return;

        // 隐藏失败就不要取消关闭：宁可正常退出，也不能让用户点了 × 却毫无反应
        args.Cancel = HideToTray();
    }

    /// <summary>
    /// 隐藏主窗口并常驻托盘——进程继续在后台运行，硬件信息等已加载的数据全部保留。
    /// 返回 false 表示隐藏失败（调用方应放行真正的关闭）。
    /// </summary>
    private bool HideToTray()
    {
        try
        {
            AppWindow.Hide();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 隐藏窗口失败，改为正常关闭: {ex.Message}");
            return false;
        }

        // 窗口位置/大小仍在隐藏前落盘：之后从托盘退出时窗口已经不可见，读的是同一份尺寸
        try { WindowSizeService.SaveWindowSize(this); } catch { }

        // 托盘图标/首次气泡在这里创建：失败不能让异常从关闭回调里逃出去
        // （宁可退回「正常关闭」，也不能让用户点了 × 既没隐藏也没退出）
        try
        {
            CloseToTrayService.OnHiddenToTray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 托盘常驻设置失败，改为正常关闭: {ex.Message}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 从托盘恢复主窗口（显示 + 取消最小化 + 激活）。
    /// 返回窗口是否真的显示出来了——失败时调用方不能收起托盘图标，
    /// 否则用户会同时失去窗口和托盘入口。
    /// </summary>
    public bool RestoreFromTray()
    {
        try
        {
            if (!AppWindow.IsVisible)
                AppWindow.Show();

            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
                presenter.Restore();

            Activate();
            return AppWindow.IsVisible;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 从托盘恢复窗口失败: {ex.Message}");
            try { return AppWindow.IsVisible; } catch { return false; }
        }
    }

    /// <summary>退出程序：放行关闭拦截，让 <see cref="MainWindow_Closed"/> 跑完整清理。</summary>
    public void CloseForExit()
    {
        _exitRequested = true;
        Close();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (ShouldHideToTray() && HideToTray())
        {
            // 编程式 Close()（后台工具页面的 App.MainWindow?.Close()）下的兜底：拦下这次关闭只做隐藏。
            // 系统关闭入口已在 MainWindow_Closing 取消，不会走到这里。
            args.Handled = true;
            return;
        }

        // Gracefully stop EnergyStar throttling so any throttled processes recover
        // their normal scheduling priority before the app exits. If the user has
        // enabled the scheduled-task auto-start, the next logon will re-enable it.
        // 必须放在「隐藏到托盘」分支之后：隐藏后程序还在后台跑（后台节流本身就是常驻功能），
        // 这时候解除节流等于把用户开着的省电功能关掉。
        try { EnergyStarService.Shutdown(); } catch { }

        BackdropService.BackdropChanged -= OnBackdropChanged;
        AppWindow.Changed -= AppWindow_Changed;
        AppWindow.Closing -= MainWindow_Closing;
        try { WindowSizeService.SaveWindowSize(this); } catch { }
        DownloadQueueService.QueueChanged -= OnDownloadQueueChanged;
        AppSettings.SettingChanged -= OnBackgroundSettingChanged;
        NavLayoutModeService.NavLayoutModeChanged -= OnNavLayoutModeChanged;
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        // AppSettings 落盘是去抖的（500ms 合并），退出前同步刷一次避免丢最后变更
        AppSettings.Flush();

        // 遥测：尽力把队列中剩余数据发出去（有界等待），随后停止采集
        try { Services.Telemetry.TelemetryService.Shutdown(); } catch { }

        // 硬件监控句柄与 FPS 的 ETW 会话必须显式释放：内核 ETW 会话不会随进程终止自动回收，
        // 残留会让下次启动的 FPS 采集失效；轮询定时器/自动覆盖层/未落盘记录一并收尾。
        try { LiteMonitorService.Instance.Dispose(); } catch { }
        try { GameOverlayAutoService.Instance.Stop(); } catch { }
        // 托盘图标必须显式移除：进程退出后残留的图标要等鼠标划过去才消失
        try { TrayIconService.Dispose(); } catch { }

        // 通知退出兜底看门狗「清理已跑完」：它据此决定何时可以硬退，
        // 避免 3 秒硬超时砍在清理中途（FPS 的 ETW 会话会因此泄漏）
        App.NotifyCleanupFinished();
    }

    private void OnDownloadQueueChanged()
    {
        DispatcherQueue.TryEnqueue(UpdateDownloadBadge);
    }

    private void UpdateDownloadBadge()
    {
        var count = DownloadQueueService.PendingCount;
        if (count > 0)
        {
            DownloadQueueBadge.Value = count > 99 ? 99 : count;
            DownloadQueueBadge.Visibility = Visibility.Visible;
        }
        else
        {
            DownloadQueueBadge.Visibility = Visibility.Collapsed;
        }

        if (count > _lastBadgeCount)
        {
            PlayDownloadPulseAnimation();
        }
        _lastBadgeCount = count;
    }

    private void PlayDownloadPulseAnimation()
    {
        var btn = DownloadQueueButton;
        btn.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        btn.RenderTransform = new ScaleTransform();

        var scaleX = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTargetProperty(scaleX, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        Storyboard.SetTarget(scaleX, btn);
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(150), Value = 1.3, EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(400), Value = 1.0, EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseInOut } });

        var scaleY = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTargetProperty(scaleY, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        Storyboard.SetTarget(scaleY, btn);
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(150), Value = 1.3, EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } });
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(400), Value = 1.0, EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseInOut } });

        var sb = new Storyboard();
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);
        sb.Begin();
    }

    private void DownloadQueueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadFlyout is null)
        {
            _downloadFlyout = new Flyout
            {
                Content = new DownloadQueueFlyout(),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
            };
        }
        _downloadFlyout.ShowAt(DownloadQueueButton);
    }

    private void AiQuickButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new Flyout
        {
            Content = new AiQuickAskFlyout(),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };
        // FlyoutPresenter 默认 MaxWidth=456（FlyoutThemeMaxWidth），会把内容钳在 456px；
        // 覆盖 presenter 宽度/高度限制，让 850px 面板完整显示。
        flyout.FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
        {
            Setters =
            {
                new Setter(FlyoutPresenter.MaxWidthProperty, 880.0),
                new Setter(FlyoutPresenter.MaxHeightProperty, 810.0)
            }
        };
        flyout.ShowAt(AiQuickButton);
    }

    private void NewbieTutorialButton_Click(object sender, RoutedEventArgs e)
    {
        NewbieTutorialWindow.Show();
    }

    private void OnBackdropChanged()
    {
        DispatcherQueue.TryEnqueue(() => BackdropService.ApplyBackdrop(this));
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // 窗口被重新显示（托盘双击、页面里的 App.MainWindow?.Activate() 等任何路径）：
        // 收起「已最小化到托盘」标记，否则会留下一个提示"已最小化到系统托盘"的过期图标
        if (args.DidVisibilityChange && sender.IsVisible)
        {
            TrayIconService.OnMainWindowRestored();
            // 用户又能看到窗口了 = 上次注销/关机没有真的发生（被取消），恢复正常关闭行为
            CloseToTrayService.ResetSessionEnding();
        }

        if (!args.DidSizeChange) return;
        var size = sender.Size;
        var minWidth = 800;
        var minHeight = 600;
        var needsResize = false;
        var newW = size.Width;
        var newH = size.Height;

        if (size.Width < minWidth)
        {
            newW = minWidth;
            needsResize = true;
        }
        if (size.Height < minHeight)
        {
            newH = minHeight;
            needsResize = true;
        }

        if (needsResize)
        {
            sender.Resize(new Windows.Graphics.SizeInt32(newW, newH));
        }
    }

    public void ApplyTitleBarTheme(ElementTheme theme)
    {
        var isDark = theme == ElementTheme.Dark ||
                     (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        TitleBarPalette.Apply(SafeTitleBar.Get(this), isDark);
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        var frame = NavFrame;
        if (frame.CanGoBack)
            frame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingNavSelection) return;

        // 设置入口改由 ItemInvoked 统一处理：布局切换（Top/侧边栏）后 NavigationView
        // 内部选中状态可能残留（设置项与菜单项同时选中），点击设置不再触发
        // SelectionChanged；而 ItemInvoked 与选中状态无关、必然触发。
        if (args.IsSettingsSelected) return;

        _navFromSidebar = true;

        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "all":
                    NavFrame.Navigate(typeof(HomePage), null);
                    break;
                case "favorites":
                    NavFrame.Navigate(typeof(FavoritesPage));
                    break;
                case "hardware":
                    NavFrame.Navigate(typeof(HardwarePage));
                    break;
                case "builtin":
                    NavFrame.Navigate(typeof(BuiltinToolsPage));
                    break;
                case "community":
                    if (RuntimeHelper.IsMsixPackaged) break;
                    NavFrame.Navigate(typeof(CommunityToolsPage));
                    break;

                case "benchmark":
                    _navFromSidebar = false;
                    _ = ExecuteBenchmarkToolAsync();
                    break;
                case string category:
                    NavFrame.Navigate(typeof(HomePage), category);
                    break;
            }
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (!args.IsSettingsInvoked) return;
        // 设置项点击与选中状态无关，保证布局切换后仍能打开设置
        _navFromSidebar = true;
        NavFrame.Navigate(typeof(SettingsPage));
    }

    private void NavigateToDefaultPage()
    {
        var defaultPage = AppSettings.Get("DefaultPage") ?? "all";

        NavigationViewItem? targetItem = null;

        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag is string tag && tag == defaultPage)
            {
                targetItem = navItem;
                break;
            }
        }

        if (targetItem is not null)
        {
            NavView.SelectedItem = targetItem;
        }
        else
        {
            NavFrame.Navigate(typeof(HomePage), null);
        }
    }

    private int _dynamicMenuItemCount;

    private void PopulateCategories(IReadOnlyList<string> categories)
    {
        while (_dynamicMenuItemCount > 0)
        {
            NavView.MenuItems.RemoveAt(NavView.MenuItems.Count - 1);
            _dynamicMenuItemCount--;
        }

        var otherCategory = categories.FirstOrDefault(c => c.Contains("其他"));
        var restCategories = categories.Where(c => !c.Contains("其他"));

        foreach (var category in restCategories)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = LocalizationService.GetCategoryDisplayName(category),
                Tag = category,
                Icon = new FontIcon { Glyph = GetCategoryGlyphStatic(category) }
            });
            _dynamicMenuItemCount++;
        }

        if (otherCategory != null)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = LocalizationService.GetCategoryDisplayName(otherCategory),
                Tag = otherCategory,
                Icon = new FontIcon { Glyph = GetCategoryGlyphStatic(otherCategory) }
            });
            _dynamicMenuItemCount++;
        }
    }

    public static string GetCategoryGlyphStatic(string category)
    {
        var customGlyph = AppSettings.Get($"CategoryGlyph_{category}");
        if (!string.IsNullOrWhiteSpace(customGlyph))
            return customGlyph;

        if (category.Contains("处理器", StringComparison.CurrentCultureIgnoreCase))
            return "\uEEA1";
        if (category.Contains("显卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uF211";
        if (category.Contains("显示器", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7F4";
        if (category.Contains("硬盘", StringComparison.CurrentCultureIgnoreCase))
            return "\uEDA2";
        if (category.Contains("内存", StringComparison.CurrentCultureIgnoreCase))
            return "\uEEA0";
        if (category.Contains("外设", StringComparison.CurrentCultureIgnoreCase))
            return "\uE962";
        if (category.Contains("游戏", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7FC";
        if (category.Contains("声卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7F5";
        if (category.Contains("网卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uEDA3";
        if (category.Contains("烤鸡", StringComparison.CurrentCultureIgnoreCase))
            return "\uECAD";
        if (category.Contains("综合", StringComparison.CurrentCultureIgnoreCase))
            return "\uEC4E";
        if (category.Contains("其他", StringComparison.CurrentCultureIgnoreCase))
            return "\uE712";

        return "\uE8B7";
    }

    public void RefreshToolCategories()
    {
        if (_refreshCategoriesInFlight)
        {
            _refreshCategoriesPending = true;
            return;
        }
        _refreshCategoriesInFlight = true;
        _ = RefreshToolCategoriesCoreAsync();
    }

    private async Task RefreshToolCategoriesCoreAsync()
    {
        try
        {
            var categories = await Task.Run(() => ToolCatalog.GetCategories().ToList());

            if (!DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    PopulateCategories(categories);
                }
                finally
                {
                    _refreshCategoriesInFlight = false;
                    if (_refreshCategoriesPending)
                    {
                        _refreshCategoriesPending = false;
                        RefreshToolCategories();
                    }
                }
            }))
            {
                _refreshCategoriesInFlight = false;
                _refreshCategoriesPending = false;
            }
        }
        catch
        {
            _refreshCategoriesInFlight = false;
            _refreshCategoriesPending = false;
        }
    }

    private void OnNavLayoutModeChanged(string mode)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyNavLayoutMode();
            // PaneDisplayMode 切换会重建侧边栏容器，NavigationView 的选中内部状态
            // 会残留（设置项与菜单项可能同时处于选中态），导致之后点击设置不再触发
            // SelectionChanged。先清空再按默认页重选，重建干净状态。
            NavView.SelectedItem = null;
            if (NavView.SettingsItem is NavigationViewItem settingsItem)
                settingsItem.IsSelected = false;
            NavView.UpdateLayout();
            NavigateToDefaultPage();
        });
    }

    private void ApplyNavLayoutMode()
    {
        var isTabMode = NavLayoutModeService.IsTabMode();
        NavView.PaneDisplayMode = isTabMode
            ? NavigationViewPaneDisplayMode.Top
            : NavigationViewPaneDisplayMode.Auto;
        AppTitleBar.IsPaneToggleButtonVisible = !isTabMode;
    }

    private async Task ExecuteBenchmarkToolAsync()
    {
        NavigateToBenchmark();
    }

    public void NavigateToBenchmark()
    {
        NavFrame.Navigate(typeof(PerformanceBenchmarkPage));
    }

    public void NavigateToToolPage(Type pageType, object? parameter = null)
    {
        // 设置"独立窗口"模式，或处于 AI 助手强制独立窗口作用域时，内置工具在新窗口的 Frame 中打开
        if (BuiltinToolWindow.ForceWindowMode || AppSettings.GetBool("BuiltinToolsOpenInWindow", false))
        {
            var title = parameter is ToolContentPageParam p
                ? p.Title
                : (ActiveToolName ?? LocalizationService.L("MainWindow_BuiltinToolFallback", "内置工具"));
            BuiltinToolWindow.Show(pageType, parameter, title);
            return;
        }
        NavFrame.Navigate(pageType, parameter, new DrillInNavigationTransitionInfo());
    }

    public void NavigateToSettings(string? highlightSettingKey = null)
    {
        NavFrame.Navigate(typeof(SettingsPage),
            highlightSettingKey is null
                ? null
                : new SearchNavigationTarget { HighlightSettingKey = highlightSettingKey });
        SyncNavSelection("settings");
    }

    public void NavigateBack()
    {
        // 若前台是独立工具窗口，返回/关闭操作作用于该窗口，避免误操作主窗口导航
        if (BuiltinToolWindow.ActiveWindow is { } toolWindow)
        {
            toolWindow.GoBackOrClose();
            return;
        }
        if (NavFrame.CanGoBack)
            NavFrame.GoBack();
    }

    public bool CanNavigateBack()
    {
        return NavFrame.CanGoBack;
    }

    private void PopulateSearchSuggestions()
    {
        var items = UnifiedSearchService.GetQuickPanelItems();
        _searchResults.Clear();
        foreach (var item in items)
            _searchResults.Add(item);
    }

    private void ShowSearchPopup()
    {
        if (_searchDismissed) return;
        SearchPopup.IsOpen = _searchResults.Count > 0;
    }

    private void HideSearchPopup()
    {
        SearchPopup.IsOpen = false;
    }

    private void SearchPopup_GettingFocus(object sender, GettingFocusEventArgs e)
    {
        e.TryCancel();
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressSearch || _searchDismissed) return;
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
            PopulateSearchSuggestions();
        ShowSearchPopup();
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!SearchBox.FocusState.HasFlag(FocusState.Programmatic))
                HideSearchPopup();
        });
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_suppressSearch) return;

        _searchDismissed = false;
        var query = SearchBox.Text.Trim();

        if (query.Length == 0)
        {
            _searchDebounceTimer.Stop();
            PopulateSearchSuggestions();
            ShowSearchPopup();
            return;
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        var query = SearchBox.Text.Trim();
        if (query.Length == 0) return;

        _ = SearchInBackgroundAsync(query);
    }

    private async Task SearchInBackgroundAsync(string query)
    {
        try
        {
            var results = await Task.Run(() => UnifiedSearchService.Search(query));
            _searchResults.Clear();
            foreach (var r in results)
                _searchResults.Add(r);
            SearchPopup.IsOpen = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Search] {ex}");
        }
    }

    private void SearchListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchResult result)
        {
            HideSearchPopup();
            HandleSearchResult(result);
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var idx = SearchListView.SelectedIndex;
        SearchResult? result = idx >= 0 && idx < _searchResults.Count
            ? _searchResults[idx]
            : _searchResults.FirstOrDefault();

        if (result is null) return;

        HideSearchPopup();
        HandleSearchResult(result);
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            SearchListView.SelectedIndex = -1;
            HideSearchPopup();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Down)
        {
            if (SearchListView.Items.Count > 0)
            {
                var next = SearchListView.SelectedIndex < 0
                    ? 0
                    : Math.Min(SearchListView.SelectedIndex + 1, SearchListView.Items.Count - 1);
                SearchListView.SelectedIndex = next;
                SearchListView.ScrollIntoView(SearchListView.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            if (SearchListView.Items.Count > 0)
            {
                var prev = SearchListView.SelectedIndex <= 0
                    ? 0
                    : SearchListView.SelectedIndex - 1;
                SearchListView.SelectedIndex = prev;
                SearchListView.ScrollIntoView(SearchListView.SelectedItem);
            }
            e.Handled = true;
        }
    }

    private void SearchListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (SearchListView.SelectedItem is SearchResult result)
            {
                _suppressSearch = true;
                SearchBox.Text = string.Empty;
                _suppressSearch = false;
                HideSearchPopup();
                HandleSearchResult(result);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideSearchPopup();
            e.Handled = true;
        }
    }

    private void HandleSearchResult(SearchResult result)
    {
        var frame = NavFrame;

        switch (result.Kind)
        {
            case SearchItemKind.ExternalTool:
            case SearchItemKind.CustomTool:
                NavigateToTool(result.MatchKey);
                break;
            case SearchItemKind.BuiltinTool:
                frame.Navigate(typeof(BuiltinToolsPage),
                    new SearchNavigationTarget { HighlightBuiltinId = result.MatchKey });
                SyncNavSelection("builtin");
                break;

            case SearchItemKind.Setting:
                frame.Navigate(typeof(SettingsPage),
                    new SearchNavigationTarget { HighlightSettingKey = result.MatchKey });
                SyncNavSelection("settings");
                break;
            case SearchItemKind.QuickAction:
                HandleQuickAction(result.MatchKey);
                break;
        }
    }

    private void SyncNavSelection(string tag)
    {
        _syncingNavSelection = true;
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag is string t && t == tag)
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }
        _syncingNavSelection = false;
    }

    private void NavigateToTool(string toolPath)
    {
        try
        {
            var tools = ToolCatalog.GetAllToolsCached();
            var tool = tools.FirstOrDefault(t => t.Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
            {
                NavFrame.Navigate(typeof(HomePage),
                    new SearchNavigationTarget { HighlightToolPath = toolPath });

                if (!string.IsNullOrEmpty(tool.Category))
                    SyncNavSelection(tool.Category);
            }
        }
        catch { }
    }

    private void HandleQuickAction(string action)
    {
        if (!action.StartsWith("navigate:")) return;
        var target = action["navigate:".Length..];
        var frame = NavFrame;

        switch (target)
        {
            case "hardware":
                frame.Navigate(typeof(HardwarePage));
                SyncNavSelection("hardware");
                break;
            case "favorites":
                frame.Navigate(typeof(FavoritesPage));
                SyncNavSelection("favorites");
                break;
            case "builtin":
                frame.Navigate(typeof(BuiltinToolsPage));
                SyncNavSelection("builtin");
                break;
            case "benchmark":
                _navFromSidebar = false;
                _ = ExecuteBenchmarkToolAsync();
                break;

            case "settings":
                frame.Navigate(typeof(SettingsPage));
                break;
        }
    }
}
