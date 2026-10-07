namespace TubaWinUi3.Services.AiPlayground.Rules;

/// <summary>规则表达式的值（字符串/数字/布尔三态，用于比较）。</summary>
public readonly struct RuleValue : IEquatable<RuleValue>
{
    public string? Text { get; }
    public double Number { get; }
    public bool IsNumber { get; }
    public bool IsText => !IsNumber;

    private RuleValue(string? text, double number, bool isNumber)
    {
        Text = text;
        Number = number;
        IsNumber = isNumber;
    }

    public static RuleValue FromText(string text) => new(text, 0, false);
    public static RuleValue FromNumber(double number) => new(null, number, true);

    public bool AsBool() => IsNumber ? Number != 0 : !string.IsNullOrEmpty(Text);

    /// <summary>数值比较（优先按数字；两边都是文本时按序号比较）。</summary>
    public int CompareTo(RuleValue other)
    {
        if (IsNumber && other.IsNumber) return Number.CompareTo(other.Number);
        if (IsNumber && double.TryParse(other.Text, out var ot)) return Number.CompareTo(ot);
        if (other.IsNumber && double.TryParse(Text, out var st)) return st.CompareTo(other.Number);
        return string.CompareOrdinal(Text ?? "", other.Text ?? "");
    }

    /// <summary>相等：数字按数值，文本按忽略大小写；混合类型尝试数值化后比较。</summary>
    public bool Equals(RuleValue other)
    {
        if (!IsNumber && !other.IsNumber)
            return string.Equals(Text ?? "", other.Text ?? "", StringComparison.OrdinalIgnoreCase);
        return CompareTo(other) == 0;
    }

    public override bool Equals(object? obj) => obj is RuleValue v && Equals(v);
    public override int GetHashCode() => IsNumber ? Number.GetHashCode() : (Text ?? "").ToLowerInvariant().GetHashCode();
    public override string ToString() => IsNumber ? Number.ToString("G") : Text ?? "";
}

/// <summary>
/// 规则的求值上下文：由调用方（检测/分类）提供字段与聚合计数。
/// 只暴露数据，不含任何代码执行/IO/反射能力。
/// </summary>
public interface IRuleContext
{
    /// <summary>取字段值（label/score/class_id/w/h/x/y…）；不存在返回 false。</summary>
    bool TryGetField(string name, out RuleValue value);

    /// <summary>取聚合计数（count(class_id) 等）；不支持返回 -1。</summary>
    int CountBy(string fieldName);
}
