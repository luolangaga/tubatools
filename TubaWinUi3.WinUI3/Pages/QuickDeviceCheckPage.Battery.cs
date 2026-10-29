using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class QuickDeviceCheckPage
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _batteryTimer;
    private Task<BatteryInfo>? _inspectionBatteryInfoTask;

    private void BuildBatteryStep()
    {
        var version = _contentVersion;
        var panel = new StackPanel { Spacing = 12 };
        StepContentPanel.Children.Add(new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        var power = Copy("正在读取供电状态…");
        var capacity = Copy("正在读取电池容量与循环次数…");
        var rate = Copy("正在读取充放电功率…", true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(power, "InspectionBatteryPower");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(capacity, "InspectionBatteryCapacity");
        panel.Children.Add(power);
        panel.Children.Add(capacity);
        panel.Children.Add(rate);
        panel.Children.Add(Copy("① 拔下电源：确认切换为电池供电，电脑不关机。\n② 重新接通电源：确认恢复插电状态，观察是否充电。\n③ 留意电量突降、异常发热、接口松动或机身鼓包。"));
        panel.Children.Add(Copy("插电未充电可能是电池已满或启用了充电上限。循环次数与容量由固件报告，未提供的数据会标为“未能读取”。电池没有统一可读取的累计通电时长，不能仅凭循环次数判断是否全新。", true));

        // The existing report reader performs synchronous WMI work before its first await.
        var infoTask = _inspectionBatteryInfoTask ??= Task.Run(BatteryReportService.GetBatteryInfoAsync);
        bool refreshing = false;
        bool Current() => !_closed && _contentVersion == version && _currentStep == 8;
        async Task LoadCapacityAsync()
        {
            try
            {
                var info = await infoTask;
                if (!Current()) return;
                capacity.Text = info.BatteryPresent
                    ? $"设计容量：{info.DesignedCapacityText}\n满充容量：{info.FullChargedCapacityText}\n电池健康度：{(info.HealthPercent > 0 ? $"{info.HealthPercent:F1}%" : "未能读取")}\n循环次数：{(info.CycleCountAvailable ? $"{info.CycleCount} 次" : "未能读取")}"
                    : "未能读取电池信息，请确认电池已安装且驱动正常。";
            }
            catch (Exception)
            {
                if (Current()) capacity.Text = "电池容量与循环次数：未能读取";
            }
        }
        async Task RefreshAsync()
        {
            if (!Current()) return;
            UpdatePower();
            if (refreshing) return;
            refreshing = true;
            try
            {
                var status = await BatteryAnalyzerService.GetRealtimeStatusAsync();
                if (!Current()) return;
                UpdatePower();
                rate.Text = status is null ? "充放电功率 / 电压：未能读取"
                    : $"充电功率：{Watts(status.ChargeRateMw)} · 放电功率：{Watts(status.DischargeRateMw)}\n电池电压：{(status.VoltageMv > 0 ? $"{status.VoltageMv / 1000.0:F2} V" : "未能读取")}";
                if (status is not null && GetInspectionPowerStatus(out var ps) && ps.BatteryFlag != 255 && (ps.BatteryFlag & 128) == 0)
                    power.Text += status.Charging ? " · 充电中" : status.Discharging ? " · 放电中" : " · 未在充放电";
            }
            catch (Exception)
            {
                if (Current()) rate.Text = "部分电池信息暂时无法读取，可继续手动检查供电切换。";
            }
            finally { refreshing = false; }
        }
        void UpdatePower()
        {
            if (!GetInspectionPowerStatus(out var status))
            {
                power.Text = "当前供电状态：未能读取";
                return;
            }
            if (status.BatteryFlag != 255 && (status.BatteryFlag & 128) != 0)
            {
                power.Text = "系统未检测到电池，请检查电池连接。";
                return;
            }
            var source = status.AcLineStatus switch { 0 => "电池供电（未插电）", 1 => "已接通外部电源", _ => "供电来源未知" };
            power.Text = $"{source}\n当前电量：{(status.BatteryLifePercent <= 100 ? $"{status.BatteryLifePercent}%" : "未能读取")}";
        }
        _batteryTimer = DispatcherQueue.CreateTimer();
        _batteryTimer.Interval = TimeSpan.FromSeconds(3);
        _batteryTimer.Tick += async (_, _) => await RefreshAsync();
        _batteryTimer.Start();
        _ = LoadCapacityAsync();
        _ = RefreshAsync();
    }

    private static string Watts(int mw) => mw >= 0 ? $"{mw / 1000.0:F1} W" : "未能读取";

    private void StopBatteryRefresh()
    {
        _batteryTimer?.Stop();
        _batteryTimer = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InspectionPowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetSystemPowerStatus")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetInspectionPowerStatus(out InspectionPowerStatus status);
}
