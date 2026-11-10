using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TubaWinUi3.Services;

/// <summary>
/// FurMark 的旧路径接口不能可靠处理非 ASCII 路径。只为它建立持久目录联接，
/// 不搬动工具，不复制配置，不改变目录/收藏的身份。快捷方式也使用同一联接。
/// </summary>
internal static class FurMarkLaunchPath
{
    internal static bool IsFurMark(string path) => Path.GetFileName(path).ToLowerInvariant()
        is "furmark_gui.exe" or "furmark.exe" or "_fm2-gui.exe";

    internal static (string ExePath, string? WorkingDirectory) Prepare(string exePath, string? workingDirectory)
    {
        if (!IsFurMark(exePath)) return (exePath, workingDirectory);
        exePath = Path.GetFullPath(exePath);
        if (!File.Exists(exePath)) throw new FileNotFoundException("找不到 FurMark。", exePath);
        workingDirectory = string.IsNullOrEmpty(workingDirectory)
            ? Path.GetDirectoryName(exePath)! : Path.GetFullPath(workingDirectory);
        if (IsAscii(exePath) && IsAscii(workingDirectory)) return (exePath, workingDirectory);

        try
        {
            var root = GetAliasRoot();
            var launchPath = Path.Combine(AliasDirectory(Path.GetDirectoryName(exePath)!, root), Path.GetFileName(exePath));
            var launchDirectory = AliasDirectory(workingDirectory, root);
            LogService.Debug(LogService.App, $"FurMark: MSIX={RuntimeHelper.IsMsixPackaged}; " +
                $"source={exePath}; launch={launchPath}; cwd={launchDirectory}; " +
                $"gxc(source/launch)={File.Exists(Path.Combine(Path.GetDirectoryName(exePath)!, "gxc_x64.dll"))}/" +
                $"{File.Exists(Path.Combine(Path.GetDirectoryName(launchPath)!, "gxc_x64.dll"))}");
            return (launchPath, launchDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            LogService.Error(LogService.App, $"FurMark 路径准备失败：{exePath}", ex);
            throw new IOException("无法为 FurMark 建立英文路径兼容入口。请检查目录权限及磁盘是否支持目录联接，或将 FurMark 整个文件夹移至纯英文路径后运行。", ex);
        }
    }

    private static bool IsAscii(string value) => value.All(c => c <= 127);

    internal static string GetAliasRoot(string? localData = null, string? commonData = null) =>
        GetAliasRoot(localData, commonData, useCommonData: false);

    private static string GetAliasRoot(string? localData, string? commonData, bool useCommonData)
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new IOException("无法识别当前用户。");
        // 通常使用用户私有目录；中文用户名时转到 ProgramData 下按 SID 隔离的目录。
        // MSIX 用明确的 LocalState，不在可能被虚拟化的通用 AppData 视图下创建启动入口。
        var root = Path.Combine(localData ?? RuntimeHelper.GetLocalAppDataRoot(), "TubaWinUi3.FurMark");
        if (useCommonData || !IsAscii(root) || !FitsLegacyLaunchPath(root))
            root = Path.Combine(commonData ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TubaWinUi3.FurMark-" + user.Value);
        if (!Path.IsPathFullyQualified(root) || !IsAscii(root))
            throw new IOException("无法找到可用的纯英文兼容目录。");

        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(root);
        directory.Create(security);
        // 不接管已有的不可信目录，避免从可被其他普通用户替换的入口启动提权工具。
        var actual = directory.GetAccessControl();
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
            !user.Equals(actual.GetOwner(typeof(SecurityIdentifier))) || !actual.AreAccessRulesProtected ||
            actual.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(r =>
                r.AccessControlType == AccessControlType.Allow && !r.IdentityReference.Equals(user) &&
                !r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)) &&
                !r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null))))
            throw new IOException("兼容目录的权限或类型不安全。");
        // 创建目录时可能发生 MSIX 重定向。子进程/快捷方式需要真实落点，不能继续使用合并视图的路径。
        var physicalRoot = ResolvePhysicalDirectory(root);
        if (!IsAscii(physicalRoot) || !FitsLegacyLaunchPath(physicalRoot))
        {
            if (!useCommonData) return GetAliasRoot(localData, commonData, useCommonData: true);
            throw new IOException("FurMark 兼容目录的实际路径不是可用的短英文路径。");
        }
        return physicalRoot;
    }

    private static bool FitsLegacyLaunchPath(string root) =>
        root.Length + 1 + 64 + 1 + "FurMark_GUI.exe".Length < 260;

    internal static string AliasDirectory(string target, string root)
    {
        target = ResolvePhysicalDirectory(target);
        if (target.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("目录联接不支持网络目标。");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(target.ToUpperInvariant())));
        var alias = Path.Combine(root, hash);
        // 搜索索引与窗口启动可能并发；跨进程也不能观察到尚未写完的联接。
        using var mutex = new Mutex(false, @"Local\TubaWinUi3.FurMark-" + hash);
        bool entered;
        try { entered = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { entered = true; }
        if (!entered) throw new IOException("等待 FurMark 兼容目录超时。");
        try
        {
            var existing = new DirectoryInfo(alias);
            if (existing.Exists || existing.LinkTarget is not null)
            {
                var linked = existing.ResolveLinkTarget(false);
                if (linked is null || !string.Equals(Path.TrimEndingDirectorySeparator(linked.FullName), target, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("FurMark 兼容入口已被其他目录占用。");
                return alias;
            }

            Directory.CreateDirectory(alias);
            try { CreateJunction(alias, target); }
            catch
            {
                // 只删除刚创建的空入口，永不递归删除联接目标。
                Directory.Delete(alias);
                throw;
            }
            return alias;
        }
        finally { mutex.ReleaseMutex(); }
    }

    internal static string ResolvePhysicalDirectory(string path)
    {
        path = Path.GetFullPath(path);
        // 跟随目录联接并取得真实打开的目录。MSIX 的合并视图路径不能直接写进内核联接目标：
        // 联接与没有包身份的工具进程均不会再次经过父进程的文件重定向。
        var extendedPath = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
            : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        using var handle = CreateFileW(extendedPath, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法打开 FurMark 目录：{path}");
        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity)
        {
            buffer.EnsureCapacity(checked((int)length + 1));
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (length == 0 || length >= buffer.Capacity)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法解析 FurMark 目录：{path}");
        var physicalPath = buffer.ToString();
        if (physicalPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            physicalPath = @"\\" + physicalPath[8..];
        else if (physicalPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            physicalPath = physicalPath[4..];
        return Path.TrimEndingDirectorySeparator(physicalPath);
    }

    private static void CreateJunction(string alias, string target)
    {
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        var print = Encoding.Unicode.GetBytes(target);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0xA0000003u); // IO_REPARSE_TAG_MOUNT_POINT
        writer.Write(checked((ushort)(8 + substitute.Length + 2 + print.Length + 2)));
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(checked((ushort)substitute.Length));
        writer.Write(checked((ushort)(substitute.Length + 2)));
        writer.Write(checked((ushort)print.Length));
        writer.Write(substitute);
        writer.Write((ushort)0);
        writer.Write(print);
        writer.Write((ushort)0);
        var buffer = stream.ToArray();
        using var handle = CreateFileW(alias, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!DeviceIoControl(handle, 0x000900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
        IntPtr output, int outputSize, out int returned, IntPtr overlapped);
}
