using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Services;
using Windows.System;

namespace TubaWinUi3.Pages;

/// <summary>「连接手机」聊天与文件弹窗：气泡显示、图片缩略图、文件「另存为」、传输实时速度。</summary>
public sealed partial class PhoneLinkChatDialog : ContentDialog
{
    private readonly ObservableCollection<PhoneChatBubbleVm> _bubbles = [];
    private readonly Dictionary<string, PhoneTransferProgress> _transfers = [];

    public PhoneLinkChatDialog(XamlRoot xamlRoot)
    {
        InitializeComponent();
        TubaWinUi3.Services.FluentDialogLayout.Attach(this);
        XamlRoot = xamlRoot;
        RequestedTheme = ThemeService.CurrentElementTheme;
        MessageList.ItemsSource = _bubbles;

        Opened += (_, _) =>
        {
            PhoneLinkUiState.ChatDialogVisible = true;
            PhoneLinkService.MessageAdded += OnMessageAdded;
            PhoneLinkService.TransferProgress += OnTransferProgress;
            foreach (var m in PhoneLinkService.GetMessages()) AppendBubble(m);
            UpdateEmptyHint();
            ScrollToEnd();
        };
        Closed += (_, _) =>
        {
            PhoneLinkUiState.ChatDialogVisible = false;
            PhoneLinkService.MessageAdded -= OnMessageAdded;
            PhoneLinkService.TransferProgress -= OnTransferProgress;
        };
    }

    // ───────────────────────── 消息 ─────────────────────────

    private void OnMessageAdded(PhoneChatMessage m) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            AppendBubble(m);
            ScrollToEnd();
        });

    private void AppendBubble(PhoneChatMessage m)
    {
        var mine = m.From == "pc";
        var vm = new PhoneChatBubbleVm
        {
            MessageId = m.Id,
            Align = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            BubbleBrush = ThemeBrush(mine ? "AccentFillColorTertiaryBrush" : "CardBackgroundFillColorSecondaryBrush"),
            TimeText = DateTimeOffset.FromUnixTimeMilliseconds(m.Time).LocalDateTime.ToString("HH:mm")
        };

        if (m.Type == "text")
        {
            vm.Text = m.Text;
            vm.TextVisibility = Visibility.Visible;
        }
        else
        {
            vm.FileVisibility = Visibility.Visible;
            vm.FileName = m.FileName;
            vm.FileMeta = (m.Type == "image" ? "图片 · " : "文件 · ") + HumanSize(m.Size);
            var path = PhoneLinkService.GetMessageFilePath(m.Id);
            if (path is not null && File.Exists(path))
            {
                vm.SaveVisibility = Visibility.Visible;
                if (m.Type == "image" && m.Size is > 0 and < 40 * 1024 * 1024)
                    _ = LoadThumbnailAsync(vm, path);
            }
        }
        _bubbles.Add(vm);
        UpdateEmptyHint();
    }

    private void UpdateEmptyHint()
        => EmptyHint.Visibility = _bubbles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async Task LoadThumbnailAsync(PhoneChatBubbleVm vm, string path)
    {
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(path); }
        catch { return; }

        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);
                var bmp = new BitmapImage { DecodePixelWidth = 480 };
                await bmp.SetSourceAsync(stream);
                vm.Thumbnail = bmp;
                vm.ThumbnailVisibility = Visibility.Visible;
            }
            catch
            {
            }
        });
    }

    private void ScrollToEnd()
    {
        if (_bubbles.Count > 0) MessageList.ScrollIntoView(_bubbles[^1]);
    }

    // ───────────────────────── 传输速度 ─────────────────────────

    private void OnTransferProgress(PhoneTransferProgress p) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (p.Completed || p.Failed) _transfers.Remove(p.Id);
            else _transfers[p.Id] = p;

            var lines = _transfers.Values.Select(t =>
            {
                var verb = t.Direction == PhoneTransferDirection.Upload ? "接收中" : "发送中";
                var percent = t.TotalBytes > 0 ? $" · {t.Bytes * 100 / t.TotalBytes}%" : "";
                return $"{verb}：{t.FileName} · {PhoneTransferProgress.FormatSpeed(t.BytesPerSecond)}{percent}";
            }).ToList();
            TransferText.Text = string.Join("\n", lines);
            TransferText.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        });

    // ───────────────────────── 发送 / 另存为 ─────────────────────────

    private void Send_Click(object sender, RoutedEventArgs e) => SendText();

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            SendText();
        }
    }

    private void SendText()
    {
        var text = InputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        PhoneLinkService.AddPcText(text);
        InputBox.Text = "";
    }

    private void SendFile_Click(object sender, RoutedEventArgs e)
    {
        foreach (var file in Win32Dialogs.PickOpenMultiple("所有文件\0*.*\0\0", "选择要发送给手机的文件"))
            PhoneLinkService.AddPcFile(file);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string id }) return;
            var path = PhoneLinkService.GetMessageFilePath(id);
            if (path is null || !File.Exists(path))
            {
                ShowStatus(InfoBarSeverity.Warning, "源文件已不存在");
                return;
            }
            var ext = Path.GetExtension(path).TrimStart('.');
            var target = Win32Dialogs.PickSave("所有文件 (*.*)|*.*", string.IsNullOrEmpty(ext) ? "bin" : ext,
                Path.GetFileName(path));
            if (string.IsNullOrEmpty(target)) return;
            await Task.Run(() => File.Copy(path, target, true));
            ShowStatus(InfoBarSeverity.Success, "已另存为：" + target);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "另存为失败：" + ex.Message);
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = false;
        StatusBar.IsOpen = true;
    }

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

    private static string HumanSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };
}

/// <summary>聊天弹窗里单个气泡的绑定数据（缩略图异步到位后靠 INPC 刷新）。</summary>
public sealed class PhoneChatBubbleVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string MessageId { get; init; } = "";
    public HorizontalAlignment Align { get; init; } = HorizontalAlignment.Left;
    public Brush BubbleBrush { get; init; } = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    public string TimeText { get; init; } = "";

    public string? Text { get; set; }
    public Visibility TextVisibility { get; set; } = Visibility.Collapsed;
    public Visibility FileVisibility { get; set; } = Visibility.Collapsed;
    public string FileName { get; set; } = "";
    public string FileMeta { get; set; } = "";
    public Visibility SaveVisibility { get; set; } = Visibility.Collapsed;

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; Notify(nameof(Thumbnail)); }
    }

    private Visibility _thumbnailVisibility = Visibility.Collapsed;
    public Visibility ThumbnailVisibility
    {
        get => _thumbnailVisibility;
        set { _thumbnailVisibility = value; Notify(nameof(ThumbnailVisibility)); }
    }
}
