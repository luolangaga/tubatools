using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 联机组件准备向导：下载 EasyTier（约 30MB）→ 就绪。
/// 没有账号、没有登录，所以只有两张步骤卡。
/// </summary>
public sealed partial class GameTunnelSetupDialog : ContentDialog
{
    private bool _busy;
    private bool _ready;
    private bool _confirmUninstall;
    private CancellationTokenSource? _cts;

    /// <summary>关闭对话框时组件是否可用。</summary>
    public bool IsEnvironmentReady => _ready;

    public GameTunnelSetupDialog(XamlRoot xamlRoot)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        RequestedTheme = ThemeService.CurrentElementTheme;
        Closed += (_, _) => _cts?.Cancel();
    }

    public async Task<bool> RunAsync()
    {
        Refresh();
        await ShowAsync();
        return _ready;
    }

    private void Refresh()
    {
        EasyTierPackage.Invalidate();
        _ready = EasyTierPackage.IsReady;

        if (_ready)
        {
            MarkDone(Step1Badge, Step1Icon);
            Step1Status.Text = "联机组件已就绪";
            MarkDone(Step2Badge, Step2Icon);
            Step2Status.Text = "可以建房或加入房间了";
            ComponentVersionText.Text = $"EasyTier {EasyTierPackage.Version} · {EasyTierPackage.Architecture}";
            ComponentVersionText.Visibility = Visibility.Visible;
            ComponentPathText.Text = EasyTierPackage.InstallDirectory;
            ComponentPathText.Visibility = Visibility.Visible;
        }
        else
        {
            MarkPending(Step1Badge, Step1Icon);
            Step1Status.Text = "未下载 · 首次联机需要下载约 30MB";
            MarkIdle(Step2Badge, Step2Icon);
            Step2Status.Text = "组件下载完成后即可建房或加入";
            ComponentVersionText.Visibility = Visibility.Collapsed;
            ComponentPathText.Visibility = Visibility.Collapsed;
        }

        PrimaryButtonText = _ready ? "完成" : "下载联机组件";
        SecondaryButtonText = _ready && !_busy ? "卸载组件" : null;
        IsPrimaryButtonEnabled = !_busy;
        IsSecondaryButtonEnabled = !_busy && _ready;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_busy)
        {
            args.Cancel = true;
            return;
        }
        if (_ready) return;

        args.Cancel = true;
        await InstallAsync();
    }

    private async Task InstallAsync()
    {
        _busy = true;
        Step1Progress.Visibility = Visibility.Visible;
        Step1Progress.IsIndeterminate = true;
        Step1ProgressText.Visibility = Visibility.Visible;
        Step1ProgressText.Text = "正在准备下载…";
        SetButtonsEnabled(false);
        ShowStatus(InfoBarSeverity.Informational, "开始下载联机组件");

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var progress = new Progress<(int Percent, string Message)>(report =>
        {
            Step1Progress.IsIndeterminate = report.Percent < 0;
            if (report.Percent >= 0) Step1Progress.Value = report.Percent;
            Step1ProgressText.Text = report.Message;
        });

        try
        {
            await EasyTierPackage.EnsureInstalledAsync(progress, _cts.Token);
            ShowStatus(InfoBarSeverity.Success, "联机组件已就绪");
        }
        catch (OperationCanceledException)
        {
            ShowStatus(InfoBarSeverity.Warning, "下载已取消");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            _busy = false;
            Step1Progress.Visibility = Visibility.Collapsed;
            Step1ProgressText.Visibility = Visibility.Collapsed;
            Refresh();
        }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (!_ready || _busy) return;

        // ContentDialog 里不能再套 ContentDialog，所以卸载确认就地做（点两次）。
        if (!_confirmUninstall)
        {
            _confirmUninstall = true;
            SecondaryButtonText = "再点一次确认卸载";
            ShowStatus(InfoBarSeverity.Warning,
                "卸载会删掉已下载的联机组件（约 60MB），下次联机需要重新下载；房间码与设置不受影响。");
            return;
        }

        _confirmUninstall = false;
        EasyTierPackage.Uninstall();
        _ready = false;
        Refresh();
        ShowStatus(InfoBarSeverity.Success, "已卸载联机组件");
    }

    private void SetButtonsEnabled(bool enabled)
    {
        IsPrimaryButtonEnabled = enabled;
        IsSecondaryButtonEnabled = enabled;
        CloseButtonText = enabled ? "关闭" : "稍后再说";
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = false;
        StatusBar.IsOpen = true;
    }

    private static void MarkDone(Border badge, FontIcon icon)
    {
        badge.Background = GameTunnelUi.ThemeBrush("SystemFillColorSuccessBackgroundBrush");
        icon.Foreground = GameTunnelUi.ThemeBrush("SystemFillColorSuccessBrush");
        icon.Glyph = "\uE73E";
    }

    private static void MarkPending(Border badge, FontIcon icon)
    {
        badge.Background = GameTunnelUi.ThemeBrush("SystemFillColorCautionBackgroundBrush");
        icon.Foreground = GameTunnelUi.ThemeBrush("SystemFillColorCautionBrush");
        icon.Glyph = "\uE9F5";
    }

    private static void MarkIdle(Border badge, FontIcon icon)
    {
        badge.Background = GameTunnelUi.ThemeBrush("SubtleFillColorSecondaryBrush");
        icon.Foreground = GameTunnelUi.ThemeBrush("TextFillColorSecondaryBrush");
        icon.Glyph = "\uE9F5";
    }
}
