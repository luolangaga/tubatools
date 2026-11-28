using System.Globalization;

namespace TubaWinUi3.Services;

public enum FestivalPhase
{
    /// <summary>节前预热：还没到正日。</summary>
    Prelude,
    /// <summary>正日 / 节日区间内。</summary>
    Peak,
    /// <summary>节后余韵。</summary>
    Echo
}

public sealed record FestivalOccurrence(FestivalDefinition Definition, DateOnly Start)
{
    public DateOnly End => Start.AddDays(Math.Max(1, Definition.LengthDays) - 1);
    public DateOnly PreludeStart => Start.AddDays(-Definition.LeadDays);
    public DateOnly EchoEnd => End.AddDays(Definition.TailDays);
}

public sealed record FestivalOccasion(
    FestivalDefinition Definition,
    DateOnly FestivalDate,
    FestivalPhase Phase,
    int DayOffset);

/// <summary>
/// 节日日期解析（纯逻辑，可单测）：
/// - 公历节日直接取月/日；
/// - 农历节日遍历当年每一天，用 <see cref="ChineseLunisolarCalendar"/> 反查，
///   闰月（如 2025 闰六月）本身不算节日，且闰月之后的月序要减 1 才对得上「八月十五」这类口语月序；
/// - 同一天撞多个节日时只返回优先级最高的那个；
/// - 跨年节日（12/31–1/1）解析时会带上上一年的 occurrence，避免 1 月 1 日丢窗口。
/// </summary>
public static class FestivalCalendar
{
    private static readonly object Gate = new();
    private static readonly Dictionary<int, IReadOnlyList<FestivalOccurrence>> YearCache = [];

    public static IReadOnlyList<FestivalOccurrence> Occurrences(int year)
    {
        lock (Gate)
        {
            if (YearCache.TryGetValue(year, out var cached)) return cached;
            var built = Build(year);
            YearCache[year] = built;
            return built;
        }
    }

    private static IReadOnlyList<FestivalOccurrence> Build(int year)
    {
        var list = new List<FestivalOccurrence>(FestivalCatalog.All.Count);
        foreach (var definition in FestivalCatalog.All)
        {
            var start = definition.DateKind == FestivalDateKind.Lunar
                ? LunarDate(year, definition.Month, definition.Day)
                : SolarDate(year, definition.Month, definition.Day);
            if (start is not null) list.Add(new FestivalOccurrence(definition, start.Value));
        }
        return list;
    }

    private static DateOnly? SolarDate(int year, int month, int day)
    {
        try { return new DateOnly(year, month, day); }
        catch { return null; }
    }

    /// <summary>
    /// 找出公历 <paramref name="year"/> 年内农历 <paramref name="month"/> 月 <paramref name="day"/> 日对应的公历日期。
    /// month 是口语月序（正月=1、八月=8），不是日历里的「第几个月」序号。
    /// </summary>
    internal static DateOnly? LunarDate(int year, int month, int day)
    {
        try
        {
            var calendar = new ChineseLunisolarCalendar();
            var cursor = new DateTime(year, 1, 1);
            var end = new DateTime(year, 12, 31);
            while (cursor <= end)
            {
                var lunarYear = calendar.GetYear(cursor);
                var lunarMonth = calendar.GetMonth(cursor);
                var lunarDay = calendar.GetDayOfMonth(cursor);

                if (calendar.IsLeapYear(lunarYear))
                {
                    var leapMonthOrdinal = calendar.GetLeapMonth(lunarYear);
                    if (lunarMonth == leapMonthOrdinal)
                    {
                        // 闰月里的「十五」不是节日。
                        cursor = cursor.AddDays(1);
                        continue;
                    }
                    if (lunarMonth > leapMonthOrdinal) lunarMonth--;
                }

                if (lunarMonth == month && lunarDay == day) return DateOnly.FromDateTime(cursor);
                cursor = cursor.AddDays(1);
            }
        }
        catch
        {
            // ChineseLunisolarCalendar 只支持 1901–2100；范围外直接当作没有节日。
        }
        return null;
    }

    public static FestivalOccasion? Occasion(DateOnly date)
    {
        FestivalOccasion? best = null;
        for (var year = date.Year - 1; year <= date.Year; year++)
        {
            foreach (var occurrence in Occurrences(year))
            {
                if (date < occurrence.PreludeStart || date > occurrence.EchoEnd) continue;

                FestivalPhase phase;
                int offset;
                if (date < occurrence.Start)
                {
                    phase = FestivalPhase.Prelude;
                    offset = -(occurrence.Start.DayNumber - date.DayNumber);
                }
                else if (date <= occurrence.End)
                {
                    phase = FestivalPhase.Peak;
                    offset = date.DayNumber - occurrence.Start.DayNumber + 1;
                }
                else
                {
                    phase = FestivalPhase.Echo;
                    offset = date.DayNumber - occurrence.End.DayNumber;
                }

                var candidate = new FestivalOccasion(occurrence.Definition, occurrence.Start, phase, offset);
                if (best is null
                    || candidate.Definition.Priority > best.Definition.Priority
                    || (candidate.Definition.Priority == best.Definition.Priority
                        && Math.Abs(candidate.DayOffset) < Math.Abs(best.DayOffset)))
                {
                    best = candidate;
                }
            }
        }
        return best;
    }
}
