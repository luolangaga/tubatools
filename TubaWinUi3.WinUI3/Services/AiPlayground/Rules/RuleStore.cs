using System.Globalization;

namespace TubaWinUi3.Services.AiPlayground.Rules;

/// <summary>
/// 规则的持久化（按模型，人可读文本文件）与默认规则集（按类别稳定配色）。
/// 存储于 &lt;DataDir&gt;/AiPlayground/rules/&lt;modelId&gt;.rules.txt。
/// </summary>
public static class RuleStore
{
    internal static string? RootOverride;

    private static string RulesDir => RootOverride
        ?? Path.Combine(ConfigManager.GetDataDir(), "AiPlayground", "rules");

    private static string PathFor(string modelId, string kind) =>
        Path.Combine(RulesDir, $"{Sanitize(modelId)}.{kind}.txt");

    private static string Sanitize(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_').ToArray();
        var result = new string(chars).Trim('_');
        return string.IsNullOrEmpty(result) ? "model" : result;
    }

    /// <summary>读取规则的原始文本；无存档返回默认规则文本。</summary>
    public static string LoadText(string modelId, string kind) => LoadText(modelId, kind, DefaultRules(kind));

    public static string LoadText(string modelId, string kind, string fallback)
    {
        try
        {
            var path = PathFor(modelId, kind);
            return File.Exists(path) ? File.ReadAllText(path) : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static void SaveText(string modelId, string kind, string text)
    {
        try
        {
            Directory.CreateDirectory(RulesDir);
            var path = PathFor(modelId, kind);
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
        }
        catch { }
    }

    public static void ResetToDefault(string modelId, string kind)
    {
        try
        {
            var path = PathFor(modelId, kind);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    public static bool HasCustom(string modelId, string kind)
    {
        try { return File.Exists(PathFor(modelId, kind)); } catch { return false; }
    }

    /// <summary>解析文本为规则（含逐行错误）。</summary>
    public static (List<DetectionRule> Rules, List<(int Line, string Error)> Errors) Parse(string text) =>
        RuleParser.Parse(text);

    /// <summary>kind: "draw"（绘制规则）。默认文本为空 = 按类别稳定配色。</summary>
    public static string DefaultRules(string kind) => "";

    /// <summary>
    /// 按类别名生成稳定颜色（同类别恒同色）。用于无自定义规则时的着色，
    /// 以及为其生成「一键生成按类别配色规则」文本。
    /// </summary>
    public static string ColorForLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return "#FF8C00";
        unchecked
        {
            int hash = 17;
            foreach (var c in label.ToLowerInvariant()) hash = hash * 31 + c;
            var hue = Math.Abs(hash) % 360;
            return HsvToHex(hue, 0.72, 0.95);
        }
    }

    private static string HsvToHex(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        int R = (int)Math.Round((r + m) * 255);
        int G = (int)Math.Round((g + m) * 255);
        int B = (int)Math.Round((b + m) * 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");
    }
}
