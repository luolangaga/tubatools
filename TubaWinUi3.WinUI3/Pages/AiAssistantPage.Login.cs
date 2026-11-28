using Microsoft.UI.Xaml;
using TubaWinUi3.Services.Ai;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Pages;

public sealed partial class AiAssistantPage
{
    private bool _zenLoginPending;

    /// <summary>
    /// OpenCode Zen sign-in from the quota notice. The key is written into the component's own
    /// auth store; the component reads it at startup, so it is restarted when nothing is running.
    /// </summary>
    private async void LoginZen_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _preparing) return;
        var state = _active;
        LoginButton.IsEnabled = false;
        try
        {
            var key = await OpenCodeZenLoginWindow.ShowAsync();
            if (_closed || string.IsNullOrWhiteSpace(key)) return;
            await ZenCredentialStore.SaveAsync(key, _lifetime?.Token ?? CancellationToken.None);
            _zenLoginPending = true;
            await ApplyZenLoginAsync();
        }
        catch (Exception ex)
        {
            if (!_closed) SetNotice(state, AgentText.FriendlyError(ex), true, retry: "prepare");
        }
        finally { LoginButton.IsEnabled = !_preparing; }
    }

    /// <summary>Restarts the AI component so the newly stored Zen key applies; running turns block that.</summary>
    private async Task ApplyZenLoginAsync()
    {
        if (_closed) return;
        if (_sessions.Values.Append(_active).Any(s => s.Run is not null || s.Draining))
        {
            SetNotice(_active, L("ZenLoginDeferred", "已保存 OpenCode Zen 登录信息，当前任务结束后自动重启 AI 组件生效。"), attention: true);
            return;
        }
        _zenLoginPending = false;
        if (_preparing || !AiComponentService.IsInstalled || _client is null) return;
        // Keep drafts and history; only this page's dedicated component is restarted.
        _client.Dispose(); _client = new(); _models = []; ModelBox.ItemsSource = null;
        _lifetime ??= new CancellationTokenSource();
        await PrepareAsync();
        if (!_closed && Ready && ModelBox.SelectedItem is AgentModel)
            SetNotice(_active, L("ZenLoginDone", "已登录 OpenCode Zen，免费模型额度已提升。"), attention: true);
    }
}
