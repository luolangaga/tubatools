using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace Tubatools.SteamToolbox;

// Implements Steam's games-played session using the same SteamKit protocol as ASF.
// References: SteamRE/SteamKit QR sample (LGPL-2.1), ASF ArchiHandler.PlayGames (Apache-2.0).
internal static class SteamIdleService
{
    private static readonly object Gate = new();
    private static SteamClient? _client;
    private static CancellationTokenSource? _stop;
    private static bool _blocked;
    private static readonly SteamIdleTaskTracker Activity = new();
    private static string _accountName = "";
    private static IReadOnlyList<SteamOwnedGame> _ownedGames = [];
    private static bool _hasOwnedGameList;
    private static DateTimeOffset? _lastOnlineCheck;
    public static string ConnectionStatus
    {
        get
        {
            lock (Gate) return !_loggedIn ? "" : _lastOnlineCheck is { } checkedAt
                ? $"在线检测：服务器已响应 · {checkedAt:HH:mm:ss}（每 15 秒检测）"
                : "在线检测：正在确认服务器连接…";
        }
    }
    public static SteamIdleTaskSnapshot CurrentTask { get { lock (Gate) return Activity.Snapshot(DateTimeOffset.Now); } }
    public static string AccountLabel { get { lock (Gate) return _loggedIn ? $"{_accountName} · SteamID {_client?.SteamID}" : "未登录"; } }
    public static bool PlayingBlocked { get { lock (Gate) return _blocked; } }
    private static TaskCompletionSource<string>? _guardCode;
    public static string? GuardPrompt { get; private set; }
    private static volatile bool _loggedIn;
    public static bool LoggedIn => _loggedIn;
    public static string Status { get; private set; } = "尚未登录挂卡会话";
    public static string? QrUrl { get; private set; }
    public static event Action? Changed;
    private static void Update(string status) { Status = status; Changed?.Invoke(); }
    public static void Login(string? username = null, string? password = null, bool preferCode = false)
    {
        lock (Gate)
        {
            DisconnectCore();
            var stop = _stop = new CancellationTokenSource();
            var client = _client = new SteamClient();
            var manager = new CallbackManager(client);
            var user = client.GetHandler<SteamUser>()!;
            manager.Subscribe<SteamClient.ConnectedCallback>(callback => { _ = AuthenticateAsync(); });
            manager.Subscribe<SteamClient.DisconnectedCallback>(_ =>
            {
                lock (Gate)
                {
                    if (_client != client) return;
                    EndDisconnected(client, stop, Status.StartsWith("登录失败", StringComparison.Ordinal) ? Status : "连接已断开，请重新登录");
                }
            });
            manager.Subscribe<SteamUser.LoggedOnCallback>(c =>
            {
                lock (Gate)
                {
                    if (_client != client || stop.IsCancellationRequested) return;
                    _loggedIn = c.Result == EResult.OK; QrUrl = null;
                    Update(_loggedIn ? $"已登录 · SteamID {client.SteamID} · 选择游戏开始挂时长" : $"Steam 登录失败：{c.Result}");
                    if (_loggedIn) _ = Task.Run(() => MonitorConnectionAsync(client, stop));
                }
            });
            manager.Subscribe<SteamUser.LoggedOffCallback>(c =>
            {
                lock (Gate)
                {
                    if (_client != client) return;
                    EndDisconnected(client, stop, $"会话已退出：{c.Result}");
                }
            });
            manager.Subscribe<SteamUser.PlayingSessionStateCallback>(c =>
            {
                if (_client != client) return;
                lock (Gate)
                {
                    if (_client != client || stop.IsCancellationRequested) return;
                    _blocked = c.PlayingBlocked;
                    if (_blocked)
                    {
                        if (Activity.Snapshot(DateTimeOffset.Now).State == SteamIdleTaskState.Running)
                            Activity.End(SteamIdleTaskState.Paused, DateTimeOffset.Now);
                        Update($"其他客户端正在玩游戏（{c.PlayingAppID}），已暂停挂时长；结束游戏后可手动恢复。");
                    }
                    else
                    {
                        var task = Activity.Snapshot(DateTimeOffset.Now);
                        // An unblocked notification does not mean our current games-played session stopped.
                        Update(task.State == SteamIdleTaskState.Running ? $"挂时长会话运行中 · {task.Name}" : task.State == SteamIdleTaskState.Paused ? "会话已空闲，点击恢复继续原任务。" : "已登录，选择游戏开始挂时长。");
                    }
                }
            });
            async Task AuthenticateAsync()
            {
                try
                {
                    AuthSession auth;
                    if (username == null)
                    {
                        var qr = await client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails { IsPersistentSession = false, DeviceFriendlyName = "图吧 Steam 工具箱" });
                        void RefreshQr() { if (_client == client && !stop.IsCancellationRequested) { QrUrl = qr.ChallengeURL; Update("请使用 Steam 手机客户端扫码，并确认登录"); } }
                        qr.ChallengeURLChanged = RefreshQr; RefreshQr();
                        auth = qr;
                    }
                    else
                    {
                        var details = new AuthSessionDetails
                        {
                            Username = username, Password = password, IsPersistentSession = false,
                            DeviceFriendlyName = "图吧 Steam 工具箱",
                            Authenticator = new PageAuthenticator(client, stop.Token, preferCode)
                        };
                        try { auth = await client.Authentication.BeginAuthSessionViaCredentialsAsync(details); }
                        finally { password = null; details.Password = null; }
                    }
                    var result = await auth.PollingWaitForResultAsync(stop.Token);
                    if (_client == client && !stop.IsCancellationRequested) { _accountName = result.AccountName; user.LogOn(new SteamUser.LogOnDetails { Username = result.AccountName, AccessToken = result.RefreshToken }); }
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { if (_client == client) { QrUrl = null; Update("登录失败：" + e.Message); client.Disconnect(); } }
            }
            Update("正在连接 Steam…");
            _ = Task.Run(() =>
            {
                try
                {
                    client.Connect();
                    while (!stop.IsCancellationRequested) manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(250));
                }
                catch (Exception e) { EndDisconnected(client, stop, "Steam 连接失败：" + e.Message); }
                finally { client.Disconnect(); }
            });
        }
    }
    private static Task MonitorConnectionAsync(SteamClient client, CancellationTokenSource stop) =>
        SteamIdleConnectionMonitor.RunAsync(async token =>
        {
            Task<SteamUnifiedMessages.ServiceMethodResponse<CPlayer_GetPlayerLinkDetails_Response>> responseTask;
            lock (Gate)
            {
                token.ThrowIfCancellationRequested();
                if (_client != client || !_loggedIn || !client.IsConnected || client.SteamID == null)
                    throw new IOException("Steam 会话连接已断开");
                // Round trip over THIS authenticated CM connection, not a public website reachability test.
                // Persona state (including Invisible/Offline) is unrelated to session connectivity.
                var player = client.GetHandler<SteamUnifiedMessages>()!.CreateService<SteamKit2.Internal.Player>();
                var request = new CPlayer_GetPlayerLinkDetails_Request();
                request.steamids.Add(client.SteamID.ConvertToUInt64());
                responseTask = player.GetPlayerLinkDetails(request).ToTask();
            }
            var response = await responseTask.WaitAsync(token).ConfigureAwait(false);
            if (response.Result != EResult.OK) throw new IOException($"Steam 在线检测失败：{response.Result}");
        }, () =>
        {
            lock (Gate)
            {
                if (_client != client || stop.IsCancellationRequested || !_loggedIn) return;
                _lastOnlineCheck = DateTimeOffset.Now;
                Changed?.Invoke();
            }
        }, e => EndDisconnected(client, stop, e is TimeoutException
            ? "Steam 在线检测超时，已中断挂时长，请重新登录。"
            : "无法确认 Steam 会话在线，已中断挂时长，请重新登录。"), stop.Token);

    private static void EndDisconnected(SteamClient client, CancellationTokenSource stop, string status)
    {
        lock (Gate)
        {
            // Late callbacks/probes from an old login must never end the replacement session.
            if (_client != client || stop.IsCancellationRequested) return;
            Activity.End(SteamIdleTaskState.Disconnected, DateTimeOffset.Now);
            _ownedGames = []; _hasOwnedGameList = false; _lastOnlineCheck = null;
            _loggedIn = false; _blocked = false; QrUrl = null; GuardPrompt = null;
            _guardCode?.TrySetCanceled(); _guardCode = null;
            stop.Cancel();
            client.Disconnect();
            Update(status);
        }
    }
    public static async Task<SteamOnlineSnapshot> GetOnlineInfoAsync()
    {
        var client = _client;
        if (!_loggedIn || client?.SteamID == null) throw new InvalidOperationException("请先登录 Steam。");
        var friends = client.GetHandler<SteamFriends>()!;
        var list = Enumerable.Range(0, friends.GetFriendCount()).Select(i => friends.GetFriendByIndex(i))
            .Select(id => new SteamOnlineFriend(id.ConvertToUInt64().ToString(), friends.GetFriendPersonaName(id) ?? id.ToString(), friends.GetFriendPersonaState(id).ToString())).ToList();
        var unified = client.GetHandler<SteamUnifiedMessages>()!;
        var player = unified.CreateService<SteamKit2.Internal.Player>();
        var response = await player.GetOwnedGames(new CPlayer_GetOwnedGames_Request
        {
            steamid = client.SteamID.ConvertToUInt64(), include_appinfo = true, include_free_sub = true, include_played_free_games = true
        });
        if (_client != client || !_loggedIn) throw new InvalidOperationException("登录会话已变化，请重新刷新。");
        if (response.Result != EResult.OK) throw new InvalidOperationException("游戏库读取失败：" + response.Result);
        var games = response.Body.games.Select(g => new SteamOwnedGame((uint)g.appid, g.name, (uint)g.playtime_forever)).OrderBy(g => g.Name).ToList();
        lock (Gate)
        {
            if (_client != client || !_loggedIn) throw new InvalidOperationException("登录会话已变化，请重新刷新。");
            _ownedGames = games; _hasOwnedGameList = true; _accountName = friends.GetPersonaName() ?? _accountName;
        }
        return new(friends.GetPersonaName() ?? "Steam 用户", client.SteamID.ConvertToUInt64().ToString(), list, games);
    }
    public static void Play(uint appId, string? name = null, string? art = null)
    {
        lock (Gate)
        {
            if (appId == 0)
            {
                // Sending GamesPlayed while another client owns the play session can log us out.
                if (_loggedIn && _client != null && !_blocked) SendPlayed(0);
                Activity.End(SteamIdleTaskState.Stopped, DateTimeOffset.Now);
                Update("已停止挂时长"); return;
            }
            if (!_loggedIn || _client == null) throw new InvalidOperationException("请先在本页登录挂卡会话。");
            if (_blocked) throw new InvalidOperationException("此账号正在其他客户端玩游戏，请结束游戏后再恢复挂时长。");
            if (_hasOwnedGameList && !_ownedGames.Any(g => g.AppId == appId)) throw new InvalidOperationException("当前登录账号未拥有此游戏，请从账号游戏库中选择。");
            var current = Activity.Snapshot(DateTimeOffset.Now);
            if (current.AppId == appId && current.State == SteamIdleTaskState.Running) return;
            name ??= _ownedGames.FirstOrDefault(g => g.AppId == appId)?.Name ?? $"游戏 {appId}";
            art ??= $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";
            SendPlayed(appId);
            Activity.Start(appId, name, art, AccountLabel, DateTimeOffset.Now);
            Update($"挂时长会话运行中 · {name}");
        }
    }
    public static void Pause()
    {
        lock (Gate)
        {
            if (Activity.Snapshot(DateTimeOffset.Now).State != SteamIdleTaskState.Running) return;
            if (_loggedIn && _client != null && !_blocked) SendPlayed(0);
            Activity.End(SteamIdleTaskState.Paused, DateTimeOffset.Now);
            Update("挂时长已暂停，点击恢复可继续原任务。");
        }
    }
    public static void Resume()
    {
        lock (Gate)
        {
            var task = Activity.Snapshot(DateTimeOffset.Now);
            if (task.State != SteamIdleTaskState.Paused) return;
            Play(task.AppId, task.Name, task.Art);
        }
    }
    private static void SendPlayed(uint appId)
    {
        var message = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
        if (appId != 0) message.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = new GameID(appId).ToUInt64() });
        _client!.Send(message);
    }
    public static void Disconnect()
    {
        lock (Gate) { DisconnectCore(); Update("挂卡会话已退出"); }
    }
    private static void DisconnectCore()
    {
        Activity.End(SteamIdleTaskState.Stopped, DateTimeOffset.Now);
        _ownedGames = []; _hasOwnedGameList = false; _lastOnlineCheck = null;
        var previous = _client; _client = null;
        _stop?.Cancel(); _stop = null;
        _guardCode?.TrySetCanceled(); _guardCode = null; GuardPrompt = null;
        _loggedIn = false; _blocked = false; QrUrl = null;
        previous?.Disconnect();
    }
    public static void SubmitGuardCode(string code)
    {
        lock (Gate)
        {
            if (_guardCode == null) throw new InvalidOperationException("当前没有等待提交的验证码。");
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("请输入 Steam Guard 验证码。");
            var pending = _guardCode; _guardCode = null; GuardPrompt = null;
            pending.TrySetResult(code.Trim());
            Update("正在验证 Steam Guard…");
        }
    }
    private sealed class PageAuthenticator(SteamClient client, CancellationToken token, bool preferCode) : IAuthenticator
    {
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) => RequestCodeAsync(previousCodeWasIncorrect ? "手机令牌码不正确，请输入新验证码" : "请输入 Steam 手机令牌码");
        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) => RequestCodeAsync(previousCodeWasIncorrect ? "邮箱验证码不正确，请重新输入" : $"请输入发送到 {email} 的 Steam Guard 邮箱验证码");
        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            if (preferCode) return Task.FromResult(false);
            if (_client == client) Update("请打开 Steam 手机客户端，确认本次登录；也可勾选使用令牌码后重新登录。");
            return Task.FromResult(true);
        }
        private async Task<string> RequestCodeAsync(string prompt)
        {
            TaskCompletionSource<string> pending;
            lock (Gate)
            {
                token.ThrowIfCancellationRequested();
                if (_client != client) throw new OperationCanceledException();
                pending = _guardCode = new(TaskCreationOptions.RunContinuationsAsynchronously);
                GuardPrompt = prompt; Update(prompt);
            }
            return await pending.Task.WaitAsync(token);
        }
    }
}

internal sealed record SteamOnlineFriend(string Id, string Name, string State)
{
    public string Display => $"{Name} · {State} · {Id}";
}
internal sealed record SteamOwnedGame(uint AppId, string Name, uint Minutes)
{
    public string Display => $"{Name} · {Minutes / 60} 小时 {Minutes % 60} 分钟 · {AppId}";
}
internal sealed record SteamOnlineSnapshot(string Name, string Id, IReadOnlyList<SteamOnlineFriend> Friends, IReadOnlyList<SteamOwnedGame> Games);
