using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>
/// 启动第三方工具进程的统一入口。
/// 打包(MSIX)环境下应用进程无法发起 UAC 提权请求:用 ShellExecuteEx 启动
/// 声明了 requireAdministrator 的程序(如 HWiNFO64.exe)会直接失败,抛出
/// Win32Exception(ERROR_NOT_SUPPORTED = 0x32,"不支持 该请求")。
/// 此时自动改用 cmd.exe 的 start 命令作为载体(实测验证:打包环境下可正常
/// 弹出 UAC 提权提示,效果等同用户手动双击该程序;而 explorer.exe 载体在
/// 打包环境下会丢失参数)。非打包环境下应用本身已提权运行,直接启动即可。
/// </summary>
public static class ToolProcessLauncher
{
    private const int ErrorNotSupported = 0x32;

    public static void Launch(string exePath, string? workingDirectory = null, bool runAsAdmin = false)
    {
        // 防呆：UseShellExecute=true 时若 FileName 是已存在的目录，会打开资源管理器窗口
        // 而不是启动程序（link.json 内置链接工具的 EffectivePath 就是其所在目录）。
        // 这类工具必须走 BuiltinToolRegistry + 内置工具窗口通道，绝不能直接 Launch。
        if (Directory.Exists(exePath))
            throw new InvalidOperationException($"路径是文件夹而非可执行程序，不能直接启动：{exePath}");
        var originalPath = exePath;
        (exePath, workingDirectory) = FurMarkLaunchPath.Prepare(exePath, workingDirectory);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
                Verb = runAsAdmin ? "runas" : null
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNotSupported && RuntimeHelper.IsMsixPackaged)
        {
            LaunchViaCmdStart(exePath, workingDirectory, runAsAdmin);
        }
        Telemetry.TelemetryService.TrackExternalToolLaunch(originalPath);
    }

    private static void LaunchViaCmdStart(string exePath, string? workingDirectory, bool runAsAdmin)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (runAsAdmin)
        {
            var script = string.IsNullOrEmpty(workingDirectory)
                ? $"Start-Process -FilePath '{exePath}' -Verb RunAs"
                : $"Start-Process -FilePath '{exePath}' -Verb RunAs -WorkingDirectory '{workingDirectory}'";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            psi.FileName = "powershell.exe";
            psi.Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}";
        }
        else
        {
            psi.Arguments = $"/c start \"\" \"{exePath}\"";
        }
        Process.Start(psi);
    }
}
