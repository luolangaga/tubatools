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
        var summary = sections[0].Items;
        var system = sections[1].Items;
        var details = sections[2].Items;

        ModelText.Text = summary.FirstOrDefault(item => item.Label == "设备型号")?.Value ?? "未知";
        SystemText.Text = system.FirstOrDefault(item => item.Label == "系统")?.Value ?? "未知";
        UpdateUptime();
        _animatingDetails = !FastModeService.IsFastModeEnabled();
        DetailsRepeater.ItemsSource = details;

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

        var logoImage = FindChild<Microsoft.UI.Xaml.Controls.Image>(el);
        if (logoImage is not null && DetailsRepeater.ItemsSource is IReadOnlyList<HardwareInfoItem> items && args.Index < items.Count)
        {
            var item = items[args.Index];
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

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(LayoutRoot);

            if (statusWasOpen) StatusBar.IsOpen = true;

            var pixelWidth = rtb.PixelWidth;
            var pixelHeight = rtb.PixelHeight;
            var pixels = await GetPixelsAsync(rtb);

            using var contentBmp = new Bitmap(pixelWidth, pixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bmpData = contentBmp.LockBits(new System.Drawing.Rectangle(0, 0, pixelWidth, pixelHeight), ImageLockMode.WriteOnly, contentBmp.PixelFormat);
            Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            contentBmp.UnlockBits(bmpData);

            var padding = 32;
            var cornerRadius = 16;
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
            var cardBg = isDark
                ? System.Drawing.Color.FromArgb(255, 44, 44, 44)
                : System.Drawing.Color.FromArgb(255, 249, 249, 249);
            var borderColor = isDark
                ? System.Drawing.Color.FromArgb(60, 255, 255, 255)
                : System.Drawing.Color.FromArgb(60, 0, 0, 0);
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

            using (var cardPath = CreateRoundedRectPath(padding, padding, pixelWidth, pixelHeight, cornerRadius))
            {
                using var cardBrush = new SolidBrush(cardBg);
                g.FillPath(cardBrush, cardPath);

                g.SetClip(cardPath);
                g.DrawImage(contentBmp, padding, padding, pixelWidth, pixelHeight);
                g.ResetClip();

                using var borderPen = new Pen(borderColor, 1);
                g.DrawPath(borderPen, cardPath);
            }

            var showWatermark = AppSettings.GetBool("ScreenshotWatermark", true);
            if (showWatermark)
            {
                var watermarkText = AppSettings.Get("ScreenshotWatermarkText") ?? "图吧工具箱";
                var watermarkFont = AppSettings.Get("ScreenshotWatermarkFont") ?? "微软雅黑";
                DrawWatermark(g, totalW, totalH, watermarkText, watermarkFont, watermarkBarBg, watermarkTextColor);
            }

            var downloadsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = $"硬件信息_{timestamp}.png";
            var filePath = Path.Combine(downloadsFolder, fileName);

            finalBmp.Save(filePath, ImageFormat.Png);

            StatusBar.Title = "截图已保存";
            StatusBar.Message = filePath;
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
            pixels[i] = (a << 24) | (b << 16) | (g << 8) | r;
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
}
