using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.SpaceCleanup;

public sealed record WindowsCleanupItem(string Key, string Name, string Description, long Bytes, string? Error = null);

/// <summary>Windows Disk Cleanup handlers, directly through IEmptyVolumeCache (emptyvc.h).
/// No cleanmgr subprocess, registry StateFlags changes, or hardcoded deletion paths.</summary>
public static class WindowsCleanupService
{
    private const string HandlerPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";
    public static async Task<List<WindowsCleanupItem>> ScanAsync(string volume, IProgress<string>? progress, CancellationToken token)
    {
        var results = new List<WindowsCleanupItem>();
        await SpaceFileOperations.RunSta(() =>
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
            using var handlers = hive.OpenSubKey(HandlerPath);
            if (handlers is null) throw new NotSupportedException("此系统没有注册 Windows 磁盘清理处理程序。");
            foreach (var name in handlers.GetSubKeyNames())
            {
                token.ThrowIfCancellationRequested(); progress?.Report($"正在检查 Windows 清理项目：{name}");
                try
                {
                    WithHandler(name, volume, (handler, title, description) =>
                    {
                        int hr = handler.GetSpaceUsed(out ulong size, new CleanupCallback(token, progress));
                        token.ThrowIfCancellationRequested(); Marshal.ThrowExceptionForHR(hr);
                        if (hr != 0 || size == ulong.MaxValue) return;
                        results.Add(new WindowsCleanupItem(name, title, description, (long)Math.Min(size, (ulong)long.MaxValue)));
                    });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { results.Add(new WindowsCleanupItem(name, name, "此处理程序不可用", 0, ex.Message)); }
            }
        });
        return results;
    }

    public static Task PurgeAsync(string volume, IEnumerable<WindowsCleanupItem> items, IProgress<string>? progress, CancellationToken token)
        => SpaceFileOperations.RunSta(() =>
        {
            var errors = new List<string>();
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested(); progress?.Report($"Windows 正在清理：{item.Name}");
                try { WithHandler(item.Key, volume, (handler, _, _) =>
                    {
                        // Some handlers build their deletion list in GetSpaceUsed. Recreate that list
                        // in the same apartment/instance as Purge, after the user's confirmation.
                        var callback = new CleanupCallback(token, progress);
                        int scan = handler.GetSpaceUsed(out _, callback); token.ThrowIfCancellationRequested(); Marshal.ThrowExceptionForHR(scan);
                        if (scan != 0) throw new IOException("Windows 无法重新分析此项目，请重新扫描。");
                        int purge = handler.Purge(ulong.MaxValue, callback); token.ThrowIfCancellationRequested(); Marshal.ThrowExceptionForHR(purge);
                        if (purge != 0) throw new IOException("Windows 未完成此清理项目，请重新扫描。");
                    }); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors.Add($"{item.Name}：{ex.Message}"); }
            }
            if (errors.Count > 0) throw new IOException(string.Join("\n", errors));
        });

    private static void WithHandler(string name, string volume, Action<IEmptyVolumeCache, string, string> action)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
        using var key = hive.OpenSubKey(HandlerPath + "\\" + name) ?? throw new IOException("清理处理程序已被移除。");
        if (!Guid.TryParse(key.GetValue("") as string, out var clsid)) return;
        object? instance = null; IEmptyVolumeCache? handler = null;
        IntPtr display = IntPtr.Zero, description = IntPtr.Zero, buttonText = IntPtr.Zero;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, true)!);
            handler = (IEmptyVolumeCache)instance!; uint flags = 0;
            string root = Path.GetPathRoot(Path.GetFullPath(volume))!.TrimEnd('\\');
            int initialized = instance is IEmptyVolumeCache2 localized
                ? localized.InitializeEx(key.Handle.DangerousGetHandle(), root, name, out display, out description, out buttonText, ref flags)
                : handler.Initialize(key.Handle.DangerousGetHandle(), root, out display, out description, ref flags);
            Marshal.ThrowExceptionForHR(initialized);
            if (initialized != 0 || (flags & 0x0004) != 0) return;
            string title = display == IntPtr.Zero ? name : Marshal.PtrToStringUni(display) ?? name;
            string detail = description == IntPtr.Zero ? "由 Windows 磁盘清理处理程序管理" : Marshal.PtrToStringUni(description) ?? "";
            action(handler, title, detail);
        }
        finally
        {
            if (handler is not null) { try { handler.Deactivate(out _); } catch { } }
            if (display != IntPtr.Zero) Marshal.FreeCoTaskMem(display);
            if (description != IntPtr.Zero) Marshal.FreeCoTaskMem(description);
            if (buttonText != IntPtr.Zero) Marshal.FreeCoTaskMem(buttonText);
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
        }
    }

    [ComImport, Guid("8fce5227-04da-11d1-a004-00805f8abe06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEmptyVolumeCache
    {
        [PreserveSig] int Initialize(IntPtr key, [MarshalAs(UnmanagedType.LPWStr)] string volume, out IntPtr display, out IntPtr description, ref uint flags);
        [PreserveSig] int GetSpaceUsed(out ulong bytes, IEmptyVolumeCacheCallBack callback);
        [PreserveSig] int Purge(ulong bytes, IEmptyVolumeCacheCallBack callback);
        [PreserveSig] int ShowProperties(IntPtr owner);
        [PreserveSig] int Deactivate(out uint flags);
    }
    [ComImport, Guid("02b7e3ba-4db3-11d2-b2d9-00c04f8eec8c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEmptyVolumeCache2
    {
        [PreserveSig] int Initialize(IntPtr key, [MarshalAs(UnmanagedType.LPWStr)] string volume, out IntPtr display, out IntPtr description, ref uint flags);
        [PreserveSig] int GetSpaceUsed(out ulong bytes, IEmptyVolumeCacheCallBack callback);
        [PreserveSig] int Purge(ulong bytes, IEmptyVolumeCacheCallBack callback);
        [PreserveSig] int ShowProperties(IntPtr owner);
        [PreserveSig] int Deactivate(out uint flags);
        [PreserveSig] int InitializeEx(IntPtr key, [MarshalAs(UnmanagedType.LPWStr)] string volume, [MarshalAs(UnmanagedType.LPWStr)] string keyName,
            out IntPtr display, out IntPtr description, out IntPtr buttonText, ref uint flags);
    }
    [ComVisible(true), Guid("6e793361-73c6-11d0-8469-00aa00442901"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEmptyVolumeCacheCallBack
    {
        [PreserveSig] int ScanProgress(ulong used, uint flags, [MarshalAs(UnmanagedType.LPWStr)] string? status);
        [PreserveSig] int PurgeProgress(ulong freed, ulong remaining, uint flags, [MarshalAs(UnmanagedType.LPWStr)] string? status);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class CleanupCallback(CancellationToken token, IProgress<string>? progress) : IEmptyVolumeCacheCallBack
    {
        public int ScanProgress(ulong used, uint flags, string? status) { if (!string.IsNullOrWhiteSpace(status)) progress?.Report(status); return token.IsCancellationRequested ? unchecked((int)0x80004004) : 0; }
        public int PurgeProgress(ulong freed, ulong remaining, uint flags, string? status) => ScanProgress(freed, flags, status);
    }
}
