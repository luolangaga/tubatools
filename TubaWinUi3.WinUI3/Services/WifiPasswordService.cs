using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace TubaWinUi3.Services;

/// <summary>Read saved profiles through WLAN, independent of Windows display language.</summary>
public static class WifiPasswordService
{
    public static Task<List<WifiNetwork>> GetNetworksAsync() => Task.Run(ReadNetworks);
    public static async Task<WifiNetwork?> GetCurrentNetworkAsync() =>
        (await GetNetworksAsync()).FirstOrDefault(n => n.IsConnected);

    private static List<WifiNetwork> ReadNetworks()
    {
        Check(WlanOpenHandle(2, IntPtr.Zero, out _, out var handle));
        try
        {
            Check(WlanEnumInterfaces(handle, IntPtr.Zero, out var interfaces));
            try
            {
                var result = new List<WifiNetwork>();
                int count = Marshal.ReadInt32(interfaces);
                for (int i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<InterfaceInfo>(IntPtr.Add(interfaces, 8 + i * Marshal.SizeOf<InterfaceInfo>()));
                    var id = info.Id;
                    string? connectedProfile = null;
                    if (WlanQueryInterface(handle, ref id, 7, IntPtr.Zero, out _, out var connection, out _) == 0)
                    {
                        try { connectedProfile = Marshal.PtrToStringUni(IntPtr.Add(connection, 8)); }
                        finally { WlanFreeMemory(connection); }
                    }
                    Check(WlanGetProfileList(handle, ref id, IntPtr.Zero, out var profiles));
                    try
                    {
                        for (int j = 0; j < Marshal.ReadInt32(profiles); j++)
                        {
                            var name = Marshal.PtrToStringUni(IntPtr.Add(profiles, 8 + j * 516))!;
                            uint flags = 4; // WLAN_PROFILE_GET_PLAINTEXT_KEY; never write keys to disk.
                            var error = WlanGetProfile(handle, ref id, name, IntPtr.Zero, out var xml, ref flags, out _);
                            if (error != 0)
                            {
                                // Some callers may read the profile but lack permission to read its key.
                                flags = 0;
                                Check(WlanGetProfile(handle, ref id, name, IntPtr.Zero, out xml, ref flags, out _));
                            }
                            try
                            {
                                var network = ParseProfile(Marshal.PtrToStringUni(xml)!);
                                network.ProfileName = name;
                                network.Adapter = info.Description;
                                network.IsConnected = name == connectedProfile;
                                result.Add(network);
                            }
                            finally { WlanFreeMemory(xml); }
                        }
                    }
                    finally { WlanFreeMemory(profiles); }
                }
                return result;
            }
            finally { WlanFreeMemory(interfaces); }
        }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
    }

    internal static WifiNetwork ParseProfile(string xml)
    {
        var root = XElement.Parse(xml);
        var ns = root.Name.Namespace;
        var security = root.Element(ns + "MSM")?.Element(ns + "security");
        var key = security?.Element(ns + "sharedKey");
        var auth = security?.Element(ns + "authEncryption");
        var material = key?.Element(ns + "keyMaterial")?.Value ?? "";
        bool encrypted = string.Equals(key?.Element(ns + "protected")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        return new WifiNetwork
        {
            Ssid = root.Element(ns + "SSIDConfig")?.Element(ns + "SSID")?.Element(ns + "name")?.Value
                ?? root.Element(ns + "name")?.Value ?? "",
            Authentication = auth?.Element(ns + "authentication")?.Value ?? "未知",
            Cipher = auth?.Element(ns + "encryption")?.Value ?? "未知",
            Password = encrypted ? "" : material,
            HasPassword = !encrypted && material.Length > 0,
            AutoConnect = root.Element(ns + "connectionMode")?.Value == "auto",
            Hidden = root.Element(ns + "SSIDConfig")?.Element(ns + "nonBroadcast")?.Value == "true"
        };
    }

    private static void Check(uint error)
    {
        if (error != 0) throw new Win32Exception((int)error);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InterfaceInfo
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public int State;
    }
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetProfileList(IntPtr handle, ref Guid id, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanGetProfile(IntPtr handle, ref Guid id, string name, IntPtr reserved, out IntPtr xml, ref uint flags, out uint access);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, int opcode, IntPtr reserved, out uint size, out IntPtr data, out int type);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
}

public sealed class WifiNetwork
{
    public string Ssid { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string Adapter { get; set; } = "";
    public string Password { get; set; } = "";
    public bool HasPassword { get; set; }
    public string Authentication { get; set; } = "";
    public string Cipher { get; set; } = "";
    public bool IsConnected { get; set; }
    public bool AutoConnect { get; set; }
    public bool Hidden { get; set; }
    public bool IsOpen => Authentication.Equals("open", StringComparison.OrdinalIgnoreCase) && Cipher.Equals("none", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => $"{Ssid}，{Status}，{SecurityLabel}";
    public string Status => IsConnected ? "已连接" : "已保存";
    public string SecurityLabel => IsOpen ? "开放网络 · 无需密码" : Authentication + (HasPassword ? " · 密码已保存" : " · 无可读取的共享密码");
    public bool CanShare => IsOpen || (HasPassword && (Authentication.StartsWith("WPA", StringComparison.OrdinalIgnoreCase) || Authentication.Equals("shared", StringComparison.OrdinalIgnoreCase)) && !Authentication.Contains("SAE", StringComparison.OrdinalIgnoreCase));
    internal string QrPayload()
    {
        if (!CanShare) throw new InvalidOperationException("此网络不支持通用 WiFi 二维码。");
        static string Escape(string value) => value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace(":", "\\:").Replace("\"", "\\\"");
        var type = IsOpen ? "nopass" : Authentication.Equals("shared", StringComparison.OrdinalIgnoreCase) ? "WEP" : "WPA";
        return $"WIFI:T:{type};S:{Escape(Ssid)};P:{Escape(Password)};H:{Hidden.ToString().ToLowerInvariant()};;";
    }
}
