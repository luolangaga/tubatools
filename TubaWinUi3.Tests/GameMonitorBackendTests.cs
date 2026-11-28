using System.Text.Json;
using TubaWinUi3.Services.ActiveIntercept.Runtime;
using TubaWinUi3.Services.ActiveIntercept.Runtime.GameMonitor;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 后端「游戏后台自动监控」与功能隔离相关测试。
/// </summary>
public class GameMonitorBackendTests
{
    // ================= 配置双开关（功能隔离的契约） =================

    [Fact]
    public void BackendConfig_Defaults_KeepInterceptCompatible()
    {
        // 旧版配置文件没有这两个字段 → 反序列化后必须保持拦截器原行为：
        // 拦截=开、游戏监控=关。否则老配置会让后端静默失去拦截功能。
        var config = JsonSerializer.Deserialize("{}", BackEndJsonContext.Default.BackendConfig)
                     ?? throw new InvalidOperationException("反序列化失败");
        Assert.True(config.EnableIntercept);
        Assert.False(config.EnableGameMonitor);
    }

    [Fact]
    public void BackendConfig_FeatureFlags_RoundTrip()
    {
        var json = JsonSerializer.Serialize(new BackendConfig
        {
            EnableIntercept = false,
            EnableGameMonitor = true,
            DataDir = "C:\\data",
        });
        var config = JsonSerializer.Deserialize(json, BackEndJsonContext.Default.BackendConfig)!;
        Assert.False(config.EnableIntercept);
        Assert.True(config.EnableGameMonitor);
        Assert.Equal("C:\\data", config.DataDir);
    }

    // ================= 全屏判定（纯几何，无窗口依赖） =================

    [Fact]
    public void FullScreen_FullMonitorRect_IsDetected()
    {
        // 2560x1440 显示器，无边框游戏窗口铺满全屏
        var window = new WinApi.RECT { Left = 0, Top = 0, Right = 2560, Bottom = 1440 };
        var monitor = new WinApi.RECT { Left = 0, Top = 0, Right = 2560, Bottom = 1440 };
        var work = new WinApi.RECT { Left = 0, Top = 0, Right = 2560, Bottom = 1408 };

        Assert.True(GameMonitorService.IsForegroundFullScreenCore(window, monitor, work, out var mc, out var wc));
        Assert.True(mc >= 0.93);
    }

    [Fact]
    public void FullScreen_BorderlessOverWorkArea_IsDetected()
    {
        // 任务栏置顶时，无边框窗口往往只铺满工作区 —— 靠 97% 工作区判据命中
        var window = new WinApi.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        var monitor = new WinApi.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var work = new WinApi.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };

        Assert.True(GameMonitorService.IsForegroundFullScreenCore(window, monitor, work, out _, out var wc));
        Assert.True(wc >= 0.97);
    }

    [Fact]
    public void FullScreen_NormalWindow_IsRejected()
    {
        // 800x600 窗口：既不满屏也不满工作区，绝不能触发后台监控
        var window = new WinApi.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 };
        var monitor = new WinApi.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var work = new WinApi.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };

        Assert.False(GameMonitorService.IsForegroundFullScreenCore(window, monitor, work, out _, out _));
    }

    // ================= 进程排除名单 =================

    [Theory]
    [InlineData("LockApp")]        // 合盖 / Win+L 锁屏：无边框全屏，最容易被当成游戏
    [InlineData("ClickToDo")]      // Win11 Click to Do：无边框满屏 AI 浮层，2026-09-19 实测中招
    [InlineData("LogonUI")]        // 登录界面
    [InlineData("Windows.UI.Logon")]
    [InlineData("explorer")]
    [InlineData("msedge")]
    [InlineData("TubaWinUi3")]
    public void ProcessFilter_ExcludesSystemShellSurfaces(string process)
    {
        Assert.True(GameProcessFilter.IsExcluded(process));
    }

    [Theory]
    [InlineData("LockApp.exe")] // 名单匹配的是进程名（不带扩展名），带扩展名不命中 —— 由调用方保证
    [InlineData("notagame")]
    public void ProcessFilter_UnknownProcessIsNotExcluded(string process)
    {
        Assert.False(GameProcessFilter.IsExcluded(process));
    }

    // ================= 共享 FpsTracker / present 判据（两端同源） =================

    [Fact]
    public void FpsPresentEvents_IdenticalToMainAppFamily()
    {
        // 共享判据必须与主程序 FpsService 的家族完全一致（两者已同一份源文件，
        // 这里做契约断言防将来有人复制一份后分叉）
        Assert.True(FpsPresentEvents.IsPresentEventId(0x00B8)); // Present
        Assert.True(FpsPresentEvents.IsPresentEventId(0x00AB)); // PresentHistory
        Assert.True(FpsPresentEvents.IsPresentEventId(0x00D7)); // PresentHistoryDetailed
        Assert.True(FpsPresentEvents.IsPresentEventId(0x00C9)); // Win32k composed
        Assert.True(FpsPresentEvents.IsPresentEventId(0x010A)); // IndependentFlip
        Assert.False(FpsPresentEvents.IsPresentEventId(0x0010)); // 随机非帧事件
        Assert.Equal(FpsService.IsPresentEventId(0x00C9), FpsPresentEvents.IsPresentEventId(0x00C9));
    }

    [Fact]
    public void FpsPresentEvents_DedupesSameFrame()
    {
        // 同一帧的多个事件（PresentHistory + MMIOFlip）只计一次 —— 1ms 去重窗口
        var tracker = new FpsTracker();
        long t0 = 10_000_000; // 1s（ticks）
        Assert.True(FpsPresentEvents.TryRecordPresent(tracker, 0x00AB, t0));
        Assert.False(FpsPresentEvents.TryRecordPresent(tracker, 0x00A8, t0 + TimeSpan.TicksPerMillisecond / 2));
        // 超出去重窗口的真实下一帧要计入
        Assert.True(FpsPresentEvents.TryRecordPresent(tracker, 0x00AB, t0 + TimeSpan.TicksPerMillisecond * 8));
    }
}
