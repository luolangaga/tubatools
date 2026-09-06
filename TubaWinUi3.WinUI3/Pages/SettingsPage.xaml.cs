using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using TubaWinUi3.Services;
using TubaWinUi3.Services.ActiveIntercept;
using TubaWinUi3.Services.Ai;
using TubaWinUi3.Models;
using Windows.UI;
using static TubaWinUi3.Services.ConfigManager;

namespace TubaWinUi3.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _isCheckingUpdate;
    private bool _isCheckingToolsBundle;
    private bool _compactModeInitializing;
    private bool _fastModeInitializing;
    private bool _navLayoutInitializing;
    private bool _rememberWindowInitializing;
    private bool _defaultPageInitializing;
    private bool _builtinToolOpenModeInitializing;
    private bool _backdropInitializing;
    private bool _opacityChanging;
    private bool _brandLogoInitializing;
    private bool _watermarkInitializing;
    private bool _watermarkTextInitializing;
    private bool _watermarkFontInitializing;
    private Border[] _backdropOptions = [];
    private Border[] _tintSwatches = [];
    private Color _currentTintColor = BackdropSettings.DefaultTintColor;
    private bool _hardwareFitScreenInitializing;
    private bool _hardwareMultiDeviceNewLineInitializing;
    private bool _cpuzBusy;
    private bool _aiSettingsInitializing;
    private bool _aiTesting;
    private bool _zenBusy;
    private bool _proxySettingsInitializing;
    private bool _proxyTesting;

    private FrameworkElement? _generalExpanderContent;
    private FrameworkElement? _appearanceExpanderContent;
    private FrameworkElement? _hardwareAiExpanderContent;
    private FrameworkElement? _toolsCommunityExpanderContent;
    private FrameworkElement? _creditsExpanderContent;

    private static readonly (string Tag, string DisplayName)[] DefaultPageOptions =
    [
        ("all", "全部工具"),
        ("favorites", "常用"),
        ("hardware", "硬件信息"),
        ("builtin", "内置工具"),
    ];

    private string? _pendingHighlightKey;
    private CancellationTokenSource? _highlightCts;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public string lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileName(ref OPENFILENAME ofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_PATHMUSTEXIST = 0x00000800;

    private static readonly Dictionary<string, string> SettingKeyToExpander = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CompactMode"] = "GeneralExpander",
        ["NavLayoutMode"] = "GeneralExpander",
        ["DefaultPage"] = "GeneralExpander",
        ["FastMode"] = "GeneralExpander",
        ["RememberWindow"] = "GeneralExpander",
        ["Update"] = "GeneralExpander",
        ["ToolsBundle"] = "GeneralExpander",
        ["Background"] = "AppearanceExpander",
        ["Backdrop"] = "AppearanceExpander",
        ["BrandLogo"] = "AppearanceExpander",
        ["Watermark"] = "AppearanceExpander",
        ["InterfaceFont"] = "AppearanceExpander",
        ["HardwareFitScreen"] = "HardwareAiExpander",
        ["HardwareMultiDeviceNewLine"] = "HardwareAiExpander",
        ["AiApiEndpoint"] = "HardwareAiExpander",
        ["AiModelName"] = "HardwareAiExpander",
        ["AiApiKey"] = "HardwareAiExpander",
        ["SearchApiKey"] = "HardwareAiExpander",
        ["ProxyEnabled"] = "GeneralExpander",
        ["ProxyAddress"] = "GeneralExpander",
        ["HttpDownload"] = "ToolsCommunityExpander",
        ["HttpDownloadPath"] = "ToolsCommunityExpander",
        ["HttpDownloadAction"] = "ToolsCommunityExpander",
        ["ConfigManager"] = "ToolsCommunityExpander",
        ["CustomToolManager"] = "ToolsCommunityExpander",
        ["ExportApp"] = "ToolsCommunityExpander",
        ["CommunityTool"] = "ToolsCommunityExpander",
        ["ActiveInterceptEnabled"] = "ToolsCommunityExpander",
        ["ActiveInterceptNotifyMode"] = "ToolsCommunityExpander",
        ["WindowsSearchIndex"] = "ToolsCommunityExpander",
    };

    private static readonly Dictionary<string, string> SettingKeyToCardName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CompactMode"] = "SettingsCompactModeCard",
        ["NavLayoutMode"] = "SettingsNavLayoutCard",
        ["DefaultPage"] = "SettingsDefaultPageCard",
        ["FastMode"] = "SettingsFastModeCard",
        ["RememberWindow"] = "SettingsRememberWindowCard",
        ["Update"] = "SettingsUpdateCard",
        ["ToolsBundle"] = "SettingsToolsBundleCard",
        ["Background"] = "SettingsBackgroundCard",
        ["Backdrop"] = "SettingsBackdropCard",
        ["BrandLogo"] = "SettingsBrandLogoCard",
        ["Watermark"] = "SettingsWatermarkCard",
        ["InterfaceFont"] = "SettingsInterfaceFontCard",
        ["HardwareFitScreen"] = "SettingsHardwareFitScreenCard",
        ["HardwareMultiDeviceNewLine"] = "SettingsHardwareMultiDeviceNewLineCard",
        ["AiApiEndpoint"] = "SettingsAiEndpointCard",
        ["AiModelName"] = "SettingsAiEndpointCard",
        ["AiApiKey"] = "SettingsAiEndpointCard",
        ["SearchApiKey"] = "SettingsAiEndpointCard",
        ["ProxyEnabled"] = "SettingsProxyCard",
        ["ProxyAddress"] = "SettingsProxyCard",
        ["HttpDownload"] = "SettingsHttpDownloadCard",
        ["HttpDownloadPath"] = "SettingsHttpDownloadCard",
        ["HttpDownloadAction"] = "SettingsHttpDownloadCard",
        ["ConfigManager"] = "SettingsConfigManagerCard",
        ["CustomToolManager"] = "SettingsCustomToolCard",
        ["ExportApp"] = "SettingsExportAppCard",
        ["CommunityTool"] = "SettingsCommunityCard",
        ["ActiveInterceptEnabled"] = "SettingsActiveInterceptCard",
        ["ActiveInterceptNotifyMode"] = "SettingsActiveInterceptNotifyCard",
        ["WindowsSearchIndex"] = "SettingsSearchIndexCard",
    };

    public SettingsPage()
    {
        InitializeComponent();

        _generalExpanderContent = GeneralExpander.Content as FrameworkElement;
        _appearanceExpanderContent = AppearanceExpander.Content as FrameworkElement;
        _hardwareAiExpanderContent = HardwareAiExpander.Content as FrameworkElement;
        _toolsCommunityExpanderContent = ToolsCommunityExpander.Content as FrameworkElement;
        _creditsExpanderContent = CreditsExpander.Content as FrameworkElement;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is not null
            ? $"版本 {version.Major}.{version.Minor}.{version.Build}"
            : "版本 1.0.0";

        _ = LoadAppIconAsync();
        InitCompactModeToggle();
        InitNavLayoutComboBox();
        InitDefaultPageComboBox();
        InitFastModeToggle();
        InitRememberWindowToggle();
        InitUpdateSection();
        InitBackdropSettings();
        LoadBackgroundSettings();
        InitBrandLogoToggle();
        InitWatermarkSettings();
        InitHardwareFitScreenToggle();
        InitHardwareMultiDeviceNewLineToggle();
        InitCpuzDataSourceStatus();
        InitAiSettings();
        InitProxySettings();
        InitGitHubLoginStatus();
        LoadCreditsAvatar();
        InitBuiltinToolOpenModeComboBox();
        InitHttpDownloadSettings();
        InitActiveInterceptToggle();
        InitActiveInterceptNotifyModeComboBox();
        InitSearchIndexToggle();

        if (RuntimeHelper.IsMsixPackaged)
        {
            SettingsCommunityCard.Visibility = Visibility.Collapsed;
            SettingsCommunitySubmitCard.Visibility = Visibility.Collapsed;
            ToolsCommunityTitleText.Text = "工具";
            ToolsCommunityDescText.Text = "配置管理、自定义工具、导出";
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DownloadQueueService.QueueChanged += UpdateDownloadQueueStatus;

        RestoreExpanderContent(GeneralExpander, _generalExpanderContent);
        RestoreExpanderContent(AppearanceExpander, _appearanceExpanderContent);
        RestoreExpanderContent(HardwareAiExpander, _hardwareAiExpanderContent);
        RestoreExpanderContent(ToolsCommunityExpander, _toolsCommunityExpanderContent);
        RestoreExpanderContent(CreditsExpander, _creditsExpanderContent);

        if (GeneralExpander is not null)
            GeneralExpander.IsExpanded = true;

        if (e.Parameter is SearchNavigationTarget target && target.HighlightSettingKey is not null)
        {
            _pendingHighlightKey = target.HighlightSettingKey;
        }

        if (_pendingHighlightKey is not null)
        {
            StartHighlight(_pendingHighlightKey);
            _pendingHighlightKey = null;
        }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        DownloadQueueService.QueueChanged -= UpdateDownloadQueueStatus;
    }

    private static void RestoreExpanderContent(Expander? expander, FrameworkElement? savedContent)
    {
        if (expander is null || savedContent is null) return;
        if (expander.Content is null || IsExpanderContentEmpty(expander))
            expander.Content = savedContent;
    }

    private static bool IsExpanderContentEmpty(Expander expander)
    {
        if (expander.Content is not FrameworkElement content) return true;
        if (content is ScrollViewer sv && sv.Content is StackPanel sp)
            return sp.Children.Count == 0;
        if (content is StackPanel sp2)
            return sp2.Children.Count == 0;
        return false;
    }

    private void StartHighlight(string settingKey)
    {
        _highlightCts?.Cancel();
        _highlightCts = new CancellationTokenSource();
        _ = HighlightSettingAsync(settingKey, _highlightCts.Token);
    }

    private async Task HighlightSettingAsync(string settingKey, CancellationToken ct)
    {
        if (SettingKeyToExpander.TryGetValue(settingKey, out var expanderName) &&
            SettingKeyToCardName.TryGetValue(settingKey, out var cardName))
        {
            if (FindName(expanderName) is Expander expander)
            {
                expander.IsExpanded = true;
            }

            try { await Task.Delay(300, ct); } catch (OperationCanceledException) { return; }

            if (ct.IsCancellationRequested) return;

            if (FindName(cardName) is Border border)
            {
                border.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalAlignmentRatio = 0.5
                });

                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }

                if (ct.IsCancellationRequested) return;
                SearchHighlightService.HighlightBorder(border);
            }
        }
    }

    public static string? ResolveExpanderName(string settingKey)
    {
        if (!SettingKeyToExpander.TryGetValue(settingKey, out var value))
            return null;
        return value;
    }

    /// <summary>
    /// 加载 AppIcon.ico 中尺寸最大的帧（PNG 压缩）作为「关于」卡片的应用图标。
    /// </summary>
    private async Task LoadAppIconAsync()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (!File.Exists(iconPath)) return;

            // ICO 目录：6 字节头 + 每条 16 字节；帧均为 PNG 压缩，取数据最大的一帧保证清晰度
            using var fs = File.OpenRead(iconPath);
            using var reader = new BinaryReader(fs);
            reader.ReadUInt16(); // reserved
            reader.ReadUInt16(); // type
            var count = reader.ReadUInt16();
            int bestOffset = 0, bestSize = 0;
            for (var i = 0; i < count; i++)
            {
                reader.ReadByte(); // width
                reader.ReadByte(); // height
                reader.ReadByte(); // color count
                reader.ReadByte(); // reserved
                reader.ReadUInt16(); // planes
                reader.ReadUInt16(); // bit count
                var bytesInRes = reader.ReadInt32();
                var imageOffset = reader.ReadInt32();
                if (bytesInRes > bestSize)
                {
                    bestSize = bytesInRes;
                    bestOffset = imageOffset;
                }
            }

            reader.BaseStream.Seek(bestOffset, SeekOrigin.Begin);
            var pngBytes = reader.ReadBytes(bestSize);

            var bitmap = new BitmapImage();
            using (var inMemStream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
            {
                var winBuffer = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(pngBytes);
                await inMemStream.WriteAsync(winBuffer);
                inMemStream.Seek(0);
                await bitmap.SetSourceAsync(inMemStream);
            }

            AppIconImage.Source = bitmap;
        }
        catch
        {
        }
    }

    private void NavWhatsNew_Tapped(object sender, TappedRoutedEventArgs e)
    {
        WhatsNewWindow.Show();
    }

    private void NavTestPage_Tapped(object sender, TappedRoutedEventArgs e)
    {
        Frame.Navigate(typeof(TestPage));
    }

    private void InitCompactModeToggle()
    {
        _compactModeInitializing = true;
        CompactModeToggle.IsOn = CompactModeService.IsCompactModeEnabled();
        _compactModeInitializing = false;
    }

    private void CompactModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_compactModeInitializing) return;
        CompactModeService.SetCompactModeEnabled(CompactModeToggle.IsOn);
    }

    private void InitNavLayoutComboBox()
    {
        _navLayoutInitializing = true;
        NavLayoutComboBox.Items.Add("侧边栏");
        NavLayoutComboBox.Items.Add("顶部标签页");
        NavLayoutComboBox.SelectedIndex = NavLayoutModeService.IsTabMode() ? 1 : 0;
        _navLayoutInitializing = false;
    }

    private void NavLayoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_navLayoutInitializing) return;
        var mode = NavLayoutComboBox.SelectedIndex == 1 ? "tabs" : "sidebar";
        NavLayoutModeService.SetNavLayoutMode(mode);
    }

    private void InitDefaultPageComboBox()
    {
        _defaultPageInitializing = true;
        DefaultPageComboBox.Items.Clear();
        var saved = AppSettings.Get("DefaultPage") ?? "all";

        for (var i = 0; i < DefaultPageOptions.Length; i++)
        {
            DefaultPageComboBox.Items.Add(DefaultPageOptions[i].DisplayName);
            if (DefaultPageOptions[i].Tag == saved)
                DefaultPageComboBox.SelectedIndex = i;
        }

        if (DefaultPageComboBox.SelectedIndex < 0)
            DefaultPageComboBox.SelectedIndex = 0;

        _defaultPageInitializing = false;
    }

    private void DefaultPageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_defaultPageInitializing) return;
        if (DefaultPageComboBox.SelectedIndex >= 0 && DefaultPageComboBox.SelectedIndex < DefaultPageOptions.Length)
            AppSettings.Set("DefaultPage", DefaultPageOptions[DefaultPageComboBox.SelectedIndex].Tag);
    }

    private void InitBuiltinToolOpenModeComboBox()
    {
        _builtinToolOpenModeInitializing = true;
        BuiltinToolOpenModeComboBox.Items.Clear();
        BuiltinToolOpenModeComboBox.Items.Add("嵌入页面");
        BuiltinToolOpenModeComboBox.Items.Add("独立窗口");
        BuiltinToolOpenModeComboBox.SelectedIndex = AppSettings.GetBool("BuiltinToolsOpenInWindow", false) ? 1 : 0;
        _builtinToolOpenModeInitializing = false;
    }

    private void BuiltinToolOpenModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_builtinToolOpenModeInitializing) return;
        AppSettings.Set("BuiltinToolsOpenInWindow", BuiltinToolOpenModeComboBox.SelectedIndex == 1);
    }

    private void InitFastModeToggle()
    {
        _fastModeInitializing = true;
        FastModeToggle.IsOn = FastModeService.IsFastModeEnabled();
        _fastModeInitializing = false;
    }

    private void FastModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_fastModeInitializing) return;
        FastModeService.SetFastModeEnabled(FastModeToggle.IsOn);
    }

    private void InitRememberWindowToggle()
    {
        _rememberWindowInitializing = true;
        RememberWindowToggle.IsOn = WindowSizeService.IsRememberEnabled();
        _rememberWindowInitializing = false;
    }

    private void RememberWindowToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_rememberWindowInitializing) return;
        WindowSizeService.SetRememberEnabled(RememberWindowToggle.IsOn);
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingUpdate) return;
        _isCheckingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新...";

        try
        {
            var update = await UpdateService.CheckForUpdateAsync();

            if (update is not null)
            {
                UpdateStatusText.Text = $"发现新版本 v{update.Version}，请查看顶部更新提示";
                (App.MainWindow as MainWindow)?.ShowUpdateBanner(update, false);
            }
            else
            {
                UpdateStatusText.Text = "已是最新版本";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"检查失败: {ex.Message}";
        }
        finally
        {
            _isCheckingUpdate = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void InitUpdateSection()
    {
        if (RuntimeHelper.IsMsixPackaged || RuntimeHelper.IsLiteBuild)
        {
            SettingsUpdateCard.Visibility = Visibility.Collapsed;
            SettingsToolsBundleCard.Visibility = Visibility.Visible;
            ToolsBundleStatusText.Text = DescribeToolsBundleStatus();
        }
        else
        {
            SettingsUpdateCard.Visibility = Visibility.Visible;
            SettingsToolsBundleCard.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>按内核变种/来源生成状态文案（MSIX 商店版与精简版便携共用）。</summary>
    private static string DescribeToolsBundleStatus()
    {
        var version = ToolsBundleService.GetCurrentVersion();
        if (version is not null)
        {
            return ToolsBundleService.GetInstalledKind() == ToolsBundleService.KindLite
                ? $"当前精简版内核 v{version}，可升级完整版"
                : $"当前完整版内核 v{version}";
        }

        // 精简版便携随包内置工具（未通过内核包安装过）
        if (RuntimeHelper.IsLiteBuild && Directory.Exists(
                Path.Combine(ToolCatalog.AppDirectory, "Tools")))
        {
            return "已内置精简工具集，可下载完整版内核";
        }

        if (!ToolsBundleService.IsToolsBundleReady())
        {
            return "内核未下载";
        }

        return "内核已就绪（版本未知）";
    }

    private async void CheckToolsBundleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingToolsBundle) return;
        _isCheckingToolsBundle = true;
        CheckToolsBundleButton.IsEnabled = false;
        ToolsBundleStatusText.Text = "正在检查内核更新...";

        try
        {
            var info = await ToolsBundleService.CheckForToolsUpdateAsync();

            if (info is null)
            {
                ToolsBundleStatusText.Text = "检查失败，请稍后重试";
                return;
            }

            // 完整版已是最新：无事可做（不可降级精简版）；其余情况打开对话框
            // （有新版本 → 选版本下载；无新版本且非完整版 → 升级完整版）。
            if (!info.HasUpdate && ToolsBundleService.GetInstalledKind() == ToolsBundleService.KindFull)
            {
                ToolsBundleStatusText.Text = $"当前内核已是最新版本 (v{info.Version})";
                return;
            }

            ToolsBundleStatusText.Text = info.HasUpdate ? $"发现新版本 v{info.Version}" : DescribeToolsBundleStatus();

            var dialog = new ToolsBundleDownloadDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await dialog.ShowDownloadAsync(info);

            if (dialog.DownloadSucceeded)
            {
                ToolsBundleStatusText.Text = DescribeToolsBundleStatus();
            }
            else
            {
                ToolsBundleStatusText.Text = info.HasUpdate ? "点击检查内核是否有新版本" : DescribeToolsBundleStatus();
            }
        }
        catch (Exception ex)
        {
            ToolsBundleStatusText.Text = $"检查失败: {ex.Message}";
        }
        finally
        {
            _isCheckingToolsBundle = false;
            CheckToolsBundleButton.IsEnabled = true;
        }
    }

    private void InitBackdropSettings()
    {
        _backdropInitializing = true;
        _backdropOptions = [BackdropMicaOption, BackdropMicaAltOption, BackdropAcrylicOption, BackdropAcrylicThinOption];
        _tintSwatches = TintSwatchPanel.Children.OfType<Border>().ToArray();

        var currentType = BackdropService.GetBackdropType();
        UpdateBackdropOptionSelection(currentType);

        var customization = BackdropService.GetCustomization();
        CustomTintToggle.IsOn = customization.UseCustomTint;
        TintOpacitySlider.Minimum = 0;
        TintOpacitySlider.Maximum = 100;
        TintOpacitySlider.StepFrequency = 5;
        TintOpacitySlider.Value = customization.TintOpacity * 100;
        TintLuminositySlider.Minimum = 0;
        TintLuminositySlider.Maximum = 100;
        TintLuminositySlider.StepFrequency = 5;
        TintLuminositySlider.Value = customization.LuminosityOpacity * 100;
        TintOpacityText.Text = $"{(int)(customization.TintOpacity * 100)}%";
        TintLuminosityText.Text = $"{(int)(customization.LuminosityOpacity * 100)}%";
        UpdateTintColorSelection(customization.TintColor);
        UpdateCustomTintPanelVisibility();
        _backdropInitializing = false;
    }

    private void UpdateBackdropOptionSelection(BackdropType selected)
    {
        foreach (var border in _backdropOptions)
        {
            if (border is null) continue;
            var tag = border.Tag?.ToString();
            var isSelected = tag == selected.ToString();
            border.BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                : (Brush)App.Current.Resources["SubtleFillColorSecondaryBrush"];
        }
    }

    private void BackdropOption_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_backdropInitializing) return;
        if (sender is not Border border) return;
        if (!Enum.TryParse<BackdropType>(border.Tag?.ToString(), out var type)) return;

        BackdropService.SetBackdropType(type);
        UpdateBackdropOptionSelection(type);
    }

    private void BackdropOption_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 0.85;
        }
    }

    private void BackdropOption_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 1.0;
        }
    }

    private void UpdateCustomTintPanelVisibility()
    {
        CustomTintPanel.Visibility = CustomTintToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CustomTintToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_backdropInitializing) return;
        UpdateCustomTintPanelVisibility();
        SaveCustomization();
    }

    private void TintSwatch_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_backdropInitializing) return;
        if (sender is not Border border) return;
        var color = BackdropSettings.ParseColor(border.Tag?.ToString(), BackdropSettings.DefaultTintColor);
        UpdateTintColorSelection(color);
        SaveCustomization();
    }

    private void TintColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_backdropInitializing) return;
        if (!CustomTintToggle.IsOn) return;
        UpdateTintColorSelection(args.NewColor);
        SaveCustomization();
    }

    private void TintOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_backdropInitializing) return;
        TintOpacityText.Text = $"{(int)e.NewValue}%";
        SaveCustomization();
    }

    private void TintLuminositySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_backdropInitializing) return;
        TintLuminosityText.Text = $"{(int)e.NewValue}%";
        SaveCustomization();
    }

    /// <summary>刷新色板选中环与"自定义颜色"色块,并同步 ColorPicker。</summary>
    private void UpdateTintColorSelection(Color color)
    {
        _currentTintColor = color;
        CustomTintColorChip.Background = new SolidColorBrush(color);
        if (TintColorPicker.Color != color)
            TintColorPicker.Color = color; // 赋值会触发 ColorChanged,值相同则跳过避免递归

        foreach (var swatch in _tintSwatches)
        {
            var isSelected = BackdropSettings.ParseColor(swatch.Tag?.ToString(), Color.FromArgb(0, 0, 0, 0)) == color;
            swatch.BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                : (Brush)App.Current.Resources["ControlStrokeColorDefaultBrush"];
        }
    }

    private void SaveCustomization()
    {
        var customization = new BackdropCustomization(
            CustomTintToggle.IsOn,
            _currentTintColor,
            TintOpacitySlider.Value / 100.0,
            TintLuminositySlider.Value / 100.0);
        BackdropService.SetCustomization(customization);
    }

    private void LoadBackgroundSettings()
    {
        _opacityChanging = true;
        BgOpacitySlider.Minimum = 5;
        BgOpacitySlider.Maximum = 80;
        BgOpacitySlider.StepFrequency = 5;

        var path = BackgroundService.GetBackgroundPath();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            ShowBgPreview(path);
        }

        var opacity = BackgroundService.GetBackgroundOpacity();
        BgOpacitySlider.Value = (int)(opacity * 100);
        _opacityChanging = false;
        BgOpacityText.Text = $"{(int)(opacity * 100)}%";

        PopulateBgList();
    }

    private void PopulateBgList()
    {
        var entries = BackgroundService.GetImportedBackgrounds();
        BgListPanel.Children.Clear();

        if (entries.Count == 0)
        {
            BgListEmptyText.Visibility = Visibility.Visible;
            BgListScrollViewer.Visibility = Visibility.Collapsed;
            BgHistoryCountText.Text = "";
            BgHistoryExpander.Visibility = Visibility.Collapsed;
            return;
        }

        BgListEmptyText.Visibility = Visibility.Collapsed;
        BgListScrollViewer.Visibility = Visibility.Visible;
        BgHistoryCountText.Text = $"({entries.Count})";
        BgHistoryExpander.Visibility = Visibility.Visible;

        foreach (var entry in entries)
        {
            var item = CreateBgListItem(entry);
            BgListPanel.Children.Add(item);
        }
    }

    private Border CreateBgListItem(BackgroundImageEntry entry)
    {
        var isSelected = entry.IsSelected;
        var accentBrush = (Brush)App.Current.Resources["AccentFillColorDefaultBrush"];

        var thumbnailBorder = new Border
        {
            Width = 140,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            BorderBrush = isSelected ? accentBrush : (Brush)App.Current.Resources["CardStrokeColorDefaultBrush"],
            Tag = entry.Path,
            Padding = new Thickness(0),
        };

        var grid = new Grid { RowSpacing = 0 };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Source = new BitmapImage(new Uri(entry.Path)),
        };
        Grid.SetRow(image, 0);
        grid.Children.Add(image);

        var infoPanel = new Grid
        {
            Padding = new Thickness(6, 4, 6, 4),
            ColumnSpacing = 4,
        };
        Grid.SetRow(infoPanel, 1);
        infoPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new TextBlock
        {
            Text = entry.FileName,
            FontSize = 11,
            Opacity = 0.72,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameText, 0);
        infoPanel.Children.Add(nameText);

        var deleteButton = new Button
        {
            Padding = new Thickness(2),
            MinWidth = 0,
            MinHeight = 0,
            Width = 22,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = entry.Path,
        };
        var deleteIcon = new FontIcon
        {
            Glyph = "\uE74D",
            FontSize = 10,
            Foreground = (Brush)App.Current.Resources["TextFillColorSecondaryBrush"],
        };
        deleteButton.Content = deleteIcon;
        deleteButton.Click += BgDeleteItem_Click;
        Grid.SetColumn(deleteButton, 1);
        infoPanel.Children.Add(deleteButton);

        grid.Children.Add(infoPanel);
        thumbnailBorder.Child = grid;

        if (isSelected)
        {
            var checkBadge = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = accentBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0),
            };
            var checkIcon = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 10,
                Foreground = (Brush)App.Current.Resources["TextOnAccentFillColorPrimaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            checkBadge.Child = checkIcon;
            grid.Children.Add(checkBadge);
        }

        thumbnailBorder.PointerPressed += (s, e) =>
        {
            BgListItem_Tapped(entry.Path);
        };

        return thumbnailBorder;
    }

    private void BgListItem_Tapped(string path)
    {
        if (!File.Exists(path)) return;

        BackgroundService.SelectBackground(path);
        ShowBgPreview(path);
        PopulateBgList();
    }

    private void BgDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string path) return;

        BackgroundService.DeleteBackground(path);

        var currentPath = BackgroundService.GetBackgroundPath();
        if (string.IsNullOrWhiteSpace(currentPath))
            HideBgPreview();
        else
            ShowBgPreview(currentPath);

        PopulateBgList();
    }

    private void ShowBgPreview(string path)
    {
        try
        {
            BgPreviewImage.Source = new BitmapImage(new Uri(path));
            BgFileNameText.Text = Path.GetFileName(path);
            BgPreviewPanel.Visibility = Visibility.Visible;
            BgPreviewBorder.Visibility = Visibility.Visible;
            ClearBgButton.Visibility = Visibility.Visible;
        }
        catch { }
    }

    private void HideBgPreview()
    {
        BgPreviewImage.Source = null;
        BgFileNameText.Text = string.Empty;
        BgPreviewPanel.Visibility = Visibility.Collapsed;
        BgPreviewBorder.Visibility = Visibility.Collapsed;
        ClearBgButton.Visibility = Visibility.Collapsed;
    }

    private async void ImportBgButton_Click(object sender, RoutedEventArgs e)
    {
        var ofn = new OPENFILENAME();
        ofn.lStructSize = Marshal.SizeOf(ofn);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        ofn.hwndOwner = hwnd;
        ofn.lpstrFilter = "图片文件\0*.jpg;*.jpeg;*.png;*.bmp\0所有文件\0*.*\0\0";
        ofn.lpstrFile = new string(new char[260]);
        ofn.nMaxFile = 260;
        ofn.lpstrTitle = "选择背景图片";
        ofn.Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR;
        ofn.nFilterIndex = 1;

        if (!GetOpenFileName(ref ofn))
            return;

        var sourcePath = ofn.lpstrFile.TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return;

        try
        {
            var bgDir = ConfigManager.GetBackgroundsDir();
            Directory.CreateDirectory(bgDir);

            var destName = $"bg_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{Path.GetExtension(sourcePath)}";
            var destPath = Path.Combine(bgDir, destName);
            File.Copy(sourcePath, destPath, true);

            BackgroundService.SetBackgroundPath(destPath);
            ShowBgPreview(destPath);
        }
        catch
        {
            BackgroundService.SetBackgroundPath(sourcePath);
            ShowBgPreview(sourcePath);
        }

        PopulateBgList();
    }

    private void ClearBgButton_Click(object sender, RoutedEventArgs e)
    {
        BackgroundService.SetBackgroundPath(null);
        HideBgPreview();
        PopulateBgList();
    }

    private void BgOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_opacityChanging) return;
        var percent = e.NewValue;
        BackgroundService.SetBackgroundOpacity(percent / 100.0);
        BgOpacityText.Text = $"{(int)percent}%";
    }

    private void BrandLogoToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_brandLogoInitializing) return;
        AppSettings.Set("ShowBrandLogo", BrandLogoToggle.IsOn);
    }

    private void InitBrandLogoToggle()
    {
        _brandLogoInitializing = true;
        BrandLogoToggle.IsOn = AppSettings.GetBool("ShowBrandLogo", true);
        _brandLogoInitializing = false;
    }

    private void InitWatermarkSettings()
    {
        _watermarkInitializing = true;
        var watermarkOn = AppSettings.GetBool("ScreenshotWatermark", true);
        WatermarkToggle.IsOn = watermarkOn;
        _watermarkInitializing = false;

        UpdateWatermarkDetailVisibility(watermarkOn);

        _watermarkTextInitializing = true;
        WatermarkTextBox.Text = AppSettings.Get("ScreenshotWatermarkText") ?? "图吧工具箱";
        _watermarkTextInitializing = false;

        _watermarkFontInitializing = true;
        InitWatermarkFontComboBox();
        _watermarkFontInitializing = false;
    }

    private void InitWatermarkFontComboBox()
    {
        WatermarkFontComboBox.Items.Clear();
        var savedFont = AppSettings.Get("ScreenshotWatermarkFont") ?? "微软雅黑";

        using var fc = new InstalledFontCollection();
        var preferredFonts = new[] { "微软雅黑", "宋体", "黑体", "楷体", "仿宋", "Arial", "Segoe UI" };
        var allFonts = new List<string>();

        foreach (var preferred in preferredFonts)
        {
            if (fc.Families.Any(f => f.Name == preferred) && !allFonts.Contains(preferred))
                allFonts.Add(preferred);
        }

        foreach (var family in fc.Families.OrderBy(f => f.Name))
        {
            if (!allFonts.Contains(family.Name))
                allFonts.Add(family.Name);
        }

        var selectedIndex = 0;
        for (var i = 0; i < allFonts.Count; i++)
        {
            WatermarkFontComboBox.Items.Add(allFonts[i]);
            if (allFonts[i] == savedFont)
                selectedIndex = i;
        }

        WatermarkFontComboBox.SelectedIndex = Math.Min(selectedIndex, allFonts.Count - 1);
    }

    private void UpdateWatermarkDetailVisibility(bool watermarkOn)
    {
        WatermarkDivider.Visibility = watermarkOn ? Visibility.Visible : Visibility.Collapsed;
        WatermarkDetailPanel.Visibility = watermarkOn ? Visibility.Visible : Visibility.Collapsed;
        WatermarkFontPanel.Visibility = watermarkOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WatermarkToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_watermarkInitializing) return;
        var enabled = WatermarkToggle.IsOn;
        AppSettings.Set("ScreenshotWatermark", enabled);
        UpdateWatermarkDetailVisibility(enabled);
    }

    private void WatermarkTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_watermarkTextInitializing) return;
        var text = WatermarkTextBox.Text.Trim();
        AppSettings.Set("ScreenshotWatermarkText", string.IsNullOrEmpty(text) ? "图吧工具箱" : text);
    }

    private void WatermarkFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_watermarkFontInitializing) return;
        if (WatermarkFontComboBox.SelectedItem is string font)
            AppSettings.Set("ScreenshotWatermarkFont", font);
    }

    private void InitHardwareFitScreenToggle()
    {
        _hardwareFitScreenInitializing = true;
        HardwareFitScreenToggle.IsOn = AppSettings.GetBool("HardwareFitScreen", true);
        _hardwareFitScreenInitializing = false;
    }

    private void HardwareFitScreenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_hardwareFitScreenInitializing) return;
        AppSettings.Set("HardwareFitScreen", HardwareFitScreenToggle.IsOn);
    }

    private void InitHardwareMultiDeviceNewLineToggle()
    {
        _hardwareMultiDeviceNewLineInitializing = true;
        HardwareMultiDeviceNewLineToggle.IsOn = AppSettings.GetBool("HardwareMultiDeviceNewLine", false);
        _hardwareMultiDeviceNewLineInitializing = false;
    }

    private void HardwareMultiDeviceNewLineToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_hardwareMultiDeviceNewLineInitializing) return;
        AppSettings.Set("HardwareMultiDeviceNewLine", HardwareMultiDeviceNewLineToggle.IsOn);
        HardwareInfoService.InvalidateCache();
    }

    private bool _activeInterceptInitializing;
    private bool _searchIndexInitializing;

    private void InitActiveInterceptToggle()
    {
        // MSIX 沙箱下不支持主动拦截后端，隐藏相关卡片
        if (RuntimeHelper.IsMsixPackaged)
        {
            if (SettingsActiveInterceptCard is not null)
                SettingsActiveInterceptCard.Visibility = Visibility.Collapsed;
            if (SettingsActiveInterceptNotifyCard is not null)
                SettingsActiveInterceptNotifyCard.Visibility = Visibility.Collapsed;
            return;
        }

        _activeInterceptInitializing = true;
        ActiveInterceptToggle.IsOn = AppSettings.GetBool("ActiveInterceptEnabled", false);
        _activeInterceptInitializing = false;
        UpdateActiveInterceptStatus();
    }

    private void ActiveInterceptToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_activeInterceptInitializing) return;
        var enabled = ActiveInterceptToggle.IsOn;
        AppSettings.Set("ActiveInterceptEnabled", enabled);

        if (enabled)
        {
            ActiveInterceptService.Start();
        }
        else
        {
            ActiveInterceptService.Stop();
        }
        UpdateActiveInterceptStatus();
    }

    private void UpdateActiveInterceptStatus()
    {
        var enabled = AppSettings.GetBool("ActiveInterceptEnabled", false);
        if (ActiveInterceptStatusText is null) return;

        if (!enabled)
        {
            ActiveInterceptStatusText.Text = "已关闭";
            ActiveInterceptStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }
        else if (ActiveInterceptService.IsRunning)
        {
            ActiveInterceptStatusText.Text = "运行中";
            ActiveInterceptStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
        }
        else
        {
            ActiveInterceptStatusText.Text = "未运行（后端缺失）";
            ActiveInterceptStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
        }
    }

    private void InitSearchIndexToggle()
    {
        _searchIndexInitializing = true;
        SearchIndexToggle.IsOn = AppSettings.GetBool("WindowsSearchIndex", false);
        _searchIndexInitializing = false;
    }

    private void SearchIndexToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_searchIndexInitializing) return;
        var enabled = SearchIndexToggle.IsOn;
        AppSettings.Set("WindowsSearchIndex", enabled);

        if (enabled)
        {
            _ = WindowsSearchIndexService.RegisterAllToolsAsync();
        }
        else
        {
            WindowsSearchIndexService.RemoveAll();
        }
    }

    private bool _activeInterceptNotifyModeInitializing;

    private void InitActiveInterceptNotifyModeComboBox()
    {
        _activeInterceptNotifyModeInitializing = true;
        ActiveInterceptNotifyModeComboBox.Items.Add("每次拦截都通知");
        ActiveInterceptNotifyModeComboBox.Items.Add("仅批量时通知");
        ActiveInterceptNotifyModeComboBox.Items.Add("从不通知");

        var mode = AppSettings.Get("ActiveInterceptNotifyMode") ?? "always";
        ActiveInterceptNotifyModeComboBox.SelectedIndex = mode switch
        {
            "batch_only" => 1,
            "never" => 2,
            _ => 0,
        };
        _activeInterceptNotifyModeInitializing = false;
    }

    private void ActiveInterceptNotifyModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_activeInterceptNotifyModeInitializing) return;
        var mode = ActiveInterceptNotifyModeComboBox.SelectedIndex switch
        {
            1 => "batch_only",
            2 => "never",
            _ => "always",
        };
        AppSettings.Set("ActiveInterceptNotifyMode", mode);
        // 重启后端使新配置生效
        if (AppSettings.GetBool("ActiveInterceptEnabled", false))
        {
            ActiveInterceptService.Stop();
            ActiveInterceptService.Start();
        }
    }

    private void InitCpuzDataSourceStatus()
    {
        UpdateCpuzDataSourceUI();
    }

    private void UpdateCpuzDataSourceUI()
    {
        var useCpuz = AppSettings.GetBool("UseCpuzDataSource", false);
        var cpuzAvailable = CpuzInfoService.FindCpuzExe() != null;

        if (useCpuz && CpuzInfoService.CachedInfo != null)
        {
            CpuzDataSourceStatusText.Text = "当前使用 CPU-Z 数据源（真实硬件读取）";
            CpuzDataSourceButtonText.Text = "切回默认";
            CpuzDataSourceIcon.Glyph = "\uE73E";
        }
        else if (useCpuz)
        {
            CpuzDataSourceStatusText.Text = cpuzAvailable
                ? "CPU-Z 数据源已启用，等待获取数据..."
                : "CPU-Z 数据源已启用，但未找到 CPU-Z";
            CpuzDataSourceButtonText.Text = "切回默认";
            CpuzDataSourceIcon.Glyph = "\uE950;";
        }
        else
        {
            CpuzDataSourceStatusText.Text = cpuzAvailable
                ? "当前使用 WMI 数据源，可切换为 CPU-Z 获取真实信息"
                : "当前使用 WMI 数据源（未找到 CPU-Z 工具）";
            CpuzDataSourceButtonText.Text = "切换";
            CpuzDataSourceIcon.Glyph = "\uE950";
        }
    }

    private async void CpuzDataSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuzBusy) return;

        var useCpuz = AppSettings.GetBool("UseCpuzDataSource", false);

        if (useCpuz)
        {
            AppSettings.Set("UseCpuzDataSource", false);
            UpdateCpuzDataSourceUI();
            return;
        }

        var cpuzExe = CpuzInfoService.FindCpuzExe();
        if (cpuzExe == null)
        {
            await ShowMessageAsync("未找到 CPU-Z", "在工具目录中未找到 CPU-Z 可执行文件，无法使用此功能。\n\n请确保 Tools/处理器工具/CPUZ/ 目录下存在 cpuz_x64.exe。");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "切换硬件信息数据源",
            PrimaryButtonText = "确认切换",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(new TextBlock
        {
            Text = "当前硬件信息通过 WMI（Windows 管理规范）获取，数据来源于厂商在 SMBIOS/DMI 中填写的内容。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        var problemBorder = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(40, 255, 185, 0)
                    : Color.FromArgb(30, 200, 130, 0)),
            BorderBrush = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(80, 255, 185, 0)
                    : Color.FromArgb(60, 200, 130, 0)),
            BorderThickness = new Thickness(1)
        };
        problemBorder.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "⚠ WMI 数据可能被伪造",
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 14
                },
                new TextBlock
                {
                    Text = "部分厂商或商家可能通过修改 BIOS/SMBIOS 信息来伪造 CPU 型号、内存品牌、主板型号等，导致 WMI 读取到的信息与实际硬件不符。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    FontSize = 13
                }
            }
        };
        stack.Children.Add(problemBorder);

        var solutionBorder = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(40, 0, 200, 100)
                    : Color.FromArgb(25, 0, 160, 80)),
            BorderBrush = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(80, 0, 200, 100)
                    : Color.FromArgb(60, 0, 160, 80)),
            BorderThickness = new Thickness(1)
        };
        solutionBorder.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "✓ CPU-Z 读取原理",
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 14
                },
                new TextBlock
                {
                    Text = "CPU-Z 通过 CPUID 指令直接读取 CPU 硬件寄存器，通过 PCI 枚举直接扫描硬件，通过 SPD 芯片直接读取内存条信息——这些是底层硬件级别的数据，厂商无法通过修改 SMBIOS 来伪造。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    FontSize = 13
                }
            }
        };
        stack.Children.Add(solutionBorder);

        var warnBorder = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(40, 100, 150, 255)
                    : Color.FromArgb(25, 60, 120, 255)),
            BorderBrush = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(80, 100, 150, 255)
                    : Color.FromArgb(60, 60, 120, 255)),
            BorderThickness = new Thickness(1)
        };
        warnBorder.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "⏱ 注意事项",
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 14
                },
                new TextBlock
                {
                    Text = "• 使用 CPU-Z 获取信息需要约 3~8 秒，期间会短暂启动 CPU-Z 进程\n• 获取完成后会自动关闭 CPU-Z 进程\n• 切换后可在设置中随时切回 WMI 数据源",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    FontSize = 13
                }
            }
        };
        stack.Children.Add(warnBorder);

        dialog.Content = new ScrollViewer
        {
            MaxHeight = 400,
            Content = stack
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        _cpuzBusy = true;
        CpuzDataSourceButton.IsEnabled = false;
        CpuzDataSourceStatusText.Text = "正在通过 CPU-Z 获取硬件信息，请稍候...";

        try
        {
            var cpuzInfo = await CpuzInfoService.FetchAsync(timeoutMs: 30000);

            if (cpuzInfo != null)
            {
                AppSettings.Set("UseCpuzDataSource", true);
                UpdateCpuzDataSourceUI();
            }
            else
            {
                CpuzInfoService.KillCpuzProcesses();
                await ShowMessageAsync("获取失败", "CPU-Z 未能成功获取硬件信息。\n\n可能原因：\n• CPU-Z 运行超时\n• CPU-Z 被安全软件拦截\n• 当前架构不支持此版本 CPU-Z");
                UpdateCpuzDataSourceUI();
            }
        }
        catch (Exception ex)
        {
            CpuzInfoService.KillCpuzProcesses();
            await ShowMessageAsync("获取失败", $"CPU-Z 获取过程中出现错误：\n{ex.Message}");
            UpdateCpuzDataSourceUI();
        }
        finally
        {
            _cpuzBusy = false;
            CpuzDataSourceButton.IsEnabled = true;
        }
    }

    private void InitAiSettings()
    {
        _aiSettingsInitializing = true;
        try
        {
            RefreshAiProviderList();
            LoadAiProviderIntoUi(null);

            SearchApiKeyBox.Password = AppSettings.Get("SearchApiKey") ?? "";
        }
        finally
        {
            _aiSettingsInitializing = false;
        }
    }

    private AiProvider? CurrentAiProvider() => AiProviderCombo.SelectedItem as AiProvider;

    private void RefreshAiProviderList()
    {
        var providers = AiProviderStore.GetProviders();
        var selectedId = AiProviderStore.SelectedProviderId;
        // 必须传副本：传活列表实例时，列表被原地修改后 ItemsSourceView 快照不刷新，
        // 同步设置 SelectedItem 会抛 E_INVALIDARG（Value does not fall within the expected range）
        AiProviderCombo.ItemsSource = providers.ToList();
        AiProviderCombo.SelectedItem = providers.FirstOrDefault(p => p.Id == selectedId) ?? providers.FirstOrDefault();
    }

    /// <summary>把指定提供商（null = 当前选中）加载到编辑器控件。</summary>
    private void LoadAiProviderIntoUi(string? providerId)
    {
        var provider = providerId is null
            ? CurrentAiProvider() ?? AiProviderStore.SelectedProvider
            : AiProviderStore.GetProvider(providerId) ?? AiProviderStore.SelectedProvider;

        AiEndpointTextBox.Text = provider.BaseUrl ?? "";
        AiEndpointTextBox.IsEnabled = !provider.EndpointLocked;
        AiApiKeyBox.Password = provider.ApiKey ?? "";

        // ItemsSource 用同一份列表实例，保证 SelectedItem 引用一致
        var models = provider.Models.ToList();
        AiModelsList.ItemsSource = models;
        AiDefaultModelCombo.ItemsSource = models;
        AiDefaultModelCombo.SelectedItem = models
            .FirstOrDefault(m => m.Id.Equals(provider.DefaultModel, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault();

        var isZen = provider.Id == AiProviderStore.OpenCodeZenProviderId;
        AiZenSection.Visibility = isZen ? Visibility.Visible : Visibility.Collapsed;
        AiKeyLinkButton.Visibility = string.IsNullOrWhiteSpace(provider.KeyHintUrl) ? Visibility.Collapsed : Visibility.Visible;
        AiApiKeyHintText.Text = isZen
            ? "API Key（可选）：留空使用匿名免费模型（额度低）；登录获取 Key 后额度大幅提升"
            : "API 密钥，将安全保存在本地";

        if (isZen)
        {
            AiZenStatusText.Text = string.IsNullOrWhiteSpace(provider.ApiKey)
                ? "未配置 Key（匿名额度较低）"
                : $"已配置 Key：{MaskAiKey(provider.ApiKey)}（额度更高）";
        }

        UpdateAiConfigStatus();
    }

    private static string MaskAiKey(string key)
        => key.Length <= 12 ? key : $"{key[..7]}…{key[^4..]}";

    private async void AiZenLoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_zenBusy) return;
        _zenBusy = true;
        AiZenLoginButton.IsEnabled = false;
        AiZenRefreshButton.IsEnabled = false;
        AiZenLoginText.Text = "等待登录...";
        AiZenLoginIcon.Glyph = "\uE895";
        try
        {
            var key = await OpenCodeZenLoginWindow.ShowAsync();
            if (string.IsNullOrWhiteSpace(key)) return;

            var provider = AiProviderStore.GetProvider(AiProviderStore.OpenCodeZenProviderId);
            if (provider is not null)
            {
                provider.ApiKey = key;
                AiProviderStore.Save();
            }
        }
        catch (Exception ex)
        {
            AiZenStatusText.Text = $"获取 Key 失败：{ex.Message}";
        }
        finally
        {
            _zenBusy = false;
            AiZenLoginButton.IsEnabled = true;
            AiZenRefreshButton.IsEnabled = true;
            AiZenLoginText.Text = "登录并获取 Key";
            AiZenLoginIcon.Glyph = "\uE77B";
            LoadAiProviderIntoUi(null);
        }
    }

    private void UpdateAiConfigStatus()
    {
        if (AiService.IsUsingDefaultModel)
        {
            AiConfigStatusText.Text = "⚠️ 使用自带默认模型，可能出现排队/限额满速，质量低下等问题。推荐使用 DeepSeek V4 Pro。";
            AiConfigStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 251, 191, 36));
        }
        else
        {
            var provider = AiProviderStore.SelectedProvider;
            AiConfigStatusText.Text = $"已配置：{provider.Name} · {AiProviderStore.SelectedModelId}";
            AiConfigStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
        }
    }

    private void AiProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_aiSettingsInitializing) return;
        if (CurrentAiProvider() is not { } provider) return;

        AiProviderStore.SetSelected(provider.Id);
        LoadAiProviderIntoUi(provider.Id);
    }

    private void AiAddProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var provider = AiProviderStore.AddCustomProvider();
        RefreshAiProviderList();
        LoadAiProviderIntoUi(provider.Id);
    }

    private void AiResetProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentAiProvider() is not { } provider) return;
        AiProviderStore.ResetProviderDefaults(provider.Id);
        LoadAiProviderIntoUi(provider.Id);
    }

    private void AiEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_aiSettingsInitializing) return;
        if (CurrentAiProvider() is not { } provider) return;
        provider.BaseUrl = AiEndpointTextBox.Text.Trim();
        AiProviderStore.Save();
        UpdateAiConfigStatus();
    }

    private void AiApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_aiSettingsInitializing) return;
        if (CurrentAiProvider() is not { } provider) return;
        provider.ApiKey = AiApiKeyBox.Password.Trim();
        AiProviderStore.Save();
        UpdateAiConfigStatus();
    }

    private void AiKeyLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentAiProvider() is not { } provider) return;
        if (string.IsNullOrWhiteSpace(provider.KeyHintUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(provider.KeyHintUrl) { UseShellExecute = true });
        }
        catch { }
    }

    private void AiModelDelete_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentAiProvider() is not { } provider) return;
        if ((sender as Button)?.Tag is not AiModelOption model) return;

        provider.Models.Remove(model);
        if (string.IsNullOrWhiteSpace(provider.DefaultModel) || provider.DefaultModel == model.Id)
            provider.DefaultModel = provider.Models.FirstOrDefault()?.Id ?? "";

        AiProviderStore.Save();
        LoadAiProviderIntoUi(provider.Id);
    }

    private void AiAddModelButton_Click(object sender, RoutedEventArgs e) => AddAiModel();

    private void AiNewModelBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            AddAiModel();
        }
    }

    private void AddAiModel()
    {
        if (CurrentAiProvider() is not { } provider) return;

        var id = AiNewModelBox.Text.Trim();
        if (id.Length == 0) return;

        provider.AddModel(id);
        if (string.IsNullOrWhiteSpace(provider.DefaultModel))
            provider.DefaultModel = id;

        AiProviderStore.Save();
        AiNewModelBox.Text = "";
        LoadAiProviderIntoUi(provider.Id);
    }

    private void AiDefaultModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_aiSettingsInitializing) return;
        if (CurrentAiProvider() is not { } provider) return;
        if (AiDefaultModelCombo.SelectedItem is not AiModelOption model) return;

        provider.DefaultModel = model.Id;
        AiProviderStore.Save();
        UpdateAiConfigStatus();
    }

    private async void AiZenRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_zenBusy) return;
        _zenBusy = true;
        AiZenRefreshButton.IsEnabled = false;
        AiZenRefreshText.Text = "刷新中...";
        AiZenRefreshIcon.Glyph = "\uE895";
        try
        {
            var (count, error) = await OpenCodeZenAuthService.RefreshFreeModelsAsync();
            AiZenStatusText.Text = error is null
                ? $"已刷新 {count} 个免费模型"
                : $"刷新失败：{error}";
        }
        catch (Exception ex)
        {
            AiZenStatusText.Text = $"刷新失败：{ex.Message}";
        }
        finally
        {
            _zenBusy = false;
            AiZenRefreshButton.IsEnabled = true;
            AiZenRefreshText.Text = "刷新免费模型";
            AiZenRefreshIcon.Glyph = "\uE72C";
            LoadAiProviderIntoUi(null);
        }
    }

    private void SearchApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_aiSettingsInitializing) return;
        AppSettings.Set("SearchApiKey", SearchApiKeyBox.Password);
    }

    private async void AiTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aiTesting) return;
        _aiTesting = true;
        AiTestButton.IsEnabled = false;
        AiTestButtonText.Text = "测试中...";
        AiTestIcon.Glyph = "\uE950";

        try
        {
            var provider = CurrentAiProvider();
            var result = await AiService.TestConnectionAsync(
                endpoint: provider?.BaseUrl,
                model: provider?.DefaultModel,
                apiKey: provider?.ApiKey);

            if (result.Success)
            {
                AiTestIcon.Glyph = "\uE73E";
                AiTestButtonText.Text = "连接成功";
                AiConfigStatusText.Text = "AI 服务已配置，连接测试成功";
                AiConfigStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
            }
            else
            {
                AiTestIcon.Glyph = "\uE783";
                AiTestButtonText.Text = "连接失败";
                AiConfigStatusText.Text = $"连接失败：{result.Error}";
                AiConfigStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);

                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "AI 连接测试失败",
                    Content = new ScrollViewer
                    {
                        MaxHeight = 200,
                        Content = new TextBlock
                        {
                            Text = result.Error ?? "未知错误",
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 13
                        }
                    },
                    CloseButtonText = "确定",
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                await dialog.ShowAsync();
            }
        }
        finally
        {
            _aiTesting = false;
            AiTestButton.IsEnabled = true;

            await Task.Delay(2000);

            if (!_aiTesting)
            {
                AiTestIcon.Glyph = "\uE73E";
                AiTestButtonText.Text = "测试连接";
            }
        }
    }

    private void InitProxySettings()
    {
        _proxySettingsInitializing = true;
        
        var proxyEnabled = ProxyService.IsProxyEnabled;
        ProxyToggle.IsOn = proxyEnabled;
        
        ProxyAddressTextBox.Text = ProxyService.ProxyAddress ?? "";
        ProxyUsernameTextBox.Text = ProxyService.ProxyUsername ?? "";
        ProxyPasswordBox.Password = ProxyService.ProxyPassword ?? "";
        
        UpdateProxyPanelVisibility(proxyEnabled);
        
        _proxySettingsInitializing = false;
    }

    private void UpdateProxyPanelVisibility(bool enabled)
    {
        ProxyDivider.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ProxyAddressPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ProxyAuthPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ProxyTestPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        
        UpdateProxyStatus();
    }

    private void UpdateProxyStatus()
    {
        if (!ProxyService.IsProxyEnabled)
        {
            ProxyStatusText.Text = "配置 HTTP/HTTPS 代理，所有网络请求将使用代理";
            return;
        }
        
        var address = ProxyService.ProxyAddress;
        if (string.IsNullOrWhiteSpace(address))
        {
            ProxyStatusText.Text = "代理已启用，但未配置地址";
            return;
        }
        
        var hasAuth = !string.IsNullOrWhiteSpace(ProxyService.ProxyUsername);
        ProxyStatusText.Text = hasAuth
            ? $"代理已启用：{address}（已配置认证）"
            : $"代理已启用：{address}";
    }

    private void ProxyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_proxySettingsInitializing) return;
        
        var enabled = ProxyToggle.IsOn;
        AppSettings.Set("ProxyEnabled", enabled);
        UpdateProxyPanelVisibility(enabled);
    }

    private void ProxyAddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_proxySettingsInitializing) return;
        AppSettings.Set("ProxyAddress", ProxyAddressTextBox.Text.Trim());
        UpdateProxyStatus();
    }

    private void ProxyUsernameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_proxySettingsInitializing) return;
        AppSettings.Set("ProxyUsername", ProxyUsernameTextBox.Text.Trim());
        UpdateProxyStatus();
    }

    private void ProxyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_proxySettingsInitializing) return;
        AppSettings.Set("ProxyPassword", ProxyPasswordBox.Password);
        UpdateProxyStatus();
    }

    private async void ProxyTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_proxyTesting) return;
        
        var address = ProxyAddressTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            await ShowMessageAsync("无法测试", "请先输入代理地址");
            return;
        }
        
        _proxyTesting = true;
        ProxyTestButton.IsEnabled = false;
        ProxyTestIcon.Glyph = "\uE950";
        ProxyTestButtonText.Text = "测试中...";
        ProxyTestStatusText.Text = "正在测试代理连接...";
        
        try
        {
            using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(15));
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ProxyTest");
            
            var testUrls = new[]
            {
                "https://www.google.com/favicon.ico",
                "https://github.com/favicon.ico",
                "https://api.github.com"
            };
            
            Exception? lastError = null;
            foreach (var url in testUrls)
            {
                try
                {
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Redirect)
                    {
                        ProxyTestIcon.Glyph = "\uE73E";
                        ProxyTestButtonText.Text = "连接成功";
                        ProxyTestStatusText.Text = $"代理连接成功（{response.StatusCode}）";
                        ProxyTestStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }
            
            ProxyTestIcon.Glyph = "\uE783";
            ProxyTestButtonText.Text = "连接失败";
            ProxyTestStatusText.Text = lastError?.Message ?? "无法连接代理服务器";
            ProxyTestStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
        }
        catch (Exception ex)
        {
            ProxyTestIcon.Glyph = "\uE783";
            ProxyTestButtonText.Text = "连接失败";
            ProxyTestStatusText.Text = ex.Message;
            ProxyTestStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
        }
        finally
        {
            _proxyTesting = false;
            ProxyTestButton.IsEnabled = true;
            
            await Task.Delay(3000);
            
            if (!_proxyTesting)
            {
                ProxyTestIcon.Glyph = "\uE73E";
                ProxyTestButtonText.Text = "测试连接";
                ProxyTestStatusText.Foreground = (Brush)App.Current.Resources["TextFillColorSecondaryBrush"];
            }
        }
    }

    private void ConfigManagerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfigManagerDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        _ = dialog.ShowAsync();
    }

    private async void ExportAppButton_Click(object sender, RoutedEventArgs e)
    {
        var exportPath = PickSaveFile("导出当前软件", "压缩包\0*.zip\0所有文件\0*.*\0\0", "TubaWinUi3-Custom.zip", "zip");
        if (string.IsNullOrWhiteSpace(exportPath))
            return;

        if (!exportPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            exportPath += ".zip";

        ExportAppButton.IsEnabled = false;
        ExportAppStatusText.Text = "正在打包当前软件...";

        try
        {
            await CustomToolPackageService.ExportCurrentAppAsync(exportPath);
            ExportAppStatusText.Text = $"已导出 {Path.GetFileName(exportPath)}";
            await ShowMessageAsync("导出完成", $"已保存到：\n{exportPath}");
        }
        catch (Exception ex)
        {
            ExportAppStatusText.Text = $"导出失败: {ex.Message}";
            await ShowMessageAsync("导出失败", ex.Message);
        }
        finally
        {
            ExportAppButton.IsEnabled = true;
        }
    }

    private static string? PickSaveFile(string title, string filter, string defaultFileName, string defaultExtension)
    {
        var buffer = defaultFileName + new string('\0', 1024 - defaultFileName.Length);
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
            lpstrFilter = filter,
            lpstrFile = buffer,
            nMaxFile = 1024,
            lpstrTitle = title,
            lpstrDefExt = defaultExtension,
            Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            nFilterIndex = 1
        };

        return GetSaveFileName(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }

    private async void InitGitHubLoginStatus()
    {
        try
        {
            if (GitHubAuthService.IsLoggedIn)
            {
                var user = await GitHubAuthService.GetCurrentUserAsync();
                if (user is not null)
                {
                    GitHubLoginStatusText.Text = $"已登录：{user.Name ?? user.Login}";
                    GitHubLoginButton.Visibility = Visibility.Collapsed;
                    GitHubLogoutButton.Visibility = Visibility.Visible;
                    GitHubAvatar.Visibility = Visibility.Visible;

                    if (!string.IsNullOrWhiteSpace(user.AvatarUrl))
                    {
                        GitHubAvatar.ProfilePicture = new BitmapImage(new Uri(user.AvatarUrl));
                    }
                    return;
                }
            }

            GitHubLoginStatusText.Text = "未登录";
            GitHubLoginButton.Visibility = Visibility.Visible;
            GitHubLogoutButton.Visibility = Visibility.Collapsed;
            GitHubAvatar.Visibility = Visibility.Collapsed;
        }
        catch
        {
            GitHubLoginStatusText.Text = "未登录";
        }
    }

    private async void GitHubLoginButton_Click(object sender, RoutedEventArgs e)
    {
        await GitHubAuthService.StartDeviceFlowAsync(XamlRoot);
        InitGitHubLoginStatus();
    }

    private void GitHubLogoutButton_Click(object sender, RoutedEventArgs e)
    {
        GitHubAuthService.Logout();
        InitGitHubLoginStatus();
    }

	private async void CommunitySubmitButton_Click(object sender, RoutedEventArgs e)
	{
		var tool = BuiltinToolRegistry.GetById("community-tools");
		if (tool is not null)
		{
			var context = new BuiltinToolContext
			{
				XamlRoot = XamlRoot,
				CancellationToken = CancellationToken.None
			};
			MainWindow.ActiveToolName = tool.Name;
			try { await tool.ExecuteAsync(context); } catch { }
			finally { MainWindow.ActiveToolName = null; }
		}
	}

    private async void FeedbackButton_Click(object sender, RoutedEventArgs e)
    {
        const string repoIssuesUrl = "https://github.com/luolangaga/tubatool/issues/new";

        var descriptionBox = new TextBox
        {
            PlaceholderText = "请描述您的问题或建议...",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            MaxHeight = 160,
            FontSize = 13,
        };

        var stepsBox = new TextBox
        {
            PlaceholderText = "1. 打开xxx页面\n2. 点击xxx按钮\n3. 出现xxx问题",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            MaxHeight = 160,
            FontSize = 13,
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "问题描述", FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 14 });
        panel.Children.Add(descriptionBox);
        panel.Children.Add(new TextBlock { Text = "复现步骤 *必填", FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 14 });
        panel.Children.Add(stepsBox);

        while (true)
        {
            var dialog = new ContentDialog
            {
                Title = "提交反馈",
                Content = panel,
                PrimaryButtonText = "提交",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var steps = stepsBox.Text.Trim();
            if (string.IsNullOrEmpty(steps))
            {
                var warn = new ContentDialog
                {
                    Title = "请填写复现步骤",
                    Content = "提交反馈前请描述复现步骤，这能帮助我们快速定位和修复问题。",
                    CloseButtonText = "返回填写",
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme,
                };
                await warn.ShowAsync();
                continue;
            }

            var description = descriptionBox.Text.Trim();
            var descSection = string.IsNullOrEmpty(description) ? "" : $"## 描述\n\n{description}\n\n";
            var body = Uri.EscapeDataString(
                descSection +
                "## 复现步骤\n\n" + steps + "\n\n" +
                "## 系统信息\n\n```\n" + GetSystemInfoForFeedback() + "\n```\n");
            var url = $"{repoIssuesUrl}?title=[反馈]+&body={body}";
            await global::Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            return;
        }
    }

    private static string GetSystemInfoForFeedback()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            var osVersion = Environment.OSVersion.VersionString;
            var arch = RuntimeInformation.ProcessArchitecture;
            return $"App: {version}\nOS: {osVersion}\nArch: {arch}";
        }
        catch
        {
            return "Unable to get system info";
        }
    }

    private void LoadCreditsAvatar()
    {
        try
        {
            AuthorAvatar.ProfilePicture = new BitmapImage(new Uri("https://github.com/luolangaga.png"));
        }
        catch
        {
        }
    }

    private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
    {
        DrawerOverlay.Visibility = Visibility.Visible;
        if (FastModeService.IsFastModeEnabled())
        {
            DrawerOverlayBackground.Opacity = 1;
            DrawerPanelTransform.X = 0;
        }
        else
        {
            DrawerOpenStoryboard.Begin();
        }
    }

    private void DrawerCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseDrawer();
    }

    private void DrawerOverlayBackground_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CloseDrawer();
    }

    private void CloseDrawer()
    {
        if (FastModeService.IsFastModeEnabled())
        {
            DrawerOverlay.Visibility = Visibility.Collapsed;
            DrawerOverlayBackground.Opacity = 0;
            DrawerPanelTransform.X = 420;
            return;
        }
        DrawerCloseStoryboard.Completed += OnDrawerCloseCompleted;
        DrawerCloseStoryboard.Begin();
    }

    private void OnDrawerCloseCompleted(object? sender, object e)
    {
        DrawerCloseStoryboard.Completed -= OnDrawerCloseCompleted;
        DrawerOverlay.Visibility = Visibility.Collapsed;
    }

    private void ThrowErrorButton_Click(object sender, RoutedEventArgs e)
    {
        throw new InvalidOperationException("这是一条手动抛出的测试异常，用于验证全局错误页面是否正常工作。");
    }

    private int _easterEggClickCount;
    private CancellationTokenSource? _easterEggCts;
    private static readonly string[] EasterEggMessages =
    [
        "被你发现啦～ 🎉",
        "呜呜别戳我啦 >_<",
        "再戳就要坏掉了哦～",
        "嘻嘻，你真有耐心呢 ✨",
        "今天也要元气满满鸭！",
        "偷偷告诉你：开发者很可爱 🤫",
        "戳我干嘛～看配置去啦！",
        "我是一只工具箱喵～ 🐱",
        "你点我一下，我开心一下 ☺️",
        "好啦好啦，知道你在啦～",
    ];

    private void AppInfoCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (FastModeService.IsFastModeEnabled()) return;

        _easterEggCts?.Cancel();
        _easterEggCts = new CancellationTokenSource();
        var ct = _easterEggCts.Token;

        _easterEggClickCount++;
        var idx = (_easterEggClickCount - 1) % EasterEggMessages.Length;

        AppInfoCardScale.ScaleX = 0.92;
        AppInfoCardScale.ScaleY = 1.08;
        AppTitleText.Text = "🎉 " + EasterEggMessages[idx];
        AppSubtitleText.Opacity = 0.5;

        var bounce = new Storyboard();
        var sx = new DoubleAnimation { From = 0.92, To = 1.0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
        var sy = new DoubleAnimation { From = 1.08, To = 1.0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
        Storyboard.SetTarget(sx, AppInfoCardScale);
        Storyboard.SetTargetProperty(sx, "ScaleX");
        Storyboard.SetTarget(sy, AppInfoCardScale);
        Storyboard.SetTargetProperty(sy, "ScaleY");
        bounce.Children.Add(sx);
        bounce.Children.Add(sy);
        bounce.Begin();

        _ = RestoreEasterEggAsync(ct);
    }

    private async Task RestoreEasterEggAsync(CancellationToken ct)
    {
        try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;

        AppInfoCardScale.ScaleX = 0.95;
        AppInfoCardScale.ScaleY = 1.05;
        AppTitleText.Text = "图吧工具箱";
        AppSubtitleText.Opacity = 1.0;

        var restore = new Storyboard();
        var rx = new DoubleAnimation { From = 0.95, To = 1.0, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
        var ry = new DoubleAnimation { From = 1.05, To = 1.0, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
        Storyboard.SetTarget(rx, AppInfoCardScale);
        Storyboard.SetTargetProperty(rx, "ScaleX");
        Storyboard.SetTarget(ry, AppInfoCardScale);
        Storyboard.SetTargetProperty(ry, "ScaleY");
        restore.Children.Add(rx);
        restore.Children.Add(ry);
        restore.Begin();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "确定",
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        await dialog.ShowAsync();
    }

    private static string GetDefaultHttpDownloadPath()
        => Path.Combine(ConfigManager.GetDataDir(), "download");

    private static string GetHttpDownloadPath()
        => PathResolver.MakeAbsolute(AppSettings.Get("HttpDownloadPath")) ?? GetDefaultHttpDownloadPath();

    private void InitHttpDownloadSettings()
    {
        HttpDownloadPathText.Text = GetHttpDownloadPath();

        HttpDownloadActionComboBox.ItemsSource = new[]
        {
            new { Key = "none", Label = "仅下载" },
            new { Key = "extract", Label = "下载并解压" },
            new { Key = "install", Label = "下载并运行" },
        };
        HttpDownloadActionComboBox.DisplayMemberPath = "Label";
        HttpDownloadActionComboBox.SelectedValuePath = "Key";

        var savedAction = AppSettings.Get("HttpDownloadAction") ?? "none";
        HttpDownloadActionComboBox.SelectedValue = savedAction;
        if (HttpDownloadActionComboBox.SelectedIndex < 0)
            HttpDownloadActionComboBox.SelectedIndex = 0;

        HttpDownloadActionComboBox.SelectionChanged += (_, _) =>
        {
            var selected = HttpDownloadActionComboBox.SelectedValue as string;
            if (selected is not null)
                AppSettings.Set("HttpDownloadAction", selected);
        };

        UpdateDownloadQueueStatus();
    }

    private void UpdateDownloadQueueStatus()
    {
        var pending = DownloadQueueService.PendingCount;
        var total = DownloadQueueService.Queue.Count;
        DispatcherQueue.TryEnqueue(() =>
        {
            HttpDownloadQueueStatusText.Text = pending > 0
                ? $"队列中 {total} 项，{pending} 项待下载"
                : total > 0
                    ? $"队列中 {total} 项，全部完成"
                    : "队列为空";
        });
    }

    private void HttpDownloadBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dir = Win32Dialogs.PickFolder();
        if (string.IsNullOrEmpty(dir))
            return;

        AppSettings.Set("HttpDownloadPath", dir);
        HttpDownloadPathText.Text = dir;
    }

    private void HttpDownloadResetPathButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Remove("HttpDownloadPath");
        HttpDownloadPathText.Text = GetDefaultHttpDownloadPath();
    }

    private void HttpDownloadAddButton_Click(object sender, RoutedEventArgs e)
    {
        var url = HttpDownloadUrlTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            _ = ShowMessageAsync("提示", "请输入下载链接");
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _ = ShowMessageAsync("提示", "请输入有效的 HTTP/HTTPS 链接");
            return;
        }

        var destPath = GetHttpDownloadPath();
        Directory.CreateDirectory(destPath);

        var action = AppSettings.Get("HttpDownloadAction") ?? "none";
        IDownloadPostProcessor? postProcessor = action switch
        {
            "extract" => new ArchiveExtractProcessor(),
            "install" => new InstallerLaunchProcessor(),
            _ => null
        };

        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('?') || fileName.Contains('='))
            fileName = null;

        var displayName = fileName ?? $"下载文件 {DateTime.Now:HH:mm:ss}";

        DownloadQueueService.Enqueue(displayName, url, destPath, postProcessor,
            description: url, glyph: "\uE896");

        HttpDownloadUrlTextBox.Text = "";
        UpdateDownloadQueueStatus();

        _ = ShowMessageAsync("已加入下载", $"\"{displayName}\" 已加入下载队列\n保存至：{destPath}");
    }

    private Flyout? _downloadFlyout;

    private void HttpDownloadViewQueueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadFlyout is null)
        {
            _downloadFlyout = new Flyout
            {
                Content = new DownloadQueueFlyout(),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
            };
        }
        _downloadFlyout.ShowAt(sender as FrameworkElement);
    }
}
