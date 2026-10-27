using System.Text.Json;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class StoreCatalogTests
{
    [Fact]
    public void CuratedCatalog_HasSixAgentsAndUniquePackages()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
                   "TubaWinUi3.WinUI3", "Metadata", "pcsetup_catalog.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var catalog = JsonSerializer.Deserialize<PcSetupCatalog>(File.ReadAllText(Path.Combine(
            directory!.FullName, "TubaWinUi3.WinUI3", "Metadata", "pcsetup_catalog.json")))!;
        var agents = Assert.Single(catalog.Categories, c => c.Name == "AI Agent");
        Assert.Equal(new[] { "Claude Code", "Codex", "OpenCode", "ZCode", "DeepSeek Harness", "WorkBuddy" },
            agents.Packages.Select(p => p.Name));
        Assert.All(agents.Packages, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.InstallNotes));
        });
        var packages = catalog.Categories.SelectMany(c => c.Packages).ToList();
        Assert.Equal(36, packages.Count);
        Assert.Equal(new[] { "Anthropic.ClaudeCode", "OpenAI.Codex", "SST.OpenCodeDesktop", "ZhipuAI.ZCode", "Tencent.WorkBuddy" },
            agents.Packages.Where(p => !p.IsDirectDownload).Select(p => p.Id));
        var deepseek = Assert.Single(agents.Packages, p => p.Name == "DeepSeek Harness");
        Assert.True(deepseek.CanInstall);
        Assert.True(deepseek.IsDirectDownload);
        Assert.Equal("https://download.deepseek.com/desktop/dsh-latest-windows-x64.exe", deepseek.DownloadUrl);
        Assert.Equal(packages.Count, packages.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(6, packages.Count(p => p.IsRecommended));
    }

    [Theory]
    [InlineData("https://download.deepseek.com/desktop/dsh-latest-windows-x64.exe", true)]
    [InlineData("file:///C:/setup.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(null, false)]
    public void DirectInstaller_OnlyUsesHttps(string? url, bool direct) =>
        Assert.Equal(direct, new StorePackage { DownloadUrl = url }.IsDirectDownload);

    [Theory]
    [InlineData("Anthropic.ClaudeCode")]
    [InlineData("OpenAI.Codex")]
    [InlineData("SST.OpenCodeDesktop")]
    [InlineData("ZhipuAI.ZCode")]
    [InlineData("Tencent.WorkBuddy")]
    public void InstallCommand_UsesExactWinGetIdAndNativeInstaller(string id)
    {
        var command = WingetStoreService.BuildInstallStartInfo(id);
        Assert.Equal("winget", command.FileName);
        Assert.False(command.UseShellExecute);
        Assert.Equal(new[] { "install", "--id", id, "--exact", "--source", "winget",
            "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity", "--silent" }, command.ArgumentList);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--all")]
    [InlineData("OpenAI.Codex --force")]
    public void InstallCommand_RejectsInvalidPackageId(string id) =>
        Assert.Throws<ArgumentException>(() => WingetStoreService.BuildInstallStartInfo(id));

    [Fact]
    public void UnlistedPackage_CannotStartAnInstall()
    {
        var package = new StorePackage { UnavailableReason = "WinGet 暂未收录" };
        Assert.False(package.CanInstall);
        Assert.Equal("暂未收录", package.ActionLabel);
    }
}
