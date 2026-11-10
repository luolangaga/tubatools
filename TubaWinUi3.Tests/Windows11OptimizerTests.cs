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
            Assert.Equal(@"%SystemRoot%\System32\shell32.dll,-50", op.OnValue);
            Assert.Null(op.OffValue);
            Assert.True(tweak.NeedsExplorerRestart);
            Assert.True(tweak.Recommended);
        }
        Assert.Equal("29", arrow.Ops[0].Target.Name);
        Assert.Equal("77", shield.Ops[0].Target.Name);
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
}
