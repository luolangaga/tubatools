using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tubatools.NexBoxSkin;

/// <summary>
/// Per-monitor display filter implemented with the GDI gamma ramp, matching
/// NexBox's <c>build_gamma_ramp</c> math. No driver or overlay process is needed.
/// </summary>
internal static class DisplayFilterService
{
    private const uint MonitorInfoPrimary = 0x1;

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private static readonly Dictionary<string, bool> Applied = new(StringComparer.OrdinalIgnoreCase);
    private static readonly FilterState Defaults = new();

    private static string? _dataDirectory;
    private static string? _settingsPath;
    private static FilterFile _file = new();
    private static bool _loaded;

    private static readonly FilterPreset[] Presets =
    [
        new("normal", "标准", 0),
        new("vivid", "鲜艳", 1),
        new("movie", "电影", 2),
        new("highlight", "高亮", 3),
        new("soft", "柔和", 4),
        new("gaming", "游戏", 5),
        new("reading", "阅读", 6),
        new("deexposure", "去曝光", 7),
        new("shadow-boost", "暗部增强", 8),
        new("benq", "BenQ", 9),
    ];

    public static void Configure(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) return;
        lock (Gate)
        {
            if (_loaded && string.Equals(_dataDirectory, dataDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _dataDirectory = dataDirectory;
            _settingsPath = Path.Combine(dataDirectory, "display-filter.json");
            LoadLocked();
        }
    }

    public static object GetDisplays() =>
        EnumerateDisplays()
            .Select(display => new
            {
                index = display.Index,
                name = display.Primary ? $"显示器 {display.Index + 1}（主）" : $"显示器 {display.Index + 1}",
                device = display.DeviceName,
                width = display.Width,
                height = display.Height,
                primary = display.Primary,
            })
            .ToArray();

    public static object GetSettings(int displayIndex)
    {
        lock (Gate)
        {
            var display = FindDisplay(displayIndex);
            var state = display is null ? Defaults : GetState(display);
            return new
            {
                displayIndex,
                device = display?.DeviceName ?? "",
                enabled = state.Enabled,
                temperature = state.Temperature,
                brightness = state.Brightness,
                contrast = state.Contrast,
                saturation = state.Saturation,
                rGamma = state.RGamma,
                gGamma = state.GGamma,
                bGamma = state.BGamma,
                shadow = state.Shadow,
                mode = state.Mode,
            };
        }
    }

    public static object GetPresets() =>
        Presets.Select(preset => new
        {
            id = preset.Id,
            name = preset.Name,
            group = "新境盒",
            temperature = Defaults.Temperature,
            brightness = Defaults.Brightness,
            contrast = Defaults.Contrast,
            saturation = Defaults.Saturation,
            mode = preset.Mode,
        }).ToArray();

    public static object SetSettings(int displayIndex, JsonElement args)
    {
        lock (Gate)
        {
            var display = FindDisplay(displayIndex);
            if (display is null) return Result(false, "找不到该显示器。");

            var state = GetState(display);
            state.Enabled = OptionalBool(args, "enabled", state.Enabled);
            state.Temperature = Clamp(OptionalInt(args, "temperature", state.Temperature), 1000, 10000);
            state.Brightness = Clamp(OptionalInt(args, "brightness", state.Brightness), 50, 150);
            state.Contrast = Clamp(OptionalInt(args, "contrast", state.Contrast), 50, 150);
            state.Saturation = Clamp(OptionalInt(args, "saturation", state.Saturation), 50, 150);
            state.RGamma = Clamp(OptionalDouble(args, "rGamma", state.RGamma), 0.5, 2.5);
            state.GGamma = Clamp(OptionalDouble(args, "gGamma", state.GGamma), 0.5, 2.5);
            state.BGamma = Clamp(OptionalDouble(args, "bGamma", state.BGamma), 0.5, 2.5);
            state.Shadow = Clamp(OptionalDouble(args, "shadow", state.Shadow), -100, 100);
            state.Mode = Clamp(OptionalInt(args, "mode", state.Mode), 0, 9);

            if (!state.Enabled)
            {
                Restore(display, state);
                SaveLocked();
                return Result(true, null);
            }

            var applied = Apply(display, state);
            SaveLocked();
            return Result(applied, applied ? null : "当前显示器拒绝修改 Gamma 曲线（HDR 模式或显卡驱动限制）。");
        }
    }

    public static object ApplyPreset(int displayIndex, string presetId)
    {
        lock (Gate)
        {
            var display = FindDisplay(displayIndex);
            if (display is null) return Result(false, "找不到该显示器。");

            var preset = Presets.FirstOrDefault(item =>
                string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
            if (preset is null) return Result(false, "未知的滤镜预设。");

            var state = GetState(display);
            state.Enabled = true;
            state.Temperature = Defaults.Temperature;
            state.Brightness = Defaults.Brightness;
            state.Contrast = Defaults.Contrast;
            state.Saturation = Defaults.Saturation;
            state.RGamma = 1;
            state.GGamma = 1;
            state.BGamma = 1;
            state.Shadow = 0;
            state.Mode = preset.Mode;

            var applied = Apply(display, state);
            SaveLocked();
            return new
            {
                ok = applied,
                error = applied ? null : "当前显示器拒绝修改 Gamma 曲线（HDR 模式或显卡驱动限制）。",
                settings = GetSettingsLocked(displayIndex, display, state),
            };
        }
    }

    public static object Reset(int displayIndex)
    {
        lock (Gate)
        {
            var display = FindDisplay(displayIndex);
            if (display is null) return Result(false, "找不到该显示器。");

            var current = GetState(display);
            Restore(display, current);

            var reset = new FilterState();
            if (current.OriginalRamp is { Length: 768 })
            {
                reset.OriginalRamp = current.OriginalRamp;
            }

            _file.Displays[display.DeviceName] = reset;
            SaveLocked();
            return new
            {
                ok = true,
                error = (string?)null,
                settings = GetSettingsLocked(displayIndex, display, reset),
            };
        }
    }

    public static object OpenColorManagement()
    {
        try
        {
            Process.Start(new ProcessStartInfo("colorcpl.exe") { UseShellExecute = true });
            return Result(true, null);
        }
        catch (Exception ex)
        {
            return Result(false, ex.Message);
        }
    }

    /// <summary>Re-applies filters that were left enabled by the previous session.</summary>
    public static void ApplyPersisted()
    {
        lock (Gate)
        {
            foreach (var display in EnumerateDisplays())
            {
                if (_file.Displays.TryGetValue(display.DeviceName, out var state) && state.Enabled)
                {
                    Apply(display, state);
                }
            }
        }
    }

    /// <summary>Restores every captured original ramp. Safe to call repeatedly.</summary>
    public static void RestoreAll()
    {
        lock (Gate)
        {
            foreach (var display in EnumerateDisplays())
            {
                if (_file.Displays.TryGetValue(display.DeviceName, out var state))
                {
                    Restore(display, state);
                }
            }

            Applied.Clear();
        }
    }

    private static object GetSettingsLocked(int displayIndex, DisplayInfo display, FilterState state) =>
        new
        {
            displayIndex,
            device = display.DeviceName,
            enabled = state.Enabled,
            temperature = state.Temperature,
            brightness = state.Brightness,
            contrast = state.Contrast,
            saturation = state.Saturation,
            rGamma = state.RGamma,
            gGamma = state.GGamma,
            bGamma = state.BGamma,
            shadow = state.Shadow,
            mode = state.Mode,
        };

    private static bool Apply(DisplayInfo display, FilterState state)
    {
        if (state.OriginalRamp is not { Length: 768 })
        {
            var current = ReadRamp(display);
            if (current is null) return false;
            state.OriginalRamp = current.Select(value => (int)value).ToArray();
        }

        var ramp = BuildRamp(state);
        var written = WriteRamp(display, ramp);
        Applied[display.DeviceName] = written;
        return written;
    }

    private static void Restore(DisplayInfo display, FilterState state)
    {
        if (state.OriginalRamp is { Length: 768 } original)
        {
            var ramp = new ushort[768];
            for (var i = 0; i < ramp.Length; i++)
            {
                ramp[i] = (ushort)Math.Clamp(original[i], 0, 65535);
            }

            WriteRamp(display, ramp);
        }

        Applied[display.DeviceName] = false;
    }

    private static FilterState GetState(DisplayInfo display)
    {
        if (!_file.Displays.TryGetValue(display.DeviceName, out var state))
        {
            state = new FilterState();
            _file.Displays[display.DeviceName] = state;
        }

        return state;
    }

    private static void LoadLocked()
    {
        _loaded = true;
        _file = new FilterFile();
        if (string.IsNullOrEmpty(_settingsPath) || !File.Exists(_settingsPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<FilterFile>(json, JsonOptions);
            if (loaded?.Displays is { Count: > 0 })
            {
                _file.Displays = new Dictionary<string, FilterState>(loaded.Displays, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            _file = new FilterFile();
        }
    }

    private static void SaveLocked()
    {
        if (string.IsNullOrEmpty(_settingsPath)) return;
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _settingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_file, JsonOptions));
            File.Move(temporary, _settingsPath, true);
        }
        catch
        {
            // The filter still works for this session; persistence is best-effort.
        }
    }

    private static DisplayInfo? FindDisplay(int index)
    {
        var displays = EnumerateDisplays();
        return index >= 0 && index < displays.Count ? displays[index] : null;
    }

    private static List<DisplayInfo> EnumerateDisplays()
    {
        var displays = new List<DisplayInfo>();
        MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var info = new MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<MONITORINFOEX>(),
                szDevice = string.Empty,
            };

            if (GetMonitorInfo(hMonitor, ref info))
            {
                displays.Add(new DisplayInfo(
                    displays.Count,
                    info.szDevice,
                    info.rcMonitor.Right - info.rcMonitor.Left,
                    info.rcMonitor.Bottom - info.rcMonitor.Top,
                    (info.dwFlags & MonitorInfoPrimary) != 0));
            }

            return true;
        };

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return displays;
    }

    private static ushort[]? ReadRamp(DisplayInfo display)
    {
        var dc = CreateDC("DISPLAY", display.DeviceName, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        try
        {
            var ramp = new ushort[768];
            return GetDeviceGammaRamp(dc, ramp) ? ramp : null;
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    private static bool WriteRamp(DisplayInfo display, ushort[] ramp)
    {
        var dc = CreateDC("DISPLAY", display.DeviceName, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        try
        {
            return SetDeviceGammaRamp(dc, ramp);
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    private static ushort[] BuildRamp(FilterState state)
    {
        var (rTemperature, gTemperature, bTemperature) = KelvinToRgb(state.Temperature);
        var brightness = state.Brightness / 100.0;
        var contrast = state.Contrast / 100.0;
        var saturation = state.Saturation / 100.0;
        var (modeGamma, sCurve, rBoost, gBoost, bBoost) = ModeParams(state.Mode);

        var perChannel = Math.Abs(state.RGamma - 1) > 0.001
            || Math.Abs(state.GGamma - 1) > 0.001
            || Math.Abs(state.BGamma - 1) > 0.001;
        var rGamma = perChannel ? state.RGamma : modeGamma;
        var gGamma = perChannel ? state.GGamma : modeGamma;
        var bGamma = perChannel ? state.BGamma : modeGamma;

        var ramp = new ushort[768];
        for (var i = 0; i < 256; i++)
        {
            var input = i / 255.0;
            var r = SCurve(Gamma(input, rGamma), sCurve);
            var g = SCurve(Gamma(input, gGamma), sCurve);
            var b = SCurve(Gamma(input, bGamma), sCurve);

            r = ((r - 0.5) * contrast + 0.5) * brightness;
            g = ((g - 0.5) * contrast + 0.5) * brightness;
            b = ((b - 0.5) * contrast + 0.5) * brightness;

            var rBase = Math.Clamp(r, 0, 1) * 65535.0;
            var gBase = Math.Clamp(g, 0, 1) * 65535.0;
            var bBase = Math.Clamp(b, 0, 1) * 65535.0;

            var rFinal = Math.Min(rBase * rTemperature * rBoost, 65535.0);
            var gFinal = Math.Min(gBase * gTemperature * gBoost, 65535.0);
            var bFinal = Math.Min(bBase * bTemperature * bBoost, 65535.0);

            var luma = 0.299 * rFinal + 0.587 * gFinal + 0.114 * bFinal;
            var rOut = Math.Abs(saturation - 1) > 0.001 ? luma + (rFinal - luma) * saturation : rFinal;
            var gOut = Math.Abs(saturation - 1) > 0.001 ? luma + (gFinal - luma) * saturation : gFinal;
            var bOut = Math.Abs(saturation - 1) > 0.001 ? luma + (bFinal - luma) * saturation : bFinal;

            ramp[i] = (ushort)Math.Clamp(Math.Round(rOut), 0, 65535);
            ramp[256 + i] = (ushort)Math.Clamp(Math.Round(gOut), 0, 65535);
            ramp[512 + i] = (ushort)Math.Clamp(Math.Round(bOut), 0, 65535);
        }

        if (Math.Abs(state.Shadow) > 0.001)
        {
            ApplyShadowLift(ramp, state.Shadow);
        }

        for (var channel = 0; channel < 3; channel++)
        {
            var offset = channel * 256;
            for (var i = 1; i < 256; i++)
            {
                if (ramp[offset + i] < ramp[offset + i - 1])
                {
                    ramp[offset + i] = ramp[offset + i - 1];
                }
            }
        }

        ramp[0] = 0;
        ramp[256] = 0;
        ramp[512] = 0;
        ramp[255] = 65535;
        ramp[511] = 65535;
        ramp[767] = 65535;
        return ramp;
    }

    private static void ApplyShadowLift(ushort[] ramp, double shadow)
    {
        var strength = Math.Clamp(shadow / 100.0, -1, 1);
        var gamma = strength > 0 ? 1 + 0.6 * strength : 1 / (1 + 0.6 * -strength);
        var inverse = 1 / gamma;

        for (var channel = 0; channel < 3; channel++)
        {
            var offset = channel * 256;
            for (var i = 0; i < 256; i++)
            {
                var x = ramp[offset + i] / 65535.0;
                var lifted = Math.Pow(x, inverse);
                var weight = (1 - x) * (1 - x);
                var y = x + (lifted - x) * weight;
                ramp[offset + i] = (ushort)Math.Clamp(Math.Round(y * 65535.0), 0, 65535);
            }
        }

        for (var channel = 0; channel < 3; channel++)
        {
            var offset = channel * 256;
            for (var i = 1; i < 256; i++)
            {
                if (ramp[offset + i] < ramp[offset + i - 1])
                {
                    ramp[offset + i] = ramp[offset + i - 1];
                }
            }
        }
    }

    private static (double Red, double Green, double Blue) KelvinToRgb(int temperature)
    {
        if (temperature == 6500) return (1, 1, 1);

        var temp = temperature / 100.0;
        var red = temp <= 66
            ? 1
            : Math.Clamp(329.698727446 * Math.Pow(temp - 60, -0.1332047592) / 255, 0, 1);
        var green = temp <= 66
            ? Math.Clamp((99.4708025861 * Math.Log(temp) - 161.1195681661) / 255, 0, 1)
            : Math.Clamp(288.1221695283 * Math.Pow(temp - 60, -0.0755148492) / 255, 0, 1);
        var blue = temp >= 66
            ? 1
            : temp <= 19
                ? 0
                : Math.Clamp((138.5177312231 * Math.Log(temp - 10) - 305.0447927307) / 255, 0, 1);
        return (red, green, blue);
    }

    private static double Gamma(double input, double gamma) => Math.Pow(input, 1 / gamma);

    private static double SCurve(double input, double strength)
    {
        strength = Math.Clamp(strength, -0.5, 0.5);
        var x = input - 0.5;
        return Math.Clamp(0.5 + x * (1 + strength * (1 - 4 * x * x)), 0, 1);
    }

    private static (double Gamma, double SCurve, double RBoost, double GBoost, double BBoost) ModeParams(int mode) =>
        Math.Clamp(mode, 0, 9) switch
        {
            1 => (0.95, 0.08, 1.02, 1.0, 1.03),
            2 => (1.05, -0.05, 1.0, 0.98, 0.96),
            3 => (0.92, 0.05, 1.0, 1.0, 1.0),
            4 => (1.08, -0.08, 0.98, 1.0, 1.02),
            5 => (0.96, 0.10, 1.0, 1.0, 1.02),
            6 => (1.0, 0.0, 1.0, 0.99, 0.97),
            7 => (0.96, -0.05, 1.0, 1.0, 1.0),
            8 => (1.12, 0.03, 1.0, 1.0, 1.0),
            9 => (1.12, 0.08, 1.0, 1.0, 1.02),
            _ => (1.0, 0.0, 1.0, 1.0, 1.0),
        };

    private static object Result(bool ok, string? error) => new { ok, error };

    private static int Clamp(int value, int minimum, int maximum) => Math.Clamp(value, minimum, maximum);

    private static double Clamp(double value, double minimum, double maximum) => Math.Clamp(value, minimum, maximum);

    private static bool OptionalBool(JsonElement args, string name, bool fallback) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static int OptionalInt(JsonElement args, string name, int fallback) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static double OptionalDouble(JsonElement args, string name, double fallback) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
            ? number
            : fallback;

    private sealed record DisplayInfo(int Index, string DeviceName, int Width, int Height, bool Primary);

    private sealed record FilterPreset(string Id, string Name, int Mode);

    private sealed class FilterFile
    {
        public Dictionary<string, FilterState> Displays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FilterState
    {
        public bool Enabled { get; set; }
        public int Temperature { get; set; } = 6500;
        public int Brightness { get; set; } = 100;
        public int Contrast { get; set; } = 100;
        public int Saturation { get; set; } = 100;
        public double RGamma { get; set; } = 1;
        public double GGamma { get; set; } = 1;
        public double BGamma { get; set; } = 1;
        public double Shadow { get; set; }
        public int Mode { get; set; }
        public int[]? OriginalRamp { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateDCW")]
    private static extern IntPtr CreateDC(string? driver, string? device, string? port, IntPtr devMode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool GetDeviceGammaRamp(IntPtr hdc, [Out] ushort[] ramp);

    [DllImport("gdi32.dll")]
    private static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);
}
