using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Controls;

/// <summary>
/// 「邀请朋友」面板：拿到联机信息后负责产出三种邀请方式
/// （邀请码 / 一键加入脚本 / 纯地址），并在缺少邀请密钥时就地引导补齐。
/// 面板会出现在主机向导的最后一步，也会被主页的「邀请朋友」按钮复用。
/// </summary>
public sealed partial class GameTunnelInvitePanel : UserControl
{
    private InviteInfo? _info;
    private string? _inviteCode;
    private string? _apiKeyId;
    private string _scriptPath = "";
    private CancellationTokenSource? _cts;

    /// <summary>剪贴板守望：用户从控制台复制令牌/密钥后自动填入，不用手动粘贴。</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _clipTimer;
    private string? _clipboardLastSeen;
    private string? _clipboardApplied;
    private bool _clipboardBusy;

    /// <summary>
    /// 刚刚生成过的邀请密钥缓存：同一局联机反复打开邀请面板时复用同一把，
    /// 免得朋友手里的邀请码失效、也免得在 tailnet 里堆一堆短期密钥。
    /// </summary>
    private static (string Key, string? KeyId, DateTimeOffset? Expires, string Host, int Port, DateTimeOffset CreatedUtc)? _recentKey;
    private static readonly TimeSpan RecentKeyLifetime = TimeSpan.FromMinutes(10);

    /// <summary>邀请码是否已经可用（宿主页面据此提示房间状态，避免「房间看着好了却发不出邀请」）。</summary>
    public event EventHandler<bool>? InviteStateChanged;

    public GameTunnelInvitePanel()
    {
        InitializeComponent();

        // 三种方式的说明统一取自 GameTunnelInviteCopy，全应用口径一致
        InviteDirectNote.Text = GameTunnelInviteCopy.InviteCodeDirect;
        ScriptDirectNote.Text = GameTunnelInviteCopy.ScriptDirect;
        AddressOnlyPreconditionText.Text = GameTunnelInviteCopy.AddressOnlyPrecondition;

        Unloaded += (_, _) =>
        {
            _cts?.Cancel();
            StopClipboardWatch();
        };
    }

    /// <summary>联机信息就绪后调用；已配置 API 密钥时自动生成邀请码。</summary>
    public async Task InitializeAsync(InviteInfo info)
    {
        _info = info;
        AddressText.Text = info.Address;

        var settings = GameTunnelCatalog.LoadSettings();
        var token = TailscaleApiService.NormalizeApiKey(settings.ApiToken);
        if (token is null)
        {
            ShowKeyPrompt();
            return;
        }

        await GenerateWithApiAsync(token);
    }

    /// <summary>把已经拿到的密钥直接灌进来（例如手动粘贴、或重复使用上次的密钥）。</summary>
    public void ApplyAuthKey(string authKey, string? keyId = null, DateTimeOffset? expires = null)
    {
        if (_info is null) return;
        var normalized = TailscaleService.NormalizeAuthKey(authKey);
        if (normalized is null)
        {
            ShowError("这个密钥格式不对，授权密钥应以 tskey-auth- 开头");
            return;
        }

        _info.AuthKey = normalized;
        _info.ExpiresAt = expires?.ToUnixTimeSeconds() ?? 0;
        _apiKeyId = keyId;
        RenderReady();
    }

    private async Task GenerateWithApiAsync(string token)
    {
        if (_info is null) return;

        // 同一房间短时间内重复打开：复用刚生成的密钥
        if (_recentKey is { } recent
            && recent.Host == _info.Host && recent.Port == _info.Port
            && DateTimeOffset.UtcNow - recent.CreatedUtc < RecentKeyLifetime
            && (recent.Expires is null || recent.Expires > DateTimeOffset.UtcNow.AddMinutes(30)))
        {
            ApplyAuthKey(recent.Key, recent.KeyId, recent.Expires);
            ShowStatus(InfoBarSeverity.Informational, "沿用了刚才生成的邀请码");
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ShowBusy("正在生成邀请码…");

        // 描述只在 Tailscale 控制台的密钥列表里出现，且服务端只接受 ASCII，
        // 所以固定用英文前缀 + 清洗过的游戏名 + 端口（中文名会被清洗掉，但不影响生成）
        var gameLabel = TailscaleApiService.SanitizeKeyDescription(_info.Game);
        var description = gameLabel.Length > 0
            ? $"Tuba Tunnel - {gameLabel} {_info.Port}"
            : $"Tuba Tunnel - {_info.Port}";

        var result = await TailscaleApiService.CreateInviteKeyAsync(token, description, ct: ct);

        if (ct.IsCancellationRequested) return;

        if (result.Ok && result.Key is { Length: > 0 })
        {
            _recentKey = (result.Key, result.KeyId, result.Expires, _info.Host, _info.Port, DateTimeOffset.UtcNow);
            ApplyAuthKey(result.Key, result.KeyId, result.Expires);
            ShowStatus(InfoBarSeverity.Success, "邀请码已生成，直接发给朋友即可");
            return;
        }

        // 令牌失效时回到引导态，并把原因讲清楚
        ShowKeyPrompt(result.Error);
        if (result.Error is { Length: > 0 }) ApiKeyBox.Text = "";
    }

    // ══════════════════ 状态切换 ══════════════════

    private void ShowBusy(string text)
    {
        BusyText.Text = text;
        BusyPanel.Visibility = Visibility.Visible;
        KeyPromptPanel.Visibility = Visibility.Collapsed;
        ReadyPanel.Visibility = Visibility.Collapsed;
        StopClipboardWatch();
        InviteStateChanged?.Invoke(this, false);
    }

    private void ShowKeyPrompt(string? error = null)
    {
        BusyPanel.Visibility = Visibility.Collapsed;
        KeyPromptPanel.Visibility = Visibility.Visible;
        ReadyPanel.Visibility = Visibility.Collapsed;
        InviteStateChanged?.Invoke(this, false);
        StartClipboardWatch();

        if (!string.IsNullOrWhiteSpace(error))
        {
            ApiKeyError.Text = error;
            ApiKeyError.Visibility = Visibility.Visible;
        }
    }

    private void ShowError(string message) => ShowStatus(InfoBarSeverity.Error, message);

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = false;
        StatusBar.IsOpen = true;
    }

    private void RenderReady()
    {
        if (_info is null) return;

        BusyPanel.Visibility = Visibility.Collapsed;
        KeyPromptPanel.Visibility = Visibility.Collapsed;
        ReadyPanel.Visibility = Visibility.Visible;
        StopClipboardWatch();
        InviteStateChanged?.Invoke(this, true);

        _inviteCode = GameTunnelInvite.Encode(_info);
        InviteCodeBox.Text = _inviteCode;
        AddressText.Text = _info.Address;

        ExpiryText.Text = _info.ExpiresAtLocal is { } expires
            ? $"这把密钥会在 {expires:HH:mm} 自动失效，届时朋友无法再用它加入。"
            : "这把密钥没有记录到期时间，失效后重新生成即可。";

        RevokeButton.Visibility = _apiKeyId is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

        _scriptPath = GameTunnelInvite.WriteScriptToDataDir(_info);
        ScriptHint.Text = $"脚本已生成：{_scriptPath}";
    }

    // ══════════════════ 密钥输入 ══════════════════

    private void UseApiKey_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyForm.Visibility = ApiKeyForm.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (ApiKeyForm.Visibility == Visibility.Visible)
        {
            ManualKeyForm.Visibility = Visibility.Collapsed;
            ApiKeyBox.Focus(FocusState.Programmatic);
            _ = TryAutoFillFromClipboardAsync();
        }
    }

    private void ManualKey_Click(object sender, RoutedEventArgs e)
    {
        ManualKeyForm.Visibility = ManualKeyForm.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (ManualKeyForm.Visibility == Visibility.Visible)
        {
            ApiKeyForm.Visibility = Visibility.Collapsed;
            ManualKeyBox.Focus(FocusState.Programmatic);
            _ = TryAutoFillFromClipboardAsync();
        }
    }

    private async void PasteApiKey_Click(object sender, RoutedEventArgs e)
    {
        var key = ExtractKey(await ReadClipboardTextAsync(), ApiKeyPrefix);
        if (key is null)
        {
            ShowApiKeyError("剪贴板里没有找到 tskey-api- 开头的令牌，请先在控制台点「复制」");
            return;
        }

        ApiKeyBox.Text = key;
        await ValidateAndSaveApiKeyAsync(key);
    }

    private void PasteAuthKey_Click(object sender, RoutedEventArgs e)
    {
        // 同步等待剪贴板可能失败，这里整体异步处理
        _ = PasteAuthKeyCoreAsync();
    }

    private async Task PasteAuthKeyCoreAsync()
    {
        var key = ExtractKey(await ReadClipboardTextAsync(), AuthKeyPrefix);
        if (key is null)
        {
            ManualKeyError.Text = "剪贴板里没有找到 tskey-auth- 开头的授权密钥，请先在控制台点「复制」";
            ManualKeyError.Visibility = Visibility.Visible;
            return;
        }

        ManualKeyBox.Text = key;
        ApplyManualAuthKey(key);
    }

    private async void ApiKeySave_Click(object sender, RoutedEventArgs e) => await ValidateAndSaveApiKeyAsync(ApiKeyBox.Text);

    /// <summary>验证并保存 API 令牌，然后立刻生成邀请码。手动点按钮和剪贴板自动填入都走这里。</summary>
    private async Task ValidateAndSaveApiKeyAsync(string raw)
    {
        var mismatch = TailscaleApiService.DescribeKeyKindMismatch(raw);
        if (mismatch is not null)
        {
            ShowApiKeyError(mismatch);
            return;
        }

        var token = TailscaleApiService.NormalizeApiKey(raw);
        if (token is null)
        {
            ShowApiKeyError("令牌格式不对：API 访问令牌以 tskey-api- 开头");
            return;
        }

        ApiKeyError.Visibility = Visibility.Collapsed;
        ApiKeySaveButton.IsEnabled = false;
        ApiKeySaveButton.Content = "正在验证…";
        try
        {
            var check = await TailscaleApiService.ValidateTokenAsync(token);
            if (!check.Ok)
            {
                ShowApiKeyError(check.Error ?? "令牌不可用");
                return;
            }

            var settings = GameTunnelCatalog.LoadSettings();
            settings.ApiToken = token;
            GameTunnelCatalog.SaveSettings(settings);
            ApiKeyForm.Visibility = Visibility.Collapsed;
            await GenerateWithApiAsync(token);
        }
        catch (Exception ex)
        {
            ShowApiKeyError($"验证令牌时出错：{ex.Message}");
        }
        finally
        {
            ApiKeySaveButton.IsEnabled = true;
            ApiKeySaveButton.Content = "验证并保存";
        }
    }

    private void ShowApiKeyError(string message)
    {
        ApiKeyError.Text = message;
        ApiKeyError.Visibility = Visibility.Visible;
    }

    private void ManualKeyApply_Click(object sender, RoutedEventArgs e) => ApplyManualAuthKey(ManualKeyBox.Text);

    private void ApplyManualAuthKey(string raw)
    {
        var key = TailscaleService.NormalizeAuthKey(raw);
        if (key is null)
        {
            ManualKeyError.Text = "密钥格式不对：授权密钥以 tskey-auth- 开头，注意不要填成 API 令牌";
            ManualKeyError.Visibility = Visibility.Visible;
            return;
        }

        ManualKeyError.Visibility = Visibility.Collapsed;
        ManualKeyForm.Visibility = Visibility.Collapsed;
        ApplyAuthKey(key);
        ShowStatus(InfoBarSeverity.Success, "已使用你提供的密钥生成邀请信息");
    }

    // ══════════════════ 剪贴板守望 ══════════════════

    private const string ApiKeyPrefix = "tskey-api-";
    private const string AuthKeyPrefix = "tskey-auth-";

    /// <summary>
    /// 缺密钥时盯着剪贴板：用户在控制台点「复制」后回到本窗口，令牌/密钥会自动填入并立即使用。
    /// 这样就不依赖 Ctrl+V（有些环境里粘贴会被抢焦点或剪贴板锁住而失败）。
    /// </summary>
    private void StartClipboardWatch()
    {
        _clipTimer ??= DispatcherQueue.CreateTimer();
        _clipTimer.Interval = TimeSpan.FromMilliseconds(900);
        _clipTimer.Tick -= ClipboardTimer_Tick;
        _clipTimer.Tick += ClipboardTimer_Tick;
        if (!_clipTimer.IsRunning) _clipTimer.Start();
        _ = TryAutoFillFromClipboardAsync();
    }

    private void StopClipboardWatch() => _clipTimer?.Stop();

    private async void ClipboardTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        => await TryAutoFillFromClipboardAsync();

    private async Task TryAutoFillFromClipboardAsync()
    {
        if (_clipboardBusy || _info is null) return;
        if (KeyPromptPanel.Visibility != Visibility.Visible) return;

        _clipboardBusy = true;
        try
        {
            var text = await ReadClipboardTextAsync();
            if (string.IsNullOrWhiteSpace(text) || text == _clipboardLastSeen) return;
            _clipboardLastSeen = text;

            var apiKey = ExtractKey(text, ApiKeyPrefix);
            if (apiKey is not null && apiKey != _clipboardApplied)
            {
                _clipboardApplied = apiKey;
                ApiKeyForm.Visibility = Visibility.Visible;
                ManualKeyForm.Visibility = Visibility.Collapsed;
                ApiKeyBox.Text = apiKey;
                ShowStatus(InfoBarSeverity.Informational, "已从剪贴板读取 API 令牌，正在验证…");
                await ValidateAndSaveApiKeyAsync(apiKey);
                return;
            }

            var authKey = ExtractKey(text, AuthKeyPrefix);
            if (authKey is not null && authKey != _clipboardApplied)
            {
                _clipboardApplied = authKey;
                ManualKeyForm.Visibility = Visibility.Visible;
                ApiKeyForm.Visibility = Visibility.Collapsed;
                ManualKeyBox.Text = authKey;
                ShowStatus(InfoBarSeverity.Informational, "已从剪贴板读取授权密钥");
                ApplyManualAuthKey(authKey);
            }
        }
        catch
        {
            // 剪贴板可能被别的程序锁住，下一轮再看
        }
        finally
        {
            _clipboardBusy = false;
        }
    }

    private static async Task<string?> ReadClipboardTextAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content is null || !content.Contains(StandardDataFormats.Text)) return null;
            return await content.GetTextAsync();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从一段文本里抠出完整密钥（控制台「复制」可能带上说明文字）。</summary>
    internal static string? ExtractKey(string? text, string prefix) => TailscaleApiService.ExtractKey(text, prefix);

    private void OpenKeysPage_Click(object sender, RoutedEventArgs e) => OpenUrl(TailscaleService.KeysUrl);

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ══════════════════ 三种邀请方式 ══════════════════

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_inviteCode is { Length: > 0 }) Copy(_inviteCode, "邀请码已复制，发给朋友吧");
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (_info is null) return;
        var text = GameTunnelInvite.BuildInviteText(_info, _inviteCode, hasScript: true);
        Copy(text, "邀请说明已复制，可以直接粘贴到聊天窗口");
    }

    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        if (_info is null) return;
        Copy(_info.Address, "地址已复制");
    }

    private void Copy(string text, string message)
    {
        // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
        if (ClipboardService.TrySetText(text).Success)
            ShowStatus(InfoBarSeverity.Success, message);
        else
            ShowError("复制失败，请手动选中文本复制");
    }

    private void SaveScript_Click(object sender, RoutedEventArgs e)
    {
        if (_info is null) return;
        try
        {
            var suggested = GameTunnelInvite.ScriptFileName(_info.Game);
            var target = Win32Dialogs.PickSave("命令脚本 (*.cmd)|*.cmd|所有文件 (*.*)|*.*", "cmd", suggested);
            if (string.IsNullOrWhiteSpace(target)) return;

            GameTunnelInvite.WriteScript(GameTunnelInvite.BuildScript(_info), target);
            _scriptPath = target;
            ScriptHint.Text = $"脚本已保存：{target}";
            ShowStatus(InfoBarSeverity.Success, "脚本已保存，把这个文件发给朋友即可");
        }
        catch (Exception ex)
        {
            ShowError($"保存脚本失败：{ex.Message}");
        }
    }

    private void OpenScriptsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(_scriptPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_scriptPath}\"") { UseShellExecute = true });
            }
            else
            {
                var dir = GameTunnelCatalog.GetScriptsDir();
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
        }
        catch
        {
            ShowError("无法打开文件夹");
        }
    }

    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (_apiKeyId is not { Length: > 0 } keyId) return;

        var settings = GameTunnelCatalog.LoadSettings();
        var token = TailscaleApiService.NormalizeApiKey(settings.ApiToken);
        if (token is null)
        {
            ShowError("没有可用的 API 令牌，无法撤销；密钥到期后会自行失效");
            return;
        }

        RevokeButton.IsEnabled = false;
        var result = await TailscaleApiService.RevokeKeyAsync(token, keyId);
        RevokeButton.IsEnabled = true;

        if (result.Ok)
        {
            _apiKeyId = null;
            RevokeButton.Visibility = Visibility.Collapsed;
            ExpiryText.Text = "邀请码已撤销，朋友无法再用它加入。";
            ShowStatus(InfoBarSeverity.Success, "邀请码已撤销");
        }
        else
        {
            ShowError(result.Error ?? "撤销失败");
        }
    }
}
