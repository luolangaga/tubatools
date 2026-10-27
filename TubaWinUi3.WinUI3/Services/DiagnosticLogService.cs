namespace TubaWinUi3.Services;

/// <summary>诊断日志属于持久数据，不参与临时文件清理。目录可在设置页自定义（issue #165）。</summary>
internal static class DiagnosticLogService
{
    /// <summary>设置页自定义日志目录的键；空/目录不存在时回退默认目录。</summary>
    internal const string LogDirSettingKey = "LogDir";

    internal static string DefaultDirectoryPath => Path.Combine(ConfigManager.GetDataDir(), "Logs");

    internal static string DirectoryPath => ResolveDirectoryPath();

    internal static string CrashLogPath => Path.Combine(DirectoryPath, "app_crash.log");

    internal static string StartupLogPath => Path.Combine(DirectoryPath, "startup_trace.log");

    /// <summary>解析当前生效的日志目录：自定义目录存在即用，否则回退数据目录下的 Logs（纯逻辑，可单测）。</summary>
    internal static string ResolveDirectoryPath()
    {
        try
        {
            var custom = AppSettings.Get(LogDirSettingKey);
            if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom))
                return custom;
        }
        catch
        {
            // 崩溃路径也会走到这里：设置读取失败绝不能拦住日志落盘
        }
        return DefaultDirectoryPath;
    }

    /// <summary>校验候选目录可写（写入并删除探测文件），返回是否可用。</summary>
    internal static bool IsDirectoryWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".tuba_log_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void Prepare()
    {
        Directory.CreateDirectory(DirectoryPath);
        foreach (var name in new[] { "app_crash.log", "startup_trace.log" })
        {
            try
            {
                var oldPath = Path.Combine(Path.GetTempPath(), name);
                if (!File.Exists(oldPath)) continue;
                File.AppendAllText(Path.Combine(DirectoryPath, name), File.ReadAllText(oldPath));
                File.Delete(oldPath);
            }
            catch { } // 迁移失败保留原文件，错误报告仍可收录。
        }
    }

    internal static void AppendCrash(string detail)
    {
        try
        {
            Prepare();
            File.AppendAllText(CrashLogPath, detail);
        }
        catch { }
    }
}
