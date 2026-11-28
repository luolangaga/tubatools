namespace TubaWinUi3.Services;

/// <summary>节日的日期来源：公历固定日，或农历「口语月序 + 日」（闰月自动跳过）。</summary>
public enum FestivalDateKind
{
    Solar,
    Lunar
}

/// <summary>全窗粒子风格。所有粒子都用 Composition 形状绘制，不产生 XAML 元素。</summary>
public enum FestivalParticleStyle
{
    None,
    RedPacket,
    Confetti
}

/// <summary>
/// 一个节日的静态定义。日期只描述起始日，展示窗口按
/// [Start - LeadDays, Start + LengthDays - 1 + TailDays] 展开。
/// </summary>
public sealed record FestivalDefinition(
    string Id,
    string Emoji,
    FestivalDateKind DateKind,
    int Month,
    int Day,
    int LengthDays,
    int LeadDays,
    int TailDays,
    int Priority,
    string AccentColor,
    FestivalParticleStyle ParticleStyle,
    string HeroEmoji,
    string PropEmoji)
{
    public bool IsRange => LengthDays > 1;
}

/// <summary>
/// 本轮只做两个节日：
/// - 春节：只在正日出现，红包入口 + 立体红包图；
/// - 元旦：12/31 显示倒数，1/1 过年。
/// 其余节日不再保留定义，避免它们在日期窗里被意外触发。
/// </summary>
public static class FestivalCatalog
{
    public const string Spring = "spring";
    public const string NewYear = "newyear";

    public static IReadOnlyList<FestivalDefinition> All { get; } =
    [
        // 春节红包只在正日出现：不预热、不延长，避免整月都在发红包。
        new(Spring, "🏮", FestivalDateKind.Lunar, 1, 1, 1, 0, 0, 100, "#C62828", FestivalParticleStyle.RedPacket, "🧧", "🏮"),
        // 元旦跨 12/31–1/1：前一天倒计时，后一天过年。
        new(NewYear, "🎊", FestivalDateKind.Solar, 12, 31, 2, 0, 0, 60, "#4527A0", FestivalParticleStyle.Confetti, "🎊", "🎆"),
    ];

    public static FestivalDefinition? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(d => d.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
}
