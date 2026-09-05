using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>DNS 预设卡片视图模型（INPC——延迟/状态变化只推送单个属性，不重建列表，避免滚动跳顶）。</summary>
public sealed class DnsPresetVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public DnsPresetVm Self => this;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Primary { get; init; }
    public string Secondary { get; init; } = "";
    public required SolidColorBrush ColorBrush { get; init; }
    public required SolidColorBrush IconBackground { get; init; }
    public string SecondaryLine => Secondary;

    private string _latencyText = "--";
    public string LatencyText { get => _latencyText; set { _latencyText = value; Raise(); } }

    private SolidColorBrush _latencyBrush = new(Color.FromArgb(255, 142, 142, 142));
    public SolidColorBrush LatencyBrush { get => _latencyBrush; set { _latencyBrush = value; Raise(); } }

    private string _latencyHint = "尚未探测或请求超时";
    public string LatencyHint { get => _latencyHint; set { _latencyHint = value; Raise(); } }

    private bool _isApplied;
    private bool IsApplied { get => _isApplied; set { _isApplied = value; Raise(); Raise(nameof(AppliedVisibility)); Raise(nameof(CardBorderBrush)); } }

    public Visibility AppliedVisibility => IsApplied ? Visibility.Visible : Visibility.Collapsed;

    private SolidColorBrush _cardBorderBrush = new(Color.FromArgb(0x1F, 0, 0, 0));
    public SolidColorBrush CardBorderBrush { get => _cardBorderBrush; set { _cardBorderBrush = value; Raise(); } }

    /// <summary>外部（应用 DNS 后）标记本卡是否已应用。</summary>
    public void SetApplied(bool applied, SolidColorBrush? accent = null)
    {
        IsApplied = applied;
        if (applied && accent is not null)
            CardBorderBrush = accent;
        else if (!applied)
            CardBorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0, 0, 0));
    }
}

/// <summary>网络优化项开关卡片视图模型（INPC：批量操作后仅推送 IsOn，不重建列表）。</summary>
public sealed class OptimizeItemVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public OptimizeItemVm Self => this;
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public required SolidColorBrush ColorBrush { get; init; }
    public required SolidColorBrush IconBackground { get; init; }

    private bool _isOn;
    public bool IsOn { get => _isOn; set { _isOn = value; Raise(); } }

    private bool _canToggle = true;
    public bool CanToggle { get => _canToggle; set { _canToggle = value; Raise(); } }
}

/// <summary>
/// 网络优化页：照搬 nexbox 网络优化模块的全部功能——TCP 拥塞控制 / Chimney / Nagle / 网卡节能四开关
/// （乐观更新 + 失败回滚）、DNS 预设与自定义 DNS（PowerShell 设置）、每秒真实 UDP 延迟测速与劫持判定、
/// 公网 IP 多源查询、清缓存 / 重置网络 / 修复 DHCP。逻辑见 NetworkOptimizeService。
/// </summary>
public sealed partial class NetworkOptimizePage : Page
{
    private static readonly Color BrandViolet = Color.FromArgb(255, 124, 108, 240);
    private static readonly Color SuccessGreen = Color.FromArgb(255, 43, 182, 115);
    private static readonly Color CautionAmber = Color.FromArgb(255, 245, 166, 35);
    private static readonly Color CriticalRed = Color.FromArgb(255, 242, 80, 59);
    private static readonly Color NeutralGray = Color.FromArgb(255, 142, 142, 142);

    /// <summary>各优化项开启前确认弹窗的影响说明（关闭/恢复默认无需确认）。</summary>
    private static readonly Dictionary<string, string> EnableImpactMessages = new()
    {
        ["tcp-congestion"] = "将系统 TCP 拥塞控制算法从默认的 NewReno 切换为 CTCP（Compound TCP）。在大带宽、高延迟链路（如大文件下载、跨地域传输）上可提升吞吐量；普通家用网络收益很小。可随时关闭恢复默认。",
        ["chimney-offload"] = "将关闭 TCP Chimney Offload（网卡 TCP 硬件卸载）。该功能自 Windows 8 起已被微软弃用、默认即为关闭状态，此操作主要起确认作用，对现代系统影响很小。",
        ["nagle-algorithm"] = "将全局禁用 Nagle 算法并直接回 ACK：小包立即发送、立即确认，游戏/远程桌面等交互场景延迟更低。代价是网络包数量略增，低速连接吞吐可能受到轻微影响。该设置写入注册表、全局生效，未遇到延迟问题时可不必开启。",
        ["adapter-power"] = "将禁用网卡节能（等效于取消勾选设备管理器「允许计算机关闭此设备以节约电源」），减少节能切换造成的延迟尖峰。代价：笔记本功耗略有增加。",
        ["tcp-autotuning"] = "将禁用 TCP 接收窗口自动调谐（接收窗口固定）。游戏延迟抖动更小，但在千兆高速或跨地域高延迟链路上可能明显限制吞吐。微软官方建议仅在确认问题确由自动调谐引起时关闭。",
        ["network-throttling"] = "将禁用多媒体网络节流：多媒体活动期间后台流量不再被限制到约 10 包/秒，游戏/语音时网络响应更好。仅修改当前用户的注册表设置，副作用很小，可随时恢复。",
    };

    private const string SavedKeyPrefix = "NetOpt_";
    private const string SavedDnsPrimaryKey = "NetOpt_dns_primary";
    private const string SavedDnsSecondaryKey = "NetOpt_dns_secondary";

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _dnsTimer;
    /// <summary>用户手动操作保存的开关状态（优先于扫描结果，对照 nexbox savedStates）。</summary>
    private readonly Dictionary<string, bool> _saved = new();
    /// <summary>后端扫描到的实时状态（对照 nexbox scannedStates）。</summary>
    private readonly Dictionary<string, bool> _scanned = new();
    private readonly Dictionary<string, DnsProbeResult?> _dnsProbes = new(); // null = 超时
    private readonly HashSet<string> _dnsInFlight = new();
    private (string Primary, string Secondary) _currentDns = ("", "");
    private readonly List<DnsPresetVm> _dnsVms = new();
    private readonly List<OptimizeItemVm> _itemVms = new();
    private bool _busy;

    public NetworkOptimizePage()
    {
        InitializeComponent();
    }

    // ───────────────────────────── 初始化 / 清理 ─────────────────────────────

    private void NetworkOptimizePage_Loaded(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        _ = LoadAsync();
    }

    private void NetworkOptimizePage_Unloaded(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _dnsTimer?.Stop();
        _dnsTimer = null;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateBack();

    private static SolidColorBrush Brush(Color color) => new(color);

    private static SolidColorBrush Tint(Color color, byte alpha) => new(Color.FromArgb(alpha, color.R, color.G, color.B));

    private static Color ParseHex(string hex)
    {
        var value = Convert.ToInt32(hex[1..], 16);
        return Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        LoadingPanel.Visibility = Visibility.Visible;
        ContentPanel.Visibility = Visibility.Collapsed;
        ErrorBar.IsOpen = false;

        try
        {
            // ① 并行：扫描状态 + 读取已保存设置（对照前端 Promise.allSettled）
            var state = await Task.Run(NetworkOptimizeService.CheckStates);
            var savedPrimary = AppSettings.Get(SavedDnsPrimaryKey) ?? "";
            var savedSecondary = AppSettings.Get(SavedDnsSecondaryKey) ?? "";

            foreach (var item in NetworkOptimizeService.OptimizerItems)
                _scanned[item.StateKey] = GetScannedValue(state, item.StateKey);
            // 用户手动保存的开关状态（优先于扫描结果，对照前端 savedStates）
            foreach (var item in NetworkOptimizeService.OptimizerItems)
            {
                var saved = AppSettings.Get(SavedKeyPrefix + item.Id);
                if (saved is not null)
                    _saved[item.Id] = saved == "true";
            }
            // DNS：优先使用保存的手动设置，否则用扫描结果
            _currentDns = savedPrimary.Length > 0 ? (savedPrimary, savedSecondary) : (state.DnsPrimary, state.DnsSecondary);

            CurrentDnsText.Text = _currentDns.Primary.Length > 0
                ? $"{_currentDns.Primary}{(_currentDns.Secondary.Length > 0 ? " / " + _currentDns.Secondary : "")}"
                : "使用自动获取";

            RebuildItemVms();
            RebuildDnsVms();

            LoadingPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Visible;

            // ② DNS 延迟轮询（每 3 秒对所有预设 UDP 实测；1 秒太频繁闪烁观感差）
            _dnsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _dnsTimer.Tick += DnsTimer_Tick;
            _dnsTimer.Start();
            _ = ProbeAllDnsAsync(tick: false);

            // ③ 公网 IP（页面加载自动查询）
            _ = RefreshPublicIpAsync(manual: false);
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorBar.Title = "加载状态失败";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
        finally
        {
            _busy = false;
        }
    }

    private static bool GetScannedValue(NetworkTweakState state, string key) => key switch
    {
        "tcp_congestion_optimized" => state.TcpCongestionOptimized,
        "chimney_offload" => state.ChimneyOffload,
        "nagle_optimized" => state.NagleOptimized,
        "adapter_power_saving_off" => state.AdapterPowerSavingOff,
        "autotuning_disabled" => state.AutoTuningDisabled,
        "throttling_disabled" => state.ThrottlingDisabled,
        _ => false
    };

    // ───────────────────────────── 视图模型构建 ─────────────────────────────

    private void RebuildItemVms()
    {
        _itemVms.Clear();
        foreach (var item in NetworkOptimizeService.OptimizerItems)
        {
            var color = ParseHex(item.ColorHex);
            _itemVms.Add(new OptimizeItemVm
            {
                Id = item.Id,
                Title = item.Title,
                Description = item.Description,
                Glyph = item.Glyph,
                ColorBrush = Brush(color),
                IconBackground = Tint(color, 0x22),
                IsOn = GetItemState(item),
                CanToggle = true
            });
        }
        OptimizeList.ItemsSource = _itemVms.ToList();
    }

    /// <summary>开关状态：已保存的手动状态优先，否则用扫描结果（对照前端 getItemState）。</summary>
    private bool GetItemState(NetworkOptimizerItem item)
    {
        if (_saved.TryGetValue(item.Id, out var saved))
            return saved;
        return _scanned.TryGetValue(item.StateKey, out var scanned) && scanned;
    }

    private void RebuildDnsVms()
    {
        _dnsVms.Clear();
        foreach (var preset in NetworkOptimizeService.DnsPresets)
        {
            var color = ParseHex(preset.ColorHex);
            var vm = new DnsPresetVm
            {
                Id = preset.Id,
                Name = preset.Name,
                Primary = preset.Primary,
                Secondary = preset.Secondary,
                ColorBrush = Brush(color),
                IconBackground = Tint(color, 0x22),
            };
            vm.SetApplied(IsPresetApplied(preset), Brush(color));
            _dnsVms.Add(vm);
        }
        DnsList.ItemsSource = _dnsVms.ToList();
    }

    private bool IsPresetApplied(DnsPreset preset) =>
        _currentDns.Primary.Length > 0
        && _currentDns.Primary == preset.Primary
        && _currentDns.Secondary == preset.Secondary;

    /// <summary>应用/恢复 DNS 后刷新各卡「已应用」状态（只推属性，不重建列表）。</summary>
    private void RefreshDnsAppliedState()
    {
        foreach (var vm in _dnsVms)
        {
            var preset = NetworkOptimizeService.DnsPresets.First(p => p.Id == vm.Id);
            vm.SetApplied(IsPresetApplied(preset), Brush(ParseHex(preset.ColorHex)));
        }
    }

    /// <summary>延迟展示（照搬前端 latencyColor / hijacked 判定）：优<80 绿 / <200 橙 / 否则红；超时灰、劫持橙。</summary>
    private static (string Text, SolidColorBrush Brush, string Hint) BuildLatencyPresentation(DnsPreset preset, DnsProbeResult? probe)
    {
        if (probe is null)
            return ("--", Brush(NeutralGray), "尚未探测或请求超时");

        var hijacked = probe.Responder.Length > 0 && probe.Responder != preset.Primary || probe.LatencyMs < 1.0;
        if (hijacked)
        {
            var detail = probe.Responder.Length > 0 && probe.Responder != preset.Primary
                ? $"响应来自 {probe.Responder}{(probe.ViaInterface is not null ? $"，经网卡「{probe.ViaInterface}」路由" : "")}"
                : $"延迟低于 1ms{(probe.ViaInterface is not null ? $"（经网卡「{probe.ViaInterface}」路由）" : "")}，查询未真正到达目标服务器";
            return ("被劫持", Brush(CautionAmber), detail);
        }
        var hint = probe.ViaInterface is not null ? $"经网卡「{probe.ViaInterface}」路由（应答 {probe.Responder}）" : $"应答来自 {probe.Responder}";
        return probe.LatencyMs < NetworkOptimizeService.DnsLatencyGoodMs
            ? ($"{Math.Round(probe.LatencyMs)} ms", Brush(SuccessGreen), hint)
            : probe.LatencyMs < NetworkOptimizeService.DnsLatencyFairMs
                ? ($"{Math.Round(probe.LatencyMs)} ms", Brush(CautionAmber), hint)
                : ($"{Math.Round(probe.LatencyMs)} ms", Brush(CriticalRed), hint);
    }

    // ───────────────────────────── DNS 延迟轮询 ─────────────────────────────

    private void DnsTimer_Tick(object? sender, object e) => _ = ProbeAllDnsAsync(tick: true);

    /// <summary>对所有预设发起 UDP 延迟探测；上轮未返回的跳过（防超时堆积并发，对照前端 dnsLatencyInFlight）。
    /// 结果只推送到单个卡片的延迟属性（INPC），不重建列表——避免每秒重排导致滚动位置跳顶。</summary>
    private async Task ProbeAllDnsAsync(bool tick)
    {
        var probes = new List<Task>();
        foreach (var preset in NetworkOptimizeService.DnsPresets)
        {
            if (_dnsInFlight.Contains(preset.Id))
                continue;
            _dnsInFlight.Add(preset.Id);
            probes.Add(Task.Run(() =>
            {
                DnsProbeResult? result;
                try
                {
                    result = NetworkOptimizeService.TestDnsLatency(preset.Primary);
                }
                catch
                {
                    result = null;
                }
                finally
                {
                    _dnsInFlight.Remove(preset.Id);
                }
                if (DispatcherQueue is not null)
                {
                    try
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            _dnsProbes[preset.Id] = result;
                            var vm = _dnsVms.FirstOrDefault(v => v.Id == preset.Id);
                            if (vm is null)
                                return;
                            var (text, brush, hint) = BuildLatencyPresentation(preset, result);
                            vm.LatencyText = text;
                            vm.LatencyBrush = brush;
                            vm.LatencyHint = hint;
                        });
                    }
                    catch { }
                }
            }));
        }
        if (tick && probes.Count == 0)
            return;
        await Task.WhenAll(probes.ToArray());
    }

    // ───────────────────────────── 公网 IP ─────────────────────────────

    private async Task RefreshPublicIpAsync(bool manual)
    {
        IpLoadingRing.IsActive = true;
        IpLoadingRing.Visibility = Visibility.Visible;
        CopyIpButton.IsEnabled = false;
        try
        {
            var ip = await Task.Run(NetworkOptimizeService.GetPublicIpAsync);
            if (manual || PublicIpText.Text == "--")
            {
                PublicIpText.Text = ip;
                PublicIpText.Foreground = Brush(Color.FromArgb(255, 240, 240, 240));
                CopyIpButton.IsEnabled = true;
            }
            _ = manual ? ShowSuccessAsync("公网 IP 已更新", ip) : Task.CompletedTask;
        }
        catch (Exception ex)
        {
            if (manual || PublicIpText.Text == "--")
            {
                PublicIpText.Text = "获取失败，请检查网络连接";
                PublicIpText.Foreground = Brush(CriticalRed);
            }
            if (manual)
                _ = ShowErrorAsync("公网 IP 获取失败", ex.Message);
        }
        finally
        {
            IpLoadingRing.IsActive = false;
            IpLoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshIpButton_Click(object sender, RoutedEventArgs e) => _ = RefreshPublicIpAsync(manual: true);

    private async void CopyIpButton_Click(object sender, RoutedEventArgs e)
    {
        var ip = PublicIpText.Text;
        if (System.Net.IPAddress.TryParse(ip, out _))
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(ip);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            await ShowSuccessAsync("公网 IP 已复制到剪贴板", ip);
        }
    }

    // ───────────────────────────── 网络优化项（乐观更新，对照前端 toggleItem） ─────────────────────────────

    private async void OptimizeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.DataContext is not OptimizeItemVm vm)
            return;
        if (_busy)
        {
            toggle.IsOn = vm.IsOn; // 回弹
            return;
        }
        _busy = true;
        var enable = toggle.IsOn;
        var previous = vm.IsOn;

        var item = NetworkOptimizeService.OptimizerItems.FirstOrDefault(i => i.Id == vm.Id);
        if (item is null)
        {
            _busy = false;
            return;
        }

        // 开启前先确认，说明该选项对系统的影响；关闭（恢复默认）无需确认。
        // 失败回滚等程序性重入时 enable 与 vm.IsOn 相同，不会二次弹窗
        if (enable && enable != vm.IsOn && !await ShowEnableConfirmAsync(item))
        {
            toggle.IsOn = previous; // 取消：回弹（重入的 Toggled 被上方 _busy 守卫拦截）
            _busy = false;
            return;
        }

        vm.IsOn = enable; // 乐观更新，让动画立即播放
        try
        {
            var result = await Task.Run(() => ExecuteItemAction(item, enable));
            _saved[item.Id] = enable;
            _scanned[item.StateKey] = enable;
            AppSettings.Set(SavedKeyPrefix + item.Id, enable);
            SuccessBar.Title = enable ? $"已优化：{item.Title}" : $"已取消优化：{item.Title}";
            SuccessBar.Message = result;
            SuccessBar.IsOpen = true;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            // 失败时回滚开关状态
            vm.IsOn = previous;
            _ = ShowErrorAsync($"操作执行失败：{item.Title}", ex.Message);
        }
        finally
        {
            _busy = false;
            toggle.IsOn = vm.IsOn;
        }
    }

    private static string ExecuteItemAction(NetworkOptimizerItem item, bool enable) => item.Id switch
    {
        "tcp-congestion" => enable
            ? NetworkOptimizeService.SetTcpCongestion().Message
            : NetworkOptimizeService.RestoreTcpCongestion().Message,
        "chimney-offload" => enable
            ? NetworkOptimizeService.SetChimneyOff().Message
            : NetworkOptimizeService.RestoreChimney().Message,
        "nagle-algorithm" => enable
            ? NetworkOptimizeService.SetNagleOptimization().Message
            : NetworkOptimizeService.RestoreNagleOptimization().Message,
        "adapter-power" => enable
            ? NetworkOptimizeService.SetAdapterPowerSavingOff().Message
            : NetworkOptimizeService.RestoreAdapterPowerSaving().Message,
        "tcp-autotuning" => enable
            ? NetworkOptimizeService.SetAutoTuningDisabled().Message
            : NetworkOptimizeService.RestoreAutoTuning().Message,
        "network-throttling" => enable
            ? NetworkOptimizeService.SetThrottlingDisabled().Message
            : NetworkOptimizeService.RestoreThrottling().Message,
        _ => throw new InvalidOperationException("未知优化项")
    };

    // ───────────────────────────── 开启前确认弹窗 ─────────────────────────────

    private static string GetEnableImpact(NetworkOptimizerItem item)
        => EnableImpactMessages.TryGetValue(item.Id, out var text) ? text : item.Description;

    private async Task<bool> ShowEnableConfirmAsync(NetworkOptimizerItem item)
        => await ShowConfirmCoreAsync($"开启「{item.Title}」？", GetEnableImpact(item));

    private async Task<bool> ShowBatchEnableConfirmAsync()
    {
        var lines = new List<string> { "将一次性对系统应用以下 6 项修改，均为可逆操作（可随时点「全部恢复默认」还原）：" };
        lines.AddRange(NetworkOptimizeService.OptimizerItems.Select(i => $"· {i.Title}：{i.Description}"));
        return await ShowConfirmCoreAsync("开启全部网络优化？", string.Join("\n", lines));
    }

    /// <summary>主题化确认弹窗；无法弹出时按「未确认」处理（不执行操作）。</summary>
    private async Task<bool> ShowConfirmCoreAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
                PrimaryButtonText = "确认开启",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            dialog.Resources["ContentDialogMaxWidth"] = 520;
            dialog.Resources["ContentDialogMaxHeight"] = 560;
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch
        {
            return false;
        }
    }

    // ───────────────────────────── 批量优化 / 恢复 ─────────────────────────────

    private async void BatchEnableButton_Click(object sender, RoutedEventArgs e) => await RunBatchAsync(enable: true);

    private async void BatchDisableButton_Click(object sender, RoutedEventArgs e) => await RunBatchAsync(enable: false);

    private async Task RunBatchAsync(bool enable)
    {
        if (_busy) return;
        if (enable && !await ShowBatchEnableConfirmAsync())
            return;
        _busy = true;
        BatchEnableButton.IsEnabled = false;
        BatchDisableButton.IsEnabled = false;
        try
        {
            var result = enable
                ? await Task.Run(NetworkOptimizeService.BatchEnable)
                : await Task.Run(NetworkOptimizeService.BatchDisable);
            if (enable)
            {
                foreach (var item in NetworkOptimizeService.OptimizerItems)
                {
                    _saved[item.Id] = true;
                    _scanned[item.StateKey] = true;
                    AppSettings.Set(SavedKeyPrefix + item.Id, true);
                }
            }
            else
            {
                _saved.Clear();
                foreach (var item in NetworkOptimizeService.OptimizerItems)
                {
                    _scanned[item.StateKey] = false;
                    AppSettings.Remove(SavedKeyPrefix + item.Id);
                }
            }
            RebuildItemVms();
            SuccessBar.Title = enable ? "全部网络优化已执行" : "全部网络优化已恢复";
            SuccessBar.Message = result.Message;
            SuccessBar.IsOpen = true;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync("批量操作执行失败", ex.Message);
            RebuildItemVms();
        }
        finally
        {
            _busy = false;
            BatchEnableButton.IsEnabled = true;
            BatchDisableButton.IsEnabled = true;
        }
    }

    // ───────────────────────────── DNS 应用 / 恢复 ─────────────────────────────

    private async void ApplyDnsPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DnsPresetVm vm })
            return;
        await ApplyDnsAsync(vm.Primary, vm.Secondary);
    }

    private async void ApplyCustomDnsButton_Click(object sender, RoutedEventArgs e)
    {
        var primary = CustomPrimaryBox.Text.Trim();
        if (primary.Length == 0)
        {
            ErrorBar.Title = "请至少填写首选 DNS 地址";
            ErrorBar.Message = "";
            ErrorBar.IsOpen = true;
            return;
        }
        await ApplyDnsAsync(primary, CustomSecondaryBox.Text.Trim());
    }

    private async Task ApplyDnsAsync(string primary, string secondary)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var result = await Task.Run(() => NetworkOptimizeService.SetDnsServers(primary, secondary));
            _currentDns = (primary, secondary);
            AppSettings.Set(SavedDnsPrimaryKey, primary);
            AppSettings.Set(SavedDnsSecondaryKey, secondary);
            CurrentDnsText.Text = $"{primary}{(secondary.Length > 0 ? " / " + secondary : "")}";
            RefreshDnsAppliedState();
            SuccessBar.Title = "DNS 已应用";
            SuccessBar.Message = result.Message;
            SuccessBar.IsOpen = true;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync("DNS 应用失败", ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void RestoreDnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        RestoreDnsButton.IsEnabled = false;
        try
        {
            var result = await Task.Run(NetworkOptimizeService.RestoreDnsServers);
            _currentDns = ("", "");
            AppSettings.Remove(SavedDnsPrimaryKey);
            AppSettings.Remove(SavedDnsSecondaryKey);
            CurrentDnsText.Text = "使用自动获取";
            RefreshDnsAppliedState();
            SuccessBar.Title = "DNS 已恢复为自动获取";
            SuccessBar.Message = result.Message;
            SuccessBar.IsOpen = true;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync("DNS 恢复失败", ex.Message);
        }
        finally
        {
            _busy = false;
            RestoreDnsButton.IsEnabled = true;
        }
    }

    // ───────────────────────────── 清缓存 / 重置 / 修复 DHCP ─────────────────────────────

    private async void ClearDnsCacheButton_Click(object sender, RoutedEventArgs e)
    {
        ClearDnsCacheButton.IsEnabled = false;
        try
        {
            var result = await Task.Run(NetworkOptimizeService.ClearDnsCache);
            SuccessBar.Title = "DNS 缓存已清理";
            SuccessBar.Message = result.Message;
            SuccessBar.IsOpen = true;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync("清理 DNS 缓存失败", ex.Message);
        }
        finally
        {
            ClearDnsCacheButton.IsEnabled = true;
        }
    }

    private async void ResetNetworkButton_Click(object sender, RoutedEventArgs e)
    {
        ResetNetworkButton.IsEnabled = false;
        try
        {
            var result = await Task.Run(NetworkOptimizeService.ResetNetwork);
            SuccessBar.Title = "网络已重置，建议重启电脑后生效";
            SuccessBar.Message = result.Message;
            SuccessBar.IsOpen = true;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync("网络重置失败", ex.Message);
        }
        finally
        {
            ResetNetworkButton.IsEnabled = true;
        }
    }

    private async void FixDhcpButton_Click(object sender, RoutedEventArgs e)
    {
        FixDhcpButton.IsEnabled = false;
        try
        {
            var result = await Task.Run(NetworkOptimizeService.FixDhcp);
            SuccessBar.Title = "已恢复 DHCP 自动获取，DNS 缓存已刷新";
            SuccessBar.Message = result.Message;
            SuccessBar.IsOpen = true;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync("DHCP 修复失败", ex.Message);
        }
        finally
        {
            FixDhcpButton.IsEnabled = true;
        }
    }

    // ───────────────────────────── 重新扫描 ─────────────────────────────

    private async void RescanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        RescanButton.IsEnabled = false;
        try
        {
            var state = await Task.Run(NetworkOptimizeService.CheckStates);
            foreach (var item in NetworkOptimizeService.OptimizerItems)
                _scanned[item.StateKey] = GetScannedValue(state, item.StateKey);
            // DNS：用户已手动设置时不覆盖
            if (_currentDns.Primary.Length == 0 && state.DnsPrimary.Length > 0)
            {
                _currentDns = (state.DnsPrimary, state.DnsSecondary);
                CurrentDnsText.Text = $"{state.DnsPrimary}{(state.DnsSecondary.Length > 0 ? " / " + state.DnsSecondary : "")}";
            }
            RebuildItemVms();
            RefreshDnsAppliedState();
            SuccessBar.Title = "扫描完成";
            SuccessBar.Message = "已按当前系统状态刷新";
            SuccessBar.IsOpen = true;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync("扫描失败", ex.Message);
        }
        finally
        {
            _busy = false;
            RescanButton.IsEnabled = true;
        }
    }

    // ───────────────────────────── 提示 ─────────────────────────────

    private async Task ShowErrorAsync(string title, string message)
    {
        await Task.CompletedTask;
        ErrorBar.Title = title;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
        SuccessBar.IsOpen = false;
    }

    private async Task ShowSuccessAsync(string title, string message)
    {
        await Task.CompletedTask;
        SuccessBar.Title = title;
        SuccessBar.Message = message;
        SuccessBar.IsOpen = true;
        ErrorBar.IsOpen = false;
    }
}