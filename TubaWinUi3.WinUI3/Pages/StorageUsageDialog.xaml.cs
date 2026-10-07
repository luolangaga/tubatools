using System.IO;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>
/// 存储占用弹窗：分组列出程序目录、缓存、内置工具产物、日志与个人数据的占用，
/// 可逐项勾选后一键删除。删除前不改变任何数据，个人数据需要二次确认。
/// </summary>
public sealed partial class StorageUsageDialog : ContentDialog
{
    private readonly List<ItemRow> _rows = [];
    private readonly List<GroupRow> _groups = [];
    private readonly Dictionary<string, ItemRow> _rowsById = [];
    private IReadOnlyList<StorageItem> _items = [];
    private bool _busy;
    private bool _confirming;
    private bool _done;

    /// <summary>最近一次清理释放的字节数，供调用方刷新状态文案。</summary>
    public long FreedBytes { get; private set; }

    /// <summary>最近一次清理的结果描述。</summary>
    public string ResultSummary { get; private set; } = string.Empty;

    public StorageUsageDialog()
    {
        InitializeComponent();

        // 默认的 ContentDialogMaxWidth 只有 548，装不下分组清单会横向溢出
        Resources["ContentDialogMaxWidth"] = 840d;
        Resources["ContentDialogMaxHeight"] = 800d;

        Opened += StorageUsageDialog_Opened;
        // 删除过程中不允许关闭，避免留下半清理状态
        Closing += (_, args) => args.Cancel = _busy;
    }

    private sealed class ItemRow
    {
        public required StorageItem Item { get; init; }
        public required CheckBox Box { get; init; }
        public required TextBlock SizeText { get; init; }
        public required FrameworkElement Root { get; init; }
    }

    private sealed class GroupRow
    {
        public required StorageGroupKind Kind { get; init; }
        public required Expander Expander { get; init; }
        public required CheckBox SelectAll { get; init; }
        public required TextBlock SizeText { get; init; }
        public required List<ItemRow> Rows { get; init; }
        public bool Updating;
    }

    private async void StorageUsageDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        FitListToWindow();
        IsPrimaryButtonEnabled = false;
        try
        {
            await ScanAsync();
        }
        catch (Exception ex)
        {
            ScanBar.IsIndeterminate = false;
            ScanText.Text = "统计失败";
            StatusText.Text = $"无法统计存储占用：{ex.Message}";
        }
    }

    /// <summary>按窗口高度决定清单可视高度，避免窗口偏小时把底部按钮挤出屏幕。</summary>
    private void FitListToWindow()
    {
        if (XamlRoot is null) return;

        const double chromeHeight = 340; // 标题栏 + 总览 + 提示区 + 按钮区
        ListScroll.MaxHeight = Math.Clamp(XamlRoot.Size.Height - chromeHeight, 180, 520);
    }

    private async Task ScanAsync()
    {
        _items = await Task.Run(StorageUsageService.CreateCatalog);
        BuildGroups();

        var progress = new Progress<StorageUsageService.ScanProgress>(p =>
        {
            ScanText.Text = $"正在统计：{p.Item.Name}（{p.Completed}/{p.Total}）";
            if (_rowsById.TryGetValue(p.Item.Id, out var row))
                row.SizeText.Text = TempCleanupService.FormatBytes(p.Item.SizeBytes);
        });

        await StorageUsageService.ScanAsync(_items, progress);

        ScanPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Visible;
        ApplySizes();
        CollapseEmptyRows();
        RefreshTotals();
    }

    /// <summary>隐藏没有占用的条目（不存在的路径、空的临时残留）与因此变空的分组。</summary>
    /// <param name="resetSelection">清理完成后调用时为 true，避免残留上一次的勾选。</param>
    private void CollapseEmptyRows(bool resetSelection = false)
    {
        foreach (var row in _rows)
        {
            if (row.Item.SizeBytes > 0)
            {
                row.Root.Visibility = Visibility.Visible;
                if (resetSelection) row.Box.IsChecked = false;
            }
            else
            {
                row.Root.Visibility = Visibility.Collapsed;
            }
        }

        foreach (var group in _groups)
        {
            group.Expander.Visibility = group.Rows.Any(r => r.Root.Visibility == Visibility.Visible)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    // ── 界面构建 ────────────────────────────────────────────

    private void BuildGroups()
    {
        GroupsPanel.Children.Clear();
        _rows.Clear();
        _rowsById.Clear();
        _groups.Clear();

        foreach (var group in _items.GroupBy(i => i.Kind).OrderBy(g => (int)g.Key))
        {
            var content = new StackPanel { Spacing = 2 };
            var rows = new List<ItemRow>();

            foreach (var item in group)
            {
                var row = BuildItemRow(item);
                rows.Add(row);
                _rows.Add(row);
                _rowsById[item.Id] = row;
                content.Children.Add(row.Root);
            }

            var selectAll = new CheckBox
            {
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
                Content = "全选"
            };

            var header = BuildGroupHeader(group.Key, selectAll, out var groupSizeText);

            var expander = new Expander
            {
                Header = header,
                Content = content,
                IsExpanded = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            var groupRow = new GroupRow
            {
                Kind = group.Key,
                Expander = expander,
                SelectAll = selectAll,
                SizeText = groupSizeText,
                Rows = rows
            };

            selectAll.Click += (_, _) => ToggleGroup(groupRow);
            foreach (var row in rows)
                row.Box.Click += (_, _) => RefreshTotals();

            GroupsPanel.Children.Add(expander);
            _groups.Add(groupRow);
        }
    }

    private UIElement BuildGroupHeader(StorageGroupKind kind, CheckBox selectAll, out TextBlock sizeText)
    {
        var chip = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(ParseColor(StorageUsageService.GroupColor(kind)))
        };

        var title = new TextBlock
        {
            Text = StorageUsageService.GroupLabel(kind),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var hint = new TextBlock
        {
            Text = StorageUsageService.GroupHint(kind),
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var textColumn = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        textColumn.Children.Add(title);
        textColumn.Children.Add(hint);

        sizeText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "…"
        };

        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(chip, 0);
        Grid.SetColumn(textColumn, 1);
        Grid.SetColumn(sizeText, 2);
        Grid.SetColumn(selectAll, 3);
        grid.Children.Add(chip);
        grid.Children.Add(textColumn);
        grid.Children.Add(sizeText);
        grid.Children.Add(selectAll);

        return grid;
    }

    private ItemRow BuildItemRow(StorageItem item)
    {
        var box = new CheckBox
        {
            MinWidth = 0,
            IsChecked = item.DefaultChecked,
            VerticalAlignment = VerticalAlignment.Center
        };

        var name = new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold };
        var description = new TextBlock
        {
            Text = item.Description,
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
            TextWrapping = TextWrapping.Wrap
        };

        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(name);
        texts.Children.Add(description);

        if (item.CanDelete && item.Paths.Count > 0)
        {
            texts.Children.Add(new TextBlock
            {
                Text = item.DisplayPath,
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        var sizeText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "…"
        };

        var openButton = new Button
        {
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = item.Paths.Count > 0
        };
        openButton.Content = new FontIcon { FontSize = 12, Glyph = "\uE8A7" };
        ToolTipService.SetToolTip(openButton, "打开所在位置");
        openButton.Click += (_, _) => OpenLocation(item);

        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(box, 0);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(sizeText, 2);
        Grid.SetColumn(openButton, 3);
        grid.Children.Add(box);
        grid.Children.Add(texts);
        grid.Children.Add(sizeText);
        grid.Children.Add(openButton);

        return new ItemRow { Item = item, Box = box, SizeText = sizeText, Root = grid };
    }

    // ── 状态刷新 ────────────────────────────────────────────

    private void ApplySizes()
    {
        foreach (var row in _rows)
            row.SizeText.Text = TempCleanupService.FormatBytes(row.Item.SizeBytes);

        foreach (var group in _groups)
        {
            if (!group.Updating) group.SizeText.Text = TempCleanupService.FormatBytes(GroupSize(group));
        }
    }

    private static long GroupSize(GroupRow group) => group.Rows.Sum(r => r.Item.SizeBytes);

    private void ToggleGroup(GroupRow group)
    {
        group.Updating = true;
        var target = group.SelectAll.IsChecked == true;
        foreach (var row in group.Rows.Where(r => r.Root.Visibility == Visibility.Visible))
            row.Box.IsChecked = target;
        group.Updating = false;
        RefreshTotals();
    }

    private void RefreshGroupState(GroupRow group)
    {
        var visible = group.Rows.Where(r => r.Root.Visibility == Visibility.Visible).ToArray();
        var checkedCount = visible.Count(r => r.Box.IsChecked == true);

        group.Updating = true;
        group.SelectAll.IsChecked = visible.Length > 0 && checkedCount == visible.Length;
        group.SizeText.Text = TempCleanupService.FormatBytes(GroupSize(group));
        group.Updating = false;
    }

    private void RefreshTotals()
    {
        foreach (var group in _groups) RefreshGroupState(group);
        UpdateSummaryTexts();
        ResetConfirmState();
        IsPrimaryButtonEnabled = !_busy && !_done && SelectedRows().Count > 0;
    }

    private void UpdateSummaryTexts()
    {
        var total = _rows.Where(r => r.Root.Visibility == Visibility.Visible).Sum(r => r.Item.SizeBytes);
        TotalText.Text = TempCleanupService.FormatBytes(total);
        TotalHintText.Text = "本软件在磁盘上的总占用";

        var selected = SelectedRows();
        var selectedBytes = selected.Sum(r => r.Item.SizeBytes);
        SelectionText.Text = selected.Count == 0
            ? "未选择任何项目"
            : $"已选 {selected.Count} 项 · {TempCleanupService.FormatBytes(selectedBytes)}";

        UpdateUsageBar();
    }

    private void UpdateUsageBar()
    {
        UsageBarGrid.Children.Clear();
        UsageBarGrid.ColumnDefinitions.Clear();

        var visible = _groups.Where(g => GroupSize(g) > 0).ToArray();
        if (visible.Length == 0) return;

        foreach (var group in visible)
        {
            UsageBarGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(GroupSize(group), 1), GridUnitType.Star)
            });
        }

        for (var i = 0; i < visible.Length; i++)
        {
            var segment = new Border
            {
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 2, 0),
                Background = new SolidColorBrush(ParseColor(StorageUsageService.GroupColor(visible[i].Kind)))
            };
            ToolTipService.SetToolTip(segment,
                $"{StorageUsageService.GroupLabel(visible[i].Kind)} · {TempCleanupService.FormatBytes(GroupSize(visible[i]))}");
            Grid.SetColumn(segment, i);
            UsageBarGrid.Children.Add(segment);
        }
    }

    private List<ItemRow> SelectedRows()
        => _rows.Where(r => r.Root.Visibility == Visibility.Visible && r.Box.IsChecked == true && r.Item.CanDelete).ToList();

    // ── 删除流程 ────────────────────────────────────────────

    private void PrimaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_busy) { args.Cancel = true; return; }
        if (_done) return;

        var selected = SelectedRows().Select(r => r.Item).ToArray();
        args.Cancel = true;
        if (selected.Length == 0) return;

        if (!_confirming && selected.Any(i => i.Kind == StorageGroupKind.UserData))
        {
            EnterConfirmState();
            return;
        }

        _ = DeleteAsync(selected);
    }

    private void EnterConfirmState()
    {
        _confirming = true;
        PrimaryButtonText = "确认删除（不可恢复）";
        WarningBar.Message = "所选内容包含个人数据（聊天记录、接收的文件等），删除后无法恢复。再次点击按钮确认。";
        WarningBar.IsOpen = true;
    }

    private void ResetConfirmState()
    {
        if (!_confirming) return;
        _confirming = false;
        PrimaryButtonText = "删除所选";
        WarningBar.IsOpen = false;
    }

    private async Task DeleteAsync(IReadOnlyList<StorageItem> selected)
    {
        _busy = true;
        IsPrimaryButtonEnabled = false;
        WarningBar.IsOpen = false;
        SummaryPanel.Visibility = Visibility.Collapsed;
        ScanPanel.Visibility = Visibility.Visible;
        ScanBar.IsIndeterminate = true;
        ScanText.Text = "正在删除…";
        _confirming = false;
        PrimaryButtonText = "删除所选";

        var progress = new Progress<StorageUsageService.ScanProgress>(p =>
            ScanText.Text = $"正在删除：{p.Item.Name}（{p.Completed}/{p.Total}）");

        var sizeBefore = selected.Sum(i => i.SizeBytes);
        StorageUsageService.DeleteOutcome outcome;
        try
        {
            outcome = await Task.Run(() => StorageUsageService.Delete(selected, progress));
            foreach (var item in selected)
            {
                var (size, files) = await Task.Run(() => StorageUsageService.MeasureItem(item));
                item.SizeBytes = size;
                item.FileCount = files;
            }
        }
        catch (Exception ex)
        {
            ScanPanel.Visibility = Visibility.Collapsed;
            SummaryPanel.Visibility = Visibility.Visible;
            StatusText.Text = $"删除失败：{ex.Message}";
            _busy = false;
            IsPrimaryButtonEnabled = true;
            return;
        }

        // 以删除前后实测差值计，被占用而跳过的文件不会算进释放量
        FreedBytes = Math.Max(0, sizeBefore - selected.Sum(i => i.SizeBytes));
        ResultSummary = $"已删除 {outcome.DeletedCount} 项，释放 {TempCleanupService.FormatBytes(FreedBytes)}";
        if (outcome.SkippedCount > 0)
            ResultSummary += $"；{outcome.SkippedCount} 项被占用或无权限，已跳过";

        CollapseEmptyRows(resetSelection: true);

        ScanPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Visible;
        StatusText.Text = ResultSummary;
        ApplySizes();
        foreach (var group in _groups) RefreshGroupState(group);
        UpdateSummaryTexts();
        _busy = false;
        _done = true;
        PrimaryButtonText = "完成";
        IsPrimaryButtonEnabled = true;
    }

    private static void OpenLocation(StorageItem item)
    {
        var path = item.Paths.FirstOrDefault();
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
                return;
            }

            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private static Color ParseColor(string hex)
    {
        var value = uint.Parse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber);
        return Color.FromArgb(0xFF, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
