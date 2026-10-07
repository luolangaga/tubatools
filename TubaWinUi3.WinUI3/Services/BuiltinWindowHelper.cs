using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

/// <summary>
/// 统一内置工具窗口样式：与"电脑使用教程"一致 —— Mica 背景、82%×85% 工作区尺寸并居中、
/// 扩展标题栏（Tall）+ 自定义主题配色。
/// </summary>
public static class BuiltinWindowHelper
{
    public static void ApplyStandardStyle(Window window, string title)
    {
        BackdropService.ApplyBackdrop(window);
        window.AppWindow.Title = title;

        try
        {
            var displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea is not null)
            {
                var workArea = displayArea.WorkArea;
                var w = (int)(workArea.Width * 0.82);
                var h = (int)(workArea.Height * 0.85);
                window.AppWindow.Resize(new SizeInt32(w, h));
                window.AppWindow.Move(new PointInt32(
                    workArea.X + (int)((workArea.Width - w) / 2),
                    workArea.Y + (int)((workArea.Height - h) / 2)));
            }
        }
        catch
        {
            window.AppWindow.Resize(new SizeInt32(1100, 750));
            try
            {
                var mainPos = App.MainWindow?.AppWindow.Position;
                if (mainPos is not null)
                    window.AppWindow.Move(new PointInt32(mainPos.Value.X + 50, mainPos.Value.Y + 50));
            }
            catch { }
        }

        SafeTitleBar.ApplyExtendedTall(window);
        ApplyTitleBarTheme(window);
    }

    public static void ApplyTitleBarTheme(Window window)
    {
        var isDark = ThemeService.IsDarkEffective;
        TitleBarPalette.Apply(SafeTitleBar.Get(window), isDark);
    }
}

/// <summary>
/// 标题栏定制 API 的安全入口。
///
/// 背景（对应应用商店崩溃报告里的
/// STOWED_EXCEPTION_80004003_Microsoft.UI.Xaml.dll!DirectUI::FrameworkApplicationGenerated::OnLaunchedProtected）：
/// 窗口的构造函数运行在 App.OnLaunched 内，从构造函数里逃出去的异常会被 WinUI 转成
/// STOWED_EXCEPTION 直接结束进程（托管空引用异常跨 ABI 映射为 0x80004003 = E_POINTER）。
/// 而 AppWindow.TitleBar 在部分 Windows 10 版本上会返回 null（WinUI 已知问题
/// microsoft-ui-xaml#6101），标题栏颜色/高度在 Windows 10 上也只是部分支持
/// （官方文档要求先查 AppWindowTitleBar.IsCustomizationSupported，否则可能崩溃）。
/// 因此这里统一做「空值判断 + 异常兜底」，任何一步失败都只是退化成系统标题栏配色，
/// 绝不让启动路径崩掉。
/// </summary>
internal static class SafeTitleBar
{
    /// <summary>取窗口标题栏对象；窗口未创建、标题栏不可用（如部分 Windows 10 版本）时返回 null。</summary>
    public static AppWindowTitleBar? Get(Window? window)
    {
        try
        {
            return window?.AppWindow?.TitleBar;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeTitleBar] 读取标题栏失败（已忽略）: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 把内容扩展进标题栏并使用 Tall 高度。标题栏对象不可用时静默跳过，
    /// 窗口仍可正常显示（只是标题栏按钮回到系统默认外观）。
    /// </summary>
    public static void ApplyExtendedTall(Window window, UIElement? dragRegion = null)
    {
        try
        {
            window.ExtendsContentIntoTitleBar = true;
            if (dragRegion is not null)
                window.SetTitleBar(dragRegion);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeTitleBar] 扩展标题栏失败（已忽略）: {ex.Message}");
        }

        var titleBar = Get(window);
        if (titleBar is null) return;

        try
        {
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        }
        catch (Exception ex)
        {
            // Windows 10 上标题栏定制只是部分支持：颜色会被忽略、高度可能不被接受，
            // 这里失败不影响窗口本身可用。
            Debug.WriteLine($"[SafeTitleBar] 标题栏高度设置失败（已忽略）: {ex.Message}");
        }
    }
}

/// <summary>
/// 全应用统一的标题栏配色（主窗口与所有子窗口共用同一套明暗色值）。
/// </summary>
internal static class TitleBarPalette
{
    public static void Apply(AppWindowTitleBar? tb, bool isDark)
    {
        // 部分 Windows 10 版本 AppWindow.TitleBar 为 null（microsoft-ui-xaml#6101），
        // 颜色 API 在 Windows 10 上也会被忽略 —— 直接用系统默认配色即可，不能抛异常。
        if (tb is null) return;

        try
        {
            ApplyCore(tb, isDark);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TitleBarPalette] 标题栏配色失败（已忽略）: {ex.Message}");
        }
    }

    private static void ApplyCore(AppWindowTitleBar tb, bool isDark)
    {
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
}
