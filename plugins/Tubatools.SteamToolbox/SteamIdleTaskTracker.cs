namespace Tubatools.SteamToolbox;

internal enum SteamIdleTaskState { None, Running, Paused, Disconnected, Stopped }
internal sealed record SteamIdleTaskSnapshot(SteamIdleTaskState State, uint AppId, string Name, string Art,
    string Account, DateTimeOffset? StartedAt, TimeSpan Elapsed);

// Transport-independent task lifecycle. All timestamps are supplied by the caller
// so pauses, repeated clicks and disconnections can be verified without a Steam account.
internal sealed class SteamIdleTaskTracker
{
    private SteamIdleTaskState _state;
    private uint _appId;
    private string _name = "", _art = "", _account = "";
    private DateTimeOffset? _startedAt, _runningSince;
    private TimeSpan _elapsed;
    public SteamIdleTaskSnapshot Snapshot(DateTimeOffset now) => new(_state, _appId, _name, _art, _account,
        _startedAt, _elapsed + (_runningSince is { } since && now > since ? now - since : TimeSpan.Zero));
    public void Start(uint appId, string name, string art, string account, DateTimeOffset now)
    {
        if (appId == 0) throw new ArgumentOutOfRangeException(nameof(appId));
        if (_appId == appId && _state == SteamIdleTaskState.Running) return;
        bool resume = _appId == appId && _state == SteamIdleTaskState.Paused;
        if (!resume) { _elapsed = TimeSpan.Zero; _startedAt = now; }
        _appId = appId; _name = name; _art = art; _account = account;
        _runningSince = now; _state = SteamIdleTaskState.Running;
    }
    public void End(SteamIdleTaskState state, DateTimeOffset now)
    {
        if (state is SteamIdleTaskState.Running or SteamIdleTaskState.None) throw new ArgumentException("Invalid terminal state", nameof(state));
        if (_appId == 0) return;
        if (_runningSince is { } since && now > since) _elapsed += now - since;
        _runningSince = null;
        // A network drop after a deliberate stop must not turn the stopped task into a failed task.
        if (state == SteamIdleTaskState.Disconnected && _state == SteamIdleTaskState.Stopped) return;
        _state = state;
    }
}
