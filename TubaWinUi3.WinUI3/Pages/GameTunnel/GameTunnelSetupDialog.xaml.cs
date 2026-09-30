using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 环境准备向导：装客户端 → 登录 → 拿到联机地址。
/// 三个步骤在一张卡片列表上实时变绿，用户只需要一直点主按钮。
///
/// 特别注意「后端卡在启动」这一态：浏览器里授权成功后，如果本机连不上控制服务器
/// （代理/加速器把 controlplane.tailscale.com 变成 fake-IP 是常见原因），
/// 状态会长期停在 NoState。此时绝不能显示「未登录」——那会把用户推进
/// 「反复点登录但永远没反应」的死循环。
/// </summary>
public sealed partial class GameTunnelSetupDialog : ContentDialog
{
    private enum Stage
    {
        NeedInstall,
        NeedLogin,
        NeedConnect,
        Starting,
        Ready
    }

    private Stage _stage = Stage.NeedInstall;
    private bool _busy;
    private string? _authUrl;
    private TailscaleStatus? _status;

    private DispatcherQueueTimer? _waitTimer;
    private DateTimeOffset _stageEnteredAt = DateTimeOffset.UtcNow;
    private string? _startupHint;
    private bool _hintTrayMissing;
    private bool _refreshing;

    /// <summary>关闭对话框时环境是否已经可用。</summary>
    public bool IsEnvironmentReady => _stage == Stage.Ready;

    public GameTunnelSetupDialog(XamlRoot xamlRoot)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        RequestedTheme = ThemeService.CurrentElementTheme;
        Closed += (_, _) => StopWaitingPoll();
    }

    /// <summary>先探测一次状态，再展示对话框；返回环境是否就绪。</summary>
    public async Task<bool> RunAsync()
    {
        await RefreshAsync();
        await ShowAsync();
        return IsEnvironmentReady;
    }

    // ══════════════════ 状态探测 ══════════════════

    private async Task RefreshAsync(bool silent = false)
    {
        if (_refreshing) return;
        _refreshing = true;

        if (!silent) SetBusy(true, null);
        try
        {
            TailscaleCli.Invalidate();
            _status = TailscaleService.IsInstalled ? await TailscaleService.GetStatusAsync() : null;
        }
        catch
        {
            _status = null;
        }
        finally
        {
            if (!silent) SetBusy(false, null);
            _refreshing = false;
        }

        Render();
    }

    private void Render()
    {
        var installed = TailscaleService.IsInstalled;

        // ── 步骤 1：客户端 ──
        if (installed)
        {
            MarkDone(Step1Badge, Step1Icon);
            Step1Status.Text = "已安装";
            _ = ShowVersionAsync();
        }
        else
        {
            MarkPending(Step1Badge, Step1Icon);
            Step1Status.Text = "未安装 · 首次使用需要下载约 38MB 的官方安装包";
        }

        // ── 步骤 2：登录 ──
        var waitingForBackend = false;
        if (!installed)
        {
            MarkIdle(Step2Badge, Step2Icon);
            Step2Status.Text = "等待客户端安装完成";
            AuthUrlPanel.Visibility = Visibility.Collapsed;
        }
        else if (_status?.IsReady == true || _status?.IsLoggedIn == true)
        {
            MarkDone(Step2Badge, Step2Icon);
            Step2Status.Text = _status.LoginName is { Length: > 0 } login ? $"已登录 · {login}" : "已登录";
            AuthUrlPanel.Visibility = Visibility.Collapsed;
        }
        else if (_status?.IsStarting == true)
        {
            // NoState / Starting：后端还没起来，**不是未登录**
            waitingForBackend = true;
            MarkPending(Step2Badge, Step2Icon);
            Step2Status.Text = _status.HaveNodeKey
                ? "本机已经授权过，正在等 Tailscale 后端就绪"
                : "Tailscale 后端正在启动，浏览器里授权成功后会自动继续";
        }
        else if (_status is null)
        {
            waitingForBackend = true;
            MarkPending(Step2Badge, Step2Icon);
            Step2Status.Text = "正在读取 Tailscale 状态…";
        }
        else
        {
            MarkPending(Step2Badge, Step2Icon);
            Step2Status.Text = "未登录 · 点下面的按钮，在浏览器里点一下授权就行";
        }

        // ── 步骤 3：地址 ──
        if (_status?.IsReady == true)
        {
            MarkDone(Step3Badge, Step3Icon);
            Step3Status.Text = "网络已连通，这就是你的联机地址";
            IpText.Text = _status.Ipv4;
            IpText.Visibility = Visibility.Visible;
        }
        else if (_status?.IsLoggedIn == true)
        {
            MarkPending(Step3Badge, Step3Icon);
            Step3Status.Text = "已登录但当前没有连接，点下面的按钮连接一下";
            IpText.Visibility = Visibility.Collapsed;
        }
        else if (waitingForBackend)
        {
            MarkPending(Step3Badge, Step3Icon);
            Step3Status.Text = "后端就绪后会自动获取地址";
            IpText.Visibility = Visibility.Collapsed;
        }
        else
        {
            MarkIdle(Step3Badge, Step3Icon);
            Step3Status.Text = "登录后自动获取";
            IpText.Visibility = Visibility.Collapsed;
        }

        // ── 阶段与按钮 ──
        var next = !installed ? Stage.NeedInstall
            : _status?.IsReady == true ? Stage.Ready
            : waitingForBackend ? Stage.Starting
            : _status?.IsLoggedIn == true ? Stage.NeedConnect
            : Stage.NeedLogin;

        if (next != _stage)
        {
            _stage = next;
            _stageEnteredAt = DateTimeOffset.UtcNow;
            _startupHint = null;
            _trayAutoStartTried = false;
            _trayAutoStartFailed = false;
            StuckHintText.Visibility = Visibility.Collapsed;
        }

        WaitingPanel.Visibility = waitingForBackend ? Visibility.Visible : Visibility.Collapsed;
        var trayMissing = waitingForBackend && TailscaleService.IsTrayRunning == false;
        if (waitingForBackend)
        {
            if (trayMissing)
            {
                // 托盘是这功能的必要条件，自动拉起它——不要求用户手动去点
                WaitingText.Text = _trayAutoStartFailed
                    ? "没能自动启动 Tailscale 客户端，请手动打开：开始菜单搜索 Tailscale"
                    : "正在自动启动 Tailscale 客户端…（它必须常驻后台）";

                if (!_trayAutoStartTried) TryAutoStartTray();
            }
            else
            {
                var seconds = (int)(DateTimeOffset.UtcNow - _stageEnteredAt).TotalSeconds;
                WaitingText.Text = $"正在等待 Tailscale 后端就绪 · 已等待 {seconds} 秒";
            }

            _ = ShowStartupHintAsync(trayMissing);
            StartWaitingPoll();
        }
        else
        {
            StopWaitingPoll();
        }

        PrimaryButtonText = _stage switch
        {
            Stage.NeedInstall => "下载并安装",
            Stage.NeedLogin => "打开浏览器登录",
            Stage.NeedConnect => "连接",
            Stage.Starting => trayMissing ? "重试启动客户端" : "重新检测",
            _ => "完成"
        };
        SecondaryButtonText = _stage == Stage.Starting ? "重启 Tailscale 服务" : null;
        IsPrimaryButtonEnabled = !_busy;
        IsSecondaryButtonEnabled = !_busy;
    }

    private async Task ShowVersionAsync()
    {
        var version = await TailscaleService.GetLocalVersionAsync();
        if (version is { Length: > 0 } && Step1Status.Text == "已安装")
        {
            Step1Status.Text = $"已安装 · 版本 {version}";
        }
    }

    /// <summary>卡在启动时给出原因（托盘没跑优先说这个，其次才是网络/代理）。</summary>
    private async Task ShowStartupHintAsync(bool trayMissing)
    {
        if (_startupHint is null || _hintTrayMissing != trayMissing)
        {
            _hintTrayMissing = trayMissing;
            _startupHint = trayMissing
                ? "Tailscale 的托盘客户端（任务栏右下角图标）没有在运行。它必须常驻：只有命令行临时连上时，" +
                  "守护进程刚建立的连接会在命令退出的瞬间被断开——这正是「浏览器里授权成功、状态却反复退回未登录」的原因。" +
                  "本向导会自动把它拉起来；如果一直失败，点下面的按钮再试一次。"
                : TailscaleService.DescribeStartupHint(
                    TailscaleService.GetSystemProxyDescription(),
                    await TailscaleService.GetMachineProxyAsync());
        }

        StuckHintText.Text = _startupHint;
        StuckHintText.Visibility = Visibility.Visible;
    }

    // ══════════════════ 自动拉起托盘客户端 ══════════════════

    private bool _trayAutoStartTried;
    private bool _trayAutoStartFailed;

    private void TryAutoStartTray()
    {
        _trayAutoStartTried = true;
        _ = AutoStartTrayAsync();
    }

    private async Task AutoStartTrayAsync()
    {
        try
        {
            var ok = await TailscaleService.EnsureTrayRunningAsync(TimeSpan.FromSeconds(15));
            _trayAutoStartFailed = !ok;

            if (ok)
            {
                // 客户端起来了，后端通常几秒内就能连上
                await TailscaleService.WaitForReadyAsync(TimeSpan.FromSeconds(15));
            }
            else
            {
                ShowError("没能自动启动 Tailscale 客户端，请手动打开：开始菜单里搜索 Tailscale");
            }
        }
        catch
        {
            _trayAutoStartFailed = true;
        }
        finally
        {
            await RefreshAsync(silent: true);
        }
    }

    // ══════════════════ 卡住时的自动轮询 ══════════════════

    private void StartWaitingPoll()
    {
        _waitTimer ??= DispatcherQueue.CreateTimer();
        _waitTimer.Interval = TimeSpan.FromSeconds(2.5);
        _waitTimer.Tick -= WaitTimer_Tick;
        _waitTimer.Tick += WaitTimer_Tick;
        if (!_waitTimer.IsRunning) _waitTimer.Start();
    }

    private void StopWaitingPoll() => _waitTimer?.Stop();

    private async void WaitTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_stage != Stage.Starting || _busy) return;
        await RefreshAsync(silent: true);
    }

    private static void MarkDone(Border badge, FontIcon icon)
    {
        badge.Background = ThemeBrush("SystemFillColorSuccessBackgroundBrush");
        icon.Foreground = ThemeBrush("SystemFillColorSuccessBrush");
        icon.Glyph = "\uE73E";
    }

    private static void MarkPending(Border badge, FontIcon icon)
    {
        badge.Background = ThemeBrush("SystemFillColorCautionBackgroundBrush");
        icon.Foreground = ThemeBrush("SystemFillColorCautionBrush");
        icon.Glyph = "\uE9F5";
    }

    private static void MarkIdle(Border badge, FontIcon icon)
    {
        badge.Background = ThemeBrush("SubtleFillColorSecondaryBrush");
        icon.Foreground = ThemeBrush("TextFillColorSecondaryBrush");
        icon.Glyph = "\uE9F5";
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

    private void SetBusy(bool busy, string? message)
    {
        _busy = busy;
        IsPrimaryButtonEnabled = !busy;
        IsSecondaryButtonEnabled = !busy;
        // 关闭按钮始终可用：下载/登录都可能等很久，不能把用户困在向导里
        CloseButtonText = busy ? "关闭" : "稍后再说";
        if (message is { Length: > 0 })
        {
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Message = message;
            StatusBar.IsOpen = true;
        }
    }

    // ══════════════════ 主按钮：按阶段推进 ══════════════════

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_busy)
        {
            args.Cancel = true;
            return;
        }

        switch (_stage)
        {
            case Stage.NeedInstall:
                args.Cancel = true;
                await InstallAsync();
                break;

            case Stage.NeedLogin:
                args.Cancel = true;
                await LoginAsync();
                break;

            case Stage.NeedConnect:
                args.Cancel = true;
                await ConnectAsync();
                break;

            case Stage.Starting:
                // 手动再查一次（自动轮询也在跑）；托盘没起来先把它拉起来
                args.Cancel = true;
                _stageEnteredAt = DateTimeOffset.UtcNow;

                if (!TailscaleService.IsTrayRunning)
                {
                    SetBusy(true, "正在启动 Tailscale 客户端…");
                    try
                    {
                        if (await TailscaleService.EnsureTrayRunningAsync())
                        {
                            StatusBar.Severity = InfoBarSeverity.Success;
                            StatusBar.Message = "客户端已启动，正在等待连接…";
                            StatusBar.IsOpen = true;
                            await TailscaleService.WaitForReadyAsync(TimeSpan.FromSeconds(20));
                        }
                        else
                        {
                            ShowError("没能启动 Tailscale 客户端，请手动打开开始菜单里的 Tailscale");
                        }
                    }
                    finally
                    {
                        SetBusy(false, null);
                    }
                }

                await RefreshAsync();
                break;
        }
    }

    private async void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;

        var confirm = new ContentDialog
        {
            Title = "重启 Tailscale 服务？",
            Content = new TextBlock
            {
                Text = "会短暂断开本机已有的 Tailscale 连接（当前没有联机的话没有影响），用来把卡住的客户端后端重新拉起来。",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "重启",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true, "正在重启 Tailscale 服务…");
        try
        {
            var result = await TailscaleService.RestartServiceAsync();
            if (!result.Ok)
            {
                ShowError(result.Message);
                return;
            }

            // 等后端重新连上控制服务器
            await TailscaleService.WaitForReadyAsync(TimeSpan.FromSeconds(25));
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Message = "服务已重启";
            StatusBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ShowError($"重启服务失败：{ex.Message}");
        }
        finally
        {
            _stageEnteredAt = DateTimeOffset.UtcNow;
            SetBusy(false, null);
            await RefreshAsync();
        }
    }

    private async Task InstallAsync()
    {
        Step1Progress.Visibility = Visibility.Visible;
        Step1Progress.IsIndeterminate = true;
        Step1ProgressText.Visibility = Visibility.Visible;
        Step1ProgressText.Text = "正在获取最新版本号…";
        SetBusy(true, "正在下载 Tailscale 官方安装包…");

        try
        {
            var version = await TailscaleService.GetLatestVersionAsync() ?? "latest";

            var progress = new Progress<InstallerProgress>(report =>
            {
                Step1Progress.IsIndeterminate = report.Percent <= 0;
                if (report.Percent > 0) Step1Progress.Value = report.Percent;
                Step1ProgressText.Text = $"{report.Stage} · {report.Detail}";
            });

            var msiPath = await TailscaleService.DownloadInstallerAsync(version, progress);

            Step1Progress.IsIndeterminate = true;
            Step1ProgressText.Text = "正在静默安装…";
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Message = "安装中，请不要关闭窗口（大约需要十几秒）";
            StatusBar.IsOpen = true;

            var install = await TailscaleService.InstallAsync(msiPath);
            if (!install.Ok)
            {
                ShowError(install.Message);
                return;
            }

            if (!await TailscaleService.WaitForCliAsync(TimeSpan.FromSeconds(40)))
            {
                ShowError("安装完成但找不到 tailscale.exe，请重启电脑后再试一次");
                return;
            }

            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Message = "Tailscale 安装完成";
            StatusBar.IsOpen = true;
            await RefreshAsync(silent: true);

            // 装好就直接往下走，不用用户再点一次
            if (_stage == Stage.NeedLogin) await LoginAsync();
        }
        catch (OperationCanceledException)
        {
            ShowError("下载已取消");
        }
        catch (Exception ex)
        {
            ShowError($"下载或安装失败：{ex.Message}。可以手动到 tailscale.com/download 安装后再回到这里。");
        }
        finally
        {
            Step1Progress.Visibility = Visibility.Collapsed;
            Step1ProgressText.Visibility = Visibility.Collapsed;
            SetBusy(false, null);
            Render();
        }
    }

    private async Task LoginAsync()
    {
        SetBusy(true, "正在打开登录页面…");
        _authUrl = null;
        AuthUrlPanel.Visibility = Visibility.Collapsed;

        var onAuthUrl = new Action<string>(url =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _authUrl = url;
                AuthUrlBox.Text = url;
                AuthUrlPanel.Visibility = Visibility.Visible;
                StatusBar.Severity = InfoBarSeverity.Informational;
                StatusBar.Message = "请在浏览器里完成登录，完成后本窗口会自动继续";
                StatusBar.IsOpen = true;
            });
        });

        // 轮询进度实时回显：卡住时用户能立刻看到「卡在哪个状态、等了多久」
        var onProgress = new Action<string>(text =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                WaitingPanel.Visibility = Visibility.Visible;
                WaitingText.Text = "正在等待 Tailscale 就绪 · " + text;
            });
        });

        try
        {
            var ok = await TailscaleService.LoginAsync(onAuthUrl, onProgress, TimeSpan.FromMinutes(5));

            if (!ok)
            {
                var status = await TailscaleService.GetStatusAsync();
                if (status?.AuthUrl is { Length: > 0 } fallback && _authUrl is null)
                {
                    _authUrl = fallback;
                    AuthUrlBox.Text = fallback;
                    AuthUrlPanel.Visibility = Visibility.Visible;
                }

                ShowError(status?.IsStarting == true
                    ? "浏览器里的授权可能已经成功了，但本机还没能从 Tailscale 服务器确认。看看下面的原因说明，必要时点「重启 Tailscale 服务」。"
                    : "还没有完成登录。完成浏览器里的授权后，再点一次下面的按钮即可继续。");
                return;
            }

            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Message = "登录成功";
            StatusBar.IsOpen = true;
            _stageEnteredAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            ShowError($"登录遇到问题：{ex.Message}");
        }
        finally
        {
            SetBusy(false, null);
            await RefreshAsync(silent: true);
        }
    }

    private async Task ConnectAsync()
    {
        SetBusy(true, "正在连接…");
        try
        {
            var result = await TailscaleService.BringUpAsync();
            if (!result.Ok)
            {
                ShowError($"连接失败：{result.Message}");
                return;
            }

            await TailscaleService.WaitForReadyAsync(TimeSpan.FromSeconds(20));
        }
        catch (Exception ex)
        {
            ShowError($"连接遇到问题：{ex.Message}");
        }
        finally
        {
            SetBusy(false, null);
            await RefreshAsync(silent: true);
        }
    }

    private void ShowError(string message)
    {
        StatusBar.Severity = InfoBarSeverity.Error;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    // ══════════════════ 授权地址按钮 ══════════════════

    private void CopyAuthUrl_Click(object sender, RoutedEventArgs e)
    {
        if (_authUrl is null) return;
        // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
        if (ClipboardService.TrySetText(_authUrl).Success)
        {
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Message = "登录地址已复制";
            StatusBar.IsOpen = true;
        }
        else
        {
            ShowError("复制失败，请手动选中文本复制");
        }
    }

    private void OpenAuthUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = _authUrl ?? AuthUrlBox.Text;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            ShowError("无法打开浏览器，请手动复制地址到浏览器");
        }
    }
}
