using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services;

public static class WindowSizeService
{
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

        if (IsRememberEnabled())
        {
            int width = AppSettings.GetInt(WidthKey, 0);
            int height = AppSettings.GetInt(HeightKey, 0);
            bool maximized = AppSettings.GetBool(MaximizedKey);

            if (width > 0 && height > 0)
            {
                width = Math.Max(800, width);
                height = Math.Max(600, height);
                appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

                if (maximized)
                {
                    var presenter = appWindow.Presenter as OverlappedPresenter;
                    presenter?.Maximize();
                }

                return;
            }
        }

        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            var workArea = displayArea.WorkArea;
            int width = (int)(workArea.Width * 0.8);
            int height = (int)(workArea.Height * 0.8);
            width = Math.Max(800, width);
            height = Math.Max(600, height);

            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

            int x = workArea.X + (workArea.Width - width) / 2;
            int y = workArea.Y + (workArea.Height - height) / 2;
            appWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
    }
}
