using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Management;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "WMI requires reflection")]
public static class BatteryReportService
{
    public static async Task<BatteryInfo> GetBatteryInfoAsync()
    {
        var info = new BatteryInfo();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            foreach (var obj in searcher.Get())
            {
                info.BatteryPresent = true;
                info.EstimatedChargeRemaining = Convert.ToInt32(obj["EstimatedChargeRemaining"]);
                info.BatteryStatus = Convert.ToInt32(obj["BatteryStatus"]) switch
                {
                    1 => "放电中",
                    2 => "交流电源",
                    3 => "充电中",
                    4 => "电量低",
                    5 => "电量严重不足",
                    _ => "未知"
                };
                break;
            }
        }
        catch { }

        try
        {
            using var designSearcher = new ManagementObjectSearcher(
                "root\\WMI",
                "SELECT * FROM BatteryStaticData");
            foreach (var obj in designSearcher.Get())
            {
                info.DesignedCapacity = Convert.ToDouble(obj["DesignedCapacity"]);
                info.ManufactureName = obj["ManufactureName"]?.ToString() ?? "";
                info.ManufactureDate = obj.GetPropertyValue("ManufactureDate") is uint date
                    ? ParseManufactureDate(date)
                    : "";
                info.UniqueId = obj["UniqueID"]?.ToString() ?? "";
                break;
            }
        }
        catch { }

        try
        {
            using var capSearcher = new ManagementObjectSearcher(
                "root\\WMI",
                "SELECT * FROM BatteryFullChargedCapacity");
            foreach (var obj in capSearcher.Get())
            {
                info.FullChargedCapacity = Convert.ToDouble(obj["FullChargedCapacity"]);
                break;
            }
        }
        catch { }

        try
        {
            using var cycleSearcher = new ManagementObjectSearcher(
                "root\\WMI",
                "SELECT * FROM BatteryCycleCount");
            foreach (var obj in cycleSearcher.Get())
            {
                info.CycleCount = Convert.ToInt32(obj["CycleCount"]);
                break;
            }
        }
        catch { }

        if (info.DesignedCapacity > 0 && info.FullChargedCapacity > 0)
        {
            info.HealthPercent = Math.Round(info.FullChargedCapacity / info.DesignedCapacity * 100, 1);
            info.BatteryPresent = true;
        }

        if (!info.BatteryPresent || info.DesignedCapacity <= 0 || info.HealthPercent <= 0)
        {
            await ParseFromHtmlReportAsync(info);
        }

        return info;
    }

    private static async Task ParseFromHtmlReportAsync(BatteryInfo info)
    {
        try
        {
            var htmlPath = Path.Combine(Path.GetTempPath(), "tuba-battery-report.html");
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/batteryreport /output \"{htmlPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return;

            await process.WaitForExitAsync();
            if (!File.Exists(htmlPath)) return;

            var html = await File.ReadAllTextAsync(htmlPath);
            try { File.Delete(htmlPath); } catch { }

            if (string.IsNullOrEmpty(html)) return;

            ParseBatteryHtml(html, info);
        }
        catch { }
    }

    private static void ParseBatteryHtml(string html, BatteryInfo info)
    {
        var labelPattern = "<span class=\"label\">{0}</span></td><td>(.*?)</td>";

        if (info.DesignedCapacity <= 0)
        {
            var designMatch = Regex.Match(html, string.Format(labelPattern, "DESIGN CAPACITY"),
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (designMatch.Success)
            {
                var val = designMatch.Groups[1].Value.Trim();
                info.DesignedCapacity = ParseMwhValue(val);
            }
        }

        if (info.FullChargedCapacity <= 0)
        {
            var fullMatch = Regex.Match(html, string.Format(labelPattern, "FULL CHARGE CAPACITY"),
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (fullMatch.Success)
            {
                var val = fullMatch.Groups[1].Value.Trim();
                info.FullChargedCapacity = ParseMwhValue(val);
            }
        }

        if (info.CycleCount <= 0)
        {
            var cycleMatch = Regex.Match(html, string.Format(labelPattern, "CYCLE COUNT"),
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (cycleMatch.Success)
            {
                var val = cycleMatch.Groups[1].Value.Trim();
                if (int.TryParse(val, out var c))
                    info.CycleCount = c;
            }
        }

        if (string.IsNullOrEmpty(info.ManufactureName))
        {
            var nameMatch = Regex.Match(html, string.Format(labelPattern, "NAME"),
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                info.ManufactureName = nameMatch.Groups[1].Value.Trim();
            }

            var mfgMatch = Regex.Match(html, string.Format(labelPattern, "MANUFACTURER"),
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (mfgMatch.Success)
            {
                var val = mfgMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(val))
                    info.ManufactureName = val;
            }
        }

        if (info.DesignedCapacity > 0 && info.FullChargedCapacity > 0)
        {
            info.HealthPercent = Math.Round(info.FullChargedCapacity / info.DesignedCapacity * 100, 1);
            info.BatteryPresent = true;
        }
    }

    private static double ParseMwhValue(string val)
    {
        var numPart = Regex.Match(val, @"[\d,]+(\.\d+)?").Value;
        if (string.IsNullOrEmpty(numPart)) return 0;
        numPart = numPart.Replace(",", "");
        return double.TryParse(numPart, out var result) ? result : 0;
    }

    private static string ParseManufactureDate(uint raw)
    {
        try
        {
            var day = raw & 0xFF;
            var month = (raw >> 8) & 0xFF;
            var year = (raw >> 16) & 0xFFFF;
            return $"{year}/{month:D2}/{day:D2}";
        }
        catch
        {
            return "";
        }
    }

    public static async Task<string> ExportHtmlReportAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "tuba-battery-report-export.html");
        var psi = new ProcessStartInfo
        {
            FileName = "powercfg",
            Arguments = $"/batteryreport /output \"{path}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is not null)
        {
            await process.WaitForExitAsync();
        }
        return File.Exists(path) ? path : "";
    }
}

public sealed class BatteryInfo
{
    public bool BatteryPresent { get; set; }
    public int EstimatedChargeRemaining { get; set; }
    public string BatteryStatus { get; set; } = "未知";
    public double DesignedCapacity { get; set; }
    public double FullChargedCapacity { get; set; }
    public double HealthPercent { get; set; }
    public int CycleCount { get; set; }
    public string ManufactureName { get; set; } = "";
    public string ManufactureDate { get; set; } = "";
    public string UniqueId { get; set; } = "";

    public string HealthStatus => HealthPercent switch
    {
        >= 80 => "良好",
        >= 60 => "一般",
        >= 40 => "较差",
        > 0 => "需更换",
        _ => "未知"
    };

    public string DesignedCapacityText => DesignedCapacity > 0 ? FormatCapacity(DesignedCapacity) : "未知";
    public string FullChargedCapacityText => FullChargedCapacity > 0 ? FormatCapacity(FullChargedCapacity) : "未知";

    private static string FormatCapacity(double mwh)
    {
        if (mwh >= 1000)
            return $"{mwh / 1000.0:F1} Wh ({mwh:F0} mWh)";
        return $"{mwh:F0} mWh";
    }
}