using System.Drawing;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class DesktopLayoutPolicyTests
{
    internal static void Run()
    {
        if (!WindowsForegroundActivationPolicy.IsVerified((nint)42, (nint)42) ||
            WindowsForegroundActivationPolicy.IsVerified((nint)42, (nint)43) ||
            WindowsForegroundActivationPolicy.IsVerified(0, 0))
        {
            throw new InvalidDataException(
                "Foreground activation must require the exact non-zero target HWND.");
        }
        if (WindowsForegroundActivationPolicy.ShouldAttach(7, 7) ||
            WindowsForegroundActivationPolicy.ShouldAttach(0, 7) ||
            !WindowsForegroundActivationPolicy.ShouldAttach(7, 8))
        {
            throw new InvalidDataException(
                "Thread-input attachment policy crossed its bounded identity rules.");
        }
        if (WindowsDesktopLayoutIdentityPolicy.IncludesWindow(
                "tooltips_class32", 0, 0x94000000, 0x00000088) ||
            WindowsDesktopLayoutIdentityPolicy.IncludesWindow(
                "TOOLTIPS_CLASS32", 0, 0, 0) ||
            WindowsDesktopLayoutIdentityPolicy.IncludesWindow(
                "SALTMPSUBFRAME", (nint)0x10, 0x94000000, 0x00000088) ||
            !WindowsDesktopLayoutIdentityPolicy.IncludesWindow(
                "SALTMPSUBFRAME", (nint)0x10, 0x94000000, 0x00000080) ||
            !WindowsDesktopLayoutIdentityPolicy.IncludesWindow(
                "SALTMPSUBFRAME", 0, 0x94000000, 0x00000088) ||
            WindowsDesktopLayoutIdentityPolicy.IncludesWindow(
                "SysShadow", 0, 0x94000000, 0x000800a8) ||
            !WindowsDesktopLayoutIdentityPolicy.IncludesWindow(
                "SysShadow", 0, 0x94000000, 0x000800a0) ||
            !WindowsDesktopLayoutIdentityPolicy.IncludesWindow(
                "#32768", 0, 0x94000000, 0x00000088) ||
            !WindowsDesktopLayoutIdentityPolicy.IncludesWindow(
                "Chrome_WidgetWin_1", 0, 0x16CF0000, 0x00000100))
        {
            throw new InvalidDataException(
                "Desktop layout identity must ignore transient help windows while retaining interactive menus and unrelated windows.");
        }

        var diagnosticLayoutBefore = WindowsDesktopLayoutDiagnostics.Create(
            0,
            0,
            1920,
            1080,
            0x10,
            new[]
            {
                new WindowsDesktopLayoutWindow(
                    0, 0x10, 0, 101, "MainWindow", 0, 0, 1920, 1040,
                    0x16CF0000, 0x00000100),
                new WindowsDesktopLayoutWindow(
                    1, 0x20, 0x10, 202, "PopupWindow", 200, 300, 400, 500,
                    0x84000000, 0x00000080),
            });
        var diagnosticLayoutAfter = WindowsDesktopLayoutDiagnostics.Create(
            0,
            0,
            1920,
            1080,
            0x30,
            new[]
            {
                new WindowsDesktopLayoutWindow(
                    0, 0x30, 0x10, 303, "#32768", 300, 400, 200, 300,
                    0x84000000, 0x08000088),
                new WindowsDesktopLayoutWindow(
                    1, 0x10, 0, 101, "MainWindow", 0, 0, 1920, 1040,
                    0x16CF0000, 0x00000100),
                new WindowsDesktopLayoutWindow(
                    2, 0x20, 0x10, 202, "PopupWindow", 210, 300, 400, 500,
                    0x84000000, 0x00000080),
            });
        var diagnosticDifference = WindowsDesktopLayoutDiagnostics.Difference(
            diagnosticLayoutBefore.FrameId,
            diagnosticLayoutBefore,
            diagnosticLayoutAfter);
        if (diagnosticLayoutBefore.FrameId == diagnosticLayoutAfter.FrameId ||
            diagnosticDifference["requested_layout_known"]?.GetValue<bool>() != true ||
            diagnosticDifference["foreground_changed"]?.GetValue<bool>() != true ||
            (diagnosticDifference["added"] as JsonArray)?.Count != 1 ||
            (diagnosticDifference["removed"] as JsonArray)?.Count != 0 ||
            (diagnosticDifference["changed"] as JsonArray)?.Count != 2 ||
            diagnosticDifference.ToJsonString().Contains(
                "\"owner_hwnd\":\"0x10\"",
                StringComparison.Ordinal) != true ||
            diagnosticDifference.ToJsonString().Contains(
                "\"style\":\"0x84000000\"",
                StringComparison.Ordinal) != true ||
            diagnosticDifference.ToJsonString().Contains(
                "\"extended_style\":\"0x08000088\"",
                StringComparison.Ordinal) != true ||
            diagnosticDifference.ToJsonString().Contains(
                "window title",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Desktop stale diagnostics did not expose a bounded private layout diff.");
        }
        var unknownDiagnosticDifference =
            WindowsDesktopLayoutDiagnostics.Difference(
                "expired-frame",
                null,
                diagnosticLayoutAfter);
        if (unknownDiagnosticDifference["requested_layout_known"]?.GetValue<bool>() !=
                false ||
            unknownDiagnosticDifference["requested"] is not null)
        {
            throw new InvalidDataException(
                "Expired desktop frame diagnostics must remain explicit and bounded.");
        }

        if (!WindowsWindowIdentityPolicy.Matches(
                (nint)42,
                100,
                "process-generation",
                7,
                (nint)42,
                100,
                "process-generation",
                7) ||
            WindowsWindowIdentityPolicy.Matches(
                (nint)42,
                100,
                "process-generation",
                7,
                (nint)43,
                100,
                "process-generation",
                7) ||
            WindowsWindowIdentityPolicy.Matches(
                (nint)42,
                100,
                "process-generation",
                7,
                (nint)42,
                100,
                "process-generation",
                8))
        {
            throw new InvalidDataException(
                "Stable window identity did not preserve HWND and lifecycle boundaries.");
        }
        // A window that leaves the visible inventory is described by its
        // native instance: hidden or zero-size windows are not "gone".
        var alive = WindowsWindowIdentityPolicy.MissingWindowMessage(WindowLifecycleStatus.Alive);
        var gone = WindowsWindowIdentityPolicy.MissingWindowMessage(WindowLifecycleStatus.Gone);
        var unknown = WindowsWindowIdentityPolicy.MissingWindowMessage(WindowLifecycleStatus.Unknown);
        if (!alive.Contains("still exists", StringComparison.Ordinal) ||
            alive.Contains("disappeared", StringComparison.Ordinal) ||
            !gone.Contains("disappeared", StringComparison.Ordinal) ||
            !unknown.Contains("could not be verified", StringComparison.Ordinal) ||
            new[] { alive, gone, unknown }.Any(text =>
                !text.EndsWith("no input was emitted.", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "A window missing from the visible inventory must be described by its lifecycle.");
        }

        var desktopBounds = new System.Drawing.Rectangle(0, 0, 1280, 800);
        if (!WindowsCaptureBounds.TryResolve(
                desktopBounds,
                new System.Drawing.Rectangle(1200, 700, 200, 200),
                out var clippedCaptureBounds) ||
            clippedCaptureBounds != new System.Drawing.Rectangle(1200, 700, 80, 100) ||
            WindowsCaptureBounds.TryResolve(
                desktopBounds,
                new System.Drawing.Rectangle(-32000, -32000, 160, 28),
                out _))
        {
            throw new InvalidDataException(
                "Capture bounds did not distinguish clipped and unavailable windows.");
        }

        var nativeSize = WindowsDesktopImageGeometry.DeliveredSize(1280, 800);
        var fullHdSize = WindowsDesktopImageGeometry.DeliveredSize(1920, 1080);
        var fourKSize = WindowsDesktopImageGeometry.DeliveredSize(3840, 2160);
        var ultrawideSize = WindowsDesktopImageGeometry.DeliveredSize(3440, 1440);
        var portraitSize = WindowsDesktopImageGeometry.DeliveredSize(1440, 2560);
        if (nativeSize != new System.Drawing.Size(1229, 768) ||
            fullHdSize != new System.Drawing.Size(1365, 768) ||
            fourKSize != new System.Drawing.Size(1365, 768) ||
            ultrawideSize != new System.Drawing.Size(1366, 572) ||
            portraitSize != new System.Drawing.Size(432, 768))
        {
            throw new InvalidDataException(
                "Windows desktop delivery did not preserve aspect ratio within 1366x768.");
        }

        var mappedOrigin = WindowsDesktopImageGeometry.ToNativeCoordinate(
            0,
            1920,
            -1920,
            3840);
        var mappedCenter = WindowsDesktopImageGeometry.ToNativeCoordinate(
            959.5,
            1920,
            -1920,
            3840);
        var mappedLast = WindowsDesktopImageGeometry.ToNativeCoordinate(
            1919,
            1920,
            -1920,
            3840);
        if (mappedOrigin != -1919.5 ||
            mappedCenter != -0.5 ||
            mappedLast != 1918.5)
        {
            throw new InvalidDataException(
                "Delivered-image coordinates did not map through native pixel centers.");
        }

        SendInputMapping();
        ImageBounds();
    }

    // SendInput lands absolute coordinate n on pixel floor(n * length / 65536).
    // Every pixel of common virtual-desktop axes, including a negative
    // origin, must map back to itself.
    private static void SendInputMapping()
    {
        foreach (var (origin, length) in new[]
        {
            (0, 1280), (0, 1365), (0, 1920), (0, 2560), (0, 3840), (-1920, 3840), (0, 2160),
        })
        {
            for (var pixel = origin; pixel < origin + length; pixel++)
            {
                var absolute = WindowsDesktopImageGeometry.ToSendInputAbsolute(
                    pixel, origin, length);
                var landed = origin + (int)((long)absolute * length / 65536);
                if (absolute < 0 || absolute > 65535 || landed != pixel)
                {
                    throw new InvalidDataException(
                        $"SendInput pixel {pixel} on axis {origin}+{length} landed on {landed}.");
                }
            }
        }
    }

    private static void ImageBounds()
    {
        foreach (var (x, y, inside) in new[]
        {
            (0.0, 0.0, true), (1364.0, 767.0, true), (1364.4, 0.0, false),
            (1365.0, 0.0, false), (-0.1, 0.0, false), (0.0, 768.0, false),
            (double.NaN, 0.0, false), (double.PositiveInfinity, 0.0, false),
        })
        {
            if (WindowsDesktopImageGeometry.ContainsPoint(x, y, 1365, 768) != inside)
            {
                throw new InvalidDataException(
                    $"Image point ({x}, {y}) inside={!inside}; expected {inside}.");
            }
        }
    }
}
