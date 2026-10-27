using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>向导暂存选择；筛选和返回上一步不会丢失选择，完成前不写设置。</summary>
internal sealed class SetupSearchSelection
{
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<SetupSearchTool> Tools { get; }

    public SetupSearchSelection(IEnumerable<SetupSearchCandidate> candidates, ISet<string> excluded)
    {
        Tools = candidates.GroupBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new SetupSearchTool
            {
                Name = group.Key,
                Description = string.Join(" · ", group.Select(c => c.Category).Distinct()),
                Keys = group.Select(c => c.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                BuiltinId = group.Select(c => c.BuiltinId).FirstOrDefault(id => id is not null),
                IconPath = group.Select(c => c.IconPath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)),
                ExternalTool = group.Select(c => c.ExternalTool).FirstOrDefault(tool => tool is not null)
            }).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var tool in Tools)
            SetSelected(tool, tool.Keys.All(key => !excluded.Contains(key)));
    }

    public bool IsSelected(SetupSearchTool tool) => tool.Keys.All(_selected.Contains);

    public void SetSelected(SetupSearchTool tool, bool selected)
    {
        foreach (var key in tool.Keys)
            if (selected) _selected.Add(key); else _selected.Remove(key);
    }

    public IReadOnlyList<SetupSearchTool> Filter(string query) => Tools.Where(t =>
        t.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || t.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

    public HashSet<string> BuildSelections() => new(_selected, StringComparer.OrdinalIgnoreCase);

    public HashSet<string> BuildExclusions(IEnumerable<string> existing)
    {
        var result = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var tool in Tools)
            foreach (var key in tool.Keys)
                if (_selected.Contains(key)) result.Remove(key); else result.Add(key);
        return result;
    }
}
