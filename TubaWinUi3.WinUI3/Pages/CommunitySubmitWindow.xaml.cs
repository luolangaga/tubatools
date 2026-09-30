using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using TubaWinUi3.Services;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

public sealed partial class CommunitySubmitWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private string _errorDetail = "";
    private readonly string _name;
    private readonly string _description;
    private readonly string _category;
    private readonly string _tags;
    private readonly string? _zipFilePath;
    private readonly string _launchTarget;
    private readonly string _publisher;
    private readonly string _homepage;
    private readonly string _version;
    private readonly string? _iconFilePath;
    private readonly string? _downloadUrl;
    private readonly string? _downloadFilter;
#pragma warning disable CS0414
    private bool _completed;
#pragma warning restore CS0414

#pragma warning disable CS0067
    public event Action? SubmitSucceeded;
#pragma warning restore CS0067

    public CommunitySubmitWindow(
        string name, string description, string category, string tags,
        string? zipFilePath, string launchTarget,
        string publisher, string homepage, string version,
        string? iconFilePath = null,
        string? downloadUrl = null,
        string? downloadFilter = null)
    {
        InitializeComponent();

        _name = name;
        _description = description;
        _category = category;
        _tags = tags;
        _zipFilePath = zipFilePath;
        _launchTarget = launchTarget;
        _publisher = publisher;
        _homepage = homepage;
        _version = version;
        _iconFilePath = iconFilePath;
        _downloadUrl = downloadUrl;
        _downloadFilter = downloadFilter;

        AppWindow.Title = "图吧工具箱CE - 提交社区工具";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var screenArea = displayArea.WorkArea;
        var width = (int)(screenArea.Width * 0.6);
        var height = (int)(screenArea.Height * 0.8);
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            (screenArea.Width - width) / 2,
            (screenArea.Height - height) / 2));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        StartSubmit();
    }

    private void StartSubmit()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<string>(msg =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        try { ProgressText.Text = msg; } catch { }
                    });
                });

                var prUrl = await CommunityToolService.SubmitPluginAsync(
                    _name, _description, _category,
                    _tags, _zipFilePath, _launchTarget,
                    _publisher, _homepage,
                    _version, progress, _iconFilePath, _cts.Token,
                    _downloadUrl, _downloadFilter);

                DispatcherQueue.TryEnqueue(() => ShowSuccess(prUrl));
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() => ShowError("已取消提交"));
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (string.IsNullOrWhiteSpace(msg)) msg = ex.GetType().Name;
                if (ex.InnerException is { } inner && !string.IsNullOrWhiteSpace(inner.Message))
                    msg += $"\n{inner.Message}";
                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    msg += $"\n\n{ex.StackTrace}";
                DispatcherQueue.TryEnqueue(() => ShowError(msg));
            }
        }, _cts.Token);
    }

    private void ShowSuccess(string? prUrl)
    {
        _completed = true;

        HeaderIcon.Glyph = "\uE73E";
        HeaderIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 15, 123, 15));
        IconBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(51, 15, 123, 15));
        TitleText.Text = "提交成功";
        SubtitleText.Text = "你的工具已成功提交到社区仓库";

        ProgressCard.Visibility = Visibility.Collapsed;
        SuccessCard.Visibility = Visibility.Visible;
        ErrorCard.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(prUrl))
        {
            try
            {
                PrLink.NavigateUri = new Uri(prUrl);
                PrLink.Visibility = Visibility.Visible;
            }
            catch { }
        }

        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string error)
    {
        _completed = true;
        _errorDetail = error;

        HeaderIcon.Glyph = "\uE783";
        HeaderIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 255, 68, 68));
        IconBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(51, 255, 68, 68));
        TitleText.Text = "提交失败";
        SubtitleText.Text = "提交过程中出现错误，请查看详情";

        ProgressCard.Visibility = Visibility.Collapsed;
        SuccessCard.Visibility = Visibility.Collapsed;
        ErrorCard.Visibility = Visibility.Visible;
        ErrorText.Text = error;

        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Visible;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _completed = false;
        _errorDetail = "";

        HeaderIcon.Glyph = "\uE898";
        HeaderIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 0, 102, 204));
        IconBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(51, 0, 102, 204));
        TitleText.Text = "正在提交";
        SubtitleText.Text = "正在将你的工具提交到社区仓库...";

        ProgressCard.Visibility = Visibility.Visible;
        SuccessCard.Visibility = Visibility.Collapsed;
        ErrorCard.Visibility = Visibility.Collapsed;
        SubmitProgressBar.IsIndeterminate = true;
        ProgressText.Text = "正在 Fork 仓库...";

        CancelButton.Visibility = Visibility.Visible;
        CloseButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;

        StartSubmit();
    }

    private void CopyErrorButton_Click(object sender, RoutedEventArgs e)
    {
        // 剪贴板写入失败不得让窗口本身崩溃（详见 ClipboardService）
        var result = ClipboardService.TrySetText(_errorDetail);
        CopyErrorButtonText.Text = result.Success ? "已复制" : "复制失败，请重试";
    }
}
