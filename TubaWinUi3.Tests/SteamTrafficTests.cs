using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>「网站加速」流量统计仍在主程序内，Steam 工具箱插件迁移不影响这部分。</summary>
public sealed class SteamTrafficTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(8));

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
