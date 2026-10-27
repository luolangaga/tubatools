namespace TubaWinUi3.Services;

internal static class HardwareDisplayPolicy
{
    public static bool IsVirtualAdapter(string? name) => !string.IsNullOrWhiteSpace(name) &&
        new[] { "Microsoft Basic Render", "Microsoft Remote Display", "DDA Wrapper", "Idd Desk", "IddDesk", "IddCx",
            "GameViewer Virtual Display", "Honor Virtual Display", "Virtual Display", "Virtual GPU", "Virtual Adapter", "虚拟", "Remote Display Adapter" }
        .Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));

    public static string FormatGpuMemory(ulong dedicated, ulong shared)
    {
        const double gb = 1024d * 1024 * 1024;
        if (dedicated >= gb) return $"专用 {dedicated / gb:0.#} GB";
        if (shared > 0) return $"共享上限 {shared / gb:0.#} GB" + (dedicated > 0 ? $"，预留 {dedicated / (1024d * 1024):0} MB" : "");
        if (dedicated > 0) return $"专用 {dedicated / (1024d * 1024):0} MB";
        return "显存未能读取";
    }

    public static string FormatMemoryBandwidth(double gigabytesPerSecond) => $"{gigabytesPerSecond:0.#} GB/s";

    /// <summary>理论带宽 = 传输速率（MT/s）× 总位宽（bit）÷ 8，换算为 GB/s。</summary>
    public static double ComputeMemoryBandwidthGbs(int transferRateMt, int busBits)
        => transferRateMt > 0 && busBits > 0 ? transferRateMt * (busBits / 8d) / 1000d : 0;

    public static string FormatMemoryDevices(int count, int reportedMaximum, int memoryType)
    {
        // SMBIOS reports logical devices for soldered LPDDR; it cannot prove replaceable sockets.
        if (memoryType is 27 or 28 or 29 or 30 or 35)
            return $"（板载内存，系统报告 {count} 个内存设备）";
        return $"（系统报告 {count}/{Math.Max(count, reportedMaximum)} 个内存设备；可更换插槽请核对机型）";
    }
}
