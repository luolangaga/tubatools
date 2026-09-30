using System.Runtime.CompilerServices;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Tests
{
    /// <summary>
    /// AI 助手面板（FieldCure ChatPanel）销毁竞态异常的识别（Issue #194）。
    /// 认错了会吞掉工具箱自己的真问题，认漏了用户又会反复看到"未处理异常"错误窗口。
    /// </summary>
    public class ChatPanelCrashFilterTests
    {
        [Fact]
        public void IsTeardownRace_AcceptsChatPanelClosedWebViewException()
            => Assert.True(ChatPanelCrashFilter.IsTeardownRace(
                Capture(() => FieldCure.AssistStudio.Fakes.ChatPanelRenderer.ThrowClosedWebView())));

        [Fact]
        public void IsTeardownRace_RejectsSameMessageThrownByOwnCode()
            => Assert.False(ChatPanelCrashFilter.IsTeardownRace(Capture(ThrowClosedWebViewLocally)));

        [Fact]
        public void IsTeardownRace_RejectsOtherChatPanelFailures()
            => Assert.False(ChatPanelCrashFilter.IsTeardownRace(
                Capture(() => FieldCure.AssistStudio.Fakes.ChatPanelRenderer.ThrowSomethingElse())));

        [Fact]
        public void IsTeardownRace_RejectsMissingException()
            => Assert.False(ChatPanelCrashFilter.IsTeardownRace(null));

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowClosedWebViewLocally()
            => throw new InvalidOperationException(
                "在意外的时间调用了方法。执行脚本失败：CoreWebView2 is not present.");

        /// <summary>
        /// 两个未处理异常入口都必须过销毁竞态过滤：WinUI 的 Application.UnhandledException
        /// 与 AppDomain 的 UnhandledException。漏掉后者，正是用户报告（1.6.4）里那条
        /// ChatPanel 异常明明能被 IsTeardownRace 认出、却依然弹出错误窗口的原因 ——
        /// 过滤逻辑本身没写错，是接线只接了一半。这条断言把两个入口焊在一起。
        /// </summary>
        [Fact]
        public void BothUnhandledExceptionEntryPointsHonourTheTeardownRaceFilter()
        {
            var source = File.ReadAllText(
                Path.Combine(FindRepoRoot(), "TubaWinUi3.WinUI3", "App.xaml.cs"));

            foreach (var method in new[] { "OnWinUIUnhandledException", "OnUnhandledException" })
            {
                var body = ExtractMethodBody(source, method);
                Assert.Contains("IsIgnorableAiPanelTeardownRace", body, StringComparison.Ordinal);
            }
        }

        private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
        {
            foreach (var start in new[] { Path.GetDirectoryName(callerFilePath), AppContext.BaseDirectory })
            {
                if (string.IsNullOrEmpty(start)) continue;

                var dir = new DirectoryInfo(start);
                while (dir is not null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "TubaWinUi3.sln")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }

            throw new InvalidOperationException("未找到仓库根目录（TubaWinUi3.sln）");
        }

        /// <summary>按大括号配对截取方法体；方法改名/删除会直接断言失败，不会静默通过。</summary>
        private static string ExtractMethodBody(string source, string methodName)
        {
            var start = source.IndexOf("void " + methodName + "(", StringComparison.Ordinal);
            Assert.True(start >= 0, $"App.xaml.cs 里找不到 {methodName}（被改名或删除了？）");

            var open = source.IndexOf('{', start);
            Assert.True(open >= 0, $"{methodName} 没有方法体");

            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
            }

            throw new InvalidOperationException($"{methodName} 的大括号不配对");
        }

        private static Exception Capture(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                return ex;
            }
            throw new InvalidOperationException("预期被测委托抛出异常，但没有");
        }
    }
}

namespace FieldCure.AssistStudio.Fakes
{
    /// <summary>冒充组件库的抛出点，让异常栈里带上 FieldCure.AssistStudio 命名空间。</summary>
    internal static class ChatPanelRenderer
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowClosedWebView()
            => throw new InvalidOperationException(
                "在意外的时间调用了方法。\r\nExecuteScriptAsync(): Failed because a valid CoreWebView2 is not present. " +
                "Make sure one was created, for example by calling EnsureCoreWebView2Async() API.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowSomethingElse()
            => throw new InvalidOperationException("在意外的时间调用了方法。");
    }
}
