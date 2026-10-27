using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace TubaWinUi3.Services.SpaceCleanup;

/// <summary>
/// Enumerates directory-entry metadata on an owned native handle. Immediately disables
/// access-time updates on that SAME handle, before enumerating anything. The -1 FILETIME
/// is a documented per-handle suppression sentinel; it does not set a timestamp on disk.
/// Never falls back to enumeration that could dirty last-access metadata.
/// </summary>
internal static class SpaceReadOnlyDirectory
{
    internal readonly record struct Entry(string Name, FileAttributes Attributes, long Bytes, DateTime Modified);

    internal static IEnumerable<Entry> Enumerate(string path, byte[] buffer, CancellationToken token)
    {
        using var handle = Open(path, true);
        bool restart = true;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!GetFileInformationByHandleEx(handle, restart ? 15 : 14, buffer, (uint)buffer.Length))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 18) yield break; // ERROR_NO_MORE_FILES
                throw Error(path, error);
            }
            restart = false;
            int offset = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var entry = Parse(buffer, offset, out int next);
                if (entry.Name is not "." and not "..") yield return entry;
                if (next == 0) break;
                offset = checked(offset + next);
            }
        }
    }

    // FILE_FULL_DIR_INFO: variable UTF-16 name at byte 68; NextEntryOffset is 8-byte aligned.
    internal static Entry Parse(byte[] buffer, int offset, out int next)
    {
        if (offset < 0 || offset > buffer.Length - 68) throw new IOException("目录元数据记录不完整。");
        var span = buffer.AsSpan(offset);
        next = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span));
        int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[60..]));
        if ((length & 1) != 0 || length > span.Length - 68 ||
            (next != 0 && (next < 68 + length || (next & 7) != 0 || next > span.Length - 68)))
            throw new IOException("目录元数据记录的边界无效。");
        string name = Encoding.Unicode.GetString(buffer, offset + 68, length);
        var attributes = (FileAttributes)BinaryPrimitives.ReadUInt32LittleEndian(span[56..]);
        long time = BinaryPrimitives.ReadInt64LittleEndian(span[24..]);
        var modified = time >= 0 && time <= 2650467743999999999L ? DateTime.FromFileTimeUtc(time).ToLocalTime() : DateTime.MinValue;
        return new(name, attributes, Math.Max(0, BinaryPrimitives.ReadInt64LittleEndian(span[40..])), modified);
    }

    internal static SafeFileHandle Open(string path, bool directory)
    {
        string nativePath = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path :
            path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + Path.GetFullPath(path);
        // WRITE_ATTRIBUTES is needed ONLY for the -1 suppression sentinel. No WRITE_DATA,
        // no timestamp restoration (which would itself write), no content reads or recall.
        var handle = CreateFileW(nativePath, (directory ? 1u : 0u) | 0x80u | 0x100u, 7, IntPtr.Zero, 3,
            0x02000000u | 0x00200000u | 0x00100000u, IntPtr.Zero);
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw Error(path, error); }
        long doNotUpdate = -1;
        if (!SetFileTime(handle, IntPtr.Zero, ref doNotUpdate, IntPtr.Zero))
        { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw Error(path + "（无法抑制访问时间更新，已跳过）", error); }
        if (!GetBasicInformation(handle, 0, out var basic, (uint)Marshal.SizeOf<BasicInformation>()))
        { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw Error(path, error); }
        if ((basic.Attributes & FileAttributes.ReparsePoint) != 0 ||
            (directory && (basic.Attributes & FileAttributes.Directory) == 0))
        { handle.Dispose(); throw new IOException(path + "：链接、挂载点或目录类型已改变，已跳过。"); }
        return handle;
    }

    internal static long NamedStreamBytes(string path, byte[] buffer)
    {
        using var handle = Open(path, false);
        if (!GetFileInformationByHandleEx(handle, 7, buffer, (uint)buffer.Length)) throw Error(path, Marshal.GetLastWin32Error());
        long total = 0; int offset = 0;
        while (true)
        {
            if (offset > buffer.Length - 24) throw new IOException("数据流元数据记录不完整。");
            var span = buffer.AsSpan(offset);
            int next = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span));
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[4..]));
            if ((length & 1) != 0 || length > span.Length - 24) throw new IOException("数据流名称边界无效。");
            string name = Encoding.Unicode.GetString(buffer, offset + 24, length);
            if (!name.Equals("::$DATA", StringComparison.OrdinalIgnoreCase)) total += Math.Max(0, BinaryPrimitives.ReadInt64LittleEndian(span[8..]));
            if (next == 0) break;
            if (next < 24 + length || next > span.Length - 24) throw new IOException("数据流记录边界无效。");
            offset += next;
        }
        return total;
    }

    private static IOException Error(string path, int error) => new($"{path}：{new Win32Exception(error).Message}（未使用可能更新时间的回退扫描）", new Win32Exception(error));

    /// <summary>Use the admin token's existing backup/restore privileges only on this worker
    /// thread so protected directories can be opened for timestamp suppression. Restore the
    /// previous impersonation token before the pool thread is returned to unrelated work.</summary>
    internal sealed class BackupScope : IDisposable
    {
        private SafeAccessTokenHandle? _token;
        public BackupScope()
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0A, out var processToken)) { processToken?.Dispose(); return; }
            using (processToken)
            {
                if (!DuplicateTokenEx(processToken, 0x2E, IntPtr.Zero, 2, 2, out var token)) { token?.Dispose(); return; }
                _token = token;
                foreach (string name in new[] { "SeBackupPrivilege", "SeRestorePrivilege" })
                    if (LookupPrivilegeValueW(null, name, out long luid))
                    {
                        var privileges = new Privileges { Count = 1, Luid = luid, Attributes = 2 };
                        // Missing privileges on non-admin tokens do not change access: ordinary
                        // ACLs apply and a directory that cannot be suppressed is skipped.
                        AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
                    }
            }
        }
        internal void Run(Action action)
        {
            if (_token is not null) WindowsIdentity.RunImpersonated(_token, action);
            else action();
        }
        public void Dispose() => _token?.Dispose();
        [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct Privileges { public uint Count; public long Luid; public uint Attributes; }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DuplicateTokenEx(SafeAccessTokenHandle source, uint access, IntPtr attributes, int level, int type, out SafeAccessTokenHandle token);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disable, ref Privileges privileges, uint length, IntPtr previous, IntPtr returned);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileTime(SafeFileHandle handle, IntPtr creation, ref long access, IntPtr write);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, [Out] byte[] buffer, uint size);
    [StructLayout(LayoutKind.Sequential)] private struct BasicInformation
    { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public FileAttributes Attributes; }
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetBasicInformation(SafeFileHandle handle, int informationClass, out BasicInformation information, uint size);
}
