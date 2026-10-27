using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Controls;

public sealed partial class StressTestControl
{
    // A compact view over the existing runner: same monitoring, stop, log and report paths.
    // Inspection presets never overwrite the standalone tool's saved settings.
    private void BuildInspectionLayout()
    {
        _settingsReady = false;
        ApplyFurMarkSettings(new FurMarkSettings { Fullscreen = false });
        ApplyNetSettings(new NetSettings { DataRateMbps = 10, DurationMinutes = 5, TargetModeIndex = 0 });
        CpuDurationBox.Value = GpuDurationBox.Value = NetDurationBox.Value = 5;
        _settingsReady = true;

        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var options = new Grid { ColumnSpacing = 8 };
        options.ColumnDefinitions.Add(new ColumnDefinition());
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var box in new[] { CpuSelBox, GpuSelBox, NetSelBox })
        {
            Detach(box); box.MinWidth = 0;
            box.Content = box == CpuSelBox ? "CPU" : box == GpuSelBox ? "GPU" : "网卡";
            choices.Children.Add(box);
        }
        options.Children.Add(choices);
        _inspectionDuration = new ComboBox { Header = "测试时长", MinWidth = 100 };
        foreach (var minutes in new[] { 5, 15, 30 }) _inspectionDuration.Items.Add(new ComboBoxItem { Content = $"{minutes} 分钟", Tag = minutes });
        _inspectionDuration.SelectedIndex = 0;
        _inspectionDuration.SelectionChanged += (_, _) =>
        {
            if (_inspectionDuration.SelectedItem is ComboBoxItem { Tag: int minutes })
                CpuDurationBox.Value = GpuDurationBox.Value = NetDurationBox.Value = minutes;
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_inspectionDuration, "InspectionStressDuration");
        Grid.SetColumn(_inspectionDuration, 1); options.Children.Add(_inspectionDuration);
        root.Children.Add(options);

        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var button in new[] { StartStressBtn, StopBtn, ExportBtn })
        {
            Detach(button); button.Padding = new Thickness(10, 6, 10, 6);
            commands.Children.Add(button);
        }
        Grid.SetRow(commands, 1); root.Children.Add(commands);
        var metrics = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var i = 0; i < 4; i++) metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metrics.ColumnDefinitions.Add(new ColumnDefinition()); metrics.ColumnDefinitions.Add(new ColumnDefinition());
        (string Label, TextBlock Value)[] values = [
            ("CPU 温度", CpuTempText), ("GPU 温度", GpuTempText),
            ("CPU 负载", CpuUsageText), ("已运行", ElapsedText),
            ("CPU 功耗", CpuPowerText), ("GPU 功耗", GpuPowerText),
            ("网卡发送", NetTxRateText), ("网卡接收", NetRxRateText)];
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
        Grid.SetRow(metrics, 2); root.Children.Add(metrics);
        // Bounded log is the only scrollable region; settings and readings stay visible.
        Detach(LogScrollViewer); LogScrollViewer.Height = double.NaN; LogScrollViewer.MinHeight = 24;
        Grid.SetRow(LogScrollViewer, 3); root.Children.Add(LogScrollViewer);
        Content = root;
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
