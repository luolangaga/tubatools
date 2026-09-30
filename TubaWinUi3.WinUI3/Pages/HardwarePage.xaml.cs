using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

public sealed partial class HardwarePage : Page, ILocalizablePage
{
    private DispatcherTimer? _uptimeTimer;
    private bool _dataLoaded;
    private bool _animatingDetails;
    private bool _nicknameMode;
    private bool _isAnimatingNickname;
    private IReadOnlyList<HardwareInfoSection>? _currentSections;
    private readonly Dictionary<HardwareInfoItem, string> _originalValues = [];

    private static SvgImageSource? IntelLogo;
    private static SvgImageSource? AmdLogo;
    private static SvgImageSource? NvidiaLogo;
    private static SvgImageSource? AppleLogo;
    private static SvgImageSource? QualcommLogo;
    private static bool _logosLoaded;

    public HardwarePage()
    {
        InitializeComponent();
        Loaded += HardwarePage_Loaded;
        Unloaded += HardwarePage_Unloaded;
        LoadBrandLogos();
        ApplyCardToolTips();
        AppSettings.SettingChanged += OnSettingChanged;
    }

    private void OnSettingChanged(string key)
    {
        if (key == "UseCpuzDataSource" || key == "HardwareFitScreen" || key == "HardwareMultiDeviceNewLine")
        {
            if (key == "HardwareFitScreen")
                _currentLayoutFitScreen = null;
            _ = LoadHardwareInfoAsync(forceRefresh: true);
        }
    }

    private static void LoadBrandLogos()
    {
        if (_logosLoaded) return;
        _logosLoaded = true;

        var brandsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Brands");
        IntelLogo = LoadSvg(Path.Combine(brandsDir, "intel.svg"));
        AmdLogo = LoadSvg(Path.Combine(brandsDir, "amd.svg"));
        NvidiaLogo = LoadSvg(Path.Combine(brandsDir, "nvidia.svg"));
        AppleLogo = LoadSvg(Path.Combine(brandsDir, "apple.svg"));
        QualcommLogo = LoadSvg(Path.Combine(brandsDir, "qualcomm.svg"));
    }

    private static SvgImageSource? LoadSvg(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var uri = new Uri($"ms-appx:///Assets/Brands/{Path.GetFileName(path)}");
            return new SvgImageSource(uri);
        }
        catch { return null; }
    }

    private static SvgImageSource? GetBrandLogo(string? brandKey) => brandKey?.ToLowerInvariant() switch
    {
        "intel" => IntelLogo,
        "amd" => AmdLogo,
        "nvidia" => NvidiaLogo,
        "apple" => AppleLogo,
        "qualcomm" => QualcommLogo,
        _ => null
    };

    private void HardwarePage_Loaded(object sender, RoutedEventArgs e)
    {
        StartUptimeTimer();
    }

    private void StartUptimeTimer()
    {
        _uptimeTimer?.Stop();
        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => UpdateUptime();
        _uptimeTimer.Start();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        StartUptimeTimer();
        // WMI 盘点在首次打开本页时后台执行（LoadAsync 自带缓存与并发合并，页面显示 loading）
        _ = LoadHardwareInfoAsync();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _uptimeTimer?.Stop();
        _uptimeTimer = null;
    }

    private void HardwarePage_Unloaded(object sender, RoutedEventArgs e)
    {
        _uptimeTimer?.Stop();
        _uptimeTimer = null;
        AppSettings.SettingChanged -= OnSettingChanged;
    }

    private void UpdateUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        UptimeText.Text = string.Format(LocalizationService.L("Hw_UptimeFormat", "{0}天{1}小时{2}分钟{3}秒"), uptime.Days, uptime.Hours, uptime.Minutes, uptime.Seconds);
    }

    /// <summary>语言切换后刷新卡片提示与详情列表（卡片 ToolTip 是附加属性，不能走 Uid）。</summary>
    public void ApplyLocalization()
    {
        ApplyCardToolTips();
        if (_currentSections is not null)
            ApplySections(_currentSections);
    }

    private void ApplyCardToolTips()
    {
        var hint = LocalizationService.L("Hw_CopyHint", "点击复制");
        ToolTipService.SetToolTip(Card1, hint);
        ToolTipService.SetToolTip(Card2, hint);
        ToolTipService.SetToolTip(Card3, hint);
    }
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadHardwareInfoAsync(forceRefresh: true);
    }

    private void DetailButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(HardwareDetailPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    private void TitleText_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _ = ToggleNicknameWithAnimationAsync();
    }

    private async Task ToggleNicknameWithAnimationAsync()
    {
        if (_currentSections is null || _isAnimatingNickname) return;
        _isAnimatingNickname = true;
        _nicknameMode = !_nicknameMode;

        var details = _currentSections[2].Items;

        foreach (var item in details)
        {
            if (!_originalValues.ContainsKey(item))
                _originalValues[item] = item.Value;
            if (item.NicknameValue is null)
                item.NicknameValue = BrandNicknameService.ApplyNickname(_originalValues[item]);
        }

        var summary = _currentSections[0].Items;
        var modelItem = summary.FirstOrDefault(i => i.Label == "设备型号");
        if (modelItem is not null)
        {
            if (!_originalValues.ContainsKey(modelItem))
                _originalValues[modelItem] = modelItem.Value;
            if (modelItem.NicknameValue is null)
                modelItem.NicknameValue = BrandNicknameService.ApplyNickname(_originalValues[modelItem]);
        }

        var valueTexts = new List<TextBlock>();
        for (int i = 0; i < DetailsRepeater.ItemsSourceView.Count; i++)
        {
            if (DetailsRepeater.TryGetElement(i) is Grid row)
            {
                var vt = FindChildByName<TextBlock>(row, "ValueText");
                if (vt is not null)
                    valueTexts.Add(vt);
            }
        }

        var eraseDuration = 180;
        var writeDuration = 250;
        var stagger = 40;

        foreach (var vt in valueTexts)
        {
            var sb = new Storyboard();
            var anim = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(eraseDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(anim, vt);
            Storyboard.SetTargetProperty(anim, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
            sb.Children.Add(anim);
            sb.Begin();
        }

        if (valueTexts.Count > 0)
            await Task.Delay(eraseDuration + 20);

        for (int i = 0; i < valueTexts.Count && i < details.Count; i++)
        {
            var newText = _nicknameMode ? details[i].NicknameValue! : _originalValues[details[i]];
            valueTexts[i].Text = newText;
            details[i].Value = newText;
        }

        if (modelItem is not null)
            ModelText.Text = _nicknameMode ? modelItem.NicknameValue! : _originalValues[modelItem];

        for (int i = 0; i < valueTexts.Count; i++)
        {
            var vt = valueTexts[i];
            var delay = TimeSpan.FromMilliseconds(i * stagger);

            var timer = new DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var sb = new Storyboard();
                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(writeDuration),
                    EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(anim, vt);
                Storyboard.SetTargetProperty(anim, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
                sb.Children.Add(anim);
                sb.Begin();
            };
            timer.Start();
        }

        var totalWriteTime = valueTexts.Count * stagger + writeDuration;
        await Task.Delay(totalWriteTime);

        _isAnimatingNickname = false;

        ShowStatusBar(
            _nicknameMode ? LocalizationService.L("Hw_NicknameModeOn", "彩蛋模式") : LocalizationService.L("Hw_NicknameModeOff", "正常模式"),
            _nicknameMode ? LocalizationService.L("Hw_NicknameOnHint", "品牌戏称已开启，点击标题恢复") : LocalizationService.L("Hw_NicknameOffHint", "品牌戏称已关闭"),
            _nicknameMode ? InfoBarSeverity.Informational : InfoBarSeverity.Success);
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (FastModeService.IsFastModeEnabled()) return;
        if (sender is not Border border) return;
        var sb = new Storyboard();
        var scaleX = new DoubleAnimation { To = 1.02, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var scaleY = new DoubleAnimation { To = 1.02, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(scaleX, border);
        Storyboard.SetTarget(scaleY, border);
        Storyboard.SetTargetProperty(scaleX, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        Storyboard.SetTargetProperty(scaleY, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);
        sb.Begin();
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (FastModeService.IsFastModeEnabled()) return;
        if (sender is not Border border) return;
        var sb = new Storyboard();
        var scaleX = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var scaleY = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(scaleX, border);
        Storyboard.SetTarget(scaleY, border);
        Storyboard.SetTargetProperty(scaleX, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        Storyboard.SetTargetProperty(scaleY, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);
        sb.Begin();
    }

    private async Task LoadHardwareInfoAsync(bool forceRefresh = false)
    {
        if (_dataLoaded)
        {
            if (FastModeService.IsFastModeEnabled())
            {
                SetElementStatesToExit();
            }
            else
            {
                ExitStoryboard.Begin();
                await Task.Delay(200);
            }
        }

        SetLoading(true);

        try
        {
            var sections = await HardwareInfoService.LoadAsync(forceRefresh);

            var useCpuz = AppSettings.GetBool("UseCpuzDataSource", false);
            if (useCpuz)
            {
                var cpuzInfo = CpuzInfoService.CachedInfo;
                if (cpuzInfo == null)
                {
                    try
                    {
                        cpuzInfo = await CpuzInfoService.FetchAsync(timeoutMs: 30000);
                    }
                    catch { }
                }

                if (cpuzInfo != null)
                {
                    sections = HardwareInfoService.ApplyCpuzOverride(sections, cpuzInfo);
                }
            }

            ApplySections(sections);
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ModelText.Text = LocalizationService.L("Hw_Unknown", "未知");
            SystemText.Text = LocalizationService.L("Hw_Unknown", "未知");
            UptimeText.Text = LocalizationService.L("Hw_Unknown", "未知");
            DetailsRepeater.ItemsSource = Array.Empty<HardwareInfoItem>();
            ShowStatusBar(LocalizationService.L("Hw_LoadFailed", "硬件信息读取失败"), ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ApplySections(IReadOnlyList<HardwareInfoSection> sections)
    {
        UpdateLayoutStructure();

        _currentSections = sections;
        _nicknameMode = false;
        _originalValues.Clear();

        var summary = sections[0].Items;
        var system = sections[1].Items;
        var details = sections[2].Items;

        ModelText.Text = summary.FirstOrDefault(item => item.Label == "设备型号")?.Value ?? LocalizationService.L("Hw_Unknown", "未知");
        SystemText.Text = system.FirstOrDefault(item => item.Label == "系统")?.Value ?? LocalizationService.L("Hw_Unknown", "未知");
        UpdateUptime();
        _animatingDetails = !FastModeService.IsFastModeEnabled();
        DetailsRepeater.ItemsSource = details;

        CpuzBadge.Visibility = details.Any(it => it.IsVerified)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (FastModeService.IsFastModeEnabled())
        {
            SetElementStatesToVisible();
        }
        else
        {
            EntranceStoryboard.Begin();
        }
        _dataLoaded = true;
    }

    private void SetElementStatesToVisible()
    {
        HeaderPanel.Opacity = 1;
        HeaderPanel.RenderTransform = new TranslateTransform { Y = 0 };
        MetricsPanel.Opacity = 1;
        MetricsPanel.RenderTransform = new TranslateTransform { Y = 0 };
        Card1.RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        Card2.RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        Card3.RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        DetailsPanel.Opacity = 1;
        DetailsPanel.RenderTransform = new TranslateTransform { Y = 0 };
    }

    private void SetElementStatesToExit()
    {
        HeaderPanel.Opacity = 0;
        MetricsPanel.Opacity = 0;
        DetailsPanel.Opacity = 0;
    }

    private void SetLoading(bool isLoading)
    {
        LoadingRing.IsActive = isLoading;
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Card1_Tapped(object sender, TappedRoutedEventArgs e) => TryCopyToClipboard(ModelText.Text);
    private void Card2_Tapped(object sender, TappedRoutedEventArgs e) => TryCopyToClipboard(SystemText.Text);
    private void Card3_Tapped(object sender, TappedRoutedEventArgs e) => TryCopyToClipboard(UptimeText.Text);

    private void DetailItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
    }

    private void DetailItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not HardwareInfoItem item) return;
        TryCopyToClipboard(item.Value);
    }

    /// <summary>
    /// 复制并给出反馈。剪贴板被占用是瞬时状态，失败重试由 ClipboardService 负责；
    /// 这里只负责把最终结果翻译成用户能看懂的状态栏提示（失败也不抛，避免闪退）。
    /// </summary>
    private bool TryCopyToClipboard(string text)
    {
        var result = ClipboardService.TrySetText(text);
        if (result.Success)
        {
            ShowCopyToast(text);
            return true;
        }

        ShowStatusBar(
            LocalizationService.L("Hw_CopyFailed", "复制失败"),
            LocalizationService.L("Hw_CopyBusyRetry", "复制失败：剪贴板被其他程序占用，请稍后重试"),
            InfoBarSeverity.Warning);
        return false;
    }

    private DispatcherTimer? _statusBarTimer;

    private void ShowCopyToast(string text)
    {
        StatusBar.Title = LocalizationService.L("Hw_Copied", "已复制");
        StatusBar.Message = text.Length > 80 ? text[..80] + "…" : text;
        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.IsOpen = true;

        _statusBarTimer?.Stop();
        _statusBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusBarTimer.Tick += (s, e) =>
        {
            StatusBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _statusBarTimer.Start();
    }

    private void DetailsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Index < 0 || args.Element is not Grid el) return;

        if (args.Index % 2 == 1)
        {
            var brush = App.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var b) ? b : null;
            if (brush is not null) el.Background = (Microsoft.UI.Xaml.Media.Brush)brush;
        }

        if (DetailsRepeater.ItemsSource is IReadOnlyList<HardwareInfoItem> items && args.Index < items.Count)
        {
            var item = items[args.Index];

            var logoImage = FindChild<Microsoft.UI.Xaml.Controls.Image>(el);
            if (logoImage is not null)
            {
                var showLogo = AppSettings.GetBool("ShowBrandLogo", true);
                if (showLogo && !string.IsNullOrEmpty(item.BrandKey))
                {
                    var logo = GetBrandLogo(item.BrandKey);
                    if (logo is not null)
                    {
                        logoImage.Source = logo;
                        logoImage.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        logoImage.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    logoImage.Visibility = Visibility.Collapsed;
                }
            }

            var verifiedBadge = FindChildByName<Border>(el, "VerifiedBadge");
            if (verifiedBadge is not null)
            {
                verifiedBadge.Visibility = item.IsVerified
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        if (!_animatingDetails)
        {
            el.Opacity = 1;
            return;
        }

        var idx = (int)args.Index;
        el.Opacity = 0;

        var delay = TimeSpan.FromMilliseconds(350 + idx * 60);
        var lastIdx = ((IReadOnlyList<HardwareInfoItem>)DetailsRepeater.ItemsSource!).Count - 1;

        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            var sb = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, el);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);

            sb.Begin();

            if (idx == lastIdx) _animatingDetails = false;
        };
        timer.Start();
    }

    private bool _isScreenshotting;

    private void CopyTextButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TryCopyToClipboard(BuildTextExport()))
                ShowStatusBar(LocalizationService.L("Hw_PlainCopied", "纯文字已复制"), LocalizationService.L("Hw_PlainCopiedMsg", "硬件信息文本已复制到剪贴板"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatusBar(LocalizationService.L("Hw_CopyFailed", "复制失败"), ex.Message, InfoBarSeverity.Error);
        }
    }

    private void CopyMarkdownButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TryCopyToClipboard(BuildMarkdownExport()))
                ShowStatusBar(LocalizationService.L("Hw_MarkdownCopied", "Markdown 已复制"), LocalizationService.L("Hw_MarkdownCopiedMsg", "硬件信息 Markdown 已复制到剪贴板"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatusBar(LocalizationService.L("Hw_CopyFailed", "复制失败"), ex.Message, InfoBarSeverity.Error);
        }
    }

    /// <summary>将当前硬件信息汇总为纯文字文本。</summary>
    private string BuildTextExport()
    {
        var sections = _currentSections;
        if (sections == null || sections.Count == 0)
            return LocalizationService.L("Hw_NoData", "暂无硬件信息");

        var sb = new StringBuilder();
        var isFirst = true;
        foreach (var section in sections)
        {
            if (!isFirst) sb.AppendLine();
            isFirst = false;
            sb.AppendLine($"【{section.Title}】");
            foreach (var item in section.Items)
            {
                sb.AppendLine($"{item.Label}: {item.Value}");
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>将当前硬件信息汇总为 Markdown 文本。</summary>
    private string BuildMarkdownExport()
    {
        var sections = _currentSections;
        if (sections == null || sections.Count == 0)
            return LocalizationService.L("Hw_NoData", "暂无硬件信息");

        var sb = new StringBuilder();
        var isFirst = true;
        foreach (var section in sections)
        {
            if (!isFirst) sb.AppendLine();
            isFirst = false;
            sb.AppendLine($"## {section.Title}");
            sb.AppendLine();
            sb.AppendLine(LocalizationService.L("Hw_MarkdownHeader", "| 项目 | 详情 |"));
            sb.AppendLine("| --- | --- |");
            foreach (var item in section.Items)
            {
                sb.AppendLine($"| {EscapeMarkdownCell(item.Label)} | {EscapeMarkdownCell(item.Value)} |");
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string EscapeMarkdownCell(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace("\r", " ")
            .Replace("\n", "<br>");
    }

    private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isScreenshotting) return;
        _isScreenshotting = true;

        try
        {
            var statusWasOpen = StatusBar.IsOpen;
            StatusBar.IsOpen = false;

            HeaderButtons.Visibility = Visibility.Collapsed;

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(LayoutRoot);

            HeaderButtons.Visibility = Visibility.Visible;

            if (statusWasOpen) StatusBar.IsOpen = true;

            var pixelWidth = rtb.PixelWidth;
            var pixelHeight = rtb.PixelHeight;
            var pixels = await GetPixelsAsync(rtb);

            using var contentBmp = new Bitmap(pixelWidth, pixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bmpData = contentBmp.LockBits(new System.Drawing.Rectangle(0, 0, pixelWidth, pixelHeight), ImageLockMode.WriteOnly, contentBmp.PixelFormat);
            Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            contentBmp.UnlockBits(bmpData);

            Bitmap? bgBmp = null;
            float bgOpacity = 0.15f;
            var mainWindowBg = (App.MainWindow as MainWindow)?.GetBackgroundImage();
            if (mainWindowBg is { Visibility: Visibility.Visible } && mainWindowBg.Source is not null)
            {
                bgOpacity = (float)mainWindowBg.Opacity;
                var bgRtb = new RenderTargetBitmap();
                await bgRtb.RenderAsync(mainWindowBg);
                var bgPixels = await GetPixelsAsync(bgRtb);
                bgBmp = new Bitmap(bgRtb.PixelWidth, bgRtb.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bgBmpData = bgBmp.LockBits(new System.Drawing.Rectangle(0, 0, bgRtb.PixelWidth, bgRtb.PixelHeight), ImageLockMode.WriteOnly, bgBmp.PixelFormat);
                Marshal.Copy(bgPixels, 0, bgBmpData.Scan0, bgPixels.Length);
                bgBmp.UnlockBits(bgBmpData);
            }

            var padding = 56;
            var totalW = pixelWidth + padding * 2;
            var totalH = pixelHeight + padding * 2;

            var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
                         (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

            var outerBg1 = isDark
                ? System.Drawing.Color.FromArgb(255, 32, 32, 32)
                : System.Drawing.Color.FromArgb(255, 243, 243, 243);
            var outerBg2 = isDark
                ? System.Drawing.Color.FromArgb(255, 24, 24, 40)
                : System.Drawing.Color.FromArgb(255, 235, 238, 248);
            var watermarkBarBg = isDark
                ? System.Drawing.Color.FromArgb(40, 0, 0, 0)
                : System.Drawing.Color.FromArgb(30, 0, 0, 0);
            var watermarkTextColor = isDark
                ? System.Drawing.Color.FromArgb(140, 255, 255, 255)
                : System.Drawing.Color.FromArgb(120, 0, 0, 0);

            using var finalBmp = new Bitmap(totalW, totalH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(finalBmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            using (var bgBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.Point(0, 0),
                new System.Drawing.Point(totalW, totalH),
                outerBg1, outerBg2))
            {
                g.FillRectangle(bgBrush, 0, 0, totalW, totalH);
            }

            if (bgBmp is not null)
            {
                var bgColorMatrix = new System.Drawing.Imaging.ColorMatrix(new float[][]
                {
                    new float[] {1, 0, 0, 0, 0},
                    new float[] {0, 1, 0, 0, 0},
                    new float[] {0, 0, 1, 0, 0},
                    new float[] {0, 0, 0, bgOpacity, 0},
                    new float[] {0, 0, 0, 0, 1}
                });
                using var bgImgAttr = new System.Drawing.Imaging.ImageAttributes();
                bgImgAttr.SetColorMatrix(bgColorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(bgBmp,
                    new System.Drawing.Rectangle(padding, padding, pixelWidth, pixelHeight),
                    0, 0, bgBmp.Width, bgBmp.Height,
                    GraphicsUnit.Pixel, bgImgAttr);
            }

            g.DrawImage(contentBmp, padding, padding, pixelWidth, pixelHeight);

            var showWatermark = AppSettings.GetBool("ScreenshotWatermark", true);
            if (showWatermark)
            {
                var watermarkText = AppSettings.Get("ScreenshotWatermarkText") ?? LocalizationService.L("App_Title", "图吧工具箱CE");
                var watermarkFont = AppSettings.Get("ScreenshotWatermarkFont") ?? "微软雅黑";
                DrawWatermark(g, totalW, totalH, watermarkText, watermarkFont, watermarkBarBg, watermarkTextColor);
            }

            using var ms = new MemoryStream();
            finalBmp.Save(ms, ImageFormat.Png);
            var bytes = ms.ToArray();

            var result = ClipboardService.TrySetBitmap(_ => BitmapFactory.Create(bytes));
            if (!result.Success)
            {
                ShowStatusBar(LocalizationService.L("Hw_ScreenshotFailed", "截图失败"),
                    LocalizationService.L("Hw_CopyBusyRetry", "复制失败：剪贴板被其他程序占用，请稍后重试"),
                    InfoBarSeverity.Warning);
                return;
            }

            ShowStatusBar(LocalizationService.L("Hw_ScreenshotCopied", "截图已复制到剪贴板"), LocalizationService.L("Hw_ScreenshotCopiedMsg", "可直接粘贴使用"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatusBar(LocalizationService.L("Hw_ScreenshotFailed", "截图失败"), ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isScreenshotting = false;
        }
    }

    /// <summary>把 PNG 字节包成剪贴板位图引用；每次尝试都要新建（旧流可能已被剪贴板消费）。</summary>
    private static class BitmapFactory
    {
        public static Windows.Storage.Streams.RandomAccessStreamReference Create(byte[] pngBytes)
        {
            var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            // 用 DataWriter 同步写入：不加锁、不等 IAsyncAction（避免在 UI 线程上阻塞等待）
            using (var writer = new Windows.Storage.Streams.DataWriter(stream))
            {
                writer.WriteBytes(pngBytes);
            }
            stream.Seek(0);
            return Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream);
        }
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectPath(int x, int y, int w, int h, int r)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawWatermark(Graphics g, int totalW, int totalH, string text, string fontFamilyName,
        System.Drawing.Color barBg, System.Drawing.Color textColor)
    {
        float fontSize = Math.Max(13f, totalH / 40f);
        using var font = new System.Drawing.Font(new System.Drawing.FontFamily(fontFamilyName), fontSize, System.Drawing.FontStyle.Regular);
        var size = g.MeasureString(text, font);
        float padH = 10;
        float padV = 5;
        float barW = size.Width + padH * 2;
        float barH = size.Height + padV * 2;
        float barX = totalW - barW - 20;
        float barY = totalH - barH - 16;

        using (var barPath = CreateRoundedRectPath((int)barX, (int)barY, (int)barW, (int)barH, 6))
        {
            using var barBrush = new SolidBrush(barBg);
            g.FillPath(barBrush, barPath);
        }

        using var textBrush = new SolidBrush(textColor);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.DrawString(text, font, textBrush, barX + padH, barY + padV);
    }

    private static async Task<int[]> GetPixelsAsync(RenderTargetBitmap rtb)
    {
        var buffer = await rtb.GetPixelsAsync();
        var bytes = new byte[buffer.Length];
        System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.CopyTo(buffer, bytes);
        var pixels = new int[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, pixels, 0, bytes.Length);
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            var a = (c >> 24) & 0xFF;
            var r = (c >> 16) & 0xFF;
            var g = (c >> 8) & 0xFF;
            var b = c & 0xFF;
            pixels[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }
        return pixels;
    }


    private static T? FindChild<T>(DependencyObject parent) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    private static T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found && found.Name == name) return found;
            var result = FindChildByName<T>(child, name);
            if (result is not null) return result;
        }
        return null;
    }

    private bool? _currentLayoutFitScreen;

    private void UpdateLayoutStructure()
    {
        var fitScreen = AppSettings.GetBool("HardwareFitScreen", true);
        if (_currentLayoutFitScreen == fitScreen) return;
        _currentLayoutFitScreen = fitScreen;

        if (LayoutRoot.Parent is Viewbox parentViewbox)
            parentViewbox.Child = null;
        else if (LayoutRoot.Parent is ScrollViewer parentScroll)
            parentScroll.Content = null;

        RootHost.Child = null;

        if (!fitScreen)
        {
            LayoutRoot.Width = double.NaN;
            LayoutRoot.MaxWidth = 1100;
            LayoutRoot.HorizontalAlignment = HorizontalAlignment.Center;

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            RootHost.Child = scroll;
            scroll.Content = LayoutRoot;
        }
        else
        {
            LayoutRoot.Width = 1100;
            LayoutRoot.MaxWidth = 1100;
            LayoutRoot.HorizontalAlignment = HorizontalAlignment.Stretch;

            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly
            };
            RootHost.Child = viewbox;
            viewbox.Child = LayoutRoot;
        }
    }

    private DispatcherTimer? _statusBarAutoCloseTimer;

    private void ShowStatusBar(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;

        _statusBarAutoCloseTimer?.Stop();
        _statusBarAutoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusBarAutoCloseTimer.Tick += (s, e) =>
        {
            StatusBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _statusBarAutoCloseTimer.Start();
    }
}
