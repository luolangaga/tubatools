using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class ServiceCenterPage : Page
{
    private void ServiceWorkspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 880;
        ServiceWorkspace.ColumnDefinitions[0].Width = wide ? new GridLength(220) : new GridLength(1, GridUnitType.Star);
        ServiceWorkspace.ColumnDefinitions[1].Width = new GridLength(wide ? 1 : 0);
        ServiceWorkspace.ColumnDefinitions[2].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ServiceWorkspace.RowDefinitions[0].Height = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(180);
        ServiceWorkspace.RowDefinitions[1].Height = wide ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ServiceDivider.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(ServiceContent, wide ? 2 : 0);
        Grid.SetRow(ServiceContent, wide ? 0 : 1);
    }

    private ServiceCenterBrand? _currentBrand;
    private Button? _selectedButton;

    public ServiceCenterPage()
    {
        InitializeComponent();
        // 代码构建的画刷不会随主题自动刷新，切换主题后重渲染选中态
        ActualThemeChanged += (_, _) =>
        {
            if (_selectedButton is not null)
                UpdateSelectionVisual(_selectedButton);
        };

        PopulateNavList();
    }

    private void PopulateNavList()
    {
        LaptopList.Children.Clear();
        DesktopList.Children.Clear();
        AccessoryList.Children.Clear();

        foreach (var brand in ServiceCenterService.GetLaptopBrands())
            AddBrandButton(brand, LaptopList);

        foreach (var brand in ServiceCenterService.GetDesktopBrands())
            AddBrandButton(brand, DesktopList);

        foreach (var brand in ServiceCenterService.GetAccessoryBrands())
            AddBrandButton(brand, AccessoryList);
    }

    private void AddBrandButton(ServiceCenterBrand brand, StackPanel container)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 8, 12, 8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Tag = brand,
            Style = Application.Current.Resources["SubtleButtonStyle"] as Style,
            CornerRadius = new CornerRadius(4)
        };

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconPanel = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };

        var logoImage = new Image
        {
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform
        };

        if (!string.IsNullOrEmpty(brand.LogoUrl))
        {
            try
            {
                logoImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(brand.LogoUrl));
            }
            catch { }
        }

        iconPanel.Child = logoImage;

        var nameText = new TextBlock
        {
            Text = brand.Name,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };

        grid.Children.Add(iconPanel);
        Grid.SetColumn(iconPanel, 0);
        grid.Children.Add(nameText);
        Grid.SetColumn(nameText, 1);

        btn.Content = grid;
        btn.Click += BrandButton_Click;

        container.Children.Add(btn);
    }

    private async void BrandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ServiceCenterBrand brand)
        {
            if (_currentBrand?.Id == brand.Id)
                return;

            _currentBrand = brand;
            UpdateSelectionVisual(btn);

            UrlText.Text = brand.ServiceUrl;
            OpenExternalBtn.Visibility = Visibility.Visible;

            LoadingBrandText.Text = brand.Name;

            EmptyState.Visibility = Visibility.Collapsed;
            LoadingState.Visibility = Visibility.Visible;
            LoadingState.Opacity = 1;
            WebView.Visibility = Visibility.Collapsed;

            try
            {
                await WebView.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());
                WebView.CoreWebView2.Navigate(brand.ServiceUrl);
            }
            catch
            {
                UrlText.Text = "WebView2 加载失败，请点击右侧链接在浏览器中打开";
            }

            await Task.Delay(300);
            LoadingState.Opacity = 0;
            await Task.Delay(200);
            LoadingState.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Visible;
        }
    }

    private void UpdateSelectionVisual(Button selectedBtn)
    {
        static void ClearSelection(StackPanel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Button btn)
                {
                    btn.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            }
        }

        ClearSelection(LaptopList);
        ClearSelection(DesktopList);
        ClearSelection(AccessoryList);

        if (selectedBtn is not null)
        {
            selectedBtn.Background = new SolidColorBrush(ThemeColors.SubtleBg);
        }

        _selectedButton = selectedBtn;
    }

    private void OpenExternalBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentBrand is not null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _currentBrand.ServiceUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
