using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace TubaWinUi3.Services;

public enum FestivalIntensity
{
    /// <summary>只保留静态装饰与文案（低配模式 / 系统关闭动画 / 高对比度）。</summary>
    Text,
    /// <summary>少量粒子（预留给后续「轻量」选项）。</summary>
    Light,
    Full
}

/// <summary>
/// 节日彩蛋运行时门面：按日期自动解析、跨零点刷新、强度降级与彩蛋弹窗。
/// 到日子就自动生效，不设用户开关。只做视觉与文案，不参与任何硬件检测 / 工具启动逻辑。
/// </summary>
public static class FestivalService
{
    public const string EffectDismissedKey = "FestivalEffectDismissed";

    /// <summary>节日效果发生变化（跨日刷新）——主窗口与首页据此重建节日视图。</summary>
    public static event Action? Changed;

    /// <summary>「放烟花」请求：由主窗口的叠加层接住，在窗口内炸开。</summary>
    public static event Action? FireworksRequested;

    private static FestivalOccasion? _current;
    private static bool _initialized;
    private static bool _launchPreviewApplied;
    private static string? _launchPreviewId;
    private static DispatcherQueueTimer? _dateTimer;

    public static FestivalOccasion? Current
    {
        get
        {
            EnsureInitialized();
            return _current;
        }
    }

    /// <summary>
    /// 显示强度：低配模式或系统关闭动画 → 仅文字；其余走完整粒子。
    /// </summary>
    public static FestivalIntensity Intensity
    {
        get
        {
            if (FastModeService.IsFastModeEnabled()) return FestivalIntensity.Text;
            try
            {
                if (!new UISettings().AnimationsEnabled) return FestivalIntensity.Text;
            }
            catch
            {
                // 个别非打包环境取 UISettings 会抛；失败时按允许动画处理。
            }
            return FestivalIntensity.Full;
        }
    }

    public static bool IsVisible => Current is not null;

    /// <summary>全窗特效的「本次节日」身份：换节日或换日期后自动重新允许展示。</summary>
    public static string EffectToken(FestivalOccasion occasion) =>
        $"{occasion.Definition.Id}:{occasion.FestivalDate:yyyy-MM-dd}";

    public static bool IsEffectDismissed(FestivalOccasion occasion) =>
        string.Equals(AppSettings.Get(EffectDismissedKey), EffectToken(occasion), StringComparison.OrdinalIgnoreCase);

    /// <summary>用户手动关闭全窗特效，只记住当前这一次节日，不关掉首页的节日徽章。</summary>
    public static void DismissEffect(FestivalOccasion occasion) =>
        AppSettings.Set(EffectDismissedKey, EffectToken(occasion));

    public static void Initialize() => EnsureInitialized();

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        ApplyLaunchPreview();
        RefreshCore();
        StartDateWatcher();
    }

    public static void Refresh()
    {
        _initialized = true;
        RefreshCore();
        Changed?.Invoke();
    }

    private static void RefreshCore()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var preview = FestivalCatalog.Find(_launchPreviewId);
        _current = preview is not null
            ? new FestivalOccasion(preview, today, FestivalPhase.Peak, 1)
            : FestivalCalendar.Occasion(today);
    }

    /// <summary>
    /// 程序常驻跨过零点时不会重建窗口，所以按「离下一个零点还有多久」排一次性定时器：
    /// 到点重算当前节日并通知界面，然后再排下一天。睡醒/休眠恢复后触发也会按当前日期重算。
    /// </summary>
    private static void StartDateWatcher()
    {
        if (_dateTimer is not null) return;
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null) return;

        _dateTimer = dispatcher.CreateTimer();
        _dateTimer.IsRepeating = false;
        _dateTimer.Tick += (_, _) =>
        {
            Refresh();
            ScheduleNextDateCheck();
        };
        ScheduleNextDateCheck();
    }

    private static void ScheduleNextDateCheck()
    {
        if (_dateTimer is null) return;
        var now = DateTime.Now;
        var next = now.Date.AddDays(1).AddSeconds(1);
        var delay = next - now;
        _dateTimer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(60);
        _dateTimer.Start();
    }

    public static void RequestFireworks() => FireworksRequested?.Invoke();

    /// <summary>元旦倒计时的目标时刻：节日日期所在年份的次年 1 月 1 日。</summary>
    public static DateTime CountdownTarget(DateOnly festivalDate) =>
        new(festivalDate.Year + 1, 1, 1);

    public static TimeSpan? NewYearCountdown()
    {
        var current = Current;
        if (current?.Definition.Id != FestivalCatalog.NewYear) return null;
        var target = CountdownTarget(current.FestivalDate);
        var span = target - DateTime.Now;
        return span > TimeSpan.Zero ? span : TimeSpan.Zero;
    }

    /// <summary>首页徽章文本：跨年时每秒刷新为倒计时，其余节日显示阶段。</summary>
    public static string BadgeText()
    {
        var current = Current;
        if (current is null) return string.Empty;
        if (current.Definition.Id == FestivalCatalog.NewYear)
        {
            var span = NewYearCountdown();
            if (span is not null && span.Value > TimeSpan.Zero)
                return FestivalCopy.Countdown(current.FestivalDate.Year + 1, span.Value);
            return FestivalCopy.Name(current.Definition);
        }
        return FestivalCopy.Badge(current);
    }

    public static async Task ShowEggDialogAsync(XamlRoot? xamlRoot)
    {
        var current = Current;
        if (current is null || xamlRoot is null) return;
        var dialog = new TubaWinUi3.Controls.FestivalEggDialog(current);
        dialog.XamlRoot = xamlRoot;
        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(5));
    }

    private static void ApplyLaunchPreview()
    {
        if (_launchPreviewApplied) return;
        _launchPreviewApplied = true;
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? candidate = null;
            if (arg.Equals("--festival", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                candidate = args[i + 1];
            else if (arg.StartsWith("--festival=", StringComparison.OrdinalIgnoreCase))
                candidate = arg["--festival=".Length..];

            if (candidate is null) continue;
            if (FestivalCatalog.Find(candidate.Trim().Trim('"')) is not null)
                _launchPreviewId = candidate.Trim().Trim('"');
            break;
        }
    }
}
