using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 新手教程页面：左侧原生 NavigationView 按分类列出全部内置工具的教程导航，
/// 右侧渲染纯文字步骤教程，支持一键打开对应工具边看边练。
/// </summary>
public sealed partial class NewbieTutorialPage : Page
{
    private const string HomeTag = "__home__";

    public NewbieTutorialPage()
    {
        InitializeComponent();
        BuildNavigation();
    }

    // ------------------------------------------------------------------
    // 导航构建
    // ------------------------------------------------------------------

    private void BuildNavigation()
    {
        var home = new NavigationViewItem
        {
            Content = "教程首页",
            Tag = HomeTag,
            Icon = new FontIcon { Glyph = "\uE80F" }
        };
        TutorialNav.MenuItems.Add(home);

        // 重点工具置顶分组，方便新手快速找到高频场景
        var featured = new NavigationViewItem
        {
            Content = "新手必看",
            Tag = "__group_featured__",
            Icon = new FontIcon { Glyph = "\uE735" }
        };
        foreach (var id in new[] { "stress-test", "quick-device-check", "context-menu-mgr", "startup-manager" })
        {
            var tool = BuiltinToolRegistry.GetById(id);
            if (tool is not null)
                featured.MenuItems.Add(MakeToolItem(tool));
        }
        TutorialNav.MenuItems.Add(featured);

        foreach (var category in BuiltinToolRegistry.GetCategories())
        {
            var group = new NavigationViewItem
            {
                Content = category,
                Tag = $"__group_{category}__",
                Icon = new FontIcon { Glyph = "\uE8FD" }
            };
            foreach (var tool in BuiltinToolRegistry.GetByCategory(category))
                group.MenuItems.Add(MakeToolItem(tool));
            if (group.MenuItems.Count > 0)
                TutorialNav.MenuItems.Add(group);
        }

        TutorialNav.SelectedItem = home;
    }

    private static NavigationViewItem MakeToolItem(IBuiltinTool tool) => new()
    {
        Content = tool.Name,
        Tag = tool.Id,
        Icon = ToolIcon(tool, 16)
    };

    /// <summary>内置工具图标：有彩色矢量图标就用它，否则回退字体字形。</summary>
    private static IconElement ToolIcon(IBuiltinTool tool, double size) =>
        BuiltinIconService.Get(tool.Id) is { } source
            ? new ImageIcon { Source = source, Width = size, Height = size }
            : new FontIcon { Glyph = tool.Glyph, FontSize = size };

    // ------------------------------------------------------------------
    // 内容渲染
    // ------------------------------------------------------------------

    private void TutorialNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;
        if (tag == HomeTag)
        {
            RenderHome();
            return;
        }
        var tool = BuiltinToolRegistry.GetById(tag);
        if (tool is not null) RenderTool(tool);
    }

    private void RenderHome()
    {
        var host = ContentHost;
        host.Children.Clear();

        host.Children.Add(new TextBlock
        {
            Text = "欢迎使用图吧工具箱CE",
            FontSize = 26,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        host.Children.Add(new TextBlock
        {
            Text = "这里是新手教程：左侧按分类列出了工具箱内置的全部工具，每个教程都包含完整操作步骤与注意事项。\n" +
                   "建议先从「新手必看」的四个高频场景开始——烤机、验机、右键菜单、启动项；遇到不确定的工具随时回来查。",
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 24
        });

        host.Children.Add(BuildSectionHeader("三步上手"));
        AddStepCard(host, "1", "找到工具", "主界面左侧选择分类（或用顶部搜索框搜索工具名），点击工具卡片打开。");
        AddStepCard(host, "2", "跟着教程操作", "在本教程窗口中选择想学的工具，按步骤操作；教程里可以一键打开对应工具边看边练。");
        AddStepCard(host, "3", "查看注意事项", "每个教程末尾都附有关键注意事项，操作前先读一遍，避免误操作。");

        host.Children.Add(BuildSectionHeader("高频工具直达"));
        var wrap = new StackPanel { Spacing = 8 };
        foreach (var id in new[] { "stress-test", "quick-device-check", "context-menu-mgr", "startup-manager", "junk-cleaner", "performance-benchmark" })
        {
            var tool = BuiltinToolRegistry.GetById(id);
            if (tool is null) continue;
            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 10, 14, 10)
            };
            btn.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    ToolIcon(tool, 16),
                    new TextBlock { Text = tool.Name, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock
                    {
                        Text = tool.Description,
                        FontSize = 12,
                        MaxLines = 1,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(ThemeColors.DimText)
                    }
                }
            };
            var captured = id;
            btn.Click += (_, _) => NavigateToToolTutorial(captured);
            wrap.Children.Add(btn);
        }
        host.Children.Add(wrap);
    }

    private void RenderTool(IBuiltinTool tool)
    {
        var host = ContentHost;
        host.Children.Clear();
        ContentScroll.ScrollToVerticalOffset(0);

        var tutorial = TutorialCatalog.Get(tool);

        // 头部：图标 + 名称 + 分类 + 简介 + 打开工具按钮
        var header = new StackPanel { Spacing = 6 };
        header.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                ToolIcon(tool, 22),
                new TextBlock { Text = tool.Name, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center }
            }
        });
        header.Children.Add(new TextBlock
        {
            Text = $"分类：{tool.Category} · 类型：{KindLabel(tool.Kind)}",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        header.Children.Add(new TextBlock
        {
            Text = tutorial.Intro,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 24,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText)
        });
        host.Children.Add(header);

        var openBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE768", FontSize = 14 },
                    new TextBlock { Text = $"打开「{tool.Name}」边看边练", VerticalAlignment = VerticalAlignment.Center }
                }
            },
            Padding = new Thickness(16, 8, 16, 8)
        };
        openBtn.Click += (_, _) => LaunchTool(tool);
        host.Children.Add(openBtn);

        // 步骤
        host.Children.Add(BuildSectionHeader("操作步骤"));
        for (int i = 0; i < tutorial.Steps.Count; i++)
        {
            var step = tutorial.Steps[i];
            AddStepCard(host, (i + 1).ToString(), step.Title, step.Body);
        }

        // 注意事项
        host.Children.Add(BuildSectionHeader("注意事项"));
        host.Children.Add(new Border
        {
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new FontIcon { Glyph = "\uE7BA", FontSize = 16, Foreground = new SolidColorBrush(ThemeColors.AccentOrange) },
                    new TextBlock
                    {
                        Text = tutorial.Tips,
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        });
    }

    // ------------------------------------------------------------------
    // 操作：打开工具 / 教程跳转
    // ------------------------------------------------------------------

    private async void LaunchTool(IBuiltinTool tool)
    {
        try
        {
            // 强制独立窗口：页面型工具在新窗口打开，不占用主界面
            using var scope = BuiltinToolWindow.ForceWindowScope();
            MainWindow.ActiveToolName = tool.Name;
            await tool.ExecuteAsync(new BuiltinToolContext { XamlRoot = XamlRoot });
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "打开工具失败",
                Content = ex.Message,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            }.ShowAsync();
        }
    }

    private void NavigateToToolTutorial(string toolId)
    {
        var item = FindNavItem(TutorialNav.MenuItems, toolId);
        if (item is not null) item.IsSelected = true;
    }

    private static NavigationViewItem? FindNavItem(IList<object> items, string tag)
    {
        foreach (var obj in items)
        {
            if (obj is not NavigationViewItem nvi) continue;
            if (Equals(nvi.Tag, tag)) return nvi;
            var child = FindNavItem(nvi.MenuItems, tag);
            if (child is not null) return child;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // 通用小部件
    // ------------------------------------------------------------------

    private static FrameworkElement BuildSectionHeader(string text) => new TextBlock
    {
        Text = text,
        FontSize = 17,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 10, 0, 0)
    };

    private static void AddStepCard(StackPanel host, string number, string title, string body)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12)
        };
        card.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };

        var badge = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(ThemeColors.AccentBlue),
            VerticalAlignment = VerticalAlignment.Top
        };
        badge.Child = new TextBlock
        {
            Text = number,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var textCol = new StackPanel { Spacing = 4 };
        textCol.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        textCol.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText)
        });

        (card.Child as StackPanel)!.Children.Add(badge);
        (card.Child as StackPanel)!.Children.Add(textCol);
        host.Children.Add(card);
    }

    private static string KindLabel(BuiltinToolKind kind) => kind switch
    {
        BuiltinToolKind.Dialog => "界面工具",
        BuiltinToolKind.ProgressTask => "扫描/清理任务",
        BuiltinToolKind.BackgroundTask => "后台任务",
        _ => "即时操作"
    };
}
