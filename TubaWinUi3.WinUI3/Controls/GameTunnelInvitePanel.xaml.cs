using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

/// <summary>
/// 「邀请朋友」面板：房间建立后产出三种邀请方式（邀请码 / 一键加入脚本 / 游戏地址）。
/// 房间码里已经带着网络密钥，所以不再需要 API 令牌或手输密钥。
/// </summary>
public sealed partial class GameTunnelInvitePanel : UserControl
{
    private InviteInfo? _info;
    private string? _inviteCode;
    private string _scriptPath = "";

    /// <summary>邀请是否已经可用（宿主页面据此提示房间状态）。</summary>
    public event EventHandler<bool>? InviteStateChanged;

    public GameTunnelInvitePanel()
    {
        InitializeComponent();

        InviteDirectNote.Text = "邀请码里带着这个房间的网络密钥：朋友用它直接加入，不需要你点批准，也不需要他自己注册任何账号。";
        ScriptDirectNote.Text = "脚本里的参数和你的房间一致：朋友双击后自动加入并建立端口转发，不需要任何人同意。";
        AddressOnlyPreconditionText.Text = "朋友加入后，游戏里填这个地址；我的世界 Java 版可以直接从「扫描局域网世界」进入。";
    }

    /// <summary>房间建立后调用。</summary>
    public Task InitializeAsync(InviteInfo info)
    {
        _info = info;
        AddressText.Text = info.Address;

        _inviteCode = GameTunnelInvite.Encode(info);
        InviteCodeBox.Text = _inviteCode;

        _scriptPath = GameTunnelInvite.WriteScriptToDataDir(info, GameTunnelCatalog.GetNodes(GameTunnelCatalog.LoadSettings()));
        ScriptHint.Text = $"脚本已生成：{_scriptPath}";

        ReadyPanel.Visibility = Visibility.Visible;
        InviteStateChanged?.Invoke(this, true);
        return Task.CompletedTask;
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

            var nodes = GameTunnelCatalog.GetNodes(GameTunnelCatalog.LoadSettings());
            GameTunnelInvite.WriteScript(GameTunnelInvite.BuildScript(_info, nodes), target);
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

    private void ShowError(string message) => ShowStatus(InfoBarSeverity.Error, message);

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = false;
        StatusBar.IsOpen = true;
    }
}
