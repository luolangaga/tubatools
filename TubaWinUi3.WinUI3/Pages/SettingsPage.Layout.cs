using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Pages;

public sealed partial class SettingsPage
{
    private readonly List<(Grid Grid, FrameworkElement Action, int Row, int Column, int Span,
        HorizontalAlignment Alignment)> _settingsActionRows = [];
    private bool? _settingsNarrow;
    private bool _settingsInitialPositioned;

    private void InitializeSettingsLayout()
    {
        Loaded += (_, _) =>
        {
            if (_settingsInitialPositioned) return;
            _settingsInitialPositioned = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_highlightCts is null)
                {
                    SettingsSectionPicker.Focus(FocusState.Programmatic);
                    RootScrollViewer.ChangeView(null, 0, null, true);
                }
            });
        };
        // Complex expanded editors use the same stacked action layout as SettingsCard.
        // Cache original positions once so resizing never progressively alters the layout.
        foreach (var grid in SettingsDescendants(ContentPanel).OfType<Grid>())
        {
            if (grid.ColumnDefinitions.Count < 3 || grid.RowDefinitions.Count > 0) continue;
            var actions = grid.Children.OfType<FrameworkElement>()
                .Where(child => Grid.GetColumn(child) == 2).ToArray();
            if (actions.Length != 1) continue;
            var action = actions[0];
            _settingsActionRows.Add((grid, action, Grid.GetRow(action), Grid.GetColumn(action),
                Grid.GetColumnSpan(action), action.HorizontalAlignment));
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        // The URL field used to overlap its description in the same grid cell.
        var urlGrid = HttpDownloadUrlTextBox.Parent as Grid;
        if (urlGrid is not null)
        {
            urlGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            urlGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            urlGrid.RowSpacing = 8;
            Grid.SetRow(HttpDownloadUrlTextBox, 1);
            HttpDownloadUrlTextBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private static IEnumerable<DependencyObject> SettingsDescendants(DependencyObject parent)
    {
        // XAML content is available before templates have been applied.
        IEnumerable<DependencyObject> children = parent switch
        {
            Panel panel => panel.Children.Cast<DependencyObject>(),
            Border border when border.Child is not null => [border.Child],
            SettingsCard card => new[] { card.Header as DependencyObject, card.Content as DependencyObject }
                .OfType<DependencyObject>(),
            SettingsExpander expander => expander.Items.OfType<DependencyObject>(),
            ContentControl control when control.Content is DependencyObject content => [content],
            _ => [],
        };
        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in SettingsDescendants(child)) yield return descendant;
        }
    }

    private void SettingsContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 760;
        ContentPanel.Padding = narrow ? new Thickness(16, 20, 16, 32) : new Thickness(32, 24, 32, 40);
        if (_settingsNarrow == narrow) return;
        _settingsNarrow = narrow;
        foreach (var entry in _settingsActionRows)
        {
            Grid.SetRow(entry.Action, narrow ? 1 : entry.Row);
            Grid.SetColumn(entry.Action, narrow ? 1 : entry.Column);
            Grid.SetColumnSpan(entry.Action, narrow ? 2 : entry.Span);
            entry.Action.HorizontalAlignment = narrow ? HorizontalAlignment.Left : entry.Alignment;
            entry.Grid.RowSpacing = narrow ? 8 : 0;
        }
        AppIconImage.Width = AppIconImage.Height = narrow ? 56 : 72;
        foreach (var input in SettingsDescendants(ContentPanel).OfType<Control>())
        {
            if (input is TextBox or ComboBox or PasswordBox)
            {
                if (input.MinWidth > 200) input.MinWidth = 120;
                input.MaxWidth = narrow ? 260 : 360;
            }
        }
    }

    private void SettingsSection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsSectionPicker.SelectedItem is ComboBoxItem { Tag: string name } &&
            FindName(name) is FrameworkElement section)
        {
            ContentPanel.UpdateLayout();
            var position = section.TransformToVisual(ContentPanel).TransformPoint(new Windows.Foundation.Point());
            RootScrollViewer.ChangeView(null, Math.Max(0, position.Y - ContentPanel.Padding.Top), null,
                Services.FastModeService.IsFastModeEnabled());
        }
    }

    private static void ExpandSettingsCard(FrameworkElement card)
    {
        if (card is Border { Child: SettingsExpander expander }) expander.IsExpanded = true;
    }

    private async void SettingsWebsite_Click(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://tubawinui3.cn"));

    private async void SettingsRepository_Click(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/luolangaga/tubatool"));

    private void SettingsWhatsNew_Click(object sender, RoutedEventArgs e) => WhatsNewWindow.Show();
}
