using System.Globalization;
using Microsoft.Win32;

namespace Tubatools.Windows11Optimizer;

internal static class RegistryAccess
{
    /// <summary>Always use the 64-bit view so an x86 toolbox build does not hit Wow6432Node redirection.</summary>
    public static object? Read(RegistryTarget target)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(target.Hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(target.Path, false);
            return key?.GetValue(target.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch
        {
            return null;
        }
    }

    public static bool Exists(RegistryTarget target)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(target.Hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(target.Path, false);
            return key?.GetValueNames().Contains(target.Name, StringComparer.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    public static void Write(RegistryTarget target, object? value)
    {
        using var baseKey = RegistryKey.OpenBaseKey(target.Hive, RegistryView.Registry64);
        if (value is null)
        {
            using var existing = baseKey.OpenSubKey(target.Path, true);
            existing?.DeleteValue(target.Name, false);
            return;
        }

        using var key = baseKey.CreateSubKey(target.Path, true)
            ?? throw new InvalidOperationException($"无法打开注册表项：{target.Hive}\\{target.Path}");
        key.SetValue(target.Name, ConvertForKind(target.Kind, value), target.Kind);
    }

    public static bool Matches(RegistryTarget target, object? expected)
    {
        var actual = Read(target);
        if (expected is null) return actual is null;
        if (actual is null) return false;
        if (expected is int or long or uint)
            return Convert.ToInt64(actual, CultureInfo.InvariantCulture) == Convert.ToInt64(expected, CultureInfo.InvariantCulture);
        return string.Equals(
            Convert.ToString(actual, CultureInfo.InvariantCulture),
            Convert.ToString(expected, CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string Describe(RegistryTarget target)
    {
        var hive = target.Hive switch
        {
            RegistryHive.CurrentUser => "HKCU",
            RegistryHive.LocalMachine => "HKLM",
            RegistryHive.ClassesRoot => "HKCR",
            _ => target.Hive.ToString()
        };
        return $@"{hive}\{target.Path}\{target.Name}";
    }

    private static object ConvertForKind(RegistryValueKind kind, object value) => kind switch
    {
        RegistryValueKind.DWord => unchecked((int)Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        RegistryValueKind.String or RegistryValueKind.ExpandString => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        _ => value
    };
}
