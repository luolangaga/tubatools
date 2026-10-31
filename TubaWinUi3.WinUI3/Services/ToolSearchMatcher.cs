using System.Collections.Concurrent;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>Shared relevance matching for catalog and command search.</summary>
internal static class ToolSearchMatcher
{
    private static readonly ConcurrentDictionary<string, string> Initials = new();
    private static readonly Lazy<Encoding> ChineseEncoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    });
    // GB2312 first-level Chinese characters are ordered by pronunciation. Unmapped
    // characters remain unchanged, rather than inventing an initial for rare characters.
    private static readonly int[] InitialBoundaries =
        [-20319, -20284, -19776, -19219, -18711, -18527, -18240, -17923,
         -17418, -16475, -16213, -15641, -15166, -14923, -14915, -14631,
         -14150, -14091, -13319, -12839, -12557, -11848, -11056];
    private const string InitialLetters = "ABCDEFGHJKLMNOPQRSTWXYZ";
    private static readonly (string[] Queries, string[] Terms)[] Intents =
    [
        (["查硬盘寿命", "硬盘异常", "磁盘健康", "硬盘健康"], ["SMART", "硬盘健康", "磁盘健康", "CrystalDiskInfo"]),
        (["测试内存稳定性", "内存报错", "内存稳定性"], ["内存测试", "内存稳定", "MemTest", "TestMem"]),
        (["游戏掉帧", "游戏卡顿", "查看帧率"], ["游戏监控", "帧率", "FPS"]),
        (["电脑卡顿", "电脑很慢"], ["进程管理", "启动项", "硬盘健康", "磁盘健康"]),
        (["新机验收", "新电脑验机"], ["验机", "新机", "设备检测"]),
    ];

    internal static double Score(string query, string name, IEnumerable<string?>? details = null)
    {
        query = query.Trim();
        if (query.Length == 0) return 0;
        var texts = new[] { name }.Concat(details ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>().ToArray();
        double score = name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 100 :
            name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 80 :
            name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 60 : 0;
        score += texts.Skip(1).Count(s => s.Contains(query, StringComparison.OrdinalIgnoreCase)) * 20;
        if (name.Any(c => c > 127) && query.Length >= 2 && query.All(char.IsAsciiLetterOrDigit) &&
            GetInitials(name).Contains(query, StringComparison.OrdinalIgnoreCase)) score += 30;
        foreach (var (queries, terms) in Intents)
            if (queries.Any(q => query.Equals(q, StringComparison.OrdinalIgnoreCase)) &&
                texts.Any(s => terms.Any(t => s.Contains(t, StringComparison.OrdinalIgnoreCase))))
                score += 25;
        return score;
    }

    private static string GetInitials(string name) => Initials.GetOrAdd(name, text =>
    {
        try
        {
            var result = new StringBuilder();
            foreach (var rune in text.EnumerateRunes())
            {
                var original = rune.ToString();
                if (rune.IsAscii) { result.Append(original); continue; }
                try
                {
                    var bytes = ChineseEncoding.Value.GetBytes(original);
                    var code = bytes.Length == 2 ? bytes[0] * 256 + bytes[1] - 65536 : 0;
                    if (code >= InitialBoundaries[0] && code <= -10247)
                    {
                        var i = Array.FindLastIndex(InitialBoundaries, boundary => boundary <= code);
                        result.Append(InitialLetters[i]);
                    }
                    else result.Append(original);
                }
                catch (EncoderFallbackException) { result.Append(original); }
            }
            return result.ToString();
        }
        catch { return text; }
    });
}
