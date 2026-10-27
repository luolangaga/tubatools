using System.Globalization;
using System.IO.Enumeration;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services.SpaceCleanup;

public readonly record struct MapRect(double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}
public sealed record SpaceTile(SpaceNode Node, MapRect Rect, int Depth, long Bytes);

/// <summary>Frame-independent navigation geometry, shared by entering and returning.</summary>
internal static class SpaceMapMotion
{
    // Fluent standard easing: cubic-bezier(.2, 0, 0, 1). Solve the time axis,
    // rather than treating its Bezier parameter as elapsed time.
    internal static double Ease(double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        double low = 0, high = 1, parameter = progress;
        for (int i = 0; i < 18; i++)
        {
            double inverse = 1 - parameter;
            double x = .6 * inverse * inverse * parameter + parameter * parameter * parameter;
            if (x < progress) low = parameter; else high = parameter;
            parameter = (low + high) / 2;
        }
        return parameter * parameter * (3 - 2 * parameter);
    }
    internal static MapRect Between(MapRect from, MapRect to, double progress)
        => new(from.X + (to.X - from.X) * progress, from.Y + (to.Y - from.Y) * progress,
            from.Width + (to.Width - from.Width) * progress, from.Height + (to.Height - from.Height) * progress);
    internal static MapRect Project(MapRect rect, MapRect viewport, MapRect destination)
        => new(destination.X + (rect.X - viewport.X) * destination.Width / Math.Max(1, viewport.Width),
            destination.Y + (rect.Y - viewport.Y) * destination.Height / Math.Max(1, viewport.Height),
            rect.Width * destination.Width / Math.Max(1, viewport.Width), rect.Height * destination.Height / Math.Max(1, viewport.Height));
    internal static MapRect Content(MapRect rect)
        => new(rect.X + 3, rect.Y + 23, Math.Max(1, rect.Width - 6), Math.Max(1, rect.Height - 26));
}

/// <summary>Squarified treemap (Bruls/Huizing/van Wijk). Layout is independent of XAML and DPI.</summary>
public static class SpaceTreemapLayout
{
    public static IReadOnlyList<MapRect> Squarify(IReadOnlyList<double> weights, MapRect bounds)
    {
        var result = new MapRect[weights.Count];
        double total = weights.Where(w => double.IsFinite(w) && w > 0).Sum();
        if (total <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return result;
        var entries = weights.Select((w, i) => (Area: w > 0 && double.IsFinite(w) ? w / total * bounds.Area : 0, Index: i))
            .Where(e => e.Area > 0).OrderByDescending(e => e.Area).ToArray();
        var remaining = bounds;
        int first = 0;
        while (first < entries.Length && remaining.Width > 0 && remaining.Height > 0)
        {
            double side = Math.Min(remaining.Width, remaining.Height), sum = entries[first].Area;
            int end = first + 1;
            double Worst(double amount, double min, double max) => Math.Max(side * side * max / (amount * amount), amount * amount / (side * side * min));
            double worst = Worst(sum, sum, sum);
            while (end < entries.Length)
            {
                double next = sum + entries[end].Area;
                double candidate = Worst(next, entries[end].Area, entries[first].Area);
                if (candidate > worst) break;
                worst = candidate; sum = next; end++;
            }
            bool vertical = remaining.Width >= remaining.Height;
            double thickness = sum / side, offset = 0;
            for (int i = first; i < end; i++)
            {
                double length = entries[i].Area / thickness;
                result[entries[i].Index] = vertical
                    ? new MapRect(remaining.X, remaining.Y + offset, thickness, length)
                    : new MapRect(remaining.X + offset, remaining.Y, length, thickness);
                offset += length;
            }
            remaining = vertical ? new MapRect(remaining.X + thickness, remaining.Y, Math.Max(0, remaining.Width - thickness), remaining.Height)
                : new MapRect(remaining.X, remaining.Y + thickness, remaining.Width, Math.Max(0, remaining.Height - thickness));
            first = end;
        }
        return result;
    }

    public static List<SpaceTile> Build(SpaceNode root, MapRect bounds, int depth, SpaceFilter filter, int maxTiles = 5000)
    {
        var tiles = new List<SpaceTile>();
        // Cache directory totals only. A per-file dictionary duplicates the entire index
        // when filtering a multi-million-file volume, even though only 5000 tiles can draw.
        var weights = new Dictionary<SpaceNode, long>();
        long Weight(SpaceNode node)
        {
            if (filter.IsEmpty) return node.Bytes;
            if (!node.IsDirectory) return filter.Matches(node) ? node.Bytes : 0;
            if (weights.TryGetValue(node, out var cached)) return cached;
            // Iterative postorder also handles very deeply nested user directories.
            var stack = new Stack<(SpaceNode Node, bool Done)>(); stack.Push((node, false));
            while (stack.TryPop(out var work))
            {
                if (weights.ContainsKey(work.Node)) continue;
                if (work.Done) { weights[work.Node] = work.Node.Children.Sum(c => c.IsDirectory ? weights[c] : filter.Matches(c) ? c.Bytes : 0); continue; }
                stack.Push((work.Node, true)); foreach (var child in work.Node.Children.Directories) stack.Push((child, false));
            }
            return weights[node];
        }
        void Visit(SpaceNode directory, MapRect rect, int level)
        {
            if (level >= depth || tiles.Count >= maxTiles || rect.Width < 8 || rect.Height < 8) return;
            int available = Math.Min(512, maxTiles - tiles.Count);
            var children = SpaceScanService.Largest(directory.Children, available, Weight, out int count, out long total, n => Weight(n) > 0);
            int take = count > available ? Math.Max(0, available - 1) : available;
            if (children.Count > take) children.RemoveAt(children.Count - 1);
            if (count > take)
            {
                // Keep the omitted area instead of scaling the largest N files to 100%.
                // The aggregate expands lazily when double-clicked; no repeated million-item UI copy.
                long rest = total - children.Sum(Weight);
                var group = new SpaceNode($"其他 {count - take:N0} 项（双击展开）", true, directory)
                { Bytes = directory.Bytes - children.Sum(n => n.Bytes), FileCount = directory.FileCount - children.Sum(n => n.FileCount),
                    IsAggregate = true, AggregateSource = directory, AggregateExcluded = children.ToHashSet() };
                weights[group] = rest; children.Add(group);
            }
            var rectangles = Squarify(children.Select(n => (double)Weight(n)).ToArray(), rect);
            var nested = new List<(SpaceNode Node, MapRect Rect)>();
            for (int i = 0; i < children.Count && tiles.Count < maxTiles; i++)
            {
                var r = rectangles[i];
                if (r.Width < 2 || r.Height < 2) continue;
                var child = children[i];
                tiles.Add(new SpaceTile(child, r, level, Weight(child)));
                if (child.IsDirectory && !child.IsAggregate && r.Width > 40 && r.Height > 45)
                    nested.Add((child, new MapRect(r.X + 3, r.Y + 23, Math.Max(0, r.Width - 6), Math.Max(0, r.Height - 26))));
            }
            // Reserve all siblings before adding deeper detail.
            foreach (var child in nested) Visit(child.Node, child.Rect, level + 1);
        }
        Visit(root, bounds, 0);
        return tiles;
    }
}

/// <summary>Semicolon = OR, whitespace = AND; wildcards, exclusions, size, date and four colour tags.</summary>
public sealed class SpaceFilter
{
    private readonly List<List<Func<SpaceNode, bool>>> _groups = [];
    public bool IsEmpty => _groups.Count == 0;
    public string? Error { get; private set; }
    public SpaceFilter(string? expression)
    {
        foreach (var alternative in (expression ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var group = new List<Func<SpaceNode, bool>>();
            foreach (Match match in Regex.Matches(alternative, "\"[^\"]+\"|\\S+"))
            {
                string text = match.Value.Trim('"'); bool exclude = text.StartsWith('!');
                if (exclude) text = text[1..];
                Func<SpaceNode, bool> predicate;
                var size = Regex.Match(text, @"^(>=|<=|>|<)(\d+(?:\.\d+)?)\s*(b|kb|mb|gb|tb)?$", RegexOptions.IgnoreCase);
                if (size.Success)
                {
                    string comparison = size.Groups[1].Value;
                    double value = double.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture) * Math.Pow(1024,
                        size.Groups[3].Value.ToLowerInvariant() switch { "kb" => 1, "mb" => 2, "gb" => 3, "tb" => 4, _ => 0 });
                    predicate = n => comparison switch { ">=" => n.Bytes >= value, "<=" => n.Bytes <= value, ">" => n.Bytes > value, _ => n.Bytes < value };
                }
                else if (text.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
                {
                    int tag = text[4..].ToLowerInvariant() switch { "red" or "1" => 1, "orange" or "2" => 2, "green" or "3" => 3, "blue" or "4" => 4, _ => -1 };
                    if (tag < 0) Error = "标记可用 tag:red / orange / green / blue（或 1–4）";
                    predicate = n => { for (var p = n; p is not null; p = p.Parent) if (p.Tag == tag) return true; return false; };
                }
                else if (text.StartsWith("date", StringComparison.OrdinalIgnoreCase) && text.Length > 5 && text[4] is '>' or '<')
                {
                    if (!DateTime.TryParseExact(text[5..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) Error = "日期格式：date>2026-01-01";
                    predicate = n => text[4] == '>' ? n.Modified > date : n.Modified < date;
                }
                else if (text.StartsWith('>') || text.StartsWith('<'))
                { Error = "大小格式：>500mb 或 <2gb"; predicate = _ => false; }
                else
                {
                    string pattern = text;
                    bool wildcard = pattern.Contains('*') || pattern.Contains('?');
                    predicate = n => n.MatchesName(pattern, wildcard);
                }
                group.Add(exclude ? n => !predicate(n) : predicate);
            }
            if (group.Count > 0) _groups.Add(group);
        }
    }
    public bool Matches(SpaceNode node)
    {
        if (IsEmpty) return true;
        foreach (var group in _groups)
        {
            bool matches = true;
            foreach (var predicate in group) if (!predicate(node)) { matches = false; break; }
            if (matches) return true;
        }
        return false;
    }
}
