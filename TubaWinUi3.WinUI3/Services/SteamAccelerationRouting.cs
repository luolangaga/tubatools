using System.IO.Enumeration;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace TubaWinUi3.Services;

internal static class SteamAccelerationRouting
{
    // Watt child rules can apply to one URL path on the same listening host.
    // Keep them all; selecting a child must not replace its whole parent website.
    internal static SteamAccelerationRule? Select(IEnumerable<SteamAccelerationRule> rules, string host, string path)
        => rules.Where(r => r.Available && r.Domains.Contains(host, StringComparer.OrdinalIgnoreCase))
            .Select(r => (Rule: r, Specificity: Match(r, host, path)))
            .Where(m => m.Specificity >= 0).OrderByDescending(m => m.Specificity).ThenBy(m => m.Rule.Order)
            .ThenBy(m => m.Rule.Id, StringComparer.Ordinal).Select(m => m.Rule).FirstOrDefault();

    private static int Match(SteamAccelerationRule rule, string host, string path)
    {
        if (rule.MatchPatterns.Length == 0) return 0;
        int best = -1;
        foreach (string pattern in rule.MatchPatterns)
        {
            int scheme = pattern.IndexOf("://", StringComparison.Ordinal);
            if (scheme < 0)
            {
                if (FileSystemName.MatchesSimpleExpression(pattern, host, true)) best = Math.Max(best, 0);
                continue;
            }
            int slash = pattern.IndexOf('/', scheme + 3);
            string domain = slash < 0 ? pattern[(scheme + 3)..] : pattern[(scheme + 3)..slash];
            string urlPath = slash < 0 ? "/" : pattern[slash..];
            bool pathMatch = FileSystemName.MatchesSimpleExpression(urlPath, path, false) ||
                (!urlPath.EndsWith('*') && FileSystemName.MatchesSimpleExpression(urlPath.TrimEnd('/') + "/*", path, false));
            if (FileSystemName.MatchesSimpleExpression(domain, host, true) && pathMatch)
                best = Math.Max(best, 1 + urlPath.Count(c => c != '*' && c != '?'));
        }
        return best;
    }
}

internal sealed class SteamAccelerationTransformer(SteamAccelerationRule rule) : HttpTransformer
{
    public override async ValueTask TransformRequestAsync(HttpContext context, HttpRequestMessage request, string prefix, CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(context, request, prefix, cancellationToken);
        // Domain-fronted CDN requests need the origin Host, while TLS uses the rule's SNI.
        request.Headers.Host = context.Request.Host.Host;
        if (!string.IsNullOrWhiteSpace(rule.UserAgent))
        {
            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", rule.UserAgent.Replace("${origin}", context.Request.Headers.UserAgent.ToString(), StringComparison.Ordinal));
        }
        // This is a local transparent proxy, not a public gateway. Do not send
        // loopback X-Forwarded-* headers to origin sites and their abuse filters.
        foreach (string header in new[] { "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-Prefix" }) request.Headers.Remove(header);
    }
}
