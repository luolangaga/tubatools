using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class DashboardPage
{
    private bool? _lastNarrowLayout;

    private void DashboardViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width != e.PreviousSize.Width)
            UpdateCardLayout();
    }

    /// <summary>
    /// 按当前视口宽度重算页头与卡片网格。所有赋值都是幂等的，可以在布局更新时反复调用：
    /// ItemsPanelRoot 在部分时机（页面刚加载、面板被重建）还是 null，此时不能把宽度当成
    /// 已处理，否则面板出现后卡片仍停留在旧列宽，右侧就会空出一整列（窗口拉大后尤其明显）。
    /// </summary>
    private void UpdateCardLayout()
    {
        if (!_ready) return;
        // Use the page's viewport so the grid's header and item sizes cannot feed
        // back into the width used to calculate them.
        double width = DashboardViewport.ActualWidth;
        if (width <= 0) return;
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
        if (_lastNarrowLayout != narrow)
        {
            HeaderPanel.Margin = new Thickness(margin, narrow ? 20 : 28, margin, 24);
            TitleText.Style = (Style)Application.Current.Resources[narrow ? "TitleTextBlockStyle" : "TitleLargeTextBlockStyle"];
            GreetingText.Style = (Style)Application.Current.Resources[narrow ? "BodyTextBlockStyle" : "BodyLargeTextBlockStyle"];
            FeatureScroll.Padding = new Thickness(margin, 0, margin, 12);
            FilterBar.Margin = new Thickness(margin, 20, margin, 20);
            ToolsToolbar.Margin = new Thickness(margin, 0, margin, 12);
            _lastNarrowLayout = narrow;
        }
        ToolSearchBox.Width = Math.Min(240, Math.Max(120, width * .4));
        if (ToolsGrid.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            double available = Math.Max(1, width - margin * 2);
            int columns = Math.Max(1, (int)((available + 12) / (narrow ? 232 : 280)));
            // 槽宽含卡片右侧 12px 外边距；交给 FitItemWidth 留出取整余量，
            // 否则特定宽度下列宽会正好顶到边界，最后一整列被挤到下一行。
            double itemWidth = ToolCardLayout.FitItemWidth(available + 12, columns);
            var panelMargin = new Thickness(margin, 0, Math.Max(0, margin - 12), 28);
            if (panel.ItemWidth != itemWidth || panel.ItemHeight != 108 || panel.Margin != panelMargin)
            {
                panel.ItemWidth = itemWidth;
                panel.ItemHeight = 108;
                panel.Margin = panelMargin;
            }
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

