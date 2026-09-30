using System.Runtime.InteropServices;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests
{
    /// <summary>
    /// 剪贴板写入的重试编排（ClipboardService）。
    ///
    /// 背景：<c>Clipboard.SetContent</c> 在剪贴板被其他进程占用时抛 COMException
    /// （0x800401D0 / CLIPBRD_E_CANT_OPEN），从 Tapped 处理器里逃出去就是闪退
    /// （硬件信息页点「运行时间」卡片必崩）。这里锁定三个不能退化的语义：
    /// ① 瞬时占用要重试；② 稳定失败要立即返回（不白等）；③ 任何异常都变成失败结果，绝不外抛。
    ///
    /// 只测与 WinRT 解耦的 RetryCore —— 真实的 Clipboard.SetContent 需要 UI 线程，
    /// 无法在单测里调用。
    /// </summary>
    public class ClipboardServiceRetryTests
    {
        private const int CantOpen = ClipboardService.HResultClipboardCantOpen;
        private const int AccessDenied = ClipboardService.HResultAccessDenied;
        private const int WrongThread = ClipboardService.HResultWrongThread;

        private static readonly int[] NoDelays = [];

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
                sleepMs: slept.Add);

            Assert.True(result.Success);
            Assert.Equal(2, attempts);
            Assert.Equal(new[] { 0, 1 }, seenIndices);
            Assert.Equal(new[] { 40 }, slept);
        }

        [Fact]
        public void RetryCore_AccessDenied_FailsFastWithoutRetry()
        {
            var attempts = 0;
            var slept = new List<int>();

            var result = ClipboardService.RetryCore(
                attempt: _ => { attempts++; return Fail(AccessDenied); },
                delaysMs: [40, 120, 300],
                sleepMs: slept.Add);

            Assert.False(result.Success);
            Assert.Equal(1, attempts);
            Assert.Empty(slept);
        }

        [Fact]
        public void RetryCore_WrongThread_FailsFastWithoutRetry()
        {
            // 后台线程调用剪贴板 API —— 重试永远是同样的结果，必须立即返回
            var attempts = 0;
            var slept = new List<int>();

            var result = ClipboardService.RetryCore(
                attempt: _ => { attempts++; return Fail(WrongThread); },
                delaysMs: [40, 120, 300],
                sleepMs: slept.Add);

            Assert.False(result.Success);
            Assert.Equal(1, attempts);
            Assert.Empty(slept);
        }

        [Fact]
        public void RetryCore_UnknownFailure_RetriesEveryDelay()
        {
            var attempts = 0;
            var slept = new List<int>();

            var result = ClipboardService.RetryCore(
                attempt: _ => { attempts++; return Fail(unchecked((int)0x80004005)); },
                delaysMs: [40, 120, 300],
                sleepMs: slept.Add);

            Assert.False(result.Success);
            Assert.Equal(4, attempts); // 首次 + 3 次重试
            Assert.Equal(new[] { 40, 120, 300 }, slept);
        }

        [Fact]
        public void RetryCore_AlwaysFails_ReturnsLastErrorWithHexHResult()
        {
            var result = ClipboardService.RetryCore(
                attempt: _ => Fail(CantOpen),
                delaysMs: [40],
                sleepMs: _ => { });

            Assert.False(result.Success);
            Assert.Equal(CantOpen, result.HResult);
            Assert.Equal("0x800401D0", result.HResultHex);
            Assert.IsType<COMException>(result.Error);
        }

        [Fact]
        public void RetryCore_SuccessOnFirstTry_DoesNotSleep()
        {
            var slept = new List<int>();

            var result = ClipboardService.RetryCore(
                attempt: _ => Ok(),
                delaysMs: [40, 120, 300],
                sleepMs: slept.Add);

            Assert.True(result.Success);
            Assert.Empty(slept);
        }

        [Fact]
        public void RetryCore_SwallowsUnexpectedExceptionsInsteadOfThrowing()
        {
            // 这是本类存在的意义：任何异常都不能从 Tapped/Click 处理器里逃出去
            var result = ClipboardService.RetryCore(
                attempt: _ => throw new InvalidOperationException("交付期间炸了"),
                delaysMs: NoDelays,
                sleepMs: _ => { });

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
                sleepMs: _ => { });

            Assert.False(result.Success);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task RetryCoreAsync_TransientFailure_RetriesUntilSuccess()
        {
            var attempts = 0;
            var delays = new List<int>();

            var result = await ClipboardService.RetryCoreAsync(
                attempt: _ => ++attempts == 1 ? Fail(CantOpen) : Ok(),
                delaysMs: [10, 20],
                delayAsync: ms => { delays.Add(ms); return Task.CompletedTask; });

            Assert.True(result.Success);
            Assert.Equal(2, attempts);
            Assert.Equal(new[] { 10 }, delays);
        }

        [Fact]
        public async Task RetryCoreAsync_WrongThread_FailsFastWithoutDelay()
        {
            var attempts = 0;
            var delays = new List<int>();

            var result = await ClipboardService.RetryCoreAsync(
                attempt: _ => { attempts++; return Fail(WrongThread); },
                delaysMs: [10, 20],
                delayAsync: ms => { delays.Add(ms); return Task.CompletedTask; });

            Assert.False(result.Success);
            Assert.Equal(1, attempts);
            Assert.Empty(delays);
        }
    }
}
