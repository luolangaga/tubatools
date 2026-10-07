namespace TubaWinUi3.Services.AiPlayground.Rules;

/// <summary>规则表达式的 AST 节点。</summary>
public abstract class RuleExpr
{
    public abstract RuleValue Evaluate(IRuleContext ctx);
}

public sealed class LiteralExpr : RuleExpr
{
    public required RuleValue Value { get; init; }
    public override RuleValue Evaluate(IRuleContext ctx) => Value;
}

/// <summary>字段引用（label / score / class_id / w / h / x / y …）。</summary>
public sealed class FieldExpr : RuleExpr
{
    public required string Name { get; init; }
    public override RuleValue Evaluate(IRuleContext ctx) =>
        ctx.TryGetField(Name, out var v) ? v : RuleValue.FromText("");
}

public sealed class NotExpr : RuleExpr
{
    public required RuleExpr Inner { get; init; }
    public override RuleValue Evaluate(IRuleContext ctx) =>
        RuleValue.FromNumber(Inner.Evaluate(ctx).AsBool() ? 0 : 1);
}

public sealed class AndExpr : RuleExpr
{
    public required RuleExpr Left { get; init; }
    public required RuleExpr Right { get; init; }
    public override RuleValue Evaluate(IRuleContext ctx)
    {
        if (!Left.Evaluate(ctx).AsBool()) return RuleValue.FromNumber(0);
        return RuleValue.FromNumber(Right.Evaluate(ctx).AsBool() ? 1 : 0);
    }
}

public sealed class OrExpr : RuleExpr
{
    public required RuleExpr Left { get; init; }
    public required RuleExpr Right { get; init; }
    public override RuleValue Evaluate(IRuleContext ctx)
    {
        if (Left.Evaluate(ctx).AsBool()) return RuleValue.FromNumber(1);
        return RuleValue.FromNumber(Right.Evaluate(ctx).AsBool() ? 1 : 0);
    }
}

public enum CompareOp { Eq, Ne, Gt, Ge, Lt, Le }

public sealed class CompareExpr : RuleExpr
{
    public required RuleExpr Left { get; init; }
    public required RuleExpr Right { get; init; }
    public required CompareOp Op { get; init; }

    public override RuleValue Evaluate(IRuleContext ctx)
    {
        var l = Left.Evaluate(ctx);
        var r = Right.Evaluate(ctx);
        bool result = Op switch
        {
            CompareOp.Eq => l.Equals(r),
            CompareOp.Ne => !l.Equals(r),
            CompareOp.Gt => l.CompareTo(r) > 0,
            CompareOp.Ge => l.CompareTo(r) >= 0,
            CompareOp.Lt => l.CompareTo(r) < 0,
            CompareOp.Le => l.CompareTo(r) <= 0,
            _ => false,
        };
        return RuleValue.FromNumber(result ? 1 : 0);
    }
}

/// <summary>函数调用：startsWith/endsWith/contains/upper/lower/count…</summary>
public sealed class CallExpr : RuleExpr
{
    public required string Name { get; init; }
    public required List<RuleExpr> Args { get; init; }

    public override RuleValue Evaluate(IRuleContext ctx)
    {
        var name = Name.ToLowerInvariant();
        switch (name)
        {
            case "count":
            {
                if (Args.Count != 1) return RuleValue.FromNumber(-1);
                // count(class_id) → 按字段名聚合；count 的参数为字段
                if (Args[0] is FieldExpr f) return RuleValue.FromNumber(ctx.CountBy(f.Name));
                var key = Args[0].Evaluate(ctx).ToString();
                return RuleValue.FromNumber(ctx.CountBy(key));
            }
            case "startswith":
                return RuleValue.FromNumber(Bool2(ctx, static (a, b) => a.StartsWith(b, StringComparison.OrdinalIgnoreCase)));
            case "endswith":
                return RuleValue.FromNumber(Bool2(ctx, static (a, b) => a.EndsWith(b, StringComparison.OrdinalIgnoreCase)));
            case "contains":
                return RuleValue.FromNumber(Bool2(ctx, static (a, b) => a.Contains(b, StringComparison.OrdinalIgnoreCase)));
            case "upper":
                return RuleValue.FromText(Str1(ctx).ToUpperInvariant());
            case "lower":
                return RuleValue.FromText(Str1(ctx).ToLowerInvariant());
            case "len":
                return RuleValue.FromNumber(Str1(ctx).Length);
            default:
                return RuleValue.FromText("");
        }
    }

    private int Bool2(IRuleContext ctx, Func<string, string, bool> fn)
    {
        if (Args.Count != 2) return 0;
        return fn(Args[0].Evaluate(ctx).ToString(), Args[1].Evaluate(ctx).ToString()) ? 1 : 0;
    }

    private string Str1(IRuleContext ctx) => Args.Count >= 1 ? Args[0].Evaluate(ctx).ToString() : "";
}

/// <summary>比较运算符的中文/符号别名。</summary>
internal static class RuleOps
{
    public static CompareOp? FromSymbol(string token) => token switch
    {
        "==" => CompareOp.Eq,
        "=" => CompareOp.Eq,
        "!=" => CompareOp.Ne,
        ">" => CompareOp.Gt,
        ">=" => CompareOp.Ge,
        "<" => CompareOp.Lt,
        "<=" => CompareOp.Le,
        _ => null,
    };
}
