using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 单实例激活转发的纯逻辑测试：启动意图解析（首次启动与「重复启动转发」共用）与接管开关识别。
/// 互斥体 / FileSystemWatcher 的进程行为不做单测，靠手工验证（见计划文档）。
/// </summary>
public class SingleInstanceTests
{
    [Fact]
    public void Parse_Normal_WhenNoNavigationArgs()
    {
        var intent = LaunchIntent.Parse(new[] { "TubaWinUi3.exe" });
        Assert.Equal(LaunchIntentKind.Normal, intent.Kind);
        Assert.Null(intent.Arg);
    }

    [Fact]
    public void Parse_OpenBuiltin_TakesFollowingToken()
    {
        var intent = LaunchIntent.Parse(new[] { "app.exe", "--open-builtin", "screen-test" });
        Assert.Equal(LaunchIntentKind.OpenBuiltin, intent.Kind);
        Assert.Equal("screen-test", intent.Arg);
    }

    [Fact]
    public void Parse_FileLock_KeepsPathWithSpaces()
    {
        var intent = LaunchIntent.Parse(new[] { "app.exe", "--file-lock", @"C:\Users\me\my file.txt" });
        Assert.Equal(LaunchIntentKind.FileLock, intent.Kind);
        Assert.Equal(@"C:\Users\me\my file.txt", intent.Arg);
    }

    [Fact]
    public void Parse_FileLock_StripsSurroundingQuotes()
    {
        var intent = LaunchIntent.Parse(new[] { "app.exe", "--file-lock", "\"C:\\a b\\c.txt\"" });
        Assert.Equal(@"C:\a b\c.txt", intent.Arg);
    }

    [Fact]
    public void Parse_ActiveIntercept()
    {
        var intent = LaunchIntent.Parse(new[] { "app.exe", "--show-active-intercept" });
        Assert.Equal(LaunchIntentKind.ActiveIntercept, intent.Kind);
    }

    [Fact]
    public void Parse_GameOverlayAuto()
    {
        var intent = LaunchIntent.Parse(new[] { "app.exe", "--game-overlay-auto" });
        Assert.Equal(LaunchIntentKind.GameOverlayAuto, intent.Kind);
    }

    [Fact]
    public void Parse_FileLockWinsOverOpenBuiltin()
    {
        // 与 App.OnLaunchedCore 的既有优先级一致：--file-lock 更具体，优先。
        var intent = LaunchIntent.Parse(new[] { "app.exe", "--open-builtin", "screen-test", "--file-lock", "x.txt" });
        Assert.Equal(LaunchIntentKind.FileLock, intent.Kind);
        Assert.Equal("x.txt", intent.Arg);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        var intent = LaunchIntent.Parse(new[] { "app.exe", "--OPEN-BUILTIN", "abc" });
        Assert.Equal(LaunchIntentKind.OpenBuiltin, intent.Kind);
        Assert.Equal("abc", intent.Arg);
    }

    [Fact]
    public void Parse_IgnoresUnrelatedArgs()
    {
        var intent = LaunchIntent.Parse(new[] { "app.exe", "--takeover", "--msix-admin-session", "-ToastActivated" });
        Assert.Equal(LaunchIntentKind.Normal, intent.Kind);
    }

    [Fact]
    public void Parse_OpenBuiltin_WithoutValue_FallsBackToNormal()
    {
        // 缺值不算数（不会导航到空 id）。
        var intent = LaunchIntent.Parse(new[] { "app.exe", "--open-builtin" });
        Assert.Equal(LaunchIntentKind.Normal, intent.Kind);
    }

    [Theory]
    [InlineData(new[] { "app.exe", "--takeover" }, true)]
    [InlineData(new[] { "app.exe", "--TAKEOVER" }, true)]
    [InlineData(new[] { "app.exe" }, false)]
    [InlineData(new[] { "app.exe", "--open-builtin", "x" }, false)]
    public void HasTakeover_DetectsFlag(string[] args, bool expected) =>
        Assert.Equal(expected, LaunchIntent.HasTakeover(args));

    [Fact]
    public void TakeoverArg_HasExpectedValue() =>
        Assert.Equal("--takeover", LaunchIntent.TakeoverArg);

    [Fact]
    public void MutexName_UnchangedForBackCompat() =>
        Assert.Equal("TubaWinUi3.MainInstance", SingleInstanceService.MutexName);
}
