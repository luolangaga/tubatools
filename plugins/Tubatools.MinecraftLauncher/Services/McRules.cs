using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tubatools.MinecraftLauncher.Services;

internal readonly record struct McOsInfo(string Name, bool Is32Bit, string Version)
{
    public static McOsInfo Current => new(
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux",
        !Environment.Is64BitOperatingSystem,
        Environment.OSVersion.Version.ToString());

    public string ArchName => Is32Bit ? "x86" : "x86_64";
}

/// <summary>PCL 的 rules 判断：os.name / os.arch / os.version 与 features。</summary>
internal static class McRules
{
    public static bool Check(JsonNode? rules, McOsInfo os)
    {
        if (rules is not JsonArray array) return true;

        bool allowed = false;
        foreach (var node in array)
        {
            if (node is not JsonObject rule) continue;
            bool matches = OsMatches(rule["os"] as JsonObject, os) && FeaturesMatch(rule["features"] as JsonObject);
            if (string.Equals(rule["action"]?.ToString(), "allow", StringComparison.Ordinal))
            {
                if (matches) allowed = true;
            }
            else if (matches)
            {
                allowed = false;
            }
        }

        return allowed;
    }

    private static bool OsMatches(JsonObject? os, McOsInfo info)
    {
        if (os is null) return true;

        bool result = true;
        var nameNode = os["name"];
        if (nameNode is not null)
        {
            var name = nameNode.ToString();
            if (name == "unknown")
            {
                // 不限制操作系统。
            }
            else if (!string.Equals(name, info.Name, StringComparison.OrdinalIgnoreCase))
            {
                result = false;
            }
            else if (name == "windows" && os["version"]?.ToString() is { Length: > 0 } version)
            {
                try { result = Regex.IsMatch(info.Version, version); }
                catch (ArgumentException) { result = true; }
            }
        }

        var archNode = os["arch"];
        if (archNode is not null)
            result = result && (string.Equals(archNode.ToString(), "x86", StringComparison.Ordinal) == info.Is32Bit);

        return result;
    }

    private static bool FeaturesMatch(JsonObject? features)
    {
        if (features is null) return true;
        // PCL：演示账号参数不启用；quickPlay 相关 feature 一律视为不匹配。
        var result = features["is_demo_user"] is null;
        if (features.Any(property => property.Key.Contains("quick_play", StringComparison.OrdinalIgnoreCase)))
            result = false;
        return result;
    }

    public static int ParseMajorJava(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return 0;
        var trimmed = version.Trim().TrimStart('"');
        var parts = trimmed.Split('.', '_', '-', '+');
        if (parts.Length == 0) return 0;
        if (parts[0] == "1" && parts.Length > 1 && int.TryParse(parts[1], out var legacy))
            return legacy;
        return int.TryParse(parts[0], out var major) ? major : 0;
    }

    public static Version ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new Version(0, 0);
        var match = Regex.Match(text, @"\d+(\.\d+){0,3}");
        return match.Success && System.Version.TryParse(match.Value, out var version)
            ? version
            : new Version(0, 0);
    }

    public static string FormatDate(DateTime value) =>
        value.Year < 2000 ? "未知" : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
