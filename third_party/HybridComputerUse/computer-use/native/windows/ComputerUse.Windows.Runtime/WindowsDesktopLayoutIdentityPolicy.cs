namespace ComputerUse.Windows.Runtime;

internal static class WindowsDesktopLayoutIdentityPolicy
{
    private const string TooltipWindowClass = "tooltips_class32";
    private const string SalTemporarySubframeClass = "SALTMPSUBFRAME";
    private const string ShadowWindowClass = "SysShadow";
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolwindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;

    internal static bool IncludesWindow(
        string className,
        nint ownerHandle,
        uint style,
        uint extendedStyle)
    {
        if (string.Equals(
                className,
                TooltipWindowClass,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var isPopup = HasAll(style, WsPopup);
        var isTopmostToolWindow = HasAll(
            extendedStyle,
            WsExTopmost | WsExToolwindow);
        if (string.Equals(
                className,
                SalTemporarySubframeClass,
                StringComparison.OrdinalIgnoreCase))
        {
            return !(ownerHandle != 0 && isPopup && isTopmostToolWindow);
        }

        if (string.Equals(
                className,
                ShadowWindowClass,
                StringComparison.OrdinalIgnoreCase))
        {
            return !(isPopup &&
                HasAll(
                    extendedStyle,
                    WsExTopmost |
                    WsExTransparent |
                    WsExToolwindow |
                    WsExLayered));
        }

        return true;
    }

    private static bool HasAll(uint value, uint flags)
    {
        return (value & flags) == flags;
    }
}
