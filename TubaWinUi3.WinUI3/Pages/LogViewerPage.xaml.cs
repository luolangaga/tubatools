using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 运行日志：把统一日志目录（<see cref="LogService.LogDirectory"/>）里的日志集中展示，
/// 支持实时跟随、按级别/关键字过滤、复制、清空与打开目录。
/// 数据全部来自 <see cref="LogService"/>，此处不自己拼路径，也不自己写文件。
/// </summary>
public sealed partial class LogViewerPage : Page, ILocalizablePage
{
    /// <summary>实时跟随时重新读取的间隔。</summary>
    private const int RefreshIntervalMs = 1000;

    /// <summary>单次载入的末尾行数（够看崩溃上下文，又不至于卡界面）。</summary>
    private const int MaxLines = 2000;

    private readonly ObservableCollection<LogFileRow> _files = [];

    private DispatcherQueueTimer? _timer;
    private string? _category;
    private string _rawText = string.Empty;
    private long _lastLength = -1;
    private DateTime _lastWrite;
    private bool _followTail = true;

    /// <summary>
    /// 控件已全部建好、可以安全刷新内容。
    /// XAML 里的 <c>SelectedIndex="0"</c> 会在 <c>InitializeComponent()</c> 解析期间就触发
    /// <c>SelectionChanged</c>，那时后面的具名控件（ContentText / CountText）还是 null。
    /// </summary>
    private bool _ready;

    public LogViewerPage()
    {
        InitializeComponent();
        FilesList.ItemsSource = _files;
        _ready = true;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        LogService.Prepare();
        LogService.EnforceQuota();
        UpdateDirText();
        ReloadFiles(selectCategory: _category);

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs);
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _timer = null;
    }

    public void ApplyLocalization()
    {
        UpdateDirText();
        // 类别显示名随语言变化，这里要重建行而不是只刷大小
        ReloadFiles(selectCategory: _category, forceRebuild: true);
    }

    // ── 文件列表 ───────────────────────────────────────────

    private void ReloadFiles(string? selectCategory, bool forceRebuild = false)
    {
        var infos = LogService.ListFiles();

        // 类别集合不变时原地更新大小，避免每秒重建列表把选中项和滚动位置抖掉
        var current = _files.Select(f => f.Category).ToList();
        var incoming = infos.Select(f => f.Category).ToList();
        if (!forceRebuild && current.Count == incoming.Count && current.SequenceEqual(incoming, StringComparer.OrdinalIgnoreCase))
        {
            for (var i = 0; i < infos.Count; i++) _files[i].Update(infos[i]);
        }
        else
        {
            _files.Clear();
            foreach (var info in infos) _files.Add(new LogFileRow(info));
        }

        var target = selectCategory is null
            ? null
            : _files.FirstOrDefault(f => string.Equals(f.Category, selectCategory, StringComparison.OrdinalIgnoreCase));

        if (target is null || !ReferenceEquals(FilesList.SelectedItem, target))
            FilesList.SelectedItem = target ?? _files.FirstOrDefault();
        else
            _category = target.Category;

        if (_files.Count == 0)
        {
            _category = null;
            _rawText = string.Empty;
            ContentText.Text = LocalizationService.L("LogViewer_NoLogs", "日志目录里还没有任何日志文件。");
            CountText.Text = string.Empty;
            PathText.Text = LogService.LogDirectory;
        }
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilesList.SelectedItem is not LogFileRow row) return;
        _category = row.Category;
        _followTail = true;
        _lastLength = -1;
        ReloadContent(force: true);
    }

    // ── 内容 ───────────────────────────────────────────────

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (LiveToggle.IsChecked != true) return;
        ReloadFiles(selectCategory: _category);
        ReloadContent(force: false);
    }

    private void ReloadContent(bool force)
    {
        if (_category is null) return;

        var path = LogService.PathFor(_category);
        long length = 0;
        DateTime write = default;
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                length = info.Length;
                write = info.LastWriteTimeUtc;
            }
        }
        catch { }

        if (!force && length == _lastLength && write == _lastWrite) return;
        _lastLength = length;
        _lastWrite = write;

        _rawText = LogService.ReadTail(path, MaxLines);
        ApplyFilter();

        PathText.Text = path;
        if (_followTail) ScrollToEnd();
    }

    private void ApplyFilter()
    {
        if (!_ready) return;

        var level = (LevelCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var query = SearchBox.Text?.Trim() ?? string.Empty;

        var lines = _rawText.Length == 0 ? Array.Empty<string>() : _rawText.Split('\n');
        var filtered = lines.AsEnumerable();

        if (level != "all") filtered = filtered.Where(line => MatchesLevel(line, level));
        if (query.Length > 0)
            filtered = filtered.Where(line => line.Contains(query, StringComparison.OrdinalIgnoreCase));

        var list = filtered.ToList();
        ContentText.Text = list.Count == 0
            ? LocalizationService.L("LogViewer_Empty", "（没有符合当前筛选条件的日志内容）")
            : string.Join("\n", list);

        CountText.Text = string.Format(
            LocalizationService.L("LogViewer_CountFormat", "显示 {0} / {1} 行"),
            list.Count, lines.Length);
    }

    /// <summary>级别筛选：tag 表示最低级别（info 含 INFO/WARN/ERROR）。</summary>
    private static bool MatchesLevel(string line, string minimum)
    {
        var hasInfo = line.Contains("[INFO ]", StringComparison.Ordinal) || line.Contains("[DEBUG]", StringComparison.Ordinal);
        var hasWarn = line.Contains("[WARN ]", StringComparison.Ordinal);
        var hasError = line.Contains("[ERROR]", StringComparison.Ordinal);

        return minimum switch
        {
            "info" => hasInfo || hasWarn || hasError,
            "warn" => hasWarn || hasError,
            "error" => hasError,
            _ => true
        };
    }

    private void ScrollToEnd()
    {
        // 内容刚换过，等布局跑完再滚，否则 ScrollableHeight 还是旧值
        DispatcherQueue.TryEnqueue(() =>
            ContentScroller.ChangeView(null, ContentScroller.ScrollableHeight, null, disableAnimation: true));
    }

    private void ContentScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        // 用户往上翻就别再自动跟到底；滚回底部自动恢复跟随
        _followTail = Math.Abs(ContentScroller.ScrollableHeight - ContentScroller.VerticalOffset) < 24;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput || args.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
            ApplyFilter();
    }

    private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void LiveToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // 重新打开实时时，强制读一次最新内容
        if (LiveToggle.IsChecked == true)
        {
            _followTail = true;
            ReloadContent(force: true);
        }
    }

    // ── 操作 ───────────────────────────────────────────────

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LogService.Prepare();
        UpdateDirText();
        ReloadFiles(selectCategory: _category);
        ReloadContent(force: true);
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LogService.Prepare();
            Process.Start(new ProcessStartInfo("explorer.exe", LogService.LogDirectory) { UseShellExecute = true });
        }
        catch { }
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var text = ContentText.Text;
        if (string.IsNullOrEmpty(text)) return;

        var result = await ClipboardService.TrySetTextAsync(text);
        if (!result.Success)
        {
            await ShowDialogAsync(
                LocalizationService.L("LogViewer_CopyFailedTitle", "复制失败"),
                LocalizationService.L("LogViewer_CopyFailedDesc", "剪贴板正被其它程序占用，请稍后重试。"));
        }
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_category is null) return;

        var confirmed = false;
        var confirm = new ContentDialog
        {
            Title = LocalizationService.L("LogViewer_ClearConfirmTitle", "清空这份日志？"),
            Content = string.Format(
                LocalizationService.L("LogViewer_ClearConfirmDesc", "将删除「{0}」及其滚动副本，删除后无法恢复。"),
                LogService.DisplayName(_category)),
            PrimaryButtonText = LocalizationService.L("LogViewer_ClearConfirmOk", "清空"),
            CloseButtonText = LocalizationService.L("LogViewer_ClearConfirmCancel", "取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        confirm.PrimaryButtonClick += (_, _) => confirmed = true;

        await ContentDialogGuard.ShowWhenIdleAsync(confirm, TimeSpan.FromSeconds(5));
        if (!confirmed) return;

        LogService.Clear(_category);
        _lastLength = -1;
        ReloadFiles(selectCategory: _category);
        ReloadContent(force: true);
    }

    private async Task ShowDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(5));
    }

    private void UpdateDirText()
    {
        DirText.Text = string.Format(
            LocalizationService.L("LogViewer_DirUsage", "{0}\n占用 {1} / 上限 {2}（超出自动清理最旧日志）"),
            LogService.LogDirectory,
            LogFileRow.FormatSize(LogService.TotalSizeBytes()),
            LogFileRow.FormatSize(LogService.MaxTotalBytes));
    }
}

/// <summary>日志文件列表的一行（大小由 <see cref="Update"/> 原地刷新）。</summary>
public sealed class LogFileRow : INotifyPropertyChanged
{
    private long _size;

    public LogFileRow(LogFile file)
    {
        Category = file.Category;
        DisplayName = LogService.DisplayName(file.Category);
        Path = file.Path;
        _size = file.SizeBytes;
    }

    public string Category { get; }

    public string DisplayName { get; }

    public string Path { get; }

    public string SizeText => FormatSize(_size);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(LogFile file)
    {
        if (_size == file.SizeBytes) return;
        _size = file.SizeBytes;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 20) return $"{(double)bytes / (1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):F1} KB";
        return $"{bytes} B";
    }
}