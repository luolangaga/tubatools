using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>队列项状态。</summary>
public enum QueueState
{
    Waiting,
    Running,
    Done,
    Failed,
    Skipped
}

/// <summary>文件队列中的一项（支持绑定更新）。</summary>
public sealed class QueueItem : INotifyPropertyChanged
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required string SizeText { get; init; }
    public required SourceCategory Category { get; init; }
    public required string CategoryGlyph { get; init; }

    private QueueState _state = QueueState.Waiting;
    private string _statusText = "等待转换";
    private Brush? _statusBrush;
    private string _detail = "";

    public QueueState State
    {
        get => _state;
        private set { _state = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State))); }
    }
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText))); }
    }
    public Brush? StatusBrush
    {
        get => _statusBrush;
        private set { _statusBrush = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush))); }
    }
    public string Detail
    {
        get => _detail;
        private set { _detail = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailVisibility))); }
    }
    public Visibility DetailVisibility => string.IsNullOrEmpty(_detail) ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void SetState(QueueState state, string status, Brush? brush, string detail = "")
    {
        State = state;
        StatusText = status;
        StatusBrush = brush;
        Detail = detail;
    }

    /// <summary>主题切换后按当前状态重新取状态色画刷（代码构建的画刷不随主题自动刷新）。</summary>
    internal void RefreshStatusBrush() => StatusBrush = FormatConverterPage.StatusBrushes.For(_state);
}

public sealed partial class FormatConverterPage : Page
{
    private sealed record ConvertSettings(
        FormatParamValues Params,
        int ZipLevel, bool ExportZip, bool MergeImages, bool CombineImagesToPdf,
        int DocImageEdge, int DocJpgQuality, string DocPageRange, string DocRenderMode,
        int[] IcoSizes);

    /// <summary>目标格式专属参数的一行控件（按 FormatParam.Kind 生成）。</summary>
    private sealed class ParamRow
    {
        public required FormatParam Param { get; init; }
        public FrameworkElement Root { get; set; } = null!;
        public Slider? Slider;
        public NumberBox? Box;
        public ComboBox? Combo;
        public ToggleSwitch? Toggle;

        public double Value => Param.Kind switch
        {
            FormatParamKind.Combo => Combo?.SelectedItem is ComboBoxItem { Tag: double v } ? v : Param.Default,
            FormatParamKind.Toggle => Toggle?.IsOn == true ? 1 : 0,
            _ => Box is { Value: var value } && !double.IsNaN(value) ? value : Param.Default
        };
    }

    /// <summary>对话框通用选项的控件句柄（导出 / ZIP / ICO 尺寸 / 文档参数）。</summary>
    private sealed class DialogUi
    {
        public FrameworkElement? ZipPanel;
        public Slider? ZipSlider;
        public ToggleSwitch? ExportZipToggle;
        public CheckBox? MergeImagesCheck;
        public CheckBox? CombineImagesCheck;
        public FrameworkElement? IcoSizesPanel;
        public CheckBox[]? IcoChecks;
        public FrameworkElement? DocImagePanel;
        public NumberBox? DocMaxEdgeBox;
        public TextBox? DocRangeBox;
        public ComboBox? DocRenderCombo;
        public FrameworkElement? DocJpgPanel;
        public Slider? DocJpgSlider;
    }

    private readonly ObservableCollection<QueueItem> _queue = [];
    private SourceCategory _category = SourceCategory.Unsupported;
    private DocumentEngineService? _docEngine;
    private DocumentConvertService? _docService;
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _engineTimer;
    private bool _dropSubscribed;
    private string? _resultDir;
    private List<string> _lastOutputs = [];
    private string? _zipSummary;

    internal static class StatusBrushes
    {
        public static Brush? Waiting;
        public static Brush? Running;
        public static Brush? Done;
        public static Brush? Failed;
        public static Brush? Skipped;
        public static bool Initialized;

        public static void Init(Page page)
        {
            if (Initialized) return;
            // 队列状态语义色：等待 = Neutral（次要文字）/ 转换中 = Accent / 完成 = Success / 失败 = Critical / 跳过 = 三级文字
            Waiting = Resolve(page, "TextFillColorSecondaryBrush") ?? new SolidColorBrush(ThemeColors.Neutral);
            Running = Resolve(page, "AccentFillColorDefaultBrush") ?? new SolidColorBrush(ThemeColors.AccentBlue);
            Done = Resolve(page, "SystemFillColorSuccessBrush") ?? new SolidColorBrush(ThemeColors.AccentGreen);
            Failed = Resolve(page, "SystemFillColorCriticalBrush") ?? new SolidColorBrush(ThemeColors.AccentRed);
            Skipped = Resolve(page, "TextFillColorTertiaryBrush") ?? new SolidColorBrush(ThemeColors.DimText);
            Initialized = true;
        }

        /// <summary>主题切换后按新主题重新解析状态色。</summary>
        public static void Reset() => Initialized = false;

        /// <summary>按队列状态取当前主题下的状态色画刷。</summary>
        public static Brush? For(QueueState state) => state switch
        {
            QueueState.Running => Running,
            QueueState.Done => Done,
            QueueState.Failed => Failed,
            QueueState.Skipped => Skipped,
            _ => Waiting
        };

        private static Brush? Resolve(Page page, string key)
        {
            try
            {
                if (page.Resources.TryGetValue(key, out var v) && v is Brush b) return b;
                if (Application.Current.Resources.TryGetValue(key, out var v2) && v2 is Brush b2) return b2;
            }
            catch { }
            return null;
        }
    }

    public FormatConverterPage()
    {
        InitializeComponent();
        StatusBrushes.Init(this);
        // 代码解析的状态色画刷不会随主题自动刷新，切换主题后按当前状态回填队列
        ActualThemeChanged += (_, _) =>
        {
            StatusBrushes.Reset();
            StatusBrushes.Init(this);
            foreach (var item in _queue) item.RefreshStatusBrush();
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RefreshEngineCards();
    }

    // ══════════════ 生命周期与拖放 ══════════════

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 管理员权限下接收 explorer 拖放（UIPI 绕过，钩子为全局共享单例）
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!);
        if (hwnd != IntPtr.Zero) Win32DropHelper.EnsureInstalled(hwnd);
        if (!_dropSubscribed)
        {
            _dropSubscribed = true;
            Win32DropHelper.FilesDropped += OnFilesDropped;
        }

        _docEngine = new DocumentEngineService(DocWeb);
        _docService = new DocumentConvertService(_docEngine);

        _engineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _engineTimer.Tick += (_, _) => RefreshEngineCards();
        _engineTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_dropSubscribed)
        {
            _dropSubscribed = false;
            Win32DropHelper.FilesDropped -= OnFilesDropped;
        }
        _engineTimer?.Stop();
        _cts?.Cancel();
        // 页面默认不缓存（NavigationCacheMode=Disabled），卸载即废弃：关闭宿主 WebView2
        _docEngine?.Close();
        _docEngine = null;
        _docService = null;
    }

    private void OnFilesDropped(IReadOnlyList<string> files)
    {
        var paths = files.Where(f => File.Exists(f)).ToList();
        if (paths.Count == 0) return;
        DispatcherQueue.TryEnqueue(() => AcceptFiles(paths));
    }

    // ══════════════ 源文件 ══════════════

    /// <summary>接收一批文件：全部进入队列（不支持类型标记为仅可 ZIP 打包），自动弹出格式选择。</summary>
    private void AcceptFiles(IReadOnlyList<string> paths)
    {
        if (_cts is not null && ProgressPanel.Visibility == Visibility.Visible)
        {
            ShowToast("正在转换中", "请等待当前转换完成后再添加文件", InfoBarSeverity.Warning);
            return;
        }

        _queue.Clear();
        var unsupported = 0;
        var mixed = 0;
        SourceCategory? firstCategory = null;

        foreach (var p in paths)
        {
            var cat = FormatConvertCatalog.Classify(p);
            if (cat == SourceCategory.Unsupported) unsupported++;
            else if (firstCategory is null) firstCategory = cat;
            else if (cat != firstCategory) mixed++;

            var item = new QueueItem
            {
                FullPath = p,
                Name = Path.GetFileName(p),
                SizeText = DownloadQueueService.FormatSize(new FileInfo(p).Length),
                Category = cat,
                CategoryGlyph = CategoryGlyph(cat)
            };
            if (cat == SourceCategory.Unsupported)
                item.SetState(QueueState.Waiting, "等待（仅支持 ZIP 打包）", StatusBrushes.Waiting);
            _queue.Add(item);
        }

        _category = firstCategory ?? SourceCategory.Unsupported;

        if (unsupported > 0)
            ShowToast("部分文件类型未知",
                $"{unsupported} 个文件无法识别格式，仍可打包为 ZIP 压缩包", InfoBarSeverity.Informational);
        if (mixed > 0)
            ShowToast("包含多种类别",
                $"批量转换一次只处理同类文件（当前按「{CategoryName(_category)}」转换），其余文件仅可 ZIP 打包", InfoBarSeverity.Informational);

        ResultPanel.Visibility = Visibility.Collapsed;
        UpdateQueuePanel();
        _ = AskFormatAsync();
    }

    private void UpdateQueuePanel()
    {
        if (_queue.Count == 0)
        {
            QueuePanel.Visibility = Visibility.Collapsed;
            return;
        }
        QueuePanel.Visibility = Visibility.Visible;
        QueueList.ItemsSource = _queue;
        QueueTitleText.Text = $"文件队列（{_queue.Count} 个 · {CategoryName(_category)}）";
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var paths = Win32Dialogs.PickOpenMultiple(
            "所有可转换文件\0*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.mp3;*.wav;*.flac;*.m4a;*.ogg;*.opus;*.wma;*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp;*.tif;*.tiff;*.heic;*.avif;*.pdf;*.doc;*.docx;*.wps;*.rtf;*.odt;*.xls;*.xlsx;*.et;*.ods;*.csv;*.ppt;*.pptx;*.dps;*.odp;*.md;*.txt;*.log;*.html;*.htm;*.json\0所有文件\0*.*\0\0",
            "选择要转换的文件");
        if (paths.Count == 0) return;
        AcceptFiles(paths);
    }

    private void RemoveItemBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: QueueItem item })
        {
            _queue.Remove(item);
            if (_queue.Count == 0)
            {
                QueuePanel.Visibility = Visibility.Collapsed;
                _category = SourceCategory.Unsupported;
            }
            else
            {
                var first = _queue.FirstOrDefault(i => i.Category != SourceCategory.Unsupported);
                _category = first?.Category ?? SourceCategory.Unsupported;
                UpdateQueuePanel();
            }
        }
    }

    private void ClearQueueBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ProgressPanel.Visibility == Visibility.Visible) return;
        _queue.Clear();
        QueuePanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        _category = SourceCategory.Unsupported;
    }

    // ══════════════ 格式对话框 ══════════════

    private async Task AskFormatAsync()
    {
        if (_queue.Count == 0) return;

        var formats = BuildFormatList();
        if (formats.Count == 0)
        {
            ShowToast("无可转换格式", "该文件类型暂无可转换的目标格式", InfoBarSeverity.Warning);
            return;
        }

        var panel = new StackPanel { Spacing = 12, Width = 500 };

        var sourceLabel = _queue.Count == 1
            ? _queue[0].Name
            : $"{_queue[0].Name} 等 {_queue.Count} 个文件";
        panel.Children.Add(new TextBlock
        {
            Text = sourceLabel,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = "选择目标格式：", FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush")
        });

        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 184
        };
        foreach (var fmt in formats)
        {
            grid.Items.Add(new GridViewItem
            {
                Tag = fmt,
                Content = new StackPanel
                {
                    Width = 76, Spacing = 2,
                    Children =
                    {
                        new FontIcon
                        {
                            Glyph = FormatGlyph(fmt),
                            FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = fmt.Name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextAlignment = TextAlignment.Center
                        }
                    }
                }
            });
        }
        panel.Children.Add(grid);
        grid.SelectedIndex = 0;

        // 输出预览（提前声明：下方事件处理器引用的 UpdatePreview 会用到它）
        var outPreview = new TextBlock
        {
            FontSize = 12, Foreground = ThemeBrush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap
        };

        // 目标格式专属参数（随所选格式重建：滑块 + 数值输入框 / 下拉框 / 开关）
        var paramsTitle = new TextBlock
        {
            Text = "格式参数：", FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush"),
            Visibility = Visibility.Collapsed
        };
        var paramsHost = new StackPanel { Spacing = 12 };
        var paramsSummary = new TextBlock
        {
            FontSize = 12, Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed
        };
        panel.Children.Add(paramsTitle);
        panel.Children.Add(paramsHost);
        panel.Children.Add(paramsSummary);

        var ui = new DialogUi();
        var paramRows = new List<ParamRow>();
        FormatOption? renderedTarget = null;

        // 导出选项：把转换产物整合为 ZIP / 多页图片合并为一张长图 / 多张图片合成一份 PDF
        var exportZip = new ToggleSwitch
        {
            Header = "导出为 ZIP 压缩包（把输出的一个或多个文件整合打包）",
            OffContent = "关闭", OnContent = "开启"
        };
        var mergeImages = new CheckBox
        {
            Content = "合并为一张长图（多页文档导出图片时纵向拼接）",
            FontSize = 12, Visibility = Visibility.Collapsed
        };
        var combineImages = new CheckBox
        {
            Content = "把多张图片合成为一份 PDF（按队列顺序逐页拼接）",
            FontSize = 12, Visibility = Visibility.Collapsed
        };
        panel.Children.Add(new TextBlock
        {
            Text = "导出选项：", FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush")
        });
        panel.Children.Add(new StackPanel { Spacing = 8, Children = { exportZip, mergeImages, combineImages } });
        ui.ExportZipToggle = exportZip;
        ui.MergeImagesCheck = mergeImages;
        ui.CombineImagesCheck = combineImages;

        // ZIP 压缩级别（所有类别的 ZIP 目标通用）
        var zipPanel = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        zipPanel.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush"),
            Text = "ZIP 压缩级别（0 = 仅打包不压缩，9 = 压缩最强最慢）"
        });
        var zipSlider = new Slider
        {
            Minimum = 0, Maximum = 9, Value = 6, StepFrequency = 1, IsThumbToolTipEnabled = true
        };
        zipPanel.Children.Add(zipSlider);
        panel.Children.Add(zipPanel);
        ui.ZipPanel = zipPanel;
        ui.ZipSlider = zipSlider;

        // ICO 多尺寸（仅目标为 ICO 时显示）
        var icoPanel = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        icoPanel.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush"),
            Text = "图标尺寸（多选，打包进同一 .ico）"
        });
        var icoWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var icoChecks = new[] { 256, 128, 64, 48, 32, 24, 16 }
            .Select(s => new CheckBox { Content = $"{s}×{s}", Tag = s, IsChecked = true, FontSize = 12 })
            .ToArray();
        foreach (var check in icoChecks) icoWrap.Children.Add(check);
        icoPanel.Children.Add(icoWrap);
        panel.Children.Add(icoPanel);
        ui.IcoSizesPanel = icoPanel;
        ui.IcoChecks = icoChecks;

        // 文档参数：文档类导出 PDF/图片时的渲染选项（OfficeCLI 原生参数）
        {
            var docImagePanel = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
            docImagePanel.Children.Add(new TextBlock
            {
                FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush"),
                Text = "图片清晰度：截图宽度（像素，越大越清晰、文件越大）"
            });
            var docMaxEdgeBox = new NumberBox
            {
                Value = 1600, Minimum = 400, Maximum = 8000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };
            docImagePanel.Children.Add(docMaxEdgeBox);

            docImagePanel.Children.Add(new TextBlock
            {
                FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush"),
                Text = "页码范围（如 1-3,5；留空 = 全部页）"
            });
            var docRangeBox = new TextBox { PlaceholderText = "全部页" };
            docImagePanel.Children.Add(docRangeBox);

            docImagePanel.Children.Add(new TextBlock
            {
                FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush"),
                Text = "渲染模式（原生渲染需本机装有 Word / PowerPoint）"
            });
            var docRenderCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            docRenderCombo.Items.Add(new ComboBoxItem { Content = "自动（优先原生）", Tag = "auto" });
            docRenderCombo.Items.Add(new ComboBoxItem { Content = "原生渲染（Word / PowerPoint）", Tag = "native" });
            docRenderCombo.Items.Add(new ComboBoxItem { Content = "HTML 引擎（内置，无需 Office）", Tag = "html" });
            docRenderCombo.SelectedIndex = 0;
            docImagePanel.Children.Add(docRenderCombo);

            var docJpgPanel = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
            docJpgPanel.Children.Add(new TextBlock
            {
                FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush"),
                Text = "JPG 质量（越大越清晰、文件越小越模糊；仅 JPG 目标生效）"
            });
            var docJpgSlider = new Slider
            {
                Minimum = 50, Maximum = 100, Value = 90, StepFrequency = 1, IsThumbToolTipEnabled = true
            };
            docJpgPanel.Children.Add(docJpgSlider);

            panel.Children.Add(docImagePanel);
            panel.Children.Add(docJpgPanel);
            ui.DocImagePanel = docImagePanel;
            ui.DocMaxEdgeBox = docMaxEdgeBox;
            ui.DocRangeBox = docRangeBox;
            ui.DocRenderCombo = docRenderCombo;
            ui.DocJpgPanel = docJpgPanel;
            ui.DocJpgSlider = docJpgSlider;
        }

        var outLabel = new TextBlock
        {
            Text = "输出：", FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush")
        };
        panel.Children.Add(outLabel);
        panel.Children.Add(outPreview);

        FormatOption SelectedTarget()
            => (grid.SelectedItem as GridViewItem)?.Tag as FormatOption ?? formats[0];

        void RefreshSummary()
        {
            var summary = FormatConvertParams.Summarize(SelectedTarget(), ReadParamValues(paramRows));
            paramsSummary.Text = summary;
            paramsSummary.Visibility = string.IsNullOrEmpty(summary) ? Visibility.Collapsed : Visibility.Visible;
        }

        void OnParamsChanged()
        {
            RefreshSummary();
            UpdatePreview();
        }

        void EnsureParams()
        {
            var target = SelectedTarget();
            if (!ReferenceEquals(target, renderedTarget))
            {
                renderedTarget = target;
                paramRows.Clear();
                paramsHost.Children.Clear();
                var parameters = target.ParamList;
                paramsTitle.Visibility = parameters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (parameters.Count > 0)
                {
                    var (root, rows) = BuildFormatParams(parameters, OnParamsChanged);
                    paramsHost.Children.Add(root);
                    paramRows.AddRange(rows);
                }
            }
            RefreshSummary();
        }

        void UpdatePreview()
        {
            var target = SelectedTarget();
            outPreview.Text = DescribeOutput(target, ui);
            bool isZip = target.Special == ConvertSpecial.ZipArchive;
            bool hasMergeSplit = target.Special is ConvertSpecial.MergePdf or ConvertSpecial.SplitPdf;

            if (ui.ZipPanel is not null)
                ui.ZipPanel.Visibility = isZip ? Visibility.Visible : Visibility.Collapsed;

            // ICO 多尺寸面板仅在目标为 ICO 时显示
            if (ui.IcoSizesPanel is not null)
                ui.IcoSizesPanel.Visibility = target.Ext == ".ico" && !isZip ? Visibility.Visible : Visibility.Collapsed;

            // 导出为 ZIP：合并/拆分/ZIP 打包等特殊操作本身就是压缩/整合，不再叠加
            if (ui.ExportZipToggle is not null)
                ui.ExportZipToggle.Visibility =
                    hasMergeSplit || isZip ? Visibility.Collapsed : Visibility.Visible;

            // 合并为一张长图：仅文档/PDF 类导出图片（含页面压缩包）时可用
            if (ui.MergeImagesCheck is not null)
            {
                bool docImage = _category is SourceCategory.Word or SourceCategory.Excel or SourceCategory.Ppt
                        or SourceCategory.Markdown or SourceCategory.Text or SourceCategory.Html
                        or SourceCategory.Json or SourceCategory.Pdf
                    && ((target.Ext is ".png" or ".jpg" or ".pdf")
                        || (isZip && _category == SourceCategory.Pdf && target.Tag is not null));
                ui.MergeImagesCheck.Visibility =
                    docImage && target.Ext != ".pdf" ? Visibility.Visible : Visibility.Collapsed;

                // 文档参数：清晰度/页码范围/渲染模式（PDF/图片目标），JPG 质量（仅 JPG）
                if (ui.DocImagePanel is not null)
                    ui.DocImagePanel.Visibility = docImage ? Visibility.Visible : Visibility.Collapsed;
                if (ui.DocJpgPanel is not null)
                    ui.DocJpgPanel.Visibility =
                        docImage && target.Ext == ".jpg" ? Visibility.Visible : Visibility.Collapsed;
            }

            // 多张图片合成一份 PDF：仅「图片→PDF」且队列多于一张时可用
            if (ui.CombineImagesCheck is not null)
            {
                bool combine = _category == SourceCategory.Image && target.Ext == ".pdf"
                    && _queue.Count(i => i.Category == SourceCategory.Image) > 1;
                ui.CombineImagesCheck.Visibility = combine ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        grid.SelectionChanged += (_, _) => { EnsureParams(); UpdatePreview(); };
        exportZip.Toggled += (_, _) => UpdatePreview();
        mergeImages.Checked += (_, _) => UpdatePreview();
        mergeImages.Unchecked += (_, _) => UpdatePreview();
        combineImages.Checked += (_, _) => UpdatePreview();
        combineImages.Unchecked += (_, _) => UpdatePreview();

        EnsureParams();
        UpdatePreview();

        var dialog = new ContentDialog
        {
            Title = $"转换为…（{CategoryName(_category)}）",
            // 参数较多的格式（如视频 11 项）会超出弹窗高度，必须放进滚动容器
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = DialogContentMaxHeight(),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled
            },
            PrimaryButtonText = "开始转换",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (_queue.Count == 0) return;

        await RunConversionAsync(SelectedTarget(), BuildConvertSettings(ReadParamValues(paramRows), ui));
    }

    /// <summary>对话框内容的最大高度（跟随窗口高度，避免长内容被裁掉）。</summary>
    private double DialogContentMaxHeight()
    {
        try
        {
            var height = Content.XamlRoot?.Size.Height ?? 0;
            if (height > 0) return Math.Clamp(height - 220, 260, 620);
        }
        catch { }
        return 620;
    }

    /// <summary>构建目标格式列表：类别目标 + PDF 合并/拆分动态项；过滤与源同扩展名的普通目标。</summary>
    private List<FormatOption> BuildFormatList()
    {
        if (_category == SourceCategory.Unsupported)
            return [FormatConvertCatalog.ZipTarget];

        var formats = new List<FormatOption>(FormatConvertCatalog.GetTargetFormats(_category));
        if (_category == SourceCategory.Pdf)
        {
            var pdfCount = _queue.Count(i => i.Category == SourceCategory.Pdf);
            if (pdfCount > 1)
                formats.Insert(0, FormatConvertCatalog.MergePdfTarget);
            else if (pdfCount == 1)
                formats.Insert(0, FormatConvertCatalog.SplitPdfTarget);
        }

        var firstConvertible = _queue.FirstOrDefault(i => i.Category != SourceCategory.Unsupported);
        if (firstConvertible is not null)
        {
            var sourceExt = Path.GetExtension(firstConvertible.FullPath).ToLowerInvariant();
            formats = formats.Where(f => f.IsSpecial || !string.Equals(f.Ext, sourceExt, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return formats;
    }

    private string DescribeOutput(FormatOption target, DialogUi? ui)
    {
        var first = _queue.FirstOrDefault(i => i.Category != SourceCategory.Unsupported) ?? _queue[0];
        var multi = _queue.Count > 1 ? $" 等 {_queue.Count} 个文件" : "";
        var notes = "";
        if (ui?.MergeImagesCheck is { IsChecked: true } check && check.Visibility == Visibility.Visible)
            notes += "（合并为一张长图）";
        if (ui?.ExportZipToggle is { IsOn: true } zip && zip.Visibility == Visibility.Visible)
            notes += "（导出后打包 ZIP）";
        if (ui?.CombineImagesCheck is { IsChecked: true } combine && combine.Visibility == Visibility.Visible)
            notes += "（多张图片合成一份 PDF）";

        switch (target.Special)
        {
            case ConvertSpecial.MergePdf:
                return $"输出：PDF合并_时间戳.pdf（把 {_queue.Count(i => i.Category == SourceCategory.Pdf)} 份 PDF 合并为一个）";
            case ConvertSpecial.SplitPdf:
                return $"输出：每页一个 {Path.GetFileNameWithoutExtension(first.FullPath)}_第N页.pdf";
            case ConvertSpecial.ZipArchive when _category == SourceCategory.Pdf && target.Tag is not null:
                return $"输出：{Path.GetFileNameWithoutExtension(first.FullPath)}_converted.zip（内含每页 {target.Tag.ToUpperInvariant()} 图片）{notes}";
            case ConvertSpecial.ZipArchive:
                return $"输出：{Path.GetFileNameWithoutExtension(first.FullPath)}.zip（压缩级别 0-9 可调，显示压缩前后大小）";
            case ConvertSpecial.OcrText:
                return "输出：.txt（系统 OCR 文字识别，本地完成）";
            case ConvertSpecial.PdfExcel:
                return "输出：.xlsx（提取 PDF 文字层中的表格；扫描版请用 OCR 文本）";
        }

        var output = FormatConvertPlanner.BuildOutputPath(first.FullPath, target.Ext);
        var extra = multi;
        if (_category is SourceCategory.Word or SourceCategory.Excel or SourceCategory.Ppt
                or SourceCategory.Markdown or SourceCategory.Text or SourceCategory.Html or SourceCategory.Json
            && (target.Ext == ".png" || target.Ext == ".jpg"))
            extra += "（多页文档会输出为每页一张图片）";
        return $"输出：{output}{extra}{notes}";
    }

    /// <summary>
    /// 按目标格式的参数描述生成控件：滑块（可拖动 + 直接输入数值）/ 输入框 / 下拉框 / 开关，
    /// 并处理 VisibleWhen 联动显隐。
    /// </summary>
    private static (StackPanel Root, List<ParamRow> Rows) BuildFormatParams(
        IReadOnlyList<FormatParam> parameters, Action onChanged)
    {
        var root = new StackPanel { Spacing = 8 };
        var rows = new List<ParamRow>();

        foreach (var param in parameters)
        {
            var row = new ParamRow { Param = param };
            var host = new StackPanel { Spacing = 4 };

            var labelText = param.Unit.Length > 0 && param.Kind is FormatParamKind.Slider or FormatParamKind.Number
                ? $"{param.Label}（{param.Unit}）"
                : param.Label;
            var label = new TextBlock
            {
                FontSize = 12, Foreground = ThemeBrush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap
            };
            label.Inlines.Add(new Run { Text = labelText });
            if (param.Hint is { Length: > 0 } hint)
                label.Inlines.Add(new Run { Text = "　" + hint, FontSize = 12, Foreground = ThemeBrush("TextFillColorTertiaryBrush") });
            host.Children.Add(label);

            var step = param.Step <= 0 ? 1 : param.Step;
            switch (param.Kind)
            {
                case FormatParamKind.Slider:
                {
                    var slider = new Slider
                    {
                        Minimum = param.Min, Maximum = param.Max, Value = param.Default,
                        StepFrequency = step, IsThumbToolTipEnabled = true
                    };
                    var box = new NumberBox
                    {
                        Value = param.Default, Minimum = param.Min, Maximum = param.Max,
                        SmallChange = step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                        Width = 116, HorizontalAlignment = HorizontalAlignment.Right
                    };
                    slider.ValueChanged += (_, e) =>
                    {
                        if (Math.Abs(box.Value - e.NewValue) > 0.0001) box.Value = e.NewValue;
                    };
                    var composite = new Grid { ColumnSpacing = 8 };
                    composite.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    composite.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    Grid.SetColumn(box, 1);
                    composite.Children.Add(slider);
                    composite.Children.Add(box);
                    host.Children.Add(composite);
                    row.Slider = slider;
                    row.Box = box;
                    break;
                }
                case FormatParamKind.Number:
                {
                    var box = new NumberBox
                    {
                        Value = param.Default, Minimum = param.Min, Maximum = param.Max,
                        SmallChange = step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
                    };
                    host.Children.Add(box);
                    row.Box = box;
                    break;
                }
                case FormatParamKind.Combo:
                {
                    var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                    var choices = param.Choices ?? Array.Empty<FormatParamChoice>();
                    foreach (var choice in choices)
                        combo.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
                    var index = 0;
                    for (var i = 0; i < choices.Count; i++)
                    {
                        if (Math.Abs(choices[i].Value - param.Default) < 0.0001) { index = i; break; }
                    }
                    combo.SelectedIndex = index;
                    host.Children.Add(combo);
                    row.Combo = combo;
                    break;
                }
                case FormatParamKind.Toggle:
                {
                    var toggle = new ToggleSwitch
                    {
                        OnContent = "开启", OffContent = "关闭", IsOn = param.Default >= 0.5, FontSize = 12
                    };
                    host.Children.Add(toggle);
                    row.Toggle = toggle;
                    break;
                }
            }

            row.Root = host;
            root.Children.Add(host);
            rows.Add(row);
        }

        void ApplyVisibility()
        {
            foreach (var row in rows)
            {
                if (row.Param.VisibleWhenId is null) continue;
                var driver = rows.FirstOrDefault(r => r.Param.Id == row.Param.VisibleWhenId);
                var show = driver is null
                    || Math.Abs(driver.Value - (row.Param.VisibleWhenValue ?? 0)) < 0.001;
                row.Root.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void HandleChange()
        {
            ApplyVisibility();
            onChanged();
        }

        foreach (var row in rows)
        {
            switch (row.Param.Kind)
            {
                case FormatParamKind.Slider:
                    row.Box!.ValueChanged += (_, _) => HandleChange();
                    break;
                case FormatParamKind.Number:
                    row.Box!.ValueChanged += (_, _) => HandleChange();
                    break;
                case FormatParamKind.Combo:
                    row.Combo!.SelectionChanged += (_, _) => HandleChange();
                    break;
                case FormatParamKind.Toggle:
                    row.Toggle!.Toggled += (_, _) => HandleChange();
                    break;
            }
        }

        ApplyVisibility();
        return (root, rows);
    }

    private static FormatParamValues ReadParamValues(IReadOnlyList<ParamRow> rows)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var row in rows)
            values[row.Param.Id] = row.Value;
        return new FormatParamValues(values);
    }

    private static Brush? ThemeBrush(string key)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
                return brush;
        }
        catch { }
        return null;
    }

    private static ConvertSettings BuildConvertSettings(FormatParamValues parameters, DialogUi ui)
    {
        var zipLevel = ui.ZipSlider is not null ? (int)ui.ZipSlider.Value : 6;
        var exportZip = ui.ExportZipToggle?.IsOn == true;
        var mergeImages = ui.MergeImagesCheck?.IsChecked == true;
        var combineImages = ui.CombineImagesCheck?.IsChecked == true;
        var docImageEdge = ui.DocMaxEdgeBox is not null
            ? Math.Clamp((int)ui.DocMaxEdgeBox.Value, 400, 8000)
            : 1600;
        var docJpgQuality = ui.DocJpgSlider is not null
            ? Math.Clamp((int)ui.DocJpgSlider.Value, 50, 100)
            : 90;
        var docPageRange = ui.DocRangeBox?.Text?.Trim() ?? "";
        var docRenderMode = ui.DocRenderCombo?.SelectedItem is ComboBoxItem { Tag: string tag }
            ? tag
            : "auto";
        var icoSizes = ui.IcoChecks?.Where(c => c.IsChecked == true).Select(c => (int)c.Tag!).ToArray() ?? [];
        if (icoSizes.Length == 0) icoSizes = new[] { 256 }; // 全不选时兜底 256

        return new ConvertSettings(parameters, zipLevel, exportZip, mergeImages, combineImages,
            docImageEdge, docJpgQuality, docPageRange, docRenderMode, icoSizes);
    }

    // ══════════════ 转换执行 ══════════════

    private void ConvertBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_queue.Count == 0) return;
        _ = AskFormatAsync();
    }

    private async Task RunConversionAsync(FormatOption target, ConvertSettings settings)
    {
        if (_queue.Count == 0) return;

        // 重置队列状态
        foreach (var item in _queue)
            item.SetState(QueueState.Waiting, item.Category == SourceCategory.Unsupported ? "等待（仅支持 ZIP 打包）" : "等待转换", StatusBrushes.Waiting);

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        _lastOutputs = [];
        _zipSummary = null;

        try
        {
            // 特殊操作：合并 / 拆分 / 任意文件 ZIP
            if (target.Special == ConvertSpecial.MergePdf)
            {
                await RunMergeAsync(token);
                return;
            }
            if (target.Special == ConvertSpecial.SplitPdf)
            {
                await RunSplitAsync(token);
                return;
            }
            if (target.Special == ConvertSpecial.ZipArchive
                && (_category != SourceCategory.Pdf || target.Tag is null))
            {
                await RunZipAsync(settings, token);
                return;
            }

            var convertible = _queue.Where(i => i.Category != SourceCategory.Unsupported).ToList();
            if (convertible.Count == 0)
            {
                ShowToast("没有可转换的文件", "队列中的文件类型未知，仅可打包为 ZIP 压缩包", InfoBarSeverity.Warning);
                return;
            }

            // 引擎下载只做一次（多个文件共用）
            var engine = FormatConvertCatalog.EngineFor(_category, target);
            var progress = new Progress<(int percent, string message)>(p =>
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (p.percent >= 0)
                    {
                        TaskProgress.IsIndeterminate = false;
                        TaskProgress.Value = p.percent;
                    }
                    ProgressText.Text = p.message;
                }));

            if (engine == ConvertEngine.Ffmpeg && !FfmpegService.IsFfmpegReady)
            {
                if (!await ConfirmDownloadAsync("FFmpeg", "视频/音频转换核心组件（约 80MB）")) return;
                await FfmpegService.EnsureFfmpegAsync(progress);
            }
            else if (engine == ConvertEngine.Magick && !MagickService.IsMagickReady)
            {
                if (!await ConfirmDownloadAsync("ImageMagick", "图片转换与压缩引擎（约 60MB）")) return;
                await MagickService.EnsureMagickAsync(progress);
            }
            else if (engine == ConvertEngine.OfficeCli && !OfficeCliService.IsReady)
            {
                if (await ConfirmDownloadAsync("OfficeCLI 渲染引擎",
                        "Word/Excel/PPT 真实渲染组件（单文件约 33MB，镜像下载，装后完全离线）。不下载则回退内置引擎转换（保真度较低）"))
                {
                    await OfficeCliService.EnsureOfficeCliAsync(progress);
                }
                else
                {
                    ProgressText.Text = "未使用 OfficeCLI，将回退内置引擎转换";
                }
            }

            // 多张图片 → 一份 PDF：不逐文件转换，一次命令按队列顺序拼接为多页 PDF
            if (settings.CombineImagesToPdf && _category == SourceCategory.Image && target.Ext == ".pdf")
            {
                var images = _queue.Where(i => i.Category == SourceCategory.Image).Select(i => i.FullPath).ToList();
                if (images.Count > 1)
                {
                    await RunCombineImagesToPdfAsync(images, settings, token);
                    return;
                }
            }

            // 文档引擎的进度为文本消息
            var docProgress = new Progress<string>(msg =>
                DispatcherQueue.TryEnqueue(() => ProgressText.Text = msg));

            int done = 0, ok = 0, failed = 0;
            var totalCount = convertible.Count;
            TaskProgress.IsIndeterminate = totalCount == 1;

            foreach (var item in convertible)
            {
                token.ThrowIfCancellationRequested();
                if (item.Category != _category)
                {
                    item.SetState(QueueState.Skipped, "已跳过", StatusBrushes.Skipped, "类型不匹配（仅同类批量转换或 ZIP 打包）");
                    done++;
                    continue;
                }

                var prefix = totalCount > 1 ? $"（{done + 1}/{totalCount}）" : "";
                item.SetState(QueueState.Running, "转换中…", StatusBrushes.Running);
                ProgressText.Text = $"{prefix}正在转换 {item.Name}...";

                try
                {
                    var outputs = await ConvertOneAsync(item, target, settings, docProgress, token);
                    item.SetState(QueueState.Done, $"完成 · {outputs.Count} 个输出", StatusBrushes.Done,
                        string.Join("\n", outputs.Select(Path.GetFileName)));
                    _lastOutputs.AddRange(outputs);
                    ok++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    item.SetState(QueueState.Failed, "失败", StatusBrushes.Failed, ex.Message);
                    failed++;
                    // 单个文件失败不中断队列
                }

                done++;
                if (totalCount > 1)
                {
                    TaskProgress.IsIndeterminate = false;
                    TaskProgress.Value = (double)done / totalCount * 100;
                }
            }

            // 导出选项：把全部输出整合为一个 ZIP 压缩包
            if (settings.ExportZip && _lastOutputs.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                ProgressText.Text = "正在把输出打包为 ZIP...";
                var zipPath = FormatConvertPlanner.BuildZipOutputPath(_lastOutputs);
                var (before, after) = await Task.Run(
                    () => FormatConvertPlanner.CreateZipArchive(_lastOutputs, zipPath, settings.ZipLevel), token);
                var saved = before > 0 ? Math.Max(0, 100 - (double)after / before * 100) : 0;
                _lastOutputs.Add(zipPath);
                _zipSummary = $"已打包 ZIP：{zipPath}（{DownloadQueueService.FormatSize(before)} → {DownloadQueueService.FormatSize(after)}，节省 {saved:F1}%）";
                ShowToast("已打包 ZIP", $"压缩前 {DownloadQueueService.FormatSize(before)} → 压缩后 {DownloadQueueService.FormatSize(after)}", InfoBarSeverity.Success);
            }

            ShowResult(_lastOutputs, ok, failed);
        }
        catch (OperationCanceledException)
        {
            foreach (var item in _queue.Where(i => i.State == QueueState.Running))
                item.SetState(QueueState.Waiting, "等待转换", StatusBrushes.Waiting);
            ShowToast("已取消", "", InfoBarSeverity.Informational);
            ProgressText.Text = "已取消";
        }
        catch (Exception ex)
        {
            ShowToast("转换失败", ex.Message, InfoBarSeverity.Error);
            ProgressText.Text = $"失败: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
            RefreshEngineCards();
        }
    }

    /// <summary>单个文件的普通转换（按引擎分发）。</summary>
    private async Task<List<string>> ConvertOneAsync(QueueItem item, FormatOption target,
        ConvertSettings settings, IProgress<string> docProgress, CancellationToken ct)
    {
        var source = item.FullPath;
        switch (FormatConvertCatalog.EngineFor(item.Category, target))
        {
            case ConvertEngine.Ffmpeg:
            {
                var isImageVideo = item.Category == SourceCategory.Image
                                   && (target.Ext == ".mp4" || target.Ext == ".webm");
                var isGifTarget = target.Ext == ".gif";
                var args = isImageVideo
                    ? FormatConvertPlanner.BuildImageVideoArgs(source, target, settings.Params)
                    : FormatConvertPlanner.BuildFfmpegArgs(source, target, settings.Params);
                var outputPath = FormatConvertPlanner.BuildOutputPath(source, target.Ext);
                try
                {
                    await FfmpegService.RunFfmpegAsync(args, null, ct);
                }
                catch (Exception ex) when (isGifTarget && IsFfmpegCrashException(ex))
                {
                    // palette filter_complex 导致 FFmpeg 崩溃，用最简参数重试
                    TryDeleteQuiet(outputPath);
                    docProgress.Report($"GIF 调色板模式失败，正在用简化模式重试...");
                    var fallbackArgs = FormatConvertPlanner.BuildFfmpegGifFallbackArgs(
                        source, settings.Params.GetInt(FormatParamIds.GifWidth, 480));
                    await FfmpegService.RunFfmpegAsync(fallbackArgs, null, ct);
                }
                catch
                {
                    TryDeleteQuiet(outputPath);
                    throw;
                }
                return [outputPath];
            }
            case ConvertEngine.Magick:
            {
                var args = FormatConvertPlanner.BuildMagickArgs(source, target, settings.Params, settings.IcoSizes);
                var outputPath = FormatConvertPlanner.BuildOutputPath(source, target.Ext);
                try
                {
                    await MagickService.RunMagickAsync(args, ct);
                }
                catch
                {
                    TryDeleteQuiet(outputPath);
                    throw;
                }
                return [outputPath];
            }
            case ConvertEngine.Ocr:
            {
                docProgress.Report($"正在识别 {item.Name} 中的文字...");
                var text = await OcrService.RecognizeImageFileAsync(source, ct);
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException("未识别到文字（图片中可能没有文字内容）");
                var outputPath = FormatConvertPlanner.BuildOutputPath(source, ".txt");
                await File.WriteAllTextAsync(outputPath, text + "\n", new System.Text.UTF8Encoding(true), ct);
                return [outputPath];
            }
            default:
            {
                var outputs = await _docService!.ConvertAsync(source, item.Category, target,
                    new DocConvertOptions(settings.ZipLevel, settings.MergeImages,
                        settings.DocImageEdge, settings.DocJpgQuality, settings.DocPageRange, settings.DocRenderMode),
                    docProgress, ct);
                return outputs;
            }
        }
    }

    /// <summary>多张图片合成为一份 PDF（ImageMagick 多输入拼接为多页）。</summary>
    private async Task RunCombineImagesToPdfAsync(IReadOnlyList<string> images,
        ConvertSettings settings, CancellationToken token)
    {
        var dir = Path.GetDirectoryName(images[0]) ?? ".";
        var outPath = Path.Combine(dir, $"图片合并_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        for (int i = 1; File.Exists(outPath) && new FileInfo(outPath).Length > 0; i++)
            outPath = Path.Combine(dir, $"图片合并_{DateTime.Now:yyyyMMdd_HHmmss}_{i}.pdf");

        TaskProgress.IsIndeterminate = true;
        ProgressText.Text = $"正在把 {images.Count} 张图片合成为一份 PDF...";
        try
        {
            var args = FormatConvertPlanner.BuildMagickMergePdfArgs(images, outPath,
                settings.Params.GetInt(FormatParamIds.ImageQuality, 0),
                settings.Params.GetInt(FormatParamIds.ImageMaxEdge, 0),
                settings.Params.GetFlag(FormatParamIds.ImageStrip));
            await MagickService.RunMagickAsync(args, token);

            if (!File.Exists(outPath) || new FileInfo(outPath).Length == 0)
                throw new InvalidOperationException("PDF 生成失败（未产生输出文件）");
            foreach (var item in _queue.Where(i => i.Category == SourceCategory.Image))
                item.SetState(QueueState.Done, "已合成 PDF", StatusBrushes.Done, Path.GetFileName(outPath));
            _lastOutputs = [outPath];

            if (settings.ExportZip)
            {
                var zipPath = FormatConvertPlanner.BuildZipOutputPath(_lastOutputs);
                var (before, after) = await Task.Run(
                    () => FormatConvertPlanner.CreateZipArchive(_lastOutputs, zipPath, settings.ZipLevel), token);
                var saved = before > 0 ? Math.Max(0, 100 - (double)after / before * 100) : 0;
                _lastOutputs.Add(zipPath);
                _zipSummary = $"已打包 ZIP：{zipPath}（{DownloadQueueService.FormatSize(before)} → {DownloadQueueService.FormatSize(after)}，节省 {saved:F1}%）";
            }
            ShowResult(_lastOutputs, images.Count, 0);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            TryDeleteQuiet(outPath);
            foreach (var item in _queue.Where(i => i.Category == SourceCategory.Image))
                item.SetState(QueueState.Failed, "失败", StatusBrushes.Failed, ex.Message);
            throw;
        }
    }

    /// <summary>多份 PDF 合并为一个。</summary>
    private async Task RunMergeAsync(CancellationToken token)
    {
        var pdfs = _queue.Where(i => i.Category == SourceCategory.Pdf).Select(i => i.FullPath).ToList();
        if (pdfs.Count < 2)
        {
            ShowToast("无法合并", "合并需要两份以上 PDF", InfoBarSeverity.Warning);
            return;
        }

        var dir = Path.GetDirectoryName(pdfs[0]) ?? ".";
        var outPath = Path.Combine(dir, $"PDF合并_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        for (int i = 1; File.Exists(outPath) && new FileInfo(outPath).Length > 0; i++)
            outPath = Path.Combine(dir, $"PDF合并_{DateTime.Now:yyyyMMdd_HHmmss}_{i}.pdf");

        TaskProgress.IsIndeterminate = true;
        ProgressText.Text = $"正在合并 {pdfs.Count} 份 PDF...";
        try
        {
            await _docEngine!.PdfMergeAsync(pdfs, outPath, token);
            foreach (var item in _queue.Where(i => i.Category == SourceCategory.Pdf))
                item.SetState(QueueState.Done, "已合并", StatusBrushes.Done, Path.GetFileName(outPath));
            _lastOutputs = [outPath];
            ShowResult([outPath], pdfs.Count, 0);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            foreach (var item in _queue.Where(i => i.Category == SourceCategory.Pdf))
                item.SetState(QueueState.Failed, "失败", StatusBrushes.Failed, ex.Message);
            throw;
        }
    }

    /// <summary>单份 PDF 拆分为单页 PDF。</summary>
    private async Task RunSplitAsync(CancellationToken token)
    {
        var pdf = _queue.FirstOrDefault(i => i.Category == SourceCategory.Pdf);
        if (pdf is null)
        {
            ShowToast("无法拆分", "拆分需要一份 PDF", InfoBarSeverity.Warning);
            return;
        }

        var dir = Path.GetDirectoryName(pdf.FullPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(pdf.FullPath) + "_拆分";

        TaskProgress.IsIndeterminate = true;
        ProgressText.Text = $"正在拆分 {pdf.Name}...";
        try
        {
            var outputs = await _docEngine!.PdfSplitAsync(pdf.FullPath, dir, baseName, token);
            pdf.SetState(QueueState.Done, $"完成 · 拆分为 {outputs.Count} 页", StatusBrushes.Done,
                string.Join("\n", outputs.Select(Path.GetFileName)));
            _lastOutputs = outputs;
            ShowResult(outputs, 1, 0);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            pdf.SetState(QueueState.Failed, "失败", StatusBrushes.Failed, ex.Message);
            throw;
        }
    }

    /// <summary>任意文件打包为 ZIP（含队列中不支持类型的文件）。</summary>
    private async Task RunZipAsync(ConvertSettings settings, CancellationToken token)
    {
        var files = _queue.Select(i => i.FullPath).ToList();
        if (files.Count == 0) return;

        var zipPath = FormatConvertPlanner.BuildZipOutputPath(files);
        TaskProgress.IsIndeterminate = true;
        ProgressText.Text = $"正在打包 {files.Count} 个文件（压缩级别 {settings.ZipLevel}）...";

        try
        {
            var (before, after) = await Task.Run(
                () => FormatConvertPlanner.CreateZipArchive(files, zipPath, settings.ZipLevel), token);

            foreach (var item in _queue)
                item.SetState(QueueState.Done, "已打包", StatusBrushes.Done, Path.GetFileName(zipPath));

            var saved = before > 0 ? Math.Max(0, 100 - (double)after / before * 100) : 0;
            _lastOutputs = [zipPath];
            _resultDir = Path.GetDirectoryName(zipPath) ?? ".";
            ResultPanel.Visibility = Visibility.Visible;
            ResultTitleText.Text = $"打包完成（{files.Count} 个文件）";
            ResultIcon.Glyph = "\uE73E";
            ResultIcon.Foreground = StatusBrushes.Done;
            ResultText.Text = $"{zipPath}\r\n压缩前 {DownloadQueueService.FormatSize(before)} → 压缩后 {DownloadQueueService.FormatSize(after)}（节省 {saved:F1}%）";
            ShowToast("打包完成", $"压缩前 {DownloadQueueService.FormatSize(before)} → 压缩后 {DownloadQueueService.FormatSize(after)}", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            TryDeleteQuiet(zipPath);
            foreach (var item in _queue)
                item.SetState(QueueState.Failed, "失败", StatusBrushes.Failed, ex.Message);
            throw;
        }
    }

    private async Task<bool> ConfirmDownloadAsync(string engineName, string description)
    {
        var d = new ContentDialog
        {
            Title = $"需要下载 {engineName}",
            Content = $"首次使用需要下载：{description}。下载后离线可用，不会增加应用安装包体积。",
            PrimaryButtonText = "下载并继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme,
            XamlRoot = Content.XamlRoot
        };
        return await d.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ShowResult(IReadOnlyList<string> outputs, int okCount, int failedCount)
    {
        ResultPanel.Visibility = Visibility.Visible;
        ResultTitleText.Text = failedCount == 0
            ? $"转换完成（成功 {okCount} 个）"
            : $"转换完成（成功 {okCount} · 失败 {failedCount}，失败原因见队列）";
        ResultIcon.Glyph = failedCount == 0 ? "\uE73E" : "\uE7BA";
        ResultIcon.Foreground = failedCount == 0 ? StatusBrushes.Done : StatusBrushes.Failed;
        var text = string.Join("\r\n", outputs.Select(Path.GetFullPath));
        if (!string.IsNullOrEmpty(_zipSummary))
            text += "\r\n" + _zipSummary;
        ResultText.Text = text;
        _resultDir = outputs.Count > 0 ? Path.GetDirectoryName(outputs[0]) : null;
        if (failedCount == 0)
            ShowToast("转换完成", $"已生成 {outputs.Count} 个文件", InfoBarSeverity.Success);
        else
            ShowToast("部分文件转换失败", $"{failedCount} 个文件失败，原因显示在文件队列中", InfoBarSeverity.Warning);
    }

    private void SetBusy(bool busy)
    {
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ConvertBtn.IsEnabled = !busy;
        CancelBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy)
        {
            TaskProgress.IsIndeterminate = true;
            TaskProgress.Value = 0;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_resultDir is not null)
                _ = Windows.System.Launcher.LaunchFolderPathAsync(_resultDir);
        }
        catch { }
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _queue.Clear();
        QueuePanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ProgressText.Text = "准备中...";
        _category = SourceCategory.Unsupported;
    }

    // ══════════════ 引擎卡片 ══════════════

    private void RefreshEngineCards()
    {
        UpdateFfmpegCard();
        UpdateMagickCard();
        UpdateOfficeCliCard();
        UpdateSystemCard();
    }

    private void UpdateFfmpegCard()
    {
        if (FfmpegService.IsFfmpegReady)
        {
            FfmpegStatusText.Text = $"已就绪（{FfmpegService.GetFfmpegSize()}）";
            FfmpegActionBtn.Content = "删除";
            FfmpegActionBtn.IsEnabled = true;
            FfmpegActionBtn.Tag = "delete";
        }
        else
        {
            var item = FfmpegService.DownloadItem;
            if (item is not null && item.State is DownloadItemState.Queued or DownloadItemState.Resolving
                    or DownloadItemState.Downloading or DownloadItemState.Processing)
            {
                var pct = item.Progress is { } p ? (int)p.Percentage : 0;
                FfmpegStatusText.Text = item.State == DownloadItemState.Processing
                    ? $"处理中：{item.ProcessingStatus ?? "解压中..."}"
                    : $"下载中 {pct}%…";
                FfmpegActionBtn.Content = "下载中…";
                FfmpegActionBtn.IsEnabled = false;
            }
            else
            {
                FfmpegStatusText.Text = "未安装（首次使用自动下载）";
                FfmpegActionBtn.Content = "下载";
                FfmpegActionBtn.IsEnabled = true;
                FfmpegActionBtn.Tag = "download";
            }
        }
    }

    private void UpdateMagickCard()
    {
        if (MagickService.IsMagickReady)
        {
            MagickStatusText.Text = $"已就绪（{MagickService.GetMagickSize()}）";
            MagickActionBtn.Content = "删除";
            MagickActionBtn.IsEnabled = true;
            MagickActionBtn.Tag = "delete";
        }
        else
        {
            var item = MagickService.DownloadItem;
            if (item is not null && item.State is DownloadItemState.Queued or DownloadItemState.Resolving
                    or DownloadItemState.Downloading or DownloadItemState.Processing)
            {
                var pct = item.Progress is { } p ? (int)p.Percentage : 0;
                MagickStatusText.Text = item.State == DownloadItemState.Processing
                    ? $"处理中：{item.ProcessingStatus ?? "解压中..."}"
                    : $"下载中 {pct}%…";
                MagickActionBtn.Content = "下载中…";
                MagickActionBtn.IsEnabled = false;
            }
            else
            {
                MagickStatusText.Text = "未安装（首次使用自动下载）";
                MagickActionBtn.Content = "下载";
                MagickActionBtn.IsEnabled = true;
                MagickActionBtn.Tag = "download";
            }
        }
    }

    private void UpdateOfficeCliCard()
    {
        if (OfficeCliService.IsReady)
        {
            OfficeCliStatusText.Text = $"已就绪（{OfficeCliService.GetOfficeCliSize()}）";
            OfficeCliActionBtn.Content = "删除";
            OfficeCliActionBtn.IsEnabled = true;
            OfficeCliActionBtn.Tag = "delete";
        }
        else
        {
            var item = OfficeCliService.DownloadItem;
            if (item is not null && item.State is DownloadItemState.Queued or DownloadItemState.Resolving
                    or DownloadItemState.Downloading or DownloadItemState.Processing)
            {
                var pct = item.Progress is { } p ? (int)p.Percentage : 0;
                OfficeCliStatusText.Text = item.State == DownloadItemState.Processing
                    ? $"处理中：{item.ProcessingStatus ?? "处理中..."}"
                    : $"下载中 {pct}%…";
                OfficeCliActionBtn.Content = "下载中…";
                OfficeCliActionBtn.IsEnabled = false;
            }
            else
            {
                OfficeCliStatusText.Text = "未安装（转换 Office 文档时自动下载，或回退内置引擎）";
                OfficeCliActionBtn.Content = "下载";
                OfficeCliActionBtn.IsEnabled = true;
                OfficeCliActionBtn.Tag = "download";
            }
        }
    }

    private void UpdateSystemCard()
    {
        var parts = new List<string>();

        // 系统 OCR（同步探测）
        parts.Add(OcrService.IsEngineAvailable
            ? "系统 OCR 可用（本地识别）"
            : "系统 OCR 未装语言包");

        // Office / WPS
        var office = new List<string>();
        if (OfficeInteropService.IsWordAvailable) office.Add("Word");
        if (OfficeInteropService.IsExcelAvailable) office.Add("Excel");
        if (OfficeInteropService.IsPptAvailable) office.Add("PowerPoint");
        parts.Add(office.Count > 0
            ? $"Office/WPS：{string.Join(" · ", office)}（旧版 doc/ppt/wps/et 可用）"
            : "Office/WPS：未安装（.doc/.ppt 等旧格式不可转换）");

        SystemStatusText.Text = string.Join("；", parts);
    }

    private void EngineActionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn == FfmpegActionBtn)
        {
            if (FfmpegService.IsFfmpegReady)
            {
                FfmpegService.DeleteFfmpeg();
                ShowToast("已删除", "FFmpeg 已删除，需要时可重新下载", InfoBarSeverity.Informational);
            }
            else
            {
                FfmpegService.EnsureFfmpegViaQueue();
                ShowToast("已加入下载队列", "可在下载中心查看进度", InfoBarSeverity.Informational);
            }
        }
        else if (btn == MagickActionBtn)
        {
            if (MagickService.IsMagickReady)
            {
                MagickService.DeleteMagick();
                ShowToast("已删除", "ImageMagick 已删除，需要时可重新下载", InfoBarSeverity.Informational);
            }
            else
            {
                MagickService.EnsureMagickViaQueue();
                ShowToast("已加入下载队列", "可在下载中心查看进度", InfoBarSeverity.Informational);
            }
        }
        else if (btn == OfficeCliActionBtn)
        {
            if (OfficeCliService.IsReady)
            {
                OfficeCliService.DeleteOfficeCli();
                ShowToast("已删除", "OfficeCLI 渲染引擎已删除，需要时可重新下载", InfoBarSeverity.Informational);
            }
            else
            {
                OfficeCliService.EnsureOfficeCliViaQueue();
                ShowToast("已加入下载队列", "单文件约 33MB，可在下载中心查看进度", InfoBarSeverity.Informational);
            }
        }
        RefreshEngineCards();
    }

    // ══════════════ 工具 ══════════════

    private static void TryDeleteQuiet(string? path)
    {
        try { if (path is not null && File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>判断异常是否为 FFmpeg 崩溃（退出码为负或 >125）。</summary>
    private static bool IsFfmpegCrashException(Exception ex)
    {
        if (ex is not Exception { Message: var msg }) return false;
        // 匹配 "FFmpeg 退出码 -541478725" 或 "FFmpeg 退出码 139" 等
        var match = System.Text.RegularExpressions.Regex.Match(msg, @"FFmpeg 退出码\s*(-?\d+)");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var code)) return false;
        return FormatConvertPlanner.IsFfmpegCrash(code);
    }

    private DispatcherTimer? _toastBarTimer;

    private void ShowToast(string title, string msg, InfoBarSeverity sev)
    {
        ToastBar.Title = title;
        ToastBar.Message = msg;
        ToastBar.Severity = sev;
        ToastBar.IsOpen = true;

        _toastBarTimer?.Stop();
        _toastBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _toastBarTimer.Tick += (s, _) =>
        {
            ToastBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _toastBarTimer.Start();
    }

    private static string CategoryName(SourceCategory c) => c switch
    {
        SourceCategory.Video => "视频",
        SourceCategory.Audio => "音频",
        SourceCategory.Image => "图片",
        SourceCategory.Pdf => "PDF 文档",
        SourceCategory.Word => "Word 文档",
        SourceCategory.Excel => "表格",
        SourceCategory.Ppt => "PPT 演示",
        SourceCategory.Markdown => "Markdown 文档",
        SourceCategory.Text => "文本文档",
        SourceCategory.Html => "HTML 网页",
        SourceCategory.Json => "JSON 数据",
        _ => "未知类型"
    };

    private static string CategoryGlyph(SourceCategory c) => c switch
    {
        SourceCategory.Video => "\uE8B2",
        SourceCategory.Audio => "\uE7E8",
        SourceCategory.Image => "\uE91B",
        SourceCategory.Pdf => "\uE8A5",
        SourceCategory.Word => "\uE8A5",
        SourceCategory.Excel => "\uE8A5",
        SourceCategory.Ppt => "\uE8A5",
        SourceCategory.Markdown => "\uE8A5",
        SourceCategory.Text => "\uE8A5",
        SourceCategory.Html => "\uE771",
        SourceCategory.Json => "\uE8A5",
        _ => "\uE838"
    };

    /// <summary>目标格式的对话框图标。</summary>
    private static string FormatGlyph(FormatOption f) => f.Special switch
    {
        ConvertSpecial.MergePdf => "\uE710",
        ConvertSpecial.SplitPdf => "\uE8A5",
        ConvertSpecial.ZipArchive => "\uE838",
        ConvertSpecial.OcrText => "\uE721",
        ConvertSpecial.PdfExcel => "\uE8A5",
        _ => f.Ext switch
        {
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" or ".flv" or ".ts" or ".gif" => "\uE8B2",
            ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" or ".opus" or ".wma" or ".aac" or ".aiff" => "\uE7E8",
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tiff" or ".heic" or ".avif" or ".ico" or ".tga" or ".psd" => "\uE91B",
            ".html" or ".htm" => "\uE771",
            ".zip" => "\uE838",
            _ => "\uE8A5"
        }
    };
}
