// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. Adapted from microsoft/WinUI-Gallery Controls/HomePage/Tile.xaml.cs.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Controls;

public sealed partial class DashboardGalleryTile : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DashboardGalleryTile), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(DashboardGalleryTile), new PropertyMetadata(""));
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(object), typeof(DashboardGalleryTile), new PropertyMetadata(null));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public HyperlinkButton ActionButton => OpenButton;
    public event RoutedEventHandler? Click;

    public DashboardGalleryTile()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateCompactLayout();
    }
    private void OpenButton_Click(object sender, RoutedEventArgs e) => Click?.Invoke(this, e);

    private void UpdateCompactLayout()
    {
        bool compact = ActualWidth < 190 || ActualHeight < 156;
        BodyGrid.Padding = new Thickness(compact ? 12 : 24);
        BodyGrid.RowSpacing = compact ? 8 : 12;
        BodyGrid.RowDefinitions[0].Height = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(48);
        BodyGrid.RowDefinitions[1].Height = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        DescriptionText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        WordsPanel.Margin = new Thickness(0, 0, compact ? 16 : 0, 0);
        LogoBox.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        LogoBox.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
        LogoBox.Width = LogoBox.Height = compact ? Math.Max(16, Math.Min(ActualWidth - 32, ActualHeight - 52) * 0.9) : 48;
    }
}
