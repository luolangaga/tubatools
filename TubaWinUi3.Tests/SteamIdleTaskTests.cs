using TubaWinUi3.Services;
namespace TubaWinUi3.Tests;
public sealed class SteamIdleTaskTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(8));
    [Fact]
    public void RepeatedStartDoesNotResetTimeAndSwitchReplacesTask()
    {
        var task = new SteamIdleTaskTracker();
        task.Start(10, "First", "art", "account", Start);
        task.Start(10, "First", "art", "account", Start.AddMinutes(4));
        Assert.Equal(TimeSpan.FromMinutes(5), task.Snapshot(Start.AddMinutes(5)).Elapsed);
        task.Start(20, "Second", "art2", "account", Start.AddMinutes(6));
        var current = task.Snapshot(Start.AddMinutes(7));
        Assert.Equal((uint)20, current.AppId); Assert.Equal("Second", current.Name);
        Assert.Equal(TimeSpan.FromMinutes(1), current.Elapsed);
    }
    [Fact]
    public void PauseExcludesPausedTimeAndResumeKeepsOriginalStart()
    {
        var task = new SteamIdleTaskTracker(); task.Start(10, "Game", "art", "account", Start);
        task.End(SteamIdleTaskState.Paused, Start.AddMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(2), task.Snapshot(Start.AddHours(1)).Elapsed);
        task.Start(10, "Game", "art", "account", Start.AddMinutes(10));
        var current = task.Snapshot(Start.AddMinutes(13));
        Assert.Equal(TimeSpan.FromMinutes(5), current.Elapsed); Assert.Equal(Start, current.StartedAt);
    }
    [Fact]
    public void DisconnectStopsTimerAndDoesNotOverwriteDeliberateStop()
    {
        var task = new SteamIdleTaskTracker(); task.Start(10, "Game", "art", "account", Start);
        task.End(SteamIdleTaskState.Disconnected, Start.AddMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(3), task.Snapshot(Start.AddHours(1)).Elapsed);
        task.End(SteamIdleTaskState.Stopped, Start.AddHours(1));
        task.End(SteamIdleTaskState.Disconnected, Start.AddHours(2));
        Assert.Equal(SteamIdleTaskState.Stopped, task.Snapshot(Start.AddHours(3)).State);
    }
    [Fact]
    public async Task TrafficCountsActualBytesAndKeepsSessionsSeparate()
    {
        var session = new SteamTrafficSession(Start);
        using var data = new MemoryStream(new byte[40]);
        await using (var stream = session.Meter(data, "github.com", "192.0.2.1:443"))
        {
            Assert.Equal(10, await stream.ReadAsync(new byte[10]));
            await stream.WriteAsync(new byte[20]);
            session.Sample(Start.AddSeconds(2));
            var current = session.Snapshot();
            Assert.Equal(10, current.Download); Assert.Equal(20, current.Upload);
            Assert.Equal(5, current.DownloadRate); Assert.Equal(10, current.UploadRate);
            Assert.Equal(1, current.ActiveConnections);
            Assert.Equal("github.com", Assert.Single(current.Connections).Host);
        }
        Assert.Equal(0, session.Snapshot().ActiveConnections);
        Assert.Equal(0, new SteamTrafficSession(Start).Snapshot().Upload);
        for (int i = 3; i < 100; i++) session.Sample(Start.AddSeconds(i));
        Assert.Equal(60, session.Snapshot().History.Count);
    }
}
