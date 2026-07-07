using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TubaWinUi3.Services;

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "WMI requires reflection")]
public sealed record BatteryTrendPoint(DateTime Timestamp, int ChargePercent, int CapacityMwh, bool OnAcPower, string State);

public sealed record BatteryWeeklyEntry(DateTime StartDate, DateTime EndDate, double FullChargeMwh, double DesignMwh, int CycleCount, TimeSpan ActiveDcTime, double ActiveDcEnergyMwh);

public sealed record BatteryRealtimeStatus(int DischargeRateMw, int ChargeRateMw, int RemainingCapacityMwh, int VoltageMv, bool Charging, bool Discharging, bool PowerOnline);

public sealed record ProcessPowerEntry(string ProcessName, double CpuPercent, long MemoryBytes, int Pid);

public sealed class SprBatteryInfo
{
    public string Id { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string Chemistry { get; init; } = "";
    public int DesignCapacity { get; init; }
    public int FullChargeCapacity { get; init; }
    public int CycleCount { get; init; }
    public double CapacityRatio => DesignCapacity > 0 ? Math.Round(100.0 * FullChargeCapacity / DesignCapacity, 1) : 0;
    public string ChemistryZh => Chemistry.ToUpperInvariant() switch
    {
        "LION" => "锂离子",
        "LI-I" => "锂离子",
        "LIP" => "锂聚合物",
        "NICD" => "镍镉",
        "NIMH" => "镍氢",
        _ => Chemistry
    };
}

public sealed class SprSessionEntry
{
    public int Type { get; init; }
    public int SessionId { get; init; }
    public int ActivityLevel { get; init; }
    public DateTime EntryTimeLocal { get; init; }
    public DateTime ExitTimeLocal { get; init; }
    public long DurationMs { get; init; }
    public bool OnAc { get; init; }
    public int EntryRemainingCapacity { get; init; }
    public int ExitRemainingCapacity { get; init; }
    public int EntryFullChargeCapacity { get; init; }
    public int ExitFullChargeCapacity { get; init; }
    public string TypeNameZh => Type switch
    {
        0 => "活动",
        1 => "屏幕关闭待机",
        2 => "睡眠待机",
        5 => "关机",
        7 => "休眠",
        11 => "蓝屏",
        _ => $"未知({Type})"
    };
    public string ActivityLevelZh => ActivityLevel switch
    {
        0 => "无",
        1 => "低",
        2 => "中",
        3 => "高",
        _ => "未知"
    };
    public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);
    public string DurationText
    {
        get
        {
            var d = Duration;
            if (d.TotalDays >= 1) return $"{(int)d.TotalDays}天 {d.Hours}时{d.Minutes}分";
            if (d.TotalHours >= 1) return $"{(int)d.TotalHours}时{d.Minutes}分";
            if (d.TotalMinutes >= 1) return $"{(int)d.TotalMinutes}分{d.Seconds}秒";
            return $"{d.Seconds}秒";
        }
    }
    public bool HasBatteryData => EntryRemainingCapacity > 0 && ExitRemainingCapacity > 0;
    public int DrainMwh
    {
        get
        {
            if (!HasBatteryData) return 0;
            return Math.Max(0, EntryRemainingCapacity - ExitRemainingCapacity);
        }
    }
    public double DrainPercent
    {
        get
        {
            if (!HasBatteryData || EntryFullChargeCapacity <= 0) return 0;
            var entryPct = 100.0 * EntryRemainingCapacity / EntryFullChargeCapacity;
            var exitPct = 100.0 * ExitRemainingCapacity / ExitFullChargeCapacity;
            return Math.Round(entryPct - exitPct, 1);
        }
    }
    public double EntryChargePercent => HasBatteryData && EntryFullChargeCapacity > 0 ? Math.Round(100.0 * EntryRemainingCapacity / EntryFullChargeCapacity, 1) : -1;
    public double ExitChargePercent => ExitRemainingCapacity > 0 && ExitFullChargeCapacity > 0 ? Math.Round(100.0 * ExitRemainingCapacity / ExitFullChargeCapacity, 1) : -1;
}

public sealed class SprEnergyDrain
{
    public DateTime StartTimeLocal { get; init; }
    public DateTime EndTimeLocal { get; init; }
    public int StartCapacityMwh { get; init; }
    public int EndCapacityMwh { get; init; }
    public int StartFullChargeMwh { get; init; }
    public int EndFullChargeMwh { get; init; }
    public bool OnAc { get; init; }
    public int Activity { get; init; }
    public int DrainMwh => !OnAc && StartCapacityMwh > 0 && EndCapacityMwh > 0 ? Math.Max(0, StartCapacityMwh - EndCapacityMwh) : 0;
    public double DrainPercent
    {
        get
        {
            if (OnAc || StartCapacityMwh <= 0 || StartFullChargeMwh <= 0) return 0;
            var startPct = 100.0 * StartCapacityMwh / StartFullChargeMwh;
            var endPct = EndFullChargeMwh > 0 && EndCapacityMwh > 0 ? 100.0 * EndCapacityMwh / EndFullChargeMwh : startPct;
            return Math.Round(startPct - endPct, 1);
        }
    }
    public double StartPercent => StartFullChargeMwh > 0 && StartCapacityMwh > 0 ? Math.Round(100.0 * StartCapacityMwh / StartFullChargeMwh, 1) : -1;
    public double EndPercent => EndFullChargeMwh > 0 && EndCapacityMwh > 0 ? Math.Round(100.0 * EndCapacityMwh / EndFullChargeMwh, 1) : -1;
    public TimeSpan Duration => EndTimeLocal > StartTimeLocal ? EndTimeLocal - StartTimeLocal : TimeSpan.Zero;
}

public sealed class SprReport
{
    public string ComputerName { get; init; } = "";
    public string SystemManufacturer { get; init; } = "";
    public string SystemProductName { get; init; } = "";
    public string BiosVersion { get; init; } = "";
    public string BiosDate { get; init; } = "";
    public string OsBuild { get; init; } = "";
    public string OsVersion { get; init; } = "";
    public int ReportDurationDays { get; init; }
    public DateTime ScanTimeLocal { get; init; }
    public DateTime ReportStartTimeLocal { get; init; }
    public List<SprBatteryInfo> Batteries { get; init; } = [];
    public List<SprSessionEntry> Sessions { get; init; } = [];
    public List<SprEnergyDrain> EnergyDrains { get; init; } = [];
    public double TotalDrainMwh { get; init; }
    public double TotalActiveDrainMwh { get; init; }
    public double TotalStandbyDrainMwh { get; init; }
    public double TotalHibernateDrainMwh { get; init; }
    public TimeSpan TotalActiveTime { get; init; }
    public TimeSpan TotalStandbyTime { get; init; }
    public TimeSpan TotalHibernateTime { get; init; }
    public double AvgActiveDrainRateMw { get; init; }
    public double AvgStandbyDrainPctPerHour { get; init; }
    public string HtmlPath { get; init; } = "";
}

public static class BatteryAnalyzerService
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/battery/2012";
    private static Dictionary<int, (TimeSpan CpuTime, DateTime SampleTime)> _prevCpu = [];
    private static DateTime _prevSampleTime = DateTime.MinValue;

    public static async Task<List<BatteryTrendPoint>> GetTrendAsync(int days)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"tuba-bat-trend-{Guid.NewGuid():N}.xml");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/batteryreport /output \"{xmlPath}\" /xml /duration {days}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return [];
            await process.WaitForExitAsync();
            if (!File.Exists(xmlPath)) return [];

            var xml = await File.ReadAllTextAsync(xmlPath);
            return ParseTrendFromXml(xml);
        }
        catch { return []; }
        finally
        {
            try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { }
        }
    }

    public static async Task<List<BatteryWeeklyEntry>> GetWeeklyHistoryAsync()
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"tuba-bat-weekly-{Guid.NewGuid():N}.xml");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/batteryreport /output \"{xmlPath}\" /xml",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return [];
            await process.WaitForExitAsync();
            if (!File.Exists(xmlPath)) return [];

            var xml = await File.ReadAllTextAsync(xmlPath);
            return ParseWeeklyFromXml(xml);
        }
        catch { return []; }
        finally
        {
            try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { }
        }
    }

    public static async Task<BatteryRealtimeStatus?> GetRealtimeStatusAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT DischargeRate, ChargeRate, RemainingCapacity, Voltage, Charging, Discharging, PowerOnline FROM BatteryStatus");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return new BatteryRealtimeStatus(
                        Convert.ToInt32(obj["DischargeRate"]),
                        Convert.ToInt32(obj["ChargeRate"]),
                        Convert.ToInt32(obj["RemainingCapacity"]),
                        Convert.ToInt32(obj["Voltage"]),
                        Convert.ToBoolean(obj["Charging"]),
                        Convert.ToBoolean(obj["Discharging"]),
                        Convert.ToBoolean(obj["PowerOnline"])
                    );
                }
            }
            catch { }
            return null;
        });
    }

    public static async Task<List<ProcessPowerEntry>> GetTopProcessesAsync(int topN = 15)
    {
        return await Task.Run(() =>
        {
            var now = DateTime.UtcNow;
            var current = new Dictionary<int, (TimeSpan CpuTime, DateTime SampleTime)>();
            var results = new List<ProcessPowerEntry>();

            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        var cpuTime = p.TotalProcessorTime;
                        current[p.Id] = (cpuTime, now);
                    }
                    catch { }
                }

                if (_prevSampleTime != DateTime.MinValue && _prevCpu.Count > 0)
                {
                    var elapsed = (now - _prevSampleTime).TotalSeconds;
                    if (elapsed > 0.1)
                    {
                        var cpuCount = Environment.ProcessorCount;
                        foreach (var (pid, (curTime, _)) in current)
                        {
                            if (!_prevCpu.TryGetValue(pid, out var prev)) continue;
                            var diff = (curTime - prev.CpuTime).TotalSeconds;
                            var pct = Math.Min(100.0 * diff / elapsed / cpuCount, 100.0);
                            if (pct < 0.01) continue;

                            try
                            {
                                var proc = Process.GetProcessById(pid);
                                var name = proc.ProcessName;
                                var mem = proc.WorkingSet64;
                                results.Add(new ProcessPowerEntry(name, pct, mem, pid));
                            }
                            catch { }
                        }
                    }
                }

                _prevCpu = current;
                _prevSampleTime = now;
            }
            catch { }

            return results.OrderByDescending(p => p.CpuPercent).Take(topN).ToList();
        });
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
            await process.WaitForExitAsync();
        return File.Exists(path) ? path : "";
    }

    private static List<BatteryTrendPoint> ParseTrendFromXml(string xml)
    {
        var points = new List<BatteryTrendPoint>();
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var entry in doc.Descendants(Ns + "UsageEntry"))
            {
                var localTs = (string?)entry.Attribute("LocalTimestamp");
                if (string.IsNullOrEmpty(localTs)) continue;
                if (!DateTime.TryParse(localTs, out var ts)) continue;

                var capacity = (int?)entry.Attribute("ChargeCapacity") ?? 0;
                var fullCharge = (int?)entry.Attribute("FullChargeCapacity") ?? 0;
                var ac = (string?)entry.Attribute("Ac") == "1";
                var state = (string?)entry.Attribute("EntryType") ?? "";

                var pct = fullCharge > 0 ? (int)Math.Round(capacity * 100.0 / fullCharge) : 0;
                points.Add(new BatteryTrendPoint(ts, pct, capacity, ac, state));
            }
        }
        catch { }
        return points;
    }

    private static List<BatteryWeeklyEntry> ParseWeeklyFromXml(string xml)
    {
        var entries = new List<BatteryWeeklyEntry>();
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var entry in doc.Descendants(Ns + "HistoryEntry"))
            {
                var startStr = (string?)entry.Attribute("StartDate");
                var endStr = (string?)entry.Attribute("EndDate");
                if (string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr)) continue;
                if (!DateTime.TryParse(startStr, out var start) || !DateTime.TryParse(endStr, out var end)) continue;

                var fullCharge = (double?)entry.Attribute("FullChargeCapacity") ?? 0;
                var design = (double?)entry.Attribute("DesignCapacity") ?? 0;
                var cycle = (int?)entry.Attribute("CycleCount") ?? 0;
                var activeDcStr = (string?)entry.Attribute("ActiveDcTime") ?? "";
                var activeDcTime = ParseDuration(activeDcStr);
                var energy = (double?)entry.Attribute("ActiveDcEnergy") ?? 0;

                entries.Add(new BatteryWeeklyEntry(start, end, fullCharge, design, cycle, activeDcTime, energy));
            }
        }
        catch { }
        return entries;
    }

    private static TimeSpan ParseDuration(string iso)
    {
        try
        {
            if (string.IsNullOrEmpty(iso) || iso == "PT0S") return TimeSpan.Zero;
            return XmlConvert.ToTimeSpan(iso);
        }
        catch { return TimeSpan.Zero; }
    }

    public static async Task<SprReport?> GetSprReportAsync()
    {
        var htmlPath = Path.Combine(Path.GetTempPath(), $"tuba-spr-report-{Guid.NewGuid():N}.html");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/spr /output \"{htmlPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            await process.WaitForExitAsync();
            if (!File.Exists(htmlPath)) return null;

            var html = await File.ReadAllTextAsync(htmlPath);
            return ParseSprReport(html, htmlPath);
        }
        catch { return null; }
    }

    public static async Task<string> ExportSprHtmlReportAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "tuba-spr-report-export.html");
        var psi = new ProcessStartInfo
        {
            FileName = "powercfg",
            Arguments = $"/spr /output \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is not null)
            await process.WaitForExitAsync();
        return File.Exists(path) ? path : "";
    }

    private static SprReport? ParseSprReport(string html, string htmlPath)
    {
        try
        {
            var match = Regex.Match(html, @"var LocalSprData\s*=\s*(\{.*?\});\s", RegexOptions.Singleline);
            if (!match.Success) return null;

            var json = match.Groups[1].Value;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var reportInfo = root.GetProperty("ReportInformation");
            var sysInfo = root.GetProperty("SystemInformation");

            var batteries = new List<SprBatteryInfo>();
            if (root.TryGetProperty("Batteries", out var batteriesArr))
            {
                foreach (var b in batteriesArr.EnumerateArray())
                {
                    batteries.Add(new SprBatteryInfo
                    {
                        Id = GetString(b, "Id"),
                        Manufacturer = GetString(b, "Manufacturer"),
                        SerialNumber = GetString(b, "SerialNumber"),
                        Chemistry = GetString(b, "Chemistry"),
                        DesignCapacity = GetInt(b, "DesignCapacity"),
                        FullChargeCapacity = GetInt(b, "FullChargeCapacity"),
                        CycleCount = GetInt(b, "CycleCount")
                    });
                }
            }

            var sessions = new List<SprSessionEntry>();
            if (root.TryGetProperty("ScenarioInstances", out var instancesArr))
            {
                foreach (var s in instancesArr.EnumerateArray())
                {
                    var entryLocal = GetDateTime(s, "EntryTimestampLocal");
                    var exitLocal = GetDateTime(s, "ExitTimestampLocal");
                    if (entryLocal == default) continue;

                    sessions.Add(new SprSessionEntry
                    {
                        Type = GetInt(s, "Type"),
                        SessionId = GetInt(s, "SessionId"),
                        ActivityLevel = GetInt(s, "ActivityLevel"),
                        EntryTimeLocal = entryLocal,
                        ExitTimeLocal = exitLocal,
                        DurationMs = GetLong(s, "Duration"),
                        OnAc = GetBool(s, "OnAc"),
                        EntryRemainingCapacity = GetInt(s, "EntryRemainingCapacity"),
                        ExitRemainingCapacity = GetInt(s, "ExitRemainingCapacity"),
                        EntryFullChargeCapacity = GetInt(s, "EntryFullChargeCapacity"),
                        ExitFullChargeCapacity = GetInt(s, "ExitFullChargeCapacity")
                    });
                }
            }

            var energyDrains = new List<SprEnergyDrain>();
            if (root.TryGetProperty("EnergyDrains", out var drainsArr))
            {
                foreach (var d in drainsArr.EnumerateArray())
                {
                    var startLocal = GetDateTime(d, "StartTimestampLocal");
                    var endLocal = GetDateTime(d, "EndTimestampLocal");
                    if (startLocal == default) continue;

                    energyDrains.Add(new SprEnergyDrain
                    {
                        StartTimeLocal = startLocal,
                        EndTimeLocal = endLocal,
                        StartCapacityMwh = GetInt(d, "StartChargeCapcity"),
                        EndCapacityMwh = GetInt(d, "EndChargeCapacity"),
                        StartFullChargeMwh = GetInt(d, "StartFullChargeCapacity"),
                        EndFullChargeMwh = GetInt(d, "EndFullChargeCapacity"),
                        OnAc = GetBool(d, "OnAc"),
                        Activity = GetInt(d, "Activity")
                    });
                }
            }

            var dcDrains = energyDrains.Where(d => !d.OnAc && d.StartCapacityMwh > 0 && d.EndCapacityMwh > 0).ToList();
            var totalDrain = dcDrains.Sum(d => (double)d.DrainMwh);

            var activeSessions = sessions.Where(s => s.Type == 0).ToList();
            var standbySessions = sessions.Where(s => s.Type is 1 or 2).ToList();
            var hibernateSessions = sessions.Where(s => s.Type == 7).ToList();

            var activeDcSessions = activeSessions.Where(s => !s.OnAc && s.HasBatteryData).ToList();
            var standbyDcSessions = standbySessions.Where(s => !s.OnAc && s.HasBatteryData).ToList();
            var hibernateDcSessions = hibernateSessions.Where(s => !s.OnAc && s.HasBatteryData).ToList();

            var totalActiveDrain = activeDcSessions.Sum(s => (double)s.DrainMwh);
            var totalStandbyDrain = standbyDcSessions.Sum(s => (double)s.DrainMwh);
            var totalHibernateDrain = hibernateDcSessions.Sum(s => (double)s.DrainMwh);

            var totalActiveTime = TimeSpan.FromMilliseconds(activeSessions.Where(s => !s.OnAc).Sum(s => (double)s.DurationMs));
            var totalStandbyTime = TimeSpan.FromMilliseconds(standbySessions.Where(s => !s.OnAc).Sum(s => (double)s.DurationMs));
            var totalHibernateTime = TimeSpan.FromMilliseconds(hibernateSessions.Where(s => !s.OnAc).Sum(s => (double)s.DurationMs));

            var avgActiveDrain = totalActiveTime.TotalHours > 0 ? totalActiveDrain / totalActiveTime.TotalHours : 0;

            double avgStandbyDrainPct = 0;
            if (standbyDcSessions.Count > 0)
            {
                var totalStandbyHours = totalStandbyTime.TotalHours;
                if (totalStandbyHours > 0)
                {
                    var avgFullCharge = standbyDcSessions
                        .Select(s => (double)s.EntryFullChargeCapacity).DefaultIfEmpty(1).Average();
                    avgStandbyDrainPct = Math.Round(totalStandbyDrain / avgFullCharge / totalStandbyHours * 100, 2);
                }
            }

            return new SprReport
            {
                ComputerName = GetString(sysInfo, "ComputerName"),
                SystemManufacturer = GetString(sysInfo, "SystemManufacturer"),
                SystemProductName = GetString(sysInfo, "SystemProductName"),
                BiosVersion = GetString(sysInfo, "BIOSVersion"),
                BiosDate = GetString(sysInfo, "BIOSDate"),
                OsBuild = GetString(sysInfo, "OSBuild"),
                OsVersion = GetString(sysInfo, "OSVer"),
                ReportDurationDays = GetInt(reportInfo, "ReportDuration"),
                ScanTimeLocal = GetDateTime(reportInfo, "ScanTimeLocal"),
                ReportStartTimeLocal = GetDateTime(reportInfo, "ReportStartTimeLocal"),
                Batteries = batteries,
                Sessions = sessions,
                EnergyDrains = energyDrains,
                TotalDrainMwh = totalDrain,
                TotalActiveDrainMwh = totalActiveDrain,
                TotalStandbyDrainMwh = totalStandbyDrain,
                TotalHibernateDrainMwh = totalHibernateDrain,
                TotalActiveTime = totalActiveTime,
                TotalStandbyTime = totalStandbyTime,
                TotalHibernateTime = totalHibernateTime,
                AvgActiveDrainRateMw = Math.Round(avgActiveDrain, 0),
                AvgStandbyDrainPctPerHour = avgStandbyDrainPct,
                HtmlPath = htmlPath
            };
        }
        catch { return null; }
    }

    private static string GetString(JsonElement el, string prop)
    {
        try { return el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : ""; }
        catch { return ""; }
    }

    private static int GetInt(JsonElement el, string prop)
    {
        try { return el.TryGetProperty(prop, out var v) ? v.GetInt32() : 0; }
        catch { return 0; }
    }

    private static long GetLong(JsonElement el, string prop)
    {
        try { return el.TryGetProperty(prop, out var v) ? v.GetInt64() : 0; }
        catch { return 0; }
    }

    private static bool GetBool(JsonElement el, string prop)
    {
        try { return el.TryGetProperty(prop, out var v) && v.GetBoolean(); }
        catch { return false; }
    }

    private static DateTime GetDateTime(JsonElement el, string prop)
    {
        try
        {
            if (!el.TryGetProperty(prop, out var v)) return default;
            var str = v.GetString();
            return string.IsNullOrEmpty(str) ? default : DateTime.Parse(str);
        }
        catch { return default; }
    }

    public static bool IsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
