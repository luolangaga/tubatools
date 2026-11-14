using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Tubatools.AiJunkCleanup.Services;

internal sealed record LockedProcess(int Pid, string Name, bool CanClose, string? Warning = null, bool HasWindow = false);

internal enum LockResolution
{
    Retry,
    Skip,
    Cancel
}

internal delegate Task<LockResolution> LockedFileHandler(
    string path, IReadOnlyList<LockedProcess> processes, CancellationToken ct);

/// <summary>
/// Windows Restart Manager：查询某个文件被哪些进程占用。只读查询，不修改文件。
/// 只允许自动关闭有主窗口的普通应用；服务、Explorer、系统关键进程和当前进程只展示，不自动关闭。
/// </summary>
internal static class LockedFileResolver
{
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TsSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, string sessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint sessionHandle, uint fileCount, string[] fileNames,
        uint applicationCount, RmUniqueProcess[]? applications, uint serviceCount, string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint sessionHandle, out uint processesNeeded, ref uint processCount,
        [In, Out] RmProcessInfo[]? affectedApps, ref uint rebootReasons);

    internal static IReadOnlyList<LockedProcess> Find(string path)
    {
        var result = new List<LockedProcess>();
        uint session = 0;
        try
        {
            if (RmStartSession(out session, 0, Guid.NewGuid().ToString("N")) != ErrorSuccess) return result;
            if (RmRegisterResources(session, 1, [path], 0, null, 0, null) != ErrorSuccess) return result;

            uint needed = 0, count = 0, reasons = 0;
            var error = RmGetList(session, out needed, ref count, null, ref reasons);
            if (error != ErrorMoreData) return result;

            count = needed;
            var apps = new RmProcessInfo[count];
            error = RmGetList(session, out needed, ref count, apps, ref reasons);
            if (error != ErrorSuccess) return result;

            for (var i = 0; i < count; i++)
            {
                var app = apps[i];
                var pid = app.Process.ProcessId;
                if (pid <= 0 || pid == Environment.ProcessId) continue;
                if (result.Any(item => item.Pid == pid)) continue;
                var name = string.IsNullOrWhiteSpace(app.AppName) ? TryProcessName(pid) : app.AppName.Trim();
                // 主窗口/普通窗口/控制台/无窗口普通程序允许结束；服务、Explorer、系统关键进程只展示。
                var canClose = app.ApplicationType is 0 or 1 or 2 or 5;
                var hasWindow = app.ApplicationType is 1 or 2;
                var warning = app.ApplicationType switch
                {
                    3 => "Windows 服务，不能自动关闭",
                    4 => "Explorer，不能自动关闭",
                    1000 => "系统关键进程，不能自动关闭",
                    5 => "控制台程序，关闭时会直接结束进程（未保存的工作可能丢失）",
                    0 => "无窗口/未知类型进程，关闭时会直接结束进程（未保存的工作可能丢失）",
                    _ => null
                };
                result.Add(new LockedProcess(pid, name, canClose, warning, hasWindow));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
        }
        finally
        {
            if (session != 0)
            {
                try { RmEndSession(session); } catch (Exception) { }
            }
        }
        return result;
    }

    private static string TryProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "PID " + pid;
        }
    }
}
