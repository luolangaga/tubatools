namespace TubaWinUi3.Services;

/// <summary>
/// 联机日志（统一写入 日志目录/game-tunnel.log），出问题时用户可以自己打开看（也用于诊断弹窗）。
/// 滚动与容量上限统一由 <see cref="LogService"/> 负责。
/// </summary>
public static class TunnelLog
{
    public static string LogPath => LogService.PathFor(LogService.GameTunnel);

    public static void Info(string message) => LogService.Info(LogService.GameTunnel, message);

    public static void Warn(string message) => LogService.Warn(LogService.GameTunnel, message);

    public static void Error(string message, Exception? ex = null)
        => LogService.Error(LogService.GameTunnel, message, ex);

    public static string ReadRecent(int maxLines = 200) => LogService.ReadTail(LogPath, maxLines);
}
