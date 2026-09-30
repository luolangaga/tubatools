using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 主机向导：选游戏 → 确认端口 → 拿到地址和邀请方式。
/// 环境没准备好时由调用方先跑 <see cref="GameTunnelSetupDialog"/>，这里不再嵌套对话框。
/// </summary>
public sealed partial class GameTunnelHostDialog : ContentDialog
{
    private const int TotalSteps = 3;

    private int _step = 1;
    private TunnelGameCard? _selected;
    private InviteInfo? _invite;
    private bool _busy;
    private bool _inviteReady;
    private int _editingCustomGameIndex = -1;

    /// <summary>主页点某张游戏卡进来时预选的游戏；null = 沿用上次选的那个。</summary>
    private readonly string? _initialPresetId;

    /// <summary>主页点「查看当前邀请」进来：直接落到第 3 步，用同一份联机信息重建邀请面板。</summary>
    private readonly bool _resumeInvite;

    /// <summary>关闭时是否已经拿到可用的邀请（主页据此显示「联机中」）。</summary>
    public bool InviteReady => _inviteReady;

    /// <summary>本次联机信息，供主页复用（再次邀请朋友时用）。</summary>
    public InviteInfo? Invite => _invite;

    public GameTunnelHostDialog(XamlRoot xamlRoot, string? presetId = null, bool resumeInvite = false)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        RequestedTheme = ThemeService.CurrentElementTheme;
        _initialPresetId = presetId;
        _resumeInvite = resumeInvite;

        // 邀请码没生成出来时必须说清楚「朋友现在还用不了」，
        // 否则用户会以为地址给出去了就能连（之前就是这样误导人的）
        InvitePanel.InviteStateChanged += (_, ready) => UpdateInviteStatus(ready);
    }

    private void UpdateInviteStatus(bool inviteReady)
    {
        RoomStatusText.Text = inviteReady
            ? "邀请码已生成 ✓ 把下面任意一种方式发给朋友，他加入后在游戏里填上面的地址即可。你保持游戏开着就行。"
            : "网络已就绪，但还差一把邀请密钥——朋友现在连不进来。用下面任意一种方式生成邀请码即可（配一次 API 密钥以后就自动了）。";
    }

    public async Task<bool> RunAsync()
    {
        LoadGames();

        // 「查看当前邀请」：先用现成的信息把第 3 步画出来（弹窗立刻可见），
        // 读状态、放行防火墙、生成密钥这些慢动作放到后面异步补，不让用户干等。
        if (_resumeInvite && GameTunnelCatalog.CurrentInvite is { } invite)
        {
            PortBox.Value = invite.Port;
            PrefillStep3(invite);
            _ = ResumeInviteAsync();
        }

        await ShowAsync();
        return _inviteReady;
    }

    /// <summary>恢复时后台补齐防火墙与邀请密钥；失败就地提示，不打断已经显示出来的页面。</summary>
    private async Task ResumeInviteAsync()
    {
        try
        {
            await StartInviteAsync();
        }
        catch (Exception ex)
        {
            StatusText($"准备邀请信息失败：{ex.Message}");
        }
    }

    /// <summary>用已有的联机信息直接画出第 3 步，不用再等一次状态查询。</summary>
    private void PrefillStep3(InviteInfo invite)
    {
        RoomAddressText.Text = invite.Address;
        UpdateInviteStatus(invite.HasAuthKey);
        UpdateGuestSteps(invite.Host);
        _step = 3;
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Collapsed;
        Step3Panel.Visibility = Visibility.Visible;
        UpdateStepChrome();
    }

    // ══════════════════ 第 1 步：游戏列表 ══════════════════

    private void LoadGames()
    {
        var cards = GameTunnelCatalog.Presets.Select(TunnelGameCard.FromPreset).ToList();
        cards.AddRange(GameTunnelCatalog.LoadCustomGames().Select(TunnelGameCard.FromCustom));
        cards.Add(TunnelGameCard.AddCard());

        GameGrid.ItemsSource = cards;
        _ = LoadLogosAsync(cards);

        // 主页点进来的游戏优先，其次「查看当前邀请」时选回当初那个游戏，最后恢复上次选的
        var index = -1;
        if (_initialPresetId is { Length: > 0 } initialId)
            index = cards.FindIndex(c => c.Id == initialId);

        if (index < 0 && _resumeInvite && GameTunnelCatalog.CurrentInvite?.Game is { Length: > 0 } gameName)
            index = cards.FindIndex(c => c.Name == gameName);

        if (index < 0)
        {
            var settings = GameTunnelCatalog.LoadSettings();
            index = 0;
            if (settings.LastPresetId is { Length: > 0 } lastId)
            {
                var found = cards.FindIndex(c => c.Id == lastId);
                if (found >= 0) index = found;
            }
        }
        GameGrid.SelectedIndex = index;

        UpdateStepChrome();
    }

    /// <summary>真实 Logo 异步补齐：先显示图标字形，图片到位后每张卡片各自就地换掉。</summary>
    private static async Task LoadLogosAsync(IEnumerable<TunnelGameCard> cards)
    {
        try
        {
            await Task.WhenAll(cards.Select(card => card.LoadLogoAsync()));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameLogo] 加载失败：{ex.Message}");
        }
    }

    private void GameGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = GameGrid.SelectedItem as TunnelGameCard;
        if (_selected is null) return;

        if (_selected.IsAddCard)
        {
            GameHintCard.Visibility = Visibility.Collapsed;
            ShowCustomGameForm(null);
        }
        else
        {
            CustomGameForm.Visibility = Visibility.Collapsed;
            ShowGameHint();
        }

        UpdateStepChrome();
    }

    private void ShowGameHint()
    {
        if (_selected is null) return;

        GameHintCard.Visibility = Visibility.Visible;
        GameHintTitle.Text = $"{_selected.Name} · {_selected.ProtocolText} {_selected.Port}";
        GameHintAction.Text = _selected.Preset?.HostAction ?? "在游戏里建房或启动服务器，然后确认端口。";
        GameHintNote.Text = _selected.Preset?.Note ?? _selected.Custom?.Note ?? "";
        GameHintNote.Visibility = string.IsNullOrWhiteSpace(GameHintNote.Text) ? Visibility.Collapsed : Visibility.Visible;

        // 自定义游戏提供编辑与删除
        CustomGameActions.Visibility = _selected.Custom is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowCustomGameForm(CustomGame? editing)
    {
        _editingCustomGameIndex = editing is null
            ? -1
            : GameTunnelCatalog.LoadCustomGames().FindIndex(g => g.Id == editing.Id);

        CustomGameFormTitle.Text = editing is null ? "新建自定义游戏" : "编辑自定义游戏";
        CustomNameBox.Text = editing?.Name ?? "";
        CustomPortBox.Text = editing?.Port > 0 ? editing.Port.ToString() : "";
        CustomProtocolBox.SelectedIndex = editing?.Protocol switch
        {
            GameTunnelProtocol.Udp => 1,
            GameTunnelProtocol.TcpAndUdp => 2,
            _ => 0
        };
        CustomGameError.Visibility = Visibility.Collapsed;
        CustomGameForm.Visibility = Visibility.Visible;
    }

    private void SaveCustomGame_Click(object sender, RoutedEventArgs e)
    {
        var name = CustomNameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowCustomGameError("给游戏起个名字吧");
            return;
        }

        if (!int.TryParse(CustomPortBox.Text.Trim(), out var port) || !GameTunnelCatalog.IsValidPort(port))
        {
            ShowCustomGameError("端口需要是 1-65535 之间的数字");
            return;
        }

        var protocol = CustomProtocolBox.SelectedIndex switch
        {
            1 => GameTunnelProtocol.Udp,
            2 => GameTunnelProtocol.TcpAndUdp,
            _ => GameTunnelProtocol.Tcp
        };

        var games = GameTunnelCatalog.LoadCustomGames();
        var existing = _editingCustomGameIndex >= 0 && _editingCustomGameIndex < games.Count ? games[_editingCustomGameIndex] : null;

        CustomGame game;
        if (existing is not null)
        {
            existing.Name = name;
            existing.Port = port;
            existing.Protocol = protocol;
            game = existing;
        }
        else
        {
            game = new CustomGame
            {
                Id = GameTunnelCatalog.NewCustomGameId(),
                Name = name,
                Port = port,
                Protocol = protocol
            };
            games.Add(game);
        }

        GameTunnelCatalog.SaveCustomGames(games);
        CustomGameForm.Visibility = Visibility.Collapsed;

        // 重新加载列表并选中刚保存的游戏（放在「自定义游戏」入口之前）
        var cards = GameTunnelCatalog.Presets.Select(TunnelGameCard.FromPreset).ToList();
        cards.AddRange(games.Select(TunnelGameCard.FromCustom));
        cards.Add(TunnelGameCard.AddCard());
        GameGrid.ItemsSource = cards;
        _ = LoadLogosAsync(cards);
        GameGrid.SelectedIndex = cards.FindIndex(c => c.Id == game.Id);
    }

    private void CancelCustomGame_Click(object sender, RoutedEventArgs e)
    {
        CustomGameForm.Visibility = Visibility.Collapsed;
        if (_selected?.IsAddCard != true) ShowGameHint();
    }

    private void EditCustomGame_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Custom is not { } custom) return;
        GameHintCard.Visibility = Visibility.Collapsed;
        ShowCustomGameForm(custom);
    }

    private void DeleteCustomGame_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Custom is not { } custom) return;

        var games = GameTunnelCatalog.LoadCustomGames();
        games.RemoveAll(g => g.Id == custom.Id);
        GameTunnelCatalog.SaveCustomGames(games);

        GameGrid.SelectedIndex = 0;
        LoadGames();
    }

    private void ShowCustomGameError(string message)
    {
        CustomGameError.Text = message;
        CustomGameError.Visibility = Visibility.Visible;
    }

    // ══════════════════ 第 2 步：端口与游戏内准备 ══════════════════

    private void EnterStep2()
    {
        if (_selected is null || _selected.IsAddCard) return;

        _step = 2;
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Visible;
        Step3Panel.Visibility = Visibility.Collapsed;

        Step2GameName.Text = _selected.Name;
        Step2GameMeta.Text = $"{_selected.ProtocolText} · 默认端口 {_selected.Port}";

        var settings = GameTunnelCatalog.LoadSettings();
        var port = _selected.Port > 0 ? _selected.Port : settings.LastPort > 0 ? settings.LastPort : 25565;
        PortBox.Value = port;

        var preset = _selected.Preset;
        HostStepsList.ItemsSource = preset?.HostSteps ?? ["在游戏里建房或启动服务器", $"确认端口是 {port}", "把这个窗口里的地址发给朋友"];
        UpdateGuestSteps();
        GuestEntryText.Text = preset is not null ? $"游戏内的位置：{preset.GuestEntryPoint}" : "";

        CheckPort();
        UpdateStepChrome();
    }

    private void PortBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_step != 2) return;
        CheckPort();
        UpdateGuestSteps();
    }

    private void UpdateGuestSteps(string host = "对方地址")
    {
        var port = _selected?.Port ?? 0;
        if (PortBox.Value is double value && !double.IsNaN(value)) port = (int)value;

        var preset = _selected?.Preset;
        if (preset is null)
        {
            GuestStepsList.ItemsSource = new List<string>
            {
                "运行一键加入脚本，或者在工具箱里粘贴邀请码（直接加入，双方都不需要点同意）",
                $"打开游戏，在联机界面里填 {host}:{port}",
                "只发地址的话，对方必须已经在你这个 Tailscale 网络里，否则填了也连不上",
                "首次连接可能需要多试一次（网络还在打洞）"
            };
            return;
        }

        // 预设步骤是「游戏里怎么操作」，最后补一句加入方式的前提，避免朋友拿着地址干等
        var steps = preset.GuestSteps
            .Select(step => step.Replace("{host}", host).Replace("{port}", port.ToString()))
            .ToList();
        steps.Add("邀请码 / 一键加入脚本会把他直接加进你的网络；只发地址的前提是他已经在这个网络里");
        GuestStepsList.ItemsSource = steps;
    }

    private void RecheckPort_Click(object sender, RoutedEventArgs e) => CheckPort();

    private void CheckPort()
    {
        var port = (int)(PortBox.Value is double value && !double.IsNaN(value) ? value : 0);
        if (!GameTunnelCatalog.IsValidPort(port))
        {
            PortCheckIcon.Glyph = "\uE783";
            PortCheckText.Text = "请填一个 1-65535 之间的端口";
            return;
        }

        var protocol = _selected?.Protocol ?? GameTunnelProtocol.Tcp;
        var status = GameTunnelProbe.Check(port, protocol);
        PortCheckIcon.Glyph = status.Listening ? "\uE73E" : "\uE7BA";
        PortCheckText.Text = status.Message;
    }

    // ══════════════════ 第 3 步：邀请朋友 ══════════════════

    private async Task StartInviteAsync()
    {
        if (_selected is null) return;

        var port = (int)(PortBox.Value is double value && !double.IsNaN(value) ? value : 0);
        if (!GameTunnelCatalog.IsValidPort(port))
        {
            ShowStatus(InfoBarSeverity.Error, "端口不合法，请回到上一步修改");
            return;
        }

        SetBusy(true, "正在准备邀请…");
        try
        {
            var status = await TailscaleService.GetStatusAsync();
            if (status?.IsReady != true)
            {
                ShowStatus(InfoBarSeverity.Error, "联机环境还没准备好，请先点主页的「准备联机环境」完成安装与登录");
                return;
            }

            var ip = status.Ipv4;
            if (string.IsNullOrWhiteSpace(ip))
            {
                ShowStatus(InfoBarSeverity.Error, "没有拿到联机地址，请到主页的「网络检测」看看是什么问题");
                return;
            }

            var protocol = _selected.Protocol;
            var game = _selected.Name;

            _invite = new InviteInfo
            {
                Host = ip!,
                Port = port,
                Game = game,
                Protocol = protocol,
                HostName = status.HostName
            };

            // 主页「查看当前邀请信息」靠它回到这一步（本次运行内有效）
            GameTunnelCatalog.CurrentInvite = _invite;

            // 防火墙只放行 Tailscale 网卡，不影响局域网与公网暴露面
            var settings = GameTunnelCatalog.LoadSettings();
            if (settings.AutoFirewall)
            {
                StatusText("正在放行防火墙（仅 Tailscale 网络）…");
                var firewall = await TailscaleService.EnsureFirewallAsync(port, protocol);
                FirewallText.Text = firewall.Ok
                    ? $"防火墙：{firewall.Message}"
                    : $"防火墙：{firewall.Message}（如果朋友连不上，可手动在 Windows 防火墙里放行 {port}）";
            }
            else
            {
                FirewallText.Text = "防火墙：已跳过（可在设置里打开自动放行）";
            }

            RoomAddressText.Text = $"{ip}:{port}";
            UpdateInviteStatus(_invite.AuthKey is { Length: > 0 });
            UpdateGuestSteps(ip!);

            _step = 3;
            Step1Panel.Visibility = Visibility.Collapsed;
            Step2Panel.Visibility = Visibility.Collapsed;
            Step3Panel.Visibility = Visibility.Visible;
            UpdateStepChrome();

            await InvitePanel.InitializeAsync(_invite);

            settings.LastPresetId = _selected.Id;
            settings.LastPort = port;
            GameTunnelCatalog.SaveSettings(settings);

            _inviteReady = true;
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void CopyRoomAddress_Click(object sender, RoutedEventArgs e)
    {
        if (_invite is null) return;
        // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
        if (ClipboardService.TrySetText(_invite.Address).Success)
            ShowStatus(InfoBarSeverity.Success, "地址已复制");
        else
            ShowStatus(InfoBarSeverity.Error, "复制失败，请手动选中文本复制");
    }

    // ══════════════════ 步骤外壳 ══════════════════

    private void UpdateStepChrome()
    {
        StepText.Text = $"第 {_step} 步 / 共 {TotalSteps} 步";
        StepNameText.Text = _step switch
        {
            1 => "· 选择游戏",
            2 => "· 确认端口",
            _ => "· 邀请朋友"
        };

        SecondaryButtonText = _step > 1 && _step < 3 ? "上一步" : null;
        CloseButtonText = _step == 3 ? null : "取消";
        PrimaryButtonText = _step switch
        {
            1 => "下一步",
            2 => "开始联机",
            _ => "完成"
        };

        // 第 1 步没选到有效游戏时不让走
        var canContinue = _step != 1 || (_selected is { IsAddCard: false });
        IsPrimaryButtonEnabled = !_busy && canContinue;
        IsSecondaryButtonEnabled = !_busy;
    }

    private void SetBusy(bool busy, string? message)
    {
        _busy = busy;
        UpdateStepChrome();
        if (message is { Length: > 0 }) StatusText(message);
    }

    private void StatusText(string message) => ShowStatus(InfoBarSeverity.Informational, message);

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

        try
        {
            switch (_step)
            {
                case 1:
                {
                    var card = GameGrid.SelectedItem as TunnelGameCard;
                    if (card is null || card.IsAddCard)
                    {
                        args.Cancel = true;
                        ShowStatus(InfoBarSeverity.Warning, "先选一个游戏，或者用「自定义游戏」自己填一个");
                        return;
                    }
                    _selected = card;
                    args.Cancel = true;
                    EnterStep2();
                    break;
                }

                case 2:
                    args.Cancel = true;
                    await StartInviteAsync();
                    break;

                default:
                    // 第 3 步点「完成」直接关闭
                    break;
            }
        }
        catch (Exception ex)
        {
            // async void 事件处理器不能让异常逃逸，否则会直接崩掉应用
            args.Cancel = true;
            ShowStatus(InfoBarSeverity.Error, $"操作失败：{ex.Message}");
        }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_step != 2) return;
        args.Cancel = true;
        _step = 1;
        Step1Panel.Visibility = Visibility.Visible;
        Step2Panel.Visibility = Visibility.Collapsed;
        Step3Panel.Visibility = Visibility.Collapsed;
        UpdateStepChrome();
    }
}
