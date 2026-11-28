using System.Text.Json;

namespace Tubatools.MinecraftLauncher.Services;

/// <summary>启动器设置，持久化到插件数据目录的 settings.json。</summary>
internal sealed class LauncherSettings
{
    public string GameDirectory { get; set; } = DefaultGameDirectory;
    public string? JavaPath { get; set; }
    public int MemoryMb { get; set; } = DefaultMemoryMb();
    public bool VersionIsolation { get; set; } = true;
    public int WindowWidth { get; set; } = 854;
    public int WindowHeight { get; set; } = 480;
    public string JvmArgs { get; set; } = "";
    public string GameArgs { get; set; } = "";
    public string Mirror { get; set; } = "auto";
    public string? LastVersion { get; set; }
    public string GameLanguage { get; set; } = "auto";
    public string MicrosoftClientId { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string DataDirectory { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string DefaultGameDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    public static int DefaultMemoryMb()
    {
        try
        {
            var totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            var half = (int)Math.Clamp(totalMb / 2, 1024, 8192);
            return Math.Clamp(half / 512 * 512, 1024, 8192);
        }
        catch
        {
            return 4096;
        }
    }

    public static LauncherSettings Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "settings.json");
        LauncherSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), JsonOptions) ?? new LauncherSettings()
                : new LauncherSettings();
        }
        catch
        {
            settings = new LauncherSettings();
        }

        settings.DataDirectory = dataDirectory;
        if (string.IsNullOrWhiteSpace(settings.GameDirectory))
            settings.GameDirectory = DefaultGameDirectory;
        settings.MemoryMb = Math.Clamp(settings.MemoryMb, 512, 32768);
        settings.WindowWidth = Math.Clamp(settings.WindowWidth, 320, 3840);
        settings.WindowHeight = Math.Clamp(settings.WindowHeight, 240, 2160);
        settings.Mirror = McMirror.Normalize(settings.Mirror);
        return settings;
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(DataDirectory)) return;
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, "settings.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, true);
    }

    public string EffectiveJavaPath => JavaPath ?? "";
}
