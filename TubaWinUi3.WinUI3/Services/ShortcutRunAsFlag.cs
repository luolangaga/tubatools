namespace TubaWinUi3.Services;

/// <summary>
/// .lnk 的「以管理员身份运行」标志（MS-SHLLINK LinkFlags.SLDF_RUNAS_USER = 0x2000）。
/// WScript.Shell 的 IWshShortcut 不暴露该标志，且 Save() 会把它清掉，因此快捷方式写完后
/// 按规范补丁文件字节：LinkFlags DWORD 位于偏移 0x14（小端），即字节 0x15 |= 0x20。
/// 带标志后 Explorer 直接弹 UAC 并以管理员单进程启动目标，省掉主程序内部的提权重启
/// （Program.Main 的前置提权闸门只是直接双击 exe 时的兜底）。
/// 只用于目标为需要管理员的主 exe 的快捷方式（主程序自身 / 内置工具 --open-builtin）；
/// 第三方工具快捷方式不加——工具自己的清单决定其权限，强加 RunAs 会改变其行为。
/// </summary>
internal static class ShortcutRunAsFlag
{
    internal const int LinkFlagsOffset = 0x14;
    internal const byte RunAsUserByte = 0x20; // SLDF_RUNAS_USER(0x2000) 的小端第二字节

    /// <summary>ShellLinkHeader 校验：HeaderSize DWORD = 0x4C，且文件足够容纳 LinkFlags。</summary>
    internal static bool IsValidHeader(byte[] lnk) =>
        lnk is { Length: >= LinkFlagsOffset + 4 }
        && lnk[0] == 0x4C && lnk[1] == 0 && lnk[2] == 0 && lnk[3] == 0;

    internal static bool IsSet(byte[] lnk) =>
        IsValidHeader(lnk) && (lnk[LinkFlagsOffset + 1] & RunAsUserByte) != 0;

    /// <summary>纯逻辑（可单测）：置位 RunAsUser。返回是否发生了修改；非法/已置位返回 false。</summary>
    internal static bool TrySet(byte[] lnk)
    {
        if (!IsValidHeader(lnk) || IsSet(lnk)) return false;
        lnk[LinkFlagsOffset + 1] |= RunAsUserByte;
        return true;
    }

    /// <summary>
    /// 读取 → 补丁 → 写回 → 回读校验（与 CreateDesktopShortcut 的写后校验同口径）。
    /// 失败静默返回 false：快捷方式仍可用，只是退回程序内提权路径，绝不阻断创建。
    /// </summary>
    internal static bool TrySetOnFile(string shortcutPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(shortcutPath);
            if (!IsValidHeader(bytes)) return false;
            if (IsSet(bytes)) return true;
            if (!TrySet(bytes)) return false;
            File.WriteAllBytes(shortcutPath, bytes);
            return IsSet(File.ReadAllBytes(shortcutPath));
        }
        catch
        {
            return false;
        }
    }
}