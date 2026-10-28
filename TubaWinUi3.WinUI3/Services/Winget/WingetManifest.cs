using YamlDotNet.Serialization;

namespace TubaWinUi3.Services;

/// <summary>
/// WinGet 官方源（cdn.winget.microsoft.com/cache）返回的合并清单（ManifestType: merged）。
/// 只声明实际用到的字段，其余字段由 YamlDotNet 忽略。
/// </summary>
internal sealed class WingetMergedManifest
{
    [YamlMember(Alias = "PackageIdentifier")] public string? PackageIdentifier { get; set; }
    [YamlMember(Alias = "PackageName")] public string? PackageName { get; set; }
    [YamlMember(Alias = "Publisher")] public string? Publisher { get; set; }
    [YamlMember(Alias = "PackageVersion")] public string? PackageVersion { get; set; }
    [YamlMember(Alias = "Moniker")] public string? Moniker { get; set; }
    [YamlMember(Alias = "ShortDescription")] public string? ShortDescription { get; set; }
    [YamlMember(Alias = "InstallerType")] public string? InstallerType { get; set; }
    [YamlMember(Alias = "Installers")] public List<WingetInstallerEntry> Installers { get; set; } = [];
}

/// <summary>合并清单中的一个安装包条目。</summary>
internal sealed class WingetInstallerEntry
{
    [YamlMember(Alias = "Architecture")] public string? Architecture { get; set; }
    [YamlMember(Alias = "InstallerType")] public string? InstallerType { get; set; }
    [YamlMember(Alias = "NestedInstallerType")] public string? NestedInstallerType { get; set; }
    [YamlMember(Alias = "InstallerUrl")] public string? InstallerUrl { get; set; }
    [YamlMember(Alias = "InstallerSha256")] public string? InstallerSha256 { get; set; }
    [YamlMember(Alias = "Scope")] public string? Scope { get; set; }
    [YamlMember(Alias = "InstallerLocale")] public string? InstallerLocale { get; set; }
}

/// <summary>
/// 包级版本数据（*.mszyml，MSZIP 压缩的 YAML）：列出该包全部版本的清单相对路径与 SHA-256。
/// </summary>
internal sealed class WingetVersionDataManifest
{
    [YamlMember(Alias = "sV")] public string? SchemaVersion { get; set; }
    [YamlMember(Alias = "vD")] public List<WingetVersionEntry> Versions { get; set; } = [];
}

internal sealed class WingetVersionEntry
{
    [YamlMember(Alias = "v")] public string? Version { get; set; }
    [YamlMember(Alias = "rP")] public string? RelativePath { get; set; }
    [YamlMember(Alias = "s256H")] public string? Sha256 { get; set; }
}

/// <summary>解析后的安装计划：纯数据，便于单测。</summary>
public sealed record WingetInstallPlan(
    string PackageId,
    string Version,
    string InstallerUrl,
    string InstallerSha256,
    string FileName,
    string InstallerType,
    string Architecture);
