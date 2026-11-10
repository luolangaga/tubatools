using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using TubaWinUi3.Services;
using TubaWinUi3.Storage;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class AuditFixTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TubaAuditFix-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previous = ConfigManager.DataDirectoryOverride;
    public AuditFixTests()
    {
        AppSettings.Flush();
        Directory.CreateDirectory(_directory);
        ConfigManager.DataDirectoryOverride = _directory;
        AppSettings.InvalidateCache();
    }
    public void Dispose()
    {
        ConfigImportTransaction.CommitFailureForTests = null;
        AppSettings.Flush();
        AppSettings.InvalidateCache();
        ConfigManager.DataDirectoryOverride = _previous;
        Directory.Delete(_directory, true);
    }

    [Fact]
    public void AtomicSettingsRecoverFromCorruptPrimaryAndMissingPrimary()
    {
        AppSettings.Set("answer", "first"); AppSettings.Flush();
        AppSettings.Set("answer", "second"); AppSettings.Flush();
        File.WriteAllText(ConfigManager.GetSettingsPath(), "{broken");
        AppSettings.InvalidateCache();
        Assert.Equal("first", AppSettings.Get("answer"));
        File.Delete(ConfigManager.GetSettingsPath());
        AppSettings.InvalidateCache();
        Assert.Equal("first", AppSettings.Get("answer"));
    }

    [Fact]
    public void SettingsSnapshotsCannotMutateCache()
    {
        AppSettings.Set("sample", "original");
        var copy = AppSettings.Load(); copy["sample"] = "changed";
        Assert.Equal("original", AppSettings.Get("sample"));
    }

    [Fact]
    public async Task ConcurrentSettingsReadsAndWritesRemainConsistent()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                AppSettings.Set("worker-" + worker, i);
                Assert.Equal(i, AppSettings.GetInt("worker-" + worker));
                _ = AppSettings.Load();
            }
        })));
    }

    [Theory]
    [InlineData("en-US", "de-DE")]
    [InlineData("de-DE", "en-US")]
    public void FloatingPointValuesSurviveRegionChanges(string writeCulture, string readCulture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(writeCulture);
            AppSettings.Set("opacity", 1.23456789); AppSettings.Flush(); AppSettings.InvalidateCache();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(readCulture);
            Assert.Equal(1.23456789, AppSettings.GetDouble("opacity"));
            AppSettings.Set("legacy", "1,50"); Assert.Equal(1.5, AppSettings.GetDouble("legacy"));
            AppSettings.Set("legacy", "1.50"); Assert.Equal(1.5, AppSettings.GetDouble("legacy"));
            AppSettings.Set("legacy", "NaN"); Assert.Equal(0.4, AppSettings.GetDouble("legacy", 0.4));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void CredentialsAndLegacyBackupsAreProtected()
    {
        File.WriteAllText(ConfigManager.GetSettingsPath(), "{\"GitHubToken\":\"audit-not-a-real-token\",\"theme\":\"dark\"}");
        Assert.Equal("audit-not-a-real-token", AppSettings.Get("GitHubToken"));
        AppSettings.Set("ProxyPassword", "audit-not-a-real-password"); AppSettings.Flush();
        foreach (var path in new[] { ConfigManager.GetSettingsPath(), ConfigManager.GetSettingsPath() + ".bak" })
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("audit-not-a-real", text);
            Assert.Contains("dpapi:v1:", text);
        }
        AppSettings.InvalidateCache();
        Assert.Equal("audit-not-a-real-token", AppSettings.Get("GitHubToken"));
    }

    [Fact]
    public void BackupPolicyRemovesSessionsAndNestedApiKeys()
    {
        var clean = ConfigBackupPolicy.Sanitize("{\"GitHubToken\":\"secret\",\"ProxyPassword\":\"secret\",\"PhoneLinkPairedDevices\":\"secret\",\"Providers\":[{\"ApiKey\":\"secret\",\"Name\":\"keep\"}],\"Theme\":\"keep\"}");
        Assert.DoesNotContain("secret", clean);
        Assert.Contains("keep", clean);
        Assert.True(ConfigBackupPolicy.Exclude("settings.json.bak"));
        Assert.True(ConfigBackupPolicy.Exclude("instance/request.json"));
        Assert.True(ConfigBackupPolicy.Exclude("INSTANCE/request.json"));
    }

    [Fact]
    public async Task ExportedBackupContainsPreferencesWithoutCredentialsOrSecretBackups()
    {
        AppSettings.Set("GitHubToken", "audit-export-secret");
        AppSettings.Set("Theme", "dark"); AppSettings.Flush();
        AppSettings.Set("Theme", "light"); AppSettings.Flush();
        var output = Path.Combine(_directory, "export.zip");
        Assert.True(await ConfigManager.ExportConfigAsync(output));
        using var zip = ZipFile.OpenRead(output);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".bak"));
        using var reader = new StreamReader(zip.GetEntry("settings.json")!.Open());
        var text = await reader.ReadToEndAsync();
        Assert.DoesNotContain("GitHubToken", text); Assert.DoesNotContain("dpapi:v1:", text);
        Assert.Contains("light", text);
    }

    [Fact]
    public void LocationSelectionIsWrittenToTheWritableMarkerAndCanResetToAppData()
    {
        var previous = ConfigManager.LocationMarkerOverride;
        var marker = Path.Combine(_directory, "per-user", "location.txt");
        ConfigManager.LocationMarkerOverride = marker;
        try
        {
            Assert.True(ConfigManager.SetConfigLocation(ConfigLocation.Custom, Path.Combine(_directory, "custom")));
            Assert.StartsWith("Custom:", File.ReadAllText(marker));
            Assert.Equal(ConfigLocation.Custom, ConfigManager.GetConfigLocation());
            Assert.True(ConfigManager.SetConfigLocation(ConfigLocation.AppData));
            Assert.Equal("AppData", File.ReadAllText(marker));
            Assert.Equal(ConfigLocation.AppData, ConfigManager.GetConfigLocation());
        }
        finally { ConfigManager.LocationMarkerOverride = previous; ConfigManager.ResetLocationCache(); }
    }

    private string Zip(params (string Name, string Text)[] files)
    {
        var path = Path.Combine(_directory, Guid.NewGuid() + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        { using var writer = new StreamWriter(zip.CreateEntry(file.Name).Open()); writer.Write(file.Text); }
        return path;
    }

    [Fact]
    public void ImportPrevalidatesAllEntriesBeforeChangingLiveData()
    {
        var target = Path.Combine(_directory, "target"); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "settings.json"), "{\"theme\":\"old\"}");
        var zip = Zip(("settings.json", "{\"theme\":\"new\"}"), ("../outside.txt", "invalid"));
        Assert.Throws<UnauthorizedAccessException>(() => ConfigImportTransaction.Import(zip, target));
        Assert.Contains("old", File.ReadAllText(Path.Combine(target, "settings.json")));
    }

    [Fact]
    public void FailedImportRollsBackAllCommittedFiles()
    {
        var target = Path.Combine(_directory, "target"); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "a.txt"), "old");
        var zip = Zip(("a.txt", "new"), ("b.txt", "new"));
        ConfigImportTransaction.CommitFailureForTests = index => { if (index == 1) throw new IOException("injected disk failure"); };
        Assert.Throws<IOException>(() => ConfigImportTransaction.Import(zip, target));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.False(File.Exists(Path.Combine(target, "b.txt")));
    }

    [Fact]
    public void ImportRejectsExistingDirectoryJunction()
    {
        var target = Path.Combine(_directory, "target"); var outside = Path.Combine(_directory, "outside");
        Directory.CreateDirectory(target); Directory.CreateDirectory(outside);
        var link = Path.Combine(target, "linked");
        // Directory junctions do not require Developer Mode or elevation.
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + outside.Replace("'", "''") + "' | Out-Null");
        using var process = Process.Start(start)!; process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        try
        {
            var zip = Zip(("linked/marker.txt", "bad"));
            Assert.Throws<UnauthorizedAccessException>(() => ConfigImportTransaction.Import(zip, target));
            Assert.False(File.Exists(Path.Combine(outside, "marker.txt")));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void SuccessfulImportDoesNotRestoreAuthentication()
    {
        var target = Path.Combine(_directory, "target");
        ConfigImportTransaction.Import(Zip(("settings.json", "{\"GitHubToken\":\"secret\",\"Theme\":\"light\"}")), target);
        var text = File.ReadAllText(Path.Combine(target, "settings.json"));
        Assert.DoesNotContain("secret", text); Assert.Contains("light", text);
    }

    [Fact]
    public void FavoritesMergeExternalChangesAndRecoverBackup()
    {
        FavoritesService.AddFavorite(@"C:\audit\a");
        // Same shared store used by the TUI, after WinUI has already read its snapshot.
        _ = FavoritesService.GetFavorites();
        FavoriteFile.Update(ConfigManager.GetFavoritesPath(), list => { list.Add(@"C:\audit\b"); return list; });
        FavoritesService.AddFavorite(@"C:\audit\c");
        Assert.Equal(3, FavoritesService.GetFavorites().Count);
        FavoritesService.SaveOrder([@"C:\audit\c", @"C:\audit\a"]);
        Assert.Contains(@"C:\audit\b", FavoritesService.GetFavorites());
        File.WriteAllText(ConfigManager.GetFavoritesPath(), "broken");
        Assert.NotEmpty(FavoritesService.GetFavorites());
    }

    [Fact]
    public void ActivationArrivingDuringInitialDrainIsDeliveredExactlyOnce()
    {
        var dir = Path.Combine(_directory, "inbox"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "first.json"), "{\"Kind\":\"Normal\"}");
        int received = 0;
        using var inbox = new ActivationInbox(dir, _ =>
        {
            if (Interlocked.Increment(ref received) == 1)
                File.WriteAllText(Path.Combine(dir, "second.json"), "{\"Kind\":\"Normal\"}");
        });
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref received) == 2, TimeSpan.FromSeconds(5)));
        inbox.Drain(); Assert.Equal(2, received); Assert.Empty(Directory.GetFiles(dir, "*.json"));
    }

    [Fact]
    public void ActivationWaitsForLegacyWriterToFinish()
    {
        var dir = Path.Combine(_directory, "inbox"); Directory.CreateDirectory(dir);
        int received = 0;
        using var inbox = new ActivationInbox(dir, _ => Interlocked.Increment(ref received));
        using (var writer = new StreamWriter(new FileStream(Path.Combine(dir, "slow.json"), FileMode.Create, FileAccess.Write, FileShare.Read)))
        {
            writer.Write("{\"Kind\":"); writer.Flush(); inbox.Drain(); Assert.Equal(0, received);
            writer.Write("\"Normal\"}");
        }
        inbox.Drain(); Assert.Equal(1, received);
    }

    [Fact]
    public void InvalidActivationCannotCrashTheInboxOrBlockValidRequests()
    {
        var dir = Path.Combine(_directory, "inbox"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bad.json"), "[]");
        File.WriteAllText(Path.Combine(dir, "valid.json"), "{\"Kind\":\"Normal\"}");
        int received = 0;
        using var inbox = new ActivationInbox(dir, _ => received++);
        Assert.Equal(1, received);
        Assert.True(File.Exists(Path.Combine(dir, "bad.json.invalid")));
    }

    [Fact]
    public void DamagedFavoritesAreQuarantinedOnceAcrossRepeatedReads()
    {
        File.WriteAllText(ConfigManager.GetFavoritesPath(), "broken");
        for (int i = 0; i < 10; i++) Assert.Empty(FavoritesService.GetFavorites());
        Assert.Single(Directory.GetFiles(_directory, "favorites.json.corrupt-*"));
    }

    private static ProcessStartInfo PowerShell(string script)
    {
        var start = new ProcessStartInfo("powershell.exe");
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
        return start;
    }

    [Fact]
    public async Task LargeProcessOutputIsDrainedOnBothPipesWithBoundedCapture()
    {
        var result = await CapturedProcess.RunAsync(PowerShell("$s='x'*4096; 1..100 | % { [Console]::Out.WriteLine($s); [Console]::Error.WriteLine($s) }"), CancellationToken.None, TimeSpan.FromSeconds(30));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(128 * 1024, result.Output.Length); Assert.Equal(128 * 1024, result.Error.Length);
    }

    [Fact]
    public async Task CancellingProcessStopsItsExecution()
    {
        var pidFile = Path.Combine(_directory, "child.pid");
        using var cancel = new CancellationTokenSource();
        var task = CapturedProcess.RunAsync(PowerShell("[IO.File]::WriteAllText('" + pidFile.Replace("'", "''") + "',[string]$PID); Start-Sleep 60"), cancel.Token);
        int pid = 0;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            try { return File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile), out pid); }
            catch (IOException) { return false; }
        }, TimeSpan.FromSeconds(10)));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }
}
