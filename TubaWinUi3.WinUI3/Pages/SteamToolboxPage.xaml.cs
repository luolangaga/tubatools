using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class SteamToolboxPage : Page
{
    private SteamSnapshot? _snapshot;
    private bool _busy;
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _renderedTaskArt;
    private readonly ObservableCollection<SteamConnectionRow> _connectionRows = [];
    private DateTimeOffset? _networkRun, _lastChartPoint;
    private bool _chartReady;
    private bool _loadingProfile;
    private string? _profileAccount;
    private bool _accelerationBusy;
    private IReadOnlyList<SteamAccelerationGroup> _siteGroups = [];
    public SteamToolboxPage()
    {
        InitializeComponent();
        _siteGroups = SteamAccelerationCatalog.Load();
        SiteGroups.ItemsSource = _siteGroups;
        foreach (var rule in _siteGroups.SelectMany(g => g.Rules)) rule.PropertyChanged += (_, _) => UpdateSelectionLabel();
        UpdateSelectionLabel();
        CatalogLabel.Text = $"Watt Toolkit 项目列表 · {_siteGroups.Count} 个分类 / {_siteGroups.Sum(g => g.Rules.Count)} 项服务 · 2026/11/01 快照";
        NetworkConnections.ItemsSource = _connectionRows;
        ActualThemeChanged += (_, _) => { _chartReady = false; _lastChartPoint = null; };
        _idleTimer.Tick += (_, _) => { RenderCurrentTask(); RenderNetworkDashboard(); };
        SteamIdleService.Changed += OnIdleChanged;
        Unloaded += (_, _) => { SteamIdleService.Changed -= OnIdleChanged; _idleTimer.Stop(); };
        Loaded += async (_, _) =>
        {
            SteamIdleService.Changed -= OnIdleChanged;
            SteamIdleService.Changed += OnIdleChanged;
            _idleTimer.Start();
            OnIdleChanged();
            AccelerationStatus.Text = SteamAccelerationService.Status;
            RenderNetworkDashboard();
            await RefreshAsync();
            DispatcherQueue.TryEnqueue(() => RefreshButton.Focus(FocusState.Programmatic));
        };
    }
    private void PageViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PageSurface != null) PageSurface.Width = Math.Max(0, Math.Min(1040, e.NewSize.Width));
        ResizeBodies();
    }
    private void WorkspaceTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (CollectionPanel == null || IdleScroll == null || SitesScroll == null) return;
        CollectionPanel.Visibility = sender.SelectedItem == CollectionTab ? Visibility.Visible : Visibility.Collapsed;
        IdleScroll.Visibility = sender.SelectedItem == IdleTab ? Visibility.Visible : Visibility.Collapsed;
        SitesScroll.Visibility = sender.SelectedItem == AccelerationTab ? Visibility.Visible : Visibility.Collapsed;
        ResizeBodies();
        RenderNetworkDashboard();
    }
    private void Body_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeBodies();
    private void ResizeBodies()
    {
        if (IdleContent == null || SitesContent == null || LoginColumns == null) return;
        double width = Math.Max(0, IdleScroll.ActualWidth);
        IdleContent.Width = Math.Min(1040, width);
        SitesContent.Width = Math.Min(1040, Math.Max(0, SitesScroll.ActualWidth));
        double inner = Math.Max(0, IdleContent.Width - 96);
        bool compact = inner < 680;
        LoginColumns.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 248);
        Grid.SetColumn(QrLoginPanel, compact ? 0 : 1); Grid.SetRow(QrLoginPanel, compact ? 1 : 0);
        CredentialFields.Width = Math.Min(420, Math.Max(0, compact ? inner : inner - 280));
    }
    private void UpdateSelectionLabel()
    {
        if (SelectionLabel != null) SelectionLabel.Text = $"已选择 {_siteGroups.SelectMany(g => g.Rules).Count(r => r.IsSelected && r.Available)} / {_siteGroups.SelectMany(g => g.Rules).Count(r => r.Available)} 项可用服务";
    }
    private void Games_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (HeaderPanel != null) HeaderPanel.Width = Math.Max(300, e.NewSize.Width - 60);
    }
    private void Back_Click(object? sender, EventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    private void ShowError(Exception e) { Notice.Message = e.Message; Notice.Severity = InfoBarSeverity.Warning; Notice.IsOpen = true; }
    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        Notice.IsOpen = false;
        try
        {
            var root = SteamToolboxService.FindRoot();
            InstallPath.Text = root ?? "未找到 Steam，请指定包含 steam.exe 的安装目录。";
            _snapshot = root == null ? null : await Task.Run(() => SteamToolboxService.Scan(root));
            var games = _snapshot?.Libraries.SelectMany(l => l.Games).ToList() ?? [];
            GameCount.Text = games.Count.ToString();
            TotalSize.Text = SteamToolboxService.Size(games.Sum(g => g.Bytes));
            AccountCount.Text = (_snapshot?.Accounts.Count ?? 0).ToString();
            AccountsView.ItemsSource = _snapshot?.Accounts;
            AccountEmpty.Visibility = _snapshot?.Accounts.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            ApplyFilter();
            if (_snapshot?.Warnings.Count > 0)
            {
                Notice.Message = string.Join("\n", _snapshot.Warnings.Take(6));
                Notice.Severity = InfoBarSeverity.Warning;
                Notice.IsOpen = true;
            }
        }
        catch (Exception e) { ShowError(e); }
        finally { _busy = false; RefreshButton.IsEnabled = true; LoadingBar.Visibility = Visibility.Collapsed; }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Locate_Click(object sender, RoutedEventArgs e)
    {
        var folder = await Win32Dialogs.PickFolderAsync();
        if (folder == null) return;
        if (!File.Exists(Path.Combine(folder, "steam.exe"))) { ShowError(new ArgumentException("此目录没有 steam.exe，请选择 Steam 安装目录。")); return; }
        AppSettings.Set("SteamToolboxRoot", folder);
        await RefreshAsync();
    }
    private void Filter_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Sort_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void ApplyFilter()
    {
        if (EmptyText == null || SearchBox == null || SortBox == null) return;
        string query = SearchBox.Text.Trim();
        var groups = _snapshot?.Libraries.Select(l => new SteamLibrary(l.Path,
            (SortBox.SelectedIndex == 1 ? l.Games.OrderByDescending(g => g.Bytes).ThenBy(g => g.Name) : l.Games.OrderBy(g => g.Name))
            .Where(g => g.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || g.AppId.ToString().Contains(query)).ToList()))
            .Where(l => l.Games.Count > 0).ToList() ?? [];
        LibrarySource.Source = groups;
        EmptyText.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _snapshot == null ? "安装并登录 Steam 后，你的游戏收藏会显示在这里。" : query.Length > 0 ? "没有匹配的游戏，试试其他名称或 AppID。" : "没有找到游戏清单，请确认游戏库磁盘已连接并在 Steam 中正确添加。";
    }
    private void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { ShowError(e); }
    }
    private void Launch_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is uint id) Open($"steam://rungameid/{id}"); }
    private void Store_Click(object sender, RoutedEventArgs e) { if (((MenuFlyoutItem)sender).Tag is uint id) Open($"https://store.steampowered.com/app/{id}/"); }
    private void Profile_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is string id && ulong.TryParse(id, out _)) Open($"https://steamcommunity.com/profiles/{id}"); }
    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (((MenuFlyoutItem)sender).Tag is string folder && Directory.Exists(folder)) Open(folder);
        else ShowError(new IOException("游戏目录不可访问，请确认游戏库磁盘已连接。"));
    }
    private void Idle_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is uint id) StartIdle(id);
    }
    private void Login_Click(object sender, RoutedEventArgs e) { Notice.IsOpen = false; SteamIdleService.Login(); }
    private void Logout_Click(object sender, RoutedEventArgs e) => SteamIdleService.Disconnect();
    private void StopIdle_Click(object sender, RoutedEventArgs e)
    {
        try { SteamIdleService.Play(0); } catch (Exception ex) { ShowError(ex); }
    }
    private void ManualIdle_Click(object sender, RoutedEventArgs e)
    {
        if (!double.IsNaN(IdleAppId.Value) && IdleAppId.Value >= 1 && IdleAppId.Value <= uint.MaxValue && IdleAppId.Value == Math.Truncate(IdleAppId.Value)) StartIdle((uint)IdleAppId.Value);
        else ShowError(new ArgumentException("请输入有效的游戏 AppID。"));
    }
    private void OwnedIdle_Click(object sender, RoutedEventArgs e)
    {
        if (OwnedGamePicker.SelectedItem is SteamOwnedGame game) StartIdle(game.AppId);
        else ShowError(new ArgumentException("请先在账号游戏库中选择游戏。"));
    }
    private void PauseIdle_Click(object sender, RoutedEventArgs e)
    {
        try { if (SteamIdleService.CurrentTask.State == SteamIdleTaskState.Paused) SteamIdleService.Resume(); else SteamIdleService.Pause(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void StartIdle(uint id)
    {
        try
        {
            var local = _snapshot?.Libraries.SelectMany(l => l.Games).FirstOrDefault(g => g.AppId == id);
            var owned = (OwnedGamePicker.ItemsSource as IEnumerable<SteamOwnedGame>)?.FirstOrDefault(g => g.AppId == id);
            SteamIdleService.Play(id, owned?.Name ?? local?.Name, local?.Art);
            WorkspaceTabs.SelectedItem = IdleTab;
            IdleScroll.ChangeView(null, 0, null);
            Notice.IsOpen = false;
        }
        catch (Exception ex) { WorkspaceTabs.SelectedItem = IdleTab; ShowError(ex); }
    }
    private void OnIdleChanged() => DispatcherQueue.TryEnqueue(async () =>
    {
        IdleStatus.Text = SteamIdleService.Status;
        RenderCurrentTask();
        if (SteamIdleService.LoggedIn && _profileAccount != SteamIdleService.AccountLabel && !_loadingProfile) await RefreshOnlineAsync();
        if (!SteamIdleService.LoggedIn)
        {
            _profileAccount = null; OwnedGamePicker.ItemsSource = null; FriendsView.ItemsSource = null;
            OnlineProfile.Text = "登录后自动加载账号游戏库与好友，也可点击刷新。";
        }
        GuardPanel.Visibility = SteamIdleService.GuardPrompt == null ? Visibility.Collapsed : Visibility.Visible;
        GuardPrompt.Text = SteamIdleService.GuardPrompt ?? "";
        LoginQr.Visibility = SteamIdleService.QrUrl == null ? Visibility.Collapsed : Visibility.Visible;
        if (SteamIdleService.QrUrl is { } url)
        {
            try
            {
                var bytes = QrCodeGenerator.GeneratePng(url);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(); await bitmap.SetSourceAsync(stream); LoginQr.Source = bitmap;
            }
            catch (Exception ex) { ShowError(ex); }
        }
    });
    private async void StartAcceleration_Click(object sender, RoutedEventArgs e)
    {
        if (_accelerationBusy) return;
        _accelerationBusy = true; StartAcceleration.IsEnabled = false;
        try { if (SteamAccelerationService.Running) await SteamAccelerationService.StopAsync(); await SteamAccelerationService.StartAsync(_siteGroups.SelectMany(g => g.Rules).Where(r => r.IsSelected && r.Available).ToList()); }
        catch (Exception ex) { ShowError(new InvalidOperationException("加速启动失败：" + ex.Message + "。请确认拥有管理员权限且 80/443 端口未被其他程序占用。")); }
        finally { AccelerationStatus.Text = SteamAccelerationService.Status; _accelerationBusy = false; StartAcceleration.IsEnabled = true; RenderNetworkDashboard(); }
    }
    private async void StopAcceleration_Click(object sender, RoutedEventArgs e)
    {
        if (_accelerationBusy) return;
        _accelerationBusy = true; StopAcceleration.IsEnabled = false; DashboardStopButton.IsEnabled = false;
        try { await SteamAccelerationService.StopAsync(); }
        catch (Exception ex) { ShowError(ex); }
        finally { AccelerationStatus.Text = SteamAccelerationService.Status; _accelerationBusy = false; StopAcceleration.IsEnabled = true; DashboardStopButton.IsEnabled = true; RenderNetworkDashboard(); }
    }
    private void PasswordLogin_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SteamUsername.Text) || string.IsNullOrEmpty(SteamPassword.Password)) { ShowError(new ArgumentException("请输入账号名称和密码。")); return; }
        Notice.IsOpen = false;
        SteamIdleService.Login(SteamUsername.Text.Trim(), SteamPassword.Password, PreferGuardCode.IsChecked == true);
        SteamPassword.Password = "";
    }
    private void GuardSubmit_Click(object sender, RoutedEventArgs e)
    {
        try { SteamIdleService.SubmitGuardCode(GuardCode.Text); GuardCode.Text = ""; }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void RefreshOnline_Click(object sender, RoutedEventArgs e) => await RefreshOnlineAsync();
    private async Task RefreshOnlineAsync()
    {
        if (_loadingProfile) return;
        _loadingProfile = true;
        var requestedAccount = SteamIdleService.AccountLabel;
        try
        {
            var data = await SteamIdleService.GetOnlineInfoAsync();
            OnlineProfile.Text = $"{data.Name} · SteamID {data.Id}\n{data.Games.Count} 个游戏 · {data.Friends.Count} 位好友";
            OwnedGamePicker.ItemsSource = data.Games; FriendsView.ItemsSource = data.Friends;
            _profileAccount = SteamIdleService.AccountLabel;
        }
        catch (Exception ex)
        {
            if (SteamIdleService.AccountLabel == requestedAccount)
            {
                _profileAccount = requestedAccount; // explicit refresh retries, timer/status changes do not spam the endpoint
                OnlineProfile.Text = "账号游戏库读取失败，可点击刷新重试：" + ex.Message;
            }
        }
        finally { _loadingProfile = false; }
    }
    private void RenderCurrentTask()
    {
        var task = SteamIdleService.CurrentTask;
        bool running = task.State == SteamIdleTaskState.Running;
        bool paused = task.State == SteamIdleTaskState.Paused;
        CurrentTaskTitle.Text = task.State switch
        {
            SteamIdleTaskState.Running => "当前正在挂时长",
            SteamIdleTaskState.Paused => "挂时长已暂停",
            SteamIdleTaskState.Disconnected => "挂时长已中断 · 连接断开",
            SteamIdleTaskState.Stopped => "上次任务 · 已停止",
            _ => "当前没有挂时长任务"
        };
        CurrentTaskName.Text = task.AppId == 0 ? "选择一个游戏，开始挂时长" : task.Name;
        CurrentTaskAppId.Text = task.AppId == 0 ? "" : $"AppID {task.AppId}";
        CurrentTaskAccount.Text = task.AppId == 0 ? SteamIdleService.AccountLabel : task.Account;
        CurrentTaskStarted.Text = task.StartedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") ?? "—";
        CurrentTaskElapsed.Text = $"{(long)task.Elapsed.TotalHours:00}:{task.Elapsed.Minutes:00}:{task.Elapsed.Seconds:00}";
        if (_renderedTaskArt != task.Art)
        {
            _renderedTaskArt = task.Art;
            CurrentTaskArt.Source = string.IsNullOrEmpty(task.Art) ? null : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(task.Art));
        }
        PauseIdleButton.Content = paused ? "恢复" : "暂停";
        PauseIdleButton.IsEnabled = running || (paused && SteamIdleService.LoggedIn && !SteamIdleService.PlayingBlocked);
        StopIdleButton.IsEnabled = running || paused;
        LogoutIdleButton.IsEnabled = SteamIdleService.LoggedIn || SteamIdleService.QrUrl != null || SteamIdleService.GuardPrompt != null;
        LoginForm.Visibility = SteamIdleService.LoggedIn ? Visibility.Collapsed : Visibility.Visible;
        CurrentTaskCard.Visibility = SteamIdleService.LoggedIn || task.AppId != 0 ? Visibility.Visible : Visibility.Collapsed;
        GameSelectionCard.Visibility = SteamIdleService.LoggedIn ? Visibility.Visible : Visibility.Collapsed;
        StartOwnedIdleButton.IsEnabled = StartManualIdleButton.IsEnabled = SteamIdleService.LoggedIn && !SteamIdleService.PlayingBlocked;
    }
    private void RenderNetworkDashboard()
    {
        bool running = SteamAccelerationService.Running;
        NetworkConfiguration.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        NetworkDashboard.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (!running || SitesScroll.Visibility != Visibility.Visible) return;
        var traffic = SteamAccelerationService.Traffic;
        if (_networkRun != traffic.StartedAt)
        {
            _networkRun = traffic.StartedAt; _lastChartPoint = null; _connectionRows.Clear();
        }
        NetworkUptime.Text = $"运行 {(long)(DateTimeOffset.Now - traffic.StartedAt).TotalMinutes} 分钟 · 每秒更新";
        NetworkDownloadRate.Text = SteamTrafficSession.FormatBytes(traffic.DownloadRate) + "/s";
        NetworkUploadRate.Text = SteamTrafficSession.FormatBytes(traffic.UploadRate) + "/s";
        NetworkDownloadTotal.Text = "累计下载 " + SteamTrafficSession.FormatBytes(traffic.Download);
        NetworkUploadTotal.Text = "累计上传 " + SteamTrafficSession.FormatBytes(traffic.Upload);
        NetworkRequestCounts.Text = $"{traffic.Requests} 次请求 · {traffic.Failures} 次失败";
        NetworkConnectionCount.Text = $"{traffic.ActiveConnections} 个活动连接";
        NetworkEmpty.Visibility = traffic.Connections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var ids = traffic.Connections.Select(c => c.Id).ToHashSet();
        for (int j = _connectionRows.Count - 1; j >= 0; j--) if (!ids.Contains(_connectionRows[j].Id)) _connectionRows.RemoveAt(j);
        foreach (var connection in traffic.Connections)
        {
            var existing = _connectionRows.FirstOrDefault(c => c.Id == connection.Id);
            if (existing != null) existing.Update(connection);
            else _connectionRows.Insert(0, new(connection));
        }
        if (!_chartReady) ConfigureNetworkChart();
        var point = traffic.History.LastOrDefault()?.Time;
        if (_lastChartPoint == point) return;
        _lastChartPoint = point;
        var series = ProxyTrafficChart.Series.Cast<LineSeries<DateTimePoint>>().ToArray();
        series[0].Values = traffic.History.Select(p => new DateTimePoint(p.Time.LocalDateTime, p.Download)).ToArray();
        series[1].Values = traffic.History.Select(p => new DateTimePoint(p.Time.LocalDateTime, p.Upload)).ToArray();
    }
    private void ConfigureNetworkChart()
    {
        static SKColor Sk(Windows.UI.Color c) => new(c.R, c.G, c.B);
        var text = Sk(ThemeColors.DimText);
        LineSeries<DateTimePoint> Series(string name, SKColor color) => new()
        {
            Name = name, GeometrySize = 0, LineSmoothness = 0,
            Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
            Fill = new SolidColorPaint(new SKColor(color.Red, color.Green, color.Blue, 22))
        };
        ProxyTrafficChart.Series = new[] { Series("下载", Sk(ThemeColors.Series3)), Series("上传", Sk(ThemeColors.Series1)) };
        ProxyTrafficChart.XAxes = new[] { new Axis
        {
            Labeler = v => v >= DateTime.MinValue.Ticks && v <= DateTime.MaxValue.Ticks ? new DateTime((long)v).ToString("HH:mm:ss") : "",
            UnitWidth = TimeSpan.FromSeconds(1).Ticks, MinStep = TimeSpan.FromSeconds(15).Ticks,
            TextSize = 11, LabelsPaint = new SolidColorPaint(text), SeparatorsPaint = null
        } };
        ProxyTrafficChart.YAxes = new[] { new Axis { MinLimit = 0, TextSize = 11,
            Labeler = v => SteamTrafficSession.FormatBytes(Math.Max(0, v)) + "/s",
            LabelsPaint = new SolidColorPaint(text), SeparatorsPaint = new SolidColorPaint(new SKColor(text.Red, text.Green, text.Blue, 30)) } };
        ProxyTrafficChart.AnimationsSpeed = TimeSpan.Zero;
        ProxyTrafficChart.EasingFunction = null;
        _chartReady = true;
    }
    private void SelectAllSites_Click(object sender, RoutedEventArgs e) { foreach (var rule in _siteGroups.SelectMany(g => g.Rules).Where(r => r.Available)) rule.IsSelected = true; }
    private void ClearSites_Click(object sender, RoutedEventArgs e) { foreach (var rule in _siteGroups.SelectMany(g => g.Rules)) rule.IsSelected = false; }
    private void SiteSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (SiteGroups == null) return;
        string query = SiteSearch.Text.Trim();
        SiteGroups.ItemsSource = _siteGroups.Select(g => new SteamAccelerationGroup(g.Name, g.Glyph, g.Rules.Where(r => g.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || r.DomainSummary.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList())).Where(g => g.Rules.Count > 0).ToList();
    }
    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        ProbeButton.IsEnabled = false; ProbeResult.Text = "正在检测三个网站…";
        try { ProbeResult.Text = await SteamCompanionService.ProbeAsync(); }
        catch (Exception ex) { ShowError(ex); }
        finally { ProbeButton.IsEnabled = true; }
    }
}

public sealed class SteamConnectionRow(SteamProxyConnection connection) : INotifyPropertyChanged
{
    private SteamProxyConnection _connection = connection;
    public long Id => _connection.Id;
    public string Host => _connection.Host;
    public string Detail => _connection.Detail;
    public string Upload => "↑ " + SteamTrafficSession.FormatBytes(_connection.Upload);
    public string Download => "↓ " + SteamTrafficSession.FormatBytes(_connection.Download);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(SteamProxyConnection value)
    {
        if (_connection == value) return;
        _connection = value; PropertyChanged?.Invoke(this, new(string.Empty));
    }
}
