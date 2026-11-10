using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class NetworkConnectionsPage : Page
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly ObservableCollection<ConnectionEntry> _connections = [];
    private bool _loading;
    private bool _active;
    private int _generation;

    public NetworkConnectionsPage()
    {
        InitializeComponent();
        ConnectionPanel.ItemsSource = _connections;
        Loaded += (_, _) => StartRefreshing();
        Unloaded += (_, _) => StopRefreshing();
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        AutoRefreshToggle.Toggled += (_, _) =>
        {
            if (_active && AutoRefreshToggle.IsOn) _refreshTimer.Start();
            else _refreshTimer.Stop();
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (IsLoaded) StartRefreshing();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        StopRefreshing();
        base.OnNavigatedFrom(e);
    }

    private void StartRefreshing()
    {
        if (_active) return;
        _active = true;
        _generation++;
        if (AutoRefreshToggle.IsOn) _refreshTimer.Start();
        _ = RefreshAsync();
    }

    private void StopRefreshing()
    {
        _active = false;
        _generation++;
        _refreshTimer.Stop();
        LoadingBar.Visibility = Visibility.Collapsed;
    }

    private async Task RefreshAsync()
    {
        if (!_active || _loading) return;
        _loading = true;
        int generation = _generation;
        LoadingBar.Visibility = Visibility.Visible;
        try
        {
            // The native query cannot be cancelled mid-call. Discard late results instead.
            var connections = await Task.Run(NetworkAdapterProxyService.GetActiveConnections);
            if (!_active || generation != _generation) return;
            ConnectionListUpdater.Apply(_connections, connections);
            EmptyPanel.Visibility = _connections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageHeader.Subtitle = $"TCP 连接实时监控 · {_connections.Count} 条连接";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"连接刷新失败：{ex}");
        }
        finally
        {
            _loading = false;
            if (_active && generation == _generation) LoadingBar.Visibility = Visibility.Collapsed;
            // A cached page may have re-entered while its previous query was finishing.
            if (_active && generation != _generation) _ = RefreshAsync();
        }
    }

    public static Brush AdapterBackground(string type)
    {
        var color = type == "Wi-Fi" ? ThemeColors.AccentBlue : ThemeColors.AccentGreen;
        return new SolidColorBrush(Color.FromArgb(25, color.R, color.G, color.B));
    }

    public static Brush AdapterForeground(string type) =>
        new SolidColorBrush(type == "Wi-Fi" ? ThemeColors.AccentBlue : ThemeColors.AccentGreen);

    private void AdapterTag_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (sender is Border { DataContext: ConnectionEntry entry, Child: TextBlock text } tag)
        {
            tag.Background = AdapterBackground(entry.AdapterType);
            text.Foreground = AdapterForeground(entry.AdapterType);
        }
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnBackRequested(object? sender, EventArgs e)
    {
        StopRefreshing();
        App.MainWindow?.NavigateBack();
    }
}
