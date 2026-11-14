using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.PluginSdk;
using TubaWinUi3.Services;
using Tubatools.NexBoxSkin.GameBox;

namespace Tubatools.NexBoxSkin;

internal static class NexBoxSkinRuntime
{
    public const string EnabledKey = "NexBoxSkin.Enabled";
    public const string HintKey = "NexBoxSkin.HintShown";

    private const string CardTag = "NexBoxSkin.SettingsCard";
    private const string ExitTag = "NexBoxSkin.ExitButton";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IPluginStartupContext? _context;
    private static IPluginHostAccess? _host;
    private static GameBoxHost? _skinHost;
    private static Button? _exitButton;
    private static FrameworkElement? _settingsCard;
    private static FrameworkElement? _hiddenTitleSearch;
    private static Visibility _titleSearchVisibility;
    private static bool _applied;
    private static bool _enabled;

    public static bool IsEnabled => _enabled;

    public static event Action<bool>? StateChanged;

    public static async Task StartAsync(IPluginStartupContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _host = context.HostAccess;
        _enabled = await ReadBoolAsync(context, EnabledKey, true, cancellationToken);
        var hintShown = await ReadBoolAsync(context, HintKey, false, cancellationToken);
        try
        {
            DisplayFilterService.Configure(context.DataDirectory);
        }
        catch
        {
            // The filter is optional; a corrupt settings file must not block the skin.
        }

        if (_host is not null)
        {
            if (_host.NavigationFrame is Frame frame)
            {
                frame.Navigated += OnFrameNavigated;
            }

            await _host.RunOnUiThreadAsync(() =>
            {
                InjectSettingsCard();
                return Task.CompletedTask;
            }, cancellationToken);
        }

        if (!hintShown)
        {
            await ShowHintAsync(cancellationToken);
        }

        if (_enabled)
        {
            await ApplyAsync(cancellationToken);
            DisplayFilterService.ApplyPersisted();
        }
    }

    public static async Task StopAsync()
    {
        if (_host?.NavigationFrame is Frame frame)
        {
            frame.Navigated -= OnFrameNavigated;
        }

        if (_host is not null)
        {
            await _host.RunOnUiThreadAsync(() =>
            {
                RemoveSettingsCard();
                return Task.CompletedTask;
            }, CancellationToken.None);
        }

        await RestoreAsync(CancellationToken.None);
        _context = null;
        _host = null;
        _enabled = false;
    }

    public static async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("皮肤插件尚未初始化。");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            _enabled = enabled;
            await context.SetSettingAsync(EnabledKey, enabled ? "true" : "false", cancellationToken);
            StateChanged?.Invoke(enabled);

            if (enabled)
            {
                await ApplyAsync(cancellationToken);
                DisplayFilterService.ApplyPersisted();
            }
            else
            {
                await RestoreAsync(cancellationToken);
                DisplayFilterService.RestoreAll();
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (_applied || _host is null)
        {
            return;
        }

        await _host.RunOnUiThreadAsync(async () =>
        {
            if (_applied) return;
            if (_host.RootVisual is not Grid root)
            {
                throw new InvalidOperationException("主窗口界面不可用。");
            }

            if (root.ColumnDefinitions.Count > 0 || root.RowDefinitions.Count < 3)
            {
                throw new InvalidOperationException("主界面布局已被其他皮肤修改，请先恢复默认外观再启用 NexBox 皮肤。");
            }

            if (_host.NavigationView is not NavigationView navigation)
            {
                throw new InvalidOperationException("主窗口导航视图不可用。");
            }

            var skin = GameBoxHost.CreateSkin(() => root.XamlRoot, "/");
            Grid.SetRow(skin, 2);
            Grid.SetColumnSpan(skin, Math.Max(1, root.ColumnDefinitions.Count));
            Canvas.SetZIndex(skin, 50);
            root.Children.Add(skin);
            navigation.Visibility = Visibility.Collapsed;
            HideHostSearch(root);
            AddExitButton(root);

            _skinHost = skin;
            _applied = true;
            try
            {
                await skin.InitializeAsync();
            }
            catch
            {
                RestoreCore();
                throw;
            }
        }, cancellationToken);
    }

    private static async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (_host is null) return;
        await _host.RunOnUiThreadAsync(() =>
        {
            RestoreCore();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    private static void RestoreCore()
    {
        if (_skinHost is { } skin)
        {
            skin.CloseWebView();
            if (skin.Parent is Panel parent)
            {
                parent.Children.Remove(skin);
            }

            _skinHost = null;
        }

        if (_host?.NavigationView is NavigationView navigation)
        {
            navigation.Visibility = Visibility.Visible;
        }

        RestoreHostSearch();
        RemoveExitButton();
        _applied = false;
        DisplayFilterService.RestoreAll();
    }

    private static void HideHostSearch(DependencyObject root)
    {
        if (_hiddenTitleSearch is not null) return;

        FrameworkElement? search = null;

        // The search box lives in the TitleBar control's Content property, not in the
        // normal visual tree until the template is applied; walk the content explicitly.
        var titleBar = FindDescendant<TitleBar>(root, "AppTitleBar");
        if (titleBar?.Content is FrameworkElement content)
        {
            search = content.FindName("TitleSearchArea") as FrameworkElement
                     ?? FindDescendant<FrameworkElement>(content, "TitleSearchArea");
            if (search is null)
            {
                var searchBox = content.FindName("SearchBox") as FrameworkElement
                                ?? FindDescendant<FrameworkElement>(content, "SearchBox");
                search = searchBox?.Parent as FrameworkElement ?? searchBox;
            }
        }

        search ??= FindDescendant<FrameworkElement>(root, "TitleSearchArea");
        if (search is null) return;

        _hiddenTitleSearch = search;
        _titleSearchVisibility = search.Visibility;
        search.Visibility = Visibility.Collapsed;
    }

    private static void RestoreHostSearch()
    {
        if (_hiddenTitleSearch is not { } search) return;
        search.Visibility = _titleSearchVisibility;
        _hiddenTitleSearch = null;
    }

    private static void AddExitButton(DependencyObject root)
    {
        if (_exitButton is not null) return;

        StackPanel? actions = null;
        var titleBar = FindDescendant<TitleBar>(root, "AppTitleBar");
        if (titleBar?.Content is FrameworkElement content)
        {
            actions = content.FindName("TitleActionButtons") as StackPanel
                      ?? FindDescendant<StackPanel>(content, "TitleActionButtons");
        }
        actions ??= FindDescendant<StackPanel>(root, "TitleActionButtons");

        if (actions is not null)
        {
            var button = new Button
            {
                Content = "经典界面",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(6, 0, 0, 0),
                BorderThickness = new Thickness(0),
                Background = null,
                Tag = ExitTag,
            };
            ToolTipService.SetToolTip(button, "退出 NexBox 皮肤，恢复经典界面");
            button.Click += OnExitClick;
            actions.Children.Add(button);
            _exitButton = button;
            return;
        }

        // Fallback: the title bar layout may differ in a future host build. Keep an
        // always-visible escape hatch over the skin so the user can never get stuck.
        if (root is Grid grid)
        {
            var floating = new Button
            {
                Content = "退出 NexBox 皮肤",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 12, 12, 0),
                Padding = new Thickness(12, 5, 12, 5),
                Tag = ExitTag,
            };
            ToolTipService.SetToolTip(floating, "退出 NexBox 皮肤，恢复经典界面");
            floating.Click += OnExitClick;
            Grid.SetRow(floating, 2);
            Canvas.SetZIndex(floating, 60);
            grid.Children.Add(floating);
            _exitButton = floating;
        }
    }

    private static void RemoveExitButton()
    {
        if (_exitButton is not { } button) return;
        button.Click -= OnExitClick;
        if (button.Parent is Panel parent)
        {
            parent.Children.Remove(button);
        }

        _exitButton = null;
    }

    private static void OnExitClick(object sender, RoutedEventArgs e)
    {
        _ = SetEnabledAsync(false, CancellationToken.None);
    }

    private static void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (e.SourcePageType.Name == "SettingsPage")
        {
            InjectSettingsCard();
        }
    }

    private static void InjectSettingsCard()
    {
        if (_host?.CurrentPage is not FrameworkElement page || page.GetType().Name != "SettingsPage")
        {
            return;
        }

        if (page.FindName("AppearanceExpander") is not Panel panel)
        {
            return;
        }

        if (panel.Children.OfType<FrameworkElement>().Any(child => (string?)child.Tag == CardTag))
        {
            return;
        }

        var card = new Border
        {
            Tag = CardTag,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 6, 0, 0),
        };
        if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var background) && background is Brush backgroundBrush)
        {
            card.Background = backgroundBrush;
        }
        if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var stroke) && stroke is Brush strokeBrush)
        {
            card.BorderBrush = strokeBrush;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = "NexBox 皮肤", FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = "默认开启，可在此关闭。开启后主界面切换为 NexBox 风格。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
        });
        grid.Children.Add(text);

        var toggle = new ToggleSwitch
        {
            IsOn = IsEnabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);

        toggle.Toggled += async (_, _) =>
        {
            try
            {
                await SetEnabledAsync(toggle.IsOn, CancellationToken.None);
            }
            catch (Exception ex)
            {
                toggle.IsOn = IsEnabled;
                await ShowMessageAsync("NexBox 皮肤", ex.Message);
            }
        };

        Action<bool> stateHandler = value =>
        {
            _ = _host?.RunOnUiThreadAsync(() =>
            {
                if (toggle.IsOn != value) toggle.IsOn = value;
                return Task.CompletedTask;
            });
        };
        StateChanged += stateHandler;
        card.Unloaded += (_, _) =>
        {
            StateChanged -= stateHandler;
            if (ReferenceEquals(_settingsCard, card)) _settingsCard = null;
        };

        card.Child = grid;
        panel.Children.Add(card);
        _settingsCard = card;
    }

    private static void RemoveSettingsCard()
    {
        if (_settingsCard is { } card)
        {
            if (card.Parent is Panel parent)
            {
                parent.Children.Remove(card);
            }

            _settingsCard = null;
            return;
        }

        if (_host?.CurrentPage is not FrameworkElement page || page.GetType().Name != "SettingsPage") return;
        if (page.FindName("AppearanceExpander") is not Panel panel) return;
        foreach (var child in panel.Children.OfType<FrameworkElement>().Where(c => (string?)c.Tag == CardTag).ToArray())
        {
            panel.Children.Remove(child);
        }
    }

    private static async Task ShowHintAsync(CancellationToken cancellationToken)
    {
        if (_host is null || _context is null) return;

        await _host.RunOnUiThreadAsync(() =>
        {
            if (_host.RootVisual is not FrameworkElement root) return Task.CompletedTask;
            var host = FindDescendant<FrameworkElement>(root, "ToolUpdateToast")?.Parent as Panel;
            var message = IsEnabled
                ? "NexBox 皮肤已启用。可点击标题栏「经典界面」按钮，或在「设置 → 外观和布局」中随时关闭。"
                : "NexBox 皮肤当前未启用，可在「设置 → 外观和布局」中重新开启。";

            var bar = new InfoBar
            {
                Title = "NexBox 皮肤",
                Message = message,
                Severity = InfoBarSeverity.Informational,
                IsOpen = true,
                IsClosable = true,
                Margin = new Thickness(12, 4, 12, 4),
            };
            var action = new Button { Content = "关闭皮肤" };
            action.Click += async (_, _) =>
            {
                bar.IsOpen = false;
                try
                {
                    await SetEnabledAsync(false, CancellationToken.None);
                }
                catch
                {
                    // Ignore; the settings card and title-bar button remain available.
                }
            };
            bar.ActionButton = action;

            if (host is not null)
            {
                host.Children.Add(bar);
            }
            else if (root is Grid grid)
            {
                Grid.SetRow(bar, 1);
                Grid.SetColumnSpan(bar, Math.Max(1, grid.ColumnDefinitions.Count));
                grid.Children.Add(bar);
            }

            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var timer = dispatcher?.CreateTimer();
            if (timer is not null)
            {
                timer.Interval = TimeSpan.FromSeconds(12);
                timer.IsRepeating = false;
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        if (host is not null) host.Children.Remove(bar);
                        else if (root is Grid parent) parent.Children.Remove(bar);
                    }
                    catch
                    {
                        // Ignore; the page may have been recreated.
                    }
                };
                timer.Start();
            }

            return Task.CompletedTask;
        }, cancellationToken);

        await _context.SetSettingAsync(HintKey, "true", cancellationToken);
    }

    private static async Task ShowMessageAsync(string title, string message)
    {
        if (_host?.RootVisual is not FrameworkElement root || root.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "知道了",
            XamlRoot = root.XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        try
        {
            await dialog.ShowAsync();
        }
        catch
        {
            // Ignore dialog failures.
        }
    }

    private static async Task<bool> ReadBoolAsync(IPluginStartupContext context, string key, bool fallback, CancellationToken cancellationToken)
    {
        try
        {
            var value = await context.GetSettingAsync(key, cancellationToken);
            return bool.TryParse(value, out var parsed) ? parsed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T element && element.Name == name)
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            if (FindDescendant<T>(VisualTreeHelper.GetChild(root, index), name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
