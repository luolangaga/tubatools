using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

/// <summary>
/// 加入向导：粘贴邀请码 → 必要时装客户端并以密钥入网 → 给出游戏内要填的地址。
/// 朋友端刻意不要求先登录自己的 Tailscale：邀请里带了密钥就一步到位。
/// </summary>
public sealed partial class GameTunnelJoinDialog : ContentDialog
{
    private InviteInfo? _invite;
    private bool _busy;
    private bool _connected;
    private bool _awaitingSwitchConfirm;
    private string? _myIp;

    /// <summary>关闭时是否已经连上对方的网络。</summary>
    public bool Connected => _connected;

    public GameTunnelJoinDialog(XamlRoot xamlRoot)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        RequestedTheme = ThemeService.CurrentElementTheme;
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
        PreviewTitle.Text = string.IsNullOrWhiteSpace(_invite.Game) ? "识别到联机地址" : $"识别到：{_invite.Game}";
        PreviewAddress.Text = _invite.Address;

        var notes = new List<string>();
        if (_invite.HasAuthKey)
        {
            // 邀请码路径：密钥自带预授权，直接加入
            notes.Add("带了短期密钥：粘贴后直接加入对方的网络，不用对方批准，也不用你登录 Tailscale");
        }
        else
        {
            // 纯地址路径：前提必须说在前面，不能让人以为填个地址就能连
            notes.Add("这是纯地址、没有密钥：只有你已经和对方在同一个 Tailscale 网络里才有效");
        }

        if (_invite.Protocol.UsesUdp()) notes.Add($"游戏使用 {_invite.Protocol.Describe()}，本工具同样支持");
        if (_invite.ExpiresAtLocal is { } expires) notes.Add($"密钥 {expires:HH:mm} 过期");
        PreviewNote.Text = string.Join(" · ", notes);

        if (!_invite.HasAuthKey)
        {
            ShowStatus(InfoBarSeverity.Warning, GameTunnelInviteCopy.AddressOnlyGuestNote);
        }

        if (!GameTunnelInvite.IsValidAddress(_invite.Host))
        {
            ShowStatus(InfoBarSeverity.Error, "地址看起来不对，请让对方重新发一次邀请");
        }

        UpdateChrome();
    }

    // ══════════════════ 第 2 步：连接 ══════════════════

    private async Task ConnectAsync()
    {
        if (_invite is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "先粘贴邀请码或联机地址");
            return;
        }

        SetBusy(true, "正在连接…");
        ResetProgress();
        try
        {
            // 1) 客户端
            TailscaleCli.Invalidate();
            if (!TailscaleService.IsInstalled)
            {
                if (!_invite.HasAuthKey)
                {
                    // 纯地址路径下，装好并登录自己的 Tailscale 是不够的——那是另一个网络，
                    // 对方的 100.x 地址在本机根本不存在。必须说清楚，别让用户白折腾。
                    MarkRow(JoinIcon, RowState.Failed, "1/3 这个邀请只有地址、没有密钥");
                    ShowStatus(InfoBarSeverity.Error, GameTunnelInviteCopy.AddressOnlyRemedy);
                    return;
                }

                MarkRow(JoinIcon, RowState.Running, "1/3 正在安装 Tailscale 客户端…");
                JoinProgress.Visibility = Visibility.Visible;
                JoinProgress.IsIndeterminate = true;

                var version = await TailscaleService.GetLatestVersionAsync() ?? "latest";
                var progress = new Progress<InstallerProgress>(report =>
                {
                    JoinProgress.IsIndeterminate = report.Percent <= 0;
                    if (report.Percent > 0) JoinProgress.Value = report.Percent;
                    if (report.Percent is > 0 and < 100) JoinText.Text = $"1/3 正在下载 Tailscale · {report.Percent:F0}%";
                });

                var msi = await TailscaleService.DownloadInstallerAsync(version, progress);
                JoinText.Text = "1/3 正在静默安装…";
                var install = await TailscaleService.InstallAsync(msi);
                if (!install.Ok)
                {
                    MarkRow(JoinIcon, RowState.Failed, $"1/3 安装失败：{install.Message}");
                    return;
                }

                if (!await TailscaleService.WaitForCliAsync(TimeSpan.FromSeconds(40)))
                {
                    MarkRow(JoinIcon, RowState.Failed, "1/3 安装完成但找不到 tailscale.exe，请重启后再试");
                    return;
                }
            }

            MarkRow(JoinIcon, RowState.Done, "1/3 客户端就绪");
            JoinProgress.Visibility = Visibility.Collapsed;

            // 2) 网络：能直连就不动用户的登录状态
            var status = await TailscaleService.GetStatusAsync();
            var alreadyReachable = false;
            TailscalePingResult? probe = null;

            if (status?.IsReady == true)
            {
                AddressText.Text = "2/3 正在测试能不能直接连到对方…";
                probe = TailscaleService.ParsePing(await TailscaleService.PingAsync(_invite.Host));
                alreadyReachable = probe.Ok;
            }

            if (!alreadyReachable && _invite.HasAuthKey)
            {
                var needSwitch = status?.IsLoggedIn == true;
                if (needSwitch && !_awaitingSwitchConfirm)
                {
                    ShowSwitchWarning(status);
                    SetBusy(false, null);
                    return;
                }

                _awaitingSwitchConfirm = false;
                SwitchWarningCard.Visibility = Visibility.Collapsed;
                AddressText.Text = "2/3 正在加入对方的网络…";

                var join = await TailscaleService.JoinTailnetAsync(_invite.AuthKey!);
                if (!join.Ok)
                {
                    MarkRow(AddressIcon, RowState.Failed, $"2/3 加入失败：{join.Message}");
                    ShowStatus(InfoBarSeverity.Error, join.Message);
                    return;
                }
            }
            else if (!alreadyReachable && !_invite.HasAuthKey)
            {
                if (status?.IsReady != true)
                {
                    MarkRow(AddressIcon, RowState.Failed, "2/3 本机的 Tailscale 还没就绪");
                    ShowStatus(InfoBarSeverity.Error, "请先点主页的「准备联机环境」完成安装与登录，或者让对方用「邀请码 / 一键加入脚本」的方式邀请你。");
                    return;
                }

                if (probe?.PeerNotInTailnet == true)
                {
                    // 铁证：这个地址不在本机 tailnet 里 —— 光有地址永远连不上，停下来说清楚
                    MarkRow(AddressIcon, RowState.Failed, "2/3 这个地址不在你的 Tailscale 网络里");
                    ShowStatus(InfoBarSeverity.Error, GameTunnelInviteCopy.AddressOnlyRemedy);
                    return;
                }

                // 在同一网络里但对方没响应：可能只是对方没开机，允许继续（后面还会再测一次）
                MarkRow(AddressIcon, RowState.Warning, "2/3 你和对方在同一个网络，但他暂时没有响应");
            }

            // 3) 等地址
            var ready = await TailscaleService.WaitForReadyAsync(TimeSpan.FromSeconds(40));
            _myIp = ready?.Ipv4;
            if (string.IsNullOrWhiteSpace(_myIp))
            {
                MarkRow(AddressIcon, RowState.Failed, "2/3 没有拿到联机地址");
                ShowStatus(InfoBarSeverity.Error, "没能拿到本机联机地址，请到主页的「网络检测」看看原因");
                return;
            }
            MarkRow(AddressIcon, RowState.Done, $"2/3 本机联机地址 {_myIp}");

            // 4) 连通性
            var ping = TailscaleService.ParsePing(await TailscaleService.PingAsync(_invite.Host));
            if (ping.Ok)
            {
                MarkRow(PingIcon, RowState.Done, $"3/3 与对方连通 · {ping.Summary}");
            }
            else
            {
                MarkRow(PingIcon, RowState.Warning, $"3/3 暂时没连上对方：{ping.Summary}");
            }

            EnterStep3(ping);

            _connected = true;
        }
        catch (OperationCanceledException)
        {
            ShowStatus(InfoBarSeverity.Error, "操作已取消");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"连接失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void ShowSwitchWarning(TailscaleStatus status)
    {
        _awaitingSwitchConfirm = true;
        SwitchWarningCard.Visibility = Visibility.Visible;
        SwitchWarningText.Text = status.LoginName is { Length: > 0 } login
            ? $"这台电脑当前登录的是 {login}。继续会把 Tailscale 切换成对方的网络，切换后你自己的设备会暂时离线；玩完可以用托盘图标的账号菜单切回来。"
            : "继续会把 Tailscale 切换成对方的网络，玩完可以随时切回来。";
        ShowStatus(InfoBarSeverity.Informational, "需要你确认一下，再点一次下面的按钮即可继续");
    }

    private async void ConfirmSwitch_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private void SkipSwitch_Click(object sender, RoutedEventArgs e)
    {
        SwitchWarningCard.Visibility = Visibility.Collapsed;
        _awaitingSwitchConfirm = false;
        ShowStatus(InfoBarSeverity.Warning, "已跳过切换。你可以在网络检测里确认自己是不是已经在对方的网络里。");
    }

    private void EnterStep3(TailscalePingResult ping)
    {
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Collapsed;
        Step3Panel.Visibility = Visibility.Visible;

        FinalAddressText.Text = _invite!.Address;
        if (ping.Ok)
        {
            FinalNoteText.Text = _invite.HasAuthKey
                ? "你已经加入对方的网络，连接正常。剩下的就是在游戏里填上面的地址。"
                : "你和对方本来就在同一个网络里，连接正常。剩下的就是在游戏里填上面的地址。";
        }
        else if (ping.PeerNotInTailnet && !_invite.HasAuthKey)
        {
            FinalNoteText.Text = GameTunnelInviteCopy.AddressOnlyRemedy;
        }
        else
        {
            FinalNoteText.Text = "与对方的连接还没建立（可能是对方刚进游戏）。先在游戏里试一次，不行再回主页做网络检测。";
        }

        var preset = GameTunnelCatalog.Presets.FirstOrDefault(p => p.Name == _invite.Game);
        GuestStepsList.ItemsSource = preset?.GuestSteps
            .Select(step => step.Replace("{host}", _invite.Host).Replace("{port}", _invite.Port.ToString()))
            .ToList()
            ?? new List<string>
            {
                $"打开游戏，在联机 / 多人游戏界面里填 {_invite.Address}",
                "首次连接可能需要多试一次（网络还在打洞）",
                "玩完想恢复正常网络：右键任务栏 Tailscale 图标选择 Disconnect"
            };

        StepNameText.Text = "· 连上了";
        StepText.Text = "完成";
    }

    private void CopyFinalAddress_Click(object sender, RoutedEventArgs e)
    {
        if (_invite is null) return;
        // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
        if (ClipboardService.TrySetText(_invite.Address).Success)
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
            RowState.Done => ThemeBrush("SystemFillColorSuccessBrush"),
            RowState.Warning => ThemeBrush("SystemFillColorCautionBrush"),
            RowState.Failed => ThemeBrush("SystemFillColorCriticalBrush"),
            _ => ThemeBrush("TextFillColorSecondaryBrush")
        };

        var target = icon == JoinIcon ? JoinText : icon == AddressIcon ? AddressText : PingText;
        target.Text = text;
    }

    private static Microsoft.UI.Xaml.Media.Brush ThemeBrush(string key)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var value) && value is Microsoft.UI.Xaml.Media.Brush brush)
                return brush;
        }
        catch
        {
        }
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void ResetProgress()
    {
        JoinProgress.Value = 0;
        JoinProgress.Visibility = Visibility.Collapsed;
        SwitchWarningCard.Visibility = Visibility.Collapsed;
        MarkRow(JoinIcon, RowState.Idle, "等待开始");
        MarkRow(AddressIcon, RowState.Idle, "本机联机地址");
        MarkRow(PingIcon, RowState.Idle, "与对方的连接");
    }

    // ══════════════════ 外壳 ══════════════════

    private void UpdateChrome()
    {
        var inStep2 = Step2Panel.Visibility == Visibility.Visible;
        var inStep3 = Step3Panel.Visibility == Visibility.Visible;

        if (!inStep3)
        {
            StepText.Text = inStep2 ? "第 2 步 / 共 2 步" : "第 1 步 / 共 2 步";
            StepNameText.Text = inStep2 ? "· 连接" : "· 粘贴邀请";
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
            ShowStatus(InfoBarSeverity.Warning, "先粘贴对方发来的邀请码，或者直接填地址");
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
        if (Step2Panel.Visibility != Visibility.Visible)
        {
            return;
        }

        args.Cancel = true;
        Step2Panel.Visibility = Visibility.Collapsed;
        Step1Panel.Visibility = Visibility.Visible;
        ResetProgress();
        UpdateChrome();
    }
}
