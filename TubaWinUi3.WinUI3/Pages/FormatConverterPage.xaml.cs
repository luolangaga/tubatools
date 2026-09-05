using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
}

public sealed partial class FormatConverterPage : Page
{
    private sealed record ConvertSettings(
        bool Compress, int Crf, string Preset, int AudioKbps,
        int ImageQuality, int MaxEdge,
        int VideoWidth, int SampleRate, int Channels,
        int[] IcoSizes, int ImageVideoSeconds, int ZipLevel,
        bool ExportZip, bool MergeImages, bool CombineImagesToPdf,
        int DocImageEdge, int DocJpgQuality, string DocPageRange, string DocRenderMode);

    /// <summary>压缩区控件句柄（从对话框内容中读取值）。</summary>
    private sealed class CompressUi
    {
        public FrameworkElement? Root;       // 压缩区整体（toggle+panel）
        public ToggleSwitch? Toggle;
        public Slider? Slider;
        public ComboBox? Combo;              // 视频: 编码预设 / 音频: 码率
        public ComboBox? Combo2;             // 视频: 分辨率 / 音频: 采样率
        public ComboBox? Combo3;             // 音频: 声道
        public NumberBox? Box;
        public StackPanel? IcoSizesPanel;
        public CheckBox[]? IcoChecks;
        public FrameworkElement? DurationPanel;   // 图片转视频时长
        public NumberBox? DurationBox;
        public FrameworkElement? ZipPanel;        // ZIP 压缩级别
        public Slider? ZipSlider;
        public ToggleSwitch? ExportZipToggle;     // 导出为 ZIP 压缩包（通用导出选项）
        public CheckBox? MergeImagesCheck;        // 合并为一张长图
        public CheckBox? CombineImagesCheck;      // 多张图片合成为一份 PDF
        public FrameworkElement? DocImagePanel;   // 文档参数: 清晰度/页码范围/渲染模式
        public NumberBox? DocMaxEdgeBox;
        public TextBox? DocRangeBox;
        public ComboBox? DocRenderCombo;
        public FrameworkElement? DocJpgPanel;     // 文档参数: JPG 质量
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

    private static class StatusBrushes
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
            Waiting = Resolve(page, "TextFillColorSecondaryBrush", "#9AA0A6");
            Running = Resolve(page, "AccentFillColorDefaultBrush", "#0078D4");
            Done = Resolve(page, "SystemFillColorSuccessBrush", "#6CCB5F");
            Failed = Resolve(page, "SystemFillColorCriticalBrush", "#FF6B6B");
            Skipped = Resolve(page, "TextFillColorTertiaryBrush", "#9AA0A6");
            Initialized = true;
        }

        private static Brush Resolve(Page page, string key, string fallbackHex)
        {
            try
            {
                if (page.Resources.TryGetValue(key, out var v) && v is Brush b) return b;
                if (Application.Current.Resources.TryGetValue(key, out var v2) && v2 is Brush b2) return b2;
            }
            catch { }
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF,
                Convert.ToByte(fallbackHex.Substring(1, 2), 16),
                Convert.ToByte(fallbackHex.Substring(3, 2), 16),
                Convert.ToByte(fallbackHex.Substring(5, 2), 16)));
        }
    }

    public FormatConverterPage()
    {
        InitializeComponent();
        StatusBrushes.Init(this);
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

        var panel = new StackPanel { Spacing = 12, Width = 470 };

        var sourceLabel = _queue.Count == 1
            ? _queue[0].Name
            : $"{_queue[0].Name} 等 {_queue.Count} 个文件";
        panel.Children.Add(new TextBlock
        {
            Text = sourceLabel,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock { Text = "选择目标格式：", FontSize = 11, Opacity = 0.6 });

        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 210
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
                            FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = fmt.Name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextAlignment = TextAlignment.Center
                        }
                    }
                }
            });
        }
        panel.Children.Add(grid);
        grid.SelectedIndex = 0;

        // 输出预览（提前声明：下方事件处理器引用的 UpdatePreview 会用到它）
        var outPreview = new TextBlock { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

        // 压缩 / 参数区（视频、音频、图片），ZIP 目标显示压缩级别
        var (compressRoot, compressUi) = BuildCompressArea(_category);
        if (compressRoot is not null)
        {
            panel.Children.Add(new TextBlock { Text = "压缩选项：", FontSize = 11, Opacity = 0.6 });
            panel.Children.Add(compressRoot);
        }

        // 图片转视频时长
        if (_category == SourceCategory.Image)
        {
            var durationPanel = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
            durationPanel.Children.Add(new TextBlock
            {
                FontSize = 11, Opacity = 0.6,
                Text = "视频时长（秒，静态图片循环展示；GIF 动图按原帧率）"
            });
            var durationBox = new NumberBox
            {
                Value = 5, Minimum = 1, Maximum = 600, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };
            durationPanel.Children.Add(durationBox);
            panel.Children.Add(durationPanel);
            if (compressUi is not null)
            {
                compressUi.DurationPanel = durationPanel;
                compressUi.DurationBox = durationBox;
            }
        }

        // 导出选项：把转换产物整合为 ZIP / 多页图片合并为一张长图 / 多张图片合成一份 PDF
        {
            var exportZip = new ToggleSwitch
            {
                Header = "导出为 ZIP 压缩包（把输出的一个或多个文件整合打包）",
                OffContent = "关闭", OnContent = "开启"
            };
            var mergeImages = new CheckBox
            {
                Content = "合并为一张长图（多页文档导出图片时纵向拼接）",
                FontSize = 11, Visibility = Visibility.Collapsed
            };
            var combineImages = new CheckBox
            {
                Content = "把多张图片合成为一份 PDF（按队列顺序逐页拼接）",
                FontSize = 11, Visibility = Visibility.Collapsed
            };
            var optsPanel = new StackPanel { Spacing = 6, Children = { exportZip, mergeImages, combineImages } };
            panel.Children.Add(new TextBlock { Text = "导出选项：", FontSize = 11, Opacity = 0.6 });
            panel.Children.Add(optsPanel);
            compressUi ??= new CompressUi();
            compressUi.ExportZipToggle = exportZip;
            compressUi.MergeImagesCheck = mergeImages;
            compressUi.CombineImagesCheck = combineImages;
            exportZip.Toggled += (_, _) => UpdatePreview();
            mergeImages.Checked += (_, _) => UpdatePreview();
            mergeImages.Unchecked += (_, _) => UpdatePreview();
            combineImages.Checked += (_, _) => UpdatePreview();
            combineImages.Unchecked += (_, _) => UpdatePreview();
        }

        // ZIP 压缩级别（所有类别的 ZIP 目标通用）
        {
            var zipPanel = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
            zipPanel.Children.Add(new TextBlock
            {
                FontSize = 11, Opacity = 0.6,
                Text = "ZIP 压缩级别（0 = 仅打包不压缩，9 = 压缩最强最慢）"
            });
            var zipSlider = new Slider
            {
                Minimum = 0, Maximum = 9, Value = 6, StepFrequency = 1, IsThumbToolTipEnabled = true
            };
            zipPanel.Children.Add(zipSlider);
            panel.Children.Add(zipPanel);
            if (compressUi is null)
                compressUi = new CompressUi();
            compressUi.ZipPanel = zipPanel;
            compressUi.ZipSlider = zipSlider;
        }

        // 文档参数：文档类导出 PDF/图片时的渲染选项（OfficeCLI 原生参数）
        {
            var docImagePanel = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
            docImagePanel.Children.Add(new TextBlock
            {
                FontSize = 11, Opacity = 0.6,
                Text = "图片清晰度：截图宽度（像素，越大越清晰、文件越大）"
            });
            var docMaxEdgeBox = new NumberBox
            {
                Value = 1600, Minimum = 400, Maximum = 8000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };
            docImagePanel.Children.Add(docMaxEdgeBox);

            docImagePanel.Children.Add(new TextBlock
            {
                FontSize = 11, Opacity = 0.6,
                Text = "页码范围（如 1-3,5；留空 = 全部页）"
            });
            var docRangeBox = new TextBox { PlaceholderText = "全部页" };
            docImagePanel.Children.Add(docRangeBox);

            docImagePanel.Children.Add(new TextBlock
            {
                FontSize = 11, Opacity = 0.6,
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
                FontSize = 11, Opacity = 0.6,
                Text = "JPG 质量（越大越清晰、文件越小越模糊；仅 JPG 目标生效）"
            });
            var docJpgSlider = new Slider
            {
                Minimum = 50, Maximum = 100, Value = 90, StepFrequency = 1, IsThumbToolTipEnabled = true
            };
            docJpgPanel.Children.Add(docJpgSlider);

            panel.Children.Add(docImagePanel);
            panel.Children.Add(docJpgPanel);
            compressUi ??= new CompressUi();
            compressUi.DocImagePanel = docImagePanel;
            compressUi.DocMaxEdgeBox = docMaxEdgeBox;
            compressUi.DocRangeBox = docRangeBox;
            compressUi.DocRenderCombo = docRenderCombo;
            compressUi.DocJpgPanel = docJpgPanel;
            compressUi.DocJpgSlider = docJpgSlider;
        }

        var outLabel = new TextBlock { Text = "输出：", FontSize = 11, Opacity = 0.6 };
        panel.Children.Add(outLabel);
        panel.Children.Add(outPreview);

        void UpdatePreview()
        {
            var target = (grid.SelectedItem as GridViewItem)?.Tag as FormatOption ?? formats[0];
            outPreview.Text = DescribeOutput(target, compressUi);
            bool isZip = target.Special == ConvertSpecial.ZipArchive;
            bool isImageVideo = _category == SourceCategory.Image
                                && (target.Ext == ".mp4" || target.Ext == ".webm");
            bool hasMergeSplit = target.Special is ConvertSpecial.MergePdf or ConvertSpecial.SplitPdf;

            if (compressUi?.Root is not null)
                compressUi.Root.Visibility =
                    isZip || hasMergeSplit ? Visibility.Collapsed : Visibility.Visible;
            if (compressUi?.DurationPanel is not null)
                compressUi.DurationPanel.Visibility = isImageVideo ? Visibility.Visible : Visibility.Collapsed;
            if (compressUi?.ZipPanel is not null)
                compressUi.ZipPanel.Visibility = isZip ? Visibility.Visible : Visibility.Collapsed;

            // ICO 多尺寸面板仅在目标为 ICO 时显示
            if (compressUi?.IcoSizesPanel is not null)
                compressUi.IcoSizesPanel.Visibility =
                    target.Ext == ".ico" && !isZip ? Visibility.Visible : Visibility.Collapsed;

            // 导出为 ZIP：合并/拆分/ZIP 打包等特殊操作本身就是压缩/整合，不再叠加
            if (compressUi?.ExportZipToggle is not null)
                compressUi.ExportZipToggle.Visibility =
                    hasMergeSplit || isZip ? Visibility.Collapsed : Visibility.Visible;

            // 合并为一张长图：仅文档/PDF 类导出图片（含页面压缩包）时可用
            if (compressUi?.MergeImagesCheck is not null)
            {
                bool docImage = _category is SourceCategory.Word or SourceCategory.Excel or SourceCategory.Ppt
                        or SourceCategory.Markdown or SourceCategory.Text or SourceCategory.Html
                        or SourceCategory.Json or SourceCategory.Pdf
                    && ((target.Ext is ".png" or ".jpg" or ".pdf")
                        || (isZip && _category == SourceCategory.Pdf && target.Tag is not null));
                compressUi.MergeImagesCheck.Visibility =
                    docImage && target.Ext != ".pdf" ? Visibility.Visible : Visibility.Collapsed;

                // 文档参数：清晰度/页码范围/渲染模式（PDF/图片目标），JPG 质量（仅 JPG）
                if (compressUi.DocImagePanel is not null)
                    compressUi.DocImagePanel.Visibility = docImage ? Visibility.Visible : Visibility.Collapsed;
                if (compressUi.DocJpgPanel is not null)
                    compressUi.DocJpgPanel.Visibility =
                        docImage && target.Ext == ".jpg" ? Visibility.Visible : Visibility.Collapsed;
            }

            // 多张图片合成一份 PDF：仅「图片→PDF」且队列多于一张时可用
            if (compressUi?.CombineImagesCheck is not null)
            {
                bool combine = _category == SourceCategory.Image && target.Ext == ".pdf"
                    && _queue.Count(i => i.Category == SourceCategory.Image) > 1;
                compressUi.CombineImagesCheck.Visibility = combine ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        grid.SelectionChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var dialog = new ContentDialog
        {
            Title = $"转换为…（{CategoryName(_category)}）",
            Content = panel,
            PrimaryButtonText = "开始转换",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (_queue.Count == 0) return;

        var chosen = (grid.SelectedItem as GridViewItem)?.Tag as FormatOption ?? formats[0];
        var settings = ReadCompressSettings(compressUi, _category);
        await RunConversionAsync(chosen, settings);
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

    private string DescribeOutput(FormatOption target, CompressUi? ui)
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

    /// <summary>按类别构建压缩选项 UI（视频/音频/图片），文档类返回 null。</summary>
    private static (FrameworkElement? Root, CompressUi? Ui) BuildCompressArea(SourceCategory category)
    {
        switch (category)
        {
            case SourceCategory.Video:
            {
                var ui = new CompressUi();
                var toggle = new ToggleSwitch { Header = "压缩体积（降低码率）", OffContent = "关闭", OnContent = "开启" };
                var panel = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };

                var crfText = new TextBlock { FontSize = 11, Opacity = 0.6, Text = "画质 CRF（0-51，越低越好，默认 23）" };
                var crf = new Slider { Minimum = 0, Maximum = 51, Value = 23, StepFrequency = 1, IsThumbToolTipEnabled = true };
                var presetText = new TextBlock { FontSize = 11, Opacity = 0.6, Text = "编码预设（越慢体积越小）" };
                var preset = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var p in new[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" })
                    preset.Items.Add(p);
                preset.SelectedIndex = 5; // medium

                var resText = new TextBlock { FontSize = 11, Opacity = 0.6, Text = "输出分辨率（最长边，保持比例）" };
                var res = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                res.Items.Add(new ComboBoxItem { Content = "不缩放", Tag = 0 });
                res.Items.Add(new ComboBoxItem { Content = "1920P", Tag = 1920 });
                res.Items.Add(new ComboBoxItem { Content = "1280P", Tag = 1280 });
                res.Items.Add(new ComboBoxItem { Content = "854P", Tag = 854 });
                res.Items.Add(new ComboBoxItem { Content = "640P", Tag = 640 });
                res.Items.Add(new ComboBoxItem { Content = "480P", Tag = 480 });
                res.SelectedIndex = 0;

                panel.Children.Add(crfText); panel.Children.Add(crf);
                panel.Children.Add(presetText); panel.Children.Add(preset);
                panel.Children.Add(resText); panel.Children.Add(res);

                toggle.Toggled += (_, _) => panel.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
                ui.Root = new StackPanel { Spacing = 6, Children = { toggle, panel } };
                ui.Toggle = toggle; ui.Slider = crf; ui.Combo = preset; ui.Combo2 = res;
                return (ui.Root, ui);
            }
            case SourceCategory.Audio:
            {
                var ui = new CompressUi();
                var toggle = new ToggleSwitch { Header = "压缩（降低码率）", OffContent = "关闭", OnContent = "开启" };
                var panel = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };

                panel.Children.Add(new TextBlock { FontSize = 11, Opacity = 0.6, Text = "码率" });
                var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 140 };
                foreach (var k in new[] { 96, 128, 160, 192, 256, 320 })
                    combo.Items.Add($"{k} kbps");
                combo.SelectedIndex = 3; // 192

                panel.Children.Add(new TextBlock { FontSize = 11, Opacity = 0.6, Text = "采样率" });
                var sr = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 140 };
                sr.Items.Add(new ComboBoxItem { Content = "保持不变", Tag = 0 });
                sr.Items.Add(new ComboBoxItem { Content = "44100 Hz", Tag = 44100 });
                sr.Items.Add(new ComboBoxItem { Content = "48000 Hz", Tag = 48000 });
                sr.Items.Add(new ComboBoxItem { Content = "96000 Hz", Tag = 96000 });
                sr.SelectedIndex = 0;

                panel.Children.Add(new TextBlock { FontSize = 11, Opacity = 0.6, Text = "声道" });
                var ch = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 140 };
                ch.Items.Add(new ComboBoxItem { Content = "保持不变", Tag = 0 });
                ch.Items.Add(new ComboBoxItem { Content = "单声道", Tag = 1 });
                ch.Items.Add(new ComboBoxItem { Content = "立体声", Tag = 2 });
                ch.SelectedIndex = 0;

                panel.Children.Add(combo);
                panel.Children.Add(sr);
                panel.Children.Add(ch);
                toggle.Toggled += (_, _) => panel.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
                ui.Root = new StackPanel { Spacing = 6, Children = { toggle, panel } };
                ui.Toggle = toggle; ui.Combo = combo; ui.Combo2 = sr; ui.Combo3 = ch;
                return (ui.Root, ui);
            }
            case SourceCategory.Image:
            {
                var ui = new CompressUi();
                var toggle = new ToggleSwitch { Header = "压缩（降低质量 / 去除元数据 / 缩小尺寸）", OffContent = "关闭", OnContent = "开启" };
                var panel = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };

                var qText = new TextBlock { FontSize = 11, Opacity = 0.6, Text = "质量（1-100，JPG/WebP 默认 85）" };
                var slider = new Slider { Minimum = 1, Maximum = 100, Value = 85, StepFrequency = 1 };
                var dText = new TextBlock { FontSize = 11, Opacity = 0.6, Text = "最长边像素（0 = 不缩放，建议 1920；图片转视频时作为分辨率上限）" };
                var box = new NumberBox { Value = 0, Minimum = 0, Maximum = 20000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
                panel.Children.Add(qText); panel.Children.Add(slider);
                panel.Children.Add(dText); panel.Children.Add(box);

                // ICO 多尺寸（仅目标为 ICO 时显示）
                var icoPanel = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
                icoPanel.Children.Add(new TextBlock { FontSize = 11, Opacity = 0.6, Text = "图标尺寸（多选，打包进同一 .ico）" });
                var checkWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                var checks = new[] { 256, 128, 64, 48, 32, 24, 16 }
                    .Select(s => new CheckBox { Content = $"{s}×{s}", Tag = s, IsChecked = true, FontSize = 11 })
                    .ToArray();
                foreach (var c in checks) checkWrap.Children.Add(c);
                icoPanel.Children.Add(checkWrap);
                panel.Children.Add(icoPanel);
                ui.IcoSizesPanel = icoPanel;
                ui.IcoChecks = checks;

                toggle.Toggled += (_, _) => panel.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
                ui.Root = new StackPanel { Spacing = 6, Children = { toggle, panel } };
                ui.Toggle = toggle; ui.Slider = slider; ui.Box = box;
                return (ui.Root, ui);
            }
            default:
                return (null, null); // 文档/PDF/文本类：无压缩选项
        }
    }

    private static ConvertSettings ReadCompressSettings(CompressUi? ui, SourceCategory category)
    {
        if (ui?.Root is null && ui?.ZipSlider is null)
            return new ConvertSettings(false, 23, "medium", 192, 85, 0, 0, 0, 0,
                new[] { 256, 128, 64, 48, 32, 16 }, 5, 6, false, false, false, 1600, 90, "", "auto");

        var compress = ui?.Toggle?.IsOn == true;
        var crf = ui?.Slider is not null ? (int)ui.Slider.Value : 23;
        var preset = ui?.Combo?.SelectedItem as string ?? "medium";
        var kbps = ui?.Combo?.SelectedItem as string;
        var bitrate = kbps is not null && int.TryParse(kbps.Replace(" kbps", ""), out var k) ? k : 192;
        var quality = ui?.Slider is not null ? (int)ui.Slider.Value : 85;
        var maxEdge = ui?.Box is not null ? (int)ui.Box.Value : 0;

        int videoWidth = 0, sampleRate = 0, channels = 0;
        if (category == SourceCategory.Video && ui?.Combo2?.SelectedItem is ComboBoxItem ri)
            videoWidth = ri.Tag is int iw ? iw : 0;
        if (category == SourceCategory.Audio)
        {
            if (ui?.Combo2?.SelectedItem is ComboBoxItem si)
                sampleRate = si.Tag is int sr ? sr : 0;
            if (ui?.Combo3?.SelectedItem is ComboBoxItem ci)
                channels = ci.Tag is int cc ? cc : 0;
        }
        var icoSizes = ui?.IcoChecks?.Where(c => c.IsChecked == true).Select(c => (int)c.Tag!).ToArray() ?? [];
        if (icoSizes.Length == 0) icoSizes = new[] { 256 }; // 全不选时兜底 256

        var duration = ui?.DurationBox is not null ? (int)ui.DurationBox.Value : 5;
        var zipLevel = ui?.ZipSlider is not null ? (int)ui.ZipSlider.Value : 6;
        var exportZip = ui?.ExportZipToggle?.IsOn == true;
        var mergeImages = ui?.MergeImagesCheck?.IsChecked == true;
        var combineImages = ui?.CombineImagesCheck?.IsChecked == true;
        var docImageEdge = ui?.DocMaxEdgeBox is not null
            ? Math.Clamp((int)ui.DocMaxEdgeBox.Value, 400, 8000)
            : 1600;
        var docJpgQuality = ui?.DocJpgSlider is not null
            ? Math.Clamp((int)ui.DocJpgSlider.Value, 50, 100)
            : 90;
        var docPageRange = ui?.DocRangeBox?.Text?.Trim() ?? "";
        var docRenderMode = ui?.DocRenderCombo?.SelectedItem is ComboBoxItem ri2
            ? ri2.Tag as string ?? "auto"
            : "auto";

        return new ConvertSettings(compress, crf, preset, bitrate, quality, maxEdge,
            videoWidth, sampleRate, channels, icoSizes, duration, zipLevel, exportZip, mergeImages, combineImages,
            docImageEdge, docJpgQuality, docPageRange, docRenderMode);
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
                    ? FormatConvertPlanner.BuildImageVideoArgs(source, target, settings.ImageVideoSeconds, settings.MaxEdge)
                    : FormatConvertPlanner.BuildFfmpegArgs(source, target, settings.Crf, settings.Preset,
                        settings.AudioKbps, settings.Compress, settings.VideoWidth, settings.SampleRate, settings.Channels);
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
                    var fallbackArgs = FormatConvertPlanner.BuildFfmpegGifFallbackArgs(source, settings.VideoWidth);
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
                var args = FormatConvertPlanner.BuildMagickArgs(source, target, settings.ImageQuality,
                    settings.MaxEdge, settings.Compress, settings.IcoSizes);
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
                settings.ImageQuality, settings.MaxEdge, settings.Compress);
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateBack();

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
