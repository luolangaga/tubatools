using System.Globalization;

namespace TubaWinUi3.Services.AiPlayground.Rules;

internal enum TokKind { Ident, Number, String, Color, Op, LParen, RParen, Comma, Semicolon, End }

internal readonly struct Tok
{
    public TokKind Kind { get; init; }
    public string Text { get; init; }
    public double Number { get; init; }
    public int Line { get; init; }
    public override string ToString() => Kind == TokKind.Number ? $"num({Number})" : $"{Kind}:{Text}";
}

/// <summary>规则词法器。仅识别受限字符集；未知字符抛 <see cref="RuleSyntaxException"/>。</summary>
internal static class RuleLexer
{
    internal static List<Tok> Tokenize(string source)
    {
        var tokens = new List<Tok>();
        int i = 0, line = 1;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }

            // 注释：# 后面非十六进制视为注释（避免与颜色冲突）。用//亦注释。
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            if (c == '#')
            {
                int j = i + 1;
                while (j < source.Length && Uri.IsHexDigit(source[j])) j++;
                if (j - i - 1 is 6 or 3 or 8)
                {
                    tokens.Add(new Tok { Kind = TokKind.Color, Text = source[i..j], Line = line });
                    i = j;
                    continue;
                }
                // 不是颜色 → 注释到行尾
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            if (c == '"')
            {
                int j = i + 1;
                var sb = new System.Text.StringBuilder();
                while (j < source.Length && source[j] != '"')
                {
                    if (source[j] == '\\' && j + 1 < source.Length)
                    {
                        sb.Append(source[j + 1] switch { 'n' => '\n', 't' => '\t', _ => source[j + 1] });
                        j += 2;
                        continue;
                    }
                    sb.Append(source[j]);
                    j++;
                }
                if (j >= source.Length) throw new RuleSyntaxException(line, "字符串未闭合");
                tokens.Add(new Tok { Kind = TokKind.String, Text = sb.ToString(), Line = line });
                i = j + 1;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
            {
                int j = i;
                while (j < source.Length && (char.IsDigit(source[j]) || source[j] == '.')) j++;
                var text = source[i..j];
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    throw new RuleSyntaxException(line, $"无效数字：{text}");
                tokens.Add(new Tok { Kind = TokKind.Number, Number = num, Text = text, Line = line });
                i = j;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int j = i;
                while (j < source.Length && (char.IsLetterOrDigit(source[j]) || source[j] == '_')) j++;
                tokens.Add(new Tok { Kind = TokKind.Ident, Text = source[i..j], Line = line });
                i = j;
                continue;
            }

            switch (c)
            {
                case '(': tokens.Add(new Tok { Kind = TokKind.LParen, Text = "(", Line = line }); i++; continue;
                case ')': tokens.Add(new Tok { Kind = TokKind.RParen, Text = ")", Line = line }); i++; continue;
                case ',': tokens.Add(new Tok { Kind = TokKind.Comma, Text = ",", Line = line }); i++; continue;
                case ';': tokens.Add(new Tok { Kind = TokKind.Semicolon, Text = ";", Line = line }); i++; continue;
            }

            // 双字符运算符
            if (i + 1 < source.Length)
            {
                var two = source.Substring(i, 2);
                if (two is "==" or "!=" or ">=" or "<=" or "&&" or "||")
                {
                    tokens.Add(new Tok { Kind = TokKind.Op, Text = two, Line = line });
                    i += 2;
                    continue;
                }
            }
            if (c is '=' or '>' or '<' or '!')
            {
                tokens.Add(new Tok { Kind = TokKind.Op, Text = c.ToString(), Line = line });
                i++;
                continue;
            }

            throw new RuleSyntaxException(line, $"意外的字符：'{c}'");
        }

        tokens.Add(new Tok { Kind = TokKind.End, Text = "", Line = line });
        return tokens;
    }
}

/// <summary>规则语法错误（含行号），供 UI 逐行标红。</summary>
public sealed class RuleSyntaxException : Exception
{
    public int Line { get; }
    public RuleSyntaxException(int line, string message) : base($"第 {line} 行：{message}") => Line = line;
}
