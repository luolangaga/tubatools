using Tubatools.AiXamlCard;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 启动阶段护栏：卡片会自动渲染，但渲染本身不允许产生外部副作用。
/// </summary>
public sealed class CardStartupGuardTests
{
    [Fact]
    public void RenderScope_IsScopedAndRestored()
    {
        Assert.False(CardStartupGuard.IsRendering);
        using (CardStartupGuard.EnterRender())
        {
            Assert.True(CardStartupGuard.IsRendering);
            using (CardStartupGuard.EnterRender()) Assert.True(CardStartupGuard.IsRendering);
            Assert.True(CardStartupGuard.IsRendering);
        }
        Assert.False(CardStartupGuard.IsRendering);
    }

    [Fact]
    public void RejectSideEffect_ExplainsTheRule()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CardStartupGuard.RejectSideEffect("发消息"));
        Assert.Contains("用户点击", error.Message);
        Assert.Contains("发消息", error.Message);
    }

    /// <summary>
    /// 基类的 Send 在渲染期必须失败——否则「自动渲染」就等于「自动替用户发起新一轮对话」。
    /// </summary>
    [Fact]
    public void Send_IsBlockedWhileRendering()
    {
        var controller = new TestController();
        var sent = 0;
        controller.Bind(text => sent++, (_, _) => { });

        using (CardStartupGuard.EnterRender())
            Assert.Throws<InvalidOperationException>(() => controller.SendNow("请继续"));
        Assert.Equal(0, sent);

        controller.SendNow("请继续");
        Assert.Equal(1, sent);
    }

    [Fact]
    public void Send_IgnoresBlankTextWithoutSideEffects()
    {
        var controller = new TestController();
        var sent = 0;
        controller.Bind(text => sent++, (_, _) => { });
        controller.SendNow("   ");
        Assert.Equal(0, sent);
    }

    private sealed class TestController : CardControllerBase
    {
        public void Bind(Action<string> send, Action<string, bool> notice)
        {
            var method = typeof(CardControllerBase).GetMethod("Attach",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            method.Invoke(this, [new Dictionary<string, Microsoft.UI.Xaml.FrameworkElement>(), send, notice]);
        }

        public void SendNow(string text) => Send(text);
    }
}
