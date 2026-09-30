using System.Runtime.InteropServices;
using TubaWinUi3.Services.Telemetry;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace TubaWinUi3.Services;

/// <summary>一次复制尝试的结果。</summary>
internal readonly record struct ClipboardCopyResult(bool Success, int HResult, Exception? Error)
{
    /// <summary>失败的 HRESULT 十六进制形式，用于日志与上报（成功时为空）。</summary>
    internal string HResultHex => $"0x{HResult:X8}";
}

/// <summary>
/// 剪贴板写入的唯一入口（防崩溃 + 重试 + 诊断）。
///
/// 背景：<c>Clipboard.SetContent</c> 需要打开共享剪贴板，**另一个进程/线程正持有它时**
/// 抛出 <c>COMException</c>（HRESULT 0x800401D0，CLIPBRD_E_CANT_OPEN）。这个异常从
/// Tapped/Click 处理器里逃出去就是未处理异常 —— 用户看到的是"点一下复制就闪退"
/// （Issue：硬件信息页点"运行时间"卡片必崩）。剪贴板占用是瞬时状态，官方建议
/// 加一个带短延时的重试循环（learn.microsoft.com/answers/questions/1695747）。
///
/// 因此全应用统一走本类：失败重试、用尽后返回结果而不是抛异常、失败落盘一行日志
/// 并上报遥测（含 HRESULT，便于下次从用户报告里直接认出真凶）。
///
/// <para><b>线程约定</b>：WinRT 剪贴板 API 必须在有 DispatcherQueue 的 UI 线程调用。
/// 从后台线程调用会得到 RPC_E_WRONG_THREAD —— 本类不重试这种失败，只记日志，让调用点
/// 的错误自己浮出来。</para>
///
/// <para><b>同步版本的代价</b>：<see cref="TrySetText"/> 只在失败路径上阻塞 UI 线程，
/// 上限 460ms；正常复制零额外开销。<see cref="TrySetTextAsync"/> 语义完全相同，
/// 只是把 Thread.Sleep 换成 Task.Delay。</para>
/// </summary>
internal static class ClipboardService
{
    // ── 已知 HRESULT ──
    /// <summary>剪贴板被其他进程占用（OpenClipboard 失败）。瞬时状态，重试可解。</summary>
    internal const int HResultClipboardCantOpen = unchecked((int)0x800401D0);
    /// <summary>拒绝访问（策略/会话隔离）。</summary>
    internal const int HResultAccessDenied = unchecked((int)0x80070005);
    /// <summary>无法写入剪贴板（CLIPBRD_E_CANT_SET）。</summary>
    internal const int HResultCantSetClipboard = unchecked((int)0x800401D1);
    /// <summary>调用了错误的线程（后台线程调用 WinRT 剪贴板 API）。重试无意义。</summary>
    internal const int HResultWrongThread = unchecked((int)0x8001010E);

    /// <summary>重试间隔（首次失败后依次等待）：总阻塞上限 460ms。</summary>
    internal static readonly int[] RetryDelaysMs = [40, 120, 300];

    private const long MaxLogBytes = 1024 * 1024;

    /// <summary>串行化写入，避免连点造成的重复竞争。</summary>
    private static readonly object _gate = new();

    private static string LogPath => Path.Combine(ConfigManager.GetDataDir(), "Clipboard", "error.log");

    /// <summary>
    /// 把文本写入剪贴板；失败时重试若干次后返回结果，**从不抛异常**。
    /// 必须从 UI 线程调用。
    /// </summary>
    /// <param name="text">要复制的文本；空/空白直接返回成功（不触碰剪贴板）。</param>
    /// <param name="flush">成功后是否调用 <c>Clipboard.Flush()</c>（应用退出后内容仍可粘贴）。</param>
    public static ClipboardCopyResult TrySetText(string? text, bool flush = false)
    {
        if (string.IsNullOrEmpty(text)) return new ClipboardCopyResult(true, 0, null);

        return RetryCore(
            attempt: _ => WriteText(text!, flush),
            delaysMs: RetryDelaysMs,
            sleepMs: Thread.Sleep);
    }

    /// <summary><see cref="TrySetText"/> 的异步版本（重试语义完全一致，仅不阻塞 UI 线程）。</summary>
    public static async Task<ClipboardCopyResult> TrySetTextAsync(string? text, bool flush = false)
    {
        if (string.IsNullOrEmpty(text)) return new ClipboardCopyResult(true, 0, null);

        return await RetryCoreAsync(
            attempt: _ => WriteText(text!, flush),
            delaysMs: RetryDelaysMs,
            delayAsync: static ms => Task.Delay(ms)).ConfigureAwait(true);
    }

    /// <summary>一次文本写入尝试（每次重建 DataPackage：上一次的包可能仍被剪贴板持有）。</summary>
    private static ClipboardCopyResult WriteText(string text, bool flush)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
        if (flush) Clipboard.Flush();
        return new ClipboardCopyResult(true, 0, null);
    }

    /// <summary>
    /// 把位图写入剪贴板（硬件信息页「截图」按钮）。流必须每次尝试重建：
    /// 上一次的 DataPackage 可能仍被剪贴板持有，复用一个已消费的流会失败。
    /// </summary>
    /// <param name="referenceFactory">参数为尝试序号（从 0 开始），每次尝试都会调用一次。</param>
    public static ClipboardCopyResult TrySetBitmap(Func<int, RandomAccessStreamReference> referenceFactory)
    {
        ArgumentNullException.ThrowIfNull(referenceFactory);

        return RetryCore(
            attempt: attemptIndex =>
            {
                var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                package.SetBitmap(referenceFactory(attemptIndex));
                Clipboard.SetContent(package);
                return new ClipboardCopyResult(true, 0, null);
            },
            delaysMs: RetryDelaysMs,
            sleepMs: Thread.Sleep);
    }

    /// <summary>
    /// 重试编排核心（与 WinRT 完全解耦，可单测）：尝试一次，失败后按
    /// <paramref name="delaysMs"/> 依次等待并重试；不可重试的失败立即返回。
    /// <paramref name="attempt"/> 的参数为尝试序号（首次为 0），便于每次尝试重建载荷。
    /// </summary>
    internal static ClipboardCopyResult RetryCore(
        Func<int, ClipboardCopyResult> attempt,
        int[] delaysMs,
        Action<int>? sleepMs)
    {
        var result = RunAttempt(attempt, 0);

        for (var i = 0; result is { Success: false } && IsRetryable(result.HResult) && i < delaysMs.Length; i++)
        {
            sleepMs?.Invoke(delaysMs[i]);
            result = RunAttempt(attempt, i + 1);
        }

        if (!result.Success) ReportFailure(result);
        return result;
    }

    /// <summary><see cref="RetryCore"/> 的异步版本（把等待换成 Task.Delay）。</summary>
    internal static async Task<ClipboardCopyResult> RetryCoreAsync(
        Func<int, ClipboardCopyResult> attempt,
        int[] delaysMs,
        Func<int, Task> delayAsync)
    {
        var result = RunAttempt(attempt, 0);

        for (var i = 0; result is { Success: false } && IsRetryable(result.HResult) && i < delaysMs.Length; i++)
        {
            await delayAsync(delaysMs[i]).ConfigureAwait(true);
            result = RunAttempt(attempt, i + 1);
        }

        if (!result.Success) ReportFailure(result);
        return result;
    }

    /// <summary>重试一次尝试；把任何异常收成一个失败结果（宿主永远不会看到异常）。</summary>
    private static ClipboardCopyResult RunAttempt(Func<int, ClipboardCopyResult> attempt, int attemptIndex)
    {
        try
        {
            return attempt(attemptIndex);
        }
        catch (Exception ex)
        {
            return new ClipboardCopyResult(false, ex.HResult, ex);
        }
    }

    /// <summary>
    /// 是否值得重试。<c>AccessDenied</c>/<c>CantSet</c>/<c>WrongThread</c> 都是稳定状态，
    /// 重试只会白白拖延用户；其余（含 <c>CLIPBRD_E_CANT_OPEN</c>）按瞬时竞争处理。
    /// </summary>
    private static bool IsRetryable(int hresult) =>
        hresult != HResultAccessDenied &&
        hresult != HResultCantSetClipboard &&
        hresult != HResultWrongThread;

    /// <summary>失败落盘一行日志 + 遥测上报；诊断本身绝不能成为新的崩溃源。</summary>
    private static void ReportFailure(ClipboardCopyResult result)
    {
        var ex = result.Error ?? new COMException($"剪贴板写入失败（HRESULT {result.HResultHex}）", result.HResult);

        try
        {
            WriteLogLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 剪贴板写入失败 {result.HResultHex} " +
                         $"（已重试 {RetryDelaysMs.Length} 次）{ex.GetType().Name}: {ex.Message}");
        }
        catch { }

        try
        {
            TelemetryService.TrackException(ex, "Clipboard");
        }
        catch { }
    }

    private static void WriteLogLine(string line)
    {
        try
        {
            lock (_gate)
            {
                var path = LogPath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxLogBytes)
                {
                    // 超限截断保留尾部（照抄 AiDiagnosticsLog 的做法）
                    var tail = ReadTail(path, 256 * 1024);
                    File.WriteAllText(path, tail);
                }

                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch { }
    }

    private static string ReadTail(string path, int maxBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var start = Math.Max(0, stream.Length - maxBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }
}
