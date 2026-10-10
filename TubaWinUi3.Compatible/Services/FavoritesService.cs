using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TubaWinUi3.Compatible.Services
{
    public static class FavoritesService
    {
        private static string FavoritesPath { get { return ConfigManager.GetFavoritesPath(); } }
        private static List<string> _cache;

        public static IReadOnlyList<string> GetFavorites()
        {
            if (_cache != null)
                return _cache;

            try
            {
                if (File.Exists(FavoritesPath))
                {
                    var json = File.ReadAllText(FavoritesPath);
                    _cache = (JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>())
                        .Select(ToolMetadataService.ResolveUserDirectory).ToList();
                }
                else
                {
                    _cache = new List<string>();
                }
            }
            catch
            {
                _cache = new List<string>();
            }

            return _cache;
        }

        public static bool IsFavorite(string toolPath)
        {
            var favs = GetFavorites();
            for (int i = 0; i < favs.Count; i++)
            {
                if (string.Equals(favs[i], toolPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static void AddFavorite(string toolPath)
        {
            var list = new List<string>();
            foreach (var p in GetFavorites())
            {
                if (!string.IsNullOrWhiteSpace(p))
                    list.Add(p);
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], toolPath, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            list.Add(toolPath);
            _cache = list;
            Save(list);
        }

        public static void RemoveFavorite(string toolPath)
        {
            var list = new List<string>();
            foreach (var p in GetFavorites())
            {
                if (!string.Equals(p, toolPath, StringComparison.OrdinalIgnoreCase))
                    list.Add(p);
            }

            _cache = list;
            Save(list);
        }

        public static void ToggleFavorite(string toolPath)
        {
            if (IsFavorite(toolPath))
                RemoveFavorite(toolPath);
            else
                AddFavorite(toolPath);
        }

        public static void InvalidateCache()
        {
            _cache = null;
        }

        private static void Save(List<string> favorites)
        {
            try
            {
                var dir = Path.GetDirectoryName(FavoritesPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(favorites);
                File.WriteAllText(FavoritesPath, json);
            }
            catch { }
        }
    }
}
