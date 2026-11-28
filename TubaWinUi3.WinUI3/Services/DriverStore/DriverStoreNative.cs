using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.Services.DriverStore;

// 根据 Windows SDK / Microsoft Learn 的公开接口独立实现；未引用第三方驱动管理器代码。
// 本机驱动库走 pnputil（进程外读 DriverStore 数据库），只有离线映像才用 DISM API——原因见 EnumerateOnline。
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
        if (offlineRoot is null) return EnumerateOnline();
        offlineRoot = Path.GetFullPath(offlineRoot);
        if (!Directory.Exists(Path.Combine(offlineRoot, "Windows", "System32", "config")))
            throw new ArgumentException("请选择离线 Windows 映像根目录（其中应包含 Windows 文件夹）");
        if (offlineRoot.TrimEnd('\\').Equals(Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("当前运行的 Windows 请使用「本机驱动库」");
        return EnumerateWithDism(offlineRoot);
    }

    /// <summary>
    /// 本机驱动库枚举：pnputil 是官方 CLI，直接读 DriverStore 数据库，进程外执行、不碰 COM。
    /// 不能用 DISM API：MSIX 打包进程里 DismInitialize 返回 0x80040154「没有注册类」
    /// （它内部要按包身份创建组件进程，注册类解析不到），servicing 栈损坏的系统上
    /// DISM.exe 也报同一个错——这正是 1.9.0 用户报告的 bug，所以在线枚举不再经过 DISM。
    /// pnputil 不提供「启动关键」标记（DISM 的 BootCritical 只在离线路径保留）：启动关键驱动
    /// 始终绑定在场设备，已被 InUse 挡住；残留的旧版本不属于任何在场设备，删除才是本页的目的。
    /// </summary>
    private static List<DriverPackage> EnumerateOnline()
    {
        Exception pnputilError;
        try
        {
            var packages = DriverStoreService.ParseEnumDrivers(ReadPnputilEnumDrivers());
            if (packages.Count > 0) return packages;
            pnputilError = new InvalidOperationException("pnputil 没有返回任何驱动包");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { pnputilError = ex; }

        // 仅作兜底：个别被策略锁死 pnputil 的机器上 DISM 仍可能可用。
        try { return EnumerateWithDism(null); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"无法枚举本机驱动库：pnputil 失败（{pnputilError.Message}）；DISM 回退也失败（{ex.Message}）", ex);
        }
    }

    private static string ReadPnputilEnumDrivers()
    {
        var encoding = ConsoleEncoding();
        var psi = new ProcessStartInfo
        {
            FileName = SystemExecutable("pnputil.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 控制台程序按控制台代码页输出（默认 = 系统 OEM 代码页）：中文系统 GBK，开了 UTF-8 beta 的系统是 65001。
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        };
        psi.ArgumentList.Add("/enum-drivers");

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("无法启动 pnputil.exe");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            throw new InvalidOperationException($"无法启动 pnputil.exe：{ex.Message}", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            throw new TimeoutException("pnputil 枚举驱动包超过 60 秒");
        }
        var output = stdout.GetAwaiter().GetResult();
        var error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"pnputil 枚举驱动包失败（退出码 {process.ExitCode}）：{FirstNonEmpty(error, output)}");
        return output;
    }

    /// <summary>
    /// 取系统目录里的可执行文件：32 位进程在 64 位系统上会把 System32 重定向到没有 pnputil.exe 的
    /// SysWOW64，必须靠 WOW64 专用别名 Sysnative 取到真实的 System32；ARM64 上跑 x64 模拟版同理。
    /// File.Exists 也按当前进程的目录重定向判断，所以逐个候选试即可。
    /// </summary>
    private static string SystemExecutable(string name)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var folder in new[] { "System32", "Sysnative", "SysWOW64" })
        {
            var candidate = Path.Combine(windows, folder, name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException($"系统目录中找不到 {name}");
    }

    private static Encoding ConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var codePage = (int)GetOEMCP();
            if (codePage > 0 && codePage != 65001) return Encoding.GetEncoding(codePage);
        }
        catch (Exception) { }
        return Encoding.UTF8; // 65001 / 取不到 OEM 代码页时按 UTF-8（UTF-8 beta 系统就是这个口径）
    }

    private static string FirstNonEmpty(string first, string second)
    {
        foreach (var text in new[] { first, second })
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
            }
        return "（无输出）";
    }

    /// <summary>离线映像的驱动库只有 DISM 能读；本机驱动库不再走这里。</summary>
    private static List<DriverPackage> EnumerateWithDism(string? offlineRoot)
    {
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
                var storeInf = Text(d.OriginalFileName);
                // 离线 DISM 返回映像内的绝对路径；不在映像内就丢弃，绝不回退到宿主机同名驱动。
                // 本机兜底时暂不填路径，由 DriverStoreService.AssignRepositoryInfo 按 FileRepository 还原。
                if (offlineRoot is null || !Path.IsPathFullyQualified(storeInf)
                    || !storeInf.StartsWith(offlineRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) storeInf = "";
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

    private const int ClassNotRegistered = unchecked((int)0x80040154);   // REGDB_E_CLASSNOTREG
    private const int ElevationRequired = unchecked((int)0x800702E4);    // ERROR_ELEVATION_REQUIRED

    private static void Check(int hr)
    {
        if (hr >= 0) return;
        var detail = "";
        if (DismGetLastErrorMessage(out var error) >= 0 && error != IntPtr.Zero)
        {
            try { detail = Text(Marshal.ReadIntPtr(error)); } finally { DismDelete(error); }
        }
        var hint = hr switch
        {
            ClassNotRegistered => "；DISM 组件没有注册（MSIX 打包进程或 servicing 栈损坏的系统上会出现，离线映像需要先修复系统）",
            ElevationRequired => "；需要以管理员身份运行",
            _ => ""
        };
        throw new InvalidOperationException($"Windows DISM API 失败（0x{hr:X8}）：{detail}{hint}", Marshal.GetExceptionForHR(hr));
    }

    [DllImport("kernel32.dll")] private static extern uint GetOEMCP();
    [DllImport("dismapi.dll", CharSet = CharSet.Unicode)] private static extern int DismInitialize(int level, string? log, string? scratch);
    [DllImport("dismapi.dll")] private static extern int DismShutdown();
    [DllImport("dismapi.dll", CharSet = CharSet.Unicode)] private static extern int DismOpenSession(string image, string? windows, string? drive, out uint session);
    [DllImport("dismapi.dll")] private static extern int DismCloseSession(uint session);
    [DllImport("dismapi.dll")] private static extern int DismGetDrivers(uint session, [MarshalAs(UnmanagedType.Bool)] bool all, out IntPtr drivers, out uint count);
    [DllImport("dismapi.dll")] private static extern int DismDelete(IntPtr data);
    [DllImport("dismapi.dll")] private static extern int DismGetLastErrorMessage(out IntPtr error);
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
