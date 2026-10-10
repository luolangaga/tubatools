using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Controls;

/// <summary>工具正文使用视口的有限宽度，避免长文本和可编辑选择框把滚动内容撑出窗口。</summary>
public sealed class ToolViewport : DependencyObject
{
    public static readonly DependencyProperty ConstrainContentProperty = DependencyProperty.RegisterAttached(
        "ConstrainContent", typeof(bool), typeof(ToolViewport), new PropertyMetadata(false, OnChanged));

    public static bool GetConstrainContent(DependencyObject target) => (bool)target.GetValue(ConstrainContentProperty);
    public static void SetConstrainContent(DependencyObject target, bool value) => target.SetValue(ConstrainContentProperty, value);

    private static void OnChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer) return;
        viewer.SizeChanged -= OnSizeChanged;
        viewer.Loaded -= OnLoaded;
        if (!(bool)args.NewValue) return;
        viewer.SizeChanged += OnSizeChanged;
        viewer.Loaded += OnLoaded;
        Update(viewer);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs args) => Update((ScrollViewer)sender);
    private static void OnLoaded(object sender, RoutedEventArgs args) => Update((ScrollViewer)sender);

    private static void Update(ScrollViewer viewer)
    {
        if (viewer.ActualWidth <= 0 || viewer.Content is not FrameworkElement content) return;
        var available = Math.Max(0, viewer.ActualWidth - viewer.Padding.Left - viewer.Padding.Right);
        content.Width = Math.Min(available, content.MaxWidth);
        content.HorizontalAlignment = HorizontalAlignment.Left;
    }
}
