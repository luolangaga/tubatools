using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace TubaWinUi3.Services.Plugins;

/// <summary>
/// 插件包的字节区间来源。抽成接口是为了让远程 Range 读取和单测共用同一套解包逻辑：
/// 插件商店只需要包里的清单和图标，不该为了一条列表项下载整个包（社区包动辄十几 MB）。
/// </summary>
internal interface IRangeSource
{
    long Length { get; }
    /// <summary>读取 [start, end] 闭区间；服务器返回不足时按实际长度返回。</summary>
    byte[] Read(long start, long end);
}

/// <summary>NuGet 包（Azure Blob 源）支持 Range，用它只取中央目录、清单与图标。</summary>
internal sealed class HttpRangeSource : IRangeSource, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly CancellationToken _ct;
    private byte[]? _whole;

    public long Length { get; }

    private HttpRangeSource(HttpClient http, string url, CancellationToken ct, long length)
    {
        _http = http;
        _url = url;
        _ct = ct;
        Length = length;
    }

    public static HttpRangeSource Open(HttpClient http, string url, CancellationToken ct)
    {
        using var head = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = http.Send(head, ct);
        var length = response.Content.Headers.ContentLength;
        if (response.StatusCode != HttpStatusCode.OK || length is null || length <= 0)
            throw new IOException($"无法读取插件包长度：HTTP {(int)response.StatusCode}");
        return new HttpRangeSource(http, url, ct, length.Value);
    }

    public byte[] Read(long start, long end)
    {
        start = Math.Clamp(start, 0, Length - 1);
        end = Math.Clamp(end, start, Length - 1);
        if (_whole is not null) return Slice(_whole, start, end);

        using var request = new HttpRequestMessage(HttpMethod.Get, _url);
        request.Headers.Range = new RangeHeaderValue(start, end);
        using var response = _http.Send(request, _ct);
        if (response.StatusCode == HttpStatusCode.PartialContent) return ReadBody(response);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            // 代理或镜像忽略 Range 时整包已经进了响应体，直接留住，后续不再发请求。
            _whole = ReadBody(response);
            return Slice(_whole, start, end);
        }
        throw new IOException($"读取插件包失败：HTTP {(int)response.StatusCode}");
    }

    private static byte[] ReadBody(HttpResponseMessage response)
    {
        using var stream = response.Content.ReadAsStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static byte[] Slice(byte[] source, long start, long end)
    {
        int offset = (int)Math.Min(start, source.Length);
        int count = (int)Math.Min(end - start + 1, source.Length - offset);
        return count <= 0 ? [] : source.AsSpan(offset, count).ToArray();
    }

    public void Dispose() { _whole = null; }
}

/// <summary>把 Range 来源伪装成可随机访问的流，交给 <see cref="ZipArchive"/> 自己解析中央目录。</summary>
internal sealed class RangeReadStream : Stream
{
    private const int MaxChunks = 32;
    private readonly IRangeSource _source;
    private readonly int _chunkSize;
    private readonly Dictionary<long, byte[]> _chunks = new();
    private long _position;

    public RangeReadStream(IRangeSource source, int chunkSize = 16 * 1024)
    {
        _source = source;
        _chunkSize = chunkSize;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _source.Length;
    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, Length);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length && _position < Length)
        {
            long index = _position / _chunkSize;
            if (!_chunks.TryGetValue(index, out var chunk)) chunk = Cache(index);
            long within = _position - index * _chunkSize;
            if (within >= chunk.Length) break; // 服务器只返回了不足一块的数据，交给调用方决定重试
            int take = (int)Math.Min(Math.Min(buffer.Length - total, chunk.Length - within), Length - _position);
            chunk.AsSpan((int)within, take).CopyTo(buffer[total..]);
            total += take;
            _position += take;
        }
        return total;
    }

    private byte[] Cache(long index)
    {
        long start = index * _chunkSize;
        long end = Math.Min(start + _chunkSize - 1, Length - 1);
        var bytes = _source.Read(start, end);
        if (_chunks.Count >= MaxChunks) _chunks.Clear();
        _chunks[index] = bytes;
        return bytes;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _chunks.Clear();
        base.Dispose(disposing);
    }
}
