using System.IO.Compression;

namespace TubaWinUi3.Services;

/// <summary>
/// WinGet 官方源 .mszyml（Content-Type: application/x-ms-zip-yaml）解压。
/// 格式：28 字节头部 + 若干 MSZIP 块，每块为 "CK" 标记 + 一个独立的 raw-deflate 流。
/// 头部字段：0-3 为魔术 0x0A51E5C0；8-15 / 16-23 为解压后长度（小端 u64）；
/// 24-27 为从第一个 "CK" 起的载荷长度（小端 u32）。头部的长度只用于自检，解压按块推进。
/// </summary>
internal static class MszipCompression
{
    private const int HeaderSize = 28;
    private static readonly byte[] Magic = [0x0A, 0x51, 0xE5, 0xC0];

    internal static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize + 2 || !data[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("MSZIP 数据头部无效。");

        // 解压后长度写在头部（base64 之外唯一可信的自检依据）；缺失时按 4 倍压缩比预分配。
        var declaredSize = BitConverter.ToInt64(data.Slice(8, 8));
        using var output = new MemoryStream(
            declaredSize is > 0 and < 64 * 1024 * 1024 ? (int)declaredSize : Math.Max(4096, data.Length * 4));

        var pos = HeaderSize;
        while (pos < data.Length)
        {
            if (data[pos..].IndexOfAnyExcept((byte)0) < 0) break; // 末尾零填充

            if (data.Length - pos < 2 || data[pos] != (byte)'C' || data[pos + 1] != (byte)'K')
                throw new InvalidDataException($"MSZIP 块标记缺失（偏移 {pos}）。");

            var blockStart = pos + 2;
            using var input = new MemoryStream(data[blockStart..].ToArray(), writable: false);
            using var throttle = new OneByteReadStream(input);
            using var deflate = new DeflateStream(throttle, CompressionMode.Decompress, leaveOpen: true);
            deflate.CopyTo(output);

            if (throttle.Consumed <= 0)
                throw new InvalidDataException($"MSZIP 块未消耗任何数据（偏移 {pos}）。");
            pos = blockStart + checked((int)throttle.Consumed);
        }

        return output.ToArray();
    }

    /// <summary>
    /// 每次 Read 只交出一个字节的包装流：DeflateStream 会预读，只有把单次可读量压到 1 字节，
    /// 解压结束后 <see cref="Consumed"/> 才精确等于该 deflate 流占用的输入长度 ——
    /// 多块文件正是靠它定位下一个 "CK" 块。样本文件均 &lt; 100 KB，逐字节喂入的额外开销可忽略。
    /// </summary>
    private sealed class OneByteReadStream(Stream inner) : Stream
    {
        public long Consumed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => Consumed;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length > 1) buffer = buffer[..1];
            var read = inner.Read(buffer);
            Consumed += read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
