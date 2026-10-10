using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>
/// 原生 WinUI3 运行库修复：检测 VC++ / .NET Framework / DirectX 缺失项，
/// 微软官方源下载 + Authenticode 签名校验 + 静默安装（逻辑见 RuntimeRepairService）。
/// </summary>
public sealed partial class RuntimeRepairPage : Page
{
    // 每个运行库卡片引用的控件集合
    private sealed record CardUi(
        Border IconBg, FontIcon Icon,
        TextBlock Missing, StackPanel ProgressPanel, TextBlock PhaseText, TextBlock PctText, ProgressBar Bar,
        FontIcon StatusIcon, TextBlock StatusText,
        Button RepairButton, ProgressRing BtnSpinner, TextBlock BtnText);

    private static readonly (string Id, string PhaseLabel)[] Phases =
    [
        (RuntimeRepairService.PhaseDownloading, "正在下载"),
        (RuntimeRepairService.PhaseVerifying, "正在校验 Microsoft 签名"),
        (RuntimeRepairService.PhaseInstalling, "正在安装"),
        (RuntimeRepairService.PhaseComplete, "已完成"),
    ];

    private readonly Dictionary<string, CardUi> _cards = new();
    private readonly Dictionary<string, bool> _repairEnabled = new();
    private readonly Dictionary<string, (string Phase, int Percent)> _lastProgress = new();

    private CancellationTokenSource? _cts;
    private bool _checking;
    private string? _activeRepairId;

    private Color _successColor, _cautionColor, _accentColor, _secondaryColor;

    public RuntimeRepairPage()
    {
        InitializeComponent();
        _cards[RuntimeRepairService.VisualCppId] = new CardUi(
            VcIconBg, VcIcon, VcMissing, VcProgressPanel, VcPhaseText, VcPctText, VcBar,
            VcStatusIcon, VcStatusText, VcRepairButton, VcBtnSpinner, VcBtnText);
        _cards[RuntimeRepairService.DotNetId] = new CardUi(
            DotNetIconBg, DotNetIcon, DotNetMissing, DotNetProgressPanel, DotNetPhaseText, DotNetPctText, DotNetBar,
            DotNetStatusIcon, DotNetStatusText, DotNetRepairButton, DotNetBtnSpinner, DotNetBtnText);
        _cards[RuntimeRepairService.DirectXId] = new CardUi(
            DxIconBg, DxIcon, DxMissing, DxProgressPanel, DxPhaseText, DxPctText, DxBar,
            DxStatusIcon, DxStatusText, DxRepairButton, DxBtnSpinner, DxBtnText);
    }

    // ───────────────────────────── 初始化 / 清理 ─────────────────────────────

    private void RuntimeRepairPage_Loaded(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        // 代码构建的画刷不会随主题自动刷新，切换主题后重新解析语义色并重着色
        ActualThemeChanged += (_, _) =>
        {
            LoadColors();
            ApplyStateColors();
        };
        LoadColors();
        _ = RefreshStatusesAsync();
    }

    private void RuntimeRepairPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private static Color ColorRes(string key, Color fallback)
    {
        if (key.EndsWith("Brush", StringComparison.Ordinal))
            return ThemeColors.ResolveBrush(key, fallback) is SolidColorBrush resolved ? resolved.Color : fallback;
        if (Application.Current.Resources.TryGetValue(key, out var v))
        {
            if (v is Color c) return c;
            if (v is SolidColorBrush b) return b.Color;
        }
        return fallback;
    }

    private void LoadColors()
    {
        // 语义色统一取自 ThemeColors（跟随系统主题/强调色），不再写死品牌色回退值
        _successColor = ColorRes("SystemFillColorSuccessBrush", ThemeColors.AccentGreen);
        _cautionColor = ColorRes("SystemFillColorCautionBrush", ThemeColors.AccentOrange);
        _accentColor = ColorRes("SystemAccentColor", ThemeColors.AccentBlue);
        _secondaryColor = ColorRes("TextFillColorSecondaryBrush", ThemeColors.SecondaryText);
    }

    private static SolidColorBrush Brush(Color color) => new(color);

    // ───────────────────────────── 检测 ─────────────────────────────

    private async Task RefreshStatusesAsync()
    {
        SetChecking(true);
        try
        {
            var statuses = await RuntimeRepairService.DetectAsync();
            if (_cts is null || _cts.IsCancellationRequested)
                return;
            foreach (var status in statuses)
                ApplyStatus(status);
        }
        catch (Exception ex)
        {
            ShowError("运行库检测失败", ex.Message);
        }
        finally
        {
            SetChecking(false);
        }
        ApplyStateColors();
    }

    private void SetChecking(bool checking)
    {
        _checking = checking;
        foreach (var (id, card) in _cards)
        {
            if (_activeRepairId is not null && _activeRepairId == id)
                continue;
            if (checking)
            {
                card.StatusIcon.Visibility = Visibility.Collapsed;
                card.StatusText.Text = "检测中…";
                card.StatusText.Foreground = Brush(_secondaryColor);
                card.RepairButton.IsEnabled = false;
            }
        }
    }

    private void ApplyStatus(RuntimeStatus status)
    {
        var card = _cards[status.Id];
        var installed = status.Installed;
        _repairEnabled[status.Id] = !installed;

        card.ProgressPanel.Visibility = Visibility.Collapsed;
        card.Missing.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        card.Missing.Text = string.Join("；", status.MissingComponents);
        ToolTipService.SetToolTip(card.Missing, string.Join("\n", status.MissingComponents));

        if (installed)
        {
            card.StatusIcon.Glyph = "\uE73E";
            card.StatusIcon.Visibility = Visibility.Visible;
            card.StatusText.Text = "已完整";
            card.RepairButton.IsEnabled = false;
            card.BtnText.Text = "已完整";
        }
        else
        {
            card.StatusIcon.Glyph = "\uE7BA";
            card.StatusIcon.Visibility = Visibility.Visible;
            card.StatusText.Text = $"检测到 {status.MissingComponents.Count} 项缺失";
            card.RepairButton.IsEnabled = _activeRepairId is null;
            card.BtnText.Text = "修复缺失项";
        }
    }

    private void ApplyStateColors()
    {
        foreach (var (id, card) in _cards)
        {
            if (_activeRepairId == id || _checking)
            {
                card.StatusText.Foreground = Brush(_secondaryColor);
                continue;
            }
            var installed = !_repairEnabled.GetValueOrDefault(id);
            var color = installed ? _successColor : _cautionColor;
            card.StatusIcon.Foreground = Brush(color);
            card.StatusText.Foreground = Brush(color);
            card.IconBg.Background = Brush(Color.FromArgb(0x22, color.R, color.G, color.B));
            card.Icon.Foreground = Brush(color);
        }
    }

    // ───────────────────────────── 修复 ─────────────────────────────

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeRepairId is not null)
            return;
        var button = (Button)sender;
        var runtimeId = (string)button.Tag;
        if (!_repairEnabled.GetValueOrDefault(runtimeId))
            return;

        _activeRepairId = runtimeId;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;
        SetBusy(runtimeId, true);

        var card = _cards[runtimeId];
        card.StatusIcon.Visibility = Visibility.Collapsed;
        card.StatusText.Text = "修复中…";
        card.StatusText.Foreground = Brush(_secondaryColor);
        card.ProgressPanel.Visibility = Visibility.Visible;
        card.Bar.Value = 0;
        card.PctText.Text = "0%";
        card.PhaseText.Text = "准备下载…";

        try
        {
            var message = await RuntimeRepairService.RepairAsync(runtimeId, OnProgress, _cts?.Token ?? CancellationToken.None);
            SuccessBar.Title = "运行库修复完成";
            SuccessBar.Message = message;
            SuccessBar.IsOpen = true;
            await RefreshStatusesAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowError("运行库修复失败", ex.Message);
            // 恢复该卡片为可重试状态
            var status = RuntimeRepairService.Detect().FirstOrDefault(s => s.Id == runtimeId);
            if (status is not null)
            {
                ApplyStatus(status);
                ApplyStateColors();
            }
        }
        finally
        {
            SetBusy(runtimeId, false);
            _activeRepairId = null;
        }
    }

    private void SetBusy(string runtimeId, bool busy)
    {
        foreach (var (id, card) in _cards)
        {
            if (id == runtimeId)
            {
                card.RepairButton.IsEnabled = !busy && _repairEnabled.GetValueOrDefault(id);
                card.BtnSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
                if (busy)
                    card.BtnText.Text = "修复中";
                return;
            }
            card.RepairButton.IsEnabled = !busy && !_checking && _repairEnabled.GetValueOrDefault(id);
        }
    }

    /// <summary>后台线程回调：按 (阶段, 百分比) 节流后投递到 UI 线程。</summary>
    private void OnProgress(RuntimeRepairProgress progress)
    {
        if (_lastProgress.TryGetValue(progress.RuntimeId, out var last) && last == (progress.Phase, progress.Percent))
            return;
        _lastProgress[progress.RuntimeId] = (progress.Phase, progress.Percent);
        DispatcherQueue.TryEnqueue(() => ApplyProgress(progress));
    }

    private void ApplyProgress(RuntimeRepairProgress progress)
    {
        if (_activeRepairId != progress.RuntimeId)
            return;
        var card = _cards[progress.RuntimeId];
        var label = Phases.FirstOrDefault(p => p.Id == progress.Phase).PhaseLabel;
        card.PhaseText.Text = $"{label}：{progress.Detail}";
        card.Bar.Value = progress.Percent;
        card.PctText.Text = $"{progress.Percent}%";
    }

    private void ShowError(string title, string message)
    {
        ErrorBar.Title = title;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
