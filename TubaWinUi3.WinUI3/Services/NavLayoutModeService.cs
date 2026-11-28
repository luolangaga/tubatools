namespace TubaWinUi3.Services;

public static class NavLayoutModeService
{
    public const string SidebarMode = "sidebar";
    public const string TabsMode = "tabs";
    public const string RailMode = "rail";

    private const string NavLayoutModeKey = "NavLayoutMode";

    public static event Action<string>? NavLayoutModeChanged;

    public static string GetNavLayoutMode() => AppSettings.Get(NavLayoutModeKey) ?? SidebarMode;

    public static void SetNavLayoutMode(string mode)
    {
        AppSettings.Set(NavLayoutModeKey, mode);
        NavLayoutModeChanged?.Invoke(mode);
    }

    public static bool IsTabMode() => GetNavLayoutMode() == TabsMode;

    public static bool IsRailMode() => GetNavLayoutMode() == RailMode;
}
