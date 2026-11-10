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

    [Fact]
    public void NestedJunction_UsesPhysicalTargetAndKeepsSiblingDllsVisible()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuba-physical-path-" + Guid.NewGuid().ToString("N"));
        var physical = Path.Combine(root, "真实目录");
        var aliases = Path.Combine(root, "aliases");
        Directory.CreateDirectory(physical);
        Directory.CreateDirectory(aliases);
        File.WriteAllText(Path.Combine(physical, "FurMark_GUI.exe"), "not executed");
        File.WriteAllText(Path.Combine(physical, "gxc_x64.dll"), "sibling dependency");
        string? first = null;
        string? second = null;
        try
        {
            first = FurMarkLaunchPath.AliasDirectory(physical, root);
            second = FurMarkLaunchPath.AliasDirectory(first, aliases);
            // A merged/indirect view must not be persisted as the new junction's target.
            Assert.Equal(physical, new DirectoryInfo(second).ResolveLinkTarget(false)!.FullName);
            Assert.Equal("sibling dependency", File.ReadAllText(Path.Combine(second, "gxc_x64.dll")));
            Assert.Equal(physical, FurMarkLaunchPath.ResolvePhysicalDirectory(first));
            // Different views of the same directory reuse one alias in a given root.
            Assert.Equal(first, FurMarkLaunchPath.AliasDirectory(first, root));
        }
        finally
        {
            if (second is not null) Directory.Delete(second);
            if (first is not null) Directory.Delete(first);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AliasRoot_ReturnsPhysicalPathThroughParentJunction()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuba-physical-root-" + Guid.NewGuid().ToString("N"));
        var physical = Path.Combine(root, "physical");
        Directory.CreateDirectory(physical);
        string? view = null;
        try
        {
            view = FurMarkLaunchPath.AliasDirectory(physical, root);
            var aliasRoot = FurMarkLaunchPath.GetAliasRoot(view, root);
            Assert.Equal(Path.Combine(physical, "TubaWinUi3.FurMark"), aliasRoot);
            Assert.Equal(aliasRoot, FurMarkLaunchPath.ResolvePhysicalDirectory(aliasRoot));
        }
        finally
        {
            if (view is not null) Directory.Delete(view);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LongPhysicalDirectory_IsResolvedWithoutTruncation()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuba-long-path-" + Guid.NewGuid().ToString("N"));
        var physical = Path.Combine(root, new string('a', 180), new string('b', 180), new string('c', 180));
        Directory.CreateDirectory(physical);
        string? alias = null;
        try
        {
            Assert.True(physical.Length > 512);
            Assert.Equal(physical, FurMarkLaunchPath.ResolvePhysicalDirectory(physical));
            alias = FurMarkLaunchPath.AliasDirectory(physical, root);
            Assert.Equal(physical, FurMarkLaunchPath.ResolvePhysicalDirectory(alias));
        }
        finally
        {
            if (alias is not null) Directory.Delete(alias);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AsciiViewOfChineseDirectory_FallsBackToPhysicalAsciiRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuba-ascii-view-" + Guid.NewGuid().ToString("N"));
        var physical = Path.Combine(root, "实际目录");
        Directory.CreateDirectory(physical);
        string? view = null;
        try
        {
            view = FurMarkLaunchPath.AliasDirectory(physical, root);
            var aliasRoot = FurMarkLaunchPath.GetAliasRoot(view, root);
            Assert.StartsWith(Path.Combine(root, "TubaWinUi3.FurMark-S-"), aliasRoot);
            Assert.All(aliasRoot, c => Assert.True(c <= 127));
            Assert.Equal(aliasRoot, FurMarkLaunchPath.ResolvePhysicalDirectory(aliasRoot));
        }
        finally
        {
            if (view is not null) Directory.Delete(view);
            Directory.Delete(root, true);
        }
    }
}
