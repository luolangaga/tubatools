using System.Runtime.InteropServices;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests
{
    /// <summary>
    /// 剪贴板写入的重试编排（ClipboardService）。
    ///
    /// 背景：<c>Clipboard.SetContent</c> 在剪贴板被其他进程占用时抛 COMException
    /// （0x800401D0 / CLIPBRD_E_CANT_OPEN），从 Tapped 处理器里逃出去就是闪退
    /// （硬件信息页点「运行时间」卡片必崩）。这里锁定四个不能退化的语义：
    /// ① 瞬时占用要重试；② 确定性失败要立即返回（不白等）；③ 任何异常都变成失败结果，绝不外抛；
    /// ④ 失败上报必须由调用方显式注入 —— 否则这些故意制造的失败会被写进用户的
    ///    clipboard.log 并上报遥测（曾经每次 dotnet test 都留下 8 行假故障，一个多月攒到 94 KB）。
    ///
    /// 只测与 WinRT 解耦的 RetryCore —— 真实的 Clipboard.SetContent 需要 UI 线程，
    /// 无法在单测里调用。
    /// </summary>
    public class ClipboardServiceRetryTests
    {
        private const int CantOpen = ClipboardService.HResultClipboardCantOpen;
        private const int CantSet = ClipboardService.HResultCantSetClipboard;
        private const int AccessDenied = ClipboardService.HResultAccessDenied;
        private const int WrongThread = ClipboardService.HResultWrongThread;
        private const int CallRejected = ClipboardService.HResultCallRejected;
        private const int ServerCallRetryLater = ClipboardService.HResultServerCallRetryLater;
        private const int GenericFailure = ClipboardService.HResultGenericFailure;
        private const int InvalidOperation = ClipboardService.HResultInvalidOperation;
        private const int ObjectDisposed = ClipboardService.HResultObjectDisposed;

        private static readonly int[] NoDelays = [];

        /// <summary>测试用上报钩子：所有故意失败的用例都必须传它（真实上报会写日志 + 遥测）。</summary>
        private static readonly Action<ClipboardCopyResult> NoReport = _ => { };

        private static ClipboardCopyResult Ok() => new(true, 0, null);

        private static ClipboardCopyResult Fail(int hresult) =>
            new(false, hresult, new COMException("剪贴板被占用", hresult));

        [Fact]
        public void RetryDelaysMs_IsAscendingShortBackoffWithinHalfSecond()
        {
            var delays = ClipboardService.RetryDelaysMs;

            Assert.NotEmpty(delays);
            Assert.All(delays, d => Assert.InRange(d, 1, 500));
            Assert.Equal(delays.OrderBy(d => d), delays);
            Assert.True(delays.Sum() <= 500, "总阻塞上限必须控制在半秒内，否则复制会明显卡顿");
        }

        [Fact]
        public void RetryCore_TransientFailure_RetriesAndReportsAttemptIndex()
        {
            var attempts = 0;
            var seenIndices = new List<int>();
            var slept = new List<int>();

            var result = ClipboardService.RetryCore(
                attempt: index =>
                {
                    seenIndices.Add(index);
                    return ++attempts == 1 ? Fail(CantOpen) : Ok();
                },
                delaysMs: [40, 120],
                sleepMs: slept.Add,
                reportFailure: NoReport);

            Assert.True(result.Success);
            Assert.Equal(2, attempts);
            Assert.Equal(new[] { 0, 1 }, seenIndices);
            Assert.Equal(new[] { 40 }, slept);
        }

        [Theory]
        [InlineData(CantOpen)]
        [InlineData(CantSet)]
        [InlineData(CallRejected)]
        [InlineData(ServerCallRetryLater)]
        public void RetryCore_TransientFailure_RetriesEveryDelay(int hresult)
        {
            // 这四类都是「等一下就会好」：剪贴板被别人打开、COM 对方忙
            var attempts = 0;
            var slept = new List<int>();

            var result = ClipboardService.RetryCore(
                attempt: _ => { attempts++; return Fail(hresult); },
                delaysMs: [40, 120, 300],
                sleepMs: slept.Add,
                reportFailure: NoReport);

            Assert.False(result.Success);
            Assert.Equal(4, attempts); // 首次 + 3 次重试
            Assert.Equal(new[] { 40, 120, 300 }, slept);
            Assert.Equal(4, result.Attempts);
        }

        [Theory]
        [InlineData(AccessDenied)]
        [InlineData(WrongThread)]
        [InlineData(GenericFailure)]
        [InlineData(InvalidOperation)]
        [InlineData(ObjectDisposed)]
        public void RetryCore_DeterministicFailure_FailsFastWithoutRetry(int hresult)
        {
            // 重试换不来不同结果，只会白拖 460ms 并掩盖真因
            var attempts = 0;
            var slept = new List<int>();

            var result = ClipboardService.RetryCore(
                attempt: _ => { attempts++; return Fail(hresult); },
                delaysMs: [40, 120, 300],
                sleepMs: slept.Add,
                reportFailure: NoReport);

            Assert.False(result.Success);
            Assert.Equal(1, attempts);
            Assert.Equal(1, result.Attempts);
            Assert.Empty(slept);
        }

        [Fact]
        public void RetryCore_AlwaysFails_ReturnsLastErrorWithHexHResult()
        {
            var result = ClipboardService.RetryCore(
                attempt: _ => Fail(CantOpen),
                delaysMs: [40],
                sleepMs: _ => { },
                reportFailure: NoReport);

            Assert.False(result.Success);
            Assert.Equal(CantOpen, result.HResult);
            Assert.Equal("0x800401D0", result.HResultHex);
            Assert.IsType<COMException>(result.Error);
        }

        [Fact]
        public void RetryCore_ReportsActualAttemptCount_NeverTheConfiguredMaximum()
        {
            // 日志曾经固定写「已重试 3 次」，而 4 类确定性失败其实一次都没重试 —— 谎报
            var result = ClipboardService.RetryCore(
                attempt: _ => Fail(CantOpen),
                delaysMs: [10, 20],
                sleepMs: _ => { },
                reportFailure: NoReport);

            Assert.Equal(3, result.Attempts);
        }

        [Fact]
        public void RetryCore_SuccessReportsSingleAttempt()
        {
            var result = ClipboardService.RetryCore(
                attempt: _ => Ok(),
                delaysMs: [40, 120, 300],
                sleepMs: _ => { },
                reportFailure: NoReport);

            Assert.True(result.Success);
            Assert.Equal(1, result.Attempts);
        }

        [Fact]
        public void RetryCore_SuccessOnFirstTry_DoesNotSleep()
        {
            var slept = new List<int>();

            var result = ClipboardService.RetryCore(
                attempt: _ => Ok(),
                delaysMs: [40, 120, 300],
                sleepMs: slept.Add,
                reportFailure: NoReport);

            Assert.True(result.Success);
            Assert.Empty(slept);
        }

        [Fact]
        public void RetryCore_FailureIsReportedOnceWithFinalResult()
        {
            var reported = new List<ClipboardCopyResult>();

            var result = ClipboardService.RetryCore(
                attempt: _ => Fail(CantOpen),
                delaysMs: [10, 20],
                sleepMs: _ => { },
                reportFailure: reported.Add);

            Assert.Single(reported);
            Assert.Equal(result, reported[0]);
        }

        [Fact]
        public void RetryCore_SuccessIsNotReportedAsFailure()
        {
            var reported = 0;

            var result = ClipboardService.RetryCore(
                attempt: _ => Ok(),
                delaysMs: NoDelays,
                sleepMs: _ => { },
                reportFailure: _ => reported++);

            Assert.True(result.Success);
            Assert.Equal(0, reported);
        }

        [Fact]
        public void RetryCore_SwallowsUnexpectedExceptionsInsteadOfThrowing()
        {
            // 这是本类存在的意义：任何异常都不能从 Tapped/Click 处理器里逃出去
            var result = ClipboardService.RetryCore(
                attempt: _ => throw new InvalidOperationException("交付期间炸了"),
                delaysMs: NoDelays,
                sleepMs: _ => { },
                reportFailure: NoReport);

            Assert.False(result.Success);
            Assert.IsType<InvalidOperationException>(result.Error);
            Assert.NotEqual(0, result.HResult); // .NET 异常也带 HRESULT，不为 0 即可（不锁具体值）
        }

        [Fact]
        public void RetryCore_GivesUpAfterDelaysAreExhausted()
        {
            var attempts = 0;

            var result = ClipboardService.RetryCore(
                attempt: _ => { attempts++; return Fail(CantOpen); },
                delaysMs: [10],
                sleepMs: _ => { },
                reportFailure: NoReport);

            Assert.False(result.Success);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task RetryCoreAsync_TransientFailure_RetriesUntilSuccess()
        {
            var attempts = 0;
            var delays = new List<int>();

            var result = await ClipboardService.RetryCoreAsync(
                attempt: _ => Task.FromResult(++attempts == 1 ? Fail(CantOpen) : Ok()),
                delaysMs: [10, 20],
                delayAsync: ms => { delays.Add(ms); return Task.CompletedTask; },
                reportFailure: NoReport);

            Assert.True(result.Success);
            Assert.Equal(2, attempts);
            Assert.Equal(new[] { 10 }, delays);
        }

        [Fact]
        public async Task RetryCoreAsync_WrongThread_FailsFastWithoutDelay()
        {
            // 后台线程调用剪贴板 API —— 重试永远是同样的结果，必须立即返回
            var attempts = 0;
            var delays = new List<int>();

            var result = await ClipboardService.RetryCoreAsync(
                attempt: _ => { attempts++; return Task.FromResult(Fail(WrongThread)); },
                delaysMs: [10, 20],
                delayAsync: ms => { delays.Add(ms); return Task.CompletedTask; },
                reportFailure: NoReport);

            Assert.False(result.Success);
            Assert.Equal(1, attempts);
            Assert.Empty(delays);
        }

        [Fact]
        public async Task RetryCoreAsync_ReportsActualAttemptCountOnFailure()
        {
            var reported = new List<ClipboardCopyResult>();

            var result = await ClipboardService.RetryCoreAsync(
                attempt: _ => Task.FromResult(Fail(CantOpen)),
                delaysMs: [10, 20],
                delayAsync: _ => Task.CompletedTask,
                reportFailure: reported.Add);

            Assert.Equal(3, result.Attempts);
            Assert.Single(reported);
            Assert.Equal(result, reported[0]);
        }

        [Fact]
        public async Task RetryCoreAsync_AsyncAttemptThrows_ReturnsFailureInsteadOfThrowing()
        {
            // 回归：装填位图流的异步尝试抛 ObjectDisposedException（0x80000013）时，
            // 必须收成失败结果（否则从 Click 处理器里逃出去就是闪退）
            var result = await ClipboardService.RetryCoreAsync(
                attempt: _ => throw new ObjectDisposedException("该对象已关闭"),
                delaysMs: NoDelays,
                delayAsync: _ => Task.CompletedTask,
                reportFailure: NoReport);

            Assert.False(result.Success);
            Assert.IsType<ObjectDisposedException>(result.Error);
        }
    }
}
