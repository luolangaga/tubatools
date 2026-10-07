namespace TubaWinUi3.Services.AiPlayground.Rules;

/// <summary>把一个检测框适配为规则求值上下文。字段：label/raw/class_id/score/x/y/w/h。</summary>
public sealed class DetectionRuleContext : IRuleContext
{
    private readonly DetectionItem _item;
    private readonly IReadOnlyDictionary<string, int> _classCounts;

    public DetectionRuleContext(DetectionItem item, IReadOnlyDictionary<string, int>? classCounts = null)
    {
        _item = item;
        _classCounts = classCounts ?? new Dictionary<string, int>();
    }

    public bool TryGetField(string name, out RuleValue value)
    {
        switch (name.ToLowerInvariant())
        {
            case "label": value = RuleValue.FromText(_item.RawLabel); return true;
            case "label_cn": value = RuleValue.FromText(_item.Label); return true;
            case "class_id": value = RuleValue.FromNumber(_item.ClassId); return true;
            case "score": value = RuleValue.FromNumber(_item.Score); return true;
            case "x": value = RuleValue.FromNumber(_item.X1); return true;
            case "y": value = RuleValue.FromNumber(_item.Y1); return true;
            case "w": value = RuleValue.FromNumber(_item.Width); return true;
            case "h": value = RuleValue.FromNumber(_item.Height); return true;
            default: value = RuleValue.FromText(""); return false;
        }
    }

    public int CountBy(string fieldName)
    {
        if (fieldName.Equals("class_id", StringComparison.OrdinalIgnoreCase) &&
            _classCounts.TryGetValue(_item.ClassId.ToString(), out var n))
            return n;
        return -1;
    }
}
