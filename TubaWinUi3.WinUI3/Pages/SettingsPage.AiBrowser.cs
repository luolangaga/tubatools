using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Pages;

public sealed partial class SettingsPage
{
    private CancellationTokenSource? _aiBrowserCts;

    private void InitAiBrowserSettings()
    {
        if (AiBrowserStatusText is null) return;
        ApplyAiBrowserResult(AiBrowserSetupService.GetStatus());
    }

    private Task RefreshAiBrowserStatusAsync()
    {
        InitAiBrowserSettings();
        return Task.CompletedTask;
    }

    private async Task OpenAiBrowserAsync()
    {
        _aiBrowserCts?.Cancel();
        _aiBrowserCts?.Dispose();
        var cts = new CancellationTokenSource();
        _aiBrowserCts = cts;
        AiBrowserProgress.IsActive = true;
        AiBrowserStatusText.Text = LocalizationService.L("Settings_AiBrowserStatusChecking", "正在打开 AI 浏览器…");
        try
        {
            var result = await AiBrowserSetupService.EnsureAsync(cts.Token);
            if (!cts.IsCancellationRequested) ApplyAiBrowserResult(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) ShowAiBrowserError(ex.Message);
        }
        finally
        {
            if (!cts.IsCancellationRequested) AiBrowserProgress.IsActive = false;
        }
    }

    private async Task RepairAiBrowserAsync()
    {
        _aiBrowserCts?.Cancel();
        _aiBrowserCts?.Dispose();
        var cts = new CancellationTokenSource();
        _aiBrowserCts = cts;
        AiBrowserProgress.IsActive = true;
        AiBrowserStatusText.Text = LocalizationService.L("Settings_AiBrowserRepairing", "正在修复并重新打开…");
        try
        {
            var result = await AiBrowserSetupService.RepairAsync(cts.Token);
            if (!cts.IsCancellationRequested) ApplyAiBrowserResult(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) ShowAiBrowserError(ex.Message);
        }
        finally
        {
            if (!cts.IsCancellationRequested) AiBrowserProgress.IsActive = false;
        }
    }

    private async Task CloseAiBrowserAsync()
    {
        _aiBrowserCts?.Cancel();
        _aiBrowserCts?.Dispose();
        var cts = new CancellationTokenSource();
        _aiBrowserCts = cts;
        try
        {
            await AiBrowserSetupService.CloseAsync(cts.Token);
            if (!cts.IsCancellationRequested) ApplyAiBrowserResult(AiBrowserSetupService.GetStatus());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) ShowAiBrowserError(ex.Message);
        }
    }

    private void ApplyAiBrowserResult(AiBrowserSetupResult result)
    {
        AiBrowserProgress.IsActive = false;
        if (!result.Ok)
        {
            AiBrowserStatusText.Text = string.Format(
                LocalizationService.L("Settings_AiBrowserStatusFailed", "连接失败：{0}"), result.Error);
            ShowAiBrowserError(result.Error);
            return;
        }

        AiBrowserInfoBar.IsOpen = false;
        AiBrowserStatusText.Text = (result.Running
                ? LocalizationService.L("Settings_AiBrowserStatusBuiltInReady", "内置 WebView2 已就绪")
                : LocalizationService.L("Settings_AiBrowserStatusBuiltInIdle", "内置 WebView2 · 打开 AI 浏览器时创建")) +
            (string.IsNullOrWhiteSpace(result.Url) ? "" : "\n" + result.Url) +
            "\n" + LocalizationService.L("Settings_AiBrowserProfileHint", "专用用户数据：") + result.ProfilePath;
    }

    private void ShowAiBrowserError(string message)
    {
        AiBrowserProgress.IsActive = false;
        AiBrowserInfoBar.Severity = InfoBarSeverity.Error;
        AiBrowserInfoBar.Title = LocalizationService.L("Settings_AiBrowserInfoTitle", "AI 浏览器未就绪");
        AiBrowserInfoBar.Message = message;
        AiBrowserInfoBar.IsOpen = true;
    }

    private void AiBrowserOpen_Click(object sender, RoutedEventArgs e) => _ = OpenAiBrowserAsync();

    private void AiBrowserRepair_Click(object sender, RoutedEventArgs e) => _ = RepairAiBrowserAsync();

    private void AiBrowserClose_Click(object sender, RoutedEventArgs e) => _ = CloseAiBrowserAsync();
}
