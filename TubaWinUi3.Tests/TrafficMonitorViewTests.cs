using System.Reflection;
using System.Runtime.InteropServices;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class TrafficMonitorViewTests
{
    private static TrafficConnectionInfo Connection(string key, int pid, long download, long upload = 0, bool available = true)
        => new() { Key = key, ProcessId = pid, ProcessName = "Browser", RemoteAddress = "2001:db8::1", RemotePort = 443,
            LocalAddress = "192.168.1.2", LocalPort = 50000, RemoteDomain = "cdn.example.com", SpeedIn = download,
            SpeedOut = upload, TotalIn = download * 10, TotalOut = upload * 10, HasTrafficStats = available };

    [Fact]
    public void ProcessView_GroupsByPid_AndSortsByCombinedRate()
    {
        var result = TrafficMonitorView.Build([Connection("a", 10, 100), Connection("b", 10, 50), Connection("c", 20, 200)], "", true, false, TrafficSort.Activity);
        Assert.Equal([20, 10], result.Select(e => e.ProcessId));
        Assert.Equal(150, result[1].SpeedIn);
        Assert.Equal(1500, result[1].TotalIn);
        Assert.Equal(2, result[1].Connections.Count);
    }

    [Fact]
    public void Filter_AppliesBeforeGrouping_AndSupportsExactPid()
    {
        var source = new[] { Connection("a", 10, 100), Connection("b", 100, 200), Connection("c", 10, 0) };
        var result = TrafficMonitorView.Build(source, "pid:10 CDN.EXAMPLE", true, true, TrafficSort.Download);
        Assert.Single(result);
        Assert.Equal(10, result[0].ProcessId);
        Assert.Single(result[0].Connections);
        Assert.Empty(TrafficMonitorView.Build(source, "pid:1", false, false, TrafficSort.Activity));
    }

    [Fact]
    public void UnknownStatistics_DoNotPretendToBeKnownZero()
    {
        var mixed = TrafficMonitorView.Build([Connection("a", 10, 100), Connection("b", 10, 999, available: false)], "", true, false, TrafficSort.Activity).Single();
        Assert.True(mixed.HasStats);
        Assert.True(mixed.PartialStats);
        Assert.Equal(100, mixed.SpeedIn);
        var unknown = TrafficMonitorView.Build([Connection("b", 10, 0, available: false)], "", true, false, TrafficSort.Activity).Single();
        Assert.False(unknown.HasStats);
    }

    [Fact]
    public void Csv_ExpandsAggregatedRows_AndEscapesExternalNames()
    {
        var dangerous = new TrafficConnectionInfo { Key = "x", ProcessId = 10, ProcessName = "=SUM(1,2)", RemoteDomain = "a\"b", HasTrafficStats = false };
        var entries = TrafficMonitorView.Build([dangerous, Connection("a", 10, 12)], "", true, false, TrafficSort.Activity);
        var csv = TrafficMonitorView.ExportView(entries, new DateTime(2026, 10, 28));
        Assert.Contains("\"'=SUM(1,2)\"", csv);
        Assert.Contains("\"a\"\"b\"", csv);
        Assert.Contains(",\"\",\"\",\"\",\"\",\"不可用\"", csv);
        Assert.Equal(3, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [InlineData(3000, 1.5, 2000)]
    [InlineData(1000, 0.5, 2000)]
    [InlineData(1000, 0, 0)]
    [InlineData(-5, 1, 0)]
    public void Rate_UsesElapsedTime(long bytes, double seconds, long expected)
        => Assert.Equal(expected, TrafficMonitorService.CalculateRate(bytes, seconds));

    [Fact]
    public void Ipv6OwnerPidLayout_MatchesWindowsNativeContract()
    {
        var native = typeof(TrafficMonitorService).GetNestedType("Native", BindingFlags.NonPublic)!;
        var row = native.GetNestedType("MIB_TCP6ROW_OWNER_PID", BindingFlags.NonPublic)!;
        Assert.Equal(56, Marshal.SizeOf(row));
        Assert.Equal(0, Marshal.OffsetOf(row, "LocalAddr").ToInt32());
        Assert.Equal(48, Marshal.OffsetOf(row, "dwState").ToInt32());
        Assert.Equal(52, Marshal.OffsetOf(row, "dwOwningPid").ToInt32());
        Assert.Equal("[2001:db8::1]:443", Connection("a", 10, 0).DisplayRemote);
    }

    [Fact]
    public void ExportRecording_RemainsUsableAfterRecorderChanges()
    {
        var recorder = new TrafficSnapshotRecorder();
        recorder.Add(new TrafficSnapshot { Time = new(2026, 10, 28), AdapterIndex = 7, TotalIn = 3000, SpeedIn = 2000 });
        var snapshot = recorder.ToArray();
        recorder.Clear();
        Assert.Single(snapshot);
        Assert.Contains("\"7\",\"3000\",\"0\",\"2000\"", TrafficMonitorView.ExportRecording(snapshot));
    }
}
