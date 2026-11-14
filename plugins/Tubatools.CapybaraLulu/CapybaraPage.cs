using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using TubaWinUi3.PluginSdk;
using Windows.UI;

namespace Tubatools.CapybaraLulu;

/// <summary>美化包的“控制台”：一张预览卡片 + 每个可以放噜噜的位置一个开关。</summary>
internal static class CapybaraPage
{
    internal sealed class View
    {
        private readonly ToggleSwitch _master = NewToggle();
        private readonly ToggleSwitch _background = NewToggle();
        private readonly ToggleSwitch _bgMotion = NewToggle();
        private readonly ToggleSwitch _sidebar = NewToggle();
        private readonly ToggleSwitch _corner = NewToggle();
        private readonly ToggleSwitch _speech = NewToggle();
        private readonly ToggleSwitch _bubble = NewToggle();
        private readonly ToggleSwitch _titleBar = NewToggle();
        private readonly ToggleSwitch _hero = NewToggle();
        private readonly ToggleSwitch _hardware = NewToggle();
        private readonly ToggleSwitch _animations = NewToggle();
        private readonly ComboBox _theme = new() { MinWidth = 190, VerticalAlignment = VerticalAlignment.Center };
        private readonly ComboBox _side = new() { MinWidth = 140, VerticalAlignment = VerticalAlignment.Center };
        private readonly ComboBox _interval = new() { MinWidth = 140, VerticalAlignment = VerticalAlignment.Center };
        private readonly Slider _opacity = new()
        {
            Minimum = 8,
            Maximum = 65,
            StepFrequency = 1,
            Width = 220,
            VerticalAlignment = VerticalAlignment.Center,
        };
        private readonly TextBlock _opacityText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 42 };
        private readonly Slider _mascotSize = new()
        {
            Minimum = 92,
            Maximum = 190,
            StepFrequency = 2,
            Width = 220,
            VerticalAlignment = VerticalAlignment.Center,
        };
        private readonly TextBlock _mascotSizeText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 52 };
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
        private readonly TextBlock _previewSay;
        private readonly Border _preview;
        private readonly DispatcherQueueTimer? _saveTimer;
        private bool _initializing;

        public FrameworkElement Root { get; }

        public View()
        {
            _previewSay = new TextBlock
            {
                Text = "今天也要好好折腾电脑噜～",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 150,
            };
            _preview = BuildPreview(_previewSay);

            var icon = new Image
            {
                Source = TryLoad("icon.png"),
                Width = 64,
                Height = 64,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var title = new TextBlock { Text = "水豚噜噜美化包", FontSize = 28, FontWeight = FontWeights.SemiBold };
            var subtitle = new TextBlock
            {
                Text = "给图吧工具箱换一身软乎乎的外衣。所有装饰都由插件在运行时叠加，主程序一个字节都没改；关掉开关就原样还回去。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72,
                MaxWidth = 680,
            };
            var tag = new Border
            {
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(9, 3, 9, 3),
                BorderThickness = new Thickness(1),
                BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush") ?? new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                Background = ThemeBrush("SubtleFillColorSecondaryBrush") ?? new SolidColorBrush(Color.FromArgb(16, 128, 128, 128)),
                Child = new TextBlock { Text = "粉丝自制 · 仅本机使用", FontSize = 11, Opacity = 0.7 },
                VerticalAlignment = VerticalAlignment.Center,
            };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            titleRow.Children.Add(title);
            titleRow.Children.Add(tag);
            var headerText = new StackPanel { Spacing = 5 };
            headerText.Children.Add(titleRow);
            headerText.Children.Add(subtitle);
            var header = new Grid { ColumnSpacing = 16 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.Children.Add(icon);
            Grid.SetColumn(headerText, 1);
            header.Children.Add(headerText);

            var masterText = new TextBlock
            {
                Text = "启用美化包",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
            };
            var masterDesc = new TextBlock
            {
                Text = "打开时会先列出要叠上去的装饰，确认后才生效；关闭后立刻还原默认外观（背景、图标、侧栏与角落装饰都会退场），设置仍然保留。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            };
            var masterStack = new StackPanel { Spacing = 4 };
            masterStack.Children.Add(masterText);
            masterStack.Children.Add(masterDesc);
            var masterRow = new Grid { ColumnSpacing = 16 };
            masterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            masterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            masterRow.Children.Add(masterStack);
            Grid.SetColumn(_master, 1);
            masterRow.Children.Add(_master);

            var backgroundPanel = new StackPanel { Spacing = 12 };
            backgroundPanel.Children.Add(Row("柔和背景", "把噜噜的插画铺在内容区后面；插件会临时隐藏你原来的背景图，关掉开关后恢复。", _background));
            backgroundPanel.Children.Add(Row("背景主题", "「自动」按日期在澡堂、草地、书桌之间轮换；深色主题自动用云端晚安。", _theme));
            var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            opacityRow.Children.Add(_opacity);
            opacityRow.Children.Add(_opacityText);
            backgroundPanel.Children.Add(Row("背景浓度", "越高越明显；默认 45%，建议 35%–55%，太高会影响文字阅读。", opacityRow));
            backgroundPanel.Children.Add(Row("缓慢漂浮", "背景会非常缓慢地放大缩小，像在水里漂。关掉后完全静止。", _bgMotion));

            var placePanel = new StackPanel { Spacing = 12 };
            placePanel.Children.Add(Row("侧栏页脚小伙伴", "只在侧栏底部放一只打盹的水豚；侧栏顶部保持干净，不放标题卡片。", _sidebar));
            placePanel.Children.Add(Row("角落陪伴", "每个页面右下角（或左下角）蹲着一只噜噜，切页面时换表情。", _corner));
            placePanel.Children.Add(Row("标题栏小噜", "把窗口标题栏的小图标换成噜噜的头。", _titleBar));
            placePanel.Children.Add(Row("首页贴纸", "在首页大卡片右侧放一只给你加油的噜噜。", _hero));
            placePanel.Children.Add(Row("硬件信息页卡片美化", "把型号、系统、运行时间三张卡和详细信息卡换成暖色噜噜主题，并在卡片里放噜噜水印；不改布局、不加新卡片。", _hardware));

            var mascotPanel = new StackPanel { Spacing = 12 };
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            sizeRow.Children.Add(_mascotSize);
            sizeRow.Children.Add(_mascotSizeText);
            mascotPanel.Children.Add(Row("噜噜大小", "角落小伙伴的宽度，92–190 像素。", sizeRow));
            mascotPanel.Children.Add(Row("站位", "选右下角或左下角；对话气泡会跟着换边。", _side));
            var speechRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            speechRow.Children.Add(_speech);
            speechRow.Children.Add(new TextBlock
            {
                Text = "自动说点废话",
                FontSize = 12,
                Opacity = 0.72,
                VerticalAlignment = VerticalAlignment.Center,
            });
            mascotPanel.Children.Add(Row("说话", "切页面时必说一句；再按下面的间隔自己找话讲。", speechRow));
            mascotPanel.Children.Add(Row("说话间隔", "每隔多久冒一句话。选「只在切页面时说」就完全安静。", _interval));
            mascotPanel.Children.Add(Row("对话气泡", "关掉后噜噜还在，只是不冒气泡。", _bubble));

            var motionPanel = new StackPanel { Spacing = 12 };
            motionPanel.Children.Add(Row("动态效果", "角落呼吸、页脚打盹、首页贴纸浮动、背景漂浮和说话弹一下，全部受这个总开关控制。", _animations));

            var speakButton = new Button { Content = "让噜噜说句话" };
            var backgroundButton = new Button { Content = "换一张背景" };
            var resetButton = new Button { Content = "一键还原" };
            speakButton.Click += async (_, _) => await SpeakAsync();
            backgroundButton.Click += (_, _) => CycleBackground();
            resetButton.Click += async (_, _) => await ResetAsync();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(speakButton);
            buttons.Children.Add(backgroundButton);
            buttons.Children.Add(resetButton);

            var note = new TextBlock
            {
                Text = "说明：这是非官方粉丝美化包，「水豚噜噜」形象版权归原作者所有。插件只在本机运行，装饰不写入主程序源码，也不会改动 Windows 壁纸。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.55,
                FontSize = 12,
            };

            var panel = new StackPanel
            {
                Spacing = 16,
                Margin = new Thickness(26, 18, 26, 28),
                MaxWidth = 1000,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            panel.Children.Add(header);
            panel.Children.Add(_preview);
            panel.Children.Add(Card(masterRow));
            panel.Children.Add(SectionTitle("背景"));
            panel.Children.Add(Card(backgroundPanel));
            panel.Children.Add(SectionTitle("放在哪里"));
            panel.Children.Add(Card(placePanel));
            panel.Children.Add(SectionTitle("角落小伙伴"));
            panel.Children.Add(Card(mascotPanel));
            panel.Children.Add(SectionTitle("动效"));
            panel.Children.Add(Card(motionPanel));
            panel.Children.Add(buttons);
            panel.Children.Add(_status);
            panel.Children.Add(note);

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel,
            };
            Root = scroll;
            Root.Loaded += Root_Loaded;
            Root.Unloaded += Root_Unloaded;

            foreach (var toggle in new[] { _background, _bgMotion, _sidebar, _corner, _speech, _bubble, _titleBar, _hero, _hardware, _animations })
                toggle.Toggled += (_, _) => SaveToggle(toggle);
            _master.Toggled += Master_Toggled;
            _theme.SelectionChanged += Theme_Changed;
            _side.SelectionChanged += Side_Changed;
            _interval.SelectionChanged += Interval_Changed;
            _opacity.ValueChanged += Opacity_Changed;
            _mascotSize.ValueChanged += MascotSize_Changed;
            _saveTimer = Root.DispatcherQueue?.CreateTimer();
            if (_saveTimer is not null)
            {
                _saveTimer.Interval = TimeSpan.FromMilliseconds(650);
                _saveTimer.IsRepeating = false;
                _saveTimer.Tick += (_, _) => PersistPreview();
            }
            BuildThemeBox();
            BuildSideBox();
            BuildIntervalBox();
        }

        public void Dispose() => _saveTimer?.Stop();

        private async void Root_Loaded(object sender, RoutedEventArgs e)
        {
            if (CapybaraRuntime.HostAccess is null)
            {
                _master.IsEnabled = false;
                _status.Text = "当前插件没有 fullTrust 权限，无法修改主界面。";
                return;
            }
            try
            {
                await CapybaraRuntime.EnsureReadyAsync(CancellationToken.None);
                SyncFromSettings();
                UpdateStatus();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "读取设置失败：" + ex.Message; }
        }

        private void Root_Unloaded(object sender, RoutedEventArgs e) => _saveTimer?.Stop();

        private void BuildThemeBox()
        {
            foreach (var (text, tag) in new[]
            {
                ("自动（按日期轮换）", CapybaraSettings.ThemeAuto),
                ("洗澡噜", CapybaraSettings.ThemeBath),
                ("草地晒太阳", CapybaraSettings.ThemePark),
                ("书桌小窝", CapybaraSettings.ThemeDesk),
                ("云端晚安", CapybaraSettings.ThemeNight),
            })
            {
                _theme.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
            }
        }

        private void BuildSideBox()
        {
            _side.Items.Add(new ComboBoxItem { Content = "右下角", Tag = CapybaraSettings.SideRight });
            _side.Items.Add(new ComboBoxItem { Content = "左下角", Tag = CapybaraSettings.SideLeft });
        }

        private void BuildIntervalBox()
        {
            foreach (var (text, seconds) in new[]
            {
                ("只在切页面时说", 0),
                ("每 20 秒", 20),
                ("每 35 秒", 35),
                ("每 50 秒（默认）", 50),
                ("每 80 秒", 80),
                ("每 2 分钟", 120),
            })
            {
                _interval.Items.Add(new ComboBoxItem { Content = text, Tag = seconds });
            }
        }

        private void SyncFromSettings()
        {
            var settings = CapybaraRuntime.Settings;
            _initializing = true;
            _master.IsOn = settings.Enabled;
            _background.IsOn = settings.Background;
            _bgMotion.IsOn = settings.BackgroundMotion;
            _sidebar.IsOn = settings.Sidebar;
            _corner.IsOn = settings.Corner;
            _speech.IsOn = settings.Speech;
            _bubble.IsOn = settings.Bubble;
            _titleBar.IsOn = settings.TitleBar;
            _hero.IsOn = settings.HeroSticker;
            _hardware.IsOn = settings.HardwareDecor;
            _animations.IsOn = settings.Animations;
            var index = Array.IndexOf(CapybaraSettings.Themes, settings.BackgroundTheme);
            _theme.SelectedIndex = index < 0 ? 0 : index;
            var side = settings.MascotSide.Equals(CapybaraSettings.SideLeft, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _side.SelectedIndex = side;
            _interval.SelectedIndex = settings.ClampedSpeechInterval switch
            {
                0 => 0,
                <= 20 => 1,
                <= 35 => 2,
                <= 50 => 3,
                <= 80 => 4,
                _ => 5,
            };
            _opacity.Value = Math.Round(settings.ClampedOpacity * 100);
            _opacityText.Text = $"{_opacity.Value:0}%";
            _mascotSize.Value = Math.Round(settings.ClampedMascotSize);
            _mascotSizeText.Text = $"{_mascotSize.Value:0} px";
            _initializing = false;
            UpdateChildEnabled();
            _preview.Opacity = settings.Enabled ? 1 : 0.55;
        }

        private void UpdateChildEnabled()
        {
            var on = _master.IsOn;
            foreach (var control in new Control[]
                     { _background, _bgMotion, _sidebar, _corner, _speech, _bubble, _titleBar, _hero, _hardware,
                       _animations, _theme, _opacity, _mascotSize, _side, _interval })
                control.IsEnabled = on;
            foreach (var control in new Control[] { _speech, _bubble, _mascotSize, _side, _interval })
                control.IsEnabled = on && _corner.IsOn;
            _bgMotion.IsEnabled = on && _background.IsOn;
        }

        private async void Master_Toggled(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            var enabled = _master.IsOn;
            if (enabled && !await ConfirmEnableAsync())
            {
                _initializing = true;
                _master.IsOn = false;
                _initializing = false;
                UpdateChildEnabled();
                return;
            }
            _master.IsEnabled = false;
            try
            {
                await CapybaraRuntime.SetEnabledAsync(enabled, CancellationToken.None);
                UpdateChildEnabled();
                _preview.Opacity = enabled ? 1 : 0.55;
                UpdateStatus();
                if (enabled) await ShowEnabledAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _initializing = true; _master.IsOn = !enabled; _initializing = false;
                UpdateChildEnabled();
                _status.Text = "应用失败：" + ex.Message;
            }
            finally { _master.IsEnabled = true; }
        }

        private async Task<bool> ConfirmEnableAsync()
        {
            var list = new StackPanel { Spacing = 7, MaxWidth = 470 };
            list.Children.Add(new TextBlock
            {
                Text = "噜噜会把下面这些装饰叠到工具箱界面上：",
                TextWrapping = TextWrapping.Wrap,
            });
            foreach (var line in new[]
            {
                "· 内容区背景插画（不会改 Windows 壁纸）",
                "· 标题栏小图标",
                "· 侧栏页脚一只打盹的水豚",
                "· 每个页面角落的陪伴噜噜和对话气泡",
                "· 首页大卡片旁的加油贴纸",
            })
            {
                list.Children.Add(new TextBlock
                {
                    Text = line,
                    FontSize = 12.5,
                    Opacity = 0.8,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 0, 0, 0),
                });
            }
            list.Children.Add(new TextBlock
            {
                Text = "所有改动都只在插件运行时生效，关掉总开关就原样还原；主程序源码不会被修改。素材为粉丝自制，版权归原作者，请勿商用。",
                FontSize = 12,
                Opacity = 0.62,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "开启水豚噜噜美化包？",
                Content = list,
                PrimaryButtonText = "开启美化包",
                CloseButtonText = "先不了",
                DefaultButton = ContentDialogButton.Primary,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task ShowEnabledAsync()
        {
            var content = new StackPanel { Spacing = 6, MaxWidth = 430 };
            content.Children.Add(new TextBlock { Text = "噜噜已经上岗了。", FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock
            {
                Text = "它会按页面换表情、偶尔说句话；想安静就去关「自动说点废话」或「对话气泡」。觉得背景太抢眼，把浓度往下拉一点就好。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
            });
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "噜噜已就位",
                Content = content,
                CloseButtonText = "知道啦",
            };
            await dialog.ShowAsync();
        }

        private async void SaveToggle(ToggleSwitch toggle)
        {
            if (_initializing) return;
            var value = toggle.IsOn;
            try
            {
                if (ReferenceEquals(toggle, _bubble))
                {
                    CapybaraRuntime.PreviewBubble(value);
                    await CapybaraRuntime.SaveAsync(CancellationToken.None);
                    UpdateStatus();
                    return;
                }
                await CapybaraRuntime.UpdateAsync(settings =>
                {
                    if (ReferenceEquals(toggle, _background)) settings.Background = value;
                    else if (ReferenceEquals(toggle, _bgMotion)) settings.BackgroundMotion = value;
                    else if (ReferenceEquals(toggle, _sidebar)) settings.Sidebar = value;
                    else if (ReferenceEquals(toggle, _corner)) settings.Corner = value;
                    else if (ReferenceEquals(toggle, _speech)) settings.Speech = value;
                    else if (ReferenceEquals(toggle, _titleBar)) settings.TitleBar = value;
                    else if (ReferenceEquals(toggle, _hero)) settings.HeroSticker = value;
                    else if (ReferenceEquals(toggle, _hardware)) settings.HardwareDecor = value;
                    else if (ReferenceEquals(toggle, _animations)) settings.Animations = value;
                }, CancellationToken.None);
                UpdateChildEnabled();
                UpdateStatus();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "应用失败：" + ex.Message; }
        }

        private async void Theme_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing || _theme.SelectedItem is not ComboBoxItem item || item.Tag is not string theme) return;
            try
            {
                await CapybaraRuntime.UpdateAsync(settings => settings.BackgroundTheme = theme, CancellationToken.None);
                UpdateStatus();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "应用失败：" + ex.Message; }
        }

        private async void Side_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing || _side.SelectedItem is not ComboBoxItem item || item.Tag is not string side) return;
            try
            {
                await CapybaraRuntime.UpdateAsync(settings => settings.MascotSide = side, CancellationToken.None);
                UpdateStatus();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "应用失败：" + ex.Message; }
        }

        private async void Interval_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing || _interval.SelectedItem is not ComboBoxItem item || item.Tag is not int seconds) return;
            try
            {
                await CapybaraRuntime.UpdateAsync(settings => settings.SpeechInterval = seconds, CancellationToken.None);
                UpdateStatus();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "应用失败：" + ex.Message; }
        }

        private void Opacity_Changed(object sender, RangeBaseValueChangedEventArgs e)
        {
            _opacityText.Text = $"{e.NewValue:0}%";
            if (_initializing) return;
            CapybaraRuntime.PreviewOpacity(e.NewValue / 100.0);
            _saveTimer?.Stop();
            _saveTimer?.Start();
        }

        private void MascotSize_Changed(object sender, RangeBaseValueChangedEventArgs e)
        {
            _mascotSizeText.Text = $"{e.NewValue:0} px";
            if (_initializing) return;
            CapybaraRuntime.PreviewMascotSize(e.NewValue);
            _saveTimer?.Stop();
            _saveTimer?.Start();
        }

        private async void PersistPreview()
        {
            try { await CapybaraRuntime.SaveAsync(CancellationToken.None); }
            catch { }
        }

        private async Task SpeakAsync()
        {
            try
            {
                await CapybaraRuntime.SpeakAsync(CancellationToken.None);
                _previewSay.Text = "噜噜已经在主界面说话啦，看看右下角～";
                _status.Text = "噜噜说完了。提示：打开「自动说点废话」它会自己找话讲。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "让它说话失败：" + ex.Message; }
        }

        private void CycleBackground()
        {
            _theme.SelectedIndex = (_theme.SelectedIndex + 1) % CapybaraSettings.Themes.Length;
            if (_theme.SelectedItem is ComboBoxItem item)
                _status.Text = "背景已切换为「" + item.Content + "」。";
        }

        private async Task ResetAsync()
        {
            try
            {
                await CapybaraRuntime.SetEnabledAsync(false, CancellationToken.None);
                var defaults = new CapybaraSettings { Enabled = false };
                await CapybaraRuntime.UpdateAsync(settings =>
                {
                    settings.Background = defaults.Background;
                    settings.BackgroundTheme = defaults.BackgroundTheme;
                    settings.BackgroundOpacity = defaults.BackgroundOpacity;
                    settings.BackgroundMotion = defaults.BackgroundMotion;
                    settings.Sidebar = defaults.Sidebar;
                    settings.Corner = defaults.Corner;
                    settings.MascotSize = defaults.MascotSize;
                    settings.MascotSide = defaults.MascotSide;
                    settings.Speech = defaults.Speech;
                    settings.SpeechInterval = defaults.SpeechInterval;
                    settings.Bubble = defaults.Bubble;
                    settings.TitleBar = defaults.TitleBar;
                    settings.HeroSticker = defaults.HeroSticker;
                    settings.HardwareDecor = defaults.HardwareDecor;
                    settings.Animations = defaults.Animations;
                }, CancellationToken.None);
                SyncFromSettings();
                _status.Text = "已还原默认外观，所有开关回到默认值（美化包保持关闭）。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "还原失败：" + ex.Message; }
        }

        private void UpdateStatus()
        {
            var settings = CapybaraRuntime.Settings;
            if (!settings.Enabled)
            {
                _status.Text = "当前已关闭：主界面保持工具箱默认外观。";
            }
            else
            {
                var parts = new List<string>();
                if (settings.Background) parts.Add("背景");
                if (settings.Sidebar) parts.Add("页脚");
                if (settings.TitleBar) parts.Add("标题栏");
                if (settings.Corner) parts.Add("角落小伙伴");
                if (settings.HeroSticker) parts.Add("首页贴纸");
                _status.Text = parts.Count == 0
                    ? "美化包已启用，但没有勾选任何位置。"
                    : "已启用：" + string.Join("、", parts) + "。改动立即生效并保存。";
            }
            if (CapybaraRuntime.LastError is { Length: > 0 } error) _status.Text += "\n" + error;
        }

        // ------------------------------------------------------------ UI 组装

        private static Grid Row(string title, string description, FrameworkElement control)
        {
            var titleBlock = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold };
            var descBlock = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.68, FontSize = 12.5 };
            var text = new StackPanel { Spacing = 3 };
            text.Children.Add(titleBlock);
            text.Children.Add(descBlock);
            var grid = new Grid { ColumnSpacing = 18 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(text);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        private static TextBlock SectionTitle(string text) => new()
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.65,
            Margin = new Thickness(2, 4, 0, -6),
        };

        private static Border Card(FrameworkElement content) => new()
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush") ?? new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush") ?? new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
            Child = content,
        };

        private static ToggleSwitch NewToggle() => new()
        {
            OnContent = "开",
            OffContent = "关",
            MinWidth = 92,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private Border BuildPreview(TextBlock say)
        {
            var root = new Grid { Height = 268 };
            var background = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new ImageBrush
                {
                    ImageSource = TryLoad("bg-desk.jpg"),
                    Stretch = Stretch.UniformToFill,
                },
            };
            root.Children.Add(background);
            root.Children.Add(new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                IsHitTestVisible = false,
            });

            var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
            foreach (var color in new[] { Color.FromArgb(255, 255, 120, 110), Color.FromArgb(255, 255, 196, 92), Color.FromArgb(255, 122, 205, 160) })
                dots.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(color) });
            var windowTitle = new TextBlock
            {
                Text = "图吧工具箱 CE",
                FontSize = 11,
                Opacity = 0.68,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            var chromeRow = new StackPanel { Orientation = Orientation.Horizontal };
            chromeRow.Children.Add(dots);
            chromeRow.Children.Add(windowTitle);
            var chrome = new Border
            {
                Height = 34,
                Margin = new Thickness(14, 12, 14, 0),
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(9),
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(96, 255, 255, 255)),
                Child = chromeRow,
            };
            root.Children.Add(chrome);

            var navItems = new StackPanel { Spacing = 8, Padding = new Thickness(12, 12, 12, 10) };
            for (var i = 0; i < 4; i++)
            {
                navItems.Children.Add(new Border
                {
                    Height = 6,
                    Width = i == 0 ? 54 : 42,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Color.FromArgb((byte)(i == 0 ? 200 : 90), 63, 63, 63)),
                });
            }
            var nav = new Border
            {
                Width = 92,
                Margin = new Thickness(14, 56, 0, 14),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(88, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = navItems,
            };
            root.Children.Add(nav);

            var content = new StackPanel { Spacing = 10, Margin = new Thickness(118, 66, 16, 16) };
            content.Children.Add(new Border
            {
                Height = 46,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            });
            content.Children.Add(new Border
            {
                Height = 72,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(96, 255, 255, 255)),
            });
            root.Children.Add(content);

            var mascot = new Image
            {
                Source = TryLoad("lulu-cheer.png"),
                Width = 104,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 12, 6),
                IsHitTestVisible = false,
            };
            root.Children.Add(mascot);

            var sayBorder = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 108, 22),
                Padding = new Thickness(10, 6, 10, 7),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Child = say,
            };
            root.Children.Add(sayBorder);

            return new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush") ?? new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                Child = root,
            };
        }

        private static ImageSource? TryLoad(string name)
        {
            try { return CapybaraAssets.Load(name); }
            catch { return null; }
        }

        private static Brush? ThemeBrush(string key)
        {
            try { return Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null; }
            catch { return null; }
        }
    }
}
