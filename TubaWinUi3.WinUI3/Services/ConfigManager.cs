using System.IO.Compression;
using TubaWinUi3.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services;

public enum ConfigLocation
{
    AppData,
    AppRoot,
    Custom
}

public static class ConfigManager
{
    private static readonly object _lock = new();
    private static string? _cachedDataDir;
    internal static string? DataDirectoryOverride;
    private static ConfigLocation? _cachedLocation;

    private static readonly string AppDataDir = Path.Combine(
        RuntimeHelper.GetLocalAppDataRoot(),
        "TubaWinUi3");

    private static readonly string AppRootDir = Path.Combine(
        ToolCatalog.AppDirectory, "Data");

    internal static string? LocationMarkerOverride;
    private static string LocationMarkerPath => LocationMarkerOverride ?? ConfigLocationMarker.GetPath(
        RuntimeHelper.GetLocalAppDataRoot(), RuntimeHelper.IsPackagedContext ? Path.Combine(
            RuntimeHelper.GetLocalAppDataRoot(), "TubaWinUi3.Msix") : ToolCatalog.AppDirectory);
    private static string ReadLocationMarker()
    {
        if (File.Exists(LocationMarkerPath)) return File.ReadAllText(LocationMarkerPath).Trim();
        // Legacy portable marker is read-only fallback; new selections always override it.
        var legacy = Path.Combine(AppRootDir, CustomLocationFile);
        return File.Exists(legacy) ? File.ReadAllText(legacy).Trim() : "AppData";
    }

    private const string CustomLocationFile = ".config_location";
    private const string CustomPathPrefix = "Custom:";

    public static string GetDataDir()
    {
        lock (_lock)
        {
            if (DataDirectoryOverride is not null) return DataDirectoryOverride;
            if (_cachedDataDir is not null) return _cachedDataDir;

            var location = GetConfigLocation();
            _cachedDataDir = location switch
            {
                ConfigLocation.AppRoot => AppRootDir,
                ConfigLocation.Custom => ResolveCustomDataDir(),
                _ => AppDataDir
            };
            return _cachedDataDir;
        }
    }

    internal static void ResetLocationCache()
    {
        lock (_lock) { _cachedDataDir = null; _cachedLocation = null; }
    }

    public static ConfigLocation GetConfigLocation()
    {
        lock (_lock)
        {
            if (_cachedLocation is not null) return _cachedLocation.Value;

            try
            {
                var content = ReadLocationMarker();
                if (content.Length > 0)
                {
                    if (content.StartsWith(CustomPathPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        _cachedLocation = ConfigLocation.Custom;
                        return ConfigLocation.Custom;
                    }
                    if (content.Equals("AppRoot", StringComparison.OrdinalIgnoreCase))
                    {
                        _cachedLocation = ConfigLocation.AppRoot;
                        return ConfigLocation.AppRoot;
                    }
                }
            }
            catch { }

            _cachedLocation = ConfigLocation.AppData;
            return ConfigLocation.AppData;
        }
    }

    public static string? GetCustomPath()
    {
        try
        {
            var content = ReadLocationMarker();
            if (content.Length > 0)
            {
                if (content.StartsWith(CustomPathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return content[CustomPathPrefix.Length..];
                }
            }
        }
        catch { }
        return null;
    }

    private static string ResolveCustomDataDir()
    {
        var customPath = GetCustomPath();
        if (string.IsNullOrWhiteSpace(customPath)) return AppDataDir;
        var expanded = PathResolver.ExpandPath(customPath);
        if (Path.IsPathRooted(expanded)) return expanded;
        return Path.Combine(ToolCatalog.AppDirectory, expanded);
    }

    public static bool SetConfigLocation(ConfigLocation location, string? customPath = null)
    {
        try
        {
            AppSettings.Flush();
            var markerPath = LocationMarkerPath;
            if (location == ConfigLocation.Custom && string.IsNullOrWhiteSpace(customPath)) return false;
            var value = location == ConfigLocation.Custom ? CustomPathPrefix + customPath!.Trim() : location.ToString();
            AtomicFile.WriteText(markerPath, value);

            lock (_lock)
            {
                _cachedDataDir = null;
                _cachedLocation = null;
            }
            AppSettings.InvalidateCache();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string GetSettingsPath() => Path.Combine(GetDataDir(), "settings.json");
    public static string GetAiProvidersPath() => Path.Combine(GetDataDir(), "ai_providers.json");
    public static string GetFavoritesPath() => Path.Combine(GetDataDir(), "favorites.json");
    public static string GetLaunchHistoryPath() => Path.Combine(GetDataDir(), "launch_history.json");
    public static string GetPopupSettingsPath() => Path.Combine(GetDataDir(), "popup_settings.json");
    public static string GetSensorDumpPath() => Path.Combine(GetDataDir(), "sensor_dump.txt");
    public static string GetSkippedVersionPath() => Path.Combine(GetDataDir(), "skipped_version.txt");
    public static string GetIconCacheDir() => Path.Combine(GetDataDir(), "IconCache");
    public static string GetBackgroundsDir() => Path.Combine(GetDataDir(), "Backgrounds");
    public static string GetMetadataDir() => Path.Combine(GetDataDir(), "Metadata");
    public static string GetDownloadQueuePath() => Path.Combine(GetDataDir(), "download_queue.json");

    public static string GetDataSize()
    {
        try
        {
            var dir = GetDataDir();
            if (!Directory.Exists(dir)) return "0 B";
            long size = 0;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            if (size >= 1L << 30) return $"{(double)size / (1L << 30):F2} GB";
            if (size >= 1L << 20) return $"{(double)size / (1L << 20):F1} MB";
            if (size >= 1L << 10) return $"{(double)size / (1L << 10):F1} KB";
            return $"{size} B";
        }
        catch { return "未知"; }
    }

    public static bool MigrateData(ConfigLocation targetLocation, bool migrate, string? customPath = null)
    {
        AppSettings.Flush();
        _ = ToolMetadataService.GetUserTools(); // 迁移旧混合元数据后再搬数据目录。
        var sourceDir = GetDataDir();
        var oldDataDir = sourceDir;

        string targetDir;
        if (targetLocation == ConfigLocation.Custom)
        {
            if (string.IsNullOrWhiteSpace(customPath)) return false;
            var expanded = PathResolver.ExpandPath(customPath);
            targetDir = Path.IsPathRooted(expanded) ? expanded : Path.Combine(ToolCatalog.AppDirectory, expanded);
        }
        else
        {
            targetDir = targetLocation == ConfigLocation.AppRoot ? AppRootDir : AppDataDir;
        }

        if (string.Equals(Path.GetFullPath(sourceDir), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase)) return true;
        if (UserToolLibrary.IsWithin(targetDir, sourceDir) || UserToolLibrary.IsWithin(sourceDir, targetDir)) return false;

        try
        {
            if (migrate && Directory.Exists(sourceDir))
            {
                Directory.CreateDirectory(targetDir);

                var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IconCache" };
                var excludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sensor_dump.txt" };

                foreach (var file in Directory.EnumerateFiles(sourceDir))
                {
                    var name = Path.GetFileName(file);
                    if (excludeFiles.Contains(name)) continue;
                    var dest = Path.Combine(targetDir, name);
                    File.Copy(file, dest, true);
                }

                foreach (var dir in Directory.EnumerateDirectories(sourceDir))
                {
                    var name = Path.GetFileName(dir);
                    if (excludeDirs.Contains(name)) continue;
                    var destDir = Path.Combine(targetDir, name);
                    CopyDirectory(dir, destDir);
                }

            }

            if (!SetConfigLocation(targetLocation, customPath)) return false;

            if (migrate)
            {
                RewritePathsInDataDir(targetDir, oldDataDir);
                // Keep the source as a recovery copy; custom directories can contain unrelated files.
            }
            InvalidateAllCaches();

            return true;
        }
        catch { return false; }
    }

    public static void RewritePathsInDataDir(string dataDir, string? oldDataDir = null)
    {
        oldDataDir ??= dataDir;

        try
        {
            var favoritesPath = Path.Combine(dataDir, "favorites.json");
            if (File.Exists(favoritesPath))
            {
                var json = File.ReadAllText(favoritesPath);
                var paths = JsonSerializer.Deserialize<List<string>>(json);
                if (paths is not null)
                {
                    var rewritten = paths.Select(p => PathResolver.MakeRelative(p)).ToList();
                    File.WriteAllText(favoritesPath, JsonSerializer.Serialize(rewritten));
                }
            }
        }
        catch { }

        try
        {
            var historyPath = Path.Combine(dataDir, "launch_history.json");
            if (File.Exists(historyPath))
            {
                var json = File.ReadAllText(historyPath);
                var records = JsonSerializer.Deserialize<List<LaunchRecord>>(json);
                if (records is not null)
                {
                    foreach (var r in records)
                    {
                        r.Path = PathResolver.MakeRelative(r.Path);
                    }
                    File.WriteAllText(historyPath, JsonSerializer.Serialize(records));
                }
            }
        }
        catch { }

        try
        {
            var settingsPath = Path.Combine(dataDir, "settings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (settings is not null)
                {
                    var pathKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "BackgroundImagePath", "HttpDownloadPath"
                    };

                    var changed = false;
                    foreach (var key in pathKeys)
                    {
                        if (settings.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                        {
                            var rewritten = PathResolver.MakeRelative(val);
                            if (rewritten != val)
                            {
                                settings[key] = rewritten;
                                changed = true;
                            }
                        }
                    }

                    if (changed)
                    {
                        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));
                    }
                }
            }
        }
        catch { }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
    }

    public static async Task<bool> ExportConfigAsync(string outputPath)
    {
        try
        {
            AppSettings.Flush();
            _ = ToolMetadataService.GetUserTools();
            var dataDir = GetDataDir();
            if (!Directory.Exists(dataDir)) return false;

            var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IconCache", "Metadata" };
            var excludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sensor_dump.txt" };

            var userSnapshot = UserToolLibrary.CreateExportSnapshot();

            if (File.Exists(outputPath)) File.Delete(outputPath);

            await Task.Run(() =>
            {
                using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);
                foreach (var file in Directory.EnumerateFiles(dataDir))
                {
                    var name = Path.GetFileName(file);
                    if (ConfigBackupPolicy.Exclude(name) || name.Equals("user-tools.json", StringComparison.OrdinalIgnoreCase) || excludeFiles.Contains(name) || Path.GetFullPath(file).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)) continue;
                    if (Path.GetExtension(name).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                        writer.Write(UserToolLibrary.RewriteExportReferences(ConfigBackupPolicy.Sanitize(File.ReadAllText(file)), userSnapshot.Directories));
                    }
                    else zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
                }
                foreach (var dir in Directory.EnumerateDirectories(dataDir))
                {
                    var dirName = Path.GetFileName(dir);
                    if (excludeDirs.Contains(dirName) || ConfigBackupPolicy.Exclude(dirName)) continue;
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        if (Path.GetFullPath(file).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)) continue;
                        var relative = file.Substring(dataDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (ConfigBackupPolicy.Exclude(relative)) continue;
                        if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            using var writer = new StreamWriter(zip.CreateEntry(relative).Open());
                            writer.Write(ConfigBackupPolicy.Sanitize(File.ReadAllText(file)));
                        }
                        else zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                    }
                }
                foreach (var (entry, source) in userSnapshot.Files)
                    if (!Path.GetFullPath(source).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                        zip.CreateEntryFromFile(source, entry, CompressionLevel.Optimal);
                if (userSnapshot.Catalog["tools"] is JsonArray)
                {
                    using var writer = new StreamWriter(zip.CreateEntry("user-tools.json").Open());
                    writer.Write(userSnapshot.Catalog.ToJsonString());
                }
            });

            return true;
        }
        catch { return false; }
    }

    public static async Task<bool> ImportConfigAsync(string zipPath)
    {
        try
        {
            AppSettings.Flush();
            var dataDir = GetDataDir();
            await Task.Run(() => ConfigImportTransaction.Import(zipPath, dataDir));

            InvalidateAllCaches();
            return true;
        }
        catch { return false; }
    }

    public static void InvalidateAllCaches()
    {
        AppSettings.InvalidateCache();
        FavoritesService.InvalidateCache();
        LaunchHistoryService.InvalidateCache();
        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateTagsCache();
    }

    private const int CurrentPathMigrationVersion = 1;

    public static void AutoMigratePathsIfNeeded()
    {
        try
        {
            var dataDir = GetDataDir();
            if (!Directory.Exists(dataDir)) return;

            var markerPath = Path.Combine(dataDir, ".path_migration_done");
            if (File.Exists(markerPath)) return;

            RewritePathsInDataDir(dataDir);

            try
            {
                File.WriteAllText(markerPath, CurrentPathMigrationVersion.ToString());
            }
            catch { }

            AppSettings.InvalidateCache();
            FavoritesService.InvalidateCache();
            LaunchHistoryService.InvalidateCache();
        }
        catch { }
    }
}
