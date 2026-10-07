using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Ai;
using TubaWinUi3.Services.AiPlayground;
using TubaWinUi3.Services.AiPlayground.Rules;
using Windows.System;
using Windows.UI.Core;

namespace TubaWinUi3.Pages;

/// <summary>
/// 本地 AI 试炼场：模型库（下载 / 删除 / 导入）、试炼场（对话 / 图像分类 / 目标检测 / 图像特征对比）、
/// 引擎与设备（Windows ML EP 安装注册、设备表、NPU 编译缓存）。
/// </summary>
public sealed partial class AiPlaygroundPage : Page, ILocalizablePage
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher =
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _refreshTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _chatFlushTimer;

    private readonly List<AiModelEntry> _entries = [];
    private readonly List<AiModelEntry> _comboEntries = [];
    private readonly List<AiEpDeviceInfo> _deviceOptions = [];
    private readonly List<LibraryCardRefs> _cardRefs = [];
    private readonly Dictionary<string, bool> _cardReadyCache = new();

    private AiModelEntry? _current;
    private AiEpDeviceInfo? _selectedDevice;
    private string _npuHardwareName = "";

    private ClassificationRunner? _classifier;
    private DetectionRunner? _detector;
    private EmbeddingRunner? _embedder;
    private LlmChatRunner? _llm;
    private string? _runnerModelId;

    // 对话状态
    private readonly List<AiChatSession> _sessions = [];
    private AiChatSession? _activeSession;
    private string? _sessionsModelId;
    private CancellationTokenSource? _chatCts;
    private StringBuilder? _chatPendingText;
    private TextBlock? _chatPendingTarget;
    private DateTime _chatStartedAt;
    private int _chatPieceCount;
    private bool _chatRunning;

    // 图像状态
    private string? _imagePath;
    private (int W, int H) _imageSize;
    private List<DetectionItem>? _lastDetections;
    private readonly List<(string Path, float[] Vector)> _gallery = [];
    private bool _busy;
    private bool _suspendUiEvents;
    private bool _subscribed;
    private string _searchText = "";
    private string _filterTag = "all";

    // 检测高级设置 / 规则
    private DetectionOptions _detectOptions = new();
    private List<DetectionRule> _drawRules = [];
    private string _drawRulesText = "";

    // 输入源
    private Win32WindowInfo? _selectedWindow;
    private bool _liveRunning;
    private IFrameSource? _frameSource;
    private DetectionLoop? _detectLoop;
    private WriteableBitmap? _previewBitmap;

    private sealed class LibraryCardRefs
    {
        public required AiModelEntry Entry { get; init; }
        public required TextBlock StatusText { get; init; }
        public required ProgressBar ProgressBar { get; init; }
        public required Button DownloadButton { get; init; }
        public required Button DeleteButton { get; init; }
        public required Button OpenButton { get; init; }
    }

    public AiPlaygroundPage()
    {
        InitializeComponent();

        _refreshTimer = _dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(700);
        _refreshTimer.Tick += (_, _) => SafeUiTick(UpdateDynamicStates);

        _chatFlushTimer = _dispatcher.CreateTimer();
        _chatFlushTimer.Interval = TimeSpan.FromMilliseconds(60);
        _chatFlushTimer.Tick += (_, _) => SafeUiTick(FlushChatText);

        ApplyStaticTexts();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LocalizationService.LanguageChanged += OnLanguageChanged;
        ActualThemeChanged += (_, _) =>
        {
            // 代码构建的画刷不会随主题切换自动刷新（与 FormatConverterPage 的做法一致）
            SafeUiTick(RenderLibrary);
            SafeUiTick(UpdateDeviceTable);
        };
    }

    /// <summary>
    /// DispatcherQueue 计时器/回调异常兜底：这类异常不会走 Application.UnhandledException，
    /// 未处理时会直接进程 fail-fast（stowed exception 0xc000027b），必须在这里截住。
    /// </summary>
    private void SafeUiTick(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiPlayground] UI 刷新失败: {ex}");
        }
    }

    // ── 生命周期 ────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            Win32DropHelper.FilesDropped += OnFilesDropped;
            DownloadQueueService.QueueChanged += OnQueueChanged;
            ImageStageInner.SizeChanged += (_, _) => RedrawOverlay();
        }
        RefreshEntries();
        PopulateModeCombo();
        PopulateDeviceCombo();
        _ = RefreshEnginePaneAsync();
        _ = UpdateDeviceChipsAsync();
        _refreshTimer.Start();

        // 恢复用户自定义的上下文上限（0 = 自动）
        if (int.TryParse(AppSettings.Get("AiPlayground_ContextTokens"), out var ctx) && ctx > 0)
        {
            _suspendUiEvents = true;
            ContextTokensBox.Value = Math.Clamp(ctx, 0, LlmContextBudget.MaxBudgetTokens);
            _suspendUiEvents = false;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _subscribed = false;
            Win32DropHelper.FilesDropped -= OnFilesDropped;
            DownloadQueueService.QueueChanged -= OnQueueChanged;
        }
        StopLive();
        _refreshTimer.Stop();
        _chatFlushTimer.Stop();
        CancelChat();
        DisposeRunners();
    }

    private void OnLanguageChanged() => SafeUiTick(ApplyStaticTexts);

    public void ApplyLocalization()
    {
        ApplyStaticTexts();
        RefreshEntries();
    }

    private void ApplyStaticTexts()
    {
        ImportButtonText.Text = L("AiPlayground_Import", "导入本地模型");
        OpenFolderButtonText.Text = L("AiPlayground_OpenFolder", "打开模型目录");
        CurrentModelLabel.Text = L("AiPlayground_CurrentModel", "当前模型");
        ReloadModelText.Text = L("AiPlayground_Reload", "重新加载");
        EmptyHint.Text = L("AiPlayground_EmptyHint", "还没有可用的模型：请先在「模型库」下载一个模型。");
        GoLibraryButton.Content = L("AiPlayground_GoLibrary", "去模型库");
        SystemPromptExpander.Header = L("AiPlayground_SystemPrompt", "系统提示词（可选）");
        SessionLabel.Text = L("AiPlayground_Session", "会话");
        NewSessionText.Text = L("AiPlayground_NewSession", "新建对话");
        DeleteSessionText.Text = L("AiPlayground_DeleteSession", "删除会话");
        SendButtonText.Text = L("AiPlayground_Send", "发送");
        StopButtonText.Text = L("AiPlayground_Stop", "停止");
        PickImageText.Text = L("AiPlayground_PickImage", "选择图片");
        RunInferenceText.Text = L("AiPlayground_Run", "开始推理");
        PickWindowText.Text = L("AiPlayground_PickWindow", "选择窗口");
        LiveToggleText.Text = _liveRunning
            ? L("AiPlayground_StopLive", "停止实时")
            : L("AiPlayground_StartLive", "开始实时");
        ClearGalleryText.Text = L("AiPlayground_ClearGallery", "清空对比");
        ThresholdLabel.Text = L("AiPlayground_Confidence", "置信度");
        IouLabel.Text = L("AiPlayground_Iou", "IoU");
        ClassAwareNmsCheck.Content = L("AiPlayground_ClassAwareNms", "类别内 NMS");
        ClassFilterText.Text = L("AiPlayground_ClassFilter", "类别过滤");
        DrawRulesText.Text = L("AiPlayground_DrawRules", "绘制规则");
        TemperatureLabel.Text = L("AiPlayground_Temperature", "温度");
        MaxTokensLabel.Text = L("AiPlayground_MaxTokens", "最大生成");
        ContextLabel.Text = L("AiPlayground_ContextTokens", "上下文");
        DropHint.Text = L("AiPlayground_DropHint", "把图片拖到这里，或点击「选择图片」");
        DeviceSectionTitle.Text = L("AiPlayground_DeviceSection", "设备信息");
        EpSectionTitle.Text = L("AiPlayground_EpSection", "加速包（执行提供程序）");
        InstallAllText.Text = L("AiPlayground_InstallAll", "一键下载并注册推荐加速包");
        DeviceTableTitle.Text = L("AiPlayground_DeviceTable", "已注册设备（ONNX Runtime）");
        CacheSectionTitle.Text = L("AiPlayground_CacheSection", "NPU 编译缓存");
        ClearCacheText.Text = L("AiPlayground_ClearCache", "清理缓存");
        UpdateModeHint();
        UpdateLibraryFooter();
    }

    private static string L(string key, string fallback) => LocalizationService.L(key, fallback);

    private static string TaskDisplay(AiTaskKind task) => task switch
    {
        AiTaskKind.Chat => L("AiPlayground_Task_Chat", "对话"),
        AiTaskKind.ImageClassification => L("AiPlayground_Task_Classification", "图像分类"),
        AiTaskKind.ObjectDetection => L("AiPlayground_Task_Detection", "目标检测"),
        _ => L("AiPlayground_Task_Embedding", "图像特征"),
    };

    private static string TagDisplay(string key) => key switch
    {
        "AiPlayground_Tag_NpuCpu" => L(key, "推荐 NPU/CPU"),
        "AiPlayground_Tag_Gpu" => L(key, "推荐 GPU"),
        "AiPlayground_Tag_Quantized" => L(key, "量化版"),
        "AiPlayground_Tag_QuantizedGpl" => L(key, "量化版 · GPL"),
        "AiPlayground_Tag_Gpl" => L(key, "GPL 许可"),
        "AiPlayground_Tag_Custom" => L(key, "本地导入"),
        _ => "",
    };

    /// <summary>加速包（EP）作用说明：让用户知道装它能启用哪类硬件（如 Intel NPU）。</summary>
    private static string DescribeEpPurpose(string epName) => epName switch
    {
        var n when n.Contains("QNN", StringComparison.OrdinalIgnoreCase) => L("AiPlayground_EpPurpose_QNN", "高通 Hexagon NPU"),
        var n when n.Contains("OpenVINO", StringComparison.OrdinalIgnoreCase) => L("AiPlayground_EpPurpose_OpenVINO", "Intel NPU / GPU / CPU"),
        var n when n.Contains("Vitis", StringComparison.OrdinalIgnoreCase) => L("AiPlayground_EpPurpose_VitisAI", "AMD Ryzen AI NPU"),
        var n when n.Contains("TensorRt", StringComparison.OrdinalIgnoreCase) => L("AiPlayground_EpPurpose_NvTensorRtRtx", "NVIDIA RTX GPU"),
        var n when n.Contains("MIGraphX", StringComparison.OrdinalIgnoreCase) => L("AiPlayground_EpPurpose_MIGraphX", "AMD RDNA GPU"),
        var n when n.Contains("Dml", StringComparison.OrdinalIgnoreCase) || n.Contains("DirectML", StringComparison.OrdinalIgnoreCase) => L("AiPlayground_EpPurpose_Dml", "通用 GPU（DirectML，内置）"),
        var n when n.Contains("Cpu", StringComparison.OrdinalIgnoreCase) => L("AiPlayground_EpPurpose_Cpu", "CPU（内置）"),
        _ => "",
    };

    // ── 状态条：模式 / 设备 ─────────────────────────────────────────

    private void PopulateModeCombo()
    {
        _suspendUiEvents = true;
        ModeCombo.Items.Clear();
        ModeCombo.Items.Add(L("AiPlayground_Mode_Auto", "自动（NPU → GPU → CPU）"));
        ModeCombo.Items.Add(L("AiPlayground_Mode_Npu", "优先 NPU"));
        ModeCombo.Items.Add(L("AiPlayground_Mode_Gpu", "优先 GPU"));
        ModeCombo.Items.Add(L("AiPlayground_Mode_Cpu", "仅 CPU"));
        ModeCombo.SelectedIndex = AiRuntimeService.GetMode() switch
        {
            AiEngineMode.Npu => 1,
            AiEngineMode.Gpu => 2,
            AiEngineMode.Cpu => 3,
            _ => 0,
        };
        _suspendUiEvents = false;
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendUiEvents) return;
        var mode = ModeCombo.SelectedIndex switch
        {
            1 => AiEngineMode.Npu,
            2 => AiEngineMode.Gpu,
            3 => AiEngineMode.Cpu,
            _ => AiEngineMode.Auto,
        };
        AiRuntimeService.SetMode(mode);
        DisposeRunners();
        UpdateModeHint();
    }

    private void PopulateDeviceCombo()
    {
        _suspendUiEvents = true;
        DeviceCombo.Items.Clear();
        _deviceOptions.Clear();
        DeviceCombo.Items.Add(L("AiPlayground_Device_Auto", "自动（按引擎模式）"));
        foreach (var device in AiRuntimeService.DescribeEpDevices())
        {
            _deviceOptions.Add(device);
            // 非 QNN 的 NPU（如 Intel OpenVINO NPU）首次使用前要经子进程探测：
            // 通过则真跑 NPU，不通过自动回退 GPU/CPU。这里提示用户会先探测。
            var probe = AiRuntimeService.RequiresProbe(device.EpName, device.DeviceType);
            DeviceCombo.Items.Add(probe
                ? $"{device.Display} · {L("AiPlayground_Device_Probe", "首次自动探测兼容性")}"
                : device.Display);
        }
        // 硬件已检测到 NPU 但加速包尚未注册时，在下拉里给出明确引导（不可选中）
        var hasNpuEp = _deviceOptions.Any(d => string.Equals(d.DeviceType, "NPU", StringComparison.OrdinalIgnoreCase));
        if (!hasNpuEp && !string.IsNullOrEmpty(_npuHardwareName))
        {
            DeviceCombo.Items.Add(new ComboBoxItem
            {
                Content = string.Format(
                    L("AiPlayground_NpuNotEnabled", "{0}（NPU 未启用 — 请在「引擎与设备」安装加速包）"),
                    _npuHardwareName),
                IsEnabled = false,
            });
        }
        DeviceCombo.SelectedIndex = 0;
        _selectedDevice = null;
        _suspendUiEvents = false;
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendUiEvents) return;
        var index = DeviceCombo.SelectedIndex - 1;
        _selectedDevice = index >= 0 && index < _deviceOptions.Count ? _deviceOptions[index] : null;
        DisposeRunners();
        UpdateModeHint();
    }

    private void UpdateModeHint()
    {
        ModeHint.Text = _selectedDevice is not null
            ? L("AiPlayground_ModeHint_Device", "已指定执行设备（优先于引擎模式），对之后加载的模型生效。")
            : AiRuntimeService.CanInstallVendorEps
                ? L("AiPlayground_ModeHint_Normal", "引擎模式对之后加载的模型生效。")
                : L("AiPlayground_ModeHint_LowOs", "当前系统低于 Win11 24H2：CPU 与 DirectML(GPU) 可用，NPU 加速包需更高系统版本。");
    }

    private async Task UpdateDeviceChipsAsync()
    {
        string npuName = "";
        string? npuTops = null;
        string cpuName = "";
        var gpuNames = new List<string>();
        await Task.Run(() =>
        {
            npuName = AiRuntimeService.DetectNpuName() ?? "";
            cpuName = AiRuntimeService.DetectCpuName() ?? "";
            gpuNames = AiRuntimeService.DetectGpuNames().ToList();
            npuTops = Services.NpuCatalog.LookupTops(npuName, cpuName);
        });

        DeviceChip.Text = string.IsNullOrEmpty(npuName)
            ? L("AiPlayground_NoNpu", "NPU：未检测到")
            : $"NPU：{npuName}{(string.IsNullOrEmpty(npuTops) ? "" : $"（{npuTops}）")}";
        ToolTipService.SetToolTip(DeviceChip,
            $"{cpuName}\n{string.Join("\n", gpuNames)}\n{npuName}");

        // 首次拿到 NPU 硬件名后刷新一次设备下拉，补上"NPU 未启用"引导项
        if (!string.Equals(_npuHardwareName, npuName, StringComparison.Ordinal))
        {
            _npuHardwareName = npuName;
            PopulateDeviceCombo();
        }

        var deviceTypes = AiRuntimeService.DescribeEpDevices()
            .Select(d => d.DeviceType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();
        EpChip.Text = deviceTypes.Count == 0
            ? L("AiPlayground_NoEp", "加速：未加载")
            : $"{L("AiPlayground_EpReady", "已注册加速")}：{string.Join(" + ", deviceTypes)}";
    }

    // ── 模型库 ──────────────────────────────────────────────────────

    private void RefreshEntries()
    {
        _entries.Clear();
        _entries.AddRange(AiModelLibrary.GetAllEntries());
        RenderLibrary();
        RefreshModelCombo();
        UpdateLibraryFooter();
    }

    private void UpdateLibraryFooter()
    {
        var total = AiModelLibrary.GetTotalSize();
        LibraryFooter.Text = string.Format(
            L("AiPlayground_LibraryFooter", "模型库占用 {0} · 模型存放于 {1}"),
            DownloadQueueService.FormatSize(total), AiModelLibrary.ModelsRoot);
    }

    private bool MatchesFilter(AiModelEntry entry)
    {
        if (_searchText.Length > 0 &&
            !entry.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) &&
            !entry.SubLine.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            return false;
        return _filterTag switch
        {
            "chat" => entry.Task == AiTaskKind.Chat,
            "classification" => entry.Task == AiTaskKind.ImageClassification,
            "detection" => entry.Task == AiTaskKind.ObjectDetection,
            "embedding" => entry.Task == AiTaskKind.ImageEmbedding,
            "ready" => AiModelLibrary.GetStatus(entry).AllReady,
            _ => true,
        };
    }

    private void RenderLibrary()
    {
        LibraryList.Children.Clear();
        _cardRefs.Clear();
        foreach (var entry in _entries.Where(MatchesFilter))
            LibraryList.Children.Add(BuildLibraryCard(entry));
    }

    private FrameworkElement BuildLibraryCard(AiModelEntry entry)
    {
        var status = AiModelLibrary.GetStatus(entry);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Background = Res("CardBackgroundFillColorDefaultBrush", Microsoft.UI.Colors.Transparent),
            BorderBrush = Res("CardStrokeColorDefaultBrush", Microsoft.UI.Colors.Gray),
            BorderThickness = new Thickness(1),
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Spacing = 4 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = entry.DisplayName,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        var tagText = TagDisplay(entry.TagKey);
        if (tagText.Length > 0)
            titleRow.Children.Add(MakeBadge(tagText, Res("AccentFillColorSecondaryBrush", Microsoft.UI.Colors.SteelBlue)));
        var taskText = TaskDisplay(entry.Task);
        titleRow.Children.Add(MakeBadge(taskText, Res("SystemFillColorNeutralBackgroundBrush", Microsoft.UI.Colors.Gray)));
        if (entry.Preset is { IsAdvanced: true })
            titleRow.Children.Add(MakeBadge(L("AiPlayground_Tag_Advanced", "进阶"), Res("SystemFillColorCautionBackgroundBrush", Microsoft.UI.Colors.Orange)));
        left.Children.Add(titleRow);
        left.Children.Add(new TextBlock
        {
            Text = entry.SubLine,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
        });

        var statusText = new TextBlock
        {
            Text = status.Describe(),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
        };
        left.Children.Add(statusText);

        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed, Height = 4 };
        left.Children.Add(progressBar);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var downloadButton = new Button { Content = status.AnyPresent ? L("AiPlayground_ContinueDownload", "继续下载") : L("AiPlayground_Download", "下载") };
        downloadButton.Visibility = entry.Preset is null ? Visibility.Collapsed : Visibility.Visible;
        downloadButton.Click += async (_, _) => await DownloadEntryAsync(entry);
        right.Children.Add(downloadButton);

        var openButton = new Button { Content = L("AiPlayground_OpenInPlayground", "在试炼场打开") };
        openButton.Click += (_, _) =>
        {
            SelectEntry(entry);
            MainPivot.SelectedIndex = 1;
        };
        right.Children.Add(openButton);

        var deleteButton = new Button { Content = entry.IsCustom ? L("AiPlayground_Remove", "移除") : L("AiPlayground_Delete", "删除") };
        deleteButton.Visibility = status.AnyPresent ? Visibility.Visible : Visibility.Collapsed;
        deleteButton.Click += async (_, _) => await DeleteEntryAsync(entry);
        right.Children.Add(deleteButton);

        Grid.SetColumn(right, 1);
        layout.Children.Add(left);
        layout.Children.Add(right);
        card.Child = layout;

        _cardRefs.Add(new LibraryCardRefs
        {
            Entry = entry,
            StatusText = statusText,
            ProgressBar = progressBar,
            DownloadButton = downloadButton,
            DeleteButton = deleteButton,
            OpenButton = openButton,
        });
        _cardReadyCache[entry.Id] = status.AllReady;
        return card;
    }

    private static Border MakeBadge(string text, Brush background) => new()
    {
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(8, 2, 8, 2),
        Background = background,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11 },
    };

    private void UpdateDynamicStates()
    {
        var readyChanged = false;
        foreach (var card in _cardRefs)
        {
            var status = AiModelLibrary.GetStatus(card.Entry);
            var item = card.Entry.Preset is null ? null : HfModelDownloader.FindQueueItem(card.Entry);

            // 注意：DownloadItem.Progress 在首个进度事件前为 null（队列自己的 UI 也用 ?. 处理）
            var itemActive = item is not null &&
                item.State is DownloadItemState.Downloading or DownloadItemState.Queued
                    or DownloadItemState.Resolving or DownloadItemState.Paused;
            var percentage = item?.Progress?.Percentage ?? 0;

            card.StatusText.Text = itemActive
                ? item!.Progress is null
                    ? DescribeState(item.State)
                    : $"{DescribeState(item.State)} · {percentage:F0}%"
                : status.Describe();
            card.ProgressBar.Visibility = item is not null && item.State is DownloadItemState.Downloading or DownloadItemState.Queued or DownloadItemState.Resolving
                ? Visibility.Visible
                : Visibility.Collapsed;
            card.ProgressBar.Value = percentage;
            card.DeleteButton.Visibility = status.AnyPresent ? Visibility.Visible : Visibility.Collapsed;
            card.DownloadButton.Visibility = card.Entry.Preset is null || status.AllReady ? Visibility.Collapsed : Visibility.Visible;
            card.DownloadButton.Content = status.AnyPresent ? L("AiPlayground_ContinueDownload", "继续下载") : L("AiPlayground_Download", "下载");

            if (_cardReadyCache.TryGetValue(card.Entry.Id, out var wasReady) && wasReady != status.AllReady)
            {
                _cardReadyCache[card.Entry.Id] = status.AllReady;
                readyChanged = true;
            }
        }
        if (readyChanged)
        {
            RefreshModelCombo();
            UpdateLibraryFooter();
        }
    }

    private static string DescribeState(DownloadItemState state) => state switch
    {
        DownloadItemState.Resolving => "解析中",
        DownloadItemState.Queued => "排队中",
        DownloadItemState.Downloading => "下载中",
        DownloadItemState.Paused => "已暂停",
        DownloadItemState.Processing => "处理中",
        DownloadItemState.Completed => "已完成",
        DownloadItemState.Failed => "失败",
        _ => "已取消",
    };

    private void OnQueueChanged() => _dispatcher.TryEnqueue(() => SafeUiTick(UpdateDynamicStates));

    private async Task DownloadEntryAsync(AiModelEntry entry)
    {
        try
        {
            if (AiRuntimeService.UnsupportedReason is { } reason)
            {
                await ShowMessageAsync(L("AiPlayground_NotSupported", "不可用"), reason);
                return;
            }
            if (entry.Preset is null) return;

            var approxBytes = AiModelLibrary.ParseApproxBytes(entry.Preset.ApproxSizeText);
            var targetDir = AiModelLibrary.GetStoredModelDir(entry.Id);
            var free = AiModelLibrary.GetFreeSpace(targetDir);
            var text = string.Format(
                L("AiPlayground_DownloadConfirm", "将下载「{0}」（{1}），来自 {2}（{3}）。\n下载在全局下载队列中进行，支持暂停与断点续传。"),
                entry.DisplayName, entry.Preset.ApproxSizeText, entry.Preset.Repo, entry.Preset.License);
            if (approxBytes > 0 && free > 0 && free < approxBytes + (1L << 30))
            {
                text += "\n\n" + string.Format(
                    L("AiPlayground_DiskWarn", "⚠ 目标磁盘可用空间仅 {0}，可能不足。"),
                    DownloadQueueService.FormatSize(free));
            }
            if (!await ConfirmAsync(L("AiPlayground_DownloadTitle", "下载模型"), text, L("AiPlayground_Download", "下载")))
                return;

            var status = new Progress<string>(msg => LibraryFooter.Text = msg);
            HfModelDownloader.EnqueueModel(entry, status);
            UpdateDynamicStates();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L("AiPlayground_DownloadFailed", "下载失败"), ex);
        }
    }

    private async Task DeleteEntryAsync(AiModelEntry entry)
    {
        try
        {
            var text = entry.IsCustom && entry.Custom?.Kind == "genai-folder"
                ? string.Format(L("AiPlayground_RemoveConfirm", "将从列表中移除「{0}」（不会删除你磁盘上的原始目录）。"), entry.DisplayName)
                : string.Format(L("AiPlayground_DeleteConfirm", "将删除「{0}」的本地模型文件（{1}），删除后需要重新下载。"),
                    entry.DisplayName, DownloadQueueService.FormatSize(AiModelLibrary.GetStatus(entry).Bytes));
            if (!await ConfirmAsync(entry.IsCustom ? L("AiPlayground_Remove", "移除") : L("AiPlayground_Delete", "删除"), text))
                return;

            if (_current?.Id == entry.Id)
            {
                _current = null;
                DisposeRunners();
                UpdatePlaygroundSetup();
            }
            AiModelLibrary.DeleteModel(entry);
            AiProviderStore.SyncLocalProviderModels();
            RefreshEntries();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L("AiPlayground_Delete", "删除"), ex);
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchText = sender.Text.Trim();
        RenderLibrary();
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilterCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _filterTag = tag;
            RenderLibrary();
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AiModelLibrary.ModelsRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", AiModelLibrary.ModelsRoot) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync(L("AiPlayground_OpenFolder", "打开模型目录"), ex);
        }
    }

    private void GoLibraryButton_Click(object sender, RoutedEventArgs e) => MainPivot.SelectedIndex = 0;

    // ── 导入本地模型 ────────────────────────────────────────────────

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = L("AiPlayground_Import", "导入本地模型"),
            Content = L("AiPlayground_ImportHint",
                "单个 .onnx 文件（图像分类 / 目标检测 / 图像特征）或 ONNX Runtime GenAI 模型目录（对话，需含 genai_config.json）。"),
            PrimaryButtonText = L("AiPlayground_ImportFile", "选择 .onnx 文件"),
            SecondaryButtonText = L("AiPlayground_ImportFolder", "选择 GenAI 目录"),
            CloseButtonText = L("AiPlayground_Cancel", "取消"),
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await ImportSingleFileFlowAsync();
        else if (result == ContentDialogResult.Secondary)
            await ImportGenAiFolderFlowAsync();
    }

    private async Task ImportSingleFileFlowAsync()
    {
        var file = Win32Dialogs.PickOpen("ONNX 模型\0*.onnx\0所有文件\0*.*\0\0", "选择 ONNX 模型");
        if (string.IsNullOrEmpty(file)) return;

        var taskCombo = new ComboBox { MinWidth = 220, SelectedIndex = 0 };
        taskCombo.Items.Add(L("AiPlayground_Task_Classification", "图像分类"));
        taskCombo.Items.Add(L("AiPlayground_Task_Detection", "目标检测"));
        taskCombo.Items.Add(L("AiPlayground_Task_Embedding", "图像特征"));
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = L("AiPlayground_ImportTaskLabel", "选择任务类型（标签优先取模型 config.json 的 id2label）："),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(taskCombo);

        var dialog = new ContentDialog
        {
            Title = L("AiPlayground_Import", "导入本地模型"),
            Content = panel,
            PrimaryButtonText = L("AiPlayground_Import", "导入"),
            CloseButtonText = L("AiPlayground_Cancel", "取消"),
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var task = taskCombo.SelectedIndex switch
        {
            1 => AiTaskKind.ObjectDetection,
            2 => AiTaskKind.ImageEmbedding,
            _ => AiTaskKind.ImageClassification,
        };
        try
        {
            await Task.Run(() => AiModelLibrary.ImportSingleFile(file, task, null, null));
            RefreshEntries();
            LibraryFooter.Text = string.Format(L("AiPlayground_ImportDone", "已导入：{0}"), Path.GetFileName(file));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L("AiPlayground_ImportFailed", "导入失败"), ex);
        }
    }

    private async Task ImportGenAiFolderFlowAsync()
    {
        var folder = await Win32Dialogs.PickFolderAsync();
        if (string.IsNullOrEmpty(folder)) return;

        var formatCombo = new ComboBox { MinWidth = 220, SelectedIndex = 0 };
        formatCombo.Items.Add("Phi-3 / Phi-4（<|system|>…）");
        formatCombo.Items.Add("Qwen / ChatML（<|im_start|>…）");
        formatCombo.Items.Add("Llama 3（<|start_header_id|>…）");
        formatCombo.Items.Add(L("AiPlayground_PromptPlain", "纯文本"));
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = L("AiPlayground_ImportFormatLabel", "选择该模型的提示词模板（不同模型家族使用不同的对话标记）："),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(formatCombo);

        var dialog = new ContentDialog
        {
            Title = L("AiPlayground_Import", "导入本地模型"),
            Content = panel,
            PrimaryButtonText = L("AiPlayground_Import", "导入"),
            CloseButtonText = L("AiPlayground_Cancel", "取消"),
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var format = formatCombo.SelectedIndex switch
        {
            1 => AiPromptFormat.ChatML,
            2 => AiPromptFormat.Llama3,
            3 => AiPromptFormat.Plain,
            _ => AiPromptFormat.Phi,
        };
        try
        {
            await Task.Run(() => AiModelLibrary.ImportGenAiFolder(folder, format, null));
            AiProviderStore.SyncLocalProviderModels();
            RefreshEntries();
            LibraryFooter.Text = string.Format(L("AiPlayground_ImportDone", "已导入：{0}"), folder);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L("AiPlayground_ImportFailed", "导入失败"), ex);
        }
    }

    // ── 试炼场：模型选择与设置 ───────────────────────────────────────

    private void RefreshModelCombo()
    {
        _suspendUiEvents = true;
        try
        {
            _comboEntries.Clear();
            ModelCombo.Items.Clear();
            foreach (var entry in _entries.Where(e => AiModelLibrary.GetStatus(e).AllReady))
            {
                _comboEntries.Add(entry);
                ModelCombo.Items.Add($"{entry.DisplayName}（{TaskDisplay(entry.Task)}）");
            }
            if (_current is not null)
            {
                var index = _comboEntries.FindIndex(e => e.Id == _current.Id);
                if (index >= 0) ModelCombo.SelectedIndex = index;
            }
            if (ModelCombo.SelectedIndex < 0 && _comboEntries.Count == 1)
                ModelCombo.SelectedIndex = 0;
        }
        finally
        {
            _suspendUiEvents = false;
        }
        UpdatePlaygroundSetup();
    }

    private void SelectEntry(AiModelEntry entry)
    {
        _current = entry;
        var index = _comboEntries.FindIndex(e => e.Id == entry.Id);
        if (index >= 0)
        {
            _suspendUiEvents = true;
            ModelCombo.SelectedIndex = index;
            _suspendUiEvents = false;
        }
        UpdatePlaygroundSetup();
    }

    private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendUiEvents) return;
        var index = ModelCombo.SelectedIndex;
        _current = index >= 0 && index < _comboEntries.Count ? _comboEntries[index] : null;
        UpdatePlaygroundSetup();
    }

    private void ReloadModelButton_Click(object sender, RoutedEventArgs e)
    {
        DisposeRunners();
        UpdatePlaygroundSetup();
    }

    private void UpdatePlaygroundSetup()
    {
        StopLive();
        DisposeRunners();
        ClearVisionResults();
        RuntimeChip.Text = "";

        var unsupported = AiRuntimeService.UnsupportedReason;
        if (unsupported is not null)
        {
            PlaygroundEmpty.Visibility = Visibility.Visible;
            ChatPane.Visibility = Visibility.Collapsed;
            VisionPane.Visibility = Visibility.Collapsed;
            EmptyHint.Text = unsupported;
            return;
        }

        if (_current is null)
        {
            PlaygroundEmpty.Visibility = Visibility.Visible;
            EmptyHint.Text = L("AiPlayground_EmptyHint", "还没有可用的模型：请先在「模型库」下载一个模型。");
            ChatPane.Visibility = Visibility.Collapsed;
            VisionPane.Visibility = Visibility.Collapsed;
            return;
        }

        PlaygroundEmpty.Visibility = Visibility.Collapsed;
        if (_current.Task == AiTaskKind.Chat)
        {
            ChatPane.Visibility = Visibility.Visible;
            VisionPane.Visibility = Visibility.Collapsed;
            LoadSessionsForCurrent();
            _ = EnsureLlmAsync();
        }
        else
        {
            ChatPane.Visibility = Visibility.Collapsed;
            VisionPane.Visibility = Visibility.Visible;
            var isDetect = _current.Task == AiTaskKind.ObjectDetection;
            DetectAdvancedPanel.Visibility = isDetect ? Visibility.Visible : Visibility.Collapsed;
            ClearGalleryButton.Visibility = _current.Task == AiTaskKind.ImageEmbedding ? Visibility.Visible : Visibility.Collapsed;
            if (isDetect)
            {
                LoadDetectOptions();
                LoadDrawRules();
            }
        }
    }

    // ── 检测高级设置 ────────────────────────────────────────────────

    private void LoadDetectOptions()
    {
        var isYolo = _current?.Preset?.Family == "yolo"
                     || (_current?.DisplayName.Contains("YOLO", StringComparison.OrdinalIgnoreCase) ?? false);
        _detectOptions = DetectionOptions.Load(_current!.Id, isYolo);
        _suspendUiEvents = true;
        ThresholdSlider.Value = Math.Round(_detectOptions.Confidence * 100);
        IouSlider.Value = Math.Round(_detectOptions.Iou * 100);
        MaxDetBox.Value = _detectOptions.MaxDetections;
        ClassAwareNmsCheck.IsChecked = _detectOptions.ClassAwareNms;
        _suspendUiEvents = false;
        UpdateThresholdValue();
        UpdateIouValue();
    }

    private void PersistDetectOptions()
    {
        if (_current is null) return;
        _detectOptions.Confidence = (float)(ThresholdSlider.Value / 100.0);
        _detectOptions.Iou = (float)(IouSlider.Value / 100.0);
        _detectOptions.MaxDetections = double.IsNaN(MaxDetBox.Value) ? 300 : (int)MaxDetBox.Value;
        _detectOptions.ClassAwareNms = ClassAwareNmsCheck.IsChecked ?? true;
        _detectOptions.Normalized().Save(_current.Id);
    }

    private void IouSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suspendUiEvents) return;
        UpdateIouValue();
        PersistDetectOptions();
    }

    private void UpdateIouValue()
    {
        if (IouValue is null || IouSlider is null) return;
        IouValue.Text = (IouSlider.Value / 100.0).ToString("F2");
    }

    // ── 绘制规则 ────────────────────────────────────────────────────

    private void LoadDrawRules()
    {
        if (_current is null) return;
        _drawRulesText = RuleStore.LoadText(_current.Id, "draw");
        (_drawRules, _) = RuleParser.Parse(_drawRulesText);
    }

    private async void DrawRulesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var edited = await EditRulesDialogAsync(
            L("AiPlayground_DrawRules", "绘制规则"),
            _drawRulesText,
            L("AiPlayground_DrawRulesHelp",
                "为每个检测框设置绘制样式（不写则自动按类别配色）。示例：\n" +
                "when label == \"person\" && score > 0.8 then stroke=#E81123 width=3 label=\"人 {score:P0}\"\n" +
                "when class_id == 2 then stroke=#0F6CBD width=2\n" +
                "when count(class_id) >= 3 then stroke=#CA5010\n" +
                "else then stroke=#888888 visible=true\n" +
                "字段：label(英文) class_id score x y w h；函数：count/contains/startsWith/endsWith/upper/lower"));
        if (edited is null) return;

        var (rules, errors) = RuleParser.Parse(edited);
        if (errors.Count > 0)
        {
            await ShowMessageAsync(L("AiPlayground_RulesError", "规则有语法错误"),
                string.Join("\n", errors.Select(er => er.Error).Take(6)));
            return;
        }

        _drawRulesText = edited;
        _drawRules = rules;
        RuleStore.SaveText(_current.Id, "draw", edited);
        RedrawOverlay();
        VisionStatus.Text = string.Format(L("AiPlayground_RulesApplied", "已应用 {0} 条绘制规则"), rules.Count);
    }

    /// <summary>通用规则编辑对话框（返回 null = 取消）。</summary>
    private async Task<string?> EditRulesDialogAsync(string title, string initial, string help)
    {
        var box = new TextBox
        {
            Text = initial,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 220,
            FontFamily = new FontFamily("Consolas"),
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray) });
        panel.Children.Add(box);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { MaxHeight = 460, Content = panel },
            PrimaryButtonText = L("AiPlayground_OK", "确定"),
            SecondaryButtonText = L("AiPlayground_RulesReset", "恢复默认"),
            CloseButtonText = L("AiPlayground_Cancel", "取消"),
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary) return "";
        return result == ContentDialogResult.Primary ? box.Text : null;
    }

    // ── 输入源（本地图片 / 摄像头 / 屏幕 / 窗口）────────────────────

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendUiEvents) return;
        var tag = (SourceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "file";
        var isWindow = tag == "window";
        PickWindowButton.Visibility = isWindow ? Visibility.Visible : Visibility.Collapsed;
        // 摄像头/屏幕/窗口支持实时检测
        var liveCapable = tag is "camera" or "screen" or "window";
        LiveToggleButton.Visibility = liveCapable ? Visibility.Visible : Visibility.Collapsed;
        VisionStatus.Text = tag switch
        {
            "camera" => L("AiPlayground_SourceCameraHint", "摄像头：点「开始实时」连续检测。"),
            "screen" => L("AiPlayground_SourceScreenHint", "屏幕：点「开始实时」连续检测（含多屏）。"),
            "window" => L("AiPlayground_SourceWindowHint", "窗口：先「选择窗口」，再点「开始实时」。"),
            _ => "",
        };
    }

    private async void PickWindowButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var windows = Win32WindowCatalog.ListVisibleWindows();
            if (windows.Count == 0)
            {
                await ShowMessageAsync(L("AiPlayground_PickWindow", "选择窗口"),
                    L("AiPlayground_NoWindows", "没有找到可捕获的窗口。"));
                return;
            }
            var combo = new ComboBox { MinWidth = 360, ItemsSource = windows.Select(w => w.Title).ToList(), SelectedIndex = 0 };
            var dialog = new ContentDialog
            {
                Title = L("AiPlayground_PickWindow", "选择窗口"),
                Content = combo,
                PrimaryButtonText = L("AiPlayground_OK", "确定"),
                CloseButtonText = L("AiPlayground_Cancel", "取消"),
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < windows.Count)
            {
                _selectedWindow = windows[combo.SelectedIndex];
                VisionStatus.Text = string.Format(L("AiPlayground_WindowPicked", "已选择窗口：{0}"), _selectedWindow.Value.Title);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L("AiPlayground_PickWindow", "选择窗口"), ex);
        }
    }

    private async void LiveToggleButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_liveRunning)
            {
                StopLive();
                return;
            }
            if (_current is null || _current.Task != AiTaskKind.ObjectDetection)
            {
                await ShowMessageAsync(L("AiPlayground_DrawRules", "实时检测"),
                    L("AiPlayground_LiveNeedDetect", "实时检测仅支持「目标检测」模型。"));
                return;
            }

            var tag = (SourceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "file";
            if (tag == "window" && _selectedWindow is null)
            {
                await ShowMessageAsync(L("AiPlayground_PickWindow", "选择窗口"),
                    L("AiPlayground_NoWindows", "请先点「选择窗口」选一个窗口。"));
                return;
            }
            IFrameSource? source = tag switch
            {
                "screen" => new GdiCaptureFrameSource(GdiCaptureFrameSource.CaptureMode.Screen),
                "window" => CreateWindowSource(),
                "camera" => await CreateCameraSourceAsync(),
                _ => null,
            };
            if (source is null) return;

            var runner = await EnsureDetectorAsync(_current);
            PersistDetectOptions();
            var options = _detectOptions.Clone();

            _frameSource = source;
            _detectLoop = new DetectionLoop(
                source,
                frame => runner.Run(frame, options),
                (items, frame) => _dispatcher.TryEnqueue(() => OnLiveResult(items, frame)));
            _detectLoop.Start();

            _liveRunning = true;
            LiveToggleText.Text = L("AiPlayground_StopLive", "停止实时");
            VisionStatus.Text = L("AiPlayground_LiveStarted", "实时检测已开始");
        }
        catch (Exception ex)
        {
            StopLive();
            await ShowErrorAsync(L("AiPlayground_InferenceFailed", "推理失败"), ex);
        }
    }

    private IFrameSource? CreateWindowSource()
    {
        if (_selectedWindow is not { } win) return null;
        return new GdiCaptureFrameSource(GdiCaptureFrameSource.CaptureMode.Window, win.Hwnd);
    }

    private async Task<IFrameSource?> CreateCameraSourceAsync()
    {
        var cam = new CameraFrameSource();
        try
        {
            await cam.InitializeAsync();
            return cam;
        }
        catch (Exception ex)
        {
            cam.Dispose();
            await ShowErrorAsync(L("AiPlayground_SourceCameraHint", "摄像头"), ex);
            return null;
        }
    }

    /// <summary>实时循环每帧回调（UI 线程）：更新预览与叠加框。</summary>
    private void OnLiveResult(List<DetectionItem> items, FrameBuffer frame)
    {
        try
        {
            if (!_liveRunning) return;
            _imageSize = (frame.Width, frame.Height);
            _lastDetections = items;
            UpdatePreviewFromFrame(frame);
            RenderDetectionResults(items);
            RedrawOverlay();
            VisionStatus.Text = string.Format(
                L("AiPlayground_LiveStats", "实时 · {0:F1} FPS · 推理 {1:F0} ms · {2} 个目标"),
                _detectLoop?.AverageFps ?? 0, _detectLoop?.LastInferenceMs ?? 0, items.Count);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiPlayground] 实时结果渲染失败: {ex}");
        }
    }

    private void UpdatePreviewFromFrame(FrameBuffer frame)
    {
        try
        {
            // 预览降采样到最长边 1280，避免频繁写大图
            int maxEdge = 1280;
            int w = frame.Width, h = frame.Height;
            double scale = Math.Min(1.0, (double)maxEdge / Math.Max(w, h));
            int dw = Math.Max(1, (int)(w * scale));
            int dh = Math.Max(1, (int)(h * scale));
            var (data, pw, ph) = IFrameSource.ScaledCopy(frame, dw, dh);

            if (_previewBitmap is null || _previewBitmap.PixelWidth != pw || _previewBitmap.PixelHeight != ph)
                _previewBitmap = new WriteableBitmap(pw, ph);
            using (var stream = _previewBitmap.PixelBuffer.AsStream())
            {
                stream.Write(data, 0, data.Length);
            }
            _previewBitmap.Invalidate();
            PreviewImage.Source = _previewBitmap;
            DropHint.Visibility = Visibility.Collapsed;
            ImageInfoText.Text = $"{frame.Width}×{frame.Height}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiPlayground] 预览更新失败: {ex}");
        }
    }

    private void StopLive()
    {
        _liveRunning = false;
        try { _detectLoop?.Stop(); } catch { }
        try { _detectLoop?.Dispose(); } catch { }
        _detectLoop = null;
        _frameSource = null;
        LiveToggleText.Text = L("AiPlayground_StartLive", "开始实时");
        VisionStatus.Text = L("AiPlayground_LiveStopped", "实时检测已停止");
    }

    private async void ClassFilterButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var labels = AiPlaygroundLabels.LoadCocoLabels();
            if (_current?.Preset?.Family is not "yolo" && labels.Length == 0) return;

            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock
            {
                Text = L("AiPlayground_ClassFilterHint", "取消勾选的类别将被过滤（不影响其他类别）。"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });
            var boxes = new List<(string Label, CheckBox Box)>();
            foreach (var label in labels)
            {
                var cb = new CheckBox
                {
                    Content = $"{AiPlaygroundLabels.Translate(label)}（{label}）",
                    IsChecked = !_detectOptions.DisabledClasses.Contains(label),
                };
                boxes.Add((label, cb));
                panel.Children.Add(cb);
            }

            var dialog = new ContentDialog
            {
                Title = L("AiPlayground_ClassFilter", "类别过滤"),
                Content = new ScrollViewer { MaxHeight = 420, Content = panel },
                PrimaryButtonText = L("AiPlayground_OK", "确定"),
                CloseButtonText = L("AiPlayground_Cancel", "取消"),
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            _detectOptions.DisabledClasses = boxes
                .Where(b => b.Box.IsChecked == false)
                .Select(b => b.Label)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            PersistDetectOptions();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L("AiPlayground_ClassFilter", "类别过滤"), ex);
        }
    }

    private void DisposeRunners()
    {
        _classifier?.Dispose();
        _classifier = null;
        _detector?.Dispose();
        _detector = null;
        _embedder?.Dispose();
        _embedder = null;
        _llm?.Dispose();
        _llm = null;
        _runnerModelId = null;
    }

    private void UpdateRuntimeChip(string? note)
    {
        RuntimeChip.Text = string.IsNullOrEmpty(note)
            ? ""
            : $"{L("AiPlayground_RunningOn", "运行")}：{note}";
    }

    private void ClearVisionResults()
    {
        _imagePath = null;
        _lastDetections = null;
        _gallery.Clear();
        PreviewImage.Source = null;
        DetectOverlay.Children.Clear();
        ResultsPanel.Children.Clear();
        VisionStatus.Text = "";
        ImageInfoText.Text = "";
        DropHint.Visibility = Visibility.Visible;
    }

    // ── 试炼场：对话 ────────────────────────────────────────────────

    private async Task EnsureLlmAsync()
    {
        if (_current is null || _current.Task != AiTaskKind.Chat) return;
        if (_llm is not null && _runnerModelId == _current.Id) return;

        DisposeRunners();
        var entry = _current;
        var dir = AiModelLibrary.GetEntryDataDir(entry);
        var runner = new LlmChatRunner { ContextTokensOverride = GetContextTokensOverride() };

        var (_, availableMb) = AiRuntimeService.GetMemoryMb();
        if (availableMb > 0 && availableMb < 4096)
            ChatStats.Text = L("AiPlayground_LowMemory", "内存紧张：本地大模型建议至少 8 GB 可用内存");
        else
            ChatStats.Text = L("AiPlayground_LoadingModel", "正在加载模型（首次较慢）…");

        var (provider, deviceType, note) = AiRuntimeService.ResolveLlmTarget(
            _selectedDevice?.EpName, _selectedDevice?.DeviceType);
        try
        {
            await Task.Run(() => runner.Load(dir, provider, deviceType, note));
            if (_current?.Id != entry.Id)
            {
                runner.Dispose();
                return;
            }
            _llm = runner;
            _runnerModelId = entry.Id;
            ChatStats.Text = L("AiPlayground_ModelReady", "模型已就绪");
            UpdateRuntimeChip(runner.RuntimeNote);
            UpdateContextHint();
        }
        catch (Exception ex)
        {
            runner.Dispose();
            ChatStats.Text = "";
            await ShowErrorAsync(L("AiPlayground_LoadFailed", "加载模型失败"), ex);
        }
    }

    // ── 试炼场：对话会话管理 ────────────────────────────────────────

    private string SessionTitle(AiChatSession session) =>
        string.IsNullOrWhiteSpace(session.Title)
            ? L("AiPlayground_DefaultSessionTitle", "新对话")
            : session.Title;

    /// <summary>切换对话模型时载入该模型的会话列表，选中最近一条；无则新建。
    /// 同一模型重复调用（下载完成等触发的界面刷新）直接返回，不打断正在进行的对话。</summary>
    private void LoadSessionsForCurrent()
    {
        if (_current?.Task != AiTaskKind.Chat) return;
        if (_sessionsModelId == _current.Id && _activeSession is not null) return;

        _sessions.Clear();
        _sessions.AddRange(AiChatSessionStore.ListSessions(_current.Id));
        if (_sessions.Count == 0)
            _sessions.Add(AiChatSessionStore.CreateSession(_current.Id));

        _sessionsModelId = _current.Id;
        _activeSession = _sessions[0];
        RebuildSessionCombo();
        RenderChatHistory();
    }

    private void RebuildSessionCombo()
    {
        _suspendUiEvents = true;
        try
        {
            SessionCombo.Items.Clear();
            foreach (var session in _sessions)
                SessionCombo.Items.Add(SessionTitle(session));
            if (_activeSession is not null)
            {
                var index = _sessions.IndexOf(_activeSession);
                if (index >= 0) SessionCombo.SelectedIndex = index;
            }
        }
        finally
        {
            _suspendUiEvents = false;
        }
    }

    private void SessionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendUiEvents) return;
        var index = SessionCombo.SelectedIndex;
        if (index < 0 || index >= _sessions.Count) return;
        if (ReferenceEquals(_sessions[index], _activeSession)) return;

        PersistActiveSession();
        _activeSession = _sessions[index];
        RenderChatHistory();
    }

    private void NewSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current?.Task != AiTaskKind.Chat) return;
        PersistActiveSession();
        var session = AiChatSessionStore.CreateSession(_current.Id);
        _sessions.Insert(0, session);
        _activeSession = session;
        RebuildSessionCombo();
        RenderChatHistory();
    }

    private async void DeleteSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current?.Task != AiTaskKind.Chat || _activeSession is null) return;
        var title = SessionTitle(_activeSession);
        if (!await ConfirmAsync(
                L("AiPlayground_DeleteSession", "删除会话"),
                string.Format(L("AiPlayground_DeleteSessionConfirm", "将删除会话「{0}」及其全部消息？"), title),
                L("AiPlayground_Delete", "删除")))
            return;

        var toDelete = _activeSession;
        AiChatSessionStore.DeleteSession(_current.Id, toDelete.Id);
        _sessions.Remove(toDelete);

        if (_sessions.Count == 0)
            _sessions.Add(AiChatSessionStore.CreateSession(_current.Id));
        _activeSession = _sessions[0];
        RebuildSessionCombo();
        RenderChatHistory();
    }

    /// <summary>把当前会话的消息重绘到消息区。</summary>
    private void RenderChatHistory()
    {
        ChatMessages.Children.Clear();
        if (_activeSession is null) return;
        foreach (var message in _activeSession.Messages)
            AddChatBubble(message.Text, isUser: message.Role == "user");
    }

    /// <summary>保存当前会话（若有消息）。</summary>
    private void PersistActiveSession()
    {
        if (_current is null || _activeSession is null) return;
        AiChatSessionStore.SaveSession(_current.Id, _activeSession);
        var index = _sessions.IndexOf(_activeSession);
        if (index >= 0)
        {
            _suspendUiEvents = true;
            try { SessionCombo.Items[index] = SessionTitle(_activeSession); }
            finally { _suspendUiEvents = false; }
        }
    }

    private void ChatInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down)
            == CoreVirtualKeyStates.Down;
        if (shift) return;
        e.Handled = true;
        _ = SendChatAsync();
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => _ = SendChatAsync();

    private void StopButton_Click(object sender, RoutedEventArgs e) => CancelChat();

    private void CancelChat()
    {
        try { _chatCts?.Cancel(); } catch { }
    }

    private async Task SendChatAsync()
    {
        if (_busy || _chatRunning || _current is null) return;
        var text = ChatInput.Text.Trim();
        if (text.Length == 0) return;
        if (_llm is null || _runnerModelId != _current.Id)
        {
            await EnsureLlmAsync();
            if (_llm is null) return;
        }

        if (_activeSession is null) LoadSessionsForCurrent();
        if (_activeSession is null) return;

        ChatInput.Text = "";
        AddChatBubble(text, isUser: true);
        _activeSession.Messages.Add(new AiPlaygroundChatMessage { Role = "user", Text = text });

        var bubble = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            MaxWidth = 660,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Res("CardBackgroundFillColorDefaultBrush", Microsoft.UI.Colors.Transparent),
            BorderBrush = Res("CardStrokeColorDefaultBrush", Microsoft.UI.Colors.Gray),
            BorderThickness = new Thickness(1),
        };
        var assistantText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Text = "…",
        };
        bubble.Child = assistantText;
        ChatMessages.Children.Add(bubble);
        ScrollChatToBottom();

        _chatRunning = true;
        _chatCts = new CancellationTokenSource();
        _chatStartedAt = DateTime.Now;
        _chatPieceCount = 0;
        var output = new StringBuilder();
        _chatPendingText = output;
        _chatPendingTarget = assistantText;
        _chatFlushTimer.Start();
        SendButton.IsEnabled = false;
        StopButton.Visibility = Visibility.Visible;

        try
        {
            var format = _current.Custom?.PromptFormat ?? _current.Preset?.PromptFormat ?? AiPromptFormat.Phi;
            var history = _activeSession.Messages.Select(m => (m.Role, m.Text)).ToList();
            var prompt = LlmPromptBuilder.BuildPrompt(format, SystemPromptBox.Text, history);
            var maxTokens = (int)Math.Clamp(MaxTokensBox.Value, 64, 4096);
            LlmChatRunner.EnsureOgaHandle();
            await foreach (var piece in _llm.GenerateAsync(prompt, TemperatureSlider.Value, 0.95, maxTokens, _chatCts.Token))
            {
                output.Append(piece);
                _chatPieceCount++;
                ScrollChatToBottom();
            }
            FlushChatText();
            var finalText = output.ToString().Trim();
            if (finalText.Length > 0)
                _activeSession.Messages.Add(new AiPlaygroundChatMessage { Role = "assistant", Text = finalText });
            SetAssistantBubble(bubble, finalText, _llm?.RuntimeNote);

            var elapsed = Math.Max(0.001, (DateTime.Now - _chatStartedAt).TotalSeconds);
            ChatStats.Text = string.Format(
                L("AiPlayground_ChatStats", "约 {0} tokens · {1:F1} tokens/s · {2} 秒"),
                _chatPieceCount, _chatPieceCount / elapsed, elapsed.ToString("F1"));
        }
        catch (OperationCanceledException)
        {
            FlushChatText();
            var partial = output.ToString().Trim();
            if (partial.Length > 0)
                _activeSession.Messages.Add(new AiPlaygroundChatMessage { Role = "assistant", Text = partial });
            SetAssistantBubble(bubble, partial, _llm?.RuntimeNote);
        }
        catch (Exception ex)
        {
            bubble.Child = new TextBlock
            {
                Text = $"{L("AiPlayground_GenFailed", "生成失败")}：{ex.Message}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Res("SystemFillColorCriticalBrush", Microsoft.UI.Colors.Red),
            };
        }
        finally
        {
            _chatFlushTimer.Stop();
            _chatPendingText = null;
            _chatPendingTarget = null;
            _chatRunning = false;
            SendButton.IsEnabled = true;
            StopButton.Visibility = Visibility.Collapsed;
            _chatCts.Dispose();
            _chatCts = null;
            PersistActiveSession();
        }
    }

    /// <summary>渲染助手最终气泡（Markdown）+「本次运行设备」标注。</summary>
    private void SetAssistantBubble(Border bubble, string finalText, string? deviceNote)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(finalText.Length == 0
            ? new TextBlock { Text = L("AiPlayground_NoOutput", "（模型没有输出任何内容）"), TextWrapping = TextWrapping.Wrap }
            : AiMarkdownRenderer.Render(finalText));
        if (!string.IsNullOrEmpty(deviceNote))
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.Format(L("AiPlayground_UsedDevice", "本次运行：{0}"), deviceNote),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        bubble.Child = panel;
    }

    private void AddChatBubble(string text, bool isUser)
    {
        var bubble = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            MaxWidth = 660,
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = isUser
                ? Res("AccentFillColorDefaultBrush", Microsoft.UI.Colors.SteelBlue)
                : Res("CardBackgroundFillColorDefaultBrush", Microsoft.UI.Colors.Transparent),
            BorderBrush = isUser ? null : Res("CardStrokeColorDefaultBrush", Microsoft.UI.Colors.Gray),
            BorderThickness = isUser ? new Thickness(0) : new Thickness(1),
        };
        bubble.Child = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            // 深色主题的强调色为浅蓝，白色文字对比度不足；用系统「强调色上的文字」画刷。
            Foreground = isUser ? Res("TextOnAccentFillColorPrimaryBrush", Microsoft.UI.Colors.White) : null,
        };
        ChatMessages.Children.Add(bubble);
        ScrollChatToBottom();
    }

    private void FlushChatText()
    {
        if (_chatPendingTarget is null || _chatPendingText is null) return;
        _chatPendingTarget.Text = _chatPendingText.ToString();
    }

    private void ScrollChatToBottom()
    {
        try { ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null, true); } catch { }
    }

    private void TemperatureSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // XAML 加载期 Value="…" 会立即触发本事件，此时依赖的具名元素尚未创建，必须判空。
        if (TemperatureValue is null || TemperatureSlider is null) return;
        TemperatureValue.Text = TemperatureSlider.Value.ToString("F2");
    }

    /// <summary>用户自定义的上下文 token 上限（0 = 自动，按可用内存）。</summary>
    private int GetContextTokensOverride()
    {
        if (ContextTokensBox is null) return 0;
        if (double.IsNaN(ContextTokensBox.Value)) return 0;
        return (int)Math.Clamp(ContextTokensBox.Value, 0, LlmContextBudget.MaxBudgetTokens);
    }

    private void ContextTokensBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suspendUiEvents) return;
        AppSettings.Set("AiPlayground_ContextTokens", GetContextTokensOverride().ToString());
        UpdateContextHint();
        // 上下文预算只在加载时生效 → 换值后重载模型（丢给后台，界面不阻塞）
        if (_current?.Task == AiTaskKind.Chat)
            _ = ReloadLlmForContextChangeAsync();
    }

    /// <summary>更新「上下文」行的提示，展示用户值与内存安全上限的关系。</summary>
    private void UpdateContextHint()
    {
        if (ContextLabel is null || ChatStats is null) return;
        // 模型已加载时展示实际生效的预算；未加载时提示「自动」。
        if (_llm is not null)
        {
            ContextLabel.Text = string.Format(
                L("AiPlayground_ContextTokensEffective", "上下文 {0}"), _llm.ContextBudgetTokens);
        }
        else
        {
            ContextLabel.Text = L("AiPlayground_ContextTokens", "上下文");
        }
    }

    private async Task ReloadLlmForContextChangeAsync()
    {
        DisposeRunners();
        await EnsureLlmAsync();
        UpdateContextHint();
    }

    // ── 试炼场：图像任务 ────────────────────────────────────────────

    private void ThresholdSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suspendUiEvents) return;
        UpdateThresholdValue();
    }

    private void UpdateThresholdValue()
    {
        // XAML 加载期 Value="50" 会先于 ThresholdValue 元素创建触发事件，必须判空。
        if (ThresholdValue is null || ThresholdSlider is null) return;
        ThresholdValue.Text = (ThresholdSlider.Value / 100.0).ToString("F2");
    }

    private void PickImageButton_Click(object sender, RoutedEventArgs e)
    {
        var file = Win32Dialogs.PickOpen(
            "图片\0*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.tif;*.tiff\0所有文件\0*.*\0\0",
            "选择图片");
        if (!string.IsNullOrEmpty(file)) SetImage(file);
    }

    private void OnFilesDropped(IReadOnlyList<string> files)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (_current is null || _current.Task == AiTaskKind.Chat) return;
            var image = files.FirstOrDefault(IsImageFile);
            if (image is not null) SetImage(image);
        });
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif" or ".tif" or ".tiff";
    }

    private void SetImage(string path)
    {
        try
        {
            var bitmap = new BitmapImage(new Uri(path));
            PreviewImage.Source = bitmap;
            _imagePath = path;
            _imageSize = ImagePreprocess.GetImageSize(path);
            _lastDetections = null;
            DetectOverlay.Children.Clear();
            DropHint.Visibility = Visibility.Collapsed;
            ImageInfoText.Text = $"{Path.GetFileName(path)} · {_imageSize.W}×{_imageSize.H}";
            VisionStatus.Text = "";
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync(L("AiPlayground_ImageFailed", "无法打开图片"), ex);
        }
    }

    private async void RunInferenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _current is null) return;
        if (AiRuntimeService.UnsupportedReason is { } reason)
        {
            await ShowMessageAsync(L("AiPlayground_NotSupported", "不可用"), reason);
            return;
        }
        if (string.IsNullOrEmpty(_imagePath))
        {
            VisionStatus.Text = L("AiPlayground_NeedImage", "请先选择图片");
            return;
        }

        _busy = true;
        RunInferenceButton.IsEnabled = false;
        try
        {
            switch (_current.Task)
            {
                case AiTaskKind.ImageClassification:
                    await RunClassificationAsync();
                    break;
                case AiTaskKind.ObjectDetection:
                    await RunDetectionAsync();
                    break;
                case AiTaskKind.ImageEmbedding:
                    await RunEmbeddingAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            VisionStatus.Text = "";
            await ShowErrorAsync(L("AiPlayground_InferenceFailed", "推理失败"), ex);
        }
        finally
        {
            _busy = false;
            RunInferenceButton.IsEnabled = true;
        }
    }

    private async Task<ClassificationRunner> EnsureClassifierAsync(AiModelEntry entry)
    {
        if (_classifier is not null && _runnerModelId == entry.Id) return _classifier;
        DisposeRunners();
        var dir = AiModelLibrary.GetEntryDataDir(entry);
        var modelFile = entry.Custom?.Kind == "single-file" ? entry.Custom.Path : AiModelLibrary.FindVisionModelFile(dir);
        if (string.IsNullOrEmpty(modelFile))
            throw new InvalidOperationException("模型目录中没有 .onnx 文件。");
        var timmCrop = entry.Preset?.Family == "resnet";
        var runner = await LoadVisionRunnerAsync(
            modelFile, entry.Id, timmCrop, AiTaskKind.ImageClassification,
            result => new ClassificationRunner(result, dir, timmCrop));
        _classifier = runner;
        _runnerModelId = entry.Id;
        return runner;
    }

    private async Task<DetectionRunner> EnsureDetectorAsync(AiModelEntry entry)
    {
        if (_detector is not null && _runnerModelId == entry.Id) return _detector;
        DisposeRunners();
        var dir = AiModelLibrary.GetEntryDataDir(entry);
        var modelFile = entry.Custom?.Kind == "single-file" ? entry.Custom.Path : AiModelLibrary.FindVisionModelFile(dir);
        if (string.IsNullOrEmpty(modelFile))
            throw new InvalidOperationException("模型目录中没有 .onnx 文件。");
        var family = entry.Preset?.Family == "yolo" || entry.DisplayName.Contains("YOLO", StringComparison.OrdinalIgnoreCase)
            ? "yolo"
            : "detr";
        var runner = await LoadVisionRunnerAsync(
            modelFile, entry.Id, false, AiTaskKind.ObjectDetection,
            result => new DetectionRunner(result, dir, family));
        _detector = runner;
        _runnerModelId = entry.Id;
        return runner;
    }

    private async Task<EmbeddingRunner> EnsureEmbedderAsync(AiModelEntry entry)
    {
        if (_embedder is not null && _runnerModelId == entry.Id) return _embedder;
        DisposeRunners();
        var dir = AiModelLibrary.GetEntryDataDir(entry);
        var modelFile = entry.Custom?.Kind == "single-file" ? entry.Custom.Path : AiModelLibrary.FindVisionModelFile(dir);
        if (string.IsNullOrEmpty(modelFile))
            throw new InvalidOperationException("模型目录中没有 .onnx 文件。");
        var runner = await LoadVisionRunnerAsync(
            modelFile, entry.Id, false, AiTaskKind.ImageEmbedding,
            result => new EmbeddingRunner(result));
        _embedder = runner;
        _runnerModelId = entry.Id;
        return runner;
    }

    private async Task<TRunner> LoadVisionRunnerAsync<TRunner>(
        string modelFile,
        string cacheKey,
        bool timmCrop,
        AiTaskKind task,
        Func<AiSessionResult, TRunner> factory)
    {
        var device = _selectedDevice;
        var (epName, deviceType, requiresProbe) = AiRuntimeService.ResolveVisionTarget(
            device?.EpName, device?.DeviceType);

        // 非 QNN 的 NPU 会因原生编译器 abort/访问违例而杀进程，必须先经隔离子进程探测。
        if (requiresProbe && epName is not null)
        {
            var probeShape = AiRuntimeService.ResolveStaticShape(modelFile, task);
            VisionStatus.Text = L("AiPlayground_NpuProbing", "正在探测 NPU 兼容性（首次较慢）…");
            var probe = await AiRuntimeService.EnsureProbedAsync(modelFile, epName, "NPU", probeShape, CancellationToken.None);
            if (!probe.Ok)
            {
                VisionStatus.Text = string.Format(
                    L("AiPlayground_NpuProbeFailed", "NPU 探测未通过，已回退 GPU/CPU：{0}"), probe.Error);
                // 探测失败 → 显式换到安全设备（GPU 优先 DirectML），绝不回 null（策略路径可能再选 NPU）
                var fallback = AiRuntimeService.ResolveVisionFallback();
                epName = fallback.EpName;
                deviceType = fallback.DeviceType;
            }
        }

        var shape = AiRuntimeService.ResolveStaticShape(modelFile, task);

        var session = await Task.Run(() => AiRuntimeService.CreateVisionSession(
            modelFile,
            cacheKey,
            epName,
            deviceType,
            shape,
            status => _dispatcher.TryEnqueue(() => VisionStatus.Text = status)));
        UpdateRuntimeChip(session.RuntimeNote);
        return factory(session);
    }

    private async Task RunClassificationAsync()
    {
        var entry = _current!;
        VisionStatus.Text = L("AiPlayground_LoadingModel", "正在加载模型（首次较慢）…");
        var runner = await EnsureClassifierAsync(entry);
        VisionStatus.Text = L("AiPlayground_Inferencing", "正在推理…");
        var path = _imagePath!;
        var results = await Task.Run(() => runner.Run(path));
        RenderClassificationResults(results);
        VisionStatus.Text = DeviceDoneText(runner.RuntimeNote);
    }

    private static string DeviceDoneText(string? note) => string.IsNullOrEmpty(note)
        ? string.Format(L("AiPlayground_InferenceDone", "完成 · {0:HH:mm:ss}"), DateTime.Now)
        : string.Format(L("AiPlayground_InferenceDoneDevice", "完成 · {0:HH:mm:ss} · {1}"), DateTime.Now, note);

    private void RenderClassificationResults(List<ClassificationResult> results)
    {
        ResultsPanel.Children.Clear();
        ResultsPanel.Children.Add(new TextBlock
        {
            Text = L("AiPlayground_Top5", "分类结果（Top 5）"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        foreach (var result in results)
        {
            var row = new StackPanel { Spacing = 4 };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = result.Label, TextWrapping = TextWrapping.Wrap };
            var score = new TextBlock
            {
                Text = $"{result.Score:P1}",
                Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            };
            Grid.SetColumn(score, 1);
            head.Children.Add(label);
            head.Children.Add(score);
            row.Children.Add(head);
            row.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = result.Score, Height = 6 });
            ResultsPanel.Children.Add(row);
        }
    }

    private async Task RunDetectionAsync()
    {
        var entry = _current!;
        VisionStatus.Text = L("AiPlayground_LoadingModel", "正在加载模型（首次较慢）…");
        var runner = await EnsureDetectorAsync(entry);
        VisionStatus.Text = L("AiPlayground_Inferencing", "正在推理…");
        var path = _imagePath!;
        PersistDetectOptions();
        var options = _detectOptions.Clone();
        var results = await Task.Run(() => runner.Run(path, options));
        _lastDetections = results;
        RenderDetectionResults(results);
        RedrawOverlay();
        VisionStatus.Text = DeviceDoneText(runner.RuntimeNote);
    }

    private void RenderDetectionResults(List<DetectionItem> results)
    {
        ResultsPanel.Children.Clear();
        ResultsPanel.Children.Add(new TextBlock
        {
            Text = string.Format(L("AiPlayground_DetectCount", "检测到 {0} 个目标"), results.Count),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        foreach (var item in results.Take(50))
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Background = Res("CardBackgroundFillColorSecondaryBrush", Microsoft.UI.Colors.Transparent),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = item.Label, TextWrapping = TextWrapping.Wrap };
            var score = new TextBlock
            {
                Text = $"{item.Score:P0}",
                Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            };
            Grid.SetColumn(score, 1);
            grid.Children.Add(label);
            grid.Children.Add(score);
            row.Child = grid;
            ResultsPanel.Children.Add(row);
        }
    }

    private void RedrawOverlay()
    {
        DetectOverlay.Children.Clear();
        if (_lastDetections is null || _imageSize.W <= 0 || _imageSize.H <= 0) return;
        double canvasWidth = ImageStageInner.ActualWidth;
        double canvasHeight = ImageStageInner.ActualHeight;
        if (canvasWidth < 2 || canvasHeight < 2) return;

        double scale = Math.Min(canvasWidth / _imageSize.W, canvasHeight / _imageSize.H);
        double drawWidth = _imageSize.W * scale;
        double drawHeight = _imageSize.H * scale;
        double offsetX = (canvasWidth - drawWidth) / 2;
        double offsetY = (canvasHeight - drawHeight) / 2;

        // 类别直方图供 count(class_id) 规则使用
        var histogram = _lastDetections
            .GroupBy(i => i.ClassId.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var item in _lastDetections)
        {
            var ruleCtx = new DetectionRuleContext(item, histogram);
            var action = _drawRules.Count == 0 ? null : RuleEngine.MatchFirst(_drawRules, ruleCtx);
            if (action?.Visible == false) continue;

            var stroke = BrushFromHex(action?.Stroke)
                         ?? new SolidColorBrush(ParseColor(RuleStore.ColorForLabel(item.RawLabel)));
            var thickness = action?.StrokeWidth is { } w ? Math.Clamp(w, 1, 10) : 2;
            var labelText = action?.LabelFormat is { Length: > 0 } fmt
                ? RuleEngine.FormatLabel(fmt, ruleCtx)
                : $"{item.Label} {item.Score:P0}";

            var rectangle = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Stroke = stroke,
                StrokeThickness = thickness,
                Width = Math.Max(1, (item.X2 - item.X1) * scale),
                Height = Math.Max(1, (item.Y2 - item.Y1) * scale),
            };
            Canvas.SetLeft(rectangle, offsetX + item.X1 * scale);
            Canvas.SetTop(rectangle, offsetY + item.Y1 * scale);
            DetectOverlay.Children.Add(rectangle);

            var tag = new Border
            {
                Background = stroke,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = labelText,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                },
            };
            Canvas.SetLeft(tag, offsetX + item.X1 * scale);
            Canvas.SetTop(tag, Math.Max(0, offsetY + item.Y1 * scale - 18));
            DetectOverlay.Children.Add(tag);
        }
    }

    private static SolidColorBrush? BrushFromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex[0] != '#') return null;
        var s = hex[1..];
        try
        {
            byte r, g, b;
            if (s.Length == 6)
            {
                r = Convert.ToByte(s[..2], 16);
                g = Convert.ToByte(s.Substring(2, 2), 16);
                b = Convert.ToByte(s.Substring(4, 2), 16);
            }
            else if (s.Length == 3)
            {
                r = Convert.ToByte(new string(s[0], 2), 16);
                g = Convert.ToByte(new string(s[1], 2), 16);
                b = Convert.ToByte(new string(s[2], 2), 16);
            }
            else return null;
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        }
        catch
        {
            return null;
        }
    }

    private static Windows.UI.Color ParseColor(string hex) => BrushFromHex(hex)?.Color ?? Microsoft.UI.Colors.OrangeRed;

    private async Task RunEmbeddingAsync()
    {
        var entry = _current!;
        VisionStatus.Text = L("AiPlayground_LoadingModel", "正在加载模型（首次较慢）…");
        var runner = await EnsureEmbedderAsync(entry);
        VisionStatus.Text = L("AiPlayground_Inferencing", "正在推理…");
        var path = _imagePath!;
        var vector = await Task.Run(() => runner.Embed(path));
        _gallery.RemoveAll(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        _gallery.Add((path, vector));
        RenderEmbeddingResults();
        VisionStatus.Text = string.Format(
                L("AiPlayground_EmbeddingDone", "特征维度 {0} · 已加入对比（{1} 张）"), vector.Length, _gallery.Count)
            + (string.IsNullOrEmpty(runner.RuntimeNote) ? "" : $" · {runner.RuntimeNote}");
    }

    private void RenderEmbeddingResults()
    {
        ResultsPanel.Children.Clear();
        ResultsPanel.Children.Add(new TextBlock
        {
            Text = string.Format(L("AiPlayground_GalleryCount", "对比图（{0}）"), _gallery.Count),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        foreach (var (path, vector) in _gallery)
        {
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = $"{Path.GetFileName(path)} · {vector.Length} 维",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        if (_gallery.Count < 2)
        {
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = L("AiPlayground_EmbeddingHint", "再选择另一张图片并运行，即可得到相似度。"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            });
            return;
        }
        ResultsPanel.Children.Add(new TextBlock
        {
            Text = L("AiPlayground_Similarity", "余弦相似度"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 8, 0, 0),
        });
        for (int i = 0; i < _gallery.Count; i++)
        {
            for (int j = i + 1; j < _gallery.Count; j++)
            {
                var similarity = AiMath.CosineSimilarity(_gallery[i].Vector, _gallery[j].Vector);
                ResultsPanel.Children.Add(new TextBlock
                {
                    Text = $"{Path.GetFileName(_gallery[i].Path)} ↔ {Path.GetFileName(_gallery[j].Path)}：{similarity:F3}",
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }
    }

    private void ClearGalleryButton_Click(object sender, RoutedEventArgs e)
    {
        _gallery.Clear();
        RenderEmbeddingResults();
    }

    // ── 引擎与设备 ──────────────────────────────────────────────────

    private async Task RefreshEnginePaneAsync()
    {
        var reason = AiRuntimeService.UnsupportedReason;
        if (reason is not null)
        {
            UnsupportedBanner.Message = reason;
            UnsupportedBanner.IsOpen = true;
        }
        else
        {
            UnsupportedBanner.IsOpen = false;
        }

        EpHintText.Text = AiRuntimeService.CanInstallVendorEps
            ? L("AiPlayground_EpHint", "加速包从微软商店按需下载（需要网络），安装后注册到 ONNX Runtime 即可在 NPU / GPU 上推理。")
            : L("AiPlayground_EpHintLowOs", "当前系统低于 Windows 11 24H2：内置 CPU 与 DirectML(GPU) 可用；厂商 NPU/GPU 加速包需要更高系统版本。");

        // 设备信息
        DeviceInfoPanel.Children.Clear();
        string npuName = "", cpuName = "";
        string? npuTops = null;
        List<string> gpus = [];
        await Task.Run(() =>
        {
            npuName = AiRuntimeService.DetectNpuName() ?? "";
            cpuName = AiRuntimeService.DetectCpuName() ?? "";
            npuTops = Services.NpuCatalog.LookupTops(npuName, cpuName);
            gpus = AiRuntimeService.DetectGpuNames().ToList();
        });
        var (totalMb, availableMb) = AiRuntimeService.GetMemoryMb();
        AddDeviceRow(L("AiPlayground_Cpu", "处理器"), cpuName);
        foreach (var gpu in gpus)
            AddDeviceRow(L("AiPlayground_Gpu", "显卡"), gpu);
        var npuEpRegistered = AiRuntimeService.DescribeEpDevices()
            .Any(d => string.Equals(d.DeviceType, "NPU", StringComparison.OrdinalIgnoreCase));
        AddDeviceRow(L("AiPlayground_Npu", "NPU"),
            string.IsNullOrEmpty(npuName)
                ? L("AiPlayground_NoNpuShort", "未检测到")
                : $"{npuName}{(string.IsNullOrEmpty(npuTops) ? "" : $" · {npuTops}")}"
                  + (npuEpRegistered ? "" : " · " + L("AiPlayground_NpuNeedEp", "未启用（安装加速包后可用于推理）")));
        AddDeviceRow(L("AiPlayground_Ram", "内存"),
            totalMb > 0 ? $"{totalMb / 1024.0:F1} GB（可用 {availableMb / 1024.0:F1} GB）" : "—");
        AddDeviceRow(L("AiPlayground_Os", "系统"), $"Windows build {AiRuntimeService.GetOsBuild()}");
        AddDeviceRow(L("AiPlayground_Runtime", "推理运行时"),
            $"Windows ML · ONNX Runtime（{(Environment.Is64BitProcess ? Environment.OSVersion.VersionString : "?")}）");

        // 加速包列表
        EpListPanel.Children.Clear();
        var providers = AiRuntimeService.GetCatalogProviders();
        if (providers.Count == 0)
        {
            EpListPanel.Children.Add(new TextBlock
            {
                Text = AiRuntimeService.CatalogError is { } catalogError
                    ? string.Format(L("AiPlayground_EpLoadFailed", "无法读取加速包列表：{0}"), catalogError)
                    : L("AiPlayground_EpNone", "未发现可用的加速包信息。"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            });
        }
        foreach (var provider in providers)
            EpListPanel.Children.Add(BuildEpCard(provider));

        UpdateDeviceTable();
        UpdateCacheText();
        UpdateModeHint();
    }

    private void AddDeviceRow(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
        });
        var valueText = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        DeviceInfoPanel.Children.Add(grid);
    }

    private FrameworkElement BuildEpCard(Microsoft.Windows.AI.MachineLearning.ExecutionProvider provider)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Background = Res("CardBackgroundFillColorDefaultBrush", Microsoft.UI.Colors.Transparent),
            BorderBrush = Res("CardStrokeColorDefaultBrush", Microsoft.UI.Colors.Gray),
            BorderThickness = new Thickness(1),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stateText = provider.ReadyState switch
        {
            Microsoft.Windows.AI.MachineLearning.ExecutionProviderReadyState.Ready => L("AiPlayground_EpReadyState", "已就绪并已注册"),
            Microsoft.Windows.AI.MachineLearning.ExecutionProviderReadyState.NotReady => L("AiPlayground_EpReadyNotRegistered", "已安装（未注册）"),
            _ => L("AiPlayground_EpNotInstalled", "未安装"),
        };
        var left = new StackPanel { Spacing = 4 };
        left.Children.Add(new TextBlock
        {
            Text = provider.Name,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        left.Children.Add(new TextBlock
        {
            Text = stateText,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
        });
        var purpose = DescribeEpPurpose(provider.Name);
        if (purpose.Length > 0)
        {
            left.Children.Add(new TextBlock
            {
                Text = purpose,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var button = new Button
        {
            Content = provider.ReadyState switch
            {
                Microsoft.Windows.AI.MachineLearning.ExecutionProviderReadyState.Ready => L("AiPlayground_EpRegistered", "已注册"),
                Microsoft.Windows.AI.MachineLearning.ExecutionProviderReadyState.NotReady => L("AiPlayground_EpEnable", "启用并注册"),
                _ => L("AiPlayground_EpInstall", "下载并安装"),
            },
            IsEnabled = provider.ReadyState != Microsoft.Windows.AI.MachineLearning.ExecutionProviderReadyState.Ready
                        && AiRuntimeService.CanInstallVendorEps,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            EpBusyRing.IsActive = true;
            EpStatusText.Text = string.Format(L("AiPlayground_EpWorking", "正在处理 {0}…"), provider.Name);
            var (ok, message) = await AiRuntimeService.EnsureProviderAsync(provider);
            EpBusyRing.IsActive = false;
            EpStatusText.Text = message;
            await RefreshEnginePaneAsync();
            PopulateDeviceCombo();
            _ = UpdateDeviceChipsAsync();
        };

        Grid.SetColumn(button, 1);
        grid.Children.Add(left);
        grid.Children.Add(button);
        card.Child = grid;
        return card;
    }

    private void UpdateDeviceTable()
    {
        DeviceTablePanel.Children.Clear();
        DeviceTablePanel.Children.Add(new TextBlock
        {
            Text = L("AiPlayground_DeviceTableCaption",
                "CPU 与 DirectML（GPU）为内置；NPU / 厂商 GPU 的加速包安装并注册后才会出现在这里。"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
        });

        var devices = AiRuntimeService.DescribeEpDevices();
        if (devices.Count == 0)
        {
            DeviceTablePanel.Children.Add(new TextBlock
            {
                Text = AiRuntimeService.EnvironmentError is { } envError
                    ? string.Format(L("AiPlayground_RuntimeLoadFailed", "无法加载推理运行时：{0}"), envError)
                    : L("AiPlayground_DeviceTableEmpty", "尚未获取到 EP 设备（可能原因：x86 版本或系统版本过低）。"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            });
            return;
        }
        foreach (var device in devices)
        {
            DeviceTablePanel.Children.Add(new TextBlock
            {
                Text = $"{device.EpName} · {device.DeviceType}（{device.Vendor}）",
            });
        }
        // 硬件已检测到 NPU 但未注册对应 EP：给出明确说明，避免"看不到 NPU"的困惑
        var hasNpuEp = devices.Any(d => string.Equals(d.DeviceType, "NPU", StringComparison.OrdinalIgnoreCase));
        if (!hasNpuEp && !string.IsNullOrEmpty(_npuHardwareName))
        {
            DeviceTablePanel.Children.Add(new TextBlock
            {
                Text = string.Format(
                    L("AiPlayground_NpuUnregistered", "{0} · NPU · 未启用（安装对应加速包后出现在这里）"),
                    _npuHardwareName),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Res("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            });
        }
    }

    private void UpdateCacheText()
    {
        var (count, bytes) = AiRuntimeService.GetCompiledCacheInfo();
        CacheText.Text = count == 0
            ? L("AiPlayground_CacheEmpty", "暂无缓存（NPU 推理时自动生成）")
            : string.Format(L("AiPlayground_CacheInfo", "{0} 个文件 · {1}"),
                count, DownloadQueueService.FormatSize(bytes));
    }

    private async void InstallAllButton_Click(object sender, RoutedEventArgs e)
    {
        InstallAllButton.IsEnabled = false;
        EpBusyRing.IsActive = true;
        EpStatusText.Text = L("AiPlayground_EpInstallingAll", "正在下载并注册推荐加速包（可能需要几分钟）…");
        try
        {
            var (_, message) = await AiRuntimeService.EnsureAndRegisterCertifiedAsync();
            EpStatusText.Text = message;
            await RefreshEnginePaneAsync();
            PopulateDeviceCombo();
            _ = UpdateDeviceChipsAsync();
        }
        finally
        {
            EpBusyRing.IsActive = false;
            InstallAllButton.IsEnabled = true;
        }
    }

    private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync(L("AiPlayground_ClearCache", "清理缓存"),
                L("AiPlayground_ClearCacheConfirm", "将删除 NPU 编译缓存（下次推理时会重新编译，可能需要几分钟）。")))
            return;
        AiRuntimeService.ClearCompiledCache();
        UpdateCacheText();
    }

    // ── 通用辅助 ────────────────────────────────────────────────────

    private static Brush Res(string key, Windows.UI.Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
            return brush;
        return new SolidColorBrush(fallback);
    }

    private async Task<bool> ConfirmAsync(string title, string message, string? primaryText = null)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryText ?? L("AiPlayground_OK", "确定"),
            CloseButtonText = L("AiPlayground_Cancel", "取消"),
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = L("AiPlayground_OK", "确定"),
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        await dialog.ShowAsync();
    }

    private async Task ShowErrorAsync(string title, Exception ex)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                Content = new TextBlock
                {
                    Text = ex.Message,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                },
            },
            CloseButtonText = L("AiPlayground_OK", "确定"),
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        await dialog.ShowAsync();
    }
}
