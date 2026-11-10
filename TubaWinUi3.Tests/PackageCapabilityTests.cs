using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 摄像头采集依赖 MSIX 清单里的 webcam 能力；三个打包脚本都会重新生成清单，
/// 任何一处漏掉都会让打包版摄像头静默失败，这里锁死四处保持一致。
/// </summary>
public class PackageCapabilityTests
{
    [Theory]
    [InlineData("TubaWinUi3.WinUI3/Package.appxmanifest")]
    [InlineData("run-msix.ps1")]
    [InlineData("build-msix-store.ps1")]
    [InlineData("build-store.ps1")]
    public void Manifests_DeclareWebcamAndMicrophone(string relativePath)
    {
        var path = Path.Combine(
            TestPaths.RepoRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"清单/脚本不存在：{path}");

        var text = File.ReadAllText(path);
        Assert.Contains("DeviceCapability Name=\"microphone\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeviceCapability Name=\"webcam\"", text, StringComparison.OrdinalIgnoreCase);
    }
}
