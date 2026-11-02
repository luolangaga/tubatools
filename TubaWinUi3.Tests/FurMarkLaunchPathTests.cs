using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public sealed class FurMarkLaunchPathTests
{
    [Fact]
    public void ChineseUserProfile_FallsBackToPrivateAsciiDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuba-ascii-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var aliasRoot = FurMarkLaunchPath.GetAliasRoot(Path.Combine(root, "用户"), root);
            Assert.All(aliasRoot, c => Assert.True(c <= 127));
            Assert.True(Directory.Exists(aliasRoot));
            Assert.Equal(aliasRoot, FurMarkLaunchPath.GetAliasRoot(Path.Combine(root, "用户"), root));
            Assert.False(Directory.Exists(Path.Combine(root, "用户")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void OtherTools_KeepOriginalPaths()
    {
        var path = @"C:\工具\Other.exe";
        Assert.Equal((path, (string?)@"C:\工具"), FurMarkLaunchPath.Prepare(path, @"C:\工具"));
    }

    [Theory]
    [InlineData("FurMark_GUI.exe")]
    [InlineData("FURMARK.EXE")]
    [InlineData("_fm2-gui.exe")]
    public void ChineseDirectory_UsesPersistentAliasWithoutCopying(string filename)
    {
        var target = Path.Combine(Path.GetTempPath(), "tuba-furmark-" + Guid.NewGuid().ToString("N"), "烤鸡工具");
        Directory.CreateDirectory(target);
        var original = Path.Combine(target, filename);
        File.WriteAllText(original, "not an executable; never launched");
        string? alias = null;
        try
        {
            var launch = FurMarkLaunchPath.Prepare(original, target);
            alias = launch.WorkingDirectory;
            Assert.All(launch.ExePath, c => Assert.True(c <= 127));
            Assert.All(alias!, c => Assert.True(c <= 127));
            Assert.Equal(target, new DirectoryInfo(alias!).ResolveLinkTarget(false)!.FullName);
            Assert.Equal(launch, FurMarkLaunchPath.Prepare(original, target));
            // Write-through proves that settings/logs remain in the original directory.
            File.WriteAllText(Path.Combine(alias!, "config.txt"), "saved");
            Assert.Equal("saved", File.ReadAllText(Path.Combine(target, "config.txt")));
            File.WriteAllText(original, "updated");
            Assert.Equal("updated", File.ReadAllText(launch.ExePath));
        }
        finally
        {
            if (alias is not null) Directory.Delete(alias); // unlink only
            Assert.True(File.Exists(original));
            Directory.Delete(Path.GetDirectoryName(target)!, true);
        }
    }

    [Fact]
    public void AliasCollision_DoesNotReplaceExistingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuba-junction-test-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "中文");
        Directory.CreateDirectory(target);
        string? alias = null;
        try
        {
            alias = FurMarkLaunchPath.AliasDirectory(target, root);
            Directory.Delete(alias);
            Directory.CreateDirectory(alias);
            File.WriteAllText(Path.Combine(alias, "keep.txt"), "keep");
            Assert.Throws<IOException>(() => FurMarkLaunchPath.AliasDirectory(target, root));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(alias, "keep.txt")));
        }
        finally
        {
            if (alias is not null && (File.GetAttributes(alias) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(alias);
            Directory.Delete(root, true);
        }
    }
}
