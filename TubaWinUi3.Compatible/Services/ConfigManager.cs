using System;
using System.Collections.Generic;
using System.IO;
using TubaWinUi3.Compatible.Models;

namespace TubaWinUi3.Compatible.Services
{
    public enum ConfigLocation
    {
        AppData,
        AppRoot,
        Custom
    }

    public static class ConfigManager
    {
        private static readonly object _lock = new object();
        private static string _cachedDataDir;

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubaWinUi3");

        private static string AppRootDir
        {
            get { return Path.Combine(Path.GetDirectoryName(ToolCatalog.ToolsRoot), "Data"); }
        }

        public static string GetDataDir()
        {
            lock (_lock)
            {
                if (_cachedDataDir != null) return _cachedDataDir;
                var location = GetConfigLocation();
                _cachedDataDir = location == ConfigLocation.AppRoot ? AppRootDir : AppDataDir;
                if (location == ConfigLocation.Custom)
                {
                    var stored = File.ReadAllText(Path.Combine(AppRootDir, ".config_location")).Trim().Substring(7);
                    var expanded = stored.Replace("{AppDir}", Path.GetDirectoryName(ToolCatalog.ToolsRoot))
                        .Replace("{ToolsRoot}", ToolCatalog.ToolsRoot).Replace("{AppDataDir}", AppDataDir);
                    _cachedDataDir = Path.IsPathRooted(expanded) ? expanded
                        : Path.Combine(Path.GetDirectoryName(ToolCatalog.ToolsRoot), expanded);
                }
                return _cachedDataDir;
            }
        }

        public static ConfigLocation GetConfigLocation()
        {
            try
            {
                var markerPath = Path.Combine(AppRootDir, ".config_location");
                if (File.Exists(markerPath))
                {
                    var marker = File.ReadAllText(markerPath).Trim();
                    if (marker.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase)) return ConfigLocation.Custom;
                    if (marker.Equals("AppRoot", StringComparison.OrdinalIgnoreCase)) return ConfigLocation.AppRoot;
                }
            }
            catch { }
            return ConfigLocation.AppData;
        }

        public static bool SetConfigLocation(ConfigLocation location)
        {
            try
            {
                if (location == ConfigLocation.AppRoot)
                {
                    Directory.CreateDirectory(AppRootDir);
                    File.WriteAllText(Path.Combine(AppRootDir, ".config_location"), "AppRoot");
                }
                else
                {
                    var markerPath = Path.Combine(AppRootDir, ".config_location");
                    if (File.Exists(markerPath)) File.Delete(markerPath);
                }
                lock (_lock) { _cachedDataDir = null; }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetSettingsPath() { return Path.Combine(GetDataDir(), "settings.json"); }
        public static string GetFavoritesPath() { return Path.Combine(GetDataDir(), "favorites.json"); }
        public static string GetIconCacheDir() { return Path.Combine(GetDataDir(), "IconCache"); }

        public static void InvalidateAllCaches()
        {
            AppSettings.InvalidateCache();
            FavoritesService.InvalidateCache();
        }
    }
}
