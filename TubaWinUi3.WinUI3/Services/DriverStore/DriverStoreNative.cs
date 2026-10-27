using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.Services.DriverStore;

// 根据 Windows SDK / Microsoft Learn 的公开接口独立实现；未引用第三方驱动管理器代码。
// DISM 的结构为 pack(1)，SetupAPI 的结构使用默认对齐。原生内存均在 finally 中释放。
internal static class DriverStoreNative
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DismPackage
    {
        public IntPtr PublishedName, OriginalFileName;
        public int InBox;
        public IntPtr CatalogFile, ClassName, ClassGuid, ClassDescription;
        public int BootCritical, Signature;
        public IntPtr Provider;
        public SystemTime Date;
        public uint Major, Minor, Build, Revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid guid, uint id) { public Guid Guid = guid; public uint Id = id; }

    private static string Text(IntPtr p) => Marshal.PtrToStringUni(p) ?? "";
    internal static int PackageStructSize => Marshal.SizeOf<DismPackage>();

    internal static List<DriverPackage> Enumerate(string? offlineRoot)
    {
        if (offlineRoot is not null)
        {
            offlineRoot = Path.GetFullPath(offlineRoot);
            if (!Directory.Exists(Path.Combine(offlineRoot, "Windows", "System32", "config")))
                throw new ArgumentException("请选择离线 Windows 映像根目录（其中应包含 Windows 文件夹）");
            if (offlineRoot.TrimEnd('\\').Equals(Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("当前运行的 Windows 请使用「本机驱动库」");
        }
        Check(DismInitialize(0, null, null));
        uint session = 0;
        IntPtr buffer = IntPtr.Zero;
        var opened = false;
        try
        {
            Check(DismOpenSession(offlineRoot ?? "DISM_{53BFAE52-B167-4E2F-A258-0A37B57FF845}", null, null, out session));
            opened = true;
            Check(DismGetDrivers(session, false, out buffer, out var count));
            var result = new List<DriverPackage>();
            for (uint i = 0; i < count; i++)
            {
                var d = Marshal.PtrToStructure<DismPackage>(IntPtr.Add(buffer, checked((int)i * Marshal.SizeOf<DismPackage>())));
                var name = Path.GetFileName(Text(d.PublishedName));
                if (d.InBox != 0 || !DriverStoreService.IsPublishedInf(name)) continue;
                var storeInf = offlineRoot is null ? GetStorePath(name) : Text(d.OriginalFileName);
                // 离线 DISM 应返回映像内的绝对路径；绝不回退到宿主机同名驱动。
                if (offlineRoot is not null && (!Path.IsPathFullyQualified(storeInf)
                    || !storeInf.StartsWith(offlineRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) storeInf = "";
                DateTime? date = null;
                try { date = new DateTime(d.Date.Year, d.Date.Month, d.Date.Day); } catch (ArgumentOutOfRangeException) { }
                result.Add(new DriverPackage
                {
                    InfName = name, OriginalInfName = Path.GetFileName(Text(d.OriginalFileName)),
                    Provider = Text(d.Provider), ClassName = Text(d.ClassName), ClassGuid = Text(d.ClassGuid),
                    Version = $"{d.Major}.{d.Minor}.{d.Build}.{d.Revision}", Date = date,
                    BootCritical = d.BootCritical != 0, IsOffline = offlineRoot is not null, StoreInfPath = storeInf,
                    Signer = d.Signature switch { 0 => "未知", 1 => "未签名", 2 => "已签名", _ => "未知" }
                });
            }
            return result;
        }
        finally
        {
            if (buffer != IntPtr.Zero) DismDelete(buffer);
            if (opened) DismCloseSession(session);
            DismShutdown();
        }
    }

    internal static string GetStorePath(string name)
    {
        var path = new StringBuilder(32768);
        return SetupGetInfDriverStoreLocationW(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", name),
            IntPtr.Zero, null, path, (uint)path.Capacity, out _) ? path.ToString() : "";
    }

    internal static Dictionary<string, List<string>> GetDevices(CancellationToken ct)
    {
        // 不带 DIGCF_PRESENT：拔出的设备仍依赖其驱动。
        var handle = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, 4 /* DIGCF_ALLCLASSES */);
        if (handle == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; ; i++)
            {
                ct.ThrowIfCancellationRequested();
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(handle, i, ref info))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                var inf = Property(handle, ref info, new(new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"), 5));
                if (inf.Length == 0) continue; // 没有驱动的设备没有此属性
                var guid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
                var name = Property(handle, ref info, new(guid, 14));
                if (name.Length == 0) name = Property(handle, ref info, new(guid, 2));
                if (name.Length == 0) name = $"设备 #{info.DevInst}";
                // CONFIGRET != CR_SUCCESS 通常是非在场设备；仅用于显示，不影响关联保护。
                if (CM_Get_DevNode_Status(out _, out _, info.DevInst, 0) != 0) name += "（未连接）";
                if (!result.TryGetValue(inf, out var list)) result[inf] = list = [];
                list.Add(name);
            }
            return result;
        }
        finally { SetupDiDestroyDeviceInfoList(handle); }
    }

    private static string Property(IntPtr handle, ref DeviceInfo info, PropertyKey key)
    {
        var data = new byte[8192];
        if (SetupDiGetDevicePropertyW(handle, ref info, ref key, out var type, data, (uint)data.Length, out var needed, 0))
            return type == 18 ? Encoding.Unicode.GetString(data, 0, (int)needed).TrimEnd('\0') : "";
        var error = Marshal.GetLastWin32Error();
        if (error is 1168 or 13) return ""; // 属性未设置
        if (error == 122)
        {
            data = new byte[needed];
            if (SetupDiGetDevicePropertyW(handle, ref info, ref key, out type, data, needed, out needed, 0))
                return type == 18 ? Encoding.Unicode.GetString(data, 0, (int)needed).TrimEnd('\0') : "";
            error = Marshal.GetLastWin32Error();
        }
        throw new Win32Exception(error, "读取设备关联失败；已停止扫描以避免错误清理");
    }

    internal static DriverOpResult Delete(string name) => SetupUninstallOEMInfW(name, 0, IntPtr.Zero)
        ? DriverOpResult.Ok() : Error("删除");

    internal static DriverOpResult Install(string path)
    {
        var success = DiInstallDriverW(IntPtr.Zero, path, 0, out var reboot);
        return success ? new(true, reboot, "安装完成") : Error("安装");
    }

    private static DriverOpResult Error(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return DriverOpResult.Fail($"{operation}失败（0x{error:X8}）：{new Win32Exception(error).Message}");
    }
    private static void Check(int hr)
    {
        if (hr >= 0) return;
        var detail = "";
        if (DismGetLastErrorMessage(out var error) >= 0 && error != IntPtr.Zero)
        {
            try { detail = Text(Marshal.ReadIntPtr(error)); } finally { DismDelete(error); }
        }
        throw new InvalidOperationException($"Windows DISM API 失败（0x{hr:X8}）：{detail}", Marshal.GetExceptionForHR(hr));
    }

    [DllImport("dismapi.dll", CharSet = CharSet.Unicode)] private static extern int DismInitialize(int level, string? log, string? scratch);
    [DllImport("dismapi.dll")] private static extern int DismShutdown();
    [DllImport("dismapi.dll", CharSet = CharSet.Unicode)] private static extern int DismOpenSession(string image, string? windows, string? drive, out uint session);
    [DllImport("dismapi.dll")] private static extern int DismCloseSession(uint session);
    [DllImport("dismapi.dll")] private static extern int DismGetDrivers(uint session, [MarshalAs(UnmanagedType.Bool)] bool all, out IntPtr drivers, out uint count);
    [DllImport("dismapi.dll")] private static extern int DismDelete(IntPtr data);
    [DllImport("dismapi.dll")] private static extern int DismGetLastErrorMessage(out IntPtr error);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool SetupGetInfDriverStoreLocationW(string name, IntPtr platform, string? locale, StringBuilder path, uint size, out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref DeviceInfo info, ref PropertyKey key, out uint type, byte[] buffer, uint size, out uint required, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool SetupUninstallOEMInfW(string name, uint flags, IntPtr reserved);
    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool DiInstallDriverW(IntPtr hwnd, string path, uint flags, [MarshalAs(UnmanagedType.Bool)] out bool reboot);
}
