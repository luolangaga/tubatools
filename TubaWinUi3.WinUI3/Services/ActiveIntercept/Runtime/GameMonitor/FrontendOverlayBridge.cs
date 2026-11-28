using System.Text.Json;
using System.Text.Json.Serialization;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime.GameMonitor;

/// <summary>
/// 游戏检测 → 覆盖层信号桥（进程内版）：
/// 检测线程命中全屏游戏后把状态写入数据目录下的 game_overlay_signal.json，
/// 由同一进程内 <c>GameOverlayAutoService</c> 轮询并用现成的 GameOverlayWindow 显示悬浮窗。
///
/// 运行时已在主程序里，不再有「主程序没运行就把它拉起来」的分支 ——
/// 检测器本身随主程序启动，主程序不在运行时也不会有检测器。
/// 信号文件保留为唯一通信媒介（原子写 + Utc 时间戳），便于覆盖层服务独立启停。
/// </summary>
internal sealed class FrontendOverlayBridge
{
    private const string SignalFileName = "game_overlay_signal.json";

    private readonly string _signalPath;
    private bool _lastActive; // 状态机：避免每 tick 重复写文件
    private (IntPtr Hwnd, uint Pid, string Proc)? _lastGame; // 心跳复用的最近一次命中

    public FrontendOverlayBridge(string dataDir)
    {
        _signalPath = Path.Combine(dataDir, SignalFileName);
    }

    public static string SignalFilePathFor(string dataDir) => Path.Combine(dataDir, SignalFileName);

    /// <summary>检测到游戏（已确认）：写 active 信号。</summary>
    public void NotifyGame(IntPtr hwnd, uint pid, string proc)
    {
        _lastGame = (hwnd, pid, proc);
        WriteSignal(new OverlaySignal
        {
            Active = true,
            Process = proc,
            Pid = pid,
            Hwnd = hwnd.ToInt64(),
            Utc = DateTimeOffset.UtcNow.ToString("o"),
        });
    }

    /// <summary>
    /// 游戏持续在前台时的心跳：刷新信号时间戳（WriteSignal 内部有 6s 节流），
    /// 覆盖层据此判断检测线程还活着 —— 否则信号 60s 过期，前端会误关悬浮窗
    /// （2026-09-11 实测踩坑：NotifyGame 只在窗口切换时调用，游戏一直前台时心跳从未跑过）。
    /// </summary>
    public void KeepAlive()
    {
        if (_lastGame is not null)
        {
            var (hwnd, pid, proc) = _lastGame.Value;
            WriteSignal(new OverlaySignal
            {
                Active = true,
                Process = proc,
                Pid = pid,
                Hwnd = hwnd.ToInt64(),
                Utc = DateTimeOffset.UtcNow.ToString("o"),
            });
        }
    }

    /// <summary>游戏退出前台 / 服务停止：写 inactive 信号，前端收到后关闭覆盖层。</summary>
    public void NotifyExit()
    {
        WriteSignal(new OverlaySignal
        {
            Active = false,
            Process = "",
            Pid = 0,
            Hwnd = 0,
            Utc = DateTimeOffset.UtcNow.ToString("o"),
        });
    }

    private void WriteSignal(OverlaySignal signal)
    {
        bool active = signal.Active;
        if (active == _lastActive && active)
        {
            // 持续游戏中：仍需刷新时间戳（覆盖层用它判断检测线程是否还活着），
            // 但降低频率 —— hwnd 未变时每 3 个检测周期刷一次即可。
            if (Environment.TickCount64 - _lastWriteTick < 6000) return;
        }

        try
        {
            var json = JsonSerializer.Serialize(signal, JsonContext.Default.OverlaySignal);
            var tmp = _signalPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _signalPath, overwrite: true);
            _lastActive = active;
            _lastWriteTick = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"游戏监控：写覆盖层信号失败 {ex.Message}");
        }
    }

    private long _lastWriteTick;
}

/// <summary>游戏检测 → 覆盖层信号（game_overlay_signal.json 内容）。</summary>
public sealed class OverlaySignal
{
    /// <summary>true=前台确认是游戏（显示覆盖层）；false=已退出前台（关闭覆盖层）。</summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("process")]
    public string Process { get; set; } = "";

    [JsonPropertyName("pid")]
    public uint Pid { get; set; }

    /// <summary>游戏前台窗口句柄（HWND 跨进程有效，前端覆盖层直接绑定跟随）。</summary>
    [JsonPropertyName("hwnd")]
    public long Hwnd { get; set; }

    /// <summary>写入时间（UTC ISO8601）—— 前端判断信号是否过期（检测线程意外停止）。</summary>
    [JsonPropertyName("utc")]
    public string Utc { get; set; } = "";
}

[JsonSerializable(typeof(OverlaySignal))]
internal sealed partial class JsonContext : JsonSerializerContext;

