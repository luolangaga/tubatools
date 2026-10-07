namespace TubaWinUi3.Services.AiPlayground.Rules;

/// <summary>一条规则：条件（null = else，恒真，优先级最低）+ 动作键值对。</summary>
public sealed class DetectionRule
{
    /// <summary>条件表达式；null 表示 else。</summary>
    public RuleExpr? Condition { get; init; }
    /// <summary>动作：键 → 值表达式（stroke/width/label/visible/fill/move/copy/rename…）。</summary>
    public Dictionary<string, RuleExpr> Actions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int Line { get; init; }
    public bool IsElse => Condition is null;
}

/// <summary>
/// 规则解析器（递归下降）。只解析本 DSL，不做任何代码执行/反射/IO。
/// 语法：<c>when &lt;expr&gt; then k=v k=v</c> 或 <c>else then k=v</c>；<c>//</c> 起注释。
/// </summary>
public static class RuleParser
{
    private sealed class Parser
    {
        private readonly List<Tok> _toks;
        private int _pos;

        public Parser(List<Tok> toks) => _toks = toks;

        private Tok Cur => _toks[_pos];
        private Tok Next() => _toks[_pos++];
        private bool IsIdent(string text) => Cur.Kind == TokKind.Ident && string.Equals(Cur.Text, text, StringComparison.OrdinalIgnoreCase);
        private bool IsOp(string text) => Cur.Kind == TokKind.Op && Cur.Text == text;

        public RuleExpr ParseExpression() => ParseOr();

        private RuleExpr ParseOr()
        {
            var left = ParseAnd();
            while (IsOp("||") || IsIdent("or"))
            {
                Next();
                var right = ParseAnd();
                left = new OrExpr { Left = left, Right = right };
            }
            return left;
        }

        private RuleExpr ParseAnd()
        {
            var left = ParseUnary();
            while (IsOp("&&") || IsIdent("and"))
            {
                Next();
                var right = ParseUnary();
                left = new AndExpr { Left = left, Right = right };
            }
            return left;
        }

        private RuleExpr ParseUnary()
        {
            if (IsOp("!") || IsIdent("not"))
            {
                Next();
                return new NotExpr { Inner = ParseUnary() };
            }
            return ParseComparison();
        }

        private RuleExpr ParseComparison()
        {
            var left = ParsePrimary();
            if (Cur.Kind == TokKind.Op && RuleOps.FromSymbol(Cur.Text) is { } op)
            {
                Next();
                var right = ParsePrimary();
                return new CompareExpr { Left = left, Right = right, Op = op };
            }
            return left;
        }

        private RuleExpr ParsePrimary()
        {
            if (Cur.Kind == TokKind.LParen)
            {
                Next();
                var inner = ParseExpression();
                Expect(TokKind.RParen, "缺少 ')'");
                return inner;
            }
            if (Cur.Kind == TokKind.Number)
            {
                var n = Next().Number;
                return new LiteralExpr { Value = RuleValue.FromNumber(n) };
            }
            if (Cur.Kind == TokKind.String)
            {
                var s = Next().Text;
                return new LiteralExpr { Value = RuleValue.FromText(s) };
            }
            if (Cur.Kind == TokKind.Color)
            {
                var c = Next().Text;
                return new LiteralExpr { Value = RuleValue.FromText(c) };
            }
            if (Cur.Kind == TokKind.Ident)
            {
                var name = Next().Text;
                if (Cur.Kind == TokKind.LParen)
                {
                    Next();
                    var args = new List<RuleExpr>();
                    if (Cur.Kind != TokKind.RParen)
                    {
                        args.Add(ParseExpression());
                        while (Cur.Kind == TokKind.Comma)
                        {
                            Next();
                            args.Add(ParseExpression());
                        }
                    }
                    Expect(TokKind.RParen, "缺少 ')'");
                    return new CallExpr { Name = name, Args = args };
                }
                // 布尔字面量
                if (name.Equals("true", StringComparison.OrdinalIgnoreCase)) return new LiteralExpr { Value = RuleValue.FromNumber(1) };
                if (name.Equals("false", StringComparison.OrdinalIgnoreCase)) return new LiteralExpr { Value = RuleValue.FromNumber(0) };
                return new FieldExpr { Name = name };
            }
            throw new RuleSyntaxException(Cur.Line, $"无法解析的记号：{Cur}");
        }

        private void Expect(TokKind kind, string message)
        {
            if (Cur.Kind != kind) throw new RuleSyntaxException(Cur.Line, message);
            Next();
        }

        public bool IsKeyword(string text) => IsIdent(text);
        public void ConsumeKeyword() => Next();
        public int CurrentLine => Cur.Line;

        public Dictionary<string, RuleExpr> ParseActions()
        {
            var actions = new Dictionary<string, RuleExpr>(StringComparer.OrdinalIgnoreCase);
            while (Cur.Kind is TokKind.Ident)
            {
                var key = Next().Text;
                if (Cur.Kind != TokKind.Op || Cur.Text != "=")
                    throw new RuleSyntaxException(Cur.Line, $"动作 '{key}' 后应为 '='");
                Next();
                actions[key] = ParsePrimary();
            }
            return actions;
        }
    }

    /// <summary>解析单个规则行（不含换行）。</summary>
    public static DetectionRule ParseLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) throw new RuleSyntaxException(1, "空行");

        var toks = RuleLexer.Tokenize(trimmed);
        var p = new Parser(toks);

        if (p.IsKeyword("else"))
        {
            p.ConsumeKeyword();
            if (!p.IsKeyword("then")) throw new RuleSyntaxException(p.CurrentLine, "else 后应为 then");
            p.ConsumeKeyword();
            return new DetectionRule { Condition = null, Actions = p.ParseActions(), Line = 1 };
        }

        if (!p.IsKeyword("when")) throw new RuleSyntaxException(p.CurrentLine, "规则应以 when 或 else 开头");
        p.ConsumeKeyword();
        var cond = p.ParseExpression();
        if (!p.IsKeyword("then")) throw new RuleSyntaxException(p.CurrentLine, "条件后应为 then");
        p.ConsumeKeyword();
        return new DetectionRule { Condition = cond, Actions = p.ParseActions(), Line = 1 };
    }

    /// <summary>解析多行规则文本（跳过空行与纯注释行）。返回规则与逐行错误。</summary>
    public static (List<DetectionRule> Rules, List<(int Line, string Error)> Errors) Parse(string text)
    {
        var rules = new List<DetectionRule>();
        var errors = new List<(int, string)>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            try
            {
                var rule = ParseLine(line);
                rules.Add(new DetectionRule { Condition = rule.Condition, Actions = rule.Actions, Line = i + 1 });
            }
            catch (RuleSyntaxException ex)
            {
                errors.Add((i + 1, ex.Message));
            }
            catch (Exception ex)
            {
                errors.Add((i + 1, ex.Message));
            }
        }
        return (rules, errors);
    }
}
