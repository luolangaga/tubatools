using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Services;

/// <summary>Constrain dialog content in DIPs, keeping native buttons outside the scroll area.</summary>
internal sealed class FluentDialogLayout
{
    private static readonly ConditionalWeakTable<ContentDialog, FluentDialogLayout> Instances = new();
    private readonly ContentDialog _dialog;
    private readonly Dictionary<FrameworkElement, (double Min, double Max)> _widths = new();
    private readonly Dictionary<FrameworkElement, double> _heights = new();
    private XamlRoot? _root;
    private double _preferredWidth;

    public static void Attach(ContentDialog dialog) => Instances.GetValue(dialog, d => new(d));

    private FluentDialogLayout(ContentDialog dialog)
    {
        _dialog = dialog;
        dialog.Loaded += OnLoaded;
        dialog.Opened += (_, _) => Update();
        dialog.Closed += (_, _) => DetachRoot();
        dialog.Unloaded += (_, _) => DetachRoot();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        DetachRoot();
        _root = _dialog.XamlRoot;
        if (_root is not null) _root.Changed += RootChanged;
        if (_preferredWidth == 0)
        {
            _preferredWidth = _dialog.Resources.TryGetValue("ContentDialogMaxWidth", out var width)
                && width is double w ? w : 600;
            if (_dialog.Content is FrameworkElement content)
            {
                // Fixed-width forms previously overflowed the native 548-DIP surface.
                var desired = double.IsNaN(content.Width) ? content.MinWidth : content.Width;
                _preferredWidth = Math.Max(_preferredWidth, desired + 48);
            }
        }
        if (_dialog.Title is string && _dialog.TitleTemplate is null)
            _dialog.TitleTemplate = Application.Current.Resources["FluentDialogTitleTemplate"] as DataTemplate;
        Update();
    }

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Update();

    private void DetachRoot()
    {
        if (_root is not null) _root.Changed -= RootChanged;
        _root = null;
    }

    private void Update()
    {
        if (_root is null || _root.Size.Width <= 0 || _root.Size.Height <= 0) return;
        var width = Math.Max(1, Math.Min(_preferredWidth, _root.Size.Width - 32));
        var height = Math.Max(1, _root.Size.Height - 32);
        _dialog.Resources["ContentDialogMinWidth"] = Math.Min(280d, width);
        _dialog.Resources["ContentDialogMaxWidth"] = width;
        _dialog.Resources["ContentDialogMaxHeight"] = height;
        // ContentDialog itself spans the host to position the modal and smoke layer.
        // Limit only BackgroundElement; constraining the control shifts the modal left.
        // Keep the stock template and its command row. Its scrolling is disabled by
        // default, which makes tall forms unreachable on small windows.
        if (Find(_dialog, "ContentScrollViewer") is ScrollViewer viewer)
        {
            viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            viewer.VerticalScrollMode = ScrollMode.Auto;
            viewer.HorizontalScrollMode = ScrollMode.Disabled;
        }
        if (Find(_dialog, "BackgroundElement") is FrameworkElement surface)
        {
            surface.MinWidth = Math.Min(280, width);
            surface.MaxWidth = width;
            surface.MaxHeight = height;
        }
        if (_dialog.Content is FrameworkElement content)
            Constrain(content, Math.Max(1, width - 48), Math.Max(1, height - 180));
    }

    private void Constrain(FrameworkElement element, double width, double height)
    {
        if (!_widths.TryGetValue(element, out var original))
            _widths[element] = original = (element.MinWidth, element.MaxWidth);
        element.MinWidth = Math.Min(original.Min, width);
        element.MaxWidth = Math.Min(original.Max, width);
        // The native template already scrolls ordinary content. Bound inner scrolling
        // surfaces as well, rather than wrapping a virtualized ListView in another viewer.
        if (element is ScrollViewer or ListViewBase)
        {
            if (!_heights.TryGetValue(element, out var max)) _heights[element] = max = element.MaxHeight;
            element.MaxHeight = Math.Min(max, height);
        }
        if (element is TextBlock text && text.ReadLocalValue(TextBlock.TextWrappingProperty) == DependencyProperty.UnsetValue)
            text.TextWrapping = TextWrapping.Wrap;
        // Do not enter collection item templates or control templates: their own layout
        // and virtualization must keep control of measurements.
        if (element is Panel panel)
            foreach (var child in panel.Children.OfType<FrameworkElement>()) Constrain(child, width, height);
        else if (element is Border { Child: FrameworkElement child }) Constrain(child, width, height);
        else if (element is ScrollViewer { Content: FrameworkElement body }) Constrain(body, width, height);
    }

    private static FrameworkElement? Find(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.Name == name) return element;
            if (Find(child, name) is { } match) return match;
        }
        return null;
    }
}
