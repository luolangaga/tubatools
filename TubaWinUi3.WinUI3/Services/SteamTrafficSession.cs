using System.Collections.Concurrent;

namespace TubaWinUi3.Services;

public sealed record SteamTrafficPoint(DateTimeOffset Time, double Upload, double Download);
public sealed record SteamProxyConnection(long Id, string Host, string Endpoint, DateTimeOffset OpenedAt,
    long Upload, long Download, string State)
{
    public string Transfer => $"↑ {SteamTrafficSession.FormatBytes(Upload)}   ↓ {SteamTrafficSession.FormatBytes(Download)}";
    public string Detail => $"{Endpoint} · TLS / HTTP/1.1 · {State}";
}
public sealed record SteamTrafficSnapshot(DateTimeOffset StartedAt, long Upload, long Download, double UploadRate,
    double DownloadRate, int ActiveConnections, long Requests, long Failures,
    IReadOnlyList<SteamTrafficPoint> History, IReadOnlyList<SteamProxyConnection> Connections);

// Per-run counters avoid old pooled connections contaminating a restarted accelerator.
internal sealed class SteamTrafficSession(DateTimeOffset startedAt)
{
    internal sealed class Connection(long id, string host, string endpoint, DateTimeOffset opened)
    {
        public readonly long Id = id;
        public readonly string Host = host, Endpoint = endpoint;
        public readonly DateTimeOffset Opened = opened;
        public long Up, Down;
        public int Closed;
    }
    private readonly DateTimeOffset _startedAt = startedAt;
    private readonly ConcurrentDictionary<long, Connection> _connections = new();
    private readonly object _gate = new();
    private readonly Queue<SteamTrafficPoint> _history = new();
    private long _up, _down, _requests, _failures, _nextId, _previousUp, _previousDown;
    private DateTimeOffset _lastSample = startedAt;
    private double _upRate, _downRate;
    public void Request() => Interlocked.Increment(ref _requests);
    public void Fail() => Interlocked.Increment(ref _failures);
    public Stream Meter(Stream stream, string host, string endpoint)
    {
        var connection = new Connection(Interlocked.Increment(ref _nextId), host, endpoint, DateTimeOffset.Now);
        _connections[connection.Id] = connection;
        foreach (var old in _connections.Values.Where(c => Volatile.Read(ref c.Closed) != 0).OrderByDescending(c => c.Id).Skip(80)) _connections.TryRemove(old.Id, out _);
        return new MeteredStream(stream, this, connection);
    }
    public void Sample(DateTimeOffset now)
    {
        lock (_gate)
        {
            double seconds = (now - _lastSample).TotalSeconds;
            if (seconds <= 0) return;
            long up = Interlocked.Read(ref _up), down = Interlocked.Read(ref _down);
            _upRate = Math.Max(0, up - _previousUp) / seconds;
            _downRate = Math.Max(0, down - _previousDown) / seconds;
            _previousUp = up; _previousDown = down; _lastSample = now;
            _history.Enqueue(new(now, _upRate, _downRate));
            while (_history.Count > 60) _history.Dequeue();
        }
    }
    public SteamTrafficSnapshot Snapshot()
    {
        lock (_gate)
        {
            var connections = _connections.Values.Select(c => new SteamProxyConnection(c.Id, c.Host, c.Endpoint, c.Opened,
                Interlocked.Read(ref c.Up), Interlocked.Read(ref c.Down), Volatile.Read(ref c.Closed) == 0 ? "已连接" : "已关闭"))
                .OrderBy(c => c.State == "已关闭").ThenByDescending(c => c.Id).ToList();
            return new(_startedAt, Interlocked.Read(ref _up), Interlocked.Read(ref _down), _upRate, _downRate,
                connections.Count(c => c.State == "已连接"), Interlocked.Read(ref _requests), Interlocked.Read(ref _failures),
                _history.ToArray(), connections.Take(120).ToList());
        }
    }
    public static string FormatBytes(double bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824:0.00} GB" : bytes >= 1048576 ? $"{bytes / 1048576:0.0} MB" : bytes >= 1024 ? $"{bytes / 1024:0.0} KB" : $"{bytes:0} B";
    private sealed class MeteredStream(Stream inner, SteamTrafficSession session, Connection connection) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        private void ReadCount(int count) { Interlocked.Add(ref session._down, count); Interlocked.Add(ref connection.Down, count); }
        private void WriteCount(int count) { Interlocked.Add(ref session._up, count); Interlocked.Add(ref connection.Up, count); }
        public override int Read(byte[] buffer, int offset, int count) { int n = inner.Read(buffer, offset, count); ReadCount(n); return n; }
        public override int Read(Span<byte> buffer) { int n = inner.Read(buffer); ReadCount(n); return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { int n = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); ReadCount(n); return n; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Write(byte[] buffer, int offset, int count) { inner.Write(buffer, offset, count); WriteCount(count); }
        public override void Write(ReadOnlySpan<byte> buffer) { inner.Write(buffer); WriteCount(buffer.Length); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false); WriteCount(buffer.Length); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing && Interlocked.Exchange(ref connection.Closed, 1) == 0) inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref connection.Closed, 1) == 0) await inner.DisposeAsync().ConfigureAwait(false); GC.SuppressFinalize(this); }
    }
}
