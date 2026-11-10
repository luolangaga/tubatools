using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace Tubatools.Windows11Optimizer;

internal static class TweakEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool? ReadToggle(TweakDefinition tweak)
    {
        try
        {
            if (tweak.ReadToggle is not null) return tweak.ReadToggle();
            if (tweak.Ops.Count == 0) return null;
            // Values that Windows has never written follow the Windows default (e.g. Game Mode is on).
            if (tweak.Ops.All(op => !RegistryAccess.Exists(op.Target))) return tweak.DefaultOn;
            return tweak.Ops.All(op => RegistryAccess.Matches(op.Target, op.OnValue));
        }
        catch
        {
            return null;
        }
    }

    public static int ReadChoiceIndex(TweakDefinition tweak)
    {
        try
        {
            if (tweak.ReadChoiceIndex is not null) return tweak.ReadChoiceIndex();
            for (var i = 0; i < tweak.Choices.Count; i++)
            {
                var choice = tweak.Choices[i];
                if (choice.Ops.Count > 0 && choice.Ops.All(op => RegistryAccess.Matches(op.Target, op.OnValue)))
                    return i;
            }
        }
        catch
        {
            // A missing key simply means "system default".
        }
        return -1;
    }

    public static async Task ApplyToggleAsync(TweakDefinition tweak, bool on, CancellationToken ct)
    {
        if (tweak.ApplyToggle is not null)
        {
            await tweak.ApplyToggle(on, ct).ConfigureAwait(false);
            return;
        }
        foreach (var op in tweak.Ops) RegistryAccess.Write(op.Target, on ? op.OnValue : op.OffValue);
    }

    public static async Task ApplyChoiceAsync(TweakDefinition tweak, int index, CancellationToken ct)
    {
        if (index < 0 || index >= tweak.Choices.Count) return;
        var choice = tweak.Choices[index];
        if (choice.Apply is not null) await choice.Apply(ct).ConfigureAwait(false);
        foreach (var op in choice.Ops) RegistryAccess.Write(op.Target, op.OnValue);
    }

    public static IEnumerable<RegistryTarget> AllBackupTargets()
    {
        var targets = TweakCatalog.Tweaks
            .SelectMany(t => t.Ops.Select(op => op.Target)
                .Concat(t.Choices.SelectMany(c => c.Ops.Select(op => op.Target)))
                .Concat(t.ExtraBackupTargets))
            .GroupBy(t => (t.Hive, t.Path, t.Name))
            .Select(g => g.First());
        return targets;
    }

    public static async Task<string> CreateBackupAsync(string dataDirectory, CancellationToken ct)
    {
        var entries = new List<BackupValue>();
        foreach (var target in AllBackupTargets())
        {
            ct.ThrowIfCancellationRequested();
            entries.Add(ReadBackupValue(target));
        }

        var directory = Path.Combine(dataDirectory, "backups");
        Directory.CreateDirectory(directory);
        var document = new BackupDocument { CreatedAt = DateTimeOffset.Now, Entries = entries };
        var path = Path.Combine(directory, $"optimizer-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(document, JsonOptions), ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
        PruneBackups(directory);
        return path;
    }

    public static string? FindLatestBackup(string dataDirectory)
    {
        var directory = Path.Combine(dataDirectory, "backups");
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "optimizer-*.json")
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static async Task<(int Restored, int Skipped)> RestoreBackupAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var document = JsonSerializer.Deserialize<BackupDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("备份文件内容无效。");
        if (document.Entries is null) throw new InvalidDataException("备份文件没有注册表条目。");

        var restored = 0;
        var skipped = 0;
        foreach (var entry in document.Entries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var hive = entry.Hive switch
                {
                    "CurrentUser" => RegistryHive.CurrentUser,
                    "LocalMachine" => RegistryHive.LocalMachine,
                    _ => RegistryHive.CurrentUser
                };
                var kind = Enum.TryParse<RegistryValueKind>(entry.Kind, out var parsed) ? parsed : RegistryValueKind.String;
                var target = new RegistryTarget(hive, entry.Path, entry.Name, kind);
                RegistryAccess.Write(target, entry.Exists ? ParseBackupValue(entry) : null);
                restored++;
            }
            catch
            {
                skipped++;
            }
        }
        return (restored, skipped);
    }

    public static async Task<string> RestartExplorerAsync(CancellationToken ct)
    {
        await ShellRunner.RunAsync("taskkill.exe", "/f /im explorer.exe", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        await Task.Delay(700, ct).ConfigureAwait(false);
        ShellRunner.StartDetached("explorer.exe");
        return "资源管理器已重启。";
    }

    public static async Task<string> ClearIconCacheAsync(CancellationToken ct)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var explorerCache = Path.Combine(local, "Microsoft", "Windows", "Explorer");
        var files = new List<string>();
        if (Directory.Exists(explorerCache))
        {
            files.AddRange(Directory.EnumerateFiles(explorerCache, "iconcache*.db"));
            files.AddRange(Directory.EnumerateFiles(explorerCache, "thumbcache*.db"));
        }
        var legacy = Path.Combine(local, "IconCache.db");
        if (File.Exists(legacy)) files.Add(legacy);

        await ShellRunner.RunAsync("taskkill.exe", "/f /im explorer.exe", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        await Task.Delay(600, ct).ConfigureAwait(false);

        var deleted = 0;
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch
            {
                // Files still mapped by another process are skipped; Explorer restart retries next time.
            }
        }

        ShellRunner.StartDetached("explorer.exe");
        return $"已清理 {deleted} 个图标/缩略图缓存文件，资源管理器已重启。";
    }

    public static async Task<string> ResetFolderViewsAsync(CancellationToken ct)
    {
        await ShellRunner.RunAsync("taskkill.exe", "/f /im explorer.exe", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        await Task.Delay(600, ct).ConfigureAwait(false);
        var keys = new[]
        {
            @"Software\Microsoft\Windows\Shell\Bags",
            @"Software\Microsoft\Windows\Shell\BagMRU",
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags",
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU"
        };
        foreach (var key in keys)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                baseKey.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
            }
            catch
            {
                // Explorer may still hold the key for a moment.
            }
        }
        ShellRunner.StartDetached("explorer.exe");
        return "文件夹视图已重置为 Windows 默认。";
    }

    public static async Task<string> PauseWindowsUpdateAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var end = now.AddDays(30);
        const string path = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        var values = new (string Name, string Value)[]
        {
            ("PauseFeatureUpdatesStartTime", now.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            ("PauseFeatureUpdatesEndTime", end.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            ("PauseQualityUpdatesStartTime", now.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            ("PauseQualityUpdatesEndTime", end.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            ("PauseUpdatesStartTime", now.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            ("PauseUpdatesExpiryTime", end.ToString("yyyy-MM-ddTHH:mm:ssZ"))
        };
        foreach (var (name, value) in values)
            RegistryAccess.Write(new RegistryTarget(RegistryHive.LocalMachine, path, name, RegistryValueKind.String), value);
        await Task.CompletedTask.ConfigureAwait(false);
        return $"Windows 更新已暂停，恢复时间：{end.ToLocalTime():yyyy-MM-dd HH:mm}。";
    }

    public static Task<string> ResumeWindowsUpdateAsync(CancellationToken ct)
    {
        const string path = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        foreach (var name in new[]
        {
            "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
            "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime",
            "PauseUpdatesStartTime", "PauseUpdatesExpiryTime"
        })
        {
            RegistryAccess.Write(new RegistryTarget(RegistryHive.LocalMachine, path, name, RegistryValueKind.String), null);
        }
        return Task.FromResult("已取消 Windows 更新暂停，请到设置中检查更新。");
    }

    private static BackupValue ReadBackupValue(RegistryTarget target)
    {
        var exists = RegistryAccess.Exists(target);
        var raw = exists ? RegistryAccess.Read(target) : null;
        return new BackupValue
        {
            Hive = target.Hive.ToString(),
            Path = target.Path,
            Name = target.Name,
            Kind = target.Kind.ToString(),
            Exists = exists,
            Value = exists ? EncodeValue(target.Kind, raw) : null
        };
    }

    private static string? EncodeValue(RegistryValueKind kind, object? value) => value switch
    {
        null => null,
        int i => i.ToString(),
        long l => l.ToString(),
        string s => s,
        string[] array => string.Join('\n', array),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
    };

    private static object? ParseBackupValue(BackupValue entry)
    {
        if (entry.Value is null) return null;
        return entry.Kind switch
        {
            nameof(RegistryValueKind.DWord) => int.TryParse(entry.Value, out var dword) ? dword : 0,
            nameof(RegistryValueKind.QWord) => long.TryParse(entry.Value, out var qword) ? qword : 0L,
            nameof(RegistryValueKind.Binary) => Convert.FromBase64String(entry.Value),
            nameof(RegistryValueKind.MultiString) => entry.Value.Split('\n'),
            _ => entry.Value
        };
    }

    private static void PruneBackups(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "optimizer-*.json")
                         .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                         .Skip(20))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Pruning is cosmetic.
        }
    }

    internal sealed class BackupDocument
    {
        public DateTimeOffset CreatedAt { get; set; }
        public List<BackupValue> Entries { get; set; } = [];
    }

    internal sealed class BackupValue
    {
        public string Hive { get; set; } = "";
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public bool Exists { get; set; }
        public string? Value { get; set; }
    }
}
