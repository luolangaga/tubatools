using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>
/// 联机日志：写进数据目录，出问题时用户可以自己打开看（也用于诊断弹窗）。
/// 单文件滚动，超过 256 KB 就重开，避免长期运行把磁盘写满。
/// </summary>
public static class TunnelLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Sync = new();
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    public static string LogPath => Path.Combine(GameTunnelCatalog.DataDir, "tunnel.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : message + Environment.NewLine + ex);

    public static string ReadRecent(int maxLines = 200)
    {
        try
        {
            if (!File.Exists(LogPath)) return "";
            var lines = File.ReadAllLines(LogPath, Utf8);
            var start = Math.Max(0, lines.Length - maxLines);
            return string.Join(Environment.NewLine, lines[start..]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static void Write(string level, string message)
    {
        var line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                   + " [" + level + "] " + message;
        Debug.WriteLine("[GameTunnel] " + line);

        lock (Sync)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                    File.Delete(LogPath);

                File.AppendAllText(LogPath, line + Environment.NewLine, Utf8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
