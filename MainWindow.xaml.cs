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
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);

        ApplyTitleBarTheme(ElementTheme.Default);

        WindowSizeService.ApplySavedWindowSize(this);

        Closed += MainWindow_Closed;

        PopulateCategories();
        NavigateToDefaultPage();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        WindowSizeService.SaveWindowSize(this);
    }

    public void ApplyTitleBarTheme(ElementTheme theme)
    {
        var tb = AppWindow.TitleBar;
        var isDark = theme == ElementTheme.Dark ||
                     (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);

            tb.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
            tb.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
        }
        else
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);

            tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
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

    private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
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
                case "monitor":
                    if (!Services.LiteMonitorService.IsDriverReady())
                    {
                        var ok = await Services.LiteMonitorService.Instance.EnsureDriverAsync(Content.XamlRoot);
                        if (!ok) break;
                    }
                    NavFrame.Navigate(typeof(Pages.LiteMonitorPage), false);
                    break;
                case string category:
                    NavFrame.Navigate(typeof(HomePage), category);
                    break;
            }
        }

        ThemeService.ApplySavedTheme();
    }

    private void NavigateToDefaultPage()
    {
        var defaultPage = AppSettings.Get("DefaultPage") ?? "all";
        NavigationViewItem? targetItem = null;

        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag is string tag && tag == defaultPage)
            {
                targetItem = navItem;
                break;
            }
        }

        if (targetItem is not null)
        {
            NavView.SelectedItem = targetItem;
        }
        else
        {
            NavFrame.Navigate(typeof(HomePage), null);
        }
    }

    private void PopulateCategories()
    {
        while (NavView.MenuItems.Count > 5)
        {
            NavView.MenuItems.RemoveAt(5);
        }

        var categories = ToolCatalog.GetCategories();
        var otherCategory = categories.FirstOrDefault(c => c.Contains("其他"));
        var restCategories = categories.Where(c => !c.Contains("其他"));

        foreach (var category in restCategories)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = category.Replace("工具", ""),
                Tag = category,
                Icon = new FontIcon { Glyph = GetCategoryGlyph(category) }
            });
        }

        if (otherCategory != null)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = otherCategory.Replace("工具", ""),
                Tag = otherCategory,
                Icon = new FontIcon { Glyph = GetCategoryGlyph(otherCategory) }
            });
        }
    }

    private static string GetCategoryGlyph(string category)
    {
        var customGlyph = AppSettings.Get($"CategoryGlyph_{category}");
        if (!string.IsNullOrWhiteSpace(customGlyph))
            return customGlyph;

        if (category.Contains("处理器", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEEA1";
        }

        if (category.Contains("显卡", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uF211";
        }

        if (category.Contains("显示器", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE7F4";
        }

        if (category.Contains("硬盘", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEDA2";
        }

        if (category.Contains("内存", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEEA0";
        }

        if (category.Contains("外设", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE962";
        }

        if (category.Contains("游戏", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE7FC";
        }

        if (category.Contains("烤鸡", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE9D9";
        }

        if (category.Contains("声卡", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE7F5";
        }

        if (category.Contains("网卡", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEDA3";
        }

        if (category.Contains("综合", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEC4E";
        }

        if (category.Contains("其他", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE712";
        }

        return "\uE8B7";
    }

    public void RefreshToolCategories()
    {
        PopulateCategories();
    }
}
