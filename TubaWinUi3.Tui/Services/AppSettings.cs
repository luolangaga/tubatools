using System.Text.Json.Nodes;

namespace TubaWinUi3.Tui.Services;

internal static class AppSettings
{
    private static JsonObject _settings;

    public static string Get(string key)
    {
        if (_settings == null)
        {
            try
            {
                _settings = JsonNode.Parse(File.ReadAllText(ConfigManager.GetSettingsPath())) as JsonObject ?? new();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                _settings = new();
            }
        }

        return _settings[key]?.ToString();
    }

    public static bool GetBool(string key, bool fallback) =>
        bool.TryParse(Get(key), out var value) ? value : fallback;
}
