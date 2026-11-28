using Microsoft.Win32;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 注册表底层访问封装：按 hive/view 打开基键。
/// 与现有 RogueCleaner 的 RegistryHelper 手法一致（RegistryView 处理 WOW64）。
/// </summary>
public static class RegistryAccess
{
    public static RegistryKey OpenBase(RegHive hive, RegView view, bool writable)
    {
        var h = hive == RegHive.HKLM ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        var v = view switch
        {
            RegView.Registry64 => RegistryView.Registry64,
            RegView.Registry32 => RegistryView.Registry32,
            _ => RegistryView.Default,
        };
        return RegistryKey.OpenBaseKey(h, v);
    }

    /// <summary>打开子键；不存在返回 null。</summary>
    public static RegistryKey? OpenSubKey(RegHive hive, RegView view, string subKey, bool writable)
    {
        try
        {
            using var root = OpenBase(hive, view, writable);
            return root.OpenSubKey(subKey, writable);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读字符串值（容错）。</summary>
    public static string ReadString(RegistryKey? key, string name)
    {
        if (key is null) return "";
        try { return Convert.ToString(key.GetValue(name, "")) ?? ""; } catch { return ""; }
    }

    /// <summary>是否存在指定名称的值（大小写不敏感）。</summary>
    public static bool HasValue(RegistryKey? key, string name)
    {
        if (key is null) return false;
        try
        {
            return key.GetValueNames().Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>列子键名（容错）。</summary>
    public static string[] GetSubKeyNames(RegistryKey? key)
    {
        if (key is null) return [];
        try { return key.GetSubKeyNames(); } catch { return []; }
    }
}

