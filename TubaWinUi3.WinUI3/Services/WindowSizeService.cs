using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services;

public static class WindowSizeService
{
    /// <summary>最小窗口尺寸（物理像素）。</summary>
    public const int MinWidth = 800;

    /// <summary>最小窗口尺寸（物理像素）。</summary>
    public const int MinHeight = 600;

    private const string WidthKey = "WindowWidth";
    private const string HeightKey = "WindowHeight";
    private const string MaximizedKey = "WindowMaximized";
    private const string RememberKey = "RememberWindowSize";

    public static bool IsRememberEnabled()
    {
        return AppSettings.GetBool(RememberKey);
    }

    public static void SetRememberEnabled(bool enabled)
    {
        AppSettings.Set(RememberKey, enabled);
        if (!enabled)
        {
            AppSettings.Remove(WidthKey);
            AppSettings.Remove(HeightKey);
            AppSettings.Remove(MaximizedKey);
        }
    }

    /// <summary>
    /// 把最小尺寸交给系统呈现器钳制。
    ///
    /// 以前是在 <c>AppWindow.Changed</c> 里发现尺寸小于 800×600 再 <c>Resize</c> 回去，等于在
    /// 用户拖边缩放/贴边分屏的过程中不断由应用改写窗口尺寸：拖动被打断、窗口跟手性差。
    /// <c>OverlappedPresenter.PreferredMinimumWidth/Height</c> 由系统在缩放与贴边时直接限制，
    /// 不再产生「先变小再被拉回」的反馈回环（它同样作用于 AppWindow.Resize 调用）。
    /// </summary>
    public static void ApplyMinimumWindowSize(Window window)
    {
        if (window?.AppWindow.Presenter is not OverlappedPresenter presenter) return;

        try
        {
            presenter.PreferredMinimumWidth = MinWidth;
            presenter.PreferredMinimumHeight = MinHeight;
        }
        catch (Exception ex)
        {
            // 少数旧系统不接受该属性：保持 0，由 AppWindow_Changed 的 Resize 兜底。
            System.Diagnostics.Debug.WriteLine($"[WindowSize] 设置最小窗口尺寸失败（改用兜底）: {ex.Message}");
        }
    }

    public static void SaveWindowSize(MainWindow window)
    {
        if (!IsRememberEnabled()) return;
        if (window is null) return;

        var appWindow = window.AppWindow;
        if (appWindow is null) return;

        var presenter = appWindow.Presenter as OverlappedPresenter;
        var isMaximized = presenter?.State == OverlappedPresenterState.Maximized;

        if (isMaximized)
        {
            AppSettings.Set(MaximizedKey, true);
        }
        else
        {
            AppSettings.Set(MaximizedKey, false);
            AppSettings.Set(WidthKey, appWindow.Size.Width);
            AppSettings.Set(HeightKey, appWindow.Size.Height);
        }
    }

    public static void ApplySavedWindowSize(MainWindow window)
    {
        if (window is null) return;

        var appWindow = window.AppWindow;

        // 本方法运行在 MainWindow 构造函数里（即 App.OnLaunched 内），
        // 任何一个未捕获异常都会让进程以 STOWED_EXCEPTION 闪退 —— 尺寸还原失败
        // 只应退化成默认尺寸，绝不能拖垮启动。
        if (IsRememberEnabled() && TryRestoreSavedSize(appWindow))
            return;

        ApplyDefaultSize(appWindow);
    }

    private static bool TryRestoreSavedSize(AppWindow appWindow)
    {
        var width = AppSettings.GetInt(WidthKey, 0);
        var height = AppSettings.GetInt(HeightKey, 0);
        if (width <= 0 || height <= 0) return false;

        try
        {
            appWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(MinWidth, width), Math.Max(MinHeight, height)));

            if (AppSettings.GetBool(MaximizedKey))
                (appWindow.Presenter as OverlappedPresenter)?.Maximize();

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowSize] 还原上次窗口尺寸失败（改用默认尺寸）: {ex.Message}");
            return false;
        }
    }

    private static void ApplyDefaultSize(AppWindow appWindow)
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea is null) return;

            var workArea = displayArea.WorkArea;
            int width = Math.Max(MinWidth, (int)(workArea.Width * 0.8));
            int height = Math.Max(MinHeight, (int)(workArea.Height * 0.8));

            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
            appWindow.Move(new Windows.Graphics.PointInt32(
                workArea.X + (workArea.Width - width) / 2,
                workArea.Y + (workArea.Height - height) / 2));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowSize] 设置默认窗口尺寸失败（已忽略）: {ex.Message}");
        }
    }
}
