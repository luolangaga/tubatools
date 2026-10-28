using System.Globalization;
using System.Text;

namespace TubaWinUi3.Services;

public enum TrafficSort { Activity, Download, Upload, Total, Process }

/// <summary>展示层的纯逻辑：先筛选连接，再按 PID 汇总，最后稳定排序。</summary>
public static class TrafficMonitorView
{
    public static List<TrafficViewEntry> Build(IEnumerable<TrafficConnectionInfo> source,
        string query, bool groupByProcess, bool activeOnly, TrafficSort sort)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filtered = source.Where(c => (!activeOnly || c.HasTrafficStats && (c.SpeedIn > 0 || c.SpeedOut > 0)) &&
            terms.All(term => Matches(c, term))).ToArray();
        IEnumerable<TrafficViewEntry> entries = groupByProcess
            ? filtered.GroupBy(c => c.ProcessId).Select(g => new TrafficViewEntry($"pid:{g.Key}", g.ToArray(), true))
            : filtered.Select(c => new TrafficViewEntry(c.Key, [c], false));
        entries = sort switch
        {
            TrafficSort.Download => entries.OrderByDescending(e => e.SpeedIn),
            TrafficSort.Upload => entries.OrderByDescending(e => e.SpeedOut),
            TrafficSort.Total => entries.OrderByDescending(e => (double)e.TotalIn + e.TotalOut),
            TrafficSort.Process => entries.OrderBy(e => e.ProcessName, StringComparer.OrdinalIgnoreCase),
            _ => entries.OrderByDescending(e => (double)e.SpeedIn + e.SpeedOut)
        };
        return ((IOrderedEnumerable<TrafficViewEntry>)entries).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
    }

    private static bool Matches(TrafficConnectionInfo c, string term)
    {
        if (term.StartsWith("pid:", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(term.AsSpan(4), out var pid) && c.ProcessId == pid;
        return c.ProcessName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || c.ProcessId.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal)
            || c.LocalAddress.Contains(term, StringComparison.OrdinalIgnoreCase)
            || c.RemoteAddress.Contains(term, StringComparison.OrdinalIgnoreCase)
            || c.RemoteDomain.Contains(term, StringComparison.OrdinalIgnoreCase)
            || c.LocalPort.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal)
            || c.RemotePort.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal)
            || c.Protocol.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public static string ExportView(IEnumerable<TrafficViewEntry> entries, DateTime time)
    {
        var csv = new StringBuilder("时间,进程,PID,连接数,本地地址,远程地址,域名,协议,下载字节,上传字节,下载字节每秒,上传字节每秒,统计状态\r\n");
        foreach (var e in entries)
            foreach (var c in e.Connections)
                AppendCsvRow(csv, time.ToString("O"), c.ProcessName, c.ProcessId, e.Connections.Count,
                    TrafficConnectionInfo.FormatEndpoint(c.LocalAddress, c.LocalPort), c.DisplayRemote,
                    c.RemoteDomain, c.Protocol, c.HasTrafficStats ? c.TotalIn : "",
                    c.HasTrafficStats ? c.TotalOut : "", c.HasTrafficStats ? c.SpeedIn : "",
                    c.HasTrafficStats ? c.SpeedOut : "", c.HasTrafficStats ? "可用" : "不可用");
        return csv.ToString();
    }

    public static string ExportRecording(IEnumerable<TrafficSnapshot> snapshots)
    {
        var csv = new StringBuilder("时间,网卡索引,累计下载字节,累计上传字节,下载字节每秒,上传字节每秒,TCP连接数\r\n");
        foreach (var s in snapshots)
            AppendCsvRow(csv, s.Time.ToString("O"), s.AdapterIndex, s.TotalIn, s.TotalOut,
                s.SpeedIn, s.SpeedOut, Math.Max(s.ConnectionCount, s.Connections.Count));
        return csv.ToString();
    }

    private static void AppendCsvRow(StringBuilder csv, params object[] fields)
        => csv.AppendLine(string.Join(",", fields.Select(f => CsvCell(Convert.ToString(f, CultureInfo.InvariantCulture) ?? ""))));

    private static string CsvCell(string text)
    {
        // CSV 可能用 Excel 打开：外部进程名/域名不应被解释为公式。
        if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}

public sealed record TrafficViewEntry(string Key, IReadOnlyList<TrafficConnectionInfo> Connections, bool IsProcess)
{
    public string ProcessName => Connections[0].ProcessName;
    public int ProcessId => Connections[0].ProcessId;
    public long SpeedIn => Connections.Where(c => c.HasTrafficStats).Sum(c => c.SpeedIn);
    public long SpeedOut => Connections.Where(c => c.HasTrafficStats).Sum(c => c.SpeedOut);
    public long TotalIn => Connections.Where(c => c.HasTrafficStats).Sum(c => c.TotalIn);
    public long TotalOut => Connections.Where(c => c.HasTrafficStats).Sum(c => c.TotalOut);
    public bool HasStats => Connections.Any(c => c.HasTrafficStats);
    public bool PartialStats => HasStats && Connections.Any(c => !c.HasTrafficStats);
}
