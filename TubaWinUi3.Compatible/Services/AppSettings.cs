using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TubaWinUi3.Compatible.Services
{
    public static class AppSettings
    {
        public static event Action<string> SettingChanged;

        private static string SettingsPath { get { return ConfigManager.GetSettingsPath(); } }

        private static Dictionary<string, string> _cache;
        private static bool _dirty;

        public static Dictionary<string, string> Load()
        {
            if (_cache != null) return _cache;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _cache = ParseSettings(json);
                }
                else
                {
                    _cache = new Dictionary<string, string>();
                }
            }
            catch
            {
                _cache = new Dictionary<string, string>();
            }
            return _cache;
        }

        public static void Save()
        {
            if (!_dirty || _cache == null) return;
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var root = new JObject();
                foreach (var pair in _cache)
                    root[pair.Key] = pair.Value;
                File.WriteAllText(SettingsPath, root.ToString(Formatting.Indented));
                _dirty = false;
            }
            catch { }
        }

        public static void Set(string key, string value)
        {
            var s = Load();
            s[key] = value;
            _dirty = true;
            Save();
            if (SettingChanged != null) SettingChanged(key);
        }

        public static void Set(string key, bool value) { Set(key, value.ToString().ToLowerInvariant()); }
        public static void Set(string key, int value) { Set(key, value.ToString()); }

        public static void Remove(string key)
        {
            var s = Load();
            s.Remove(key);
            _dirty = true;
            Save();
            if (SettingChanged != null) SettingChanged(key);
        }

        public static string Get(string key)
        {
            var s = Load();
            string v;
            return s.TryGetValue(key, out v) ? v : null;
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            var v = Get(key);
            bool b;
            return v != null && bool.TryParse(v, out b) ? b : defaultValue;
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            var v = Get(key);
            int i;
            return v != null && int.TryParse(v, out i) ? i : defaultValue;
        }

        public static void InvalidateCache()
        {
            _cache = null;
            _dirty = false;
        }

        /// <summary>
        /// 只用 Newtonsoft 的 DOM（JObject）读取，避免 JsonConvert.DeserializeObject&lt;T&gt;
        /// 的反射反序列化——后者在 NativeAOT 下不可用（PE 兼容版直接 AOT 发布）。
        /// </summary>
        private static Dictionary<string, string> ParseSettings(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            var root = JObject.Parse(json);
            foreach (var pair in root)
            {
                var token = pair.Value;
                if (token == null || token.Type == JTokenType.Null)
                    continue;

                result[pair.Key] = token.Type == JTokenType.String ? (string)token : token.ToString();
            }

            return result;
        }
    }
}
