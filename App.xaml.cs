using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3;

public partial class App : Application
{
    private Window? _window;
    public static Window? MainWindow => ((App)Current)?._window;

    public App()
    {
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        InitializeComponent();
        BuiltinToolRegistry.RegisterDefaults();

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        UnhandledException += OnWinUIUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
        ThemeService.ApplySavedTheme();
        ToolIconService.CleanExpiredCache();

        _ = CheckForUpdateSilentAsync();
    }

    private static async Task CheckForUpdateSilentAsync()
    {
        try
        {
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is null) return;

            var skipped = UpdateService.GetSkippedVersion();
            if (skipped == update.Version) return;

            if (MainWindow?.DispatcherQueue is null) return;

            MainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new UpdateDialog();
                await dialog.ShowUpdateAsync(update);

                if (dialog.SkipThisVersion)
                    UpdateService.SetSkippedVersion(update.Version);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] Silent check failed: {ex.Message}");
        }
    }

    private static Exception? _pendingException;

    private void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        _pendingException = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "未知错误");
        NavigateToErrorPage();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _pendingException = e.Exception;
        NavigateToErrorPage();
        e.SetObserved();
    }

    private void OnWinUIUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _pendingException = e.Exception ?? new Exception(e.Message);
        NavigateToErrorPage();
        e.Handled = true;
    }

    public static Exception? ConsumePendingException()
    {
        var ex = _pendingException;
        _pendingException = null;
        return ex;
    }

    private void NavigateToErrorPage()
    {
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (_window?.Content is not FrameworkElement root) return;
            var frame = FindFrame(root);
            if (frame is not null)
            {
                frame.Navigate(typeof(ErrorPage));
            }
        });
    }

    private static Frame? FindFrame(DependencyObject root)
    {
        if (root is Frame f) return f;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (FindFrame(child) is Frame found) return found;
        }
        return null;
    }
}
