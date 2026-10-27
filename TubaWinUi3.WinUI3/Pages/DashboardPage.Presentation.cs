using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Pages;

public sealed partial class DashboardPage
{
    private void ToolsGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateCardLayout();

    private void UpdateCardLayout()
    {
        if (!_ready || ToolsGrid.ActualWidth <= 0) return;
        double width = ToolsGrid.ActualWidth;
        bool narrow = width < 720;
        double margin = narrow ? 20 : 36;
        ContentStack.Width = width;
        // Extend the quiet left side in layout; uniformly scale the complete artwork.
        HeroPanel.Height = Math.Clamp(width / 3.6, narrow ? 160 : 220, 320);
        HeroArtwork.Width = HeroLeftBlend.Width = Math.Min(width, HeroPanel.Height * 2.4);
        HeroArtwork.Height = HeroPanel.Height;
        // Keep the raised row clear of the title in short, narrow headers.
        FeatureStrip.Margin = new Thickness(0, -Math.Min(112, Math.Max(0, HeroPanel.Height - 112)), 0, 0);
        double artworkOffset = Math.Min(100, Math.Min(width * .08, width - HeroArtwork.Width));
        HeroArtwork.Margin = HeroLeftBlend.Margin = HeroRightBlend.Margin = new Thickness(0, 0, artworkOffset, 0);
        HeroRightBlend.Width = Math.Min(80, HeroArtwork.Width * .12);
        HeaderPanel.Margin = new Thickness(margin, narrow ? 20 : 28, margin, 24);
        TitleText.Style = (Style)Application.Current.Resources[narrow ? "TitleTextBlockStyle" : "TitleLargeTextBlockStyle"];
        GreetingText.Style = (Style)Application.Current.Resources[narrow ? "BodyTextBlockStyle" : "BodyLargeTextBlockStyle"];
        FeatureScroll.Padding = new Thickness(margin, 0, margin, 12);
        foreach (FrameworkElement tile in FeaturesPanel.Children) { tile.Width = 232; tile.Height = 172; }
        FeatureStrip.Visibility = _features.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        FilterBar.Margin = new Thickness(margin, 20, margin, 20);
        ToolsToolbar.Margin = new Thickness(margin, 0, margin, 12);
        ToolSearchBox.Width = Math.Min(240, Math.Max(120, width * .4));
        if (ToolsGrid.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            double available = Math.Max(1, width - margin * 2);
            int columns = Math.Max(1, (int)((available + 12) / (narrow ? 232 : 280)));
            panel.ItemWidth = (available + 12) / columns;
            panel.ItemHeight = 108;
            panel.Margin = new Thickness(margin, 0, Math.Max(0, margin - 12), 28);
        }
        UpdateFeatureButtons();
    }

    private void FeatureScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) => UpdateFeatureButtons();
    private void UpdateFeatureButtons()
    {
        bool overflow = FeatureScroll.ScrollableWidth > 1;
        FeaturePrevious.Visibility = FeatureNext.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        FeaturePrevious.IsEnabled = FeatureScroll.HorizontalOffset > 1;
        FeatureNext.IsEnabled = FeatureScroll.HorizontalOffset < FeatureScroll.ScrollableWidth - 1;
    }
    private void FeaturePrevious_Click(object sender, RoutedEventArgs e) => FeatureScroll.ChangeView(Math.Max(0, FeatureScroll.HorizontalOffset - Math.Max(244, FeatureScroll.ViewportWidth - 244)), null, null);
    private void FeatureNext_Click(object sender, RoutedEventArgs e) => FeatureScroll.ChangeView(Math.Min(FeatureScroll.ScrollableWidth, FeatureScroll.HorizontalOffset + Math.Max(244, FeatureScroll.ViewportWidth - 244)), null, null);
}
