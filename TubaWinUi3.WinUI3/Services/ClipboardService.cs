using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using TubaWinUi3.Services.Telemetry;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace TubaWinUi3.Services;

/// <summary>一次复制尝试的结果。</summary>
internal readonly record struct ClipboardCopyResult(bool Success, int HResult, Exception? Error, int Attempts = 1)
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
/// <para><b>只有"稍后能成功"的错误才重试</b>：见 <see cref="IsRetryable"/>。访问拒绝、
/// 跨线程调用、对象已释放、E_FAIL 这类是确定性失败，重试只是白拖 460ms 并掩盖真因；
/// 重试次数也不再固定写死 3 次，日志里报告的是**实际尝试次数**（<see cref="ClipboardCopyResult.Attempts"/>）。</para>
///
/// <para><b>线程约定</b>：WinRT 剪贴板 API 只认带 DispatcherQueue 的 UI 线程。
/// <see cref="TrySetTextAsync"/> / <see cref="TrySetBitmapAsync"/> 从后台线程调用时会自动
/// 送回 UI 线程再写；只有同步版 <see cref="TrySetText"/> 无处可等 —— 它遇到
/// RPC_E_WRONG_THREAD 会立即返回失败并留痕，不重试。</para>
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
    /// <summary>拒绝访问（策略/会话隔离）。确定性失败，重试无用。</summary>
    internal const int HResultAccessDenied = unchecked((int)0x80070005);
    /// <summary>无法写入剪贴板（CLIPBRD_E_CANT_SET）：同样是"剪贴板被别人拿着"，重试可解。</summary>
    internal const int HResultCantSetClipboard = unchecked((int)0x800401D1);
    /// <summary>调用了错误的线程（后台线程调用 WinRT 剪贴板 API）。重试永远是同样的结果。</summary>
    internal const int HResultWrongThread = unchecked((int)0x8001010E);
    /// <summary>COM 调用被对方拒绝（RPC_E_CALL_REJECTED）：对方忙，稍后重试可解。</summary>
    internal const int HResultCallRejected = unchecked((int)0x80010001);
    /// <summary>COM 服务器要求稍后重试（RPC_E_SERVERCALL_RETRYLATER）。</summary>
    internal const int HResultServerCallRetryLater = unchecked((int)0x8001010A);
    /// <summary>对象已释放（COR_E_OBJECTDISPOSED）：确定性失败（典型原因：复用了已被消费的流）。</summary>
    internal const int HResultObjectDisposed = unchecked((int)0x80131622);
    /// <summary>无效操作（COR_E_INVALIDOPERATION）：确定性失败。</summary>
    internal const int HResultInvalidOperation = unchecked((int)0x80131509);
    /// <summary>未指定的失败（E_FAIL）：确定性失败，重试换不来不同结果。</summary>
    internal const int HResultGenericFailure = unchecked((int)0x80004005);

    /// <summary>重试间隔（首次失败后依次等待）：总阻塞上限 460ms。</summary>
    internal static readonly int[] RetryDelaysMs = [40, 120, 300];

    /// <summary>
    /// 把文本写入剪贴板；失败时重试若干次后返回结果，**从不抛异常**。
    /// 必须从 UI 线程调用（后台线程请用 <see cref="TrySetTextAsync"/>）。
    /// </summary>
    /// <param name="text">要复制的文本；空串直接返回成功（不触碰剪贴板）。</param>
    /// <param name="flush">成功后是否调用 <c>Clipboard.Flush()</c>（应用退出后内容仍可粘贴）。</param>
    public static ClipboardCopyResult TrySetText(string? text, bool flush = false)
    {
        if (string.IsNullOrEmpty(text)) return new ClipboardCopyResult(true, 0, null);

        return RetryCore(
            attempt: _ => WriteText(text!, flush),
            delaysMs: RetryDelaysMs,
            sleepMs: Thread.Sleep);
    }

    /// <summary>
    /// <see cref="TrySetText"/> 的异步版本（重试语义完全一致，仅不阻塞调用线程）。
    /// 可以从任意线程调用：不在 UI 线程时会自动送回 UI 线程。
    /// </summary>
    public static Task<ClipboardCopyResult> TrySetTextAsync(string? text, bool flush = false)
    {
        if (string.IsNullOrEmpty(text)) return Task.FromResult(new ClipboardCopyResult(true, 0, null));

        return RunOnUiThreadAsync(() => RetryCoreAsync(
            attempt: _ => Task.FromResult(WriteText(text!, flush)),
            delaysMs: RetryDelaysMs,
            delayAsync: static ms => Task.Delay(ms)));
    }

    /// <summary>一次文本写入尝试（每次重建 DataPackage：上一次的包可能仍被剪贴板持有）。</summary>
    private static ClipboardCopyResult WriteText(string text, bool flush)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
        FlushQuietly(flush);
        return new ClipboardCopyResult(true, 0, null);
    }

    /// <summary>
    /// 把位图写入剪贴板（硬件信息页「截图」按钮）。引用必须每次尝试重建：
    /// 上一次的 DataPackage 可能仍被剪贴板持有，复用一个已消费的流会失败。
    /// 只有异步版本 —— 装填流的写入本身就要 await（见 <see cref="CreateBitmapReferenceAsync"/>）。
    /// 可以从任意线程调用：不在 UI 线程时会自动送回 UI 线程。
    /// </summary>
    /// <param name="referenceFactory">参数为尝试序号（从 0 开始），每次尝试都会调用一次。</param>
    /// <param name="flush">成功后是否调用 <c>Clipboard.Flush()</c>（应用退出后内容仍可粘贴）。</param>
    public static Task<ClipboardCopyResult> TrySetBitmapAsync(
        Func<int, Task<RandomAccessStreamReference>> referenceFactory,
        bool flush = false)
    {
        ArgumentNullException.ThrowIfNull(referenceFactory);

        return RunOnUiThreadAsync(() => RetryCoreAsync(
            attempt: async attemptIndex =>
            {
                var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                package.SetBitmap(await referenceFactory(attemptIndex).ConfigureAwait(true));
                Clipboard.SetContent(package);
                FlushQuietly(flush);
                return new ClipboardCopyResult(true, 0, null);
            },
            delaysMs: RetryDelaysMs,
            delayAsync: static ms => Task.Delay(ms)));
    }

    /// <summary>
    /// <c>Clipboard.Flush()</c> 失败**不能算复制失败**：走到这一步内容已经在剪贴板上，
    /// 只是"进程退出后还能不能粘贴"这一项没落实。曾经把它当整体失败重试并报错，
    /// 用户看到"复制失败"而实际粘贴是好的。
    /// </summary>
    private static void FlushQuietly(bool flush)
    {
        if (!flush) return;

        try
        {
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            try
            {
                LogService.Debug(LogService.Clipboard,
                    $"剪贴板 Flush 失败（内容已写入，仅退出后可能失效）：{ex.GetType().Name} 0x{ex.HResult:X8}");
            }
            catch { }
        }
    }

    /// <summary>
    /// 把一段 WinRT 剪贴板访问送到有 DispatcherQueue 的线程上执行。
    /// <paramref name="work"/> 内部只允许出现 WinRT 剪贴板调用，异常全部由它自己收口。
    /// </summary>
    private static Task<ClipboardCopyResult> RunOnUiThreadAsync(Func<Task<ClipboardCopyResult>> work)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
        if (dispatcher is null || dispatcher.HasThreadAccess) return work();

        var completion = new TaskCompletionSource<ClipboardCopyResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!dispatcher.TryEnqueue(async void () =>
            {
                try
                {
                    completion.TrySetResult(await work().ConfigureAwait(true));
                }
                catch (Exception ex)
                {
                    completion.TrySetResult(new ClipboardCopyResult(false, ex.HResult, ex));
                }
            }))
        {
            completion.TrySetResult(new ClipboardCopyResult(false, HResultWrongThread, null));
        }

        return completion.Task;
    }

    /// <summary>
    /// 把编码后的图片字节（截图当前为 PNG）包成剪贴板位图引用。
    ///
    /// 直接用流的 WriteAsync 写入，而不是 DataWriter：DataWriter 在 Dispose 时会关闭
    /// 底层流（除非先 DetachStream），且 WriteBytes 只写内部缓冲（要 StoreAsync 才落流）。
    /// 两条都踩过的后果是每次复制都抛 ObjectDisposedException（0x80000013），
    /// 界面却提示「剪贴板被其他程序占用」。
    /// </summary>
    internal static async Task<RandomAccessStreamReference> CreateBitmapReferenceAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);
        return RandomAccessStreamReference.CreateFromStream(stream);
    }

    /// <summary>
    /// 重试编排核心（与 WinRT 完全解耦，可单测）：尝试一次，失败后按
    /// <paramref name="delaysMs"/> 依次等待并重试；不可重试的失败立即返回。
    /// <paramref name="attempt"/> 的参数为尝试序号（首次为 0），便于每次尝试重建载荷。
    /// </summary>
    /// <param name="reportFailure">
    /// 失败上报钩子；<c>null</c> 表示走 <see cref="ReportFailure"/>（写日志 + 遥测）。
    /// 单元测试必须传空实现：这些用例故意制造失败，真上报会把假故障写进用户的
    /// clipboard.log，并污染遥测。
    /// </param>
    internal static ClipboardCopyResult RetryCore(
        Func<int, ClipboardCopyResult> attempt,
        int[] delaysMs,
        Action<int>? sleepMs,
        Action<ClipboardCopyResult>? reportFailure = null)
    {
        var result = RunAttempt(attempt, 0);
        var attempts = 1;

        for (var i = 0; !result.Success && IsRetryable(result.HResult) && i < delaysMs.Length; i++)
        {
            sleepMs?.Invoke(delaysMs[i]);
            result = RunAttempt(attempt, attempts);
            attempts++;
        }

        result = result with { Attempts = attempts };
        if (!result.Success) (reportFailure ?? ReportFailure)(result);
        return result;
    }

    /// <summary><see cref="RetryCore"/> 的异步版本（尝试与等待都不阻塞调用线程）。</summary>
    internal static async Task<ClipboardCopyResult> RetryCoreAsync(
        Func<int, Task<ClipboardCopyResult>> attempt,
        int[] delaysMs,
        Func<int, Task> delayAsync,
        Action<ClipboardCopyResult>? reportFailure = null)
    {
        var result = await RunAttemptAsync(attempt, 0).ConfigureAwait(true);
        var attempts = 1;

        for (var i = 0; !result.Success && IsRetryable(result.HResult) && i < delaysMs.Length; i++)
        {
            await delayAsync(delaysMs[i]).ConfigureAwait(true);
            result = await RunAttemptAsync(attempt, attempts).ConfigureAwait(true);
            attempts++;
        }

        result = result with { Attempts = attempts };
        if (!result.Success) (reportFailure ?? ReportFailure)(result);
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

    /// <summary><see cref="RunAttempt"/> 的异步版本；异常同样收成失败结果。</summary>
    private static async Task<ClipboardCopyResult> RunAttemptAsync(
        Func<int, Task<ClipboardCopyResult>> attempt,
        int attemptIndex)
    {
        try
        {
            return await attempt(attemptIndex).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            return new ClipboardCopyResult(false, ex.HResult, ex);
        }
    }

    /// <summary>
    /// 是否值得重试。只有「等一下就会好」的瞬时竞争算数：
    /// 剪贴板被别人打开（CANT_OPEN / CANT_SET）与 COM 对方忙
    /// （CALL_REJECTED / SERVERCALL_RETRYLATER）。
    /// 访问拒绝、跨线程调用、对象已释放、E_FAIL、无效操作都是确定性失败 ——
    /// 重试只会白拖 460ms，还让日志里看不出真正的病因。
    /// </summary>
    private static bool IsRetryable(int hresult) =>
        hresult is HResultClipboardCantOpen
            or HResultCantSetClipboard
            or HResultCallRejected
            or HResultServerCallRetryLater;

    /// <summary>失败落盘一行日志 + 遥测上报；诊断本身绝不能成为新的崩溃源。</summary>
    private static void ReportFailure(ClipboardCopyResult result)
    {
        var ex = result.Error ?? new COMException($"剪贴板写入失败（HRESULT {result.HResultHex}）", result.HResult);

        try
        {
            var reason = IsRetryable(result.HResult) ? "重试已用尽" : "确定性失败，不重试";
            WriteLogLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 剪贴板写入失败 {result.HResultHex} " +
                         $"（{reason}，共尝试 {result.Attempts} 次）{ex.GetType().Name}: {ex.Message}");
        }
        catch { }

        try
        {
            TelemetryService.TrackException(ex, "Clipboard");
        }
        catch { }
    }

    private static void WriteLogLine(string line)
        => LogService.WriteBlock(LogService.Clipboard, line + Environment.NewLine);
}
