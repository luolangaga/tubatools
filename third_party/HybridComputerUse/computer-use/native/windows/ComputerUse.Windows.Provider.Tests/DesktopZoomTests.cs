using System.Drawing;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

// The Windows zoom geometry equals the Linux provider geometry recorded in
// testdata/contract/zoom_geometry.json, and the region rule follows the
// windows over the region.
internal static class DesktopZoomTests
{
    internal static void Run(string fixtureRoot)
    {
        var path = Path.Combine(fixtureRoot, "..", "contract", "zoom_geometry.json");
        var cases = (JsonNode.Parse(File.ReadAllText(path))!["cases"] as JsonArray)!;
        var checkedCases = 0;
        foreach (var node in cases)
        {
            var item = (JsonObject)node!;
            var name = item["name"]!.GetValue<string>();
            var native = Numbers(item["native"]);
            var region = Numbers(item["region"]);
            var frame = WindowsDesktopImageGeometry.DeliveredSize((int)native[0], (int)native[1]);
            Check(Numbers(item["frame"]).SequenceEqual(new double[] { frame.Width, frame.Height }),
                $"{name}: frame size");
            WindowsZoomGeometry? geometry = null;
            try
            {
                geometry = WindowsDesktopZoom.Geometry(region, (int)native[0], (int)native[1]);
            }
            catch (ArgumentException)
            {
            }
            if (item["expect"] is not JsonObject expect)
            {
                Check(geometry is null, $"{name}: must be rejected");
                checkedCases++;
                continue;
            }
            Check(geometry is not null, $"{name}: rejected");
            var crop = Numbers(expect["native"]);
            Check(geometry!.Native == new Rectangle((int)crop[0], (int)crop[1], (int)crop[2], (int)crop[3]),
                $"{name}: native crop {geometry.Native}");
            Check(geometry.Width == expect["width"]!.GetValue<int>() &&
                  geometry.Height == expect["height"]!.GetValue<int>(), $"{name}: size");
            var want = Numbers(expect["region"]);
            for (var index = 0; index < 4; index++)
            {
                Check(Math.Abs(geometry.Region[index] - want[index]) < 1e-9, $"{name}: region");
            }
            checkedCases++;
        }
        Check(checkedCases > 50, "zoom fixture too small");

        var before = Layout((1, 0, 0, 50, 50), (2, 60, 60, 30, 30));
        var area = new Rectangle(10, 10, 30, 20);
        Check(WindowsDesktopZoom.RegionStillValid(before, Layout((1, 0, 0, 50, 50), (2, 65, 60, 30, 30)), area),
            "an unrelated window move must not reject the zoom");
        Check(!WindowsDesktopZoom.RegionStillValid(before, Layout((1, 2, 0, 50, 50), (2, 60, 60, 30, 30)), area),
            "a window over the region moved");
        Check(!WindowsDesktopZoom.RegionStillValid(before,
            Layout((3, 38, 28, 5, 5), (1, 0, 0, 50, 50), (2, 60, 60, 30, 30)), area),
            "a new window covers the region");
        Check(!WindowsDesktopZoom.RegionStillValid(null, before, area), "unpublished frame");
        Console.WriteLine($"Zoom geometry tests passed ({checkedCases} cases).");
    }

    private static WindowsDesktopLayoutSnapshot Layout(
        params (long Handle, int X, int Y, int Width, int Height)[] windows)
    {
        return new WindowsDesktopLayoutSnapshot(
            0, 0, 100, 100, windows.Length > 0 ? windows[0].Handle : 0,
            windows.Select((window, index) => new WindowsDesktopLayoutWindow(
                index, window.Handle, 0, 1, "w", window.X, window.Y,
                window.Width, window.Height, 0, 0)).ToArray(),
            "frame");
    }

    private static double[] Numbers(JsonNode? node) =>
        ((JsonArray)node!).Select(value => value!.GetValue<double>()).ToArray();

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
