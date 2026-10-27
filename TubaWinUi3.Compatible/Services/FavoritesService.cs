using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
                    _cache = ParseFavorites(json);
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
                var array = new JArray();
                foreach (var favorite in favorites)
                    array.Add(favorite);
                File.WriteAllText(FavoritesPath, array.ToString(Formatting.None));
            }
            catch { }
        }

        /// <summary>
        /// 只用 Newtonsoft 的 DOM（JArray）读取，避免 JsonConvert.DeserializeObject&lt;List&lt;string&gt;&gt;
        /// 的反射反序列化——后者在 NativeAOT 下不可用（PE 兼容版直接 AOT 发布）。
        /// </summary>
        private static List<string> ParseFavorites(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            var array = JArray.Parse(json);
            foreach (var token in array)
            {
                if (token == null || token.Type == JTokenType.Null)
                    continue;

                var value = token.ToString();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                result.Add(ToolMetadataService.ResolveUserDirectory(value));
            }

            return result;
        }
    }
}
