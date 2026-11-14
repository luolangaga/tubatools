using TubaWinUi3.PluginSdk;

namespace Tubatools.CapybaraLulu;

/// <summary>本插件可以放噜噜的地方。全部通过 SDK / fullTrust 视觉树接入，不碰主程序源码。</summary>
internal sealed class CapybaraSettings
{
    public const string KeyEnabled = "Lulu.Enabled";
    public const string KeyBackground = "Lulu.Background";
    public const string KeyBackgroundTheme = "Lulu.BackgroundTheme";
    public const string KeyBackgroundOpacity = "Lulu.BackgroundOpacity";
    public const string KeyBackgroundMotion = "Lulu.BackgroundMotion";
    public const string KeySidebar = "Lulu.Sidebar";
    public const string KeyCorner = "Lulu.Corner";
    public const string KeyMascotSize = "Lulu.MascotSize";
    public const string KeyMascotSide = "Lulu.MascotSide";
    public const string KeySpeech = "Lulu.Speech";
    public const string KeySpeechInterval = "Lulu.SpeechInterval";
    public const string KeyBubble = "Lulu.SpeechBubble";
    public const string KeyTitleBar = "Lulu.TitleBar";
    public const string KeyHero = "Lulu.HeroSticker";
    public const string KeyHardwareDecor = "Lulu.HardwareDecor";
    public const string KeyAnimations = "Lulu.Animations";

    public const string ThemeAuto = "auto";
    public const string ThemeBath = "bath";
    public const string ThemePark = "park";
    public const string ThemeDesk = "desk";
    public const string ThemeNight = "night";

    public const string SideRight = "right";
    public const string SideLeft = "left";

    public const double OldDefaultOpacity = 0.26;
    public const double DefaultOpacity = 0.45;

    public static readonly string[] Themes = [ThemeAuto, ThemeBath, ThemePark, ThemeDesk, ThemeNight];

    public bool Enabled { get; set; } = true;
    public bool Background { get; set; } = true;
    public string BackgroundTheme { get; set; } = ThemeAuto;
    public double BackgroundOpacity { get; set; } = DefaultOpacity;
    public bool BackgroundMotion { get; set; } = true;
    public bool Sidebar { get; set; } = true;
    public bool Corner { get; set; } = true;
    public double MascotSize { get; set; } = 132;
    public string MascotSide { get; set; } = SideRight;
    public bool Speech { get; set; } = true;
    public int SpeechInterval { get; set; } = 50;
    public bool Bubble { get; set; } = true;
    public bool TitleBar { get; set; } = true;
    public bool HeroSticker { get; set; } = true;
    public bool HardwareDecor { get; set; } = true;
    public bool Animations { get; set; } = true;

    public double ClampedOpacity => Math.Clamp(BackgroundOpacity, 0.08, 0.65);
    public double ClampedMascotSize => Math.Clamp(MascotSize, 92, 190);
    public int ClampedSpeechInterval => Math.Clamp(SpeechInterval, 0, 180);

    public CapybaraSettings Clone() => new()
    {
        Enabled = Enabled,
        Background = Background,
        BackgroundTheme = BackgroundTheme,
        BackgroundOpacity = BackgroundOpacity,
        BackgroundMotion = BackgroundMotion,
        Sidebar = Sidebar,
        Corner = Corner,
        MascotSize = MascotSize,
        MascotSide = MascotSide,
        Speech = Speech,
        SpeechInterval = SpeechInterval,
        Bubble = Bubble,
        TitleBar = TitleBar,
        HeroSticker = HeroSticker,
        HardwareDecor = HardwareDecor,
        Animations = Animations,
    };

    public static async Task<CapybaraSettings> LoadAsync(Func<string, Task<string?>> get, CancellationToken ct)
    {
        var settings = new CapybaraSettings();
        settings.Enabled = await ReadBool(get, KeyEnabled, true, ct);
        settings.Background = await ReadBool(get, KeyBackground, true, ct);
        settings.BackgroundMotion = await ReadBool(get, KeyBackgroundMotion, true, ct);
        settings.Sidebar = await ReadBool(get, KeySidebar, true, ct);
        settings.Corner = await ReadBool(get, KeyCorner, true, ct);
        settings.Speech = await ReadBool(get, KeySpeech, true, ct);
        settings.Bubble = await ReadBool(get, KeyBubble, true, ct);
        settings.TitleBar = await ReadBool(get, KeyTitleBar, true, ct);
        settings.HeroSticker = await ReadBool(get, KeyHero, true, ct);
        settings.HardwareDecor = await ReadBool(get, KeyHardwareDecor, true, ct);
        settings.Animations = await ReadBool(get, KeyAnimations, true, ct);
        var theme = await get(KeyBackgroundTheme);
        settings.BackgroundTheme = theme is not null && Themes.Contains(theme) ? theme : ThemeAuto;
        var opacity = await get(KeyBackgroundOpacity);
        if (double.TryParse(opacity, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            // 旧版默认 26% 看起来太淡；只有用户从没调过（仍是旧默认）时才抬到新默认。
            settings.BackgroundOpacity = Math.Abs(value - OldDefaultOpacity) < 0.001 ? DefaultOpacity : value;
        }
        var size = await get(KeyMascotSize);
        if (double.TryParse(size, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var sizeValue))
            settings.MascotSize = sizeValue;
        var side = await get(KeyMascotSide);
        settings.MascotSide = side is not null && side.Equals(SideLeft, StringComparison.OrdinalIgnoreCase) ? SideLeft : SideRight;
        var interval = await get(KeySpeechInterval);
        if (int.TryParse(interval, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            settings.SpeechInterval = seconds;
        return settings;
    }

    public async Task SaveAsync(Func<string, string?, Task> set, CancellationToken ct)
    {
        await set(KeyEnabled, Enabled ? "true" : "false");
        await set(KeyBackground, Background ? "true" : "false");
        await set(KeyBackgroundTheme, BackgroundTheme);
        await set(KeyBackgroundOpacity, BackgroundOpacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        await set(KeyBackgroundMotion, BackgroundMotion ? "true" : "false");
        await set(KeySidebar, Sidebar ? "true" : "false");
        await set(KeyCorner, Corner ? "true" : "false");
        await set(KeyMascotSize, MascotSize.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        await set(KeyMascotSide, MascotSide);
        await set(KeySpeech, Speech ? "true" : "false");
        await set(KeySpeechInterval, SpeechInterval.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await set(KeyBubble, Bubble ? "true" : "false");
        await set(KeyTitleBar, TitleBar ? "true" : "false");
        await set(KeyHero, HeroSticker ? "true" : "false");
        await set(KeyHardwareDecor, HardwareDecor ? "true" : "false");
        await set(KeyAnimations, Animations ? "true" : "false");
        ct.ThrowIfCancellationRequested();
    }

    private static async Task<bool> ReadBool(Func<string, Task<string?>> get, string key, bool fallback, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var value = await get(key);
        return value is null ? fallback : !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }
}
