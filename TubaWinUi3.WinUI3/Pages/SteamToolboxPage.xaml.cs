using System.Diagnostics;
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
    private bool _loadingProfile;
    private string? _profileAccount;
    public SteamToolboxPage()
    {
        InitializeComponent();
        _idleTimer.Tick += (_, _) => RenderCurrentTask();
        SteamIdleService.Changed += OnIdleChanged;
        Unloaded += (_, _) => { SteamIdleService.Changed -= OnIdleChanged; _idleTimer.Stop(); };
        Loaded += async (_, _) =>
        {
            SteamIdleService.Changed -= OnIdleChanged;
            SteamIdleService.Changed += OnIdleChanged;
            _idleTimer.Start();
            OnIdleChanged();
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
        if (CollectionPanel == null || IdleScroll == null) return;
        CollectionPanel.Visibility = sender.SelectedItem == CollectionTab ? Visibility.Visible : Visibility.Collapsed;
        IdleScroll.Visibility = sender.SelectedItem == IdleTab ? Visibility.Visible : Visibility.Collapsed;
        ResizeBodies();
    }
    private void Body_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeBodies();
    private void ResizeBodies()
    {
        if (IdleContent == null || LoginColumns == null) return;
        double width = Math.Max(0, IdleScroll.ActualWidth);
        IdleContent.Width = Math.Min(1040, width);
        double inner = Math.Max(0, IdleContent.Width - 96);
        bool compact = inner < 680;
        LoginColumns.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 248);
        Grid.SetColumn(QrLoginPanel, compact ? 0 : 1); Grid.SetRow(QrLoginPanel, compact ? 1 : 0);
        CredentialFields.Width = Math.Min(420, Math.Max(0, compact ? inner : inner - 280));
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
        var connectionStatus = SteamIdleService.ConnectionStatus;
        IdleStatus.Text = SteamIdleService.Status + (connectionStatus.Length == 0 ? "" : "\n" + connectionStatus);
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
}
