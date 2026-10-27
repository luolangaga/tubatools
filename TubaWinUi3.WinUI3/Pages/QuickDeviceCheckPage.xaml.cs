using System.Speech.Synthesis;
using System.Text.Json;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;

namespace TubaWinUi3.Pages;

public sealed partial class QuickDeviceCheckPage : Page
{
    private int _currentStep;
    private const int TotalSteps = 8;
    private Controls.StressTestControl? _stressControl;
    private DiskHealthPage? _diskHealthPage;
    private Controls.KeyboardTestControl? _keyboardTestControl;
    private KeyboardTestInput? _keyboardInput;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _keyboardLoadTimeout;
    private bool _keyboardPrepared, _keyboardStarting;
    private bool _isCompleted, _stressWasRun;
    private readonly HashSet<int> _visitedSteps = [];
    private bool _sceneFailed, _keyboardFailed, _keyboardReady, _layoutAnimating;
    private double _keyboardExpansion, _layoutFrom, _layoutTo, _detailsWidth = 480;
    private long _layoutStarted;
    private SpeechSynthesizer? _speechSynth;
    private bool _cameraLaunched, _closed, _sceneReady, _sceneBooted, _laptop;
    private int _contentVersion;
    private MediaCapture? _mediaCapture;
    private MediaPlayerElement? _cameraPlayer;
    private IHardwareScene? _scene;
    private List<HardwareInfoSection>? _hardware;
    private string? _hardwareError;
    private string? _focusLabel;
    private string? _wallpaper;
    private readonly Dictionary<int, bool> _exteriorChecks = [];
    private static readonly string[] StepTitles = ["欢迎收到新电脑", "核对硬件配置", "检查硬盘状态", "检查屏幕", "检查键盘", "检查摄像头", "检查扬声器", "散热与稳定性"];
    private static readonly string[] StepLabels = ["外观", "配置", "硬盘", "屏幕", "键盘", "摄像头", "音频", "烤机"];
    private static readonly string[] StepDescriptions = ["先看整机全景，再检查机身外观。", "点击硬件条目定位整机上的对应区域，并核对订单配置。", "查看 SMART 信息，结合购买渠道与商家说明判断。", "在全屏纯色画面中检查坏点、色斑和漏光。", "逐个按下按键，已测试的键位会留下标记。", "开启预览，检查画面、对焦和颜色是否正常。", "播放测试语音，留意无声、杂音或破音。", "验机特别版：统一时长，实时观察温度与负载。"];

    public QuickDeviceCheckPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => Cleanup();
        ActualThemeChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() => _scene?.SetBackground(SceneBackground));
            PostScene(new { type = "theme", dark = ActualTheme == ElementTheme.Dark });
        };
        UpdateStepUI();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var wasClosed = _closed;
        _closed = false;
        if (wasClosed) { _sceneFailed = _keyboardFailed = false; UpdateStepUI(); }
        ToolBackButton.Visibility = BuiltinToolWindow.IsInToolWindow(this) ? Visibility.Collapsed : Visibility.Visible;
        if (_scene is not null) return;
        var scene = HardwareSceneRuntime.Create();
        _scene = scene;
        SceneHost.Content = scene.View;
        scene.MessageReceived += SceneMessageReceived;
        try
        {
            // Reuse the hardware page cache and the same laptop/desktop detection.
            _ = LoadHardwareAsync(scene);
            var laptopTask = Task.Run(HardwareInfoService.IsLaptop);
            var wallpaperTask = DesktopWallpaperService.LoadDataUriAsync();
            await Task.WhenAll(laptopTask, wallpaperTask);
            _laptop = laptopTask.Result;
            _wallpaper = wallpaperTask.Result;
            if (_closed || _scene != scene) return;
            await scene.InitializeAsync(SceneBackground);
            await Task.Delay(12000);
            if (!_closed && _scene == scene && !_sceneBooted) ShowSceneError("3D 场景加载超时，可继续完成右侧检测。");
        }
        catch (Exception ex) { if (!_closed) ShowSceneError($"3D 预览暂不可用：{ex.Message}"); }
    }

    private async Task LoadHardwareAsync(IHardwareScene scene)
    {
        try
        {
            var hardware = await HardwareInfoService.LoadAsync(false);
            if (_closed || _scene != scene) return;
            _hardware = hardware.ToList();
            _hardwareError = null;
            DeviceText.Text = _hardware.SelectMany(s => s.Items).FirstOrDefault(i => i.Label == "设备型号")?.Value ?? "本机硬件";
        }
        catch (Exception ex)
        {
            if (_closed || _scene != scene) return;
            _hardwareError = $"硬件配置读取失败：{ex.Message}。其他检测可继续。";
            DeviceText.Text = "配置暂未能读取";
        }
        if (_currentStep == 1) BuildHardwareInfoStep();
    }

    private void SceneMessageReceived(string message)
    {
        if (_closed) return;
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    _sceneReady = true;
                    PostScene(new { laptop = _laptop, wallpaper = _wallpaper, embedded = true, inspection = true, dark = ActualTheme == ElementTheme.Dark, connectors = false });
                    break;
                case "booted":
                    _sceneBooted = true;
                    SceneStatus.Visibility = Visibility.Collapsed;
                    SceneLoading.IsActive = false;
                    FocusScene(_focusLabel);
                    if (_currentStep == 4) StartKeyboardScene();
                    else if (_currentStep == 3) PostScene(new { type = "prepareKeyboard" });
                    PostScene(new { type = "stress", active = _stressControl?.IsRunning == true });
                    if (_isCompleted) PostScene(new { type = "inspectionComplete", active = true });
                    break;
                case "keyboardPrepared":
                    if (_currentStep != 4 || _keyboardFailed || _sceneFailed || _keyboardPrepared) break;
                    _keyboardPrepared = true;
                    AnimateWorkspace(true);
                    StartKeyboardTransition();
                    break;
                case "keyboardReady":
                    if (_currentStep != 4 || _keyboardFailed || _sceneFailed || _keyboardReady) break;
                    try
                    {
                        _keyboardInput = new KeyboardTestInput(DispatcherQueue);
                        _keyboardInput.KeyChanged += KeyboardKeyChanged;
                        _keyboardReady = true;
                        _keyboardLoadTimeout?.Stop();
                        KeyboardToolbar.Visibility = Visibility.Visible;
                        SceneStatus.Visibility = Visibility.Collapsed;
                        SceneLoading.IsActive = false;
                        SendKeyboard(new { type = "layout", compact = KeyboardLayout.SelectedIndex == 1 });
                        SceneHint.Text = "按实体键盘检测 · 松开后 1 秒回到全景 · 点击键帽可预览 · 功能键可能需要配合 Fn";
                    }
                    catch (Exception ex) { ShowKeyboardFallback(ex.Message); }
                    break;
                case "stats":
                    if (_currentStep == 4)
                    {
                        KeyboardStatistics.Text = $"已检测 {root.GetProperty("count").GetInt32()} / {root.GetProperty("total").GetInt32()} 键 · 最近按键：{root.GetProperty("last").GetString()}";
                        int held = root.GetProperty("held").GetInt32();
                        ActivityText.Text = held > 0 ? $"同时按住 {held} 键" : "逐个按下按键，确认后继续";
                    }
                    break;
                case "keyboardError":
                    _keyboardFailed = true;
                    ShowKeyboardFallback("3D 键盘初始化失败");
                    break;
                case "dismissFocus": FocusScene(null); break;
                case "error": ShowSceneError("3D 预览暂不可用，可继续完成右侧检测。"); break;
                case "focusFailed": ShowSceneError("部件特写未能加载，可点“回到全景”继续。"); break;
            }
        }
        catch (JsonException) { }
    }

    private void ShowSceneError(string message)
    {
        _sceneFailed = true;
        SceneLoading.IsActive = false;
        SceneStatusText.Text = message;
        SceneStatus.Visibility = Visibility.Visible;
        if (_currentStep == 4) ShowKeyboardFallback(message);
    }
    private void PostScene(object message) { if (_sceneReady) _scene?.Post(JsonSerializer.Serialize(message)); }
    private Windows.UI.Color SceneBackground => SceneSurface.Background is SolidColorBrush brush ? brush.Color : ThemeColors.CardBg;
    private void FocusScene(string? label)
    {
        _focusLabel = label;
        SceneTitle.Text = _currentStep == 4 ? (_keyboardFailed || _sceneFailed ? "检查键盘 · 2D 备用版" : "检查键盘 · 3D") : label is null ? "整机全景" : $"{label} · 检查位置";
        if (!_sceneBooted) return;
        SceneStatus.Visibility = Visibility.Collapsed;
        PostScene(new { type = "inspectionFocus", label });
    }
    private void Overview_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 4) SendKeyboard(new { type = "panorama" });
        else FocusScene(null);
    }
    private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep the controls readable at laptop widths; give remaining width to the scene.
        _detailsWidth = Math.Clamp(e.NewSize.Width * .46, 360, 520);
        ApplyWorkspaceLayout();
        LayoutRoot.Padding = e.NewSize.Width < 1000 ? new Thickness(16, 8, 16, 12) : new Thickness(24, 8, 24, 16);
        UpdateCompactChrome();
    }
    public void Cleanup()
    {
        if (_closed) return;
        _closed = true;
        StopWorkspaceAnimation();
        _contentVersion++;
        ReleaseStep();
        _sceneReady = _sceneBooted = false;
        if (_scene is { } scene)
        {
            scene.MessageReceived -= SceneMessageReceived;
            _scene = null;
            SceneHost.Content = null;
            scene.Dispose();
        }
        _speechSynth?.Dispose();
        _speechSynth = null;
        _wallpaper = null;
    }
    private void ReleaseStep()
    {
        StopKeyboardInput();
        PostScene(new { type = "keyboardMode", active = false });
        KeyboardToolbar.Visibility = Visibility.Collapsed;
        KeyboardFallbackHost.Content = null;
        KeyboardFallbackHost.Visibility = Visibility.Collapsed;
        SceneHost.Visibility = Visibility.Visible;
        if (_stressControl is { } stress)
        {
            stress.StressStarted -= OnStressStarted;
            stress.StressStopped -= OnStressStopped;
            stress.InspectionSampled -= OnInspectionSampled;
            stress.Cleanup();
            _stressControl = null;
        }
        _keyboardTestControl?.Cleanup();
        _keyboardTestControl = null;
        StopCamera();
        _cameraPlayer = null;
        _speechSynth?.SpeakAsyncCancelAll();
    }
    private void ToolBackButton_Click(object sender, RoutedEventArgs e) { Cleanup(); App.MainWindow?.NavigateBack(); }
    private void UpdateStepUI()
    {
        if (_isCompleted) LeaveCompletion();
        _visitedSteps.Add(_currentStep);
        ReleaseStep();
        _contentVersion++;
        StepIndicatorText.Text = $"{_currentStep + 1:00} / {TotalSteps:00}";
        StepTitleText.Text = StepTitles[_currentStep];
        StepDescText.Text = StepDescriptions[_currentStep];
        BackButton.IsEnabled = _currentStep > 0;
        NextButton.Content = _currentStep == 0 ? "开始验机" : _currentStep == TotalSteps - 1 ? "完成验机" : "下一步";
        ActivityText.Text = "确认后继续";
        SceneHint.Text = "拖动旋转 · 镜头随检测步骤切换 · 模型为硬件示意";
        if (_currentStep != 4) AnimateWorkspace(false);
        UpdateCompactChrome();
        StepProgressPanel.Children.Clear();
        StepProgressPanel.ColumnDefinitions.Clear();
        for (int i = 0; i < TotalSteps; i++)
        {
            StepProgressPanel.ColumnDefinitions.Add(new ColumnDefinition());
            var step = i;
            var button = new Button { Content = $"{i + 1:00}  {StepLabels[i]}", HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4, 8, 4, 8) };
            if (i == _currentStep) button.Style = Application.Current.Resources["AccentButtonStyle"] as Style;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, $"InspectionStep{i}");
            button.Click += (_, _) => GoToStep(step);
            Grid.SetColumn(button, i);
            StepProgressPanel.Children.Add(button);
        }
        StepContentPanel.Children.Clear();
        switch (_currentStep)
        {
            case 0: BuildWelcomeStep(); break;
            case 1: BuildHardwareInfoStep(); break;
            case 2: BuildDiskInfoStep(); break;
            case 3: BuildScreenTestStep(); break;
            case 4: BuildPeripheralStep(); break;
            case 5: BuildCameraStep(); break;
            case 6: BuildAudioStep(); break;
            case 7: BuildStressTestStep(); break;
        }
        FocusScene(_currentStep switch { 1 => "处理器", 2 => "硬盘", 3 => "显示器", 4 => "键盘", 5 => "摄像头", 6 => "声卡", 7 => "散热", _ => null });
    }
    private void GoToStep(int step)
    {
        if (_stressControl?.IsRunning == true || step == _currentStep) return;
        _currentStep = step;
        UpdateStepUI();
    }
    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stressControl?.IsRunning == true) return;
        if (_isCompleted)
        {
            LeaveCompletion();
            _visitedSteps.Clear(); _exteriorChecks.Clear(); _stressWasRun = false;
            _diskHealthPage = null;
            _currentStep = 0; UpdateStepUI(); return;
        }
        if (_currentStep < TotalSteps - 1) GoToStep(_currentStep + 1);
        else CompleteInspection();
    }
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted) { LeaveCompletion(); return; }
        if (_currentStep > 0) GoToStep(_currentStep - 1);
    }

    private void CompleteInspection()
    {
        if (_closed || _isCompleted || _stressControl?.IsRunning == true) return;
        _isCompleted = true;
        StopWorkspaceAnimation(); StopKeyboardInput();
        _keyboardExpansion = 0;
        PageTitle.Text = "验机完成";
        PageSubtitle.Text = "保留本次检查记录，给新电脑一个从容的开始";
        StepIndicatorText.Text = "流程已结束";
        StepTitleText.Text = "本次检查记录";
        StepDescText.Text = _stressWasRun ? "烤机记录已保留，可返回查看和导出。" : "按需返回检查，或开始新一轮验机。";
        CompletionSummaryText.Text = $"已查看 {_visitedSteps.Count} / {TotalSteps} 项 · 外观已确认 {_exteriorChecks.Count(item => item.Value)} / 5 项";
        BackButton.Content = "返回检查";
        NextButton.Content = "重新验机";
        ActivityText.Text = "本次记录保留在当前页面";
        SceneTitle.Text = "你的电脑 · 整机全景";
        SceneHint.Text = "拖动旋转 · 当前桌面壁纸 · 模型为硬件示意";
        ApplyCompletionLayout(true);
        // Retain the same WebView and the same stress control/report; no Cleanup here.
        PostScene(new { type = "inspectionComplete", active = true });
    }
    private void LeaveCompletion()
    {
        if (!_isCompleted) return;
        _isCompleted = false;
        PageTitle.Text = "快速验机";
        PageSubtitle.Text = "从外观到稳定性，逐项认识你的新电脑";
        StepIndicatorText.Text = $"{_currentStep + 1:00} / {TotalSteps:00}";
        StepTitleText.Text = StepTitles[_currentStep];
        StepDescText.Text = StepDescriptions[_currentStep];
        BackButton.Content = "上一步";
        NextButton.Content = "完成验机";
        ActivityText.Text = _stressWasRun ? "已停止，可导出报告" : "确认后继续";
        ApplyCompletionLayout(false);
        PostScene(new { type = "inspectionComplete", active = false });
        FocusScene("散热");
    }
    private void ApplyCompletionLayout(bool completed)
    {
        MainWorkspaceRow.Height = completed ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        CompletionSceneRow.Height = completed ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(SceneSurface, completed ? 1 : 0);
        Grid.SetColumnSpan(SceneSurface, completed ? 2 : 1);
        Grid.SetColumn(DetailsPanel, completed ? 0 : 1);
        Grid.SetColumnSpan(DetailsPanel, completed ? 2 : 1);
        DetailsPanel.Margin = completed ? new Thickness(0, 0, 0, 12) : new Thickness(0);
        StepContentPanel.Visibility = completed ? Visibility.Collapsed : Visibility.Visible;
        CompletionSummary.Visibility = completed ? Visibility.Visible : Visibility.Collapsed;
        StepProgressPanel.Visibility = completed ? Visibility.Collapsed : Visibility.Visible;
        ApplyWorkspaceLayout(); UpdateCompactChrome();
    }

    private static TextBlock Copy(string text, bool secondary = false)
    {
        var block = (TextBlock)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<TextBlock xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Foreground='{ThemeResource " +
            (secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush") + "}' />");
        block.Text = text; block.FontSize = 13; block.TextWrapping = TextWrapping.Wrap;
        return block;
    }
    private StackPanel ContentStack() { var panel = new StackPanel { Spacing = 12 }; StepContentPanel.Children.Add(panel); return panel; }
    private void BuildWelcomeStep()
    {
        var panel = ContentStack();
        panel.Children.Add(Copy("花几分钟完成 8 项检查。自动读取的配置与手动检测相结合，先从外观开始。", true));
        string[] checks = ["机身：没有划痕、磕碰与使用痕迹", "转轴 / 面板：无松动、异响或变形", "接口：无氧化、明显插拔痕迹", "屏幕表面：无划痕、压痕", "散热口：没有明显积灰或堵塞"];
        for (int i = 0; i < checks.Length; i++)
        {
            var index = i;
            var check = new CheckBox { Content = Copy(checks[i]), IsChecked = _exteriorChecks.GetValueOrDefault(i), HorizontalAlignment = HorizontalAlignment.Stretch };
            check.Checked += (_, _) => _exteriorChecks[index] = true;
            check.Unchecked += (_, _) => _exteriorChecks[index] = false;
            panel.Children.Add(check);
        }
        panel.Children.Add(Copy("发现异常时，先拍照留证，再联系商家。", true));
    }
    private void BuildHardwareInfoStep()
    {
        StepContentPanel.Children.Clear();
        if (_hardware is null)
        {
            ContentStack().Children.Add(Copy(_hardwareError ?? "正在读取硬件配置，请稍候…"));
            return;
        }
        var layout = new Grid { RowSpacing = 8 };
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var rows = new Grid { RowSpacing = 4 };
        var values = new List<TextBlock>();
        var items = _hardware.SelectMany(s => s.Items).GroupBy(i => i.Label).ToDictionary(g => g.Key, g => g.Last().Value);
        string[] labels = ["处理器", "显卡", "内存", "硬盘", "主板", "显示器", "网卡", "电池", "系统"];
        foreach (var label in labels)
        {
            if (!items.TryGetValue(label, out var value)) continue;
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(Copy(label, true));
            var text = Copy(value.Replace("\r", " ").Replace("\n", " "));
            text.MaxLines = 1;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            values.Add(text);
            Grid.SetColumn(text, 1); row.Children.Add(text);
            var button = new Button { Content = row, MinHeight = 0, VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 4, 8, 4) };
            ToolTipService.SetToolTip(button, value);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, $"InspectionHardware_{label}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{label}：{value}");
            button.Click += (_, _) => FocusScene(label);
            Grid.SetRow(button, rows.RowDefinitions.Count);
            rows.RowDefinitions.Add(new RowDefinition());
            rows.Children.Add(button);
        }
        rows.SizeChanged += (_, args) =>
        {
            var maxLines = args.NewSize.Height / Math.Max(1, values.Count) >= 48 ? 2 : 1;
            foreach (var text in values) text.MaxLines = maxLines;
        };
        layout.Children.Add(rows);
        var caption = Copy("悬停查看完整型号；完整参数可在“硬件信息”查看。", true);
        Grid.SetRow(caption, 1); layout.Children.Add(caption);
        StepContentPanel.Children.Add(layout);
    }
    private void BuildDiskInfoStep()
    {
        StepContentPanel.Children.Add(_diskHealthPage ??= new DiskHealthPage(embedded: true));
    }
    private void BuildScreenTestStep()
    {
        if (_sceneBooted && !_keyboardFailed) PostScene(new { type = "prepareKeyboard" });
        var panel = ContentStack();
        panel.Children.Add(Copy("白色 / 黑色：检查亮点、暗点与漏光\n红色 / 绿色 / 蓝色：检查坏点与色斑"));
        var button = new Button { Content = "开始全屏检测", Style = Application.Current.Resources["AccentButtonStyle"] as Style };
        button.Click += (_, _) => LaunchScreenTest(); panel.Children.Add(button);
        panel.Children.Add(Copy("← → 切换颜色 · Esc 返回验机", true));
    }
    private void BuildPeripheralStep()
    {
        ActivityText.Text = "逐个按下按键，确认后继续";
        SceneHint.Text = "正在准备 3D 键盘…";
        if (_sceneFailed || _keyboardFailed) ShowKeyboardFallback("3D 场景暂不可用");
        else StartKeyboardScene();
    }

    private void StartKeyboardScene()
    {
        if (!_sceneBooted || _currentStep != 4 || _sceneFailed || _keyboardFailed) return;
        SceneStatusText.Text = "正在准备 3D 键盘…";
        SceneStatus.Visibility = Visibility.Visible;
        SceneLoading.IsActive = true;
        _keyboardLoadTimeout?.Stop();
        _keyboardLoadTimeout = DispatcherQueue.CreateTimer();
        _keyboardLoadTimeout.Interval = TimeSpan.FromSeconds(15);
        _keyboardLoadTimeout.IsRepeating = false;
        _keyboardLoadTimeout.Tick += (_, _) => { if (!_keyboardReady) ShowKeyboardFallback("3D 键盘加载超时"); };
        _keyboardLoadTimeout.Start();
        PostScene(new { type = "prepareKeyboard", notify = true, width = Math.Max(1, Workspace.ActualWidth - 26) });
    }
    private void SendKeyboard(object message)
    {
        if (_keyboardReady && _currentStep == 4) PostScene(new { type = "keyboard", message });
    }
    private void KeyboardKeyChanged(int key, bool down, bool repeat) => SendKeyboard(new { type = "key", key, down, repeat });
    private void KeyboardLayout_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_keyboardReady) SendKeyboard(new { type = "layout", compact = ((ComboBox)sender).SelectedIndex == 1 });
    }
    private void KeyboardReset_Click(object sender, RoutedEventArgs e) => SendKeyboard(new { type = "reset" });
    private void StopKeyboardInput()
    {
        _keyboardReady = false;
        _keyboardPrepared = _keyboardStarting = false;
        _keyboardLoadTimeout?.Stop();
        _keyboardLoadTimeout = null;
        if (_keyboardInput is not { } input) return;
        input.KeyChanged -= KeyboardKeyChanged;
        input.Dispose();
        _keyboardInput = null;
    }
    private void ShowKeyboardFallback(string reason)
    {
        if (_closed || _currentStep != 4 || _keyboardTestControl is not null) return;
        System.Diagnostics.Debug.WriteLine($"[InspectionKeyboard] {reason}");
        _keyboardFailed = true;
        AnimateWorkspace(true);
        StopKeyboardInput();
        PostScene(new { type = "keyboardMode", active = false });
        KeyboardToolbar.Visibility = SceneStatus.Visibility = SceneHost.Visibility = Visibility.Collapsed;
        SceneLoading.IsActive = false;
        _keyboardTestControl = new Controls.KeyboardTestControl { Width = 940 };
        KeyboardFallbackHost.Content = new Viewbox { Child = _keyboardTestControl, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        KeyboardFallbackHost.Visibility = Visibility.Visible;
        SceneTitle.Text = "检查键盘 · 2D 备用版";
        SceneHint.Text = "3D 暂不可用，已自动切换到 2D 键盘检测。功能键可能需要配合 Fn。";
    }

    // Animate the real viewport width: the WebView and its camera stay alive throughout.
    private void AnimateWorkspace(bool expanded)
    {
        StopWorkspaceAnimation();
        _layoutFrom = _keyboardExpansion;
        _layoutTo = expanded ? 1 : 0;
        DetailsPanel.Visibility = Visibility.Visible;
        if (!IsLoaded || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled || _layoutFrom == _layoutTo)
        {
            _keyboardExpansion = _layoutTo;
            ApplyWorkspaceLayout();
            StartKeyboardTransition();
            return;
        }
        _layoutStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _layoutAnimating = true;
        CompositionTarget.Rendering += WorkspaceRendering;
    }
    private void WorkspaceRendering(object? sender, object e)
    {
        double t = Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(_layoutStarted).TotalMilliseconds / 450, 0, 1);
        double ease = t * t * t * (10 + t * (-15 + 6 * t));
        _keyboardExpansion = _layoutFrom + (_layoutTo - _layoutFrom) * ease;
        ApplyWorkspaceLayout();
        if (t >= 1) { StopWorkspaceAnimation(); StartKeyboardTransition(); }
    }
    private void ApplyWorkspaceLayout()
    {
        if (DetailsColumn is null) return;
        if (_isCompleted)
        {
            DetailsColumn.Width = new GridLength(0); Workspace.ColumnSpacing = 0;
            DetailsPanel.Opacity = 1; DetailsPanel.IsHitTestVisible = true;
            DetailsPanel.Clip = null; DetailsPanel.Visibility = Visibility.Visible;
            return;
        }
        double remaining = 1 - _keyboardExpansion;
        DetailsColumn.Width = new GridLength(_detailsWidth * remaining);
        Workspace.ColumnSpacing = 20 * remaining;
        DetailsPanel.Opacity = remaining;
        DetailsPanel.IsHitTestVisible = _currentStep != 4;
        DetailsPanel.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, _detailsWidth * remaining, Math.Max(0, Workspace.ActualHeight)) };
        DetailsPanel.Visibility = remaining < .001 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void StopWorkspaceAnimation()
    {
        if (!_layoutAnimating) return;
        CompositionTarget.Rendering -= WorkspaceRendering;
        _layoutAnimating = false;
    }
    private void StartKeyboardTransition()
    {
        if (_closed || _currentStep != 4 || !_keyboardPrepared || _keyboardStarting || _keyboardFailed || _sceneFailed) return;
        _keyboardStarting = true;
        PostScene(new { type = "keyboardMode", active = true, width = Math.Max(1, Workspace.ActualWidth - 26) });
    }
    private void UpdateCompactChrome()
    {
        bool compact = LayoutRoot.ActualHeight > 0 && LayoutRoot.ActualHeight < 560;
        LayoutRoot.RowSpacing = compact ? 8 : 16;
        PageTitle.FontSize = compact ? 24 : 32;
        PageSubtitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DeviceText.Visibility = _currentStep == 4 && compact ? Visibility.Collapsed : Visibility.Visible;
        SceneHint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }
    private void BuildAudioStep()
    {
        var panel = ContentStack();
        panel.Children.Add(Copy("先将音量调到适中，确认当前输出设备是要测试的扬声器。", true));
        var button = new Button { Content = "播放测试语音", Style = Application.Current.Resources["AccentButtonStyle"] as Style };
        button.Click += (_, _) => PlayTestAudio(); panel.Children.Add(button);
        panel.Children.Add(Copy("听不到声音时，请检查系统音量、静音状态与音频输出设备。", true));
    }
    private void BuildStressTestStep()
    {
        _stressControl = new Controls.StressTestControl(inspectionMode: true);
        _stressControl.StressStarted += OnStressStarted;
        _stressControl.StressStopped += OnStressStopped;
        _stressControl.InspectionSampled += OnInspectionSampled;
        StepContentPanel.Children.Add(_stressControl);
    }
    private void OnStressStarted(object? sender, EventArgs e)
    {
        if (_closed || _isCompleted) return;
        _stressWasRun = true;
        BackButton.IsEnabled = NextButton.IsEnabled = ToolBackButton.IsEnabled = false;
        foreach (var button in StepProgressPanel.Children.OfType<Button>()) button.IsEnabled = false;
        ActivityText.Text = "运行中 · 先停止再继续";
        FocusScene("烤机");
        SceneTitle.Text = "烤机中 · 夏日热浪";
        PostScene(new { type = "stress", active = true });
    }
    private void OnStressStopped(object? sender, EventArgs e)
    {
        if (_closed) return;
        if (_isCompleted) { PostScene(new { type = "stress", active = false }); return; }
        BackButton.IsEnabled = NextButton.IsEnabled = ToolBackButton.IsEnabled = true;
        foreach (var button in StepProgressPanel.Children.OfType<Button>()) button.IsEnabled = true;
        ActivityText.Text = "已停止，可导出报告";
        PostScene(new { type = "stress", active = false });
        FocusScene("散热");
    }
    private void OnInspectionSampled(MonitorSample sample) => PostScene(new
    {
        type = "stressSample", cpuTemp = SceneReading(sample.CpuTemp), gpuTemp = SceneReading(sample.GpuTemp),
        cpuLoad = SceneReading(sample.CpuLoad), gpuLoad = SceneReading(sample.GpuLoad)
    });
    private static float SceneReading(float value) => float.IsFinite(value) ? value : -1;
    private void BuildCameraStep()
    {
        var version = _contentVersion;
        var panel = ContentStack();
        var placeholder = Copy("摄像头未开启", true);
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        var player = new MediaPlayerElement { AutoPlay = true, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        _cameraPlayer = player;
        var preview = new Grid { Height = 180 };
        preview.Children.Add(placeholder); preview.Children.Add(player);
        panel.Children.Add(preview);
        var button = new Button { Content = "开启摄像头", Style = Application.Current.Resources["AccentButtonStyle"] as Style };
        panel.Children.Add(button);
        panel.Children.Add(Copy("没有摄像头的台式机可直接跳过此项。", true));
        button.Click += async (_, _) =>
        {
            if (_cameraLaunched)
            {
                StopCamera(); player.Visibility = Visibility.Collapsed;
                placeholder.Visibility = Visibility.Visible;
                button.Content = "开启摄像头"; return;
            }
            button.IsEnabled = false;
            MediaCapture? capture = null;
            try
            {
                var groups = await MediaFrameSourceGroup.FindAllAsync();
                if (_closed || version != _contentVersion) return;
                var group = groups.FirstOrDefault(g => g.SourceInfos.Any(i => i.SourceKind == MediaFrameSourceKind.Color));
                if (group is null) { placeholder.Text = "未找到摄像头设备，可跳过此项"; return; }
                capture = new MediaCapture();
                await capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    SourceGroup = group, SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                    StreamingCaptureMode = StreamingCaptureMode.Video, MemoryPreference = MediaCaptureMemoryPreference.Cpu
                });
                if (_closed || version != _contentVersion) return;
                var source = capture.FrameSources.Values.FirstOrDefault(f => f.Info.SourceKind == MediaFrameSourceKind.Color);
                if (source is null) { placeholder.Text = "摄像头未提供彩色视频源"; return; }
                player.Source = MediaSource.CreateFromMediaFrameSource(source);
                _mediaCapture = capture; capture = null;
                player.Visibility = Visibility.Visible; placeholder.Visibility = Visibility.Collapsed;
                _cameraLaunched = true; button.Content = "关闭摄像头";
            }
            catch (UnauthorizedAccessException) { placeholder.Text = "访问被拒绝，请在 Windows 隐私设置中允许摄像头访问"; }
            catch (Exception ex) { if (!_closed && version == _contentVersion) placeholder.Text = $"摄像头启动失败：{ex.Message}"; }
            finally { capture?.Dispose(); button.IsEnabled = true; }
        };
    }
    private void StopCamera()
    {
        if (_cameraPlayer is { } player)
        {
            var source = player.Source;
            player.Source = null;
            var mediaPlayer = player.MediaPlayer;
            player.SetMediaPlayer(null);
            mediaPlayer?.Dispose();
            (source as IDisposable)?.Dispose();
        }
        _mediaCapture?.Dispose(); _mediaCapture = null;
        _cameraLaunched = false;
    }

    private void LaunchScreenTest()
    {
        try
        {
            var screenTestTool = BuiltinToolRegistry.GetById("screen-test");
            if (screenTestTool != null)
            {
                var context = new BuiltinToolContext { XamlRoot = XamlRoot };
                MainWindow.ActiveToolName = screenTestTool.Name;
                _ = screenTestTool.ExecuteAsync(context);
                SendToast("屏幕检测已启动", "使用方向键切换颜色，ESC 退出");
            }
        }
        catch (Exception ex)
        {
            SendToast("启动失败", ex.Message);
        }
    }

    private void PlayTestAudio()
    {
        try
        {
            _speechSynth ??= new SpeechSynthesizer();
            _speechSynth.SpeakAsync("你能否听清这段录音？");
            SendToast("正在播放测试语音", "请确认是否能听清");
        }
        catch (Exception ex)
        {
            SendToast("语音播放失败", ex.Message);
        }
    }

    private static void SendToast(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title).AddText(message).Show();
        }
        catch { }
    }


}
