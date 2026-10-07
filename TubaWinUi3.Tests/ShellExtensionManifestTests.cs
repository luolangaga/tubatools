using TubaWinUi3.ShellIntegration;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 「文件占用查看」Win11 新版右键菜单的清单一致性：
/// 四处清单（dev 注册脚本 / 商店打包脚本 / 旧商店脚本 / 源清单）都必须声明同一 CLSID、
/// 同一 DLL 路径与三类扩展（执行别名 / fileExplorerContextMenus / comServer）——
/// 漏一处或改一处名字都会让菜单静默失效（处理程序根本不会被加载）。
/// </summary>
public class ShellExtensionManifestTests
{
    private static readonly string[] ManifestFiles =
    [
        "run-msix.ps1",
        "build-msix-store.ps1",
        "build-store.ps1",
        Path.Combine("TubaWinUi3.WinUI3", "Package.appxmanifest"),
    ];

    [Fact]
    public void AllManifests_DeclareSameClsidDllAndExtensions()
    {
        foreach (var relativePath in ManifestFiles)
        {
            var path = Path.Combine(TestPaths.RepoRoot, relativePath);
            Assert.True(File.Exists(path), $"缺少清单文件：{relativePath}");

            var text = File.ReadAllText(path);
            Assert.Contains(FileLockShellMenuContract.CommandClsid, text, StringComparison.Ordinal);
            Assert.Contains($"Path=\"{FileLockShellMenuContract.ShellExtensionDllName}\"", text, StringComparison.Ordinal);
            Assert.Contains("windows.appExecutionAlias", text, StringComparison.Ordinal);
            Assert.Contains($"Alias=\"{FileLockShellMenuContract.ExecutionAlias}\"", text, StringComparison.Ordinal);
            Assert.Contains("windows.fileExplorerContextMenus", text, StringComparison.Ordinal);
            Assert.Contains("windows.comServer", text, StringComparison.Ordinal);
            Assert.Contains("com:SurrogateServer", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Contract_IsSharedIntoShellExtensionProject()
    {
        var csproj = File.ReadAllText(Path.Combine(
            TestPaths.RepoRoot, "TubaWinUI3.ShellExtension", "TubaWinUi3.ShellExtension.csproj"));
        Assert.Contains("FileLockShellMenuContract.cs", csproj, StringComparison.Ordinal);
        Assert.Contains("PublishAot", csproj, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(
            TestPaths.AppRoot, "Services", "FileLock", "FileLockShellMenuContract.cs")));
    }

    [Fact]
    public void MainProject_ThrottlesShellExtensionPublishOnIncludeShellExtension()
    {
        var csproj = File.ReadAllText(Path.Combine(TestPaths.AppRoot, "TubaWinUi3.csproj"));
        Assert.Contains("PublishShellExtension", csproj, StringComparison.Ordinal);
        Assert.Contains("IncludeShellExtension", csproj, StringComparison.Ordinal);
        // 商店 MSIX 发布必须带 -p:IncludeShellExtension=true（否则包内缺处理程序）
        var storeScript = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "build-msix-store.ps1"));
        Assert.Contains("-p:IncludeShellExtension=true", storeScript, StringComparison.Ordinal);
        Assert.Contains("TubaWinUi3.ShellExtension.dll not found", storeScript, StringComparison.Ordinal);
    }
}
