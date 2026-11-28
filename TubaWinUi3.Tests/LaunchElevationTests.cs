using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// XAML 前置自动提权闸门（LaunchElevation）的纯逻辑测试：
/// 只有「要开主窗口」的普通启动才前置提权；无窗口辅助模式 / Toast 激活标记 / 未知参数必须放行，
/// 交给 App.OnLaunchedCore 的兜底判定，绝不能误弹 UAC；打包上下文 / 已管理员 / 截图旁路不触发。
/// runas / ShellExecute 的进程行为不做单测。
/// </summary>
public class LaunchElevationTests
{
    [Fact]
    public void PlainLaunch_NoArgs_IsWindowLaunch() =>
        Assert.True(LaunchElevation.IsPlainWindowLaunch(System.Array.Empty<string>()));

    [Theory]
    [InlineData("--open-builtin", "screen-test")]
    [InlineData("--file-lock", @"C:\Users\me\my file.txt")]
    [InlineData("--file-lock", "\"C:\\a b\\c.txt\"")]
    [InlineData("--show-active-intercept")]
    [InlineData("--autostart")]
    [InlineData("--hide-ui")]
    [InlineData("--autostart", "--hide-ui")]
    [InlineData("--safe-mode")]
    [InlineData("--OPEN-BUILTIN", "abc")]
    [InlineData("--open-builtin", "x", "--safe-mode")]
    public void WindowLaunchSwitches_AreAllowed(params string[] args) =>
        Assert.True(LaunchElevation.IsPlainWindowLaunch(args));

    [Theory]
    [InlineData("--file-lock")]
    [InlineData("--open-builtin")]
    public void ValueSwitch_WithoutValue_StillWindowLaunch(params string[] args)
    {
        // 缺值时 LaunchIntent.Parse 退化为普通启动（仍开主窗口），允许清单必须同口径。
        Assert.Equal(LaunchIntentKind.Normal, LaunchIntent.Parse(args).Kind);
        Assert.True(LaunchElevation.IsPlainWindowLaunch(args));
    }

    [Fact]
    public void ValueSwitch_ConsumesNextToken_LikeLaunchIntent()
    {
        // 与 LaunchIntent.FindValue 一致：紧随的 token 一律视为值，不再当作开关判定。
        var args = new[] { "--file-lock", "--toast" };
        Assert.Equal(LaunchIntentKind.FileLock, LaunchIntent.Parse(args).Kind);
        Assert.True(LaunchElevation.IsPlainWindowLaunch(args));
    }

    [Theory]
    [InlineData("--copy-path", @"C:\x.txt")]
    [InlineData("--toast", @"C:\data\notif.json")]
    [InlineData("--context-title-probe", "a", "b", "c", "--probe-out", "o.json")]
    [InlineData("--computer-use-mcp")]
    [InlineData("--browser-use-mcp")]
    [InlineData("--agent-cli", "help")]
    [InlineData("--win-benchmark-worker", "pipe", "123", "dark")]
    [InlineData("--ai-ep-probe", "a", "b", "c", "1", "2", "d")]
    [InlineData("--energystar-silent")]
    [InlineData("--phone-toast-handler", "action=phone-link", "target=x")]
    [InlineData("--msix-admin-session")]
    [InlineData("-ToastActivated")]
    [InlineData("--takeover")]
    [InlineData("--whatever")]
    [InlineData("--open-builtin", "x", "--toast", "y")]
    [InlineData("--safe-mode", "--unknown")]
    public void HeadlessOrUnknownArgs_FallThroughToLegacyPath(params string[] args) =>
        Assert.False(LaunchElevation.IsPlainWindowLaunch(args));

    [Fact]
    public void ShouldElevate_PlainNonAdminUnpackaged() =>
        Assert.True(LaunchElevation.ShouldElevateBeforeXaml(
            isPackagedContext: false, isAdmin: false, noElevateEnv: null, System.Array.Empty<string>()));

    [Fact]
    public void ShouldNotElevate_PackagedContext() =>
        Assert.False(LaunchElevation.ShouldElevateBeforeXaml(
            isPackagedContext: true, isAdmin: false, noElevateEnv: null, System.Array.Empty<string>()));

    [Fact]
    public void ShouldNotElevate_AlreadyAdmin() =>
        Assert.False(LaunchElevation.ShouldElevateBeforeXaml(
            isPackagedContext: false, isAdmin: true, noElevateEnv: null, System.Array.Empty<string>()));

    [Fact]
    public void ShouldNotElevate_ScreenshotBypass() =>
        Assert.False(LaunchElevation.ShouldElevateBeforeXaml(
            isPackagedContext: false, isAdmin: false, noElevateEnv: "1", System.Array.Empty<string>()));

    [Fact]
    public void ShouldElevate_OtherEnvValueDoesNotBypass() =>
        Assert.True(LaunchElevation.ShouldElevateBeforeXaml(
            isPackagedContext: false, isAdmin: false, noElevateEnv: "0", System.Array.Empty<string>()));

    [Fact]
    public void ShouldNotElevate_HeadlessInvocation() =>
        Assert.False(LaunchElevation.ShouldElevateBeforeXaml(
            isPackagedContext: false, isAdmin: false, noElevateEnv: null, new[] { "--copy-path", @"C:\x.txt" }));
}