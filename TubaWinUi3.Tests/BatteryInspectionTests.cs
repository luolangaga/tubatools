using System.Reflection;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class BatteryInspectionTests
{
    [Theory]
    [InlineData("-", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("4294967295", false, 0)]
    [InlineData("0", true, 0)]
    [InlineData("123", true, 123)]
    public void ReportCycleCount_DistinguishesMissingValueFromZero(string value, bool available, int count)
    {
        var info = new BatteryInfo();
        var html = $"<span class=\"label\">CYCLE COUNT</span></td><td>{value}</td>";
        typeof(BatteryReportService).GetMethod("ParseBatteryHtml", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [html, info]);
        Assert.Equal(available, info.CycleCountAvailable);
        Assert.Equal(count, info.CycleCount);
    }
}
