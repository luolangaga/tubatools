using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

/// <summary>
/// 从合并清单的 Installers 中挑出「下载后能直接运行」的那个：
/// 架构优先本机原生 → 中性 → x86 兼容层；类型优先 msi/wix/burn → inno/nullsoft → exe；
/// zip / portable / msix / msstore 这类需要额外解包或包身份的不在此处理（交给本机 winget 回退）。
/// </summary>
internal static class WingetInstallerSelector
{
    private static readonly HashSet<string> MsiFamily =
        new(StringComparer.OrdinalIgnoreCase) { "msi", "wix", "burn" };

    private static readonly HashSet<string> ExeFamily =
        new(StringComparer.OrdinalIgnoreCase) { "exe", "inno", "nullsoft" };

    /// <summary>可从官方直链下载后直接 shell 运行的安装程序类型。</summary>
    internal static bool IsRunnableInstallerType(string? installerType) =>
        string.IsNullOrEmpty(installerType)
        || MsiFamily.Contains(installerType)
        || ExeFamily.Contains(installerType);

    internal static WingetInstallerEntry? Select(
        IReadOnlyList<WingetInstallerEntry> installers,
        Architecture machineArchitecture)
    {
        WingetInstallerEntry? best = null;
        var bestScore = int.MaxValue;

        foreach (var installer in installers)
        {
            if (string.IsNullOrWhiteSpace(installer.InstallerUrl)
                || !Uri.TryCreate(installer.InstallerUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
                continue;

            if (!IsRunnableInstallerType(installer.InstallerType))
                continue;

            var archScore = ArchitectureScore(installer.Architecture, machineArchitecture);
            if (archScore < 0) continue;

            var typeScore = TypeScore(installer.InstallerType);
            var scopeScore = string.Equals(installer.Scope, "user", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            var score = archScore * 100 + typeScore * 10 + scopeScore;
            if (score >= bestScore) continue;

            best = installer;
            bestScore = score;
        }

        return best;
    }

    /// <summary>0 = 本机原生；1 = 中性/未声明；2 = x86 兼容层；-1 = 不兼容（如 x64 机器上的 arm64 包）。</summary>
    private static int ArchitectureScore(string? architecture, Architecture machine)
    {
        if (string.IsNullOrWhiteSpace(architecture)
            || architecture.Equals("neutral", StringComparison.OrdinalIgnoreCase))
            return 1;

        var isArm64 = machine == Architecture.Arm64;
        if (architecture.Equals("arm64", StringComparison.OrdinalIgnoreCase))
            return isArm64 ? 0 : -1;
        if (architecture.Equals("x64", StringComparison.OrdinalIgnoreCase))
            return isArm64 ? -1 : 0;
        if (architecture.Equals("x86", StringComparison.OrdinalIgnoreCase))
            return machine == Architecture.X86 ? 0 : 2;
        if (architecture.Equals("arm", StringComparison.OrdinalIgnoreCase))
            return isArm64 ? 2 : -1;

        return 1;
    }

    private static int TypeScore(string? installerType)
    {
        if (string.IsNullOrEmpty(installerType)) return 3;
        if (MsiFamily.Contains(installerType)) return 0;
        if (installerType.Equals("inno", StringComparison.OrdinalIgnoreCase)
            || installerType.Equals("nullsoft", StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    /// <summary>从安装包 URL 推断保存文件名；无明显扩展名时按安装程序类型补全。</summary>
    internal static string BuildFileName(string installerUrl, string packageId, string? installerType)
    {
        string? candidate = null;
        try
        {
            var path = new Uri(installerUrl).AbsolutePath;
            candidate = Uri.UnescapeDataString(Path.GetFileName(path));
        }
        catch { }

        var fallbackExtension = installerType?.ToLowerInvariant() switch
        {
            "msi" or "wix" or "burn" => ".msi",
            _ => ".exe"
        };

        if (string.IsNullOrWhiteSpace(candidate))
            candidate = packageId + fallbackExtension;

        candidate = SanitizeFileName(candidate);
        if (!Path.HasExtension(candidate))
            candidate += fallbackExtension;

        return candidate;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrEmpty(result) ? "installer.exe" : result;
    }
}
