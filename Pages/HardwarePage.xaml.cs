using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

public sealed partial class HardwarePage : Page
{
    private DispatcherTimer? _uptimeTimer;
    private bool _dataLoaded;
    private bool _animatingDetails;

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
        AppSettings.SettingChanged += OnSettingChanged;
    }

    private void OnSettingChanged(string key)
    {
        if (key == "UseCpuzDataSource" || key == "CompactModeEnabled")
        {
            // 简洁模式切换时重置布局容器缓存，确保 UpdateLayoutStructure 能强制重建
            if (key == "CompactModeEnabled")
                _currentLayoutIsCompact = null;
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
        ApplyBackground();
        _ = LoadHardwareInfoAsync();

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => UpdateUptime();
        _uptimeTimer.Start();
    }

    private void ApplyBackground()
    {
        var bmp = BackgroundService.LoadBackgroundImage();
        if (bmp is not null)
        {
            BackgroundImg.Source = bmp;
            BackgroundImg.Opacity = BackgroundService.GetBackgroundOpacity();
            BackgroundImg.Visibility = Visibility.Visible;
        }
        else
        {
            BackgroundImg.Source = null;
            BackgroundImg.Visibility = Visibility.Collapsed;
        }
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
        UptimeText.Text = $"{uptime.Days}天{uptime.Hours}小时{uptime.Minutes}分钟{uptime.Seconds}秒";
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadHardwareInfoAsync(forceRefresh: true);
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
            ModelText.Text = "未知";
            SystemText.Text = "未知";
            UptimeText.Text = "未知";
            DetailsRepeater.ItemsSource = Array.Empty<HardwareInfoItem>();
            StatusBar.Title = "硬件信息读取失败";
            StatusBar.Message = ex.Message;
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.IsOpen = true;
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ApplySections(IReadOnlyList<HardwareInfoSection> sections)
    {
        UpdateLayoutStructure();


        var summary = sections[0].Items;
        var system = sections[1].Items;
        var details = sections[2].Items;

        ModelText.Text = summary.FirstOrDefault(item => item.Label == "设备型号")?.Value ?? "未知";
        SystemText.Text = system.FirstOrDefault(item => item.Label == "系统")?.Value ?? "未知";
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

    private void Card1_Tapped(object sender, TappedRoutedEventArgs e) => CopyToClipboard(ModelText.Text);
    private void Card2_Tapped(object sender, TappedRoutedEventArgs e) => CopyToClipboard(SystemText.Text);
    private void Card3_Tapped(object sender, TappedRoutedEventArgs e) => CopyToClipboard(UptimeText.Text);

    private void DetailItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
    }

    private void DetailItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not HardwareInfoItem item) return;
        CopyToClipboard(item.Value);
    }

    private void CopyToClipboard(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        ShowCopyToast(text);
    }

    private void ShowCopyToast(string text)
    {
        StatusBar.Title = "已复制";
        StatusBar.Message = text.Length > 80 ? text[..80] + "…" : text;
        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.IsOpen = true;
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
            if (BackgroundImg.Visibility == Visibility.Visible && BackgroundImg.Source is not null)
            {
                var bgRtb = new RenderTargetBitmap();
                await bgRtb.RenderAsync(BackgroundImg);
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
                float bgOpacity = (float)BackgroundImg.Opacity;
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
                var watermarkText = AppSettings.Get("ScreenshotWatermarkText") ?? "图吧工具箱";
                var watermarkFont = AppSettings.Get("ScreenshotWatermarkFont") ?? "微软雅黑";
                DrawWatermark(g, totalW, totalH, watermarkText, watermarkFont, watermarkBarBg, watermarkTextColor);
            }

            using var ms = new MemoryStream();
            finalBmp.Save(ms, ImageFormat.Png);
            ms.Seek(0, SeekOrigin.Begin);

            var inMemStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var bytes = ms.ToArray();
            var winBuffer = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes);
            await inMemStream.WriteAsync(winBuffer);
            inMemStream.Seek(0);

            var dataPackage = new DataPackage();
            dataPackage.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(inMemStream));
            dataPackage.RequestedOperation = DataPackageOperation.Copy;
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();

            StatusBar.Title = "截图已复制到剪贴板";
            StatusBar.Message = "可直接粘贴使用";
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            StatusBar.Title = "截图失败";
            StatusBar.Message = ex.Message;
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.IsOpen = true;
        }
        finally
        {
            _isScreenshotting = false;
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

    private bool? _currentLayoutIsCompact;

    private void UpdateLayoutStructure()
    {
        var isCompact = AppSettings.GetBool("CompactModeEnabled", false);
        if (_currentLayoutIsCompact == isCompact) return;
        _currentLayoutIsCompact = isCompact;

        if (LayoutRoot.Parent is Panel parentPanel)
        {
            parentPanel.Children.Remove(LayoutRoot);
        }
        else if (LayoutRoot.Parent is Border parentBorder)
        {
            parentBorder.Child = null;
        }
        else if (LayoutRoot.Parent is ScrollViewer parentScroll)
        {
            parentScroll.Content = null;
        }
        else if (LayoutRoot.Parent is Viewbox parentViewbox)
        {
            parentViewbox.Child = null;
        }

        if (isCompact)
        {
            LayoutRoot.Width = double.NaN;
            LayoutRoot.MaxWidth = 1100;
            LayoutRoot.HorizontalAlignment = HorizontalAlignment.Center;

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = LayoutRoot
            };
            RootHost.Child = scroll;
        }
        else
        {
            LayoutRoot.Width = 1100;
            LayoutRoot.MaxWidth = double.PositiveInfinity;
            LayoutRoot.HorizontalAlignment = HorizontalAlignment.Stretch;

            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Child = LayoutRoot
            };
            RootHost.Child = viewbox;
        }
    }
}
