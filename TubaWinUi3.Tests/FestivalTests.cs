using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 春节 / 元旦的日期解析与窗口判定（纯逻辑，不依赖系统时间/UI）：
/// 农历换算（含闰月）、元旦跨年、春节只出现正日、清单完整性。
/// </summary>
public class FestivalTests
{
    private static FestivalOccurrence Occurrence(string id, int year) =>
        FestivalCalendar.Occurrences(year).Single(o => o.Definition.Id == id);

    // ══════════════════ 农历 ══════════════════

    [Fact]
    public void SpringFestival_UsesLunarNewYear()
    {
        Assert.Equal(new DateOnly(2026, 2, 17), Occurrence(FestivalCatalog.Spring, 2026).Start);
        Assert.Equal(new DateOnly(2025, 1, 29), Occurrence(FestivalCatalog.Spring, 2025).Start);
    }

    [Fact]
    public void LunarDate_SkipsLeapMonth()
    {
        // 2025 有闰六月：闰月十五不是八月十五，口语「八月十五」要落在闰月之后。
        Assert.Equal(new DateOnly(2025, 10, 6), FestivalCalendar.LunarDate(2025, 8, 15));
        Assert.Equal(new DateOnly(2026, 9, 25), FestivalCalendar.LunarDate(2026, 8, 15));
    }

    [Fact]
    public void LunarDate_OutOfSupportedRange_ReturnsNull()
    {
        Assert.Null(FestivalCalendar.LunarDate(2101, 1, 1));
        Assert.Null(FestivalCalendar.LunarDate(1900, 1, 1));
    }

    // ══════════════════ 元旦 ══════════════════

    [Fact]
    public void NewYear_StartsOnDec31()
    {
        Assert.Equal(new DateOnly(2026, 12, 31), Occurrence(FestivalCatalog.NewYear, 2026).Start);
    }

    [Fact]
    public void NewYear_SpansIntoNextYear_AndCountsDownOnDec31()
    {
        var occurrence = Occurrence(FestivalCatalog.NewYear, 2026);
        Assert.Equal(new DateOnly(2026, 12, 31), occurrence.Start);
        Assert.Equal(new DateOnly(2027, 1, 1), occurrence.End);

        var eve = FestivalCalendar.Occasion(new DateOnly(2026, 12, 31));
        Assert.NotNull(eve);
        Assert.Equal(FestivalCatalog.NewYear, eve!.Definition.Id);
        Assert.Equal(FestivalPhase.Peak, eve.Phase);
        Assert.Equal(1, eve.DayOffset); // 倒数日

        var day = FestivalCalendar.Occasion(new DateOnly(2027, 1, 1));
        Assert.NotNull(day);
        Assert.Equal(2, day!.DayOffset);
        Assert.Equal(FestivalPhase.Peak, day.Phase);

        Assert.True(FestivalCalendar.Occasion(new DateOnly(2026, 12, 30)) is null);
    }

    [Fact]
    public void NewYearCountdownTargetsNextJanFirst()
    {
        var target = FestivalService.CountdownTarget(new DateOnly(2026, 12, 31));
        Assert.Equal(new DateTime(2027, 1, 1), target);

        // 预览时 FestivalDate 是今天，目标年按「下一年」算，不会变成过去。
        Assert.Equal(new DateTime(2027, 1, 1), FestivalService.CountdownTarget(new DateOnly(2026, 11, 23)));
    }

    // ══════════════════ 春节窗口 ══════════════════

    [Fact]
    public void SpringFestival_OnlyOnTheDay()
    {
        var occurrence = Occurrence(FestivalCatalog.Spring, 2026);
        Assert.Equal(new DateOnly(2026, 2, 17), occurrence.Start);
        Assert.Equal(new DateOnly(2026, 2, 17), occurrence.End);
        Assert.Equal(new DateOnly(2026, 2, 17), occurrence.PreludeStart);
        Assert.Equal(new DateOnly(2026, 2, 17), occurrence.EchoEnd);

        var dayBefore = FestivalCalendar.Occasion(new DateOnly(2026, 2, 16));
        Assert.True(dayBefore is null || dayBefore.Definition.Id != FestivalCatalog.Spring);

        var peak = FestivalCalendar.Occasion(new DateOnly(2026, 2, 17));
        Assert.Equal(FestivalPhase.Peak, peak!.Phase);
        Assert.Equal(1, peak.DayOffset);

        var dayAfter = FestivalCalendar.Occasion(new DateOnly(2026, 2, 18));
        Assert.True(dayAfter is null || dayAfter.Definition.Id != FestivalCatalog.Spring);
    }

    [Fact]
    public void EffectDismissToken_ScopesToFestivalAndDate()
    {
        var spring = FestivalCalendar.Occasion(new DateOnly(2026, 2, 17));
        var newYear = FestivalCalendar.Occasion(new DateOnly(2026, 12, 31));
        Assert.NotNull(spring);
        Assert.NotNull(newYear);
        Assert.Equal("spring:2026-02-17", FestivalService.EffectToken(spring!));
        Assert.Equal("newyear:2026-12-31", FestivalService.EffectToken(newYear!));
    }

    // ══════════════════ 清单 ══════════════════

    [Fact]
    public void Catalog_ContainsOnlySpringAndNewYear()
    {
        Assert.Equal(2, FestivalCatalog.All.Count);
        Assert.Equal(
            new[] { FestivalCatalog.NewYear, FestivalCatalog.Spring },
            FestivalCatalog.All.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());

        foreach (var definition in FestivalCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Emoji));
            Assert.False(string.IsNullOrWhiteSpace(definition.HeroEmoji));
            Assert.False(string.IsNullOrWhiteSpace(definition.PropEmoji));
            Assert.InRange(definition.LeadDays, 0, 14);
            Assert.InRange(definition.TailDays, 0, 14);
            Assert.True(definition.LengthDays >= 1);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", definition.AccentColor);
        }
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndTrims()
    {
        Assert.Equal(FestivalCatalog.Spring, FestivalCatalog.Find(" SPRING ")!.Id);
        Assert.Null(FestivalCatalog.Find("nope"));
        Assert.Null(FestivalCatalog.Find(null));
    }
}
