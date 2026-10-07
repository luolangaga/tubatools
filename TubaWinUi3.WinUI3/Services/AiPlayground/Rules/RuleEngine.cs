namespace TubaWinUi3.Services.AiPlayground.Rules;

/// <summary>规则命中后要执行的绘制动作。全部为纯数据。</summary>
public sealed class RuleAction
{
    public string? Stroke { get; init; }        // #RRGGBB
    public double? StrokeWidth { get; init; }
    public string? LabelFormat { get; init; }   // 支持 {label} {score:P0} {class_id}
    public bool? Visible { get; init; }
    public string? Fill { get; init; }
}

/// <summary>
/// 规则引擎：对每个检测/分类项，按顺序匹配第一条命中的规则（else 视为恒真、放最后），
/// 把动作键值求值为 <see cref="RuleAction"/>。无渲染/IO 依赖，可单测。
/// </summary>
public static class RuleEngine
{
    /// <summary>求值动作键值 → RuleAction。</summary>
    public static RuleAction Evaluate(DetectionRule rule, IRuleContext ctx)
    {
        // 动作值里的「裸词」按字面量处理（如 file=move / stroke=red）；
        // 只有确实存在于上下文的标识符才当字段引用（如 label / score）。
        string? TextOf(string key)
        {
            if (!rule.Actions.TryGetValue(key, out var e)) return null;
            if (e is FieldExpr f && !ctx.TryGetField(f.Name, out _)) return f.Name;
            return e.Evaluate(ctx).ToString();
        }

        double? NumberOf(string key)
        {
            if (!rule.Actions.TryGetValue(key, out var e)) return null;
            var v = e.Evaluate(ctx);
            return v.IsNumber ? v.Number : double.TryParse(v.Text, out var d) ? d : null;
        }

        bool? BoolOf(string key)
        {
            if (!rule.Actions.TryGetValue(key, out var e)) return null;
            return e.Evaluate(ctx).AsBool();
        }

        return new RuleAction
        {
            Stroke = TextOf("stroke"),
            StrokeWidth = NumberOf("width"),
            LabelFormat = rule.Actions.ContainsKey("label") ? TextOf("label") : null,
            Visible = BoolOf("visible"),
            Fill = TextOf("fill"),
        };
    }

    /// <summary>为首个条件为真的规则求值；都不命中返回 null（由调用方回退默认样式）。</summary>
    public static RuleAction? MatchFirst(IReadOnlyList<DetectionRule> rules, IRuleContext ctx)
    {
        DetectionRule? elseRule = null;
        foreach (var rule in rules)
        {
            if (rule.IsElse) { elseRule ??= rule; continue; }
            try
            {
                if (rule.Condition!.Evaluate(ctx).AsBool())
                    return Evaluate(rule, ctx);
            }
            catch
            {
                // 单条规则求值异常不影响其余规则
            }
        }
        return elseRule is null ? null : Evaluate(elseRule, ctx);
    }

    /// <summary>把标签格式串里的占位符替换为实际值（{label} {class_id} {score} {score:P0} {dir} …）。</summary>
    public static string FormatLabel(string format, IRuleContext ctx)
    {
        if (string.IsNullOrEmpty(format) || !format.Contains('{')) return format;
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < format.Length)
        {
            if (format[i] == '{')
            {
                int end = format.IndexOf('}', i + 1);
                if (end < 0) { sb.Append(format[i..]); break; }
                var token = format[(i + 1)..end];
                sb.Append(ResolvePlaceholder(token, ctx));
                i = end + 1;
            }
            else
            {
                sb.Append(format[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    private static string ResolvePlaceholder(string token, IRuleContext ctx)
    {
        var parts = token.Split(':', 2);
        var name = parts[0].Trim();
        var spec = parts.Length > 1 ? parts[1].Trim() : "";
        if (!ctx.TryGetField(name, out var value)) return "";

        if (value.IsNumber && spec.Length > 0 && spec.StartsWith('P'))
        {
            var digits = spec.Length > 1 && int.TryParse(spec[1..], out var d) ? d : 0;
            return value.Number.ToString("P" + digits);
        }
        if (value.IsNumber && spec is "F0" or "F1" or "F2")
            return value.Number.ToString(spec);
        return value.ToString();
    }
}
