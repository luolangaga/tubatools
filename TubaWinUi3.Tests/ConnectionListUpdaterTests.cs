using System.Collections.ObjectModel;
using System.Collections.Specialized;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class ConnectionListUpdaterTests
{
    private static ConnectionEntry Row(int port, string name = "browser", int localPort = 1234) => new()
    {
        ProcessId = 12, ProcessName = name, LocalAddress = "127.0.0.1", LocalPort = localPort,
        RemoteAddress = "1.2.3.4", RemotePort = port, AdapterName = "Ethernet", AdapterType = "Ethernet"
    };

    [Fact]
    public void UnchangedSnapshot_PreservesRowsWithoutCollectionNotifications()
    {
        var first = Row(443); var second = Row(80);
        ObservableCollection<ConnectionEntry> rows = [first, second];
        int notifications = 0;
        rows.CollectionChanged += (_, _) => notifications++;
        ConnectionListUpdater.Apply(rows, [Row(443), Row(80)]);
        Assert.Equal(0, notifications);
        Assert.Same(first, rows[0]); Assert.Same(second, rows[1]);
    }

    [Fact]
    public void ChangedSnapshot_AddsRemovesReordersAndUpdatesWithoutReset()
    {
        var first = Row(443); var second = Row(80);
        ObservableCollection<ConnectionEntry> rows = [first, second, Row(22)];
        rows.CollectionChanged += (_, e) => Assert.NotEqual(NotifyCollectionChangedAction.Reset, e.Action);
        ConnectionListUpdater.Apply(rows, [Row(80, "changed"), Row(53), Row(443)]);
        Assert.Equal(new[] { 80, 53, 443 }, rows.Select(r => r.RemotePort));
        Assert.Equal("changed", rows[0].ProcessName);
        Assert.Same(first, rows[2]);
        Assert.DoesNotContain(second, rows);
    }

    [Fact]
    public void LocalPorts_DistinguishConnectionsToSameRemoteEndpoint()
    {
        ObservableCollection<ConnectionEntry> rows = [];
        ConnectionListUpdater.Apply(rows, [Row(443, localPort: 1), Row(443, localPort: 2), Row(443, localPort: 1)]);
        Assert.Equal(2, rows.Count);
        ConnectionListUpdater.Apply(rows, []);
        Assert.Empty(rows);
    }
}
