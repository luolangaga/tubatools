using TubaWinUi3.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using TubaWinUi3.Tui.Models;

namespace TubaWinUi3.Tui.Services
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
        private static string _cachedMarker;

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
                var marker = File.Exists(MarkerPath) ? File.ReadAllText(MarkerPath) : "AppData";
                if (_cachedDataDir != null && marker == _cachedMarker) return _cachedDataDir;
                _cachedMarker = marker;
                var location = GetConfigLocation();
                _cachedDataDir = location == ConfigLocation.AppRoot ? AppRootDir : AppDataDir;
                if (location == ConfigLocation.Custom)
                {
                    var stored = File.ReadAllText(MarkerPath).Trim().Substring(7);
                    var expanded = stored.Replace("{AppDir}", Path.GetDirectoryName(ToolCatalog.ToolsRoot))
                        .Replace("{ToolsRoot}", ToolCatalog.ToolsRoot).Replace("{AppDataDir}", AppDataDir);
                    _cachedDataDir = Path.IsPathRooted(expanded) ? expanded
                        : Path.Combine(Path.GetDirectoryName(ToolCatalog.ToolsRoot), expanded);
                }
                return _cachedDataDir;
            }
        }

        private static string MarkerPath
        {
            get
            {
                var current = ConfigLocationMarker.GetPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Path.GetDirectoryName(ToolCatalog.ToolsRoot));
                return File.Exists(current) ? current : Path.Combine(AppRootDir, ".config_location");
            }
        }

        public static ConfigLocation GetConfigLocation()
        {
            try
            {
                var markerPath = MarkerPath;
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

        public static string GetSettingsPath() => Path.Combine(GetDataDir(), "settings.json");
        public static string GetFavoritesPath() => Path.Combine(GetDataDir(), "favorites.json");
    }
}
