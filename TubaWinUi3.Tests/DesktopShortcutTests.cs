using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 「发送到桌面」快捷方式回归：第三方工具与内置工具都必须走进程内 WScript.Shell COM 写入器，
/// 中文分类目录等非 ASCII 路径必须原样落进 .lnk。
/// （旧实现第三方工具另起 powershell.exe -Command，在非中文系统 / UTF-8 代码页下会把中文路径写成乱码。）
/// </summary>
[Trait("Category", "DesktopIntegration")]
public class DesktopShortcutTests
{
    [Fact]
    public void CreateDesktopShortcut_FurMark_UsesAsciiTargetAndWorkingDirectory()
    {
        var root = NewTempRoot();
        string? alias = null;
        try
        {
            var desktop = Path.Combine(root, "桌面");
            Directory.CreateDirectory(desktop);
            var exe = CreateTool(root, "烤鸡工具", "FurMark_win64", "FurMark_GUI.exe");
            var tool = NewTool("FurMark", "烤鸡工具", exe, @"烤鸡工具\FurMark_win64\FurMark_GUI.exe");
            var launch = FurMarkLaunchPath.Prepare(exe, Path.GetDirectoryName(exe));
            alias = launch.WorkingDirectory;
            var shortcut = ReadShortcut(WindowsSearchIndexService.CreateDesktopShortcut(tool, desktop));
            Assert.Equal(launch.ExePath, shortcut.Target, ignoreCase: true);
            Assert.Equal(alias, shortcut.WorkingDirectory, ignoreCase: true);
            Assert.All(shortcut.Target, c => Assert.True(c <= 127));
            Assert.True(File.Exists(shortcut.Target));
        }
        finally
        {
            if (alias is not null) Directory.Delete(alias);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CreateDesktopShortcut_ChineseCategoryTarget_RoundTripsExactly()
    {
        var root = NewTempRoot();
        try
        {
            var desktop = Path.Combine(root, "桌面");
            var exe = CreateTool(root, "综合检测", "AIDA64", "aida64.exe");
            var tool = NewTool("AIDA64", "综合检测", exe, @"综合检测\AIDA64\aida64.exe");
            Directory.CreateDirectory(desktop);

            var lnk = WindowsSearchIndexService.CreateDesktopShortcut(tool, desktop);

            Assert.Equal(Path.Combine(desktop, "AIDA64.lnk"), lnk);
            Assert.True(File.Exists(lnk));

            var sc = ReadShortcut(lnk);
            Assert.Equal(exe, sc.Target, ignoreCase: true);
            Assert.Equal(Path.GetDirectoryName(exe)!, sc.WorkingDirectory, ignoreCase: true);
            Assert.Equal("AIDA64", sc.Description);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateDesktopShortcut_InvalidCharsInName_AreStripped()
    {
        var root = NewTempRoot();
        try
        {
            var desktop = Path.Combine(root, "桌面");
            var exe = CreateTool(root, "处理器工具", "CPU-Z", "cpuz.exe");
            var tool = NewTool("A/B*C", "处理器工具", exe, @"处理器工具\CPU-Z\cpuz.exe");
            Directory.CreateDirectory(desktop);

            var lnk = WindowsSearchIndexService.CreateDesktopShortcut(tool, desktop);

            Assert.Equal(Path.Combine(desktop, "ABC.lnk"), lnk);
            Assert.True(File.Exists(lnk));
            Assert.False(Directory.Exists(Path.Combine(desktop, "A")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateDesktopShortcut_ArchOverride_UsesEffectivePathAndSuffix()
    {
        var root = NewTempRoot();
        try
        {
            var desktop = Path.Combine(root, "桌面");
            var x86 = CreateTool(root, "处理器工具", "CPU-Z", "cpuz_x86.exe");
            var x64 = CreateTool(root, "处理器工具", "CPU-Z", "cpuz_x64.exe");
            var tool = NewTool("CPU-Z", "处理器工具", x86, @"处理器工具\CPU-Z\cpuz_x86.exe");
            tool.SelectedArch = new ArchOption { Name = "CPU-Z (x64)", Path = x64, Arch = "x64" };
            Directory.CreateDirectory(desktop);

            var lnk = WindowsSearchIndexService.CreateDesktopShortcut(tool, desktop);

            Assert.Equal(Path.Combine(desktop, "CPU-Z (x64).lnk"), lnk);
            var sc = ReadShortcut(lnk);
            Assert.Equal(x64, sc.Target, ignoreCase: true);
            Assert.Equal("CPU-Z (x64)", sc.Description);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateDesktopShortcut_BuiltinLink_LaunchesSelfWithOpenBuiltin()
    {
        if (BuiltinToolRegistry.GetById("screen-test") is null)
            BuiltinToolRegistry.RegisterDefaults();

        var root = NewTempRoot();
        try
        {
            var desktop = Path.Combine(root, "桌面");
            var tool = new ToolItem
            {
                Name = "屏幕坏点检测",
                Category = "综合检测",
                Path = Path.Combine(root, "Tools", "综合检测", "屏幕坏点检测"),
                RelativePath = @"综合检测\屏幕坏点检测",
                Extension = "内置",
                IsBuiltinLink = true,
                BuiltinToolId = "screen-test"
            };
            Directory.CreateDirectory(desktop);

            var lnk = WindowsSearchIndexService.CreateDesktopShortcut(tool, desktop);

            Assert.Equal(Path.Combine(desktop, "屏幕坏点检测.lnk"), lnk);
            var sc = ReadShortcut(lnk);
            var self = Process.GetCurrentProcess().MainModule!.FileName;
            Assert.Equal(self, sc.Target, ignoreCase: true);
            Assert.Equal("--open-builtin screen-test", sc.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateDesktopShortcut_NotDownloadedTool_Throws()
    {
        var root = NewTempRoot();
        try
        {
            var desktop = Path.Combine(root, "桌面");
            var tool = new ToolItem
            {
                Name = "AIDA64",
                Category = "综合检测",
                Path = Path.Combine(root, "Tools", "综合检测", "AIDA64", "aida64.exe"),
                RelativePath = @"综合检测\AIDA64\aida64.exe",
                Extension = ".exe",
                DownloadUrl = "https://example.invalid/aida64.zip"
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => WindowsSearchIndexService.CreateDesktopShortcut(tool, desktop));
            Assert.Contains("尚未下载", ex.Message);
            Assert.False(Directory.Exists(desktop));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VerifyShortcutTarget_MismatchedTarget_Throws()
    {
        var root = NewTempRoot();
        try
        {
            var desktop = Path.Combine(root, "桌面");
            var exe = CreateTool(root, "内存工具", "MemTest", "memtest.exe");
            var other = CreateTool(root, "内存工具", "MemTest", "other.exe");
            var tool = NewTool("MemTest", "内存工具", exe, @"内存工具\MemTest\memtest.exe");
            Directory.CreateDirectory(desktop);

            var lnk = WindowsSearchIndexService.CreateDesktopShortcut(tool, desktop);

            var verify = typeof(WindowsSearchIndexService)
                .GetMethod("VerifyShortcutTarget", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(verify);

            var ex = Assert.Throws<TargetInvocationException>(
                () => verify.Invoke(null, new object[] { lnk, other }));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("校验失败", ex.InnerException!.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tubawinui3-lnk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateTool(string root, string category, string dirName, string fileName)
    {
        var path = Path.Combine(root, "Tools", category, dirName, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    private static ToolItem NewTool(string name, string category, string path, string relativePath) => new()
    {
        Name = name,
        Category = category,
        Path = path,
        RelativePath = relativePath,
        Extension = Path.GetExtension(path)
    };

    private static (string Target, string WorkingDirectory, string Description, string Arguments) ReadShortcut(string lnk)
    {
        var target = string.Empty;
        var workingDirectory = string.Empty;
        var description = string.Empty;
        var arguments = string.Empty;

        RunSta(() =>
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            target = Convert.ToString(shortcut.TargetPath) ?? string.Empty;
            workingDirectory = Convert.ToString(shortcut.WorkingDirectory) ?? string.Empty;
            description = Convert.ToString(shortcut.Description) ?? string.Empty;
            arguments = Convert.ToString(shortcut.Arguments) ?? string.Empty;
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        });

        return (target, workingDirectory, description, arguments);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw error;
    }
}
