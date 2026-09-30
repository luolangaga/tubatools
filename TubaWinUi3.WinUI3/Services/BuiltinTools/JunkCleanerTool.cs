using FluentCleaner.Models;
using FluentCleaner.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Pages;
using Windows.UI;

namespace TubaWinUi3.Services;

/// <summary>
/// 垃圾清理（重构版）：基于 FluentCleaner.Core 引擎与 Winapp2.ini 规则库。
/// 规则库可从原仓库 builtbybel/FluentCleaner 一键更新。
/// </summary>
public sealed class JunkCleanerTool : IBuiltinTool
{
    public string Id => "junk-cleaner";
    public string Name => LocalizationService.L("Builtin_junk-cleaner_Name", "垃圾清理");
    public string Description => LocalizationService.L("Builtin_junk-cleaner_Desc", "基于 Winapp2 规则库扫描并清理应用缓存、临时文件与注册表残留（引擎来自 FluentCleaner）。");
    public string Glyph => "\uE74D";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.ProgressTask;

    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);
    private static readonly Color AccentYellow = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color AccentPurple = Color.FromArgb(255, 167, 139, 250);

    // Detail preview shows at most this many file lines; beyond that a hint points at 复制完整列表.
    private const int DetailFileCap = 800;

    private readonly Winapp2Parser _parser = new();
    private readonly DetectionService _detection = new();
    private readonly CleaningService _cleaner = new();

    private List<CleanerEntry>? _allEntries;
    private List<JunkItem>? _items;
    private List<AppxItem>? _appxItems;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        var content = BuildDialogContent();
        var scroll = new ScrollViewer
        {
            Content = content,
            Padding = new Thickness(24, 0, 24, 24),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 1080
        };

        App.MainWindow?.NavigateToToolPage(typeof(ToolContentPage), new ToolContentPageParam
        {
            Title = "垃圾清理",
            Description = "基于 Winapp2 规则库扫描并清理应用缓存、临时文件与注册表残留",
            Glyph = Glyph,
            Content = scroll,
            OnClose = () => _cts?.Cancel()
        });

        return Task.CompletedTask;
    }

    // One UI row: an entry that was analyzed and found junk.
    private sealed class JunkItem
    {
        public required CleanerEntry Entry { get; init; }
        public required ScanResult Result { get; init; }
        public bool Selected { get; set; } = true;
        public bool Expanded { get; set; }          // 详情面板展开状态（点击条目切换）
        public bool HasRegistry => Result.RegistryToDelete.Count > 0;
        public long SizeBytes => Result.TotalBytes;
    }

    // One UI row in the Winappx bloatware section.
    private sealed class AppxItem
    {
        public required AppxEntry Entry { get; init; }
        public bool Selected { get; set; }
    }

    private sealed class UiState
    {
        public TextBlock DbInfoText = null!;
        public TextBlock TotalSizeText = null!;
        public TextBlock TotalFilesText = null!;
        public TextBlock ItemCountText = null!;
        public Button ScanBtn = null!;
        public Button CleanBtn = null!;
        public Button SelectAllBtn = null!;
        public Button DeselectAllBtn = null!;
        public Button UpdateDbBtn = null!;
        public StackPanel CategoryList = null!;
        public TextBlock ListHint = null!;
        public ProgressRing LoadingRing = null!;
        public StackPanel LoadingPanel = null!;
        public TextBlock LoadingText = null!;
        public TextBlock ResultText = null!;
        public Border ConfirmPanel = null!;
        public TextBlock ConfirmText = null!;
        public Button ConfirmYesBtn = null!;
        public Button ConfirmNoBtn = null!;
        public Button ScanAppxBtn = null!;
        public Button RemoveAppxBtn = null!;
        public TextBlock AppxStatus = null!;
        public StackPanel AppxList = null!;
    }

    private StackPanel BuildDialogContent()
    {
        var dbInfoText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        };

        var totalSizeText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentBlue), Text = "0 B" };
        var totalFilesText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentGreen), Text = "0" };
        var itemCountText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Text = "0" };

        var statsGrid = new Grid { ColumnSpacing = 10 };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sizeCard = MakeStatCard("可清理大小", totalSizeText, "\uEDA2", AccentBlue);
        var filesCard = MakeStatCard("文件数", totalFilesText, "\uE8C8", AccentGreen);
        var countCard = MakeStatCard("选中项目", itemCountText, "\uE7F4", AccentPurple);
        statsGrid.Children.Add(sizeCard); Grid.SetColumn(sizeCard, 0);
        statsGrid.Children.Add(filesCard); Grid.SetColumn(filesCard, 1);
        statsGrid.Children.Add(countCard); Grid.SetColumn(countCard, 2);

        Button MakeActionButton(string text, string glyph, bool enabled = true) => new()
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontSize = 12 },
                    new TextBlock { Text = text }
                }
            },
            IsEnabled = enabled
        };

        var scanBtn = MakeActionButton("扫描垃圾", "\uE72C");
        var cleanBtn = MakeActionButton("清理", "\uE74D", enabled: false);
        var selectAllBtn = new Button { Content = "全选", Padding = new Thickness(8, 4, 8, 4) };
        var deselectAllBtn = new Button { Content = "取消全选", Padding = new Thickness(8, 4, 8, 4) };
        var updateDbBtn = MakeActionButton("更新规则库", "\uE895");

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionBar.Children.Add(scanBtn);
        actionBar.Children.Add(cleanBtn);
        actionBar.Children.Add(selectAllBtn);
        actionBar.Children.Add(deselectAllBtn);
        actionBar.Children.Add(updateDbBtn);

        var categoryList = new StackPanel { Spacing = 12 };

        var loadingRing = new ProgressRing { Width = 28, Height = 28, IsActive = true };
        var loadingText = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = 640
        };
        var loadingPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Padding = new Thickness(0, 12, 0, 12),
            Visibility = Visibility.Collapsed,
            Children = { loadingRing, loadingText }
        };

        var confirmText = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(AccentYellow),
            TextWrapping = TextWrapping.Wrap
        };

        var confirmYesBtn = new Button { Content = "确认清理" };
        var confirmNoBtn = new Button { Content = "取消" };

        var confirmPanel = new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            Background = new SolidColorBrush(Color.FromArgb(30, AccentYellow.R, AccentYellow.G, AccentYellow.B)),
            BorderBrush = new SolidColorBrush(AccentYellow),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    confirmText,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirmYesBtn, confirmNoBtn } }
                }
            }
        };

        var registryTip = new InfoBar
        {
            Title = "规则包含注册表清理",
            Message = "部分 Winapp2 规则会删除注册表残留项，清理前请仔细核对，必要时先备份注册表。",
            Severity = InfoBarSeverity.Warning,
            IsOpen = true,
            IsClosable = true,
            Visibility = Visibility.Collapsed
        };

        var resultText = new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(AccentGreen),
            TextWrapping = TextWrapping.NoWrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Height = 44,
            VerticalAlignment = VerticalAlignment.Top
        };

        var contentGrid = new Grid { RowSpacing = 10 };
        for (var i = 0; i < 6; i++)
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        contentGrid.Children.Add(dbInfoText); Grid.SetRow(dbInfoText, 0);
        contentGrid.Children.Add(statsGrid); Grid.SetRow(statsGrid, 1);
        contentGrid.Children.Add(actionBar); Grid.SetRow(actionBar, 2);
        contentGrid.Children.Add(confirmPanel); Grid.SetRow(confirmPanel, 3);
        contentGrid.Children.Add(loadingPanel); Grid.SetRow(loadingPanel, 4);
        contentGrid.Children.Add(registryTip); Grid.SetRow(registryTip, 5);

        var root = new StackPanel { Spacing = 14, MaxWidth = 880 };
        root.Children.Add(contentGrid);
        root.Children.Add(resultText);

        var listHint = new TextBlock
        {
            Text = "提示：点击条目可展开查看将要删除的具体文件与注册表项；右侧开关可跳过不想清理的项目，点「删除」可单独清理某一项。",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(listHint);
        root.Children.Add(categoryList);

        // --- 预装应用清理（Winappx.ini） ---
        var appxStatus = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        };
        var appxList = new StackPanel { Spacing = 6 };
        var scanAppxBtn = new Button { Content = "扫描预装应用" };
        var removeAppxBtn = new Button { Content = "卸载选中", IsEnabled = false };
        var appxExpander = new Expander
        {
            Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE7B8", FontSize = 14 },
                    new TextBlock { Text = "预装应用清理（Winappx.ini）" }
                }
            },
            IsExpanded = false,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { scanAppxBtn, removeAppxBtn } },
                    appxStatus,
                    appxList
                }
            }
        };
        root.Children.Add(appxExpander);

        root.Tag = new UiState
        {
            DbInfoText = dbInfoText,
            TotalSizeText = totalSizeText,
            TotalFilesText = totalFilesText,
            ItemCountText = itemCountText,
            ScanBtn = scanBtn,
            CleanBtn = cleanBtn,
            SelectAllBtn = selectAllBtn,
            DeselectAllBtn = deselectAllBtn,
            UpdateDbBtn = updateDbBtn,
            CategoryList = categoryList,
            ListHint = listHint,
            LoadingRing = loadingRing,
            LoadingPanel = loadingPanel,
            LoadingText = loadingText,
            ResultText = resultText,
            ConfirmPanel = confirmPanel,
            ConfirmText = confirmText,
            ConfirmYesBtn = confirmYesBtn,
            ConfirmNoBtn = confirmNoBtn,
            ScanAppxBtn = scanAppxBtn,
            RemoveAppxBtn = removeAppxBtn,
            AppxStatus = appxStatus,
            AppxList = appxList
        };

        scanBtn.Click += async (_, _) => await ScanAllAsync(root);
        cleanBtn.Click += async (_, _) => await CleanSelectedAsync(root);
        updateDbBtn.Click += async (_, _) => await UpdateDatabaseAsync(root);
        scanAppxBtn.Click += async (_, _) => await ScanAppxAsync(root);
        removeAppxBtn.Click += async (_, _) => await RemoveAppxAsync(root);

        selectAllBtn.Click += (_, _) => SetAllSelected(root, true);
        deselectAllBtn.Click += (_, _) => SetAllSelected(root, false);

        RefreshDbInfo(root);
        return root;
    }

    // --- Database info / update --------------------------------------

    private void RefreshDbInfo(StackPanel root)
    {
        var state = GetState(root);
        if (state is null) return;

        var w2 = JunkCleanerDatabase.GetInfo(JunkDatabaseKind.Winapp2);
        var wx = JunkCleanerDatabase.GetInfo(JunkDatabaseKind.Winappx);

        string Describe(JunkDatabaseInfo? info) => info is null
            ? "缺失"
            : info.IsBundled
                ? $"内置副本 · {info.EntryCount} 条规则"
                : $"已更新 · 版本 {info.Version} · {info.EntryCount} 条规则 · {info.UpdatedAt:yyyy-MM-dd HH:mm}";

        state.DbInfoText.Text =
            $"Winapp2.ini：{Describe(w2)}\nWinappx.ini（预装应用清理）：{Describe(wx)} · 来源 builtbybel/FluentCleaner（点击「更新规则库」可手动获取最新版）";
    }

    private async Task UpdateDatabaseAsync(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _busy) return;

        _busy = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;
        state.LoadingText.Text = "正在更新规则库...";
        state.ScanBtn.IsEnabled = false;
        state.UpdateDbBtn.IsEnabled = false;
        state.ResultText.Text = "";

        try
        {
            // Throttle: scanning reports thousands of per-file paths; only repaint ~10x/s
            // so the header row doesn't flicker under the flood of updates.
            var progress = CreateUiProgress(state);

            await JunkCleanerDatabase.UpdateAllFromRepoAsync(progress, _cts.Token);

            _allEntries = null;   // force re-parse on next scan
            _items = null;
            _appxItems = null;
            state.CategoryList.Children.Clear();
            state.AppxList.Children.Clear();

            state.ResultText.Text = "规则库更新完成";
            state.ResultText.Foreground = new SolidColorBrush(AccentGreen);
            state.ResultText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            state.ResultText.Text = "规则库更新已取消";
            state.ResultText.Foreground = new SolidColorBrush(ThemeColors.DimText);
            state.ResultText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            state.ResultText.Text = $"规则库更新失败：{ex.Message}";
            state.ResultText.Foreground = new SolidColorBrush(AccentRed);
            state.ResultText.Visibility = Visibility.Visible;
        }
        finally
        {
            _busy = false;
            state.LoadingPanel.Visibility = Visibility.Collapsed;
            state.LoadingRing.IsActive = false;
            state.ScanBtn.IsEnabled = true;
            state.UpdateDbBtn.IsEnabled = true;
            RefreshDbInfo(root);
        }
    }

    private async Task<List<CleanerEntry>> LoadEntriesAsync()
    {
        if (_allEntries is not null) return _allEntries;
        if (!JunkCleanerDatabase.Exists(JunkDatabaseKind.Winapp2)) return [];

        var entries = await _parser.ParseFileAsync(
            JunkCleanerDatabase.GetEffectivePath(JunkDatabaseKind.Winapp2));

        // 用户自定义规则（与 FluentCleaner 一致：<数据目录>\JunkCleaner\Custom\*.ini）
        if (Directory.Exists(JunkCleanerDatabase.CustomDir))
        {
            foreach (var file in Directory.GetFiles(JunkCleanerDatabase.CustomDir, "*.ini"))
            {
                try
                {
                    foreach (var ce in await _parser.ParseFileAsync(file))
                    {
                        ce.IsCustom = true;
                        bool hasDetection = ce.DetectFiles.Count > 0 || ce.DetectKeys.Count > 0 || ce.SpecialDetect is not null;
                        if (hasDetection && !_detection.IsInstalled(ce)) continue;
                        entries.RemoveAll(e => string.Equals(e.Name, ce.Name, StringComparison.OrdinalIgnoreCase));
                        entries.Add(ce);
                    }
                }
                catch { }
            }
        }

        _allEntries = entries;
        return entries;
    }

    // --- Scan --------------------------------------------------------

    private async Task ScanAllAsync(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _busy) return;

        if (!JunkCleanerDatabase.Exists(JunkDatabaseKind.Winapp2))
        {
            state.ResultText.Text = "内置 Winapp2 规则库缺失，请点击「更新规则库」重新获取。";
            state.ResultText.Foreground = new SolidColorBrush(AccentYellow);
            return;
        }

        _busy = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;
        state.CleanBtn.IsEnabled = false;
        state.ScanBtn.IsEnabled = false;
        state.UpdateDbBtn.IsEnabled = false;
        state.ResultText.Text = "";
        state.CategoryList.Children.Clear();
        state.ConfirmPanel.Visibility = Visibility.Collapsed;

        try
        {
            var entries = await LoadEntriesAsync();
            if (entries.Count == 0)
            {
                state.ResultText.Text = "规则库为空或解析失败，请尝试重新更新规则库。";
                state.ResultText.Foreground = new SolidColorBrush(AccentYellow);
                state.ResultText.Visibility = Visibility.Visible;
                return;
            }

            var progress = CreateUiProgress(state);

            var items = new List<JunkItem>();
            var installedCount = 0;

            for (var i = 0; i < entries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = entries[i];
                state.LoadingText.Text = $"正在分析 ({i + 1}/{entries.Count})：{entry.Name}";

                var result = await Task.Run(async () =>
                {
                    if (!_detection.IsInstalled(entry)) return null;
                    return await _cleaner.AnalyzeAsync(entry, progress, ct);
                }, ct);

                if (result is null) continue;
                installedCount++;

                if (result.FilesToDelete.Count > 0 || result.RegistryToDelete.Count > 0)
                    items.Add(new JunkItem { Entry = entry, Result = result, Selected = entry.Default });
            }

            _items = items;
            RenderItems(root);

            state.TotalFilesText.Text = items.Sum(x => x.Result.FilesToDelete.Count).ToString("N0");
            state.TotalSizeText.Text = ScanResult.FormatBytes(items.Sum(x => x.SizeBytes));
            state.ItemCountText.Text = items.Count.ToString("N0");

            state.ResultText.Text = items.Count > 0
                ? $"扫描完成：发现 {items.Count:N0} 项可清理内容，共 {ScanResult.FormatBytes(items.Sum(x => x.SizeBytes))}。点击条目可预览将删除的文件。"
                : $"扫描完成：检查了 {installedCount:N0} 个已安装应用，没有发现可清理的垃圾。";
            state.ResultText.Foreground = new SolidColorBrush(items.Count > 0 ? AccentGreen : AccentBlue);
            state.ResultText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            state.ResultText.Text = "扫描已取消";
            state.ResultText.Foreground = new SolidColorBrush(ThemeColors.DimText);
            state.ResultText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            state.ResultText.Text = $"扫描失败：{ex.Message}";
            state.ResultText.Foreground = new SolidColorBrush(AccentRed);
            state.ResultText.Visibility = Visibility.Visible;
        }
        finally
        {
            _busy = false;
            state.LoadingPanel.Visibility = Visibility.Collapsed;
            state.LoadingRing.IsActive = false;
            state.ScanBtn.IsEnabled = true;
            state.UpdateDbBtn.IsEnabled = true;
            if (GetState(root) is { } st)
                st.CleanBtn.IsEnabled = _items?.Any(x => x.Selected) ?? false;
        }
    }

    // --- Clean -------------------------------------------------------

    private void SetAllSelected(StackPanel root, bool selected)
    {
        if (_items is null) return;
        foreach (var item in _items) item.Selected = selected;
        RenderItems(root);
    }

    private async Task CleanSelectedAsync(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _items is null || _busy) return;

        var selected = _items.Where(x => x.Selected).ToList();
        if (selected.Count == 0) return;

        var totalSize = selected.Sum(x => x.SizeBytes);
        var registryCount = selected.Count(x => x.HasRegistry);
        var confirmed = await ConfirmAsync(state,
            $"即将清理 {selected.Count} 个项目（共 {ScanResult.FormatBytes(totalSize)}）" +
            (registryCount > 0 ? $"，其中 {registryCount} 个项目包含注册表清理。" : "。") +
            "此操作不可撤销，确定继续？");
        if (!confirmed) return;

        _busy = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        state.CleanBtn.IsEnabled = false;
        state.ScanBtn.IsEnabled = false;
        state.UpdateDbBtn.IsEnabled = false;
        state.ResultText.Text = "";
        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;

        try
        {
            long totalBytes = 0;
            int totalDeleted = 0;

            var progress = CreateUiProgress(state);

            for (var i = 0; i < selected.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = selected[i];
                state.LoadingText.Text = $"正在清理 ({i + 1}/{selected.Count})：{item.Entry.Name}";

                var (count, bytes) = await _cleaner.CleanAsync(item.Result, progress, ct);
                totalDeleted += count;
                totalBytes += bytes;
            }

            // Drop fully cleaned items; keep the ones still holding leftovers.
            _items = _items.Where(x => !x.Selected ||
                    x.Result.FilesToDelete.Count > 0 && AnyFileLeft(x.Result.FilesToDelete) ||
                    x.Result.RegistryToDelete.Count > 0 && AnyRegistryLeft(x.Result))
                .ToList();
            foreach (var item in _items) item.Selected = true;

            RenderItems(root);

            state.TotalFilesText.Text = _items.Sum(x => x.Result.FilesToDelete.Count).ToString("N0");
            state.TotalSizeText.Text = ScanResult.FormatBytes(_items.Sum(x => x.SizeBytes));
            state.ItemCountText.Text = _items.Count.ToString("N0");

            state.ResultText.Text = _items.Count > 0
                ? $"清理完成：已删除 {totalDeleted:N0} 项，释放 {ScanResult.FormatBytes(totalBytes)}。仍有 {_items.Count:N0} 项存在残留（文件被占用或需重启后再清理）。"
                : $"清理完成：已删除 {totalDeleted:N0} 项，释放 {ScanResult.FormatBytes(totalBytes)} 空间。";
            state.ResultText.Foreground = new SolidColorBrush(AccentGreen);
            state.ResultText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            state.ResultText.Text = "清理已取消";
            state.ResultText.Foreground = new SolidColorBrush(ThemeColors.DimText);
            state.ResultText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            state.ResultText.Text = $"清理失败：{ex.Message}";
            state.ResultText.Foreground = new SolidColorBrush(AccentRed);
            state.ResultText.Visibility = Visibility.Visible;
        }
        finally
        {
            _busy = false;
            state.LoadingPanel.Visibility = Visibility.Collapsed;
            state.LoadingRing.IsActive = false;
            state.ScanBtn.IsEnabled = true;
            state.UpdateDbBtn.IsEnabled = true;
            if (GetState(root) is { } st)
                st.CleanBtn.IsEnabled = _items?.Any(x => x.Selected) ?? false;
        }
    }

    // 单独删除某一项：只清理这一个项目，不影响其他项目（含未勾选的）。
    private async Task DeleteOneAsync(StackPanel root, JunkItem item)
    {
        var state = GetState(root);
        if (state is null || _items is null || _busy) return;

        var confirmed = await ConfirmAsync(state,
            $"即将单独清理「{item.Entry.Name}」（共 {ScanResult.FormatBytes(item.SizeBytes)}）" +
            (item.HasRegistry ? "，包含注册表清理。" : "。") +
            "此操作不可撤销，其他项目不受影响，确定继续？");
        if (!confirmed) return;

        _busy = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        state.CleanBtn.IsEnabled = false;
        state.ScanBtn.IsEnabled = false;
        state.UpdateDbBtn.IsEnabled = false;
        state.ResultText.Text = "";
        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;

        try
        {
            state.LoadingText.Text = $"正在清理：{item.Entry.Name}";
            var progress = CreateUiProgress(state);
            var (count, bytes) = await _cleaner.CleanAsync(item.Result, progress, ct);

            bool leftover = item.Result.FilesToDelete.Count > 0 && AnyFileLeft(item.Result.FilesToDelete) ||
                            item.Result.RegistryToDelete.Count > 0 && AnyRegistryLeft(item.Result);
            if (!leftover) _items.Remove(item);

            RenderItems(root);
            state.TotalFilesText.Text = _items.Sum(x => x.Result.FilesToDelete.Count).ToString("N0");
            state.TotalSizeText.Text = ScanResult.FormatBytes(_items.Sum(x => x.SizeBytes));
            state.ItemCountText.Text = _items.Count.ToString("N0");

            state.ResultText.Text = leftover
                ? $"已删除 {count:N0} 项，释放 {ScanResult.FormatBytes(bytes)}；「{item.Entry.Name}」仍有残留（文件被占用或需重启后再清理）。"
                : $"已清理「{item.Entry.Name}」：删除 {count:N0} 项，释放 {ScanResult.FormatBytes(bytes)} 空间。";
            state.ResultText.Foreground = new SolidColorBrush(AccentGreen);
            state.ResultText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            state.ResultText.Text = "清理已取消";
            state.ResultText.Foreground = new SolidColorBrush(ThemeColors.DimText);
            state.ResultText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            state.ResultText.Text = $"清理失败：{ex.Message}";
            state.ResultText.Foreground = new SolidColorBrush(AccentRed);
            state.ResultText.Visibility = Visibility.Visible;
        }
        finally
        {
            _busy = false;
            state.LoadingPanel.Visibility = Visibility.Collapsed;
            state.LoadingRing.IsActive = false;
            state.ScanBtn.IsEnabled = true;
            state.UpdateDbBtn.IsEnabled = true;
            if (GetState(root) is { } st)
                st.CleanBtn.IsEnabled = _items?.Any(x => x.Selected) ?? false;
        }
    }

    private static bool AnyFileLeft(List<string> files)
    {
        foreach (var f in files)
            try { if (File.Exists(f)) return true; } catch { }
        return false;
    }

    private static bool AnyRegistryLeft(ScanResult result)
    {
        foreach (var reg in result.RegistryToDelete)
        {
            try
            {
                var idx = reg.KeyPath.IndexOf('\\');
                if (idx < 0) continue;
                using var root = RegistryHelpers.OpenHive(reg.KeyPath[..idx].ToUpperInvariant());
                using var key = root?.OpenSubKey(reg.KeyPath[(idx + 1)..]);
                if (key is not null) return true;
            }
            catch { }
        }
        return false;
    }

    // --- Winappx: preinstalled apps -----------------------------------

    private async Task ScanAppxAsync(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _busy) return;

        if (!JunkCleanerDatabase.Exists(JunkDatabaseKind.Winappx))
        {
            state.AppxStatus.Text = "Winappx.ini 缺失，请先点击「更新规则库」。";
            return;
        }

        _busy = true;
        state.ScanAppxBtn.IsEnabled = false;
        state.AppxStatus.Text = "正在枚举已安装的预装应用（PowerShell）...";
        try
        {
            var all = await AppxService.ParseDatabaseAsync(
                JunkCleanerDatabase.GetEffectivePath(JunkDatabaseKind.Winappx));
            var installed = await AppxService.ScanInstalledAsync(all);

            _appxItems = installed.Select(e => new AppxItem { Entry = e }).ToList();
            RenderAppx(root);

            state.AppxStatus.Text = installed.Count == 0
                ? "未发现 Winappx 清单中的预装应用。"
                : $"发现 {installed.Count} 个预装应用。默认不勾选，请谨慎勾选后再卸载（卸载 Store 应用不可恢复）。";
        }
        catch (Exception ex)
        {
            state.AppxStatus.Text = $"扫描失败：{ex.Message}";
        }
        finally
        {
            _busy = false;
            state.ScanAppxBtn.IsEnabled = true;
            if (GetState(root) is { } st)
                st.RemoveAppxBtn.IsEnabled = _appxItems?.Any(x => x.Selected) ?? false;
        }
    }

    private async Task RemoveAppxAsync(StackPanel root)
    {
        var state = GetState(root);
        if (state is null || _appxItems is null || _busy) return;

        var selected = _appxItems.Where(x => x.Selected).ToList();
        if (selected.Count == 0) return;

        var confirmed = await ConfirmAsync(state,
            $"即将卸载 {selected.Count} 个预装应用：{string.Join("、", selected.Select(x => x.Entry.Name))}。" +
            "此操作不可恢复（可尝试在 Microsoft Store 重新安装），确定继续？");
        if (!confirmed) return;

        _busy = true;
        state.RemoveAppxBtn.IsEnabled = false;
        state.ScanAppxBtn.IsEnabled = false;
        state.AppxStatus.Text = "正在卸载...";
        try
        {
            var ok = 0;
            var fail = 0;
            for (var i = 0; i < selected.Count; i++)
            {
                var item = selected[i];
                state.AppxStatus.Text = $"正在卸载 ({i + 1}/{selected.Count})：{item.Entry.Name}...";
                if (await AppxService.RemoveAsync(item.Entry)) ok++;
                else fail++;
            }

            // Re-scan: drop the ones that are now gone.
            var installed = await AppxService.GetInstalledNamesAsync();
            _appxItems = _appxItems.Where(x => !x.Selected ||
                    !installed.Any(n => n.Contains(x.Entry.PackageName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            RenderAppx(root);

            state.AppxStatus.Text = fail == 0
                ? $"卸载完成：成功移除 {ok} 个预装应用。"
                : $"卸载完成：成功 {ok} 个，失败 {fail} 个（部分应用受保护，需在设置中手动卸载）。";
        }
        catch (Exception ex)
        {
            state.AppxStatus.Text = $"卸载失败：{ex.Message}";
        }
        finally
        {
            _busy = false;
            state.ScanAppxBtn.IsEnabled = true;
            if (GetState(root) is { } st)
                st.RemoveAppxBtn.IsEnabled = _appxItems?.Any(x => x.Selected) ?? false;
        }
    }

    private void RenderAppx(StackPanel root)
    {
        var state = GetState(root);
        if (state is null) return;

        state.AppxList.Children.Clear();
        if (_appxItems is null || _appxItems.Count == 0) return;

        foreach (var item in _appxItems.OrderBy(x => x.Entry.Name))
        {
            var nameText = new TextBlock
            {
                Text = item.Entry.Name,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                TextWrapping = TextWrapping.Wrap
            };
            var warnText = new TextBlock
            {
                Text = item.Entry.Warning ?? item.Entry.PackageName,
                FontSize = 11,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var toggle = new ToggleSwitch
            {
                IsOn = item.Selected,
                OnContent = "",
                OffContent = "",
                MinWidth = 76
            };
            toggle.Toggled += (_, _) =>
            {
                item.Selected = toggle.IsOn;
                var st = GetState(root);
                if (st is not null)
                    st.RemoveAppxBtn.IsEnabled = _appxItems?.Any(x => x.Selected) ?? false;
            };

            var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(nameText);
            infoPanel.Children.Add(warnText);

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(infoPanel);
            grid.Children.Add(toggle); Grid.SetColumn(toggle, 1);

            state.AppxList.Children.Add(new Border
            {
                Padding = new Thickness(14, 10, 14, 10),
                Background = new SolidColorBrush(ThemeColors.CardBg),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = grid
            });
        }
    }

    // --- Rendering ---------------------------------------------------

    private void RenderItems(StackPanel root)
    {
        var state = GetState(root);
        if (state is null) return;

        state.CategoryList.Children.Clear();
        state.ListHint.Visibility = _items is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        if (_items is null || _items.Count == 0) return;

        foreach (var group in _items
                     .GroupBy(x =>
                     {
                         var info = CategoryResolver.TryMapLangSecRef(x.Entry);
                         return (info.Name, info.Order);
                     })
                     .OrderBy(g => g.Key.Order))
        {
            var groupSize = group.Sum(x => x.SizeBytes);
            var groupHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            groupHeader.Children.Add(new TextBlock
            {
                Text = group.Key.Name,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
            });
            groupHeader.Children.Add(new TextBlock
            {
                Text = $"{group.Count()} 项 · {ScanResult.FormatBytes(groupSize)}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(ThemeColors.DimText)
            });
            state.CategoryList.Children.Add(groupHeader);

            var list = new StackPanel { Spacing = 6 };
            foreach (var item in group.OrderByDescending(x => x.SizeBytes).ThenBy(x => x.Entry.Name))
            {
                list.Children.Add(CreateItemRow(item, root));
                if (item.Expanded)
                    list.Children.Add(CreateDetailPanel(item, root));
            }
            state.CategoryList.Children.Add(list);
        }

        state.CleanBtn.IsEnabled = _items.Any(x => x.Selected);
    }

    private Border CreateItemRow(JunkItem item, StackPanel root)
    {
        var accent = item.HasRegistry ? AccentYellow : AccentBlue;
        var dimAccent = Color.FromArgb(26, accent.R, accent.G, accent.B);

        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(dimAccent),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = item.HasRegistry ? "\uE7BA" : "\uE8B7" }
        };

        var nameText = new TextBlock
        {
            Text = item.Entry.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            TextWrapping = TextWrapping.Wrap
        };

        var descText = new TextBlock
        {
            Text = DescribeResult(item),
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var sizeText = new TextBlock
        {
            Text = item.SizeBytes > 0 ? ScanResult.FormatBytes(item.SizeBytes) : (item.HasRegistry ? "注册表" : "0 B"),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(item.SizeBytes > 0 || item.HasRegistry ? accent : ThemeColors.DimText)
        };

        var chevron = new FontIcon
        {
            Glyph = item.Expanded ? "\uE70E" : "\uE70D",
            FontSize = 10,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        var detailHint = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                chevron,
                new TextBlock { Text = item.Expanded ? "收起详情" : "查看详情", FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) }
            }
        };

        var toggle = new ToggleSwitch
        {
            IsOn = item.Selected,
            OnContent = "",
            OffContent = "",
            MinWidth = 76
        };
        toggle.Toggled += (_, _) =>
        {
            item.Selected = toggle.IsOn;
            var st = GetState(root);
            if (st is not null)
            {
                st.CleanBtn.IsEnabled = _items?.Any(x => x.Selected) ?? false;
                st.ItemCountText.Text = (_items?.Count(x => x.Selected) ?? 0).ToString("N0");
            }
        };

        var deleteBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children =
                {
                    new FontIcon { Glyph = "\uE74D", FontSize = 12, Foreground = new SolidColorBrush(AccentRed) },
                    new TextBlock { Text = "删除", Foreground = new SolidColorBrush(AccentRed) }
                }
            },
            Padding = new Thickness(10, 6, 10, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteBtn.Click += async (_, _) => await DeleteOneAsync(root, item);
        ToolTipService.SetToolTip(deleteBtn, "单独清理此项目，不影响其他项目");

        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(nameText);
        infoPanel.Children.Add(descText);

        var sizePanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        sizePanel.Children.Add(sizeText);
        sizePanel.Children.Add(detailHint);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconBorder);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(sizePanel); Grid.SetColumn(sizePanel, 2);
        grid.Children.Add(toggle); Grid.SetColumn(toggle, 3);
        grid.Children.Add(deleteBtn); Grid.SetColumn(deleteBtn, 4);

        var row = new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };

        // 点击整行（开关、删除按钮除外）展开/收起删除明细
        row.Tapped += (_, e) =>
        {
            if (_busy) return;
            if (IsDescendantOf(e.OriginalSource as DependencyObject, toggle)) return;
            if (IsDescendantOf(e.OriginalSource as DependencyObject, deleteBtn)) return;
            item.Expanded = !item.Expanded;
            RenderItems(root);
        };

        ToolTipService.SetToolTip(row, "点击查看 / 收起将删除的文件与注册表项");
        return row;
    }

    // --- 删除明细面板 ------------------------------------------------

    private Border CreateDetailPanel(JunkItem item, StackPanel root)
    {
        var accent = item.HasRegistry ? AccentYellow : AccentBlue;
        var fileCount = item.Result.FilesToDelete.Count;
        var regCount = item.Result.RegistryToDelete.Count;

        var copyBtn = new Button
        {
            Content = "复制完整列表",
            Padding = new Thickness(10, 4, 10, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        copyBtn.Click += async (_, _) =>
        {
            copyBtn.IsEnabled = false;
            copyBtn.Content = "已复制 ✓";
            CopyAllToClipboard(item);
            await Task.Delay(1600);
            copyBtn.IsEnabled = true;
            copyBtn.Content = "复制完整列表";
        };

        var titleBlock = new TextBlock
        {
            Text = "清理详情",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };
        var summaryBlock = new TextBlock
        {
            Text = $"{fileCount:N0} 个文件 · {regCount:N0} 项注册表 · 共 {ScanResult.FormatBytes(item.SizeBytes)}",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerText = new StackPanel { Spacing = 2, Children = { titleBlock, summaryBlock } };
        header.Children.Add(headerText);
        header.Children.Add(copyBtn); Grid.SetColumn(copyBtn, 1);

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(header);

        if (fileCount > 0)
            content.Children.Add(BuildDetailSection($"将删除的文件（{fileCount:N0} 个）", "\uE8B7", AccentBlue, item.Result.FilesToDelete));
        if (regCount > 0)
            content.Children.Add(BuildDetailSection($"将删除的注册表项（{regCount:N0} 项）", "\uE7BA", AccentYellow,
                item.Result.RegistryToDelete.Select(r => r.ToString()).ToList()));

        if (item.Entry.Warning is not null)
        {
            content.Children.Add(new InfoBar
            {
                Title = "规则警告（原规则库提示，清理前请注意）",
                Message = item.Entry.Warning,
                Severity = InfoBarSeverity.Warning,
                IsOpen = true,
                IsClosable = false
            });
        }

        if (fileCount == 0 && regCount == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "该条目暂无可删除的内容（可能已被清理，或文件正被占用需稍后重试）。",
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var dimAccent = Color.FromArgb(16, accent.R, accent.G, accent.B);
        return new Border
        {
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(16, 12, 16, 12),
            Background = new SolidColorBrush(dimAccent),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = content
        };
    }

    // 一个带标题和折叠列表的分区（文件 / 注册表项）
    private static UIElement BuildDetailSection(string title, string glyph, Color accent, IReadOnlyList<string> paths)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, Foreground = new SolidColorBrush(accent) });
        label.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });

        var panel = new StackPanel { Spacing = 1 };
        var shown = Math.Min(paths.Count, DetailFileCap);
        for (var i = 0; i < shown; i++)
        {
            panel.Children.Add(new TextBlock
            {
                Text = paths[i],
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsTextSelectionEnabled = true
            });
        }
        if (paths.Count > DetailFileCap)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"…… 其余 {paths.Count - DetailFileCap:N0} 项未显示，可用上方「复制完整列表」查看全部。",
                FontSize = 11,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        var section = new StackPanel { Spacing = 6 };
        section.Children.Add(label);
        section.Children.Add(paths.Count > 60
            ? new ScrollViewer { MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel }
            : panel);

        return section;
    }

    // 中文摘要：如 "1,234 个文件，共 45.2 MB · 注册表 3 项"
    private static string DescribeResult(JunkItem item)
    {
        var fileCount = item.Result.FilesToDelete.Count;
        var regCount = item.Result.RegistryToDelete.Count;
        var parts = new List<string>(2);
        if (fileCount > 0)
            parts.Add($"{fileCount:N0} 个文件，共 {ScanResult.FormatBytes(item.Result.TotalBytes)}");
        if (regCount > 0)
            parts.Add($"注册表 {regCount:N0} 项");
        return parts.Count > 0 ? string.Join(" · ", parts) : "暂无可删除内容";
    }

    // 把删除明细复制到剪贴板（含文件与注册表完整列表）
    private static void CopyAllToClipboard(JunkItem item)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{item.Entry.Name}（{ScanResult.FormatBytes(item.SizeBytes)}）");
        if (item.Result.FilesToDelete.Count > 0)
        {
            sb.AppendLine("=== 文件 ===");
            foreach (var f in item.Result.FilesToDelete) sb.AppendLine(f);
        }
        if (item.Result.RegistryToDelete.Count > 0)
        {
            sb.AppendLine("=== 注册表 ===");
            foreach (var r in item.Result.RegistryToDelete) sb.AppendLine(r.ToString());
        }
        // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
        ClipboardService.TrySetText(sb.ToString());
    }

    // 判断点击是否落在某个子元素内（用于屏蔽开关自身的点击）
    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (node == ancestor) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private static UiState? GetState(StackPanel root) => root?.Tag as UiState;

    // 弹确认面板等待用户选择，返回是否确认继续
    private static async Task<bool> ConfirmAsync(UiState state, string text)
    {
        state.ConfirmText.Text = text;
        state.ConfirmPanel.Visibility = Visibility.Visible;

        var tcs = new TaskCompletionSource<bool>();
        void OnYes(object s, RoutedEventArgs e) => tcs.TrySetResult(true);
        void OnNo(object s, RoutedEventArgs e) => tcs.TrySetResult(false);
        state.ConfirmYesBtn.Click += OnYes;
        state.ConfirmNoBtn.Click += OnNo;

        var confirmed = await tcs.Task;

        state.ConfirmYesBtn.Click -= OnYes;
        state.ConfirmNoBtn.Click -= OnNo;
        state.ConfirmPanel.Visibility = Visibility.Collapsed;
        return confirmed;
    }

    // Progress reporting throttled to ~10 reports/second so per-file scan updates
    // never flood the dispatcher and make the header layout jump around.
    private static Progress<string> CreateUiProgress(UiState state)
    {
        var lastReport = DateTime.MinValue;
        return new Progress<string>(p =>
        {
            var now = DateTime.UtcNow;
            if ((now - lastReport).TotalMilliseconds < 100) return;
            lastReport = now;
            state.LoadingText.Text = p;
        });
    }

    private static Border MakeStatCard(string label, TextBlock value, string glyph, Color accent)
    {
        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = glyph }
        };
        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(labelBlock);
        stack.Children.Add(value);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconBorder);
        grid.Children.Add(stack); Grid.SetColumn(stack, 1);

        return new Border
        {
            Padding = new Thickness(12),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }
}
