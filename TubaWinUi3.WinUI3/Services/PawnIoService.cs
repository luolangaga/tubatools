using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

/// <summary>
/// PawnIO 传感器驱动支持：LibreHardwareMonitor 0.9.7 起 ring0 访问（MSR）完全依赖 PawnIO，
/// 没有它就读不到 CPU 温度/频率/功耗 —— 游戏监控与CPU / GPU 烤机共用同一引擎，都会缺这几项数据。
/// 这里负责：检测驱动 → 已安装未启动时启动服务 → 未安装时引导下载官方安装包。
/// </summary>
public static class PawnIoService
{
    private const string DevicePath = @"\\.\PawnIO";
    private const string ServiceName = "PawnIO";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public const string SetupUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_START = 0x0010;
    private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;

    private static string SetupFile => Path.Combine(ConfigManager.GetDataDir(), "downloads", "PawnIO_setup.exe");

    public static bool IsDeviceAvailable()
    {
        try
        {
            using var handle = new FileStream(DevicePath, FileMode.Open, FileAccess.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
            if (key is not null) return true;
            // x86 构建下默认视图会被重定向到 Wow6432Node，显式读 64 位视图（与 LHM 自身的判断一致）
            using var key64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(UninstallKey);
            if (key64 is not null) return true;
        }
        catch { }

        IntPtr scm = IntPtr.Zero, svc = IntPtr.Zero;
        try
        {
            scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return false;
            svc = OpenService(scm, ServiceName, SERVICE_QUERY_STATUS);
            return svc != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (svc != IntPtr.Zero) CloseServiceHandle(svc);
            if (scm != IntPtr.Zero) CloseServiceHandle(scm);
        }
    }

    private static bool s_startFailed;

    /// <summary>确保驱动已加载：设备直接可用则返回；已安装但停着（重启后驱动不会自动加载）就启动服务再确认。</summary>
    public static bool EnsureDriver()
    {
        if (IsDeviceAvailable()) return true;
        if (s_startFailed || !IsInstalled()) return false;
        if (!TryStartService())
        {
            s_startFailed = true;
            return false;
        }

        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(200);
            if (IsDeviceAvailable()) return true;
        }
        s_startFailed = true;
        return false;
    }

    /// <summary>安装/重装驱动后清掉「启动失败」判断，让下一次采样重新尝试。</summary>
    public static void ResetStartState() => s_startFailed = false;

    private static bool TryStartService()
    {
        IntPtr scm = IntPtr.Zero, svc = IntPtr.Zero;
        try
        {
            scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return false;
            svc = OpenService(scm, ServiceName, SERVICE_START | SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;
            if (StartService(svc, 0, null)) return true;
            return Marshal.GetLastWin32Error() == ERROR_SERVICE_ALREADY_RUNNING;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (svc != IntPtr.Zero) CloseServiceHandle(svc);
            if (scm != IntPtr.Zero) CloseServiceHandle(scm);
        }
    }

    /// <summary>下载并运行官方安装包（安装界面由用户完成），结束后重新确认驱动是否可用。</summary>
    public static async Task<(bool Ok, string Message)> InstallAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(SetupFile)!;
            Directory.CreateDirectory(dir);
            await ToolDownloaderService.DownloadToFileAsync(SetupUrl, dir, Path.GetFileName(SetupFile), null);
            if (!File.Exists(SetupFile)) return (false, "PawnIO 安装包下载失败，请检查网络后重试");

            using var proc = Process.Start(new ProcessStartInfo(SetupFile) { UseShellExecute = true });
            if (proc is not null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                try { await proc.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { }
            }

            ResetStartState();
            if (EnsureDriver()) return (true, "PawnIO 传感器驱动已就绪");
            return (false, "驱动仍未就绪：可能安装未完成，或安装后需要重启系统");
        }
        catch (Exception ex)
        {
            return (false, $"PawnIO 安装失败: {ex.Message}");
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scm, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartService(IntPtr service, int numArgs, string[]? args);

    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
