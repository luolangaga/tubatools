using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.PluginSdk;
using Windows.UI;

namespace Tubatools.IconStudio;

internal static class IconStudioPage
{
    public static FrameworkElement Create(PluginExecutionContext context) => new View(context).Root;

    private sealed class View
    {
        private readonly PluginExecutionContext _context;
        private readonly Border _root;
        private readonly TextBlock _stateText;
        private readonly StackPanel _presetPanel;
        private readonly RadioButtons _groups;
        private readonly AutoSuggestBox _search;
        private readonly TextBlock _countText;
        private readonly Button _resetAll;
        private readonly InfoBar _status;
        private readonly ListView _list;
        private readonly ProgressRing _ring;
        private readonly List<(PluginIconPreset Preset, Border Card, TextBlock Badge)> _presetCards = [];

        private IPluginIconService? _icons;
        private IReadOnlyList<PluginIconSlot> _slots = [];
        private PluginIconState? _state;
        private string _group = "builtin";
        private bool _busy;
        private bool _loaded;

        public FrameworkElement Root => _root;

        public View(PluginExecutionContext context)
        {
            _context = context;

            _stateText = new TextBlock { Text = "正在读取图标…", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
            var presetHeader = new StackPanel { Spacing = 6 };
            presetHeader.Children.Add(new TextBlock { Text = "全局预设", FontSize = 16, FontWeight = FontWeights.SemiBold });
            presetHeader.Children.Add(_stateText);

            _presetPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            BuildPresetCards();
            var presetScroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _presetPanel
            };
            var presetSection = new StackPanel { Spacing = 8 };
            presetSection.Children.Add(presetHeader);
            presetSection.Children.Add(presetScroller);

            _groups = new RadioButtons
            {
                MaxColumns = 3,
                VerticalAlignment = VerticalAlignment.Center
            };
            _groups.Items.Add("内置工具");
            _groups.Items.Add("侧栏菜单");
            _groups.Items.Add("消息弹窗");
            _groups.SelectedIndex = 0;
            AutomationProperties.SetName(_groups, "图标分组");

            _search = new AutoSuggestBox
            {
                PlaceholderText = "搜索名称或分类",
                Width = 240,
                QueryIcon = new SymbolIcon(Symbol.Find),
                VerticalAlignment = VerticalAlignment.Center
            };
            _search.TextChanged += (_, _) => RefreshList();

            _countText = new TextBlock { Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            _resetAll = new Button { Content = "全部恢复默认", VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(_resetAll, "恢复全部默认图标");
            _resetAll.Click += async (_, _) => await ResetAllAsync();

            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(_countText);
            right.Children.Add(_resetAll);

            var toolbar = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 14, 0, 10) };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.Children.Add(_groups);
            Grid.SetColumn(_search, 1);
            toolbar.Children.Add(_search);
            Grid.SetColumn(right, 2);
            toolbar.Children.Add(right);

            _status = new InfoBar
            {
                IsOpen = false,
                IsClosable = true,
                Severity = InfoBarSeverity.Informational,
                Margin = new Thickness(0, 0, 0, 8)
            };

            _list = new ListView
            {
                SelectionMode = ListViewSelectionMode.None,
                IsItemClickEnabled = false,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            _list.ItemContainerStyle = itemStyle;

            _ring = new ProgressRing
            {
                Width = 32,
                Height = 32,
                IsActive = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var listArea = new Grid();
            listArea.Children.Add(_list);
            listArea.Children.Add(_ring);

            var layout = new Grid { MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch, RowSpacing = 0 };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.Children.Add(presetSection);
            layout.Children.Add(toolbar);
            Grid.SetRow(toolbar, 1);
            layout.Children.Add(_status);
            Grid.SetRow(_status, 2);
            layout.Children.Add(listArea);
            Grid.SetRow(listArea, 3);

            _root = new Border
            {
                Padding = new Thickness(28, 14, 28, 24),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = layout
            };
            _root.Loaded += Root_Loaded;

            _groups.SelectionChanged += (_, _) =>
            {
                _group = _groups.SelectedIndex switch { 1 => "navigation", 2 => "dialog", _ => "builtin" };
                RefreshList();
            };
        }

        private async void Root_Loaded(object sender, RoutedEventArgs e)
        {
            _root.Loaded -= Root_Loaded;
            _loaded = true;
            _icons = _context.Services.GetService<IPluginIconService>();
            if (_icons is null)
            {
                SetBusy(false);
                ShowStatus("当前宿主没有提供图标服务，请更新图吧工具箱后再使用图标工坊。", InfoBarSeverity.Error);
                return;
            }

            SetBusy(true);
            try
            {
                await LoadCoreAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ShowStatus("读取图标失败：" + ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task LoadCoreAsync()
        {
            if (_icons is null) return;
            var state = await _icons.GetStateAsync(_context.CancellationToken);
            var slots = await _icons.GetSlotsAsync(_context.CancellationToken);
            _state = state;
            _slots = slots;
            _stateText.Text = $"当前：{PresetName(state.Preset)} · 自定义 {state.CustomCount} 处";
            UpdatePresetCards();
            RefreshList();
        }

        private async Task ReloadAsync() => await LoadCoreAsync();

        private async Task ChangeImageAsync(PluginIconSlot slot)
        {
            if (_icons is null || _busy) return;
            SetBusy(true);
            try
            {
                var path = await _context.Services.PickOpenFileAsync($"选择「{slot.Title}」的图标", _context.CancellationToken);
                if (string.IsNullOrWhiteSpace(path)) return;
                await _icons.SetImageAsync(slot.Group, slot.Id, path, _context.CancellationToken);
                await ReloadAsync();
                ShowStatus($"已更新「{slot.Title}」的图标。", InfoBarSeverity.Success);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ShowStatus($"「{slot.Title}」设置失败：{ex.Message}", InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task ResetSlotAsync(PluginIconSlot slot)
        {
            if (_icons is null || _busy) return;
            SetBusy(true);
            try
            {
                await _icons.ResetAsync(slot.Group, slot.Id, _context.CancellationToken);
                await ReloadAsync();
                ShowStatus($"「{slot.Title}」已恢复为当前预设的图标。", InfoBarSeverity.Success);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ShowStatus($"恢复「{slot.Title}」失败：{ex.Message}", InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task ResetAllAsync()
        {
            if (_icons is null || _busy) return;
            var custom = _state?.CustomCount ?? 0;
            var preset = _state?.Preset ?? PluginIconPreset.Default;
            if (custom == 0 && preset == PluginIconPreset.Default)
            {
                ShowStatus("现在已经是默认彩色图标，无需恢复。", InfoBarSeverity.Informational);
                return;
            }

            var message = custom > 0
                ? $"将清除 {custom} 处自定义图片并切回默认彩色预设，是否继续？"
                : "将切回默认彩色预设，是否继续？";
            if (!await ConfirmAsync("恢复全部默认图标", message)) return;

            SetBusy(true);
            try
            {
                await _icons.ResetAllAsync(_context.CancellationToken);
                await ReloadAsync();
                ShowStatus("已恢复全部默认图标。", InfoBarSeverity.Success);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ShowStatus("恢复默认失败：" + ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task ApplyPresetAsync(PluginIconPreset preset)
        {
            if (_icons is null || _busy) return;
            if (_state?.Preset == preset)
            {
                ShowStatus($"当前已经使用「{PresetName(preset)}」预设。", InfoBarSeverity.Informational);
                return;
            }

            var custom = _state?.CustomCount ?? 0;
            var message = custom > 0
                ? $"切换到「{PresetName(preset)}」会清除现有的 {custom} 处自定义图片，是否继续？"
                : $"切换到「{PresetName(preset)}」预设？";
            if (!await ConfirmAsync("切换全局图标预设", message)) return;

            SetBusy(true);
            try
            {
                await _icons.ApplyPresetAsync(preset, _context.CancellationToken);
                await ReloadAsync();
                ShowStatus($"已应用「{PresetName(preset)}」预设。", InfoBarSeverity.Success);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ShowStatus("应用预设失败：" + ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void BuildPresetCards()
        {
            _presetPanel.Children.Add(PresetCard(
                PluginIconPreset.Default, "默认彩色", "当前版本的彩色矢量图标",
                CreateIconVisual("ms-appx:///Assets/BuiltinIcons/cpu-ranking.svg", "\uE9D9", 40)));
            _presetPanel.Children.Add(PresetCard(
                PluginIconPreset.ClassicColor, "远古彩色", "早期版本的简易彩色线稿",
                CreateIconVisual("ms-appx:///Assets/IconStyles/Classic/Builtin/cpu-ranking.svg", "\uE9D9", 40)));
            _presetPanel.Children.Add(PresetCard(
                PluginIconPreset.FluentGlyph, "Fluent 字形", "Segoe Fluent Icons 字体字形",
                CreateIconVisual(null, "\uE977", 40)));
        }

        private Button PresetCard(PluginIconPreset preset, string title, string description, FrameworkElement preview)
        {
            var badge = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = AccentBrush(),
                Visibility = Visibility.Collapsed
            };
            var card = new Border
            {
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(8),
                Background = CardBrush(),
                BorderBrush = CardStrokeBrush(),
                BorderThickness = new Thickness(1),
                MinWidth = 238
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(preview);
            var text = new StackPanel { Spacing = 4 };
            text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 160 });
            text.Children.Add(badge);
            row.Children.Add(text);
            card.Child = row;

            var button = new Button
            {
                Content = card,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0)
            };
            AutomationProperties.SetName(button, $"应用「{title}」预设");
            button.Click += async (_, _) => await ApplyPresetAsync(preset);
            _presetCards.Add((preset, card, badge));
            return button;
        }

        private void UpdatePresetCards()
        {
            var preset = _state?.Preset ?? PluginIconPreset.Default;
            foreach (var (item, card, badge) in _presetCards)
            {
                var selected = item == preset;
                card.BorderBrush = selected ? AccentBrush() : CardStrokeBrush();
                card.BorderThickness = new Thickness(selected ? 2 : 1);
                badge.Text = selected ? "使用中" : "";
                badge.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshList()
        {
            if (!_loaded || _state is null) return;
            var query = _search.Text.Trim();
            var items = _slots
                .Where(s => GroupKey(s.Group) == _group)
                .Where(s => query.Length == 0 ||
                            s.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                            s.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Select(CreateSlotRow)
                .ToList();
            _countText.Text = $"{items.Count} 项";
            _list.ItemsSource = items.Count == 0
                ? [new TextBlock
                {
                    Text = "没有匹配的图标位置。",
                    Opacity = 0.6,
                    Margin = new Thickness(8, 24, 8, 24),
                    HorizontalAlignment = HorizontalAlignment.Center
                }]
                : items;
        }

        private FrameworkElement CreateSlotRow(PluginIconSlot slot)
        {
            var icon = CreateIconVisual(PreviewPath(slot), slot.Glyph, 32);
            var title = new TextBlock { Text = slot.Title, FontSize = 14, FontWeight = FontWeights.SemiBold };
            var subtitle = new TextBlock
            {
                Text = slot.Subtitle,
                FontSize = 12,
                Opacity = 0.65,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(title);
            text.Children.Add(subtitle);

            var status = new TextBlock
            {
                Text = slot.IsCustom ? "已自定义" : "预设",
                FontSize = 12,
                Foreground = slot.IsCustom ? AccentBrush() : null,
                Opacity = slot.IsCustom ? 1 : 0.55,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            };

            var change = new Button
            {
                Content = "换图片",
                FontSize = 12,
                Padding = new Thickness(12, 6, 12, 6)
            };
            AutomationProperties.SetName(change, $"更换「{slot.Title}」的图标");
            change.Click += async (_, _) => await ChangeImageAsync(slot);

            var reset = new Button
            {
                Content = "恢复默认",
                FontSize = 12,
                Padding = new Thickness(12, 6, 12, 6),
                IsEnabled = slot.IsCustom
            };
            AutomationProperties.SetName(reset, $"恢复「{slot.Title}」的默认图标");
            reset.Click += async (_, _) => await ResetSlotAsync(slot);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(change);
            buttons.Children.Add(reset);

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(icon);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Grid.SetColumn(status, 2);
            grid.Children.Add(status);
            Grid.SetColumn(buttons, 3);
            grid.Children.Add(buttons);

            var row = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 4),
                CornerRadius = new CornerRadius(8),
                Background = CardBrush(),
                BorderBrush = CardStrokeBrush(),
                BorderThickness = new Thickness(1),
                Child = grid,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            ToolTipService.SetToolTip(row, slot.IsCustom ? $"{slot.Title}\n自定义图片：{slot.CurrentImagePath}" : slot.Subtitle);
            return row;
        }

        private string? PreviewPath(PluginIconSlot slot)
        {
            if (!string.IsNullOrWhiteSpace(slot.CurrentImagePath)) return slot.CurrentImagePath;
            return _state?.Preset switch
            {
                PluginIconPreset.ClassicColor => slot.ClassicImagePath,
                PluginIconPreset.FluentGlyph => null,
                _ => slot.DefaultImagePath
            };
        }

        private static FrameworkElement CreateIconVisual(string? path, string glyph, double size)
        {
            var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            var icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = size * 0.75,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var image = new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Visibility = Visibility.Collapsed
            };
            if (!string.IsNullOrWhiteSpace(path) && TryImageSource(path) is { } source)
            {
                image.Source = source;
                image.Visibility = Visibility.Visible;
                image.ImageOpened += (_, _) => icon.Visibility = Visibility.Collapsed;
                image.ImageFailed += (_, _) =>
                {
                    image.Visibility = Visibility.Collapsed;
                    icon.Visibility = Visibility.Visible;
                };
            }
            grid.Children.Add(icon);
            grid.Children.Add(image);
            return grid;
        }

        private static ImageSource? TryImageSource(string path)
        {
            try
            {
                var uri = path.StartsWith("ms-appx", StringComparison.OrdinalIgnoreCase) ||
                          path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(path)
                    : new Uri(Path.GetFullPath(path));
                return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                    ? new SvgImageSource(uri)
                    : new BitmapImage(uri);
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> ConfirmAsync(string title, string message)
        {
            try
            {
                return await _context.ConfirmAsync(title, message);
            }
            catch (NotSupportedException) { }
            catch (Exception)
            {
                return false;
            }

            try
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    PrimaryButtonText = "继续",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = _root.XamlRoot
                };
                return await dialog.ShowAsync() == ContentDialogResult.Primary;
            }
            catch
            {
                return false;
            }
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            _status.Title = severity switch
            {
                InfoBarSeverity.Success => "已完成",
                InfoBarSeverity.Warning => "请注意",
                InfoBarSeverity.Error => "操作失败",
                _ => "提示"
            };
            _status.Message = message;
            _status.Severity = severity;
            _status.IsOpen = true;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _groups.IsEnabled = !busy;
            _search.IsEnabled = !busy;
            _resetAll.IsEnabled = !busy;
            _list.IsEnabled = !busy;
            _presetPanel.IsHitTestVisible = !busy;
            _ring.IsActive = busy;
        }

        private static string PresetName(PluginIconPreset preset) => preset switch
        {
            PluginIconPreset.ClassicColor => "远古彩色",
            PluginIconPreset.FluentGlyph => "Fluent 字形",
            _ => "默认彩色"
        };

        private static string GroupKey(PluginIconGroup group) => group switch
        {
            PluginIconGroup.Navigation => "navigation",
            PluginIconGroup.Dialog => "dialog",
            _ => "builtin"
        };

        private static Brush? ThemeBrush(string key)
        {
            try { return Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null; }
            catch { return null; }
        }

        private static Brush CardBrush() => ThemeBrush("CardBackgroundFillColorDefaultBrush")
            ?? new SolidColorBrush(Color.FromArgb(20, 128, 128, 128));

        private static Brush CardStrokeBrush() => ThemeBrush("CardStrokeColorDefaultBrush")
            ?? new SolidColorBrush(Color.FromArgb(48, 128, 128, 128));

        private static Brush AccentBrush() => ThemeBrush("AccentTextFillColorPrimaryBrush")
            ?? ThemeBrush("AccentFillColorDefaultBrush")
            ?? new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
    }
}
