using System.Diagnostics;

namespace Tubatools.AiJunkCleanup.Services;

/// <summary>关闭占用文件的普通进程。先尝试关闭主窗口/结束进程树，失败时用 taskkill 兜底，并返回可读错误。</summary>
internal static class ProcessCloser
{
    internal static string? Terminate(IReadOnlyList<LockedProcess> processes)
    {
        var errors = new List<string>();
        foreach (var item in processes)
        {
            if (!item.CanClose) continue;
            if (item.Pid == Environment.ProcessId) continue;

            Process? process = null;
            try
            {
                process = Process.GetProcessById(item.Pid);
            }
            catch (ArgumentException)
            {
                // 已经退出。
                continue;
            }

            try
            {
                using (process)
                {
                    if (process.HasExited) continue;
                    if (process.CloseMainWindow())
                    {
                        if (!process.WaitForExit(2000))
                            process.Kill(entireProcessTree: true);
                    }
                    else
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    if (!process.WaitForExit(3000) && !TryTaskKill(item.Pid))
                        errors.Add(item.Name + " (PID " + item.Pid + ") 仍在运行");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                           or NotSupportedException or UnauthorizedAccessException)
            {
                if (!TryTaskKill(item.Pid))
                    errors.Add(item.Name + " (PID " + item.Pid + ")：" + ex.Message);
            }
        }
        return errors.Count == 0 ? null : string.Join("；", errors);
    }

    private static bool TryTaskKill(int pid)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + pid + " /T /F")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null) return false;
            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                       or NotSupportedException or IOException)
        {
            return false;
        }
    }
}
