using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class DownloadQueueFlyout : UserControl
{
    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    private readonly Dictionary<string, DownloadItemViewModel> _vmMap = [];
    private readonly DispatcherQueue _dq;

    public DownloadQueueFlyout()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();

        QueueRepeater.ItemsSource = Items;

        foreach (var item in DownloadQueueService.Queue)
            AddViewModel(item);

        DownloadQueueService.Queue.CollectionChanged += OnQueueCollectionChanged;
        DownloadQueueService.QueueChanged += OnQueueChanged;

        // 代码解析的状态色画刷不会随主题自动刷新，切换主题后重新广播所有队列项
        ActualThemeChanged += (_, _) =>
        {
            foreach (var vm in Items)
                vm.Rebind();
        };

        UpdateEmptyState();
    }

    private void OnQueueCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _dq.TryEnqueue(() =>
        {
            if (e.NewItems is not null)
                foreach (DownloadItem item in e.NewItems)
                    AddViewModel(item);

            if (e.OldItems is not null)
                foreach (DownloadItem item in e.OldItems)
                    RemoveViewModel(item);

            UpdateEmptyState();
        });
    }

    private void OnQueueChanged()
    {
        _dq.TryEnqueue(UpdateEmptyState);
    }

    private void AddViewModel(DownloadItem item)
    {
        var vm = new DownloadItemViewModel(item);
        _vmMap[item.Id] = vm;
        Items.Insert(0, vm);
    }

    private void RemoveViewModel(DownloadItem item)
    {
        if (_vmMap.Remove(item.Id, out var vm))
            Items.Remove(vm);
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueScrollViewer.Visibility = Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearAllButton.Visibility = Items.Any(i => i.State is DownloadItemState.Completed
            or DownloadItemState.Failed or DownloadItemState.Cancelled)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
            DownloadQueueService.Pause(id);
    }

    private void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            DownloadQueueService.Resume(id);
            if (_vmMap.TryGetValue(id, out var vm))
                vm.Rebind();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
            DownloadQueueService.Cancel(id);
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            DownloadQueueService.Retry(id);
            if (_vmMap.TryGetValue(id, out var vm))
                vm.Rebind();
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
            DownloadQueueService.Remove(id);
    }

    private void DeleteFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
            DownloadQueueService.DeleteFile(id);
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var item = DownloadQueueService.Queue.FirstOrDefault(i => i.Id == id);
            if (item is null) return;
            var path = item.DestinationPath;
            if (Directory.Exists(path))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    Verb = "open"
                };
                System.Diagnostics.Process.Start(psi);
            }
        }
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadQueueService.ClearCompleted();
    }
}

public sealed class DownloadItemViewModel : INotifyPropertyChanged
{
    private readonly DownloadItem _item;

    public DownloadItemViewModel(DownloadItem item)
    {
        _item = item;
        _item.PropertyChanged += OnItemPropertyChanged;
    }

    public string Id => _item.Id;
    public string DisplayName => _item.DisplayName;
    public string? Description => _item.Description;
    public string? Glyph => !string.IsNullOrEmpty(_item.Glyph) ? _item.Glyph : GetStateGlyph();
    public DownloadItemState State => _item.State;

    public string StateText => State switch
    {
        DownloadItemState.Queued => "排队中",
        DownloadItemState.Resolving => "解析中",
        DownloadItemState.Downloading => "下载中",
        DownloadItemState.Paused => "已暂停",
        DownloadItemState.Processing => "处理中",
        DownloadItemState.Completed => "已完成",
        DownloadItemState.Failed => "失败",
        DownloadItemState.Cancelled => "已取消",
        _ => ""
    };

    /// <summary>
    /// 队列项状态语义色：排队/暂停/取消 = Neutral，解析/下载/处理中 = Accent，
    /// 完成 = Success，失败 = Critical（与格式转换页同一套语义）。
    /// </summary>
    public SolidColorBrush StateBrush => new(State switch
    {
        DownloadItemState.Resolving or DownloadItemState.Downloading or DownloadItemState.Processing => ThemeColors.AccentBlue,
        DownloadItemState.Completed => ThemeColors.AccentGreen,
        DownloadItemState.Failed => ThemeColors.AccentRed,
        _ => ThemeColors.Neutral
    });

    public bool IsIndeterminate => State is DownloadItemState.Resolving or DownloadItemState.Processing or DownloadItemState.Queued;
    public double ProgressValue => _item.Progress?.Percentage ?? 0;
    public bool IsError => State is DownloadItemState.Failed;
    public bool IsPaused => State is DownloadItemState.Paused;

    public Visibility ProgressVisible => State is DownloadItemState.Queued
        or DownloadItemState.Resolving
        or DownloadItemState.Downloading
        or DownloadItemState.Paused
        or DownloadItemState.Processing
        ? Visibility.Visible : Visibility.Collapsed;

    public string StatusLine
    {
        get
        {
            var p = _item.Progress;
            return State switch
            {
                DownloadItemState.Queued => "等待开始...",
                DownloadItemState.Resolving => "正在解析下载地址...",
                DownloadItemState.Downloading when p is not null =>
                    $"{DownloadQueueService.FormatSpeed(p.SpeedMbps)} · {DownloadQueueService.FormatTime(p.EstimatedRemaining)} · {DownloadQueueService.FormatSize(p.BytesReceived)}/{DownloadQueueService.FormatSize(p.TotalBytes)}",
                DownloadItemState.Downloading => "连接中...",
                DownloadItemState.Paused when p is not null =>
                    $"已暂停 · {DownloadQueueService.FormatSize(p.BytesReceived)}/{DownloadQueueService.FormatSize(p.TotalBytes)}",
                DownloadItemState.Paused => "已暂停",
                DownloadItemState.Processing => _item.ProcessingStatus ?? "处理中...",
                DownloadItemState.Completed => _item.ProcessingStatus is not null
                    ? $"{_item.ProcessingStatus} · 已完成"
                    : "下载完成",
                DownloadItemState.Failed => _item.ErrorMessage ?? "下载失败",
                DownloadItemState.Cancelled => "已取消下载",
                _ => ""
            };
        }
    }

    public Visibility PauseVisible => State is DownloadItemState.Downloading
        or DownloadItemState.Queued
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ResumeVisible => State is DownloadItemState.Paused
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CancelVisible => State is DownloadItemState.Paused
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RetryVisible => State is DownloadItemState.Failed or DownloadItemState.Cancelled
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OpenFolderVisible => State is DownloadItemState.Completed
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RemoveVisible => State is DownloadItemState.Completed
        or DownloadItemState.Failed
        or DownloadItemState.Cancelled
        or DownloadItemState.Paused
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DeleteFileVisible => State is DownloadItemState.Completed
        ? Visibility.Visible : Visibility.Collapsed;

    public void Rebind()
    {
        OnPropertyChanged(string.Empty);
    }

    private string GetStateGlyph() => State switch
    {
        DownloadItemState.Queued => "\uE9F5",
        DownloadItemState.Resolving => "\uE895",
        DownloadItemState.Downloading => "\uE896",
        DownloadItemState.Paused => "\uE769",
        DownloadItemState.Processing => "\uE9F5",
        DownloadItemState.Completed => "\uE73E",
        DownloadItemState.Failed => "\uE783",
        DownloadItemState.Cancelled => "\uE733",
        _ => "\uE896"
    };

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
