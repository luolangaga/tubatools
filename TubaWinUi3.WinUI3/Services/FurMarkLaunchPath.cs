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
            return (Path.Combine(AliasDirectory(Path.GetDirectoryName(exePath)!, root), Path.GetFileName(exePath)),
                AliasDirectory(workingDirectory, root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            throw new IOException("无法为 FurMark 建立英文路径兼容入口。请检查目录权限及磁盘是否支持目录联接，或将 FurMark 整个文件夹移至纯英文路径后运行。", ex);
        }
    }

    private static bool IsAscii(string value) => value.All(c => c <= 127);

    internal static string GetAliasRoot(string? localData = null, string? commonData = null)
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new IOException("无法识别当前用户。");
        // 通常使用用户私有目录；中文用户名时转到 ProgramData 下按 SID 隔离的目录。
        var root = Path.Combine(localData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TubaWinUi3.FurMark");
        if (!IsAscii(root))
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
        return root;
    }

    internal static string AliasDirectory(string target, string root)
    {
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException(target);
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
        IntPtr output, int outputSize, out int returned, IntPtr overlapped);
}
