using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.PluginSdk;
using Windows.UI;

namespace Tubatools.ClassicTheme;

internal static class ClassicThemePage
{
    public static FrameworkElement Create(PluginExecutionContext context) => new View(context).Root;

    private sealed class View
    {
        private readonly PluginExecutionContext _context;
        private readonly ToggleSwitch _toggle;
        private readonly TextBlock _status;
        private bool _initializing;

        public FrameworkElement Root { get; }

        public View(PluginExecutionContext context)
        {
            _context = context;

            var title = new TextBlock { Text = "经典样式", FontSize = 28, FontWeight = FontWeights.SemiBold };
            var subtitle = new TextBlock
            {
                Text = "把主界面换成经典蓝色渐变：深蓝侧栏、浅蓝内容区、半透明白色卡片与白色文字。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            };

            var header = new StackPanel { Spacing = 6 };
            header.Children.Add(title);
            header.Children.Add(subtitle);

            var preview = BuildPreview();

            _toggle = new ToggleSwitch
            {
                OnContent = "已启用",
                OffContent = "已关闭",
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(_toggle, "启用经典样式");
            _toggle.Toggled += Toggle_Toggled;

            var toggleText = new StackPanel { Spacing = 4 };
            toggleText.Children.Add(new TextBlock { Text = "启用经典蓝色主题", FontSize = 16, FontWeight = FontWeights.SemiBold });
            toggleText.Children.Add(new TextBlock
            {
                Text = "立即应用到主窗口，并在下次启动时自动生效。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

            var toggleGrid = new Grid { ColumnSpacing = 18 };
            toggleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toggleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toggleGrid.Children.Add(toggleText);
            Grid.SetColumn(_toggle, 1);
            toggleGrid.Children.Add(_toggle);
            var toggleCard = new Border
            {
                Padding = new Thickness(18),
                CornerRadius = new CornerRadius(8),
                Background = ThemeBrush("CardBackgroundFillColorDefaultBrush") ?? new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush") ?? new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                BorderThickness = new Thickness(1),
                Child = toggleGrid
            };

            var applyButton = new Button { Content = "应用经典样式" };
            applyButton.Click += async (_, _) => await SetEnabledAsync(true);
            var resetButton = new Button { Content = "恢复默认外观" };
            resetButton.Click += async (_, _) => await SetEnabledAsync(false);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(applyButton);
            buttons.Children.Add(resetButton);

            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
            var note = new TextBlock
            {
                Text = "主题只在插件启用时生效；停用或卸载插件会恢复默认外观。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.6,
                FontSize = 12
            };

            var panel = new StackPanel
            {
                Spacing = 18,
                Margin = new Thickness(28, 20, 28, 28),
                MaxWidth = 960,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            panel.Children.Add(header);
            panel.Children.Add(preview);
            panel.Children.Add(toggleCard);
            panel.Children.Add(buttons);
            panel.Children.Add(_status);
            panel.Children.Add(note);

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };
            Root = scroll;
            Root.Loaded += Root_Loaded;
        }

        private async void Root_Loaded(object sender, RoutedEventArgs e)
        {
            Root.Loaded -= Root_Loaded;
            if (_context.Services.GetService<IPluginHostAccess>() is null)
            {
                _toggle.IsEnabled = false;
                _status.Text = "当前插件没有 fullTrust 权限，无法修改主界面。";
                return;
            }

            try
            {
                var value = await _context.Services.GetSettingAsync(ClassicThemeRuntime.EnabledKey, _context.CancellationToken);
                _initializing = true;
                _toggle.IsOn = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                _initializing = false;
                UpdateStatus();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "读取设置失败：" + ex.Message; }
        }

        private async void Toggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            await SetEnabledAsync(_toggle.IsOn);
        }

        private async Task SetEnabledAsync(bool enabled)
        {
            _toggle.IsEnabled = false;
            _initializing = true;
            _toggle.IsOn = enabled;
            _initializing = false;
            try
            {
                await ClassicThemeRuntime.SetEnabledAsync(enabled, _context.Services, _context.CancellationToken);
                UpdateStatus();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _status.Text = "应用失败：" + ex.Message;
                _initializing = true;
                _toggle.IsOn = !enabled;
                _initializing = false;
            }
            finally { _toggle.IsEnabled = true; }
        }

        private void UpdateStatus()
        {
            _status.Text = ClassicThemeRuntime.IsApplied
                ? "已启用经典样式：深蓝侧栏、浅蓝内容区、半透明白色卡片。"
                : "当前使用工具箱默认外观。";
            if (ClassicThemeRuntime.LastError is { Length: > 0 } error)
                _status.Text += "\n" + error;
        }

        private static Border BuildPreview()
        {
            var sidebar = new Border { Background = ClassicThemePalette.NavGradient() };
            var sidebarPanel = new StackPanel { Spacing = 8, Padding = new Thickness(16, 18, 16, 16) };
            for (var i = 0; i < 4; i++)
                sidebarPanel.Children.Add(new Border
                {
                    Height = 5,
                    Width = i == 0 ? 58 : 46,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Color.FromArgb((byte)(i == 0 ? 230 : 110), 255, 255, 255))
                });
            sidebar.Child = sidebarPanel;

            var card1 = new Border
            {
                Height = 54,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(ClassicThemePalette.CardFill),
                BorderBrush = new SolidColorBrush(ClassicThemePalette.CardStroke),
                BorderThickness = new Thickness(1)
            };
            var card2 = new Border
            {
                Height = 54,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(ClassicThemePalette.CardFill),
                BorderBrush = new SolidColorBrush(ClassicThemePalette.CardStroke),
                BorderThickness = new Thickness(1)
            };
            var content = new StackPanel { Spacing = 10, Padding = new Thickness(18), VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(card1);
            content.Children.Add(card2);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(sidebar);
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);

            return new Border
            {
                Height = 190,
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Background = ClassicThemePalette.RootGradient(),
                Child = grid
            };
        }

        private static Brush? ThemeBrush(string key)
        {
            try { return Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null; }
            catch { return null; }
        }
    }
}
