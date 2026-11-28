using Windows.UI;

namespace TubaWinUi3.Services;

/// <summary>
/// 春节 / 元旦的全部文案。放在代码里而不是 resw：文案成对出现（中/英），
/// 且需要按「正日 / 倒数」变化，集中在一处更容易保持语气一致。
/// 语言判定沿用现有做法（LocalizationService.CurrentLanguage）。
/// </summary>
public static class FestivalCopy
{
    private static bool En => !LocalizationService.CurrentLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static string Pick(string zh, string en) => En ? en : zh;

    public static string Name(FestivalDefinition definition) => definition.Id switch
    {
        FestivalCatalog.Spring => Pick("春节", "Spring Festival"),
        FestivalCatalog.NewYear => Pick("元旦", "New Year's Day"),
        _ => definition.Id
    };

    public static string Greeting(FestivalOccasion occasion)
    {
        var id = occasion.Definition.Id;
        return occasion.Phase switch
        {
            FestivalPhase.Prelude => id switch
            {
                FestivalCatalog.Spring => Pick("年味渐浓，红包已经备好啦", "The new year is coming — red envelopes are ready."),
                FestivalCatalog.NewYear => Pick("今晚一起倒数，烟花已经备好", "Countdown tonight — fireworks are ready."),
                _ => Pick("节日快到了", "A festival is coming.")
            },
            FestivalPhase.Peak => id switch
            {
                FestivalCatalog.Spring => Pick("新春快乐，愿你今年的帧数稳如老狗", "Happy Spring Festival! May your frame rates stay rock steady."),
                FestivalCatalog.NewYear => occasion.DayOffset >= 2
                    ? Pick("新年快乐！新的一年继续折腾", "Happy New Year — keep tinkering.")
                    : Pick("今晚一起倒数，烟花已经备好", "Countdown tonight — fireworks are ready."),
                _ => Pick("节日快乐", "Happy festival.")
            },
            _ => id switch
            {
                FestivalCatalog.Spring => Pick("年还没过完，再摸两天鱼", "The celebration isn't over — sneak in a couple more days."),
                FestivalCatalog.NewYear => Pick("新的一年，继续折腾", "A new year — keep tinkering."),
                _ => Pick("节日余韵", "Still in the festival mood.")
            }
        };
    }

    public static string Splash(FestivalDefinition definition) => definition.Id switch
    {
        FestivalCatalog.Spring => Pick("正在点亮新春氛围…", "Lighting up the Spring Festival…"),
        FestivalCatalog.NewYear => Pick("正在准备跨年倒数…", "Getting the countdown ready…"),
        _ => Pick("正在准备节日彩蛋…", "Preparing a festival surprise…")
    };

    public static string Badge(FestivalOccasion occasion)
    {
        if (occasion.Definition.Id == FestivalCatalog.NewYear)
        {
            return occasion.DayOffset >= 2
                ? Pick("元旦 · 新年快乐", "New Year's Day")
                : Pick("元旦 · 倒数", "New Year's Day · countdown");
        }

        var name = Name(occasion.Definition);
        return occasion.Phase switch
        {
            FestivalPhase.Prelude => En
                ? $"{name} · {Math.Abs(occasion.DayOffset)} days to go"
                : $"{name} · 还有 {Math.Abs(occasion.DayOffset)} 天",
            FestivalPhase.Peak => En
                ? $"{name} · Day {occasion.DayOffset}"
                : $"{name} · 第 {occasion.DayOffset} 天",
            _ => En
                ? $"{name} · afterglow {occasion.DayOffset}"
                : $"{name} · 余韵 {occasion.DayOffset} 天"
        };
    }

    public static string Countdown(int targetYear, TimeSpan span)
    {
        var hours = (int)Math.Floor(span.TotalHours);
        return En
            ? $"{hours:00}:{span.Minutes:00}:{span.Seconds:00} until {targetYear}"
            : $"距 {targetYear} 年还有 {hours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }

    public static string DialogTitle(FestivalOccasion occasion) => occasion.Definition.Id switch
    {
        FestivalCatalog.Spring => Pick("🏮 新春快乐", "🏮 Happy Spring Festival"),
        FestivalCatalog.NewYear => Pick("🎊 元旦快乐", "🎊 Happy New Year's Day"),
        _ => occasion.Definition.Emoji
    };

    public static string DialogBody(FestivalDefinition definition) => definition.Id switch
    {
        FestivalCatalog.Spring => Pick(
            "红包已经备好啦。红包图里放着今年的口令，打开支付宝输入就能领——手快有，手慢无。",
            "The red envelope is ready. This year's code is on the envelope — enter it in Alipay. First come, first served."),
        FestivalCatalog.NewYear => Pick("一年过去了。要不要放一场烟花迎接新年？", "Another year gone. Let's welcome the new one with fireworks."),
        _ => Pick("节日快乐！", "Happy festival!")
    };

    public static string ActionLabel(FestivalDefinition definition) => definition.Id switch
    {
        FestivalCatalog.Spring => Pick("抽一句祝福", "Draw a blessing"),
        FestivalCatalog.NewYear => Pick("放烟花", "Fireworks"),
        _ => Pick("试试手气", "Try your luck")
    };

    private static readonly string[][] SpringBlessings =
    [
        ["「愿你的显卡永不花屏」", "“May your GPU never artifact.”"],
        ["「愿你的 CPU 永远不降频」", "“May your CPU never throttle.”"],
        ["「硬盘不响，风扇不吵，帧数不掉」", "“Quiet drives, quiet fans, steady frames.”"],
        ["「新的一年，装机一次点亮」", "“May every new build POST on the first try.”"],
        ["「愿你的电脑比你更耐用」", "“May your PC outlast you.”"],
    ];

    public static string ActionResult(FestivalDefinition definition, int index)
    {
        if (definition.Id == FestivalCatalog.NewYear)
        {
            return Pick("烟花已经升空了。", "Fireworks are up.");
        }
        if (definition.Id == FestivalCatalog.Spring)
        {
            var row = SpringBlessings[Math.Abs(index) % SpringBlessings.Length];
            return En ? row[1] : row[0];
        }
        return Pick("节日快乐！", "Happy festival!");
    }

    public static string EnvelopeHint() => Pick(
        "口令就在红包图里：打开支付宝 → 红包 → 输入口令。",
        "The code is on the envelope: Alipay → Red packet → enter the code.");

    public static string EnvelopeLinkLabel() => Pick("在浏览器打开红包大图", "Open the envelope in a browser");

    public static string EnvelopeLoading() => Pick("红包加载中…", "Loading the red envelope…");

    public static string EnvelopeLoadFailed() => Pick(
        "红包图暂时没取到，可能网络或官网在忙；可以点下面的按钮直接在浏览器打开。",
        "The envelope image didn't load — the site or network may be busy. Open it in a browser below.");

    public static string AcceptButton() => Pick("收下祝福", "Keep it");

    public static string CloseButton() => Pick("下次再说", "Maybe later");

    public static Color AccentColor(FestivalDefinition definition)
    {
        var hex = definition.AccentColor.TrimStart('#');
        try
        {
            if (hex.Length == 6)
            {
                return Color.FromArgb(
                    255,
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }
        }
        catch
        {
        }
        return Color.FromArgb(255, 198, 40, 40);
    }
}
