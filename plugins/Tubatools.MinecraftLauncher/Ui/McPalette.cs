using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>现代 Minecraft 启动器配色：深色玻璃面板、草绿强调色、柔和金色高亮。</summary>
internal sealed class McPalette
{
    public SolidColorBrush Text { get; } = new();
    public SolidColorBrush Secondary { get; } = new();
    public SolidColorBrush Tertiary { get; } = new();
    public SolidColorBrush Gold { get; } = new();
    public SolidColorBrush Card { get; } = new();
    public SolidColorBrush CardHover { get; } = new();
    public SolidColorBrush CardStroke { get; } = new();
    public SolidColorBrush Panel { get; } = new();
    public SolidColorBrush Subtle { get; } = new();
    public SolidColorBrush Slot { get; } = new();
    public SolidColorBrush Accent { get; } = new();
    public SolidColorBrush AccentDark { get; } = new();
    public SolidColorBrush AccentSoft { get; } = new();
    public SolidColorBrush OnAccent { get; } = new();
    public SolidColorBrush Danger { get; } = new();
    public SolidColorBrush Success { get; } = new();
    public SolidColorBrush Warning { get; } = new();
    public SolidColorBrush Separator { get; } = new();
    public SolidColorBrush Input { get; } = new();
    public SolidColorBrush BevelLight { get; } = new();
    public SolidColorBrush BevelDark { get; } = new();
    public SolidColorBrush StoneFill { get; } = new();
    public SolidColorBrush StoneHover { get; } = new();
    public SolidColorBrush GreenHover { get; } = new();
    public SolidColorBrush ConsoleBackground { get; } = new();
    public SolidColorBrush ConsoleBorder { get; } = new();
    public SolidColorBrush ConsoleText { get; } = new();
    public bool Dark => true;

    public Color AccentColor => Color(0xFF, 0x4F, 0xAE, 0x3F);

    public McPalette() => Apply(true);

    public void Apply(bool dark)
    {
        Text.Color = Color(0xFF, 0xF6, 0xF7, 0xF9);
        Secondary.Color = Color(0xFF, 0xB6, 0xBD, 0xC7);
        Tertiary.Color = Color(0xFF, 0x7E, 0x87, 0x94);
        Gold.Color = Color(0xFF, 0xF5, 0xC4, 0x51);
        Card.Color = Color(0xE0, 0x1B, 0x1D, 0x23);
        CardHover.Color = Color(0xE6, 0x23, 0x26, 0x2E);
        CardStroke.Color = Color(0x22, 0xFF, 0xFF, 0xFF);
        Panel.Color = Color(0xA6, 0x10, 0x12, 0x17);
        Subtle.Color = Color(0x1A, 0xFF, 0xFF, 0xFF);
        Slot.Color = Color(0x26, 0x00, 0x00, 0x00);
        Accent.Color = Color(0xFF, 0x4F, 0xAE, 0x3F);
        AccentDark.Color = Color(0xFF, 0x35, 0x85, 0x2B);
        AccentSoft.Color = Color(0x33, 0x4F, 0xAE, 0x3F);
        OnAccent.Color = Color(0xFF, 0xFF, 0xFF, 0xFF);
        Danger.Color = Color(0xFF, 0xFF, 0x6B, 0x6B);
        Success.Color = Color(0xFF, 0x6F, 0xCF, 0x7A);
        Warning.Color = Color(0xFF, 0xF5, 0xC4, 0x51);
        Separator.Color = Color(0x14, 0xFF, 0xFF, 0xFF);
        Input.Color = Color(0xCC, 0x1F, 0x22, 0x29);
        BevelLight.Color = Color(0x18, 0xFF, 0xFF, 0xFF);
        BevelDark.Color = Color(0x66, 0x00, 0x00, 0x00);
        StoneFill.Color = Color(0x1F, 0xFF, 0xFF, 0xFF);
        StoneHover.Color = Color(0x33, 0xFF, 0xFF, 0xFF);
        GreenHover.Color = Color(0xFF, 0x66, 0xC2, 0x55);
        ConsoleBackground.Color = Color(0xCC, 0x0A, 0x0B, 0x0E);
        ConsoleBorder.Color = Color(0x1A, 0xFF, 0xFF, 0xFF);
        ConsoleText.Color = Color(0xFF, 0xD9, 0xDE, 0xE7);
    }

    public static Color Color(byte a, byte r, byte g, byte b) => ColorHelper.FromArgb(a, r, g, b);
}
