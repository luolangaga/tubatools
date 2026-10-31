using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

/// <summary>Only return an unambiguous model match; avoid guessing from a partial model number.</summary>
internal static class LocalRankingMatcher
{
    private static string Normalize(string name)
    {
        name = Regex.Replace(name, @"\([^)]*\)", " ");
        name = Regex.Replace(name, @"@.*$", " ");
        name = Regex.Replace(name, @"\b(Intel|AMD|NVIDIA|Corporation|CPU|Processor|Graphics|GeForce|Radeon|Core|TM|R)\b", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(name.ToUpperInvariant(), @"[^A-Z0-9]+", " ").Trim();
    }

    internal static T? Find<T>(string detected, IEnumerable<T> entries, Func<T, string> name) where T : class
    {
        var actual = Normalize(detected);
        if (actual.Length < 4 || !actual.Any(char.IsDigit)) return null;
        var matches = entries.Where(e =>
        {
            var candidate = Normalize(name(e));
            return candidate.Length >= 4 && candidate.Any(char.IsDigit) &&
                actual == candidate;
        }).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
