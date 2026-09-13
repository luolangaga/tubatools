using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using TubaWinUi3.Services.ActiveIntercept;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;

namespace TubaWinUi3.Pages;

#region Widget type enum

public enum OverlayWidgetType
{
    FpsText, CpuTempText, CpuLoadText, CpuClockText, CpuPowerText,
    GpuTempText, GpuLoadText, GpuClockText, GpuPowerText, GpuVramText,
    MemLoadText, MemUsedText,
    DiskReadText, DiskWriteText,
    NetUpText, NetDownText,
    CpuNameText, GpuNameText,
    FpsChart, CpuTempChart,
    CustomText, CustomImage, ColorBlock,
    FpsLow1Text, FpsLow01Text,
    // 追加（必须留在末尾）：布局按枚举数字序列化，插中间会错位旧配置。
    FpsTimeText, FpsTimeChart, FpsRenderLatencyText, FpsRenderLatencyChart,
    FpsLow1Chart, FpsLow01Chart
}

#endregion

public sealed partial class GameOverlayPage : Page
{
    #region Fields

    private readonly List<DesignerWidget> _widgets = new();
    private DesignerWidget? _selectedWidget;
    private bool _overlayRunning;
    private DispatcherTimer? _pollTimer;
    private readonly List<GameWindowInfo> _gameWindows = new();
    private readonly List<string> _customWindowTitles = new();
    private IntPtr _targetHwnd;
    private bool _isDesktopTarget;
    private bool _samplingInFlight;
    private double _scalePercent = 100;
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartX, _dragStartY;
    private bool _suppressEvents;
    // LoadConfig 完成前禁止 SaveConfig 落盘（防止初始化事件用空 _widgets 覆盖已保存布局）
    private bool _configLoaded;
    private bool _widgetHasCapture;
    private const string SettingsPrefix = "GameOverlay_";

    // ---- 数据记录（会话逻辑在 Services.GameMonitorRecordSession，与后台自动记录共用一份） ----
    private readonly Dictionary<string, CheckBox> _recordChecks = new();
    private readonly GameMonitorRecordSession _session = new();
    private List<MonitorRecordMetric> _recordMetrics = new();
    private DispatcherTimer? _recordTimer;
    private bool _recording;
    private bool _recordSamplingInFlight;
    private bool _suppressRecordMetricEvents;
    private double _recordIntervalMs = 1000;
    private string _lastRecordDir = "";
    private MonitorRecordOutput _recordOutputs = GameMonitorRecorder.DefaultOutputs;
    private bool _recordUiReady;
    private bool _recordDialogOpen;
    private const string RecordMetricsSetting = SettingsPrefix + "RecordMetrics";
    private const string RecordOutputsSetting = SettingsPrefix + "RecordOutputs";

    private sealed class DesignerWidget
    {
        public OverlayWidgetType Type;
        public Border? Container;
        public TextBlock? TextElement;
        public Canvas? ChartElement;
        public double X, Y, Width = 140, Height = 32;
        public double FontSize = 14;
        public string Prefix = "";
        public bool ShowPrefix = true;
        public int Layer;
        public string Label = "";
        public bool IsChart;
        // Custom content
        public string CustomText = "";
        public string ImagePath = "";
        public uint ColorArgb = 0xFF00A0FF;
        public uint TextColorArgb = 0xFFFFFFFF;
        // Resize handle
        public Thumb? ResizeThumb;
    }

    private sealed class GameWindowInfo
    {
        public IntPtr Hwnd;
        public string Title = "";
        public string ProcessName = "";
        public uint Pid;
        public bool IsCustom;
        public bool IsDesktop;
    }

    #endregion

    #region Win32 P/Invoke for window enumeration

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    #endregion

    public GameOverlayPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        InitBackendMonitorToggle();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // 开关状态可能被设置页/另一处改过，每次进入页面都刷新
        _backendToggleInitializing = true;
        TglBackendMonitor.IsOn = GameMonitorBackendService.IsEnabled;
        _backendToggleInitializing = false;
        UpdateBackendMonitorStatus();

        if (!IsRunningAsAdmin())
        {
            AdminOverlay.Visibility = Visibility.Visible;
            return;
        }

        InitPalette();
        InitFontCombo();
        InitRecordMetrics();
        LoadRecordSettings();
        RefreshPresetCombo();
        LoadConfig();
        ScanGameWindows();

        // 首次打开游戏监控工具：弹窗询问是否开启后台自动监控（仅询问一次）
        _ = ShowBackendMonitorPromptAsync();
    }

    // ================= 后台自动监控（与流氓软件拦截共用后端进程） =================

    private bool _backendToggleInitializing;

    /// <summary>
    /// 初始化「后台自动监控」开关：MSIX 打包模式 / 后端 exe 缺失时整个选项隐藏。
    /// </summary>
    private void InitBackendMonitorToggle()
    {
        _backendToggleInitializing = true;

        if (!GameMonitorBackendService.IsSupported || !GameMonitorBackendService.IsBackendAvailable)
        {
            TglBackendMonitor.Visibility = Visibility.Collapsed;
        }
        else
        {
            TglBackendMonitor.IsOn = GameMonitorBackendService.IsEnabled;
        }

        _backendToggleInitializing = false;
        UpdateBackendMonitorStatus();
    }

    private void TglBackendMonitor_Toggled(object sender, RoutedEventArgs e)
    {
        if (_backendToggleInitializing) return;
        GameMonitorBackendService.SetEnabled(TglBackendMonitor.IsOn);
        UpdateBackendMonitorStatus();
    }

    private void UpdateBackendMonitorStatus()
    {
        if (!GameMonitorBackendService.IsSupported) return;

        if (!TglBackendMonitor.IsOn)
        {
            TxtBackendStatus.Text = "";
        }
        else if (ActiveInterceptService.IsRunning)
        {
            TxtBackendStatus.Text = "运行中";
            TxtBackendStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
        }
        else
        {
            TxtBackendStatus.Text = "未运行（后端缺失）";
            TxtBackendStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
        }
    }

    /// <summary>首次打开本工具时询问是否开启后台自动监控；无论选什么只询问一次。</summary>
    private async Task ShowBackendMonitorPromptAsync()
    {
        try
        {
            if (!GameMonitorBackendService.IsSupported || !GameMonitorBackendService.IsBackendAvailable) return;
            if (GameMonitorBackendService.HasPrompted) return;
            if (XamlRoot is null) return;

            GameMonitorBackendService.MarkPrompted();

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "开启游戏后台自动监控？",
                Content = "开启后，即使图吧工具箱没有运行，后台服务也会在检测到全屏/无边框游戏时，"
                        + "自动在游戏窗口上显示 FPS、1% Low、帧生成时间等读数。\n\n"
                        + "该功能与「流氓软件拦截」共用同一个轻量后台服务，随时可以在本页面开关。",
                PrimaryButtonText = "开启",
                CloseButtonText = "暂不",
                DefaultButton = ContentDialogButton.Primary,
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !TglBackendMonitor.IsOn)
            {
                _backendToggleInitializing = true;
                TglBackendMonitor.IsOn = true;
                _backendToggleInitializing = false;
                GameMonitorBackendService.SetEnabled(true);
                UpdateBackendMonitorStatus();
            }
        }
        catch
        {
            // 弹窗失败（页面未挂载等）不影响页面其余功能
        }
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void RestartAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
            Application.Current.Exit();
        }
        catch
        {
            // User cancelled UAC prompt — do nothing
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        StopPolling();
        // 离开页面时把已记录的数据落盘，避免整段记录丢失
        SaveRecordingOnUnload();
        GameOverlayWindow.CloseOverlay();
    }

    #region Widget Palette

    private static readonly (OverlayWidgetType Type, string Label, string Icon, bool IsChart)[] PaletteItems =
    [
        (OverlayWidgetType.FpsText, "FPS", "\uE9F5", false),
        (OverlayWidgetType.CpuTempText, "CPU 温度", "\uE9B0", false),
        (OverlayWidgetType.CpuLoadText, "CPU 负载", "\uE9B0", false),
        (OverlayWidgetType.CpuClockText, "CPU 频率", "\uE9B0", false),
        (OverlayWidgetType.CpuPowerText, "CPU 功耗", "\uE9B0", false),
        (OverlayWidgetType.CpuNameText, "CPU 名称", "\uE9B0", false),
        (OverlayWidgetType.GpuTempText, "GPU 温度", "\uE9B0", false),
        (OverlayWidgetType.GpuLoadText, "GPU 负载", "\uE9B0", false),
        (OverlayWidgetType.GpuClockText, "GPU 频率", "\uE9B0", false),
        (OverlayWidgetType.GpuPowerText, "GPU 功耗", "\uE9B0", false),
        (OverlayWidgetType.GpuVramText, "显存使用", "\uE9B0", false),
        (OverlayWidgetType.GpuNameText, "GPU 名称", "\uE9B0", false),
        (OverlayWidgetType.MemLoadText, "内存负载", "\uE9B0", false),
        (OverlayWidgetType.MemUsedText, "内存使用", "\uE9B0", false),
        (OverlayWidgetType.DiskReadText, "磁盘读取", "\uE9B0", false),
        (OverlayWidgetType.DiskWriteText, "磁盘写入", "\uE9B0", false),
        (OverlayWidgetType.NetUpText, "网络上传", "\uE9B0", false),
        (OverlayWidgetType.NetDownText, "网络下载", "\uE9B0", false),
        (OverlayWidgetType.FpsChart, "FPS 图表", "\uE9F5", true),
        (OverlayWidgetType.FpsTimeText, "帧时间", "\uE823", false),
        (OverlayWidgetType.FpsTimeChart, "帧时间 图表", "\uE823", true),
        (OverlayWidgetType.FpsRenderLatencyText, "渲染延迟", "\uE823", false),
        (OverlayWidgetType.FpsRenderLatencyChart, "渲染延迟 图表", "\uE823", true),
        (OverlayWidgetType.FpsLow1Text, "1% Low", "\uE9F5", false),
        (OverlayWidgetType.FpsLow1Chart, "1% Low 图表", "\uE9F5", true),
        (OverlayWidgetType.FpsLow01Text, "0.1% Low", "\uE9F5", false),
        (OverlayWidgetType.FpsLow01Chart, "0.1% Low 图表", "\uE9F5", true),
        (OverlayWidgetType.CpuTempChart, "CPU温度 图表", "\uE9B0", true),
        (OverlayWidgetType.CustomText, "自定义文字", "\uE8E5", false),
        (OverlayWidgetType.CustomImage, "自定义图片", "\uEB9F", false),
        (OverlayWidgetType.ColorBlock, "自定义色块", "\uE790", false),
    ];

    private void InitPalette()
    {
        PalettePanel.Children.Clear();
        foreach (var (type, label, icon, isChart) in PaletteItems)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                Tag = type,
                IsHitTestVisible = true,
                AllowDrop = false,
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            sp.Children.Add(new FontIcon
            {
                Glyph = icon,
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = label + (isChart ? " 📊" : ""),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            card.Child = sp;

            // Click to add widget
            card.PointerPressed += PaletteItem_PointerPressed;

            // Hover effect
            card.PointerEntered += (_, _) =>
            {
                card.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
            };
            card.PointerExited += (_, _) =>
            {
                card.Background = new SolidColorBrush(Colors.Transparent);
            };

            PalettePanel.Children.Add(card);
        }
    }

    private void InitFontCombo()
    {
        var families = SKFontManager.Default.GetFontFamilies();
        CmbFont.Items.Clear();
        int selectedIndex = 0;
        string defaultFont = "Microsoft YaHei UI";
        for (int i = 0; i < families.Length; i++)
        {
            CmbFont.Items.Add(families[i]);
            if (families[i].Equals(defaultFont, StringComparison.OrdinalIgnoreCase))
                selectedIndex = i;
        }
        // 设置选中项会触发 Font_Changed → SaveConfig()；此时 LoadConfig 还没跑、
        // _widgets 为空，会把已保存的 GameOverlay_Layout 覆盖成 "[]" —— 自动悬浮窗
        // 退化成默认 FPS+曲线、编辑器画布空白的元凶。必须先屏蔽事件。
        _suppressEvents = true;
        if (CmbFont.Items.Count > 0)
            CmbFont.SelectedIndex = selectedIndex;
        _suppressEvents = false;
    }

    private void Font_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (CmbFont.SelectedItem is not string family) return;
        GameOverlayWindow.SetFontFamily(family);
        SaveConfig();
    }

    private void PaletteItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card && card.Tag is OverlayWidgetType type)
        {
            // Click to add widget at a staggered position on the canvas
            double offsetX = (_widgets.Count % 5) * 30 + 10;
            double offsetY = (_widgets.Count / 5) * 40 + 10;
            AddWidgetToCanvas(type, offsetX, offsetY);
        }
    }

    #endregion

    #region Canvas Drag-Drop

    private void Canvas_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "放置组件";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        try
        {
            var def = e.GetDeferral();
            var pos = e.GetPosition(DesignCanvas);

            // Try to get the widget type from the data
            if (e.DataView.Contains("StandardText"))
            {
                var task = e.DataView.GetTextAsync().AsTask();
                task.Wait();
                var text = task.Result;
                if (Enum.TryParse<OverlayWidgetType>(text, out var type))
                {
                    AddWidgetToCanvas(type, pos.X, pos.Y);
                }
            }
            def.Complete();
        }
        catch { }
    }

    private void AddWidgetToCanvas(OverlayWidgetType type, double x, double y)
    {
        var info = PaletteItems.FirstOrDefault(p => p.Type == type);
        var widget = new DesignerWidget
        {
            Type = type,
            X = Math.Max(4, x),
            Y = Math.Max(4, y),
            Label = info.Label,
            IsChart = info.IsChart,
            Width = type switch
            {
                OverlayWidgetType.CustomImage => 120,
                OverlayWidgetType.ColorBlock => 48,
                OverlayWidgetType.CpuNameText or OverlayWidgetType.GpuNameText => 220,
                _ when info.IsChart => 160,
                _ => 140
            },
            Height = type switch
            {
                OverlayWidgetType.CustomImage => 90,
                OverlayWidgetType.ColorBlock => 48,
                _ when info.IsChart => 60,
                _ => 32
            },
            FontSize = type == OverlayWidgetType.CpuNameText || type == OverlayWidgetType.GpuNameText ? 13 : 14,
            CustomText = type == OverlayWidgetType.CustomText ? "自定义文字" : "",
            ImagePath = type == OverlayWidgetType.CustomImage ? "" : "",
            ColorArgb = 0xFF00A0FF,
            ShowPrefix = !info.IsChart && type != OverlayWidgetType.CustomText,
            Prefix = GameOverlayWindow.GetDefaultPrefix(type),
            Layer = 0
        };

        CreateWidgetElement(widget);
        _widgets.Add(widget);
        SelectWidget(widget);
        UpdateStatus();
        SaveConfig();
    }

    private void CreateWidgetElement(DesignerWidget widget)
    {
        var container = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Width = widget.Width,
            Height = widget.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Tag = widget,
            IsHitTestVisible = true,
            AllowDrop = false,
        };

        if (widget.IsChart)
        {
            var chartCanvas = new Canvas { Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)) };
            // Add chart label preview
            var chartLabel = new TextBlock
            {
                Text = widget.Label,
                FontSize = Math.Max(8, widget.FontSize),
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 2, 0, 0)
            };
            chartCanvas.Children.Add(chartLabel);
            widget.ChartElement = chartCanvas;
            container.Child = chartCanvas;
        }
        else if (widget.Type == OverlayWidgetType.CustomImage)
        {
            // Preview: show a placeholder or loaded image
            var img = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Source = LoadImageSource(widget.ImagePath, widget.Label)
            };
            widget.ChartElement = null;
            widget.TextElement = null;
            container.Child = img;
        }
        else if (widget.Type == OverlayWidgetType.ColorBlock)
        {
            // Preview: solid color block
            var c = FromArgb(widget.ColorArgb);
            container.Background = new SolidColorBrush(c);
            widget.TextElement = null;
            widget.ChartElement = null;
            container.Child = null;
            container.CornerRadius = new CornerRadius(4);
        }
        else
        {
            string preview = widget.Type == OverlayWidgetType.CustomText
                ? (string.IsNullOrEmpty(widget.CustomText) ? "自定义文字" : widget.CustomText)
                : widget.ShowPrefix
                    ? $"{widget.Prefix}--"
                    : "--";
            var tb = new TextBlock
            {
                Text = preview,
                FontSize = widget.FontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(FromArgb(widget.TextColorArgb)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                TextTrimming = TextTrimming.Clip
            };
            widget.TextElement = tb;
            container.Child = tb;
        }

        widget.Container = container;

        // Mouse handlers for drag reposition on canvas
        container.PointerPressed += Widget_PointerPressed;
        container.PointerMoved += Widget_PointerMoved;
        container.PointerReleased += Widget_PointerReleased;

        Canvas.SetLeft(container, widget.X);
        Canvas.SetTop(container, widget.Y);
        Canvas.SetZIndex(container, widget.Layer);
        DesignCanvas.Children.Add(container);

        // Resize thumb
        var thumb = new Thumb
        {
            Width = 8,
            Height = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(180, 100, 100, 255)),
        };
        thumb.DragDelta += ResizeThumb_DragDelta;
        widget.ResizeThumb = thumb;
        // Add resize thumb to canvas overlay
        Canvas.SetLeft(thumb, widget.X + widget.Width - 4);
        Canvas.SetTop(thumb, widget.Y + widget.Height - 4);
        Canvas.SetZIndex(thumb, 10);
        DesignCanvas.Children.Add(thumb);
    }

    private void Widget_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border container || container.Tag is not DesignerWidget widget) return;

        SelectWidget(widget);
        _isDragging = false;
        _widgetHasCapture = true;
        var pos = e.GetCurrentPoint(DesignCanvas).Position;
        _dragStartPoint = pos;
        _dragStartX = widget.X;
        _dragStartY = widget.Y;
        container.CapturePointer(e.Pointer);
    }

    private void Widget_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border container || container.Tag is not DesignerWidget widget) return;
        if (!_widgetHasCapture) return;

        var pos = e.GetCurrentPoint(DesignCanvas).Position;
        var dx = pos.X - _dragStartPoint.X;
        var dy = pos.Y - _dragStartPoint.Y;

        if (!_isDragging && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
            _isDragging = true;

        if (_isDragging)
        {
            widget.X = Math.Max(0, _dragStartX + dx);
            widget.Y = Math.Max(0, _dragStartY + dy);
            Canvas.SetLeft(widget.Container, widget.X);
            Canvas.SetTop(widget.Container, widget.Y);

            // Move resize thumb too
            if (widget.ResizeThumb != null)
            {
                Canvas.SetLeft(widget.ResizeThumb, widget.X + widget.Width - 4);
                Canvas.SetTop(widget.ResizeThumb, widget.Y + widget.Height - 4);
            }

            // Update properties panel
            _suppressEvents = true;
            if (PropX != null) PropX.Value = widget.X;
            if (PropY != null) PropY.Value = widget.Y;
            _suppressEvents = false;
        }
    }

    private void Widget_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border container)
        {
            _widgetHasCapture = false;
            container.ReleasePointerCapture(e.Pointer);
            if (_isDragging)
            {
                SaveConfig();
                _isDragging = false;
            }
        }
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb) return;
        var widget = _widgets.FirstOrDefault(w => w.ResizeThumb == thumb);
        if (widget == null) return;

        widget.Width = Math.Max(30, widget.Width + e.HorizontalChange);
        widget.Height = Math.Max(16, widget.Height + e.VerticalChange);

        if (widget.Container != null)
        {
            widget.Container.Width = widget.Width;
            widget.Container.Height = widget.Height;
        }

        Canvas.SetLeft(thumb, widget.X + widget.Width - 4);
        Canvas.SetTop(thumb, widget.Y + widget.Height - 4);

        _suppressEvents = true;
        if (PropW != null) PropW.Value = widget.Width;
        if (PropH != null) PropH.Value = widget.Height;
        _suppressEvents = false;

        SaveConfig();
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Click on empty canvas area — deselect
        if (e.OriginalSource is Canvas canvas && canvas == DesignCanvas)
        {
            SelectWidget(null);
        }
    }

    #endregion

    #region Widget Selection & Properties

    private void SelectWidget(DesignerWidget? widget)
    {
        // Deselect previous
        if (_selectedWidget?.Container != null)
        {
            _selectedWidget.Container.BorderBrush = new SolidColorBrush(Colors.Transparent);
            if (_selectedWidget.ResizeThumb != null)
                _selectedWidget.ResizeThumb.Visibility = Visibility.Collapsed;
        }

        _selectedWidget = widget;

        if (widget != null)
        {
            widget.Container!.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            if (widget.ResizeThumb != null)
                widget.ResizeThumb.Visibility = Visibility.Visible;

            CardProps.Visibility = Visibility.Visible;
            TxtCompLabel.Text = widget.Label;
            _suppressEvents = true;
            PropX.Value = widget.X;
            PropY.Value = widget.Y;
            PropW.Value = widget.Width;
            PropH.Value = widget.Height;
            PropFS.Value = widget.FontSize;
            PropPrefix.Text = widget.Prefix;
            // Font size is editable for text widgets AND charts (chart title size)
            PropPrefix.IsEnabled = !widget.IsChart;
            PropShowPrefix.IsEnabled = !widget.IsChart && widget.Type != OverlayWidgetType.CustomText;
            PropShowPrefix.IsOn = widget.ShowPrefix;
            TxtLayer.Text = $"图层 {widget.Layer}";
            BtnLayerUp.IsEnabled = true;
            BtnLayerDown.IsEnabled = true;

            // Custom content panels
            bool isCustomText = widget.Type == OverlayWidgetType.CustomText;
            bool isCustomImage = widget.Type == OverlayWidgetType.CustomImage;
            bool isColorBlock = widget.Type == OverlayWidgetType.ColorBlock;
            bool isTextWidget = !widget.IsChart && !isCustomImage && !isColorBlock;
            CardCustomText.Visibility = isCustomText ? Visibility.Visible : Visibility.Collapsed;
            CardCustomImage.Visibility = isCustomImage ? Visibility.Visible : Visibility.Collapsed;
            CardCustomColor.Visibility = isColorBlock ? Visibility.Visible : Visibility.Collapsed;
            CardTextColor.Visibility = isTextWidget ? Visibility.Visible : Visibility.Collapsed;

            if (isCustomText) PropCustomText.Text = widget.CustomText;
            if (isCustomImage) PropImagePath.Text = string.IsNullOrEmpty(widget.ImagePath) ? "未选择图片" : Path.GetFileName(widget.ImagePath);
            if (isColorBlock) ColorPreview.Background = new SolidColorBrush(FromArgb(widget.ColorArgb));
            if (isTextWidget) TextColorPreview.Background = new SolidColorBrush(FromArgb(widget.TextColorArgb));
            _suppressEvents = false;
        }
        else
        {
            CardProps.Visibility = Visibility.Collapsed;
        }
    }

    private void PropPos_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents || _selectedWidget == null) return;
        _selectedWidget.X = PropX.Value;
        _selectedWidget.Y = PropY.Value;
        if (_selectedWidget.Container != null)
        {
            Canvas.SetLeft(_selectedWidget.Container, _selectedWidget.X);
            Canvas.SetTop(_selectedWidget.Container, _selectedWidget.Y);
        }
        if (_selectedWidget.ResizeThumb != null)
        {
            Canvas.SetLeft(_selectedWidget.ResizeThumb, _selectedWidget.X + _selectedWidget.Width - 4);
            Canvas.SetTop(_selectedWidget.ResizeThumb, _selectedWidget.Y + _selectedWidget.Height - 4);
        }
        SaveConfig();
    }

    private void PropSize_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents || _selectedWidget == null) return;
        _selectedWidget.Width = Math.Max(30, PropW.Value);
        _selectedWidget.Height = Math.Max(16, PropH.Value);
        if (_selectedWidget.Container != null)
        {
            _selectedWidget.Container.Width = _selectedWidget.Width;
            _selectedWidget.Container.Height = _selectedWidget.Height;
        }
        if (_selectedWidget.ResizeThumb != null)
        {
            Canvas.SetLeft(_selectedWidget.ResizeThumb, _selectedWidget.X + _selectedWidget.Width - 4);
            Canvas.SetTop(_selectedWidget.ResizeThumb, _selectedWidget.Y + _selectedWidget.Height - 4);
        }
        SaveConfig();
    }

    private void PropFS_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents || _selectedWidget == null) return;
        _selectedWidget.FontSize = Math.Max(8, PropFS.Value);
        if (_selectedWidget.TextElement != null)
        {
            _selectedWidget.TextElement.FontSize = _selectedWidget.FontSize;
        }
        else if (_selectedWidget.ChartElement?.Children.FirstOrDefault() is TextBlock chartLabel)
        {
            // Charts: FontSize controls the title — keep the designer preview in sync
            chartLabel.FontSize = Math.Max(8, _selectedWidget.FontSize);
        }
        SaveConfig();
    }

    private void PropPrefix_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || _selectedWidget == null || _selectedWidget.IsChart) return;
        _selectedWidget.Prefix = PropPrefix.Text ?? "";
        RefreshSelectedPreview();
        SaveConfig();
    }

    private void PropShowPrefix_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || _selectedWidget == null || _selectedWidget.IsChart) return;
        _selectedWidget.ShowPrefix = PropShowPrefix.IsOn;
        RefreshSelectedPreview();
        SaveConfig();
    }

    private void LayerUp_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidget == null) return;
        _selectedWidget.Layer++;
        ApplySelectedLayer();
    }

    private void LayerDown_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidget == null) return;
        _selectedWidget.Layer--;
        ApplySelectedLayer();
    }

    private void ApplySelectedLayer()
    {
        if (_selectedWidget == null) return;
        TxtLayer.Text = $"图层 {_selectedWidget.Layer}";
        if (_selectedWidget.Container != null)
            Canvas.SetZIndex(_selectedWidget.Container, _selectedWidget.Layer);
        SaveConfig();
    }

    private void RefreshSelectedPreview()
    {
        if (_selectedWidget?.TextElement == null) return;
        if (_selectedWidget.Type == OverlayWidgetType.CustomText)
        {
            _selectedWidget.TextElement.Text = string.IsNullOrEmpty(_selectedWidget.CustomText)
                ? "自定义文字" : _selectedWidget.CustomText;
        }
        else
        {
            _selectedWidget.TextElement.Text = _selectedWidget.ShowPrefix
                ? $"{_selectedWidget.Prefix}--"
                : "--";
        }
    }

    private void PropCustomText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || _selectedWidget == null) return;
        _selectedWidget.CustomText = PropCustomText.Text ?? "";
        if (_selectedWidget.TextElement != null)
            _selectedWidget.TextElement.Text = string.IsNullOrEmpty(_selectedWidget.CustomText)
                ? "自定义文字" : _selectedWidget.CustomText;
        SaveConfig();
    }

    private async void PickImage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidget == null) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".webp");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

            var window = App.MainWindow;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                _selectedWidget.ImagePath = file.Path;
                PropImagePath.Text = Path.GetFileName(file.Path);
                // Refresh preview
                var img = _selectedWidget.Container?.Child as Image;
                if (img != null) img.Source = LoadImageSource(file.Path, _selectedWidget.Label);
                SaveConfig();
            }
        }
        catch { }
    }

    private async void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidget == null) return;

        var (ok, picked) = await ShowColorPickerDialogAsync(_selectedWidget.ColorArgb, "选择色块颜色");
        if (ok)
        {
            _selectedWidget.ColorArgb = picked;
            ColorPreview.Background = new SolidColorBrush(FromArgb(picked));
            if (_selectedWidget.Container != null)
                _selectedWidget.Container.Background = new SolidColorBrush(FromArgb(picked));
            SaveConfig();
        }
    }

    private async void PickTextColor_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidget == null) return;

        var (ok, picked) = await ShowColorPickerDialogAsync(_selectedWidget.TextColorArgb, "选择文字颜色");
        if (ok)
        {
            _selectedWidget.TextColorArgb = picked;
            TextColorPreview.Background = new SolidColorBrush(FromArgb(picked));
            if (_selectedWidget.TextElement != null)
                _selectedWidget.TextElement.Foreground = new SolidColorBrush(FromArgb(picked));
            SaveConfig();
        }
    }

    /// <summary>
    /// Shows the preset color grid dialog; returns the picked ARGB color (with alpha).
    /// </summary>
    private async Task<(bool ok, uint argb)> ShowColorPickerDialogAsync(uint current, string title)
    {
        var presetColors = new (string Name, uint Argb)[]
        {
            ("蓝色", 0xFF0080FF), ("青色", 0xFF00C8C8), ("绿色", 0xFF00C050),
            ("橙色", 0xFFFF8000), ("红色", 0xFFFF4040), ("黄色", 0xFFFFD700),
            ("紫色", 0xFF9040FF), ("粉色", 0xFFFF69B4), ("白色", 0xFFFFFFFF),
            ("灰色", 0xFF808080), ("黑色", 0xFF202020), ("透明黑", 0xEE000000),
        };

        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

        uint picked = current;
        var tcs = new TaskCompletionSource<bool>();
        ContentDialog? dialog = null;

        for (int i = 0; i < presetColors.Length; i++)
        {
            var (name, argb) = presetColors[i];
            var box = new Button
            {
                Width = 36,
                Height = 26,
                Background = new SolidColorBrush(FromArgb(argb)),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = argb
            };
            ToolTipService.SetToolTip(box, name);
            box.Click += (_, _) =>
            {
                picked = (uint)box.Tag;
                // Pick a color → apply and close the dialog right away
                try { dialog?.Hide(); } catch { }
                tcs.TrySetResult(true);
            };
            Grid.SetRow(box, i / 3);
            Grid.SetColumn(box, i % 3);
            grid.Children.Add(box);
        }

        dialog = new ContentDialog
        {
            Title = title,
            Content = new StackPanel { Spacing = 10, Children = { grid } },
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        bool ok = false;
        try { await dialog.ShowAsync(); } catch { }
        if (tcs.Task.IsCompleted) ok = true;
        return (ok, picked);
    }

    private void DeleteComp_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidget == null) return;
        if (_selectedWidget.Container != null)
            DesignCanvas.Children.Remove(_selectedWidget.Container);
        if (_selectedWidget.ResizeThumb != null)
            DesignCanvas.Children.Remove(_selectedWidget.ResizeThumb);
        _widgets.Remove(_selectedWidget);
        SelectWidget(null);
        UpdateStatus();
        SaveConfig();
    }

    #endregion

    #region Canvas Size & Position

    /// <summary>
    /// Positions the resize thumb, size label and dashed border at the canvas's
    /// bottom-right corner so they always track the canvas dimensions.
    /// </summary>
    private void UpdateCanvasDecorations()
    {
        double w = DesignCanvas.Width;
        double h = DesignCanvas.Height;
        if (double.IsNaN(w) || w < 1) w = 600;
        if (double.IsNaN(h) || h < 1) h = 300;
        DesignCanvas.Width = w;
        DesignCanvas.Height = h;

        Canvas.SetLeft(CanvasResizeThumb, w - CanvasResizeThumb.Width);
        Canvas.SetTop(CanvasResizeThumb, h - CanvasResizeThumb.Height);

        Canvas.SetLeft(TxtCanvasSize, w - 90);
        Canvas.SetTop(TxtCanvasSize, h - 18);

        CanvasBorderRect.Width = w;
        CanvasBorderRect.Height = h;

        TxtCanvasSize.Text = $"{(int)w} × {(int)h}";
    }

    private void CanvasSize_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents) return;
        var w = NbCanvasW.Value;
        var h = NbCanvasH.Value;
        if (double.IsNaN(w) || w < 200) w = 600;
        if (double.IsNaN(h) || h < 100) h = 300;
        DesignCanvas.Width = w;
        DesignCanvas.Height = h;
        UpdateCanvasDecorations();
        SaveConfig();
    }

    private void Position_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveConfig();
        // If overlay is running, update position
        if (GameOverlayWindow.Instance != null)
        {
            GameOverlayWindow.Instance.SetPosition(GetSelectedPosition());
        }
    }

    private GameOverlayWindow.OverlayPosition GetSelectedPosition()
    {
        return CmbPosition.SelectedIndex switch
        {
            0 => GameOverlayWindow.OverlayPosition.TopLeft,
            1 => GameOverlayWindow.OverlayPosition.TopCenter,
            2 => GameOverlayWindow.OverlayPosition.TopRight,
            3 => GameOverlayWindow.OverlayPosition.MiddleLeft,
            4 => GameOverlayWindow.OverlayPosition.Center,
            5 => GameOverlayWindow.OverlayPosition.MiddleRight,
            6 => GameOverlayWindow.OverlayPosition.BottomLeft,
            7 => GameOverlayWindow.OverlayPosition.BottomCenter,
            8 => GameOverlayWindow.OverlayPosition.BottomRight,
            _ => GameOverlayWindow.OverlayPosition.TopLeft
        };
    }

    private void CanvasResize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double curW = double.IsNaN(DesignCanvas.Width) ? 600 : DesignCanvas.Width;
        double curH = double.IsNaN(DesignCanvas.Height) ? 300 : DesignCanvas.Height;
        var newW = Math.Clamp(curW + e.HorizontalChange, 200, 2000);
        var newH = Math.Clamp(curH + e.VerticalChange, 100, 1500);
        DesignCanvas.Width = newW;
        DesignCanvas.Height = newH;

        _suppressEvents = true;
        NbCanvasW.Value = newW;
        NbCanvasH.Value = newH;
        _suppressEvents = false;

        UpdateCanvasDecorations();
        SaveConfig();
    }

    #endregion

    #region Overall Scale

    private void Scale_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents) return;
        var val = NbScale.Value;
        if (double.IsNaN(val))
        {
            _suppressEvents = true;
            NbScale.Value = _scalePercent;
            _suppressEvents = false;
            return;
        }
        ApplyGlobalScale(Math.Clamp(val, 50, 200));
    }

    /// <summary>
    /// Scales the whole overlay layout (widget positions/sizes/fonts AND canvas) by the
    /// ratio between the new and the previous scale percent. Values are baked in, so the
    /// designer canvas, property panel and the running overlay stay WYSIWYG-consistent.
    /// </summary>
    private void ApplyGlobalScale(double newPercent)
    {
        if (newPercent == _scalePercent) return;
        double ratio = newPercent / _scalePercent;

        foreach (var w in _widgets)
        {
            w.X *= ratio;
            w.Y *= ratio;
            w.Width = Math.Max(20, w.Width * ratio);
            w.Height = Math.Max(12, w.Height * ratio);
            w.FontSize = Math.Max(8, w.FontSize * ratio);
        }

        double cw = Math.Clamp(NbCanvasW.Value * ratio, 200, 2000);
        double ch = Math.Clamp(NbCanvasH.Value * ratio, 100, 1500);
        _suppressEvents = true;
        NbCanvasW.Value = cw;
        NbCanvasH.Value = ch;
        _suppressEvents = false;
        DesignCanvas.Width = cw;
        DesignCanvas.Height = ch;
        UpdateCanvasDecorations();

        // Rebuild canvas elements so the preview matches the new scale
        foreach (var w in _widgets)
        {
            if (w.Container != null) DesignCanvas.Children.Remove(w.Container);
            if (w.ResizeThumb != null) DesignCanvas.Children.Remove(w.ResizeThumb);
        }
        var selected = _selectedWidget;
        foreach (var w in _widgets) CreateWidgetElement(w);
        if (selected != null) SelectWidget(selected);

        _scalePercent = newPercent;
        SaveConfig();

        if (_overlayRunning)
            TxtStatus.Text = $"已整体缩放到 {newPercent:F0}%，重新启动覆盖层即可生效";
        else
            TxtStatus.Text = $"已整体缩放到 {newPercent:F0}%";
    }

    #endregion

    #region Background Opacity

    private void BgOpacity_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var val = SliderBgOpacity.Value;
        TxtBgOpacity.Text = $"{val:F0}%";
        if (GameOverlayWindow.Instance != null)
            GameOverlayWindow.Instance.SetBackgroundOpacity((float)(val / 100));
        SaveConfig();
    }

    private void OledProtection_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.Set(SettingsPrefix + "OledProtection", TglOled.IsOn ? "true" : "false");
        GameOverlayWindow.Instance?.SetOledProtection(TglOled.IsOn);
    }

    #endregion

    #region Game Window Scanning

    private void ScanWindows_Click(object sender, RoutedEventArgs e)
    {
        ScanGameWindows();
    }

    private void ScanGameWindows()
    {
        // Remember the current selection so a re-scan doesn't lose it (match by title)
        GameWindowInfo? prevSelection = null;
        if (CmbGameWindow.SelectedItem is ComboBoxItem prevItem && prevItem.Tag is GameWindowInfo prevInfo)
            prevSelection = prevInfo;

        _gameWindows.Clear();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            int titleLen = GetWindowTextLengthW(hwnd);
            if (titleLen == 0) return true;

            var sb = new StringBuilder(titleLen + 1);
            GetWindowTextW(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            GetWindowThreadProcessId(hwnd, out var pid);
            string procName = "";
            try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }

            // Filter out system/irrelevant windows
            if (IsSystemProcess(procName)) return true;

            _gameWindows.Add(new GameWindowInfo
            {
                Hwnd = hwnd,
                Title = title,
                ProcessName = procName,
                Pid = pid
            });
            return true;
        }, IntPtr.Zero);

        // Add custom windows
        foreach (var customTitle in _customWindowTitles)
        {
            if (_gameWindows.Any(w => w.Title == customTitle)) continue;
            var found = _gameWindows.FirstOrDefault(w => w.Title.Contains(customTitle, StringComparison.OrdinalIgnoreCase));
            if (found == null)
            {
                _gameWindows.Add(new GameWindowInfo
                {
                    Title = customTitle,
                    ProcessName = "(自定义)",
                    IsCustom = true
                });
            }
        }

        // Populate ComboBox — desktop entry first, then scanned windows
        CmbGameWindow.Items.Clear();
        CmbGameWindow.Items.Add(new ComboBoxItem
        {
            Content = "🖥️ Windows 桌面（FPS 跟随活动窗口）",
            Tag = new GameWindowInfo
            {
                Hwnd = IntPtr.Zero,
                Title = "(桌面)",
                ProcessName = "Windows 桌面",
                IsDesktop = true
            }
        });
        foreach (var w in _gameWindows.OrderBy(w => w.Title))
        {
            var item = new ComboBoxItem
            {
                Content = $"{w.ProcessName} — {TruncateTitle(w.Title, 30)}",
                Tag = w
            };
            CmbGameWindow.Items.Add(item);
        }

        // Restore selection: keep the previously picked window if it still exists,
        // otherwise fall back to the saved target (desktop or last game window).
        if (prevSelection is { IsDesktop: true })
        {
            CmbGameWindow.SelectedIndex = 0;
        }
        else if (prevSelection != null)
        {
            var idx = -1;
            for (int i = 1; i < CmbGameWindow.Items.Count; i++)
            {
                if (CmbGameWindow.Items[i] is ComboBoxItem it && it.Tag is GameWindowInfo g
                    && !g.IsDesktop && g.Title == prevSelection.Title && g.ProcessName == prevSelection.ProcessName)
                {
                    idx = i;
                    break;
                }
            }
            if (idx >= 0) CmbGameWindow.SelectedIndex = idx;
            else RestoreSavedTarget();
        }
        else
        {
            RestoreSavedTarget();
        }

        TxtWindowStatus.Text = $"已扫描到 {_gameWindows.Count} 个窗口，以及桌面目标";
    }

    private void RestoreSavedTarget()
    {
        if (AppSettings.GetInt(SettingsPrefix + "DesktopTarget", 0) == 1)
        {
            CmbGameWindow.SelectedIndex = 0;
        }
        else
        {
            var savedIdx = AppSettings.GetInt(SettingsPrefix + "SelectedWindow", -1);
            if (savedIdx > 0 && savedIdx < CmbGameWindow.Items.Count)
                CmbGameWindow.SelectedIndex = savedIdx;
        }
    }

    private static string TruncateTitle(string title, int maxLen)
    {
        return title.Length > maxLen ? title[..maxLen] + "..." : title;
    }

    private static bool IsSystemProcess(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        ReadOnlySpan<string> excluded = [
            "dwm", "explorer", "svchost", "csrss", "smss", "lsass", "wininit",
            "services", "winlogon", "fontdrvhost", "dllhost", "conhost", "Taskmgr",
            "System", "Idle", "SearchHost", "ShellExperienceHost", "RuntimeBroker",
            "ApplicationFrameHost", "StartMenuExperienceHost", "sihost", "taskhostw",
            "ctfmon", "TubaWinUi3", "MSBuild", "devenv"
        ];
        foreach (var ex in excluded)
            if (name.Equals(ex, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void GameWindow_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CmbGameWindow.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not GameWindowInfo info) return;

        _targetHwnd = info.Hwnd;
        _isDesktopTarget = info.IsDesktop;
        TxtWindowStatus.Text = _isDesktopTarget
            ? "已选择: Windows 桌面 — 覆盖层固定于屏幕设置位置，FPS 显示当前活动窗口"
            : $"已选择: {info.ProcessName}";
        BtnRemoveCustom.Visibility = info.IsCustom ? Visibility.Visible : Visibility.Collapsed;

        if (_overlayRunning && GameOverlayWindow.Instance != null)
        {
            GameOverlayWindow.Instance.SetTargetWindow(_targetHwnd);
            GameOverlayWindow.Instance.SetDesktopMode(_isDesktopTarget);
        }
        SaveConfig();
    }

    private void AddCustomWindow_Click(object sender, RoutedEventArgs e)
    {
        // Show a dialog to add a custom window by title
        ShowAddCustomWindowDialog();
    }

    private async void ShowAddCustomWindowDialog()
    {
        var inputBox = new TextBox { PlaceholderText = "输入窗口标题关键字...", Width = 300 };
        var dialog = new ContentDialog
        {
            Title = "添加自定义游戏窗口",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "输入游戏窗口标题（支持部分匹配）:", FontSize = 13 },
                    inputBox
                }
            },
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
        {
            var title = inputBox.Text.Trim();
            if (!_customWindowTitles.Contains(title, StringComparer.OrdinalIgnoreCase))
            {
                _customWindowTitles.Add(title);
                ScanGameWindows();
                SaveConfig();
            }
        }
    }

    private void RemoveCustomWindow_Click(object sender, RoutedEventArgs e)
    {
        if (CmbGameWindow.SelectedItem is ComboBoxItem item && item.Tag is GameWindowInfo info && info.IsCustom)
        {
            _customWindowTitles.Remove(info.Title);
            ScanGameWindows();
            SaveConfig();
        }
    }

    #endregion

    #region Overlay Toggle

    private void ToggleOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayRunning)
            StopOverlay();
        else
            StartOverlay();
    }

    private void StartOverlay()
    {
        if (_widgets.Count == 0)
        {
            TxtStatus.Text = "请先拖入至少一个组件";
            return;
        }

        var overlayWidgets = _widgets.Select(w => new GameOverlayWindow.WidgetInstance
        {
            Type = w.Type,
            X = (int)w.X,
            Y = (int)w.Y,
            Width = (int)w.Width,
            Height = (int)w.Height,
            FontSize = (int)w.FontSize,
            Prefix = w.Prefix,
            ShowPrefix = w.ShowPrefix,
            Layer = w.Layer,
            IsChart = w.IsChart,
            CustomText = w.CustomText,
            ImagePath = w.ImagePath,
            ColorArgb = w.ColorArgb,
            TextColorArgb = w.TextColorArgb
        }).ToList();

        int cw = (int)Math.Max(200, NbCanvasW.Value);
        int ch = (int)Math.Max(100, NbCanvasH.Value);

        GameOverlayWindow.ShowOverlay(
            _targetHwnd,
            overlayWidgets,
            (float)(SliderBgOpacity.Value / 100),
            GetSelectedPosition(),
            cw, ch,
            _isDesktopTarget,
            oledProtection: AppSettings.Get(SettingsPrefix + "OledProtection") == "true"
        );

        StartPolling();
        // 覆盖层已经在采样，记录复用同一批采样即可，不必再起一个定时器
        if (_recording) StopRecordTimer();
        _overlayRunning = true;
        ToggleOverlayIcon.Glyph = "\uE71A"; // Stop icon
        ToggleOverlayText.Text = "停止覆盖层";
        TxtStatus.Text = "覆盖层运行中";
    }

    private void StopOverlay()
    {
        bool wasRunning = _overlayRunning;
        StopPolling();
        GameOverlayWindow.CloseOverlay();
        // 覆盖层停了，记录若还在进行则自己接管采样
        if (wasRunning && _recording) StartRecordTimer();
        _overlayRunning = false;
        ToggleOverlayIcon.Glyph = "\uE768"; // Play icon
        ToggleOverlayText.Text = "启动覆盖层";
        TxtStatus.Text = "已停止";
    }

    #endregion

    #region Data Polling

    private void StartPolling()
    {
        StopPolling();
        var interval = Math.Max(200, (int)NbRefresh.Value);
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        if (_pollTimer != null)
        {
            _pollTimer.Tick -= OnPollTick;
            _pollTimer.Stop();
            _pollTimer = null;
        }
    }

    private async void OnPollTick(object? sender, object e)
    {
        // 采样（LHM 全硬件树 + WMI + FPS 统计）可能超过 1s：首次 LHM 初始化可阻塞数秒、
        // 拷机时硬件读取会变慢。上一轮未完成就跳过本 tick，避免 async void 重入叠加。
        if (_samplingInFlight) return;
        _samplingInFlight = true;
        try
        {
            // LHM/WMI 读取放到后台线程，UI 线程只负责把结果绘制到覆盖层
            var sample = await Task.Run(() => LiteMonitorService.Instance.Read(fpsEnabled: true));
            if (!_overlayRunning) return; // 覆盖层已停止，丢弃迟到的采样
            GameOverlayWindow.Instance?.UpdateData(sample);
            // 记录中：复用这次采样，不额外增加硬件读取开销
            if (_recording) AppendRecordSample(sample);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlay] 采样失败: {ex.Message}");
        }
        finally
        {
            _samplingInFlight = false;
        }
    }

    #endregion

    #region Data Recording

    // 设计取舍（性能优先）：
    //  · 记录期间采样只追加到内存 List，绝不落盘，游戏帧率不受影响；
    //  · 结束（手动停止 / 离开页面 / 达到 2 小时上限）时一次性写出 JSON / Markdown；
    //  · 单次记录硬上限 2 小时，到点自动停止并保存，内存占用有界（≈3.6 万条 @200ms）。

    private void InitRecordMetrics()
    {
        RecordMetricPanel.Children.Clear();
        RecordMetricPanel.ColumnDefinitions.Clear();
        RecordMetricPanel.RowDefinitions.Clear();
        RecordMetricPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RecordMetricPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _recordChecks.Clear();

        var selectedKeys = GameMonitorRecorder.ParseSelection(AppSettings.Get(RecordMetricsSetting))
            .Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int row = 0;
        foreach (var group in GameMonitorRecorder.AllMetrics.GroupBy(m => m.Group))
        {
            EnsureRecordRow(row);
            var header = new TextBlock
            {
                Text = group.Key,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Opacity = 0.75,
                Margin = new Thickness(0, 4, 0, 2)
            };
            Grid.SetRow(header, row);
            Grid.SetColumn(header, 0);
            Grid.SetColumnSpan(header, 2);
            RecordMetricPanel.Children.Add(header);
            row++;

            int col = 0;
            foreach (var m in group)
            {
                EnsureRecordRow(row);
                var cb = new CheckBox
                {
                    Content = m.Label,
                    FontSize = 11,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                    Tag = m.Key,
                    IsChecked = selectedKeys.Contains(m.Key)
                };
                ToolTipService.SetToolTip(cb, string.IsNullOrEmpty(m.Unit) ? m.Label : $"{m.Label}（{m.Unit}）");
                cb.Checked += RecordMetric_Changed;
                cb.Unchecked += RecordMetric_Changed;
                Grid.SetRow(cb, row);
                Grid.SetColumn(cb, col);
                RecordMetricPanel.Children.Add(cb);
                _recordChecks[m.Key] = cb;
                col++;
                if (col == 2) { col = 0; row++; }
            }
            if (col != 0) row++; // 该组占半行，下一组另起一行
        }
        UpdateRecordMetricCount();
    }

    private void EnsureRecordRow(int row)
    {
        while (RecordMetricPanel.RowDefinitions.Count <= row)
            RecordMetricPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }

    private void LoadRecordSettings()
    {
        var outputs = GameMonitorRecorder.ParseOutputs(AppSettings.Get(RecordOutputsSetting));
        _suppressRecordMetricEvents = true;
        ChkOutJson.IsChecked = outputs.HasFlag(MonitorRecordOutput.Json);
        ChkOutMarkdown.IsChecked = outputs.HasFlag(MonitorRecordOutput.Markdown);
        ChkOutCsv.IsChecked = outputs.HasFlag(MonitorRecordOutput.Csv);
        _suppressRecordMetricEvents = false;
        _recordOutputs = outputs;
        _recordUiReady = true;
        UpdateRecordDirText();
        UpdateRecordButton();
        UpdateRecordStatus();
    }

    /// <summary>导出格式勾选框变化（多选，至少保留一种）。</summary>
    private void RecordOutput_Changed(object sender, RoutedEventArgs e)
    {
        if (!_recordUiReady || _suppressRecordMetricEvents) return;
        var outputs = GetSelectedOutputs();
        if (outputs == MonitorRecordOutput.None)
        {
            // 不允许全不选：把刚取消的那个恢复回来
            if (sender is CheckBox cb)
            {
                _suppressRecordMetricEvents = true;
                cb.IsChecked = true;
                _suppressRecordMetricEvents = false;
            }
            TxtRecordStatus.Text = "至少要选择一种导出格式";
            return;
        }
        _recordOutputs = outputs;
        AppSettings.Set(RecordOutputsSetting, GameMonitorRecorder.SerializeOutputs(outputs));
    }

    private MonitorRecordOutput GetSelectedOutputs()
    {
        var outputs = MonitorRecordOutput.None;
        if (ChkOutJson.IsChecked == true) outputs |= MonitorRecordOutput.Json;
        if (ChkOutMarkdown.IsChecked == true) outputs |= MonitorRecordOutput.Markdown;
        if (ChkOutCsv.IsChecked == true) outputs |= MonitorRecordOutput.Csv;
        return outputs;
    }

    private void RecordMetric_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressRecordMetricEvents) return;
        UpdateRecordMetricCount();
        AppSettings.Set(RecordMetricsSetting, GameMonitorRecorder.SerializeSelection(GetSelectedRecordKeys()));
    }

    private List<string> GetSelectedRecordKeys() =>
        _recordChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

    private List<MonitorRecordMetric> GetSelectedRecordMetrics() =>
        GameMonitorRecorder.AllMetrics
            .Where(m => _recordChecks.TryGetValue(m.Key, out var cb) && cb.IsChecked == true)
            .ToList();

    private void SetRecordMetricsEnabled(bool enabled)
    {
        // Panel 在 WinUI 里没有公开的 IsEnabled，逐个勾选框控制
        foreach (var cb in _recordChecks.Values) cb.IsEnabled = enabled;
        ChkOutJson.IsEnabled = enabled;
        ChkOutMarkdown.IsEnabled = enabled;
        ChkOutCsv.IsEnabled = enabled;
    }

    private void UpdateRecordMetricCount() => TxtRecordMetricCount.Text = $"已选 {GetSelectedRecordKeys().Count} 项";

    private void RecordSelectAll_Click(object sender, RoutedEventArgs e) => SetAllRecordMetrics(true);

    private void RecordClearAll_Click(object sender, RoutedEventArgs e) => SetAllRecordMetrics(false);

    private void SetAllRecordMetrics(bool isChecked)
    {
        _suppressRecordMetricEvents = true;
        foreach (var cb in _recordChecks.Values) cb.IsChecked = isChecked;
        _suppressRecordMetricEvents = false;
        UpdateRecordMetricCount();
        AppSettings.Set(RecordMetricsSetting, GameMonitorRecorder.SerializeSelection(GetSelectedRecordKeys()));
    }

    /// <summary>刷新间隔变化：让正在跑的采样定时器立即生效（不重启覆盖层/记录）。</summary>
    private void RefreshInterval_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents) return;
        var interval = Math.Max(200, (int)NbRefresh.Value);
        if (_overlayRunning) StartPolling();
        if (_recording && !_overlayRunning)
        {
            StartRecordTimer();
            _recordIntervalMs = interval;
        }
        SaveConfig();
    }

    private string GetRecordDir() => GameMonitorRecorder.GetOutputDir();

    private void UpdateRecordDirText()
    {
        var dir = GetRecordDir();
        TxtRecordDir.Text = "输出目录：" + dir;
        BtnOpenRecordDir.IsEnabled = Directory.Exists(dir);
    }

    private async void PickRecordDir_Click(object sender, RoutedEventArgs e)
    {
        if (_recording)
        {
            TxtRecordStatus.Text = "记录进行中，无法修改输出目录";
            return;
        }
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null && !string.IsNullOrWhiteSpace(folder.Path))
            {
                AppSettings.Set(GameMonitorRecorder.RecordDirSetting, folder.Path);
                UpdateRecordDirText();
            }
        }
        catch (Exception ex)
        {
            TxtRecordStatus.Text = $"选择输出目录失败: {ex.Message}";
        }
    }

    private void OpenRecordDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = string.IsNullOrEmpty(_lastRecordDir) ? GetRecordDir() : _lastRecordDir;
        try
        {
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            else
                TxtRecordStatus.Text = "输出目录还不存在，先完成一次记录";
        }
        catch (Exception ex)
        {
            TxtRecordStatus.Text = $"打开目录失败: {ex.Message}";
        }
    }

    private void ViewRecords_Click(object sender, RoutedEventArgs e) => OpenRecordsViewer(null);

    /// <summary>
    /// 「记录设置」弹窗：把指标勾选/导出格式/输出目录整块（RecordSettingsPanel）
    /// 搬进 ContentDialog —— 控件的 x:Name 与事件绑定不变，主界面保持精简。
    /// 关闭后放回隐藏宿主（RecordSettingsHost），下次打开再搬。
    /// </summary>
    private async void RecordSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_recordDialogOpen) return;
        _recordDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "记录设置",
                Content = new ScrollViewer { Content = RecordSettingsPanel, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                CloseButtonText = "完成",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlay] 记录设置弹窗失败: {ex.Message}");
        }
        finally
        {
            _recordDialogOpen = false;
            // 弹窗关闭后把控件收回隐藏宿主：控件不能留在已关闭的对话框视觉树里
            RecordSettingsHost.Children.Clear();
            RecordSettingsHost.Children.Add(RecordSettingsPanel);
        }
    }

    /// <summary>打开「记录查看」窗口（独立窗口，不影响正在进行的覆盖层与记录）。</summary>
    private static void OpenRecordsViewer(string? file)
    {
        try
        {
            BuiltinToolWindow.Show(typeof(GameMonitorRecordsPage), file, "游戏监控 · 记录查看");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlay] 打开记录查看失败: {ex.Message}");
        }
    }

    private async void ToggleRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_recording) await StopRecordingAsync(autoStop: false);
        else StartRecording();
    }

    private void StartRecording()
    {
        var metrics = GetSelectedRecordMetrics();
        if (metrics.Count == 0)
        {
            TxtRecordStatus.Text = "请至少勾选一项要记录的指标";
            return;
        }
        var outputs = GetSelectedOutputs();
        if (outputs == MonitorRecordOutput.None)
        {
            TxtRecordStatus.Text = "请至少选择一种导出格式";
            return;
        }

        _recordMetrics = metrics;
        _recordOutputs = outputs;
        _recordIntervalMs = Math.Max(200, (int)NbRefresh.Value);
        var target = (CmbGameWindow.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(target)) target = TxtWindowStatus.Text;
        _session.Start(metrics, outputs, (int)_recordIntervalMs, target);
        _recording = true;

        // 覆盖层运行时已在自己的轮询里采样，复用它；否则记录器自己起定时器
        if (_overlayRunning) StopRecordTimer();
        else StartRecordTimer();

        SetRecordMetricsEnabled(false);
        UpdateRecordButton();
        UpdateRecordStatus();
    }

    private async Task StopRecordingAsync(bool autoStop)
    {
        if (!_recording) return;
        _recording = false;
        StopRecordTimer();
        SetRecordMetricsEnabled(true);
        UpdateRecordButton();

        TxtRecordStatus.Text = "正在写入文件…";
        RecordResult? result;
        try
        {
            // 写盘放线程池，避免样本多时阻塞 UI
            result = await Task.Run(() => _session.Stop());
        }
        catch (Exception ex)
        {
            TxtRecordStatus.Text = $"写入失败: {ex.Message}";
            return;
        }

        if (result is null)
        {
            TxtRecordStatus.Text = "没有采集到数据，未生成文件";
            BtnOpenRecordDir.IsEnabled = Directory.Exists(GetRecordDir());
            return;
        }

        _lastRecordDir = GetRecordDir();
        UpdateRecordDirText();
        var names = string.Join("、", result.Paths.Select(Path.GetFileName));
        var prefix = autoStop ? $"已达 {GameMonitorRecorder.MaxDurationMinutes} 分钟上限，已自动停止并保存：" : "已保存：";
        TxtRecordStatus.Text = prefix + names + TrimNote(result.HeadTrim, result.TailTrim);
        await ShowRecordSavedDialogAsync(result, autoStop);
    }

    /// <summary>保存完成后的结果弹窗：文件清单 + 关键统计，一键打开输出文件夹。</summary>
    private async Task ShowRecordSavedDialogAsync(RecordResult result, bool autoStop)
    {
        var (paths, meta, samples, headTrim, tailTrim) = result;
        var metrics = _recordMetrics; // 与样本列一一对应
        if (_recordDialogOpen || XamlRoot is null) return;
        _recordDialogOpen = true;
        try
        {
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(new TextBlock
            {
                Text = autoStop
                    ? $"记录已达 {GameMonitorRecorder.MaxDurationMinutes} 分钟上限，已自动停止并保存："
                    : "记录已保存：",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            });
            foreach (var p in paths)
            {
                body.Children.Add(new TextBlock
                {
                    Text = "· " + Path.GetFileName(p),
                    FontSize = 12,
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var stats = new StringBuilder();
            stats.AppendLine($"记录时长：{GameMonitorRecorder.FormatDuration(meta.DurationSeconds)}");
            stats.AppendLine($"采样点数：{samples.Count}");
            var trimNote = TrimNote(headTrim, tailTrim);
            if (trimNote.Length > 0) stats.AppendLine(trimNote.Trim('（', '）'));
            var fpsIndex = metrics.FindIndex(m => m.Key == "fps");
            if (fpsIndex >= 0)
            {
                var fps = GameMonitorRecorder.Collect(metrics, samples, fpsIndex);
                if (fps.Count > 0)
                {
                    stats.AppendLine(
                        $"FPS：平均 {GameMonitorRecorder.FormatValue(GameMonitorRecorder.Average(fps), "")}" +
                        $" / 最低 {GameMonitorRecorder.FormatValue(fps[0], "")}" +
                        $" / P1 {GameMonitorRecorder.FormatValue(GameMonitorRecorder.Percentile(fps, 1), "")}");
                }
            }
            body.Children.Add(new TextBlock
            {
                Text = stats.ToString().TrimEnd(),
                FontSize = 12,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap
            });
            body.Children.Add(new TextBlock
            {
                Text = GetRecordDir(),
                FontSize = 11,
                Opacity = 0.6,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap
            });

            var dialog = new ContentDialog
            {
                Title = "数据记录完成",
                Content = new ScrollViewer { Content = body, MaxHeight = 320 },
                PrimaryButtonText = "打开文件夹",
                CloseButtonText = "确定",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                OpenRecordDir_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlay] 结果弹窗显示失败: {ex.Message}");
        }
        finally
        {
            _recordDialogOpen = false;
        }
    }

    /// <summary>离开页面时同步落盘（此时已无法 await，宁可短暂阻塞也不能丢数据）。</summary>
    private void SaveRecordingOnUnload()
    {
        if (!_recording) return;
        _recording = false;
        StopRecordTimer();

        try
        {
            _session.Stop(); // 空会话（无有效采样）内部不会产生文件
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlay] 记录落盘失败: {ex.Message}");
        }
    }

    /// <summary>「已剔除首尾无效数据」的说明文案（没有剔除时返回空串）。</summary>
    private static string TrimNote(double headTrim, double tailTrim)
    {
        var parts = new List<string>();
        if (headTrim > 0.05) parts.Add($"开头 {headTrim:0.#} 秒");
        if (tailTrim > 0.05) parts.Add($"结尾 {tailTrim:0.#} 秒");
        return parts.Count == 0 ? "" : $"（已剔除{string.Join("、", parts)}的无效数据）";
    }

    private void StartRecordTimer()
    {
        StopRecordTimer();
        var interval = Math.Max(200, (int)NbRefresh.Value);
        _recordIntervalMs = interval;
        _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        _recordTimer.Tick += OnRecordTick;
        _recordTimer.Start();
    }

    private void StopRecordTimer()
    {
        if (_recordTimer is null) return;
        _recordTimer.Tick -= OnRecordTick;
        _recordTimer.Stop();
        _recordTimer = null;
    }

    private async void OnRecordTick(object? sender, object e)
    {
        if (_recordSamplingInFlight || !_recording) return;
        _recordSamplingInFlight = true;
        try
        {
            var needFps = GameMonitorRecorder.NeedsFps(_recordMetrics);
            var sample = await Task.Run(() => LiteMonitorService.Instance.Read(fpsEnabled: needFps));
            if (!_recording) return;
            AppendRecordSample(sample);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameOverlay] 记录采样失败: {ex.Message}");
        }
        finally
        {
            _recordSamplingInFlight = false;
        }
    }

    private void AppendRecordSample(MonitorSample sample)
    {
        if (_recordMetrics.Count == 0) return;
        _session.Append(sample);

        // 硬上限：到点立即停止并保存，避免长时间记录导致内存持续增长
        if (_session.ExceededMaxDuration)
        {
            _session.MarkTruncated();
            UpdateRecordStatus();
            _ = StopRecordingAsync(autoStop: true);
            return;
        }
        UpdateRecordStatus();
    }

    private void UpdateRecordButton()
    {
        RecordIcon.Glyph = _recording ? "\uE71A" : "\uE768";
        RecordText.Text = _recording ? "停止记录并保存" : "开始记录";
    }

    private void UpdateRecordStatus()
    {
        if (!_recording)
        {
            if (string.IsNullOrEmpty(_lastRecordDir))
                TxtRecordStatus.Text = "未开始";
            return;
        }
        var elapsed = _session.Elapsed;
        var limit = TimeSpan.FromMinutes(GameMonitorRecorder.MaxDurationMinutes);
        var remaining = limit - elapsed;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        TxtRecordStatus.Text =
            $"记录中… 已采样 {_session.SampleCount} 条 ｜ 已用 {FormatClock(elapsed)} ｜ 剩余 {FormatClock(remaining)}";
    }

    private static string FormatClock(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    #endregion

    #region Layout Presets

    private readonly record struct PresetWidget(
        OverlayWidgetType Type, double X, double Y, double W, double H, double Fs, bool ShowPrefix);

    private sealed class PresetItem
    {
        public string Name = "";
        public bool IsBuiltin;
        public double CanvasW, CanvasH;
        public string LayoutJson = "";
    }

    private sealed class UserPresetData
    {
        public double cw { get; set; }
        public double ch { get; set; }
        public string layout { get; set; } = "";
    }

    private static readonly (string Name, double W, double H, PresetWidget[] Widgets)[] BuiltinPresets =
    [
        ("极简 FPS", 240, 116,
        [
            new PresetWidget(OverlayWidgetType.FpsText, 12, 12, 216, 44, 22, false),
            new PresetWidget(OverlayWidgetType.FpsChart, 12, 64, 216, 40, 14, true),
        ]),
        ("标准监控", 320, 180,
        [
            new PresetWidget(OverlayWidgetType.FpsText, 12, 12, 140, 30, 14, true),
            new PresetWidget(OverlayWidgetType.MemLoadText, 160, 12, 148, 30, 14, true),
            new PresetWidget(OverlayWidgetType.CpuTempText, 12, 48, 140, 30, 14, true),
            new PresetWidget(OverlayWidgetType.GpuTempText, 160, 48, 148, 30, 14, true),
            new PresetWidget(OverlayWidgetType.CpuLoadText, 12, 84, 140, 30, 14, true),
            new PresetWidget(OverlayWidgetType.GpuLoadText, 160, 84, 148, 30, 14, true),
            new PresetWidget(OverlayWidgetType.FpsChart, 12, 122, 296, 46, 14, true),
        ]),
        ("性能全景", 340, 272,
        [
            new PresetWidget(OverlayWidgetType.FpsText, 12, 12, 150, 32, 15, true),
            new PresetWidget(OverlayWidgetType.MemUsedText, 170, 12, 158, 32, 14, true),
            new PresetWidget(OverlayWidgetType.CpuNameText, 12, 50, 316, 26, 12, true),
            new PresetWidget(OverlayWidgetType.CpuTempText, 12, 84, 150, 30, 13, true),
            new PresetWidget(OverlayWidgetType.CpuLoadText, 170, 84, 158, 30, 13, true),
            new PresetWidget(OverlayWidgetType.CpuClockText, 12, 120, 150, 30, 13, true),
            new PresetWidget(OverlayWidgetType.CpuPowerText, 170, 120, 158, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuTempText, 12, 156, 150, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuLoadText, 170, 156, 158, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuClockText, 12, 192, 150, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuPowerText, 170, 192, 158, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuVramText, 12, 228, 150, 30, 13, true),
        ]),
        ("CPU 专项", 300, 190,
        [
            new PresetWidget(OverlayWidgetType.CpuNameText, 12, 12, 276, 26, 12, true),
            new PresetWidget(OverlayWidgetType.CpuTempText, 12, 44, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.CpuLoadText, 156, 44, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.CpuClockText, 12, 80, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.CpuPowerText, 156, 80, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.CpuTempChart, 12, 118, 276, 60, 14, true),
        ]),
        ("GPU 专项", 300, 226,
        [
            new PresetWidget(OverlayWidgetType.GpuNameText, 12, 12, 276, 26, 12, true),
            new PresetWidget(OverlayWidgetType.GpuTempText, 12, 44, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuLoadText, 156, 44, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuClockText, 12, 80, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuPowerText, 156, 80, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuVramText, 12, 116, 276, 30, 13, true),
            new PresetWidget(OverlayWidgetType.FpsChart, 12, 154, 276, 60, 14, true),
        ]),
        ("网络与磁盘", 280, 130,
        [
            new PresetWidget(OverlayWidgetType.NetUpText, 12, 12, 256, 30, 13, true),
            new PresetWidget(OverlayWidgetType.NetDownText, 12, 48, 256, 30, 13, true),
            new PresetWidget(OverlayWidgetType.DiskReadText, 12, 84, 124, 30, 13, true),
            new PresetWidget(OverlayWidgetType.DiskWriteText, 144, 84, 124, 30, 13, true),
        ]),
        ("全功能", 360, 414,
        [
            new PresetWidget(OverlayWidgetType.FpsText, 12, 12, 150, 32, 15, true),
            new PresetWidget(OverlayWidgetType.MemUsedText, 170, 12, 178, 32, 14, true),
            new PresetWidget(OverlayWidgetType.CpuNameText, 12, 50, 336, 26, 12, true),
            new PresetWidget(OverlayWidgetType.CpuTempText, 12, 84, 162, 30, 13, true),
            new PresetWidget(OverlayWidgetType.CpuLoadText, 186, 84, 162, 30, 13, true),
            new PresetWidget(OverlayWidgetType.CpuClockText, 12, 120, 162, 30, 13, true),
            new PresetWidget(OverlayWidgetType.CpuPowerText, 186, 120, 162, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuNameText, 12, 156, 336, 26, 12, true),
            new PresetWidget(OverlayWidgetType.GpuTempText, 12, 190, 162, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuLoadText, 186, 190, 162, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuClockText, 12, 226, 162, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuPowerText, 186, 226, 162, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuVramText, 12, 262, 162, 30, 13, true),
            new PresetWidget(OverlayWidgetType.MemLoadText, 186, 262, 162, 30, 13, true),
            new PresetWidget(OverlayWidgetType.FpsChart, 12, 300, 336, 48, 14, true),
            new PresetWidget(OverlayWidgetType.CpuTempChart, 12, 354, 336, 48, 14, true),
        ]),
        ("电竞对战", 300, 196,
        [
            new PresetWidget(OverlayWidgetType.FpsText, 12, 12, 276, 34, 20, false),
            new PresetWidget(OverlayWidgetType.FpsChart, 12, 54, 276, 56, 14, true),
            new PresetWidget(OverlayWidgetType.GpuTempText, 12, 118, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuLoadText, 156, 118, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuClockText, 12, 154, 132, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuVramText, 156, 154, 132, 30, 13, true),
        ]),
        ("双图表", 300, 190,
        [
            new PresetWidget(OverlayWidgetType.FpsText, 12, 12, 132, 30, 14, true),
            new PresetWidget(OverlayWidgetType.CpuTempText, 156, 12, 132, 30, 14, true),
            new PresetWidget(OverlayWidgetType.FpsChart, 12, 48, 276, 60, 14, true),
            new PresetWidget(OverlayWidgetType.CpuTempChart, 12, 116, 276, 60, 14, true),
        ]),
        ("内存专项", 280, 130,
        [
            new PresetWidget(OverlayWidgetType.FpsText, 12, 12, 124, 30, 14, true),
            new PresetWidget(OverlayWidgetType.MemLoadText, 144, 12, 124, 30, 14, true),
            new PresetWidget(OverlayWidgetType.MemUsedText, 12, 48, 256, 30, 14, true),
            new PresetWidget(OverlayWidgetType.CpuLoadText, 12, 84, 124, 30, 13, true),
            new PresetWidget(OverlayWidgetType.GpuLoadText, 144, 84, 124, 30, 13, true),
        ]),
        ("FPS 直播监控", 240, 170,
        [
            new PresetWidget(OverlayWidgetType.FpsText, 12, 12, 216, 36, 20, false),
            new PresetWidget(OverlayWidgetType.FpsChart, 12, 56, 216, 56, 14, true),
            new PresetWidget(OverlayWidgetType.NetUpText, 12, 120, 104, 30, 13, true),
            new PresetWidget(OverlayWidgetType.NetDownText, 124, 120, 104, 30, 13, true),
        ]),
    ];

    private static string BuildLayoutJson(IEnumerable<PresetWidget> widgets)
    {
        var arr = widgets.Select(pw => new
        {
            type = (int)pw.Type,
            x = pw.X,
            y = pw.Y,
            w = pw.W,
            h = pw.H,
            fs = pw.Fs,
            prefix = GameOverlayWindow.GetDefaultPrefix(pw.Type),
            showPrefix = pw.ShowPrefix,
            layer = 0,
            text = "",
            img = "",
            color = 0xFF00A0FFu,
            tcolor = 0xFFFFFFFFu
        }).ToList();
        return JsonSerializer.Serialize(arr);
    }

    private Dictionary<string, UserPresetData> LoadUserPresets()
    {
        try
        {
            var json = AppSettings.Get(SettingsPrefix + "Presets");
            if (!string.IsNullOrEmpty(json))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, UserPresetData>>(json);
                if (dict != null) return dict;
            }
        }
        catch { }
        return new Dictionary<string, UserPresetData>();
    }

    private List<PresetItem> GetAllPresets()
    {
        var list = new List<PresetItem>();
        foreach (var (name, w, h, widgets) in BuiltinPresets)
        {
            list.Add(new PresetItem
            {
                Name = name,
                IsBuiltin = true,
                CanvasW = w,
                CanvasH = h,
                LayoutJson = BuildLayoutJson(widgets)
            });
        }

        foreach (var kv in LoadUserPresets())
        {
            if (string.IsNullOrEmpty(kv.Key)) continue;
            list.Add(new PresetItem
            {
                Name = kv.Key,
                IsBuiltin = false,
                CanvasW = kv.Value.cw > 0 ? kv.Value.cw : 600,
                CanvasH = kv.Value.ch > 0 ? kv.Value.ch : 300,
                LayoutJson = kv.Value.layout ?? ""
            });
        }
        return list;
    }

    private void RefreshPresetCombo()
    {
        _suppressEvents = true;
        var selectedName = (CmbPreset.SelectedItem as ComboBoxItem)?.Tag as PresetItem;
        CmbPreset.Items.Clear();
        foreach (var p in GetAllPresets())
        {
            var item = new ComboBoxItem { Content = (p.IsBuiltin ? "内置 · " : "") + p.Name, Tag = p };
            CmbPreset.Items.Add(item);
            if (selectedName != null && !selectedName.IsBuiltin && p.Name == selectedName.Name && !p.IsBuiltin)
                CmbPreset.SelectedItem = item;
        }
        if (CmbPreset.SelectedIndex < 0 && CmbPreset.Items.Count > 0)
            CmbPreset.SelectedIndex = 0;
        UpdateDeletePresetButton();
        _suppressEvents = false;
    }

    private void UpdateDeletePresetButton()
    {
        BtnDeletePreset.IsEnabled = CmbPreset.SelectedItem is ComboBoxItem ci
            && ci.Tag is PresetItem pi && !pi.IsBuiltin;
    }

    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        UpdateDeletePresetButton();
    }

    private async void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (CmbPreset.SelectedItem is not ComboBoxItem ci || ci.Tag is not PresetItem preset) return;

        if (_widgets.Count > 0)
        {
            var confirm = new ContentDialog
            {
                Title = "应用预设",
                Content = $"应用预设「{preset.Name}」将替换当前画布上的 {_widgets.Count} 个组件，确定继续吗？",
                PrimaryButtonText = "应用",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        }

        ApplyPreset(preset);
    }

    private void ApplyPreset(PresetItem preset)
    {
        StopOverlay();
        ClearAllWidgets();

        // Preset layouts are defined at 100% scale — reset the scale control
        _scalePercent = 100;
        _suppressEvents = true;
        NbScale.Value = 100;
        NbCanvasW.Value = Math.Max(200, preset.CanvasW);
        NbCanvasH.Value = Math.Max(100, preset.CanvasH);
        _suppressEvents = false;
        DesignCanvas.Width = NbCanvasW.Value;
        DesignCanvas.Height = NbCanvasH.Value;
        UpdateCanvasDecorations();

        try { LoadWidgetsFromJson(preset.LayoutJson); }
        catch (Exception ex) { TxtStatus.Text = $"预设加载失败: {ex.Message}"; }

        UpdateStatus();
        SaveConfig();
        TxtStatus.Text = $"已应用预设「{preset.Name}」，重新启动覆盖层即可生效";
    }

    private void ClearAllWidgets()
    {
        SelectWidget(null);
        foreach (var w in _widgets)
        {
            if (w.Container != null) DesignCanvas.Children.Remove(w.Container);
            if (w.ResizeThumb != null) DesignCanvas.Children.Remove(w.ResizeThumb);
        }
        _widgets.Clear();
    }

    private static string SuggestPresetName(IReadOnlyCollection<string> existing)
    {
        for (int i = 1; ; i++)
        {
            var n = $"自定义预设 {i}";
            if (!existing.Contains(n)) return n;
        }
    }

    private async void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_widgets.Count == 0)
        {
            TxtStatus.Text = "画布为空，无法保存预设";
            return;
        }

        var existingNames = GetAllPresets().Where(p => !p.IsBuiltin).Select(p => p.Name).ToList();
        var input = new TextBox { PlaceholderText = "输入预设名称...", Text = SuggestPresetName(existingNames) };

        var dialog = new ContentDialog
        {
            Title = "保存当前布局为预设",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "将保存画布尺寸与全部组件布局，同名预设会被覆盖：", FontSize = 13, TextWrapping = TextWrapping.Wrap },
                    input
                }
            },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var name = input.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        try
        {
            var dict = LoadUserPresets();
            dict[name] = new UserPresetData
            {
                cw = NbCanvasW.Value,
                ch = NbCanvasH.Value,
                layout = SerializeLayout()
            };
            AppSettings.Set(SettingsPrefix + "Presets", JsonSerializer.Serialize(dict));
            RefreshPresetCombo();

            foreach (var item in CmbPreset.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is PresetItem pi && !pi.IsBuiltin && pi.Name == name)
                {
                    _suppressEvents = true;
                    CmbPreset.SelectedItem = item;
                    _suppressEvents = false;
                    UpdateDeletePresetButton();
                    break;
                }
            }
            TxtStatus.Text = $"已保存预设「{name}」";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"保存预设失败: {ex.Message}";
        }
    }

    private async void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (CmbPreset.SelectedItem is not ComboBoxItem ci || ci.Tag is not PresetItem preset || preset.IsBuiltin) return;

        var confirm = new ContentDialog
        {
            Title = "删除预设",
            Content = $"确定删除预设「{preset.Name}」吗？此操作不可恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var dict = LoadUserPresets();
        if (dict.Remove(preset.Name))
        {
            AppSettings.Set(SettingsPrefix + "Presets", JsonSerializer.Serialize(dict));
            RefreshPresetCombo();
            TxtStatus.Text = $"已删除预设「{preset.Name}」";
        }
    }

    #endregion

    #region Config Persistence

    private void SaveConfig()
    {
        // LoadConfig 完成前 _widgets 还是空的，此时落盘会把已保存的布局覆盖成 "[]"
        if (!_configLoaded) return;
        try
        {
            // NaN 防护：NumberBox 未初始化/清空时 Value 可能为 NaN，一旦写成 "NaN"，
            // 下次 LoadConfig 读到 NaN 会让布局加载半途而废（画布空白）。
            double canvasW = double.IsNaN(NbCanvasW.Value) ? 600 : NbCanvasW.Value;
            double canvasH = double.IsNaN(NbCanvasH.Value) ? 300 : NbCanvasH.Value;
            double refresh = double.IsNaN(NbRefresh.Value) ? 1000 : NbRefresh.Value;
            AppSettings.Set(SettingsPrefix + "CanvasW", canvasW);
            AppSettings.Set(SettingsPrefix + "CanvasH", canvasH);
            AppSettings.Set(SettingsPrefix + "Position", CmbPosition.SelectedIndex);
            AppSettings.Set(SettingsPrefix + "Refresh", refresh);
            AppSettings.Set(SettingsPrefix + "BgOpacity", SliderBgOpacity.Value);
            AppSettings.Set(SettingsPrefix + "Scale", _scalePercent);
            AppSettings.Set(SettingsPrefix + "FontFamily", CmbFont.SelectedItem as string ?? "Microsoft YaHei UI");

            // Save widget layout as JSON
            AppSettings.Set(SettingsPrefix + "Layout", SerializeLayout());

            // Save custom windows
            AppSettings.Set(SettingsPrefix + "CustomWindows", string.Join("|", _customWindowTitles));

            // Save selected target: desktop flag + game window index
            AppSettings.Set(SettingsPrefix + "DesktopTarget", _isDesktopTarget ? 1 : 0);
            if (CmbGameWindow.SelectedIndex >= 0)
                AppSettings.Set(SettingsPrefix + "SelectedWindow", CmbGameWindow.SelectedIndex);
        }
        catch { }
    }

    private void LoadConfig()
    {
        try
        {
            _suppressEvents = true;
            _configLoaded = false;

            // NaN 防护：历史版本可能已把 NaN 写进配置（如 GameOverlay_Refresh=NaN），
            // 这里统一兜底，并在读取时把坏值修复回默认，避免下次再触发。
            double cw = AppSettings.GetDouble(SettingsPrefix + "CanvasW", 600);
            double chh = AppSettings.GetDouble(SettingsPrefix + "CanvasH", 300);
            if (double.IsNaN(cw) || cw < 200) cw = 600;
            if (double.IsNaN(chh) || chh < 100) chh = 300;
            NbCanvasW.Value = cw;
            NbCanvasH.Value = chh;
            DesignCanvas.Width = cw;
            DesignCanvas.Height = chh;
            UpdateCanvasDecorations();

            CmbPosition.SelectedIndex = Math.Clamp(AppSettings.GetInt(SettingsPrefix + "Position", 0), 0, 8);
            double refresh = AppSettings.GetDouble(SettingsPrefix + "Refresh", 1000);
            if (double.IsNaN(refresh) || refresh < 100) refresh = 1000;
            NbRefresh.Value = refresh;
            double bgOp = AppSettings.GetDouble(SettingsPrefix + "BgOpacity", 70);
            if (double.IsNaN(bgOp) || bgOp < 0 || bgOp > 100) bgOp = 70;
            SliderBgOpacity.Value = bgOp;
            TxtBgOpacity.Text = $"{SliderBgOpacity.Value:F0}%";
            TglOled.IsOn = AppSettings.Get(SettingsPrefix + "OledProtection") == "true";

            // Overall scale — the stored layout values already include the last applied scale
            double scale = AppSettings.GetDouble(SettingsPrefix + "Scale", 100);
            if (double.IsNaN(scale) || scale < 50 || scale > 200) scale = 100;
            _scalePercent = scale;
            NbScale.Value = scale;

            // Load font
            var savedFont = AppSettings.Get(SettingsPrefix + "FontFamily");
            if (!string.IsNullOrEmpty(savedFont))
            {
                GameOverlayWindow.SetFontFamily(savedFont);
                for (int i = 0; i < CmbFont.Items.Count; i++)
                {
                    if (CmbFont.Items[i] is string f && f.Equals(savedFont, StringComparison.OrdinalIgnoreCase))
                    {
                        CmbFont.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Load custom windows
            _customWindowTitles.Clear();
            var customWindowsStr = AppSettings.Get(SettingsPrefix + "CustomWindows") ?? "";
            if (!string.IsNullOrEmpty(customWindowsStr))
            {
                foreach (var title in customWindowsStr.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    _customWindowTitles.Add(title);
            }

            // Load widget layout
            var layoutJson = AppSettings.Get(SettingsPrefix + "Layout") ?? "";
            LoadWidgetsFromJson(layoutJson);

            _suppressEvents = false;
            _configLoaded = true;
            UpdateStatus();
        }
        catch
        {
            _suppressEvents = false;
            // 即使加载失败也允许保存（编辑器保持可用），但布局可能不完整
            _configLoaded = true;
        }
    }

    private string SerializeLayout()
    {
        var layout = _widgets.Select(w => new
        {
            type = (int)w.Type,
            x = w.X, y = w.Y,
            w = w.Width, h = w.Height,
            fs = w.FontSize,
            prefix = w.Prefix,
            showPrefix = w.ShowPrefix,
            layer = w.Layer,
            text = w.CustomText,
            img = w.ImagePath,
            color = w.ColorArgb,
            tcolor = w.TextColorArgb
        }).ToList();
        return JsonSerializer.Serialize(layout);
    }

    private void LoadWidgetsFromJson(string json)
    {
        // 先清空画布：页面实例可能被缓存复用，二次进入时不清会叠出重复组件，
        // 编辑界面也就无法如实还原「上一次保存的样式」；保存的布局为空 = 画布本来就该是空的
        ClearAllWidgets();
        if (string.IsNullOrEmpty(json)) return;
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            // 单组件解析失败不拖垮整个布局（跳过该组件继续加载）
            try
            {
            var type = (OverlayWidgetType)item.GetProperty("type").GetInt32();
            // 1% Low / 0.1% Low 组件重新启用（2026-09 恢复）：旧布局里保存的
            // 这类组件直接加载，不再跳过（枚举值从未删过，编号兼容）。
            var widget = new DesignerWidget
            {
                Type = type,
                X = item.GetProperty("x").GetDouble(),
                Y = item.GetProperty("y").GetDouble(),
                Width = item.GetProperty("w").GetDouble(),
                Height = item.GetProperty("h").GetDouble(),
                FontSize = item.GetProperty("fs").GetDouble(),
                Prefix = item.TryGetProperty("prefix", out var p) ? p.GetString() ?? "" : "",
                ShowPrefix = item.TryGetProperty("showPrefix", out var sp)
                    ? sp.GetBoolean()
                    : (!string.IsNullOrEmpty(item.TryGetProperty("prefix", out var pp) ? pp.GetString() ?? "" : ""))
                        && type != OverlayWidgetType.CustomText,
                Layer = item.TryGetProperty("layer", out var ly) ? ly.GetInt32() : 0,
                CustomText = item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                ImagePath = item.TryGetProperty("img", out var im) ? im.GetString() ?? "" : "",
                ColorArgb = item.TryGetProperty("color", out var cl) && cl.TryGetUInt32(out var cc) ? cc : 0xFF00A0FF,
                TextColorArgb = item.TryGetProperty("tcolor", out var tc) && tc.TryGetUInt32(out var tcv) ? tcv : 0xFFFFFFFFu,
                Label = PaletteItems.FirstOrDefault(pi => pi.Type == type).Label ?? type.ToString(),
                IsChart = type is OverlayWidgetType.FpsChart or OverlayWidgetType.CpuTempChart
                    or OverlayWidgetType.FpsTimeChart or OverlayWidgetType.FpsRenderLatencyChart
                    or OverlayWidgetType.FpsLow1Chart or OverlayWidgetType.FpsLow01Chart,
            };
            CreateWidgetElement(widget);
            _widgets.Add(widget);
            }
            catch { }
        }
    }

    #endregion

    #region Helpers

    private void UpdateStatus()
    {
        TxtCompCount.Text = $"组件: {_widgets.Count}";
    }

    private static Windows.UI.Color FromArgb(uint argb)
    {
        return Windows.UI.Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
    }

    private static ImageSource LoadImageSource(string path, string fallbackLabel)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                bmp.SetSourceAsync(stream.AsRandomAccessStream()).GetAwaiter().GetResult();
                return bmp;
            }
        }
        catch { }
        // Fallback: colored placeholder text image
        return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
    }

    #endregion
}
