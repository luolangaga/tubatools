using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        ApplyTitleBarTheme(ElementTheme.Default);

        PopulateCategories();
        NavFrame.Navigate(typeof(HomePage), null);
    }

    public void ApplyTitleBarTheme(ElementTheme theme)
    {
        var tb = AppWindow.TitleBar;
        var isDark = theme == ElementTheme.Dark ||
                     (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);
        }
        else
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
        }

        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "all":
                    NavFrame.Navigate(typeof(HomePage), null);
                    break;
                case "favorites":
                    NavFrame.Navigate(typeof(FavoritesPage));
                    break;
                case "hardware":
                    NavFrame.Navigate(typeof(HardwarePage));
                    break;
                case "builtin":
                    NavFrame.Navigate(typeof(BuiltinToolsPage));
                    break;
                case string category:
                    NavFrame.Navigate(typeof(HomePage), category);
                    break;
            }
        }

        ThemeService.ApplySavedTheme();
    }

    private void PopulateCategories()
    {
        foreach (var category in ToolCatalog.GetCategories())
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = category,
                Tag = category,
                Icon = new FontIcon { Glyph = GetCategoryGlyph(category) }
            });
        }
    }

    private static string GetCategoryGlyph(string category)
    {
        if (category.Contains("处理器", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE950";
        }

        if (category.Contains("显卡", StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains("显示器", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE7F4";
        }

        if (category.Contains("硬盘", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEDA2";
        }

        if (category.Contains("内存", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE965";
        }

        if (category.Contains("游戏", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE7FC";
        }

        if (category.Contains("烤鸡", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE945";
        }

        if (category.Contains("声卡", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE7F5";
        }

        if (category.Contains("网卡", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE968";
        }

        return "\uE8B7";
    }
}
