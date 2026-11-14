using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// AI 助手首次上线引导的展示判定：只有「没看过 + 没主动进过页面 + 有网」才弹。
/// 真实弹层与导航定位不做单测（需要窗口与 XamlRoot）。
/// </summary>
public class AiAssistantIntroTests
{
    [Fact]
    public void ShouldShow_FirstLaunchWithNetwork()
    {
        Assert.True(AiAssistantIntroService.ShouldShow(seen: false, visited: false, networkAvailable: true));
    }

    [Fact]
    public void ShouldShow_FalseWhenAlreadySeen()
    {
        Assert.False(AiAssistantIntroService.ShouldShow(seen: true, visited: false, networkAvailable: true));
    }

    [Fact]
    public void ShouldShow_FalseWhenOffline()
    {
        // 离线时不弹也不记账，下次联网启动还有机会看到
        Assert.False(AiAssistantIntroService.ShouldShow(seen: false, visited: false, networkAvailable: false));
    }

    [Fact]
    public void ShouldShow_FalseWhenAssistantAlreadyVisited()
    {
        Assert.False(AiAssistantIntroService.ShouldShow(seen: false, visited: true, networkAvailable: true));
    }
}
