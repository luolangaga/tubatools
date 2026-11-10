using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

public sealed partial class StressTestControl
{
    private bool _openingInspectionHwInfo;
    private TextBlock? _inspectionMonitorHint;
    private Button? _inspectionMonitorButton;
    private TextBlock? _inspectionGpuLoad;
    // A compact view over the existing runner: same monitoring, stop, log and report paths.
    // Inspection presets never overwrite the standalone tool's saved settings.
    private void BuildInspectionLayout()
    {
        _settingsReady = false;
        ApplyFurMarkSettings(new FurMarkSettings { Fullscreen = false });
        CpuDurationBox.Value = GpuDurationBox.Value = 5;
        _settingsReady = true;

        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var options = new Grid { ColumnSpacing = 8 };
        options.ColumnDefinitions.Add(new ColumnDefinition());
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var box in new[] { CpuSelBox, GpuSelBox })
        {
            Detach(box); box.MinWidth = 0;
            box.Content = box == CpuSelBox ? "CPU" : "GPU";
            choices.Children.Add(box);
        }
        options.Children.Add(choices);
        _inspectionDuration = new ComboBox { Header = "测试时长", MinWidth = 100 };
        foreach (var minutes in new[] { 5, 15, 30 }) _inspectionDuration.Items.Add(new ComboBoxItem { Content = $"{minutes} 分钟", Tag = minutes });
        _inspectionDuration.SelectedIndex = 0;
        _inspectionDuration.SelectionChanged += (_, _) =>
        {
            if (_inspectionDuration.SelectedItem is ComboBoxItem { Tag: int minutes })
                CpuDurationBox.Value = GpuDurationBox.Value = minutes;
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_inspectionDuration, "InspectionStressDuration");
        Grid.SetColumn(_inspectionDuration, 1); options.Children.Add(_inspectionDuration);
        root.Children.Add(options);

        var gpuOptions = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        gpuOptions.ColumnDefinitions.Add(new ColumnDefinition());
        gpuOptions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        gpuOptions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gpuOptions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Detach(StressGpuBox);
        Detach(MonitorGpuBox);
        Detach(MsaaBox);
        MsaaBox.Header = "抗锯齿 (MSAA)";
        gpuOptions.Children.Add(StressGpuBox);
        Grid.SetColumn(MsaaBox, 1);
        gpuOptions.Children.Add(MsaaBox);
        Grid.SetRow(MonitorGpuBox, 1);
        Grid.SetColumnSpan(MonitorGpuBox, 2);
        gpuOptions.Children.Add(MonitorGpuBox);
        Grid.SetRow(gpuOptions, 1);
        root.Children.Add(gpuOptions);

        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var button in new[] { StartStressBtn, StopBtn, ExportBtn })
        {
            Detach(button); button.Padding = new Thickness(10, 6, 10, 6);
            commands.Children.Add(button);
        }
        Grid.SetRow(commands, 2); root.Children.Add(commands);

        var monitor = new Grid { ColumnSpacing = 8 };
        monitor.ColumnDefinitions.Add(new ColumnDefinition());
        monitor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _inspectionMonitorHint = (TextBlock)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<TextBlock xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' FontSize='12' TextWrapping='Wrap' Foreground='{ThemeResource TextFillColorSecondaryBrush}' />");
        _inspectionMonitorHint.Text = "内置传感器可能存在读数错误或不支持部分设备，建议打开 HWMonitor 查看温度、功耗和频率；开始烤机时仍会自动打开 HWiNFO。";
        monitor.Children.Add(_inspectionMonitorHint);
        _inspectionMonitorButton = new Button { Content = "打开 HWiNFO", VerticalAlignment = VerticalAlignment.Top };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_inspectionMonitorButton, "InspectionOpenHwInfo");
        _inspectionMonitorButton.Click += async (_, _) => await OpenInspectionHwInfoAsync();
        Grid.SetColumn(_inspectionMonitorButton, 1); monitor.Children.Add(_inspectionMonitorButton);
        Grid.SetRow(monitor, 3); root.Children.Add(monitor);
        var hwButton = new Button { Content = "打开 HWMonitor" };
        hwButton.Click += OpenHwMonitor_Click;
        commands.Children.Add(hwButton);
        var metrics = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var i = 0; i < 4; i++) metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metrics.ColumnDefinitions.Add(new ColumnDefinition()); metrics.ColumnDefinitions.Add(new ColumnDefinition());
        var gpuLoad = new TextBlock { Text = "--", Style = GpuTempText.Style };
        _inspectionGpuLoad = gpuLoad;
        (string Label, TextBlock Value)[] values = [
            ("CPU 温度", CpuTempText), ("GPU 温度", GpuTempText),
            ("CPU 负载", CpuUsageText), ("GPU 负载", gpuLoad),
            ("CPU 功耗", CpuPowerText), ("GPU 功耗", GpuPowerText),
            ("已运行", ElapsedText)];
        for (var i = 0; i < values.Length; i++)
        {
            var (label, value) = values[i]; Detach(value); value.FontSize = 18;
            var stack = new StackPanel { Spacing = 2 };
            var caption = (TextBlock)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<TextBlock xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' FontSize='12' Foreground='{ThemeResource TextFillColorSecondaryBrush}' />");
            caption.Text = label;
            stack.Children.Add(caption);
            stack.Children.Add(value);
            Grid.SetColumn(stack, i % 2); Grid.SetRow(stack, i / 2); metrics.Children.Add(stack);
        }
        Grid.SetRow(metrics, 4); root.Children.Add(metrics);
        Detach(LogScrollViewer);
        LogScrollViewer.Height = 96;
        Grid.SetRow(LogScrollViewer, 5);
        root.Children.Add(LogScrollViewer);
        Content = new ScrollViewer { Content = root, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }

    private async Task OpenInspectionHwInfoAsync(bool onlyWhileRunning = false)
    {
        if (_openingInspectionHwInfo) return;
        _openingInspectionHwInfo = true;
        if (_inspectionMonitorButton is { } openingButton) openingButton.IsEnabled = false;
        try
        {
            // Catalog selection includes the existing x64 / ARM64 / x86 preference rules.
            var tools = await ToolCatalog.GetAllToolsAsync();
            if (!IsLoaded || (onlyWhileRunning && !_isRunning)) return;
            var tool = tools.FirstOrDefault(t => !t.IsBuiltinLink &&
                new[] { "HWiNFO", "HWiNFO32", "HWiNFO64", "HWiNFO_ARM64" }
                    .Contains(Path.GetFileNameWithoutExtension(t.EffectivePath), StringComparer.OrdinalIgnoreCase));
            string message;
            if (tool is null || !File.Exists(tool.EffectivePath))
                message = "未找到 HWiNFO，请先在“综合检测”中下载该工具，再点击“打开 HWiNFO”。本页监控数据可能不完整。";
            else
            {
                var running = false;
                foreach (var name in new[] { "HWiNFO", "HWiNFO32", "HWiNFO64", "HWiNFO_ARM64" })
                {
                    var processes = Process.GetProcessesByName(name);
                    running |= processes.Length > 0;
                    foreach (var process in processes) process.Dispose();
                }
                if (!running) ToolProcessLauncher.Launch(tool.EffectivePath, tool.EffectiveWorkingDir);
                message = running
                    ? "HWiNFO 已在运行，请切换到其“传感器 / Sensors”窗口查看监控数据。本页读数仅供参考。"
                    : "已打开 HWiNFO，请进入“传感器 / Sensors”窗口查看监控数据。本页读数仅供参考。";
            }
            if (_inspectionMonitorHint is { } hint) hint.Text = message;
            Log(message);
        }
        catch (Exception ex)
        {
            var message = $"HWiNFO 启动失败：{ex.Message}。可点击“打开 HWiNFO”重试，本页监控数据可能不完整。";
            if (_inspectionMonitorHint is { } hint) hint.Text = message;
            Log(message);
        }
        finally
        {
            _openingInspectionHwInfo = false;
            if (_inspectionMonitorButton is { } button) button.IsEnabled = true;
        }
    }

    private async void OpenHwMonitor_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button != null) button.IsEnabled = false;
        try
        {
            var tools = await ToolCatalog.GetAllToolsAsync();
            var tool = tools.FirstOrDefault(t => !t.IsBuiltinLink &&
                Path.GetFileNameWithoutExtension(t.EffectivePath).StartsWith("HWMonitor", StringComparison.OrdinalIgnoreCase));
            if (tool == null || !File.Exists(tool.EffectivePath))
                throw new FileNotFoundException("未找到 HWMonitor，请先在工具箱中下载该工具。");
            ToolProcessLauncher.Launch(tool.EffectivePath, tool.EffectiveWorkingDir);
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "无法打开 HWMonitor", Content = ex.Message, CloseButtonText = "知道了" };
            await dialog.ShowAsync();
        }
        finally { if (button != null) button.IsEnabled = true; }
    }

    private void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
        else if (element.Parent is Border border) border.Child = null;
        else RemoveFromLayout(Content as DependencyObject, element);
    }

    // Before Loaded, FrameworkElement.Parent can be null even for XAML-owned elements.
    // Traverse the logical content here so each reused control has exactly one owner.
    private static bool RemoveFromLayout(DependencyObject? root, UIElement element)
    {
        if (root is Panel panel)
        {
            if (panel.Children.Contains(element)) { panel.Children.Remove(element); return true; }
            foreach (var child in panel.Children.ToArray())
                if (RemoveFromLayout(child, element)) return true;
        }
        else if (root is Border border)
        {
            if (border.Child == element) { border.Child = null; return true; }
            return RemoveFromLayout(border.Child, element);
        }
        else if (root is ContentControl content)
        {
            if (ReferenceEquals(content.Content, element)) { content.Content = null; return true; }
            return RemoveFromLayout(content.Content as DependencyObject, element);
        }
        return false;
    }
}
