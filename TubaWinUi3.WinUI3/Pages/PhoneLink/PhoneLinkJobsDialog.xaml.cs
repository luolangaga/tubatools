using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>「手机任务」弹窗：查看手机发起的 PowerShell / winget 任务实时输出，并可终止。</summary>
public sealed partial class PhoneLinkJobsDialog : ContentDialog
{
    private readonly ObservableCollection<PhoneJobRowVm> _rows = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string? _initialJobId;
    private string? _outputJobId;
    private int _outputLength = -1;

    public PhoneLinkJobsDialog(XamlRoot xamlRoot, string? initialJobId = null)
    {
        InitializeComponent();
        TubaWinUi3.Services.FluentDialogLayout.Attach(this);
        XamlRoot = xamlRoot;
        RequestedTheme = ThemeService.CurrentElementTheme;
        _initialJobId = initialJobId;
        JobList.ItemsSource = _rows;

        Opened += (_, _) =>
        {
            PhoneLinkUiState.JobsDialogVisible = true;
            PhoneLinkService.JobsChanged += OnJobsChanged;
            Refresh();
            if (_initialJobId is not null)
                JobList.SelectedItem = _rows.FirstOrDefault(r => r.Id == _initialJobId);
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            PhoneLinkUiState.JobsDialogVisible = false;
            PhoneLinkService.JobsChanged -= OnJobsChanged;
            _timer.Stop();
        };
    }

    private void OnJobsChanged() => DispatcherQueue.TryEnqueue(Refresh);

    private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetail();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is PhoneJobRowVm row) PhoneLinkService.CancelJob(row.Id);
        Refresh();
    }

    // ───────────────────────── 刷新 ─────────────────────────

    private void Refresh()
    {
        var jobs = PhoneLinkService.GetJobs().OrderByDescending(j => j.StartedAt).ToList();
        var ids = jobs.Select(j => j.Id).ToList();

        if (_rows.Count != ids.Count || !_rows.Select(r => r.Id).SequenceEqual(ids))
        {
            // 任务集合有增删（或被裁剪）：整体重建并尽量保留选中项
            var selectedId = (JobList.SelectedItem as PhoneJobRowVm)?.Id ?? _initialJobId;
            _rows.Clear();
            foreach (var job in jobs) _rows.Add(CreateRow(job));
            var restored = selectedId is null ? null : _rows.FirstOrDefault(r => r.Id == selectedId);
            JobList.SelectedItem = restored ?? _rows.FirstOrDefault();
        }
        else
        {
            for (var i = 0; i < jobs.Count; i++) UpdateRow(_rows[i], jobs[i]);
        }
        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetail();
    }

    private static PhoneJobRowVm CreateRow(PhoneJob job) => new()
    {
        Id = job.Id,
        Title = job.Title,
        StatusText = StatusText(job),
        StatusBrush = StatusBrush(job),
        Subtitle = BuildSubtitle(job)
    };

    private static void UpdateRow(PhoneJobRowVm row, PhoneJob job)
    {
        row.StatusText = StatusText(job);
        row.StatusBrush = StatusBrush(job);
        row.Subtitle = BuildSubtitle(job);
    }

    private void UpdateDetail()
    {
        var row = JobList.SelectedItem as PhoneJobRowVm;
        var job = row is null ? null : PhoneLinkService.GetJob(row.Id);
        if (job is null)
        {
            DetailHeader.Text = "选择一项任务查看详情";
            CancelButton.IsEnabled = false;
            if (_outputJobId is not null)
            {
                _outputJobId = null;
                _outputLength = -1;
                OutputBox.Text = "";
            }
            return;
        }

        DetailHeader.Text = $"{StatusText(job)} · {job.Title}";
        CancelButton.IsEnabled = job.IsRunning;

        if (_outputJobId != job.Id || _outputLength != job.Output.Length)
        {
            _outputJobId = job.Id;
            _outputLength = job.Output.Length;
            OutputBox.Text = job.Output;
            OutputBox.SelectionStart = OutputBox.Text.Length;
        }
    }

    private static string StatusText(PhoneJob job) => job.Status switch
    {
        PhoneJobStatus.Running => "进行中",
        PhoneJobStatus.Done => "已完成",
        PhoneJobStatus.Cancelled => "已终止",
        _ => $"失败（退出码 {job.ExitCode}）"
    };

    private static Brush StatusBrush(PhoneJob job) => ThemeBrush(job.Status switch
    {
        PhoneJobStatus.Running => "AccentTextFillColorPrimaryBrush",
        PhoneJobStatus.Done => "SystemFillColorSuccessBrush",
        PhoneJobStatus.Cancelled => "TextFillColorSecondaryBrush",
        _ => "SystemFillColorCriticalBrush"
    });

    private static Brush ThemeBrush(string key)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush) return brush;
        }
        catch
        {
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private static string BuildSubtitle(PhoneJob job)
    {
        var kind = job.Kind == PhoneJobKind.Exec ? "命令" : "安装";
        var elapsed = (job.FinishedAt ?? DateTime.Now) - job.StartedAt;
        var duration = FormatDuration(elapsed);
        return $"{kind} · {job.StartedAt:HH:mm:ss} · {(job.IsRunning ? "已运行 " : "用时 ")}{duration}";
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} 小时 {span.Minutes} 分";
        if (span.TotalMinutes >= 1) return $"{span.Minutes} 分 {span.Seconds} 秒";
        return $"{Math.Max(0, span.Seconds)} 秒";
    }
}

/// <summary>任务列表行的绑定数据。</summary>
public sealed class PhoneJobRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Id { get; init; } = "";
    public string Title { get; init; } = "";

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; Notify(nameof(StatusText)); }
    }

    private Brush _statusBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    public Brush StatusBrush
    {
        get => _statusBrush;
        set { _statusBrush = value; Notify(nameof(StatusBrush)); }
    }

    private string _subtitle = "";
    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; Notify(nameof(Subtitle)); }
    }
}
