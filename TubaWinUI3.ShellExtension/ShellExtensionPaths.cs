using System.Runtime.InteropServices;
using System.Text;
using TubaWinUi3.ShellIntegration;

namespace TubaWinUi3.ShellExtension;

/// <summary>
/// 运行环境定位（包目录 / 数据根 / 启动主程序）与诊断日志。
/// 全部容错：任何一步失败都只降级（回退路径、写日志），绝不向 COM 调用方抛异常。
/// </summary>
internal static class ShellExtensionPaths
{
    private static readonly Lazy<string?> LazyPackageRoot = new(TryFindPackageRootDirectory);

    /// <summary>
    /// 标记文件候选数据根（处理程序与主程序写入位置一致）：
    /// 1) 共享真实路径 %LocalAppData%（便携版数据根；打包/提权会话都会镜像写这里）；
    /// 2) 包 LocalState（%LocalAppData%\Packages\&lt;PFN&gt;\LocalState，打包运行时的数据根）。
    /// </summary>
    internal static IReadOnlyList<string> DataRoots()
    {
        var roots = new List<string>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) return roots;

        roots.Add(localAppData);

        var familyName = TryGetPackageFamilyName();
        if (familyName is not null)
            roots.Add(Path.Combine(localAppData, "Packages", familyName, "LocalState"));

        return roots;
    }

    /// <summary>本 DLL 所在目录（= 包根；用于定位 Assets 图标与包内 exe 回退）。</summary>
    internal static string? PackageRootDirectory => LazyPackageRoot.Value;

    /// <summary>菜单图标引用：包内 Assets\AppIcon.ico，索引 0。</summary>
    internal static string? TryGetIconReference()
    {
        var root = PackageRootDirectory;
        if (string.IsNullOrEmpty(root)) return null;
        var icon = Path.Combine(root, "Assets", "AppIcon.ico");
        return File.Exists(icon) ? icon + ",0" : null;
    }

    /// <summary>
    /// 启动「文件占用查看」：优先应用执行别名（保留包身份，参数原样传递），
    /// 别名缺失/启动失败时回退包内 exe 直启（无包身份但功能可用）。
    /// </summary>
    internal static bool TryLaunchFileLockCheck(string filePath, out string detail)
    {
        detail = string.Empty;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var alias = Path.Combine(localAppData, "Microsoft", "WindowsApps", FileLockShellMenuContract.ExecutionAlias);
        if (File.Exists(alias) && TryCreateProcess(alias, filePath, out detail)) return true;

        var aliasDetail = detail;
        var root = PackageRootDirectory;
        if (!string.IsNullOrEmpty(root))
        {
            var exe = Path.Combine(root, "TubaWinUi3.exe");
            if (File.Exists(exe) && TryCreateProcess(exe, filePath, out detail)) return true;
        }

        detail = detail.Length > 0 ? detail : aliasDetail;
        if (detail.Length == 0) detail = "执行别名与包内 exe 均不可用。";
        return false;
    }

    /// <summary>诊断日志：%LocalAppData%\TubaWinUi3\shell-menu\handler.log（64 KB 滚动）。</summary>
    internal static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TubaWinUi3", "shell-menu");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "handler.log");
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);
            }
            catch
            {
            }

            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}",
                new UTF8Encoding(false));
        }
        catch
        {
            // 日志失败绝不影响菜单本身
        }
    }

    private static unsafe bool TryCreateProcess(string exePath, string filePath, out string detail)
    {
        var commandLine = $"\"{exePath}\" {FileLockShellMenuContract.FileLockArg} \"{filePath}\"";
        // CreateProcessW 可能原地修改命令行缓冲区：用自有字符数组 + fixed 传递，不用托管字符串。
        var buffer = (commandLine + "\0").ToCharArray();
        fixed (char* commandLinePointer = buffer)
        {
            var startup = new ShellExtensionNative.StartupInfo { cb = sizeof(ShellExtensionNative.StartupInfo) };
            if (ShellExtensionNative.CreateProcess(
                    null, commandLinePointer, 0, 0, false, 0, 0, null, ref startup, out var information))
            {
                ShellExtensionNative.CloseHandle(information.hProcess);
                ShellExtensionNative.CloseHandle(information.hThread);
                detail = string.Empty;
                return true;
            }

            detail = $"{Path.GetFileName(exePath)} 启动失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
            return false;
        }
    }

    private static unsafe string? TryFindPackageRootDirectory()
    {
        // 用导出函数的地址反查本模块句柄（dllhost 进程里 Environment.ProcessPath 是 dllhost.exe，不可用）
        var address = (nint)(delegate* unmanaged[Stdcall]<Guid*, Guid*, void**, int>)&ExplorerCommandServer.DllGetClassObject;
        if (!ShellExtensionNative.GetModuleHandleEx(
                ShellExtensionNative.GetModuleHandleExFromAddress | ShellExtensionNative.GetModuleHandleExUnchangedRefCount,
                address, out var module) || module == 0)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[1024];
        fixed (char* pointer = buffer)
        {
            var length = (int)ShellExtensionNative.GetModuleFileName(module, pointer, (uint)buffer.Length);
            if (length <= 0 || length >= buffer.Length) return null;
            return Path.GetDirectoryName(new string(buffer[..length]));
        }
    }

    private static unsafe string? TryGetPackageFamilyName()
    {
        var length = 0;
        ShellExtensionNative.GetCurrentPackageFamilyName(ref length, null);
        if (length <= 1) return null;

        var buffer = stackalloc char[length];
        var hr = ShellExtensionNative.GetCurrentPackageFamilyName(ref length, buffer);
        if (hr != 0) return null;
        return new string(buffer, 0, length - 1);
    }
}
