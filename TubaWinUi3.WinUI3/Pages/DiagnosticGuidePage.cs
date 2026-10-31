using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>Task guidance reuses existing tools without claiming an automatic diagnosis.</summary>
public sealed class DiagnosticGuidePage : Page
{
    private readonly StackPanel _steps = new() { Spacing = 16 };
    public DiagnosticGuidePage()
    {
        var panel = new StackPanel { Spacing = 24, Margin = new Thickness(32), MaxWidth = 800 };
        panel.Children.Add(ToolExperience.Text("问题排查", "TitleTextBlockStyle"));
        panel.Children.Add(ToolExperience.Text("选择遇到的问题，按顺序检查。每一步都提供结果解读；打开工具不会自动开始烤机或修改系统。"));
        var picker = new ComboBox { Header = "想解决什么问题？", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(picker, "DiagnosticScenario");
        foreach (var title in new[] { "电脑卡顿", "游戏掉帧", "硬盘异常", "新机验收" }) picker.Items.Add(title);
        picker.SelectionChanged += (_, _) => ShowSteps(picker.SelectedIndex);
        panel.Children.Add(picker);
        panel.Children.Add(_steps);
        Content = new ScrollViewer
        {
            Content = panel, HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        picker.SelectedIndex = 0;
    }

    private void ShowSteps(int scenario)
    {
        _steps.Children.Clear();
        (string Title, string Help, string Tool)[] steps = scenario switch
        {
            1 => [
                ("1. 记录实际掉帧时的数据", "打开游戏监控，观察帧率、CPU/GPU 负载及温度。先记录发生问题的场景，再比较降低画质或帧率上限后的变化。", "game-monitor"),
                ("2. 检查散热与稳定性", "在稳定供电、散热正常时手动开始短时测试。若出现异常高温、黑屏或报错，停止测试；负载高本身不等于硬件故障。", "stress-test")],
            2 => [
                ("1. 查看硬盘健康信息", "先查看 SMART 告警、介质错误和温度。健康百分比不能单独证明没有故障；如有重要数据，先备份再继续排查。", "disk-health"),
                ("2. 对照设备与使用情况", "核对硬盘型号、容量及通电信息。异常声音、频繁掉盘或读取失败时，避免反复进行高负载测试。", "quick-device-check")],
            3 => [
                ("1. 按验机流程逐项检查", "依次核对订单配置、硬盘、屏幕、键盘及音频。每一步可以跳过，未测试不代表通过。", "quick-device-check"),
                ("2. 解读测试结果", "结合检测教程了解温度、负载与稳定性。保存异常现象和测试条件，便于复现及联系售后。", "pc-tutorial")],
            _ => [
                ("1. 观察卡顿时的负载", "用监控记录 CPU、内存、磁盘和温度，确认卡顿发生时哪个指标变化，避免盲目清理或禁用系统组件。", "game-monitor"),
                ("2. 检查开机负担", "查看启动项，优先处理自己认识且不需要开机启动的软件。保留驱动、安全软件及用途不明的系统项。", "startup-manager"),
                ("3. 排除存储异常", "若伴随读写缓慢、卡死或文件读取失败，检查硬盘健康信息。存在告警时先备份数据。", "disk-health")]
        };
        foreach (var step in steps)
        {
            _steps.Children.Add(ToolExperience.Text(step.Title, "SubtitleTextBlockStyle"));
            _steps.Children.Add(ToolExperience.Text(step.Help));
            _steps.Children.Add(ToolExperience.Button("Diagnostic_" + step.Tool, "打开工具", () => ToolExperience.OpenBuiltinAsync(step.Tool, XamlRoot)));
        }
    }
}
