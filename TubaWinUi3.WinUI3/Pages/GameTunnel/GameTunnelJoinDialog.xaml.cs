using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

/// <summary>
/// 加入向导：粘贴房间码 → （首次）下载组件 → 加入虚拟网络 → 找到房主并转发端口 → 给出本机地址。
/// 房间码里带着网络密钥，所以不需要账号、登录或对方批准。
/// </summary>
public sealed partial class GameTunnelJoinDialog : ContentDialog
{
    private InviteInfo? _invite;
    private bool _busy;
    private bool _connected;
    private bool _startedJoin;
    private int _localPort;

    /// <summary>关闭时是否已经连上房间。</summary>
    public bool Connected => _connected;

    public GameTunnelJoinDialog(XamlRoot xamlRoot)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        RequestedTheme = ThemeService.CurrentElementTheme;
        Closed += (_, _) =>
        {
            // 只收回自己发起的加入；用户开着房间时打开本弹窗又关掉，不能把已有房间停掉。
            if (_startedJoin && !_connected) TunnelSession.Instance.Stop();
        };
    }

    public async Task<bool> RunAsync()
    {
        await TryFillFromClipboardAsync();
        UpdateChrome();
        await ShowAsync();
        return _connected;
    }

    // ══════════════════ 第 1 步：输入 ══════════════════

    /// <summary>剪贴板里正好是邀请码时自动填入——用户从聊天软件复制后直接就能下一步。</summary>
    private async Task TryFillFromClipboardAsync()
    {
        try
        {
            var clipboard = Clipboard.GetContent();
            if (!clipboard.Contains(StandardDataFormats.Text)) return;
            var text = await clipboard.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (GameTunnelInvite.Decode(text) is null) return;
            InviteBox.Text = text;
        }
        catch
        {
        }
    }

    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = Clipboard.GetContent();
            if (clipboard.Contains(StandardDataFormats.Text))
            {
                InviteBox.Text = await clipboard.GetTextAsync();
            }
        }
        catch
        {
            ShowStatus(InfoBarSeverity.Error, "读不到剪贴板内容，请手动粘贴");
        }
    }

    private void InviteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _invite = GameTunnelInvite.Decode(InviteBox.Text);

        if (_invite is null)
        {
            PreviewCard.Visibility = Visibility.Collapsed;
            UpdateChrome();
            return;
        }

        PreviewCard.Visibility = Visibility.Visible;
        PreviewTitle.Text = string.IsNullOrWhiteSpace(_invite.Game) ? "识别到房间码" : $"识别到：{_invite.Game}";
        PreviewAddress.Text = _invite.RoomCode;

        var notes = new List<string>
        {
            "房间码里带着这个房间的网络密钥：粘贴后直接加入，不需要对方批准，也不用注册任何账号"
        };
        if (_invite.Port > 0) notes.Add($"房主游戏端口 {_invite.Port}");
        if (_invite.Protocol.UsesUdp()) notes.Add($"游戏使用 {_invite.Protocol.Describe()}，本工具同样支持");
        if (_invite.UsesLanDiscovery) notes.Add("加入后可直接从「扫描局域网世界」进入");
        PreviewNote.Text = string.Join(" · ", notes);

        UpdateChrome();
    }

    // ══════════════════ 第 2 步：连接 ══════════════════

    private async Task ConnectAsync()
    {
        if (_invite is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "先粘贴邀请码或房间码");
            return;
        }

        SetBusy(true, "正在连接…");
        ResetProgress();
        try
        {
            var session = TunnelSession.Instance;

            // 1) 组件
            MarkRow(JoinIcon, RowState.Running, "1/3 正在准备联机组件…");
            JoinProgress.Visibility = Visibility.Visible;
            JoinProgress.IsIndeterminate = true;

            var progress = new Progress<(int Percent, string Message)>(report =>
            {
                JoinProgress.IsIndeterminate = report.Percent < 0;
                if (report.Percent >= 0) JoinProgress.Value = report.Percent;
                JoinText.Text = $"1/3 {report.Message}";
            });

            if (!EasyTierPackage.IsReady)
            {
                await EasyTierPackage.EnsureInstalledAsync(progress);
            }

            MarkRow(JoinIcon, RowState.Done, "1/3 联机组件就绪");
            JoinProgress.Visibility = Visibility.Collapsed;

            // 2) 入网 + 找房主 + 端口转发（会话内部按步骤推进，这里实时回显）
            MarkRow(AddressIcon, RowState.Running, "2/3 正在加入虚拟网络…");
            _startedJoin = true;
            var join = session.StartJoinAsync(_invite);
            while (!join.IsCompleted)
            {
                RenderSessionSteps(session);
                await Task.WhenAny(join, Task.Delay(400));
            }
            await join;

            _localPort = session.LocalPort;
            MarkRow(AddressIcon, RowState.Done, $"2/3 已加入房间（本地端口 {_localPort}）");
            MarkRow(PingIcon, RowState.Done,
                session.HostPeer is { } host
                    ? $"3/3 房主 {host.Hostname} · {host.Describe()}"
                    : "3/3 端口转发已建立");

            EnterStep3(session);
            _connected = true;
        }
        catch (Exception ex)
        {
            _startedJoin = false;
            MarkRow(AddressIcon, RowState.Failed, "2/3 加入失败");
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void RenderSessionSteps(TunnelSession session)
    {
        // 入网 → 找房主 → 端口转发，按会话当前步骤推进文案。
        if (session.GetStepState(TunnelStep.Network) == TunnelStepState.Active)
        {
            MarkRow(AddressIcon, RowState.Running, "2/3 正在加入虚拟网络…");
            return;
        }

        if (session.GetStepState(TunnelStep.FindHost) == TunnelStepState.Active)
        {
            MarkRow(AddressIcon, RowState.Done, "2/3 已加入虚拟网络");
            MarkRow(PingIcon, RowState.Running,
                session.ConnectedNodes >= 0
                    ? $"3/3 正在寻找房主…（已连上 {session.ConnectedNodes} 个节点）"
                    : "3/3 正在寻找房主…");
            return;
        }

        if (session.GetStepState(TunnelStep.Enter) == TunnelStepState.Active)
        {
            MarkRow(AddressIcon, RowState.Done, "2/3 已加入虚拟网络");
            MarkRow(PingIcon, RowState.Running, "3/3 正在建立端口转发…");
        }
    }

    private void EnterStep3(TunnelSession session)
    {
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Collapsed;
        Step3Panel.Visibility = Visibility.Visible;

        var hostAddress = $"{_invite!.Host}:{session.GamePort}";
        FinalAddressText.Text = _localPort > 0 ? $"127.0.0.1:{_localPort}" : hostAddress;

        if (_invite.UsesLanDiscovery)
        {
            FinalHintText.Text = "不用手填地址：打开游戏「多人游戏」，房间会出现在「扫描局域网世界」里";
            FinalNoteText.Text = "没出现时再点一次「扫描」，或点「直接连接」填上面的地址（就是本机端口）。";
        }
        else
        {
            FinalHintText.Text = "在游戏里连接这个地址：";
            FinalNoteText.Text = $"本机端口已转发到房主 {hostAddress}。首次连接可能需要多试一次（双方还在打洞直连）。";
        }

        var preset = GameTunnelCatalog.Presets.FirstOrDefault(p => p.Name == _invite.Game);
        GuestStepsList.ItemsSource = preset?.GuestSteps
            .Select(step => step.Replace("{host}", "127.0.0.1").Replace("{port}", _localPort.ToString()))
            .ToList()
            ?? new List<string>
            {
                $"打开游戏，在联机界面里填 127.0.0.1:{_localPort}",
                "首次连接可能需要多试一次（网络还在打洞直连）",
                "玩完想断开：回主页点「停止联机」"
            };

        StepNameText.Text = "· 连上了";
        StepText.Text = "完成";
    }

    private void CopyFinalAddress_Click(object sender, RoutedEventArgs e)
    {
        var text = _localPort > 0 ? $"127.0.0.1:{_localPort}" : _invite?.Address;
        if (string.IsNullOrWhiteSpace(text)) return;

        // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
        if (ClipboardService.TrySetText(text).Success)
            ShowStatus(InfoBarSeverity.Success, "地址已复制");
        else
            ShowStatus(InfoBarSeverity.Error, "复制失败，请手动选中复制");
    }

    // ══════════════════ 进度行 ══════════════════

    private enum RowState
    {
        Idle,
        Running,
        Done,
        Warning,
        Failed
    }

    private void MarkRow(FontIcon icon, RowState state, string text)
    {
        icon.Glyph = state switch
        {
            RowState.Done => "\uE73E",
            RowState.Warning => "\uE7BA",
            RowState.Failed => "\uE783",
            _ => "\uE9F5"
        };
        icon.Foreground = state switch
        {
            RowState.Done => GameTunnelUi.ThemeBrush("SystemFillColorSuccessBrush"),
            RowState.Warning => GameTunnelUi.ThemeBrush("SystemFillColorCautionBrush"),
            RowState.Failed => GameTunnelUi.ThemeBrush("SystemFillColorCriticalBrush"),
            _ => GameTunnelUi.ThemeBrush("TextFillColorSecondaryBrush")
        };

        var target = icon == JoinIcon ? JoinText : icon == AddressIcon ? AddressText : PingText;
        target.Text = text;
    }

    private void ResetProgress()
    {
        JoinProgress.Value = 0;
        JoinProgress.Visibility = Visibility.Collapsed;
        MarkRow(JoinIcon, RowState.Idle, "等待开始");
        MarkRow(AddressIcon, RowState.Idle, "加入虚拟网络");
        MarkRow(PingIcon, RowState.Idle, "找到房主并建立端口转发");
    }

    // ══════════════════ 外壳 ══════════════════

    private void UpdateChrome()
    {
        var inStep2 = Step2Panel.Visibility == Visibility.Visible;
        var inStep3 = Step3Panel.Visibility == Visibility.Visible;

        if (!inStep3)
        {
            StepText.Text = inStep2 ? "第 2 步 / 共 2 步" : "第 1 步 / 共 2 步";
            StepNameText.Text = inStep2 ? "· 连接" : "· 粘贴房间码";
        }

        SecondaryButtonText = inStep2 ? "上一步" : null;
        CloseButtonText = inStep3 ? "关闭" : "取消";
        PrimaryButtonText = inStep3 ? "完成" : inStep2 ? "重新连接" : "开始连接";

        IsPrimaryButtonEnabled = !_busy && (inStep2 || _invite is not null);
        IsSecondaryButtonEnabled = !_busy;
    }

    private void SetBusy(bool busy, string? message)
    {
        _busy = busy;
        UpdateChrome();
        if (message is { Length: > 0 }) ShowStatus(InfoBarSeverity.Informational, message);
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = false;
        StatusBar.IsOpen = true;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_busy)
        {
            args.Cancel = true;
            return;
        }

        // 第 3 步：完成即关闭
        if (Step3Panel.Visibility == Visibility.Visible) return;

        // 第 2 步：重新连接
        if (Step2Panel.Visibility == Visibility.Visible)
        {
            args.Cancel = true;
            await ConnectAsync();
            return;
        }

        // 第 1 步：进入连接
        if (_invite is null)
        {
            args.Cancel = true;
            ShowStatus(InfoBarSeverity.Warning, "先粘贴对方发来的邀请码，或者直接填房间码");
            return;
        }

        args.Cancel = true;
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Visible;
        UpdateChrome();
        await ConnectAsync();
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (Step2Panel.Visibility != Visibility.Visible) return;

        args.Cancel = true;
        Step2Panel.Visibility = Visibility.Collapsed;
        Step1Panel.Visibility = Visibility.Visible;
        ResetProgress();
        UpdateChrome();
    }
}
