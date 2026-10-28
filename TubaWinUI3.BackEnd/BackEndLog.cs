namespace TubaWinUI3.BackEnd;

/// <summary>
/// 轻量日志：控制台 + 可选文件。线程安全。
///
/// 文件自己滚动（超过 <see cref="MaxBytes"/> 轮转成 <c>.1 / .2</c>），因为后端是独立进程：
/// 主程序那边的总量配额只在主程序写日志时才检查，不能指望它来兜住后端的心跳噪声。
/// </summary>
public static class BackEndLog
{
    private const long MaxBytes = 1024 * 1024;
    private const int MaxRolledFiles = 2;

    private static readonly object _lock = new();
    private static string? _file;

    public static void Configure(string? logFile)
    {
        lock (_lock)
        {
            _file = string.IsNullOrWhiteSpace(logFile) ? null : logFile;
            if (_file is not null)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_file);
                    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                }
                catch { }
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    /// <summary>调试级：只进控制台，不落盘（心跳/每轮扫描这类噪声走这里）。</summary>
    public static void Debug(string message)
    {
        try { Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [DEBUG] {message}"); } catch { }
    }

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_lock)
        {
            try { Console.WriteLine(line); } catch { }
            if (_file is null) return;

            try
            {
                var dir = Path.GetDirectoryName(_file);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                RotateIfNeeded(_file!);
                File.AppendAllText(_file, line + Environment.NewLine);
            }
            catch { }
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxBytes) return;

            var oldest = $"{path}.{MaxRolledFiles}";
            if (File.Exists(oldest)) File.Delete(oldest);

            for (var i = MaxRolledFiles - 1; i >= 1; i--)
            {
                var from = $"{path}.{i}";
                if (File.Exists(from)) File.Move(from, $"{path}.{i + 1}", overwrite: true);
            }

            File.Move(path, $"{path}.1", overwrite: true);
        }
        catch
        {
            // 滚动失败就继续追加，不影响日志本身
        }
    }
}
