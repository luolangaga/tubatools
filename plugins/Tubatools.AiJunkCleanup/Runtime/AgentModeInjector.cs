using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Tubatools.AiJunkCleanup.Models;
using Tubatools.AiJunkCleanup.Services;
using Tubatools.AiJunkCleanup.UI;
using TubaWinUi3.PluginSdk;

namespace Tubatools.AiJunkCleanup.Runtime;

/// <summary>
/// 在 AI 助手页面运行时注入「垃圾清理」模式。没有改宿主代码：只往现有 ComboBox 里增加一项，
/// 并在用户输入前把模式说明拼进消息。清单通过只读反射读取页面当前会话的公开消息属性。
/// </summary>
internal sealed class AgentModeInjector : IDisposable
{
    internal const string ModeTag = "Tubatools.AiJunkCleanup:junk-cleanup";
    internal const string ModeName = "垃圾清理";
    private const string Marker = "〔垃圾清理模式〕";
    private const string ScanResultMarker = "〔插件浅层扫描结果";
    private const string TargetLabel = "我的清理目标：";
    private const string AssistantPageType = "TubaWinUi3.Pages.AiAssistantPage";
    private const int MaxRetries = 5;

    private readonly IPluginHostAccess _access;
    private readonly Func<string, string?, Task> _saveSetting;
    private readonly Func<JunkPlan?> _lastPlan;
    private readonly Action<JunkPlan, string> _rememberPlan;
    private readonly Action<JunkCleanupResult> _rememberResult;
    private readonly Func<JunkScanDepth, IProgress<ShallowScanProgress>, Task<ShallowScanResult?>> _scanShallow;
    private readonly string? _dataDirectory;
    private readonly HashSet<string> _handled;
    private readonly HashSet<string> _blockedCommands = new(StringComparer.Ordinal);
    private readonly HashSet<Frame> _extraFrames = [];
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueTimer _attachRetry;
    private Frame? _frame;
    private Page? _page;
    private Panel? _transcript;
    private ComboBox? _modeBox;
    private TextBox? _promptBox;
    private ComboBox? _depthBox;
    private StackPanel? _tools;
    private Button? _lastPlanButton;
    private Button? _tutorialButton;
    private bool _tutorialShown;
    private bool _tutorialOpen;
    private Button? _sendButton;
    private Button? _customSend;
    private ShallowScanOverlay? _scanOverlay;
    private long _sendVisibilityToken;
    private long _sendEnabledToken;
    private bool _sendControls;
    private bool _previewHooked;
    private KeyEventHandler? _previewHandler;
    private bool _scanSending;
    private bool _syncingSend;
    private bool _suppress;
    private bool _disposed;
    private bool _dialogOpen;
    private JunkPlan? _pendingPlan;
    private JunkScanDepth _pendingDepth;
    private JunkScanDepth _depth;
    private bool _pageHooked;
    private bool _pageHandlers;
    private bool _guardPrimed;
    private bool _noticeOpen;
    private string? _sessionId;

    internal AgentModeInjector(IPluginHostAccess access, JunkScanDepth depth, bool tutorialShown, IEnumerable<string> handled,
        Func<string, string?, Task> saveSetting, Func<JunkPlan?> lastPlan, Action<JunkPlan, string> rememberPlan,
        Action<JunkCleanupResult> rememberResult,
        Func<JunkScanDepth, IProgress<ShallowScanProgress>, Task<ShallowScanResult?>> scanShallow,
        string? dataDirectory)
    {
        _access = access;
        _depth = depth;
        _tutorialShown = tutorialShown;
        _handled = new HashSet<string>(handled, StringComparer.Ordinal);
        _saveSetting = saveSetting;
        _lastPlan = lastPlan;
        _rememberPlan = rememberPlan;
        _rememberResult = rememberResult;
        _scanShallow = scanShallow;
        _dataDirectory = dataDirectory;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1200);
        _timer.Tick += (_, _) => Poll();
        _attachRetry = _dispatcher.CreateTimer();
        _attachRetry.Interval = TimeSpan.FromSeconds(1);
        _attachRetry.Tick += (_, _) => TryAttachFrame();
    }

    internal bool IsAttached => _page is not null && _modeBox is not null;

    internal void Attach()
    {
        if (_disposed) return;
        TryAttachFrame();
        if (_frame is null) _attachRetry.Start();
        // 独立工具窗口模式下主 NavFrame 里没有 AI 页面；定时器必须常驻，
        // 否则永远不会发现 BuiltinToolWindow 里的 AI 助手。
        _timer.Start();
    }

    internal bool SelectMode()
    {
        if (_disposed || _modeBox is null) return false;
        var item = EnsureModeItem();
        if (item is null) return false;
        _modeBox.SelectedItem = item;
        EnsureTemplate();
        return true;
    }

    internal async Task ShowLastPlanAsync()
    {
        if (_lastPlan() is { } plan)
            await ShowPlanAsync(plan, _depth);
    }

    private async Task ShowTutorialDelayedAsync()
    {
        await Task.Delay(450);
        if (_disposed || !IsJunkSelected) return;
        await ShowTutorialAsync(force: false);
    }

    /// <summary>插件首次加载时调用：等主窗口 XamlRoot 就绪后弹出教程，最多等待约 6 秒。</summary>
    internal async Task ShowTutorialAtStartupAsync()
    {
        if (_disposed || _tutorialShown || _tutorialOpen) return;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (_disposed || _tutorialShown) return;
            var root = GetTutorialRoot();
            if (root is not null)
            {
                await ShowTutorialAsync(force: false, root);
                return;
            }
            await Task.Delay(600);
        }
    }

    private XamlRoot? GetTutorialRoot()
    {
        if (_page?.XamlRoot is { } pageRoot) return pageRoot;
        if ((_access.RootVisual as FrameworkElement)?.XamlRoot is { } root) return root;
        if ((_access.MainWindow as Window)?.Content is FrameworkElement main) return main.XamlRoot;
        return null;
    }

    private async Task ShowTutorialAsync(bool force, XamlRoot? root = null)
    {
        if (_disposed || _tutorialOpen) return;
        if (!force && _tutorialShown) return;
        root ??= GetTutorialRoot();
        if (root is null) return;

        _tutorialOpen = true;
        var shown = false;
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var dialog = new TutorialDialog(root);
                    await dialog.ShowAsync();
                    shown = true;
                    break;
                }
                catch (InvalidOperationException)
                {
                    // 宿主可能正开着别的 ContentDialog；稍后重试。
                    if (attempt < 2) await Task.Delay(1200);
                }
            }
        }
        finally { _tutorialOpen = false; }

        if (shown && !_tutorialShown)
        {
            _tutorialShown = true;
            _ = _saveSetting("startupTutorialShown", "true");
        }
    }

    private void TryAttachFrame()
    {
        if (_disposed) return;
        if (_frame is null && _access.NavigationFrame is Frame frame)
        {
            _frame = frame;
            _frame.Navigated += OnNavigated;
            _attachRetry.Stop();
        }
        if (_frame?.Content is Page current && current.GetType().FullName == AssistantPageType)
            TryAttach(current);
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        if (_disposed) return;
        if (e.Content is Page page && page.GetType().FullName == AssistantPageType)
            TryAttach(page);
        else if (ReferenceEquals(sender, _frame))
            DetachPage();
    }

    private void TryAttach(Page page)
    {
        if (_disposed) return;
        if (ReferenceEquals(_page, page) && _modeBox is not null) return;
        DetachPage();
        _page = page;
        if (page.IsLoaded) AttachNow();
        else if (!_pageHooked)
        {
            _pageHooked = true;
            page.Loaded += OnPageLoaded;
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_disposed) return;
        AttachNow();
    }

    private void AttachNow()
    {
        if (_disposed || _page is null) return;
        _modeBox ??= Find<ComboBox>(_page, "ModeBox", "AgentMode");
        _promptBox ??= Find<TextBox>(_page, "PromptBox", "AgentPrompt");
        _transcript ??= _page.FindName("Transcript") as Panel;
        _sendButton ??= Find<Button>(_page, "SendButton", "AgentSend");
        if (_modeBox is null || _promptBox is null) return;
        EnsureModeItem();
        EnsureDepthControls();
        EnsureSendControls();
        if (!_pageHandlers)
        {
            _pageHandlers = true;
            _modeBox.SelectionChanged += OnModeSelectionChanged;
            _promptBox.TextChanged += OnPromptTextChanged;
            _page.SizeChanged += OnPageSizeChanged;
        }
        _timer.Start();
        _guardPrimed = false;
        if (IsJunkSelected) EnsureTemplate();
        SyncSendButtons();
    }

    private void EnsureDepthControls()
    {
        if (_modeBox is null) return;
        if (_modeBox.Parent is not Grid grid) return;
        if (_tools is not null)
        {
            if (_tools.Parent is Panel oldParent && !ReferenceEquals(oldParent, grid)) oldParent.Children.Remove(_tools);
            if (!grid.Children.Contains(_tools)) grid.Children.Add(_tools);
            return;
        }

        _depthBox = new ComboBox
        {
            Style = _modeBox.Style,
            MinWidth = 78,
            MaxWidth = 96,
            MinHeight = 26,
            FontSize = 12,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _depthBox.Items.Add("快速");
        _depthBox.Items.Add("标准");
        _depthBox.Items.Add("深度");
        _depthBox.SelectedIndex = (int)_depth;
        AutomationProperties.SetAutomationId(_depthBox, "AiJunkCleanupDepth");
        AutomationProperties.SetName(_depthBox, "垃圾清理扫描深度");
        ToolTipService.SetToolTip(_depthBox, "扫描深度：" + JunkScanDepths.Tip(_depth));
        _depthBox.SelectionChanged += OnDepthChanged;

        Style? iconStyle = null;
        if (_page is not null && _page.Resources.TryGetValue("AgentIconButton", out var styleValue) && styleValue is Style foundStyle)
            iconStyle = foundStyle;
        _lastPlanButton = new Button
        {
            Style = iconStyle,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };
        _lastPlanButton.Content = new FontIcon { Glyph = "\uE8A5", FontSize = 12 };
        AutomationProperties.SetAutomationId(_lastPlanButton, "AiJunkCleanupLastPlan");
        AutomationProperties.SetName(_lastPlanButton, "查看上次清理清单");
        ToolTipService.SetToolTip(_lastPlanButton, "查看上次清理清单");
        _lastPlanButton.Click += (_, _) => _ = ShowLastPlanAsync();

        _tutorialButton = new Button
        {
            Style = iconStyle,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };
        _tutorialButton.Content = new FontIcon { Glyph = "\uE897", FontSize = 12 };
        AutomationProperties.SetAutomationId(_tutorialButton, "AiJunkCleanupTutorial");
        AutomationProperties.SetName(_tutorialButton, "查看垃圾清理教程");
        ToolTipService.SetToolTip(_tutorialButton, "查看垃圾清理教程");
        _tutorialButton.Click += (_, _) => _ = ShowTutorialAsync(force: true);

        _tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _tools.Children.Add(_depthBox);
        _tools.Children.Add(_lastPlanButton);
        _tools.Children.Add(_tutorialButton);
        Grid.SetColumn(_tools, 3);
        grid.Children.Add(_tools);
    }

    private ComboBoxItem? EnsureModeItem()
    {
        if (_modeBox is null) return null;
        foreach (var item in _modeBox.Items)
            if (item is ComboBoxItem { Tag: string tag } && tag == ModeTag) return (ComboBoxItem)item;
        var created = new ComboBoxItem { Content = ModeName, Tag = ModeTag };
        ToolTipService.SetToolTip(created, "AI 只读扫描并生成清单；由你逐项确认后才会删除。");
        AutomationProperties.SetAutomationId(created, "AiJunkCleanupMode");
        _modeBox.Items.Add(created);
        return created;
    }

    private bool IsJunkSelected => _modeBox?.SelectedItem is ComboBoxItem { Tag: string tag } && tag == ModeTag;

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_tools is null) return;
        _tools.Visibility = IsJunkSelected && e.NewSize.Width >= 720 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _disposed) return;
        var selected = IsJunkSelected;
        if (_tools is not null)
            _tools.Visibility = selected && (_page?.ActualWidth ?? 0) >= 720 ? Visibility.Visible : Visibility.Collapsed;
        if (_lastPlanButton is not null)
            _lastPlanButton.Visibility = selected && _lastPlan() is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_tutorialButton is not null)
            _tutorialButton.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        if (selected)
        {
            EnsureTemplate();
            SyncSendButtons();
            // 每次进入清理模式都重新给历史命令做一次基线，避免把普通对话里的旧命令当成违规。
            _guardPrimed = false;
            if (!_tutorialShown) _ = ShowTutorialDelayedAsync();
        }
        else
        {
            RemoveTemplate();
            SyncSendButtons();
        }
    }

    /// <summary>
    /// 把宿主的发送按钮换成插件自己的按钮：按下先扫描，扫描完成后把结果拼进消息再调用宿主发送。
    /// 行内回车由页面 PreviewKeyDown 拦截（隧道事件先到页面，再到输入框）。
    /// </summary>
    private void EnsureSendControls()
    {
        if (_disposed || _page is null || _sendButton is null) return;

        if (_customSend is null)
        {
            _customSend = new Button
            {
                Style = _sendButton.Style,
                Width = _sendButton.Width,
                Height = _sendButton.Height,
                MinWidth = _sendButton.MinWidth,
                MinHeight = _sendButton.MinHeight,
                Padding = _sendButton.Padding,
                Background = _sendButton.Background,
                Foreground = _sendButton.Foreground,
                BorderBrush = _sendButton.BorderBrush,
                BorderThickness = _sendButton.BorderThickness,
                CornerRadius = _sendButton.CornerRadius,
                HorizontalAlignment = _sendButton.HorizontalAlignment,
                VerticalAlignment = _sendButton.VerticalAlignment,
                Margin = _sendButton.Margin,
                Visibility = Visibility.Collapsed,
                Content = new FontIcon { Glyph = "\uE74A", FontSize = 14 }
            };
            AutomationProperties.SetAutomationId(_customSend, "AiJunkCleanupSend");
            AutomationProperties.SetName(_customSend, "扫描后发送");
            ToolTipService.SetToolTip(_customSend, "先扫描磁盘浅层占用，再把结果交给 AI");
            _customSend.Click += OnCustomSendClick;

            if (_sendButton.Parent is Panel parent)
            {
                var index = parent.Children.IndexOf(_sendButton);
                if (index >= 0) parent.Children.Insert(index + 1, _customSend);
                else parent.Children.Add(_customSend);
                if (parent is Grid)
                {
                    Grid.SetColumn(_customSend, Grid.GetColumn(_sendButton));
                    Grid.SetColumnSpan(_customSend, Grid.GetColumnSpan(_sendButton));
                }
            }
        }

        if (!_sendControls)
        {
            _sendControls = true;
            _sendVisibilityToken = _sendButton.RegisterPropertyChangedCallback(
                UIElement.VisibilityProperty, (_, _) => SyncSendButtons());
            _sendEnabledToken = _sendButton.RegisterPropertyChangedCallback(
                Control.IsEnabledProperty, (_, _) => SyncSendButtons());
        }

        if (!_previewHooked)
        {
            _previewHandler = OnPagePreviewKeyDown;
            _page.AddHandler(UIElement.PreviewKeyDownEvent, _previewHandler, true);
            _previewHooked = true;
        }

        SyncSendButtons();
    }

    private void OnCustomSendClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _scanSending) return;
        if (ShouldScanBeforeSend())
        {
            _ = BeginScanAndSendAsync();
            return;
        }
        InvokeHostSend();
    }

    private void SyncSendButtons()
    {
        if (_disposed || _syncingSend || _sendButton is null) return;
        _syncingSend = true;
        try
        {
            if (IsJunkSelected)
            {
                _sendButton.Visibility = Visibility.Collapsed;
                if (_customSend is not null)
                {
                    _customSend.Visibility = Visibility.Visible;
                    _customSend.IsEnabled = _sendButton.IsEnabled && !_scanSending;
                }
            }
            else
            {
                if (_customSend is not null) _customSend.Visibility = Visibility.Collapsed;
                _sendButton.Visibility = Visibility.Visible;
            }
        }
        finally { _syncingSend = false; }
    }

    private void OnPagePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_disposed || _promptBox is null || e.Key != Windows.System.VirtualKey.Enter) return;
        if (!IsWithinPrompt(e.OriginalSource)) return;
        if (IsShiftDown()) return;
        if (_scanSending)
        {
            e.Handled = true;
            return;
        }
        if (!ShouldScanBeforeSend()) return;
        e.Handled = true;
        _ = BeginScanAndSendAsync();
    }

    private bool ShouldScanBeforeSend()
    {
        if (_disposed || _scanSending || !IsJunkSelected) return false;
        if (_promptBox is null || string.IsNullOrWhiteSpace(_promptBox.Text)) return false;
        if (_sendButton is null || !_sendButton.IsEnabled) return false;
        return !_promptBox.Text.Contains(ScanResultMarker, StringComparison.Ordinal);
    }

    private async Task BeginScanAndSendAsync()
    {
        if (_disposed || _scanSending || _promptBox is null) return;
        if (!ShouldScanBeforeSend())
        {
            InvokeHostSend();
            return;
        }

        _scanSending = true;
        SyncSendButtons();
        var original = _promptBox.Text;
        var wasReadOnly = _promptBox.IsReadOnly;
        _promptBox.IsReadOnly = true;

        ShallowScanOverlay? overlay = null;
        try
        {
            if (_page?.Content is Panel root) overlay = new ShallowScanOverlay(root);
        }
        catch (Exception) { overlay = null; }
        _scanOverlay = overlay;
        var progress = new Progress<ShallowScanProgress>(item =>
        {
            if (_dispatcher.HasThreadAccess) overlay?.Update(item);
            else _dispatcher.TryEnqueue(() => overlay?.Update(item));
        });

        ShallowScanResult? result = null;
        string? failure = null;
        try
        {
            result = await _scanShallow(_depth, progress);
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        if (_disposed || _promptBox is null)
        {
            overlay?.Close();
            _scanOverlay = null;
            _scanSending = false;
            return;
        }

        if (result is { } data)
        {
            overlay?.Complete();
            SetPrompt(original + "\n\n" + BuildScanSummary(data), atEnd: true);
        }
        else
        {
            var reason = string.IsNullOrWhiteSpace(failure) ? "没有返回扫描结果。" : failure;
            overlay?.Fail(reason);
            SetPrompt(original + "\n\n〔插件浅层扫描失败〕" + reason +
                      " 请读取工作区的 shallow-scan.json；不存在时再运行 .opencode/skills/tuba-junk-cleanup/shallow-scan.ps1。",
                atEnd: true);
        }

        await Task.Delay(result is null ? 900 : 550);
        overlay?.Close();
        _scanOverlay = null;
        _promptBox.IsReadOnly = wasReadOnly;
        _scanSending = false;
        SyncSendButtons();
        InvokeHostSend();
    }

    internal static string BuildScanSummary(ShallowScanResult result)
    {
        var builder = new StringBuilder();
        builder.Append(ScanResultMarker).Append("·只读〕\n");
        builder.Append("根目录 ").Append(result.Root)
            .Append(" · 总大小 ").Append(JunkSizeScanner.FormatBytes(result.TotalBytes))
            .Append(" · 文件 ").Append(result.Files.ToString("N0"))
            .Append(" · 文件夹 ").Append(result.Folders.ToString("N0"))
            .Append(" · 耗时 ").Append(result.ElapsedSeconds.ToString("0.0")).Append(" 秒")
            .Append(" · ").Append(result.Method).Append('\n');

        var top = result.Directories
            .Where(item => item.Level == 3)
            .OrderByDescending(item => item.Bytes)
            .Take(24)
            .ToList();
        if (top.Count > 0)
        {
            builder.Append("第 3 层占用最大的目录：\n");
            foreach (var item in top)
            {
                builder.Append("- ").Append(item.Path)
                    .Append(" · ").Append(JunkSizeScanner.FormatBytes(item.Bytes))
                    .Append(" · ").Append(item.Files.ToString("N0")).Append(" 文件\n");
            }
        }

        if (result.IndexDepth > 0)
        {
            builder.Append("深度索引：");
            if (result.IndexPath is null)
            {
                builder.Append("scan-index.json 写入失败");
            }
            else
            {
                builder.Append("scan-index.json（目录到第 ").Append(result.IndexDepth)
                    .Append(" 层、≥ ").Append(JunkSizeScanner.FormatBytes(result.IndexMinDirectoryBytes))
                    .Append("；常见垃圾目录优先收录；大文件 ≥ ").Append(JunkSizeScanner.FormatBytes(result.IndexMinFileBytes))
                    .Append("；").Append(result.IndexDirectoryCount.ToString("N0"))
                    .Append(" 个目录 / ").Append(result.IndexFileCount.ToString("N0"))
                    .Append(" 个文件，含修改时间）");
            }
            builder.Append('\n');
        }

        builder.Append("完整数据：shallow-scan.json");
        if (result.IndexPath is not null) builder.Append("；深层目录和大文件直接查 scan-index.json");
        builder.Append("。请直接使用以上结果，不要再跑全盘递归扫描。");
        return builder.ToString();
    }

    private void InvokeHostSend()
    {
        if (_disposed || _sendButton is null) return;
        try
        {
            var peer = new ButtonAutomationPeer(_sendButton);
            if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider provider)
            {
                provider.Invoke();
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or COMException) { }
        try
        {
            for (var type = _sendButton.GetType(); type is not null; type = type.BaseType)
            {
                var onClick = type.GetMethod("OnClick", System.Reflection.BindingFlags.Instance |
                                                       System.Reflection.BindingFlags.NonPublic);
                if (onClick is null) continue;
                onClick.Invoke(_sendButton, null);
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                                       or System.Reflection.TargetInvocationException) { }
    }

    private bool IsWithinPrompt(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, _promptBox)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static bool IsShiftDown() =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) &
         Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    private void OnDepthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_depthBox is null || _depthBox.SelectedIndex < 0) return;
        _depth = (JunkScanDepth)_depthBox.SelectedIndex;
        ToolTipService.SetToolTip(_depthBox, "扫描深度：" + JunkScanDepths.Tip(_depth));
        _ = _saveSetting("scanDepth", JunkScanDepths.Key(_depth));
        if (!IsJunkSelected || _promptBox is null || !_promptBox.Text.StartsWith(Marker, StringComparison.Ordinal)) return;
        var target = ExtractTarget(_promptBox.Text);
        SetPrompt(BuildTemplate(_depth) + target, atEnd: true);
    }

    private void OnPromptTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _disposed || _promptBox is null || !IsJunkSelected) return;
        var text = _promptBox.Text;
        if (text.Length == 0 || text.StartsWith(Marker, StringComparison.Ordinal)) return;
        var caret = _promptBox.SelectionStart;
        var next = BuildTemplate(_depth) + text;
        SetPrompt(next, caret is > 0 ? Math.Min(caret + next.Length - text.Length, next.Length) : next.Length);
    }

    private void EnsureTemplate()
    {
        if (_promptBox is null) return;
        var text = _promptBox.Text;
        if (text.StartsWith(Marker, StringComparison.Ordinal)) return;
        SetPrompt(BuildTemplate(_depth) + (text.Length == 0 ? "" : text), atEnd: text.Length == 0);
    }

    private void RemoveTemplate()
    {
        if (_promptBox is null) return;
        var text = _promptBox.Text;
        if (!text.StartsWith(Marker, StringComparison.Ordinal)) return;
        var target = ExtractTarget(text);
        SetPrompt(target, atEnd: true);
    }

    private void SetPrompt(string text, bool atEnd) => SetPrompt(text, atEnd ? text.Length : 0);

    private void SetPrompt(string text, int caret)
    {
        if (_promptBox is null) return;
        _suppress = true;
        try
        {
            _promptBox.Text = text;
            _promptBox.SelectionStart = Math.Clamp(caret, 0, text.Length);
        }
        finally { _suppress = false; }
    }

    private static string ExtractTarget(string text)
    {
        var index = text.IndexOf(TargetLabel, StringComparison.Ordinal);
        if (index < 0)
            return text.StartsWith(Marker, StringComparison.Ordinal)
                ? text[Marker.Length..].TrimStart('\r', '\n', ' ')
                : text;
        return text[(index + TargetLabel.Length)..].TrimStart('\r', '\n', ' ');
    }

    internal static string BuildTemplate(JunkScanDepth depth) =>
        Marker + "先加载 tuba-junk-cleanup 技能（失败则读 .opencode/skills/tuba-junk-cleanup/SKILL.md）；扫描深度：" +
        JunkScanDepths.Label(depth) +
        "。本次消息末尾会自动附带插件刚完成的 NTFS/MFT 浅层扫描结果（根目录下三层）；直接使用它，不要再跑全盘递归扫描。工作区还有 scan-index.json（同一次扫描导出的更深目录 + 大文件索引），需要深层数据时直接查询它，不要再枚举硬盘；需要更多浅层细节时读取工作区 shallow-scan.json。不要只根据索引里的大文件下结论：临时目录、浏览器缓存（Cache/Code Cache/GPUCache）、Windows 更新下载缓存、崩溃转储、WER、日志、缩略图/图标缓存、应用缓存和旧安装包无论大小都要主动检查。若末尾没有结果（扫描失败），再按技能运行慢速回退。最后输出一个 tuba-junk-plan 代码块，禁止删除、移动或重命名任何文件。字段：summary, items[{path,kind,sizeBytes,risk(low|medium|high),category,reason,riskDetail,suggestion}]。\n" +
        TargetLabel + "\n";

    /// <summary>AI 在垃圾清理模式里只能读。发现删除/移动/写入命令时立即取消本轮运行。</summary>
    private static readonly Regex DestructiveCommand = new(
        @"(?ims)(?:^|[;&|{]\s*)[ \t]*(?:cmd(?:\.exe)?\s+/c\s+|powershell(?:\.exe)?\s+(?:-command|-c)\s+[""']?)?(?:remove-item|del|erase|rmdir|rd|rm|ri|move-item|move|rename-item|ren|mv|rename|format(?=\s|;|&|\||$)|diskpart|cleanmgr|clear-recyclebin|defrag|cipher|reg\s+(?:delete|add)|dism\s+[^\r\n]*?/cleanup-image|vssadmin\s+delete|set-content|out-file|add-content|clear-content|set-itemproperty|remove-itemproperty)(?=\s|;|&|\||$|})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool IsDestructiveCommand(string? command) =>
        !string.IsNullOrWhiteSpace(command) && DestructiveCommand.IsMatch(command);

    private bool GuardDestructive(IReadOnlyList<AssistantMessageView> messages)
    {
        // 只读约束只属于「垃圾清理」模式；普通对话里不能拦截宿主自己的删除命令。
        if (!IsJunkSelected) return false;
        var candidates = messages
            .SelectMany(message => message.Operations)
            .Where(operation => operation.Kind == "edit" ||
                                (operation.Kind == "command" && IsDestructiveCommand(operation.Command)))
            .ToList();
        if (!_guardPrimed)
        {
            // 打开旧会话时不要把历史命令当成刚刚发生的违规。
            foreach (var operation in candidates) _blockedCommands.Add(operation.Id);
            _guardPrimed = true;
            return false;
        }
        foreach (var operation in candidates)
        {
            if (!_blockedCommands.Add(operation.Id)) continue;
            if (_page is not null) HostBridge.CancelRun(_page);
            _ = ShowBlockedAsync(operation.Kind == "edit"
                ? "修改文件：" + (operation.Target.Length > 0 ? operation.Target : "(未知文件)")
                : operation.Command);
            return true;
        }
        return false;
    }

    private async Task ShowBlockedAsync(string command)
    {
        if (_noticeOpen) return;
        var root = _page?.XamlRoot;
        if (root is null) return;
        _noticeOpen = true;
        try
        {
            var text = command.Length > 600 ? command[..600] + "…" : command;
            var dialog = new ContentDialog
            {
                Title = "已阻止删除/修改操作",
                Content = new TextBlock
                {
                    Text = "垃圾清理模式只允许只读扫描。插件已停止本轮运行，因为 AI 尝试执行：\n\n" + text +
                           "\n\n请让 AI 重新生成清单，由你勾选确认后再删除。",
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                },
                CloseButtonText = "知道了",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = root
            };
            await dialog.ShowAsync();
        }
        catch (InvalidOperationException) { }
        finally { _noticeOpen = false; }
    }

    private void Poll()
    {
        if (_disposed) return;
        try
        {
            if (_page is not null && (_page.XamlRoot is null || !_page.IsLoaded)) DetachPage();
            ScanToolWindows();
            if (_page is null || _page.XamlRoot is null || !_page.IsLoaded) return;
            var sessionId = HostBridge.TryGetActiveSessionId(_page);
            if (!string.Equals(sessionId, _sessionId, StringComparison.Ordinal))
            {
                _sessionId = sessionId;
                _guardPrimed = false;
            }
            var messages = HostBridge.ReadMessages(_page);
            if (GuardDestructive(messages)) return;
            DecoratePlanMessages();
            for (var i = messages.Count - 1; i >= 0; i--)
            {
                var message = messages[i];
                if (!message.Completed || _handled.Contains(message.Id)) continue;
                if (!JunkPlanParser.TryParse(message.Text, out var plan)) continue;
                _handled.Add(message.Id);
                _rememberPlan(plan, JsonSerializer.Serialize(plan));
                _ = SaveHandledAsync();
                if (_lastPlanButton is not null && IsJunkSelected) _lastPlanButton.Visibility = Visibility.Visible;
                _ = ShowPlanAsync(plan, _depth);
                break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or COMException or IOException)
        {
            // 宿主页面重建/关闭时不要影响 AI 助手本身；下一次 tick 再试。
        }
    }

    /// <summary>把对话里的 tuba-junk-plan 代码块折叠掉，原位换成可重复打开的清单按钮。</summary>
    private void DecoratePlanMessages()
    {
        if (_page is null) return;
        var root = (DependencyObject?)_transcript ?? _page;
        foreach (var markdown in Descendants(root).OfType<UserControl>()
                     .Where(control => control.GetType().FullName == "TubaWinUi3.Controls.MarkdownView")
                     .ToList())
        {
            if (markdown.Content is not StackPanel body) continue;
            for (var i = 0; i < body.Children.Count; i++)
            {
                if (body.Children[i] is not Border block) continue;
                if (block.Tag as string == "AiJunkCleanupPlanBlock") continue;
                var header = Descendants(block).OfType<TextBlock>()
                    .FirstOrDefault(text => text.Text.Trim().Equals("tuba-junk-plan", StringComparison.OrdinalIgnoreCase));
                if (header is null) continue;
                var json = Descendants(block).OfType<ScrollViewer>()
                    .Select(viewer => (viewer.Content as TextBlock)?.Text)
                    .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
                if (json is null) continue;
                if (!JunkPlanParser.TryParse("```tuba-junk-plan\n" + json + "\n```", out var plan)) continue;

                block.Tag = "AiJunkCleanupPlanBlock";
                block.Visibility = Visibility.Collapsed;
                var button = BuildPlanButton(plan);
                body.Children.Insert(Math.Min(i + 1, body.Children.Count), button);
                i++;
            }
        }
    }

    private Button BuildPlanButton(JunkPlan plan)
    {
        var label = "查看清理清单 · " + plan.Items.Count + " 项";
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = "\uE74D", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button
        {
            Content = content,
            Tag = "AiJunkCleanupPlanButton",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(12, 5, 12, 5)
        };
        ToolTipService.SetToolTip(button, "打开清理清单；可以重新勾选，或查看上次未处理的项。");
        AutomationProperties.SetAutomationId(button, "AiJunkCleanupPlanButton");
        button.Click += (_, _) =>
        {
            if (!_disposed) _ = ShowPlanAsync(plan, _depth);
        };
        return button;
    }

    private async Task SaveHandledAsync()
    {
        try
        {
            // 只保留最近 200 个消息 ID，避免设置文件无限增长。
            var ids = _handled.Count <= 200 ? _handled.ToArray() : _handled.Skip(_handled.Count - 200).ToArray();
            await _saveSetting("handledPlans", JsonSerializer.Serialize(ids));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException) { }
    }

    private async Task ShowPlanAsync(JunkPlan plan, JunkScanDepth depth)
    {
        if (_disposed) return;
        if (_dialogOpen)
        {
            _pendingPlan = plan;
            _pendingDepth = depth;
            return;
        }
        _dialogOpen = true;
        try
        {
            for (var attempt = 0; attempt <= MaxRetries; attempt++)
            {
                var root = _page?.XamlRoot ?? (_access.RootVisual as FrameworkElement)?.XamlRoot;
                if (root is null)
                {
                    if (attempt < MaxRetries) await Task.Delay(1500);
                    continue;
                }
                try
                {
                    var dialog = new JunkPlanDialog(plan, depth, root, _dataDirectory, result => _rememberResult(result));
                    await dialog.ShowAsync();
                    break;
                }
                catch (InvalidOperationException)
                {
                    // 宿主可能正开着别的 ContentDialog；稍后重试，不丢清单。
                    if (attempt >= MaxRetries) break;
                    await Task.Delay(2000);
                }
                catch (Exception ex) when (ex is COMException or IOException)
                {
                    break;
                }
            }
        }
        finally { _dialogOpen = false; }
        if (_pendingPlan is { } pending)
        {
            _pendingPlan = null;
            await ShowPlanAsync(pending, _pendingDepth);
        }
    }

    /// <summary>“内置工具在独立窗口打开”时 AI 助手不在主 NavFrame 里，需要扫描 BuiltinToolWindow 的 Frame。</summary>
    private void ScanToolWindows()
    {
        if (_page is not null && _page.XamlRoot is not null) return;
        foreach (var frame in HostBridge.GetToolWindowFrames())
        {
            if (_extraFrames.Add(frame)) frame.Navigated += OnNavigated;
            if (frame.Content is Page page && page.GetType().FullName == AssistantPageType)
            {
                TryAttach(page);
                return;
            }
        }
        if (_frame?.Content is Page mainPage && mainPage.GetType().FullName == AssistantPageType)
            TryAttach(mainPage);
    }

    private void DetachPage()
    {
        if (_modeBox is not null) _modeBox.SelectionChanged -= OnModeSelectionChanged;
        if (_promptBox is not null) _promptBox.TextChanged -= OnPromptTextChanged;
        if (_page is not null) _page.SizeChanged -= OnPageSizeChanged;
        _pageHandlers = false;
        if (_page is not null && _previewHooked && _previewHandler is not null)
        {
            try { _page.RemoveHandler(UIElement.PreviewKeyDownEvent, _previewHandler); }
            catch (InvalidOperationException) { }
        }
        _previewHooked = false;
        _previewHandler = null;
        UnhookSendControls();
        if (_page is not null && _pageHooked) _page.Loaded -= OnPageLoaded;
        _pageHooked = false;
        _page = null;
        _modeBox = null;
        _promptBox = null;
        _transcript = null;
    }

    private void UnhookSendControls()
    {
        if (_sendButton is not null)
        {
            if (_sendVisibilityToken != 0)
                _sendButton.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, _sendVisibilityToken);
            if (_sendEnabledToken != 0)
                _sendButton.UnregisterPropertyChangedCallback(Control.IsEnabledProperty, _sendEnabledToken);
        }
        _sendVisibilityToken = 0;
        _sendEnabledToken = 0;
        if (_customSend?.Parent is Panel customParent) customParent.Children.Remove(_customSend);
        _customSend = null;
        _sendButton = null;
        _sendControls = false;
        _scanOverlay?.Close();
        _scanOverlay = null;
    }

    private static T? Find<T>(Page page, string name, string automationId) where T : FrameworkElement
    {
        if (page.FindName(name) is T named) return named;
        return Descendants(page).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == automationId);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _attachRetry.Stop();
        if (_frame is not null) _frame.Navigated -= OnNavigated;
        foreach (var frame in _extraFrames) frame.Navigated -= OnNavigated;
        _extraFrames.Clear();
        if (_modeBox is not null)
        {
            if (IsJunkSelected) _modeBox.SelectedIndex = 0;
            _modeBox.SelectionChanged -= OnModeSelectionChanged;
            for (var i = _modeBox.Items.Count - 1; i >= 0; i--)
                if (_modeBox.Items[i] is ComboBoxItem { Tag: string tag } && tag == ModeTag) _modeBox.Items.RemoveAt(i);
        }
        if (_promptBox is not null) _promptBox.TextChanged -= OnPromptTextChanged;
        if (_page is not null && _previewHooked && _previewHandler is not null)
        {
            try { _page.RemoveHandler(UIElement.PreviewKeyDownEvent, _previewHandler); }
            catch (InvalidOperationException) { }
        }
        _previewHooked = false;
        _previewHandler = null;
        var sendButton = _sendButton;
        UnhookSendControls();
        if (sendButton is not null) sendButton.Visibility = Visibility.Visible;
        if (_page is not null) _page.SizeChanged -= OnPageSizeChanged;
        if (_tools?.Parent is Panel oldParent) oldParent.Children.Remove(_tools);
        if (_page is not null && _pageHooked) _page.Loaded -= OnPageLoaded;
        _scanSending = false;
        _page = null;
        _frame = null;
    }
}
