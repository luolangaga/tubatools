using System.Text;
using TubaWinUi3.ShellIntegration;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 「文件占用查看」右键菜单契约（主程序与 ShellExtension 共享源码）的纯逻辑回归：
/// 模式判定、经典菜单命令构造、标记文件读写与解析、提权脚本拼装。
/// 不触碰真实注册表键——注册表路径只做文本级断言，避免污染开发机 HKCU。
/// </summary>
public class FileLockShellMenuTests
{
    [Fact]
    public void ResolveMode_ClassicForUnpackaged_ModernForWin11Packaged_UnsupportedForWin10Packaged()
    {
        Assert.Equal(FileLockShellMenuContract.MenuMode.ClassicRegistry,
            FileLockShellMenuContract.ResolveMode(packagedContext: false, osBuild: 19045));
        Assert.Equal(FileLockShellMenuContract.MenuMode.ClassicRegistry,
            FileLockShellMenuContract.ResolveMode(packagedContext: false, osBuild: 22631));
        Assert.Equal(FileLockShellMenuContract.MenuMode.Unsupported,
            FileLockShellMenuContract.ResolveMode(packagedContext: true, osBuild: 19045));
        Assert.Equal(FileLockShellMenuContract.MenuMode.ModernMenu,
            FileLockShellMenuContract.ResolveMode(packagedContext: true, osBuild: 22000));
        Assert.Equal(FileLockShellMenuContract.MenuMode.ModernMenu,
            FileLockShellMenuContract.ResolveMode(packagedContext: true, osBuild: 26100));
    }

    [Fact]
    public void BuildClassicCommand_QuotesExeAndShellPlaceholder()
    {
        var command = FileLockShellMenuContract.BuildClassicCommand(@"C:\图吧 工具箱\TubaWinUi3.exe");
        Assert.Equal("\"C:\\图吧 工具箱\\TubaWinUi3.exe\" --file-lock \"%1\"", command);
    }

    [Fact]
    public void ClassicRegistryKeys_CoverFilesAndDirectoriesWithSharedKeyName()
    {
        var paths = FileLockShellMenuContract.ClassicRegistryKeyPaths();
        Assert.Equal(2, paths.Count);
        Assert.Contains(paths, p => p.StartsWith(@"Software\Classes\*\shell\", StringComparison.Ordinal));
        Assert.Contains(paths, p => p.StartsWith(@"Software\Classes\Directory\shell\", StringComparison.Ordinal));
        Assert.All(paths, p => Assert.EndsWith(FileLockShellMenuContract.ClassicMenuKeyName, p, StringComparison.Ordinal));
        Assert.All(paths, p => Assert.StartsWith(@"Software\Classes\", p, StringComparison.Ordinal));
    }

    [Fact]
    public void Marker_SerializeParse_RoundTripsTitleAndTooltip()
    {
        var text = FileLockShellMenuContract.SerializeMarker("用图吧工具箱检测文件占用", "查看是哪个进程占用了此文件");
        Assert.True(FileLockShellMenuContract.TryParseMarker(text, out var title, out var tooltip));
        Assert.Equal("用图吧工具箱检测文件占用", title);
        Assert.Equal("查看是哪个进程占用了此文件", tooltip);
    }

    [Fact]
    public void Marker_ParsesLeniently_CrLfAndMissingTooltip()
    {
        Assert.True(FileLockShellMenuContract.TryParseMarker("标题\r\n提示", out var t1, out var tip1));
        Assert.Equal("标题", t1);
        Assert.Equal("提示", tip1);

        Assert.True(FileLockShellMenuContract.TryParseMarker("只有标题", out var t2, out var tip2));
        Assert.Equal("只有标题", t2);
        Assert.Equal(FileLockShellMenuContract.DefaultToolTip, tip2);
    }

    [Fact]
    public void Marker_EmptyOrBlankTitle_IsNotEnabled()
    {
        Assert.False(FileLockShellMenuContract.TryParseMarker(null, out _, out _));
        Assert.False(FileLockShellMenuContract.TryParseMarker("", out _, out _));
        Assert.False(FileLockShellMenuContract.TryParseMarker("   \n提示", out _, out _));
    }

    [Fact]
    public void Marker_SerializeSanitizesNewlinesInArguments()
    {
        // 注入换行不能让标题/提示串行（否则提示行会顶替其它内容）
        var text = FileLockShellMenuContract.SerializeMarker("标\n题", "提\r\n示");
        Assert.True(FileLockShellMenuContract.TryParseMarker(text, out var title, out var tooltip));
        Assert.Equal("标 题", title);
        Assert.Equal("提  示", tooltip);
    }

    [Fact]
    public void MarkerStore_WriteReadDelete_RoundTripsOnDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuba-shellmenu-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            FileLockShellMenuContract.WriteMarker(root, "菜单标题", "悬停提示");
            Assert.True(FileLockShellMenuContract.MarkerExists([root]));
            Assert.True(FileLockShellMenuContract.TryReadMarker([root], out var title, out var tooltip));
            Assert.Equal("菜单标题", title);
            Assert.Equal("悬停提示", tooltip);

            // 候选根列表：第一个根没有标记、第二个根有 —— 应继续探测到第二个
            var emptyRoot = Path.Combine(root, "empty");
            Assert.True(FileLockShellMenuContract.TryReadMarker([emptyRoot, root], out _, out _));

            FileLockShellMenuContract.DeleteMarker(root);
            Assert.False(FileLockShellMenuContract.MarkerExists([root]));
            Assert.False(FileLockShellMenuContract.TryReadMarker([root], out _, out _));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TryGetMarkerPath_RejectsInvalidRoot()
    {
        Assert.Null(FileLockShellMenuContract.TryGetMarkerPath(""));
        Assert.Null(FileLockShellMenuContract.TryGetMarkerPath("   "));
        Assert.EndsWith(FileLockShellMenuContract.MarkerRelativePath,
            FileLockShellMenuContract.TryGetMarkerPath(@"C:\Users\test")!, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedRelaunchScript_SetsDataRootAndEscapesArguments()
    {
        var script = FileLockShellMenuContract.BuildElevatedRelaunchScript(
            @"C:\Program Files\WindowsApps\pkg\TubaWinUi3.exe",
            @"C:\Users\a\AppData\Local\Packages\pfn\LocalState",
            [FileLockShellMenuContract.FileLockArg, @"C:\用 户\它's\文件.txt", FileLockShellMenuContract.MsixAdminSessionArg]);

        Assert.Contains($"$env:{FileLockShellMenuContract.LocalStateEnvVar}='C:\\Users\\a\\AppData\\Local\\Packages\\pfn\\LocalState';", script, StringComparison.Ordinal);
        Assert.Contains("-Verb RunAs", script, StringComparison.Ordinal);
        Assert.Contains("'--file-lock','C:\\用 户\\它''s\\文件.txt','--msix-admin-session'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodePowerShellCommand_DecodesToOriginalScript()
    {
        const string script = "Start-Process -FilePath 'x' -Verb RunAs; # 中文注释";
        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(FileLockShellMenuContract.EncodePowerShellCommand(script)));
        Assert.Equal(script, decoded);
    }

    [Fact]
    public void CommandIdentity_IsStableAndConsistent()
    {
        // CLSID 一旦发布不能改（清单 + DLL 双侧绑定）；本测试是防误改的锚点。
        Assert.True(Guid.TryParse(FileLockShellMenuContract.CommandClsid, out var guid));
        Assert.Equal(guid, FileLockShellMenuContract.CommandClsidGuid);
        Assert.Equal("TubaWinUi3.ShellExtension.dll", FileLockShellMenuContract.ShellExtensionDllName);
        Assert.Equal("TubaWinUi3.exe", FileLockShellMenuContract.ExecutionAlias);
        Assert.Equal("--file-lock", FileLockShellMenuContract.FileLockArg);
        Assert.Equal("--msix-admin-session", FileLockShellMenuContract.MsixAdminSessionArg);
    }
}
