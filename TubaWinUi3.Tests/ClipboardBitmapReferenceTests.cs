using TubaWinUi3.Services;
using Windows.Storage.Streams;

namespace TubaWinUi3.Tests;

/// <summary>
/// 截图复制到剪贴板的位图引用（ClipboardService.CreateBitmapReferenceAsync）。
///
/// 回归：这里原用 DataWriter「同步」写入 —— DataWriter 被 Dispose 时会关闭底层
/// InMemoryRandomAccessStream（除非先 DetachStream），WriteBytes 又只进内部缓冲
/// （要 StoreAsync 才落流），结果每次复制都抛 ObjectDisposedException（0x80000013），
/// 界面却提示「剪贴板被其他程序占用，请稍后重试」。
///
/// 锁定两个必须保持的语义：流保持可读、位置在开头，且内容与输入逐字节一致。
/// </summary>
public class ClipboardBitmapReferenceTests
{
    [Fact]
    public async Task CreateBitmapReferenceAsync_KeepsStreamReadableWithIdenticalBytes()
    {
        var bytes = Enumerable.Range(0, 4096).Select(i => (byte)(i * 31)).ToArray();

        var reference = await ClipboardService.CreateBitmapReferenceAsync(bytes);

        using var stream = await reference.OpenReadAsync();
        Assert.Equal(bytes.Length, (int)stream.Size);

        var reader = new DataReader(stream);
        await reader.LoadAsync((uint)bytes.Length);
        var readBack = new byte[bytes.Length];
        reader.ReadBytes(readBack);

        Assert.Equal(bytes, readBack);
    }
}
