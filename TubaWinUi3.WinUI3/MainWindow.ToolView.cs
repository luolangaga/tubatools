using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3;

public sealed partial class MainWindow
{
    /// <summary>Temporarily give a full-screen tool the titlebar, notices and navigation area.</summary>
    internal Action HideChromeForToolView()
    {
        var root = (Grid)Content;
        var chrome = root.Children.OfType<FrameworkElement>()
            .Where(element => Grid.GetRowSpan(element) == 1 && Grid.GetRow(element) < 2)
            .Select(element => (Element: element, element.Visibility)).ToArray();
        var heights = root.RowDefinitions.Take(2).Select(row => row.Height).ToArray();
        bool paneVisible = NavView.IsPaneVisible;
        foreach (var item in chrome) item.Element.Visibility = Visibility.Collapsed;
        foreach (var row in root.RowDefinitions.Take(2)) row.Height = new GridLength(0);
        NavView.IsPaneVisible = false;
        return () =>
        {
            foreach (var item in chrome) item.Element.Visibility = item.Visibility;
            for (int index = 0; index < heights.Length; index++) root.RowDefinitions[index].Height = heights[index];
            NavView.IsPaneVisible = paneVisible;
        };
    }
}
