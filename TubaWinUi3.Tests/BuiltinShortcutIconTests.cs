using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 内置工具桌面快捷方式链路：字形 .ico 编码必须能被 GDI 正常解析；
/// COM 写 .lnk 后能原样读回目标与参数（--open-builtin 启动链）。
/// </summary>
public class BuiltinShortcutIconTests
{
    [Fact]
    public void GlyphIcon_EncodesToParseableIco()
    {
        var type = typeof(WindowsSearchIndexService);
        var render = type.GetMethod("RenderGlyphBitmap", BindingFlags.NonPublic | BindingFlags.Static);
        var encode = type.GetMethod("EncodeIco", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(render);
        Assert.NotNull(encode);

        using var master = (Bitmap)render.Invoke(null, new object[] { "\uE7F4" })!;
        Assert.Equal(256, master.Width);
        var bytes = (byte[])encode.Invoke(null, new object[] { master })!;

        // 文件头：ICONDIR + 5 个条目
        Assert.True(bytes.Length > 6 + 16 * 5);
        Assert.Equal(0, bytes[0]);                // reserved
        Assert.Equal(1, bytes[2]);                // type:icon
        Assert.Equal(5, bytes[4]);                // 图片数

        // GDI 能完整解析多尺寸 ICO 且成功创建图标句柄 = 结构合法
        using var icon = new Icon(new MemoryStream(bytes));
        Assert.NotEqual(IntPtr.Zero, icon.Handle);
    }

    [Fact]
    public void CreateShortcut_WritesLnkReadBackWithArguments()
    {
        var type = typeof(WindowsSearchIndexService);
        var create = type.GetMethod("CreateShortcut", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(create);

        var dir = Path.Combine(Path.GetTempPath(), "tubawinui3-lnk-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var lnk = Path.Combine(dir, "屏幕坏点检测.lnk");
        var previousDataDir = ConfigManager.DataDirectoryOverride;
        ConfigManager.DataDirectoryOverride = dir;
        try
        {
            var iconPath = WindowsSearchIndexService.EnsureBuiltinIcon(new ScreenTestTool());
            Assert.NotNull(iconPath);
            Assert.EndsWith(".ico", iconPath);
            using var coloredIcon = new Icon(iconPath);
            Assert.NotEqual(IntPtr.Zero, coloredIcon.Handle);
            create.Invoke(null, new object?[]
            {
                lnk, @"C:\Windows\System32\notepad.exe", @"C:\Windows",
                "屏幕坏点检测 - 硬件工具", "--open-builtin screen-test", iconPath
            });
            Assert.True(File.Exists(lnk));

            // 读回验证（WScript.Shell 需 STA）
            string? target = null, args = null, iconLocation = null;
            RunSta(() =>
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                dynamic sc = shell.CreateShortcut(lnk);
                target = (string)sc.TargetPath;
                args = (string)sc.Arguments;
                iconLocation = (string)sc.IconLocation;
                Marshal.FinalReleaseComObject(sc);
                Marshal.FinalReleaseComObject(shell);
            });

            Assert.Equal(@"C:\Windows\System32\notepad.exe", target);
            Assert.Equal("--open-builtin screen-test", args);
            Assert.Equal(iconPath + ",0", iconLocation);
        }
        finally
        {
            ConfigManager.DataDirectoryOverride = previousDataDir;
            Directory.Delete(dir, recursive: true);
        }
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