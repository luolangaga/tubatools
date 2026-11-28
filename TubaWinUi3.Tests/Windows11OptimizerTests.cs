using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Tubatools.Windows11Optimizer;
using TubaWinUi3.PluginSdk;

namespace TubaWinUi3.Tests;

/// <summary>Windows 11 优化插件（plugins/Tubatools.Windows11Optimizer）的清单与注册表回归。</summary>
public sealed class Windows11OptimizerTests
{
    [Fact]
    public void PluginRegistersOneMultiFunctionTool()
    {
        var host = new CapturingHost();
        new Windows11OptimizerPlugin().Register(host);
        var tool = Assert.Single(host.Tools);
        Assert.Equal("windows11-optimizer", tool.Id);
        Assert.Equal("系统工具", tool.Category);
        Assert.NotNull(tool.CreateContent);
        Assert.NotNull(tool.InitializeAsync);
        Assert.Null(tool.Xaml);
        Assert.Null(tool.ExecuteAsync);
        Assert.Contains("Windows 11", tool.Tags);
    }

    [Fact]
    public void Catalog_HasUniqueIdsAndCoversEverySection()
    {
        var tweaks = TweakCatalog.Tweaks;
        Assert.True(tweaks.Count >= 60, $"只有 {tweaks.Count} 项设置");
        Assert.Equal(tweaks.Count, tweaks.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var tab in new[]
                 {
                     OptimizerTab.Explorer, OptimizerTab.Privacy, OptimizerTab.Performance,
                     OptimizerTab.Update, OptimizerTab.Power, OptimizerTab.Notifications
                 })
        {
            Assert.Contains(tweaks, t => t.Tab == tab);
        }
        Assert.All(tweaks, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Group), t.Id);
        });
        Assert.All(Enum.GetValues<OptimizerTab>(), tab => Assert.False(string.IsNullOrWhiteSpace(TweakCatalog.Title(tab))));
    }

    [Fact]
    public void Catalog_TogglesAndChoicesHaveWrites()
    {
        foreach (var tweak in TweakCatalog.Tweaks)
        {
            if (tweak.Kind == TweakKind.Toggle)
            {
                Assert.True(tweak.Ops.Count > 0 || tweak.ReadToggle is not null || tweak.ApplyToggle is not null, tweak.Id);
                Assert.All(tweak.Ops, op => Assert.False(string.IsNullOrWhiteSpace(op.Target.Path), tweak.Id));
                continue;
            }

            Assert.NotEmpty(tweak.Choices);
            Assert.Equal(tweak.Choices.Count, tweak.Choices.Select(c => c.Label).Distinct(StringComparer.Ordinal).Count());
            Assert.All(tweak.Choices, choice => Assert.True(choice.Ops.Count > 0 || choice.Apply is not null, tweak.Id + " / " + choice.Label));
            Assert.Contains(tweak.Choices, c => c.IsDefault);
        }

        Assert.True(TweakCatalog.Tweaks.Count(t => t.Recommended) >= 5);
        Assert.NotEmpty(TweakCatalog.Actions);
    }

    [Fact]
    public void Catalog_RegistryOpsUseSupportedValueKinds()
    {
        var ops = TweakCatalog.Tweaks
            .SelectMany(t => t.Ops.Concat(t.Choices.SelectMany(c => c.Ops)))
            .ToList();
        Assert.NotEmpty(ops);
        foreach (var op in ops)
        {
            Assert.True(op.Target.Kind is RegistryValueKind.DWord or RegistryValueKind.String or RegistryValueKind.ExpandString, op.Target.Name);
            Assert.False(string.IsNullOrWhiteSpace(op.Target.Path));
            Assert.False(string.IsNullOrWhiteSpace(op.Target.Name));
            if (op.Target.Kind == RegistryValueKind.DWord)
            {
                if (op.OnValue is not null) Assert.IsType<int>(op.OnValue);
                if (op.OffValue is not null) Assert.IsType<int>(op.OffValue);
            }
            else
            {
                if (op.OnValue is not null) Assert.IsType<string>(op.OnValue);
                if (op.OffValue is not null) Assert.IsType<string>(op.OffValue);
            }
        }
    }

    [Fact]
    public void HeadlineTweaks_RemoveArrowAndShieldViaShellIcons()
    {
        var arrow = Assert.Single(TweakCatalog.Tweaks, t => t.Id == "shortcut-arrow");
        var shield = Assert.Single(TweakCatalog.Tweaks, t => t.Id == "shortcut-shield");
        foreach (var tweak in new[] { arrow, shield })
        {
            var op = Assert.Single(tweak.Ops);
            Assert.Equal(RegistryHive.LocalMachine, op.Target.Hive);
            Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons", op.Target.Path);
            Assert.Equal(RegistryValueKind.ExpandString, op.Target.Kind);
            Assert.Equal(ShortcutIconOverlay.BlankIconPath, op.OnValue);
            Assert.Null(op.OffValue);
            Assert.True(tweak.NeedsExplorerRestart);
            Assert.True(tweak.Recommended);
        }
        Assert.Equal("29", arrow.Ops[0].Target.Name);
        Assert.Equal("77", shield.Ops[0].Target.Name);
        Assert.NotEqual(ShortcutIconOverlay.LegacyShell32Blank, arrow.Ops[0].OnValue);
    }

    [Fact]
    public void ShortcutOverlayIcon_IsAValidShellSafeTransparentIco()
    {
        var icon = ShortcutIconOverlay.BuildIcon();
        var expectedSizes = new[] { 16, 32, 48 };

        Assert.Equal(15086, icon.Length);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(0, 2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(2, 2)));
        Assert.Equal(expectedSizes.Length, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(4, 2)));

        var expectedOffset = 6 + expectedSizes.Length * 16;
        for (var i = 0; i < expectedSizes.Length; i++)
        {
            var size = expectedSizes[i];
            var entry = 6 + i * 16;
            Assert.Equal(size, icon[entry]);
            Assert.Equal(size, icon[entry + 1]);
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(entry + 6, 2)));
            var imageBytes = 40 + size * size * 4 + ((size + 31) / 32) * 4 * size;
            Assert.Equal((uint)imageBytes, BinaryPrimitives.ReadUInt32LittleEndian(icon.AsSpan(entry + 8, 4)));
            Assert.Equal((uint)expectedOffset, BinaryPrimitives.ReadUInt32LittleEndian(icon.AsSpan(entry + 12, 4)));

            var dib = expectedOffset;
            Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(icon.AsSpan(dib, 4)));
            Assert.Equal(size, BinaryPrimitives.ReadInt32LittleEndian(icon.AsSpan(dib + 4, 4)));
            Assert.Equal(size * 2, BinaryPrimitives.ReadInt32LittleEndian(icon.AsSpan(dib + 8, 4)));
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(dib + 14, 2)));
            Assert.Equal((uint)(size * size * 4), BinaryPrimitives.ReadUInt32LittleEndian(icon.AsSpan(dib + 20, 4)));

            // 不能是“全 alpha=0”：Explorer 10/11 会把它渲染成不透明黑块。
            // 每帧保留恰好一个 alpha=1 的像素，强制 Shell 选择 alpha 混合路径。
            var nonzeroAlpha = 0;
            for (var pixel = 0; pixel < size * size; pixel++)
                if (icon[dib + 40 + pixel * 4 + 3] != 0) nonzeroAlpha++;
            Assert.Equal(1, nonzeroAlpha);
            Assert.Equal(1, icon[dib + 40 + (size - 1) * 4 + 3]);

            // AND 掩码是 1bpp 位图：有效位 1=透明、0=不透明黑，每行按 4 字节对齐。
            // 全 0 掩码会在桌面快捷方式上画出一整块黑；正确结构是只留一个 0 位与 alpha 像素对应。
            var andMask = dib + 40 + size * size * 4;
            var andStride = ((size + 31) / 32) * 4;
            var effectiveBytes = (size + 7) / 8;
            var zeroBits = 0;
            for (var row = 0; row < size; row++)
            {
                for (var b = 0; b < effectiveBytes; b++)
                {
                    var value = icon[andMask + row * andStride + b];
                    for (var bit = 0; bit < 8; bit++)
                        if ((value & (1 << bit)) == 0) zeroBits++;
                }
                for (var pad = effectiveBytes; pad < andStride; pad++)
                    Assert.Equal(0, icon[andMask + row * andStride + pad]);
            }
            Assert.Equal(1, zeroBits);
            Assert.Equal(0xFE, icon[andMask + effectiveBytes - 1]);
            expectedOffset += imageBytes;
        }
        Assert.Equal(icon.Length, expectedOffset);
    }

    [Fact]
    public void ShortcutOverlayIcon_DoesNotRenderAsBlackThroughShell()
    {
        var directory = Path.Combine(Path.GetTempPath(), "optimizer-shell-icon-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "blank.ico");
        try
        {
            Directory.CreateDirectory(directory);
            ShortcutIconOverlay.EnsureInstalled(path);

            foreach (var size in new[] { 16, 32, 48 })
            {
                var large = new IntPtr[1];
                var ids = new uint[1];
                var count = PrivateExtractIcons(path, 0, size, size, large, ids, 1, 0);
                Assert.Equal(1u, count);
                Assert.NotEqual(IntPtr.Zero, large[0]);
                try
                {
                    using var image = Icon.FromHandle(large[0]);
                    using var bitmap = image.ToBitmap();
                    var opaqueBlack = 0;
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        for (var y = 0; y < bitmap.Height; y++)
                        {
                            var pixel = bitmap.GetPixel(x, y);
                            if (pixel.A != 0 && pixel.R < 8 && pixel.G < 8 && pixel.B < 8) opaqueBlack++;
                        }
                    }

                    // Shell 对旧的“全 alpha=0”图标会返回整张不透明黑图；修复后最多剩一个 alpha=1 的像素。
                    Assert.True(opaqueBlack <= 1, $"{size}x{size} 经 Shell 渲染出 {opaqueBlack} 个不透明黑像素");
                }
                finally
                {
                    if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
                }
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ShortcutOverlayIcon_InstallsAndRepairsItsFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "optimizer-icon-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "blank.ico");
        try
        {
            ShortcutIconOverlay.EnsureInstalled(path);
            Assert.Equal(ShortcutIconOverlay.BuildIcon(), File.ReadAllBytes(path));
            Assert.True(ShortcutIconOverlay.HasCurrentIcon(path));

            File.WriteAllBytes(path, [0, 1, 2, 3]);
            Assert.False(ShortcutIconOverlay.HasCurrentIcon(path));
            ShortcutIconOverlay.EnsureInstalled(path);
            Assert.True(ShortcutIconOverlay.HasCurrentIcon(path));
            Assert.Equal(ShortcutIconOverlay.BuildIcon(), File.ReadAllBytes(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ShortcutOverlayIcon_RecognizesLegacyShell32Blank()
    {
        var expanded = Environment.ExpandEnvironmentVariables(ShortcutIconOverlay.LegacyShell32Blank);
        Assert.True(ShortcutIconOverlay.IsLegacyShell32Blank(ShortcutIconOverlay.LegacyShell32Blank));
        Assert.True(ShortcutIconOverlay.IsLegacyShell32Blank(expanded));
        Assert.True(ShortcutIconOverlay.IsLegacyShell32Blank(@"""%windir%\System32\shell32.dll,-50"""));
        Assert.False(ShortcutIconOverlay.IsLegacyShell32Blank(ShortcutIconOverlay.BlankIconPath));
        Assert.False(ShortcutIconOverlay.IsLegacyShell32Blank(null));
        Assert.False(ShortcutIconOverlay.IsLegacyShell32Blank(@"C:\Custom\blank.ico"));
    }

    [Fact]
    public void UacChoice_WritesTheWindowsPolicyKey()
    {
        var uac = Assert.Single(TweakCatalog.Tweaks, t => t.Id == "uac-level");
        Assert.All(uac.Choices.SelectMany(c => c.Ops), op =>
        {
            Assert.Equal(RegistryHive.LocalMachine, op.Target.Hive);
            Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", op.Target.Path);
        });
        var defaultValue = Assert.Single(uac.Choices, c => c.IsDefault);
        var adminPrompt = Assert.Single(defaultValue.Ops, op => op.Target.Name == "ConsentPromptBehaviorAdmin");
        Assert.Equal(5, adminPrompt.OnValue);
    }

    [Fact]
    public void RegistryAccess_RoundTripsUnderTestKey()
    {
        var path = @"Software\TubaWinUi3.Tests\Optimizer-" + Guid.NewGuid().ToString("N");
        var dword = new RegistryTarget(RegistryHive.CurrentUser, path, "Dword", RegistryValueKind.DWord);
        var text = new RegistryTarget(RegistryHive.CurrentUser, path, "Text", RegistryValueKind.String);
        try
        {
            RegistryAccess.Write(dword, 26);
            RegistryAccess.Write(text, "hello");
            Assert.Equal(26, Assert.IsType<int>(RegistryAccess.Read(dword)));
            Assert.Equal("hello", Assert.IsType<string>(RegistryAccess.Read(text)));
            Assert.True(RegistryAccess.Matches(dword, 26));
            Assert.False(RegistryAccess.Matches(dword, 2));
            Assert.True(RegistryAccess.Exists(dword));

            RegistryAccess.Write(text, null);
            Assert.Null(RegistryAccess.Read(text));
            Assert.False(RegistryAccess.Exists(text));
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\TubaWinUi3.Tests", true);
            key?.DeleteSubKeyTree(Path.GetFileName(path), false);
        }
    }

    [Fact]
    public async Task Backup_WritesAllManagedTargets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "optimizer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = await TweakEngine.CreateBackupAsync(directory, CancellationToken.None);
            Assert.True(File.Exists(path));
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var entries = document.RootElement.GetProperty("Entries");
            Assert.True(entries.GetArrayLength() >= 50);
            Assert.Contains(entries.EnumerateArray(), entry => entry.GetProperty("Name").GetString() == "29");
            Assert.Contains(entries.EnumerateArray(), entry => entry.GetProperty("Name").GetString() == "77");
            Assert.NotNull(TweakEngine.FindLatestBackup(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Catalog_DefaultOnMatchesKnownWindowsDefaults()
    {
        foreach (var id in new[]
                 {
                     "status-bar", "thumbnails", "nav-expand", "info-tips", "recent-files",
                     "desktop-recycle", "transparency", "game-mode", "hags"
                 })
        {
            Assert.True(Assert.Single(TweakCatalog.Tweaks, t => t.Id == id).DefaultOn, id);
        }

        foreach (var id in new[]
                 {
                     "show-extensions", "show-hidden-files", "show-super-hidden", "compact-mode",
                     "taskbar-left", "dark-apps", "suggestions", "game-dvr"
                 })
        {
            Assert.False(Assert.Single(TweakCatalog.Tweaks, t => t.Id == id).DefaultOn, id);
        }
    }

    [Fact]
    public void AppxAndWingetCatalogs_AreValid()
    {
        var pattern = new Regex(@"^[A-Za-z0-9._\-]+$");
        Assert.True(TweakCatalog.AppxApps.Count >= 20);
        Assert.Equal(TweakCatalog.AppxApps.Count, TweakCatalog.AppxApps.Select(a => a.PackageName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(TweakCatalog.AppxApps, app =>
        {
            Assert.Matches(pattern, app.PackageName);
            Assert.False(string.IsNullOrWhiteSpace(app.Title));
        });

        var wingetPattern = new Regex(@"^[A-Za-z0-9._+\-]+$");
        Assert.True(TweakCatalog.WingetApps.Count >= 10);
        Assert.Equal(TweakCatalog.WingetApps.Count, TweakCatalog.WingetApps.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(TweakCatalog.WingetApps, app =>
        {
            Assert.Matches(wingetPattern, app.Id);
            Assert.False(string.IsNullOrWhiteSpace(app.Title));
        });
    }

    private sealed class CapturingHost : IPluginHost
    {
        public List<PluginToolDefinition> Tools { get; } = [];
        public void RegisterTool(PluginToolDefinition tool) => Tools.Add(tool);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(
        string file, int index, int cx, int cy, IntPtr[] icons, uint[] iconIds, uint count, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
