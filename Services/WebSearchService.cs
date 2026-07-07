using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

public static partial class WebSearchService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly Regex DdgLinkRegex = DdgLinkPattern();
    private static readonly Regex DdgSnippetRegex = DdgSnippetPattern();

    public static async Task<WebSearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var uapiResult = await SearchUapisAsync(query, ct);
            if (uapiResult.Items.Count > 0)
                return uapiResult;
        }
        catch { }

        var customEndpoint = AppSettings.Get("SearchApiEndpoint");
        if (!string.IsNullOrWhiteSpace(customEndpoint))
        {
            try
            {
                var customResult = await SearchCustomApiAsync(customEndpoint, query, ct);
                if (customResult.Items.Count > 0)
                    return customResult;
            }
            catch { }
        }

        try
        {
            var ddgResult = await SearchDuckDuckGoHtmlAsync(query, ct);
            if (ddgResult.Items.Count > 0)
                return ddgResult;
        }
        catch { }

        try
        {
            var ddgApiResult = await SearchDuckDuckGoApiAsync(query, ct);
            if (ddgApiResult.Items.Count > 0)
                return ddgApiResult;
        }
        catch { }

        try
        {
            var wikiResult = await SearchWikipediaAsync(query, ct);
            if (wikiResult.Items.Count > 0)
                return wikiResult;
        }
        catch { }

        return new WebSearchResult
        {
            Query = query,
            Items = [new()
            {
                Title = "未找到结果",
                Snippet = $"搜索 \"{query}\" 未返回相关结果，请尝试更换关键词。",
                Url = ""
            }]
        };
    }

    private static async Task<WebSearchResult> SearchUapisAsync(string query, CancellationToken ct)
    {
        var result = new WebSearchResult { Query = query };

        var url = "https://uapis.cn/api/v1/search/aggregate";

        var apiKey = AppSettings.Get("SearchApiKey");
        var bodyDict = new JsonObject { ["query"] = query };

        var json = bodyDict.ToJsonString();

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("results", out var results)) return result;

        foreach (var item in results.EnumerateArray())
        {
            if (result.Items.Count >= 10) break;

            var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";
            var itemUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var domain = item.TryGetProperty("domain", out var d) ? d.GetString() ?? "" : "";
            var source = item.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(title)) continue;

            result.Items.Add(new WebSearchItem
            {
                Title = title,
                Snippet = snippet,
                Url = itemUrl,
                Source = string.IsNullOrWhiteSpace(domain) ? source : domain
            });
        }

        return result;
    }

    private static async Task<WebSearchResult> SearchCustomApiAsync(string endpoint, string query, CancellationToken ct)
    {
        var result = new WebSearchResult { Query = query };

        var url = endpoint.TrimEnd('/') + "/search?q=" + Uri.EscapeDataString(query) + "&format=json&categories=general&language=auto";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0");

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("results", out var results)) return result;

        foreach (var item in results.EnumerateArray())
        {
            if (result.Items.Count >= 10) break;

            var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var snippet = item.TryGetProperty("content", out var s) ? s.GetString() ?? "" : "";
            var itemUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var engine = item.TryGetProperty("engine", out var e) ? e.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(title)) continue;

            result.Items.Add(new WebSearchItem
            {
                Title = title,
                Snippet = snippet,
                Url = itemUrl,
                Source = string.IsNullOrWhiteSpace(engine) ? "SearXNG" : engine
            });
        }

        return result;
    }

    private static async Task<WebSearchResult> SearchDuckDuckGoHtmlAsync(string query, CancellationToken ct)
    {
        var result = new WebSearchResult { Query = query };

        var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);

        var linkMatches = DdgLinkRegex.Matches(html);
        var snippetMatches = DdgSnippetRegex.Matches(html);

        var count = Math.Min(linkMatches.Count, 10);

        for (int i = 0; i < count; i++)
        {
            var m = linkMatches[i];
            var rawUrl = HtmlDecode(m.Groups[1].Value);
            var title = HtmlDecode(m.Groups[2].Value);

            var actualUrl = ExtractDdgActualUrl(rawUrl);

            string snippet = "";
            if (i < snippetMatches.Count)
                snippet = HtmlDecode(snippetMatches[i].Groups[1].Value);

            if (string.IsNullOrWhiteSpace(title)) continue;

            result.Items.Add(new WebSearchItem
            {
                Title = title,
                Snippet = snippet,
                Url = actualUrl,
                Source = "DuckDuckGo"
            });
        }

        return result;
    }

    private static string ExtractDdgActualUrl(string ddgUrl)
    {
        if (string.IsNullOrWhiteSpace(ddgUrl)) return "";

        if (ddgUrl.StartsWith("//duckduckgo.com/l/", StringComparison.OrdinalIgnoreCase))
        {
            var uddgIdx = ddgUrl.IndexOf("uddg=", StringComparison.OrdinalIgnoreCase);
            if (uddgIdx >= 0)
            {
                var encoded = ddgUrl.Substring(uddgIdx + 5);
                var ampIdx = encoded.IndexOf('&');
                if (ampIdx >= 0) encoded = encoded.Substring(0, ampIdx);
                try { return Uri.UnescapeDataString(encoded); }
                catch { }
            }
        }

        if (ddgUrl.StartsWith("//")) return "https:" + ddgUrl;
        return ddgUrl;
    }

    private static async Task<WebSearchResult> SearchDuckDuckGoApiAsync(string query, CancellationToken ct)
    {
        var result = new WebSearchResult { Query = query };

        var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1&kl=cn-zh";

        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("AbstractText", out var abstractText))
        {
            var text = abstractText.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var title = root.TryGetProperty("Heading", out var heading)
                    ? heading.GetString() ?? query
                    : query;
                var sourceUrl = root.TryGetProperty("AbstractURL", out var absUrl)
                    ? absUrl.GetString() ?? ""
                    : "";
                var sourceName = root.TryGetProperty("AbstractSource", out var absSrc)
                    ? absSrc.GetString() ?? ""
                    : "";

                result.Items.Add(new WebSearchItem
                {
                    Title = title,
                    Snippet = text,
                    Url = sourceUrl,
                    Source = sourceName
                });
            }
        }

        if (root.TryGetProperty("Infobox", out var infobox) &&
            infobox.TryGetProperty("content", out var infoContent))
        {
            var sb = new StringBuilder();
            foreach (var item in infoContent.EnumerateArray())
            {
                if (item.TryGetProperty("label", out var label) &&
                    item.TryGetProperty("value", out var val))
                {
                    var l = label.GetString();
                    var v = val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString();
                    if (!string.IsNullOrWhiteSpace(l) && !string.IsNullOrWhiteSpace(v))
                        sb.AppendLine($"- {l}: {v}");
                }
            }

            if (sb.Length > 0 && result.Items.Count > 0)
                result.Items[0].Snippet += "\n\n详细信息：\n" + sb.ToString();
        }

        return result;
    }

    private static async Task<WebSearchResult> SearchWikipediaAsync(string query, CancellationToken ct)
    {
        var result = new WebSearchResult { Query = query };

        var searchUrl = $"https://zh.wikipedia.org/w/api.php?action=opensearch&search={Uri.EscapeDataString(query)}&limit=5&format=json";

        using var searchResponse = await _http.GetAsync(searchUrl, ct);
        searchResponse.EnsureSuccessStatusCode();

        var searchJson = await searchResponse.Content.ReadAsStringAsync(ct);
        using var searchDoc = JsonDocument.Parse(searchJson);
        var searchRoot = searchDoc.RootElement;

        if (searchRoot.GetArrayLength() < 4) return result;

        var titles = searchRoot[1];
        var urls = searchRoot[3];

        var titleCount = titles.GetArrayLength();
        for (int i = 0; i < titleCount && i < 3; i++)
        {
            var title = titles[i].GetString() ?? "";
            var wikiUrl = urls[i].GetString() ?? "";

            string? snippet = null;
            try
            {
                var extractUrl = $"https://zh.wikipedia.org/w/api.php?action=query&titles={Uri.EscapeDataString(title)}&prop=extracts&exintro=true&explaintext=true&format=json";
                using var extractResponse = await _http.GetAsync(extractUrl, ct);
                if (extractResponse.IsSuccessStatusCode)
                {
                    var extractJson = await extractResponse.Content.ReadAsStringAsync(ct);
                    using var extractDoc = JsonDocument.Parse(extractJson);
                    if (extractDoc.RootElement.TryGetProperty("query", out var q) &&
                        q.TryGetProperty("pages", out var pages))
                    {
                        foreach (var page in pages.EnumerateObject())
                        {
                            if (page.Value.TryGetProperty("extract", out var ext))
                            {
                                snippet = ext.GetString();
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

            result.Items.Add(new WebSearchItem
            {
                Title = title,
                Snippet = snippet ?? $"维基百科条目：{title}",
                Url = wikiUrl,
                Source = "维基百科"
            });
        }

        return result;
    }

    private static string HtmlDecode(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        var text = Regex.Replace(html, "<[^>]+>", "");
        text = text.Replace("&amp;", "&")
                   .Replace("&#x27;", "'")
                   .Replace("&apos;", "'")
                   .Replace("&quot;", "\"")
                   .Replace("&lt;", "<")
                   .Replace("&gt;", ">")
                   .Replace("&nbsp;", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    public static string FormatResult(WebSearchResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"搜索查询：{result.Query}");
        sb.AppendLine($"结果数量：{result.Items.Count}");
        sb.AppendLine();

        for (int i = 0; i < result.Items.Count; i++)
        {
            var item = result.Items[i];
            sb.AppendLine($"### 结果 {i + 1}: {item.Title}");
            if (!string.IsNullOrWhiteSpace(item.Source))
                sb.AppendLine($"来源：{item.Source}");
            sb.AppendLine(item.Snippet);
            if (!string.IsNullOrWhiteSpace(item.Url))
                sb.AppendLine($"链接：{item.Url}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static async Task<WebPageContent> FetchWebPageAsync(string url, CancellationToken ct = default)
    {
        var apiKey = AppSettings.Get("SearchApiKey");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var uapiResult = await FetchViaUapisAsync(url, apiKey, ct);
                if (uapiResult is not null) return uapiResult;
            }
            catch { }
        }

        return await FetchDirectAsync(url, ct);
    }

    private static async Task<WebPageContent?> FetchViaUapisAsync(string url, string apiKey, CancellationToken ct)
    {
        var submitUrl = "https://uapis.cn/api/v1/web/tomarkdown/async";
        var body = JsonSerializer.Serialize(new WebMarkdownBody { Url = url }, TubaNullIgnoreContext.Default.WebMarkdownBody);

        using var submitRequest = new HttpRequestMessage(HttpMethod.Post, submitUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        submitRequest.Headers.Add("Authorization", $"Bearer {apiKey}");

        using var submitResponse = await _http.SendAsync(submitRequest, ct);
        submitResponse.EnsureSuccessStatusCode();

        var submitJson = await submitResponse.Content.ReadAsStringAsync(ct);
        using var submitDoc = JsonDocument.Parse(submitJson);
        var taskId = submitDoc.RootElement.TryGetProperty("task_id", out var tid) ? tid.GetString() : null;
        if (string.IsNullOrWhiteSpace(taskId)) return null;

        for (int i = 0; i < 15; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(2000, ct);

            var statusUrl = $"https://uapis.cn/api/v1/web/tomarkdown/async/{taskId}?task_id={taskId}";
            using var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
            if (!string.IsNullOrWhiteSpace(apiKey))
                statusRequest.Headers.Add("Authorization", $"Bearer {apiKey}");

            using var statusResponse = await _http.SendAsync(statusRequest, ct);
            if (!statusResponse.IsSuccessStatusCode) continue;

            var statusJson = await statusResponse.Content.ReadAsStringAsync(ct);
            using var statusDoc = JsonDocument.Parse(statusJson);
            var root = statusDoc.RootElement;

            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";

            if (status == "completed")
            {
                var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var markdown = root.TryGetProperty("markdown", out var md) ? md.GetString() ?? "" : "";

                if (markdown.Length > 30000)
                    markdown = markdown.Substring(0, 30000) + "\n\n...(内容过长已截断)";

                return new WebPageContent
                {
                    Title = title,
                    Content = markdown,
                    Url = url,
                    ContentType = "markdown"
                };
            }

            if (status is "failed" or "timeout") return null;
        }

        return null;
    }

    private static async Task<WebPageContent> FetchDirectAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);

        var title = ExtractHtmlTitle(html);
        var text = HtmlToText(html);

        if (text.Length > 30000)
            text = text.Substring(0, 30000) + "\n\n...(内容过长已截断)";

        return new WebPageContent
        {
            Title = title,
            Content = text,
            Url = url,
            ContentType = "text"
        };
    }

    private static string ExtractHtmlTitle(string html)
    {
        var m = Regex.Match(html, @"<title[^>]*>([\s\S]*?)</title>", RegexOptions.IgnoreCase);
        if (!m.Success) return "";
        var title = m.Groups[1].Value;
        title = Regex.Replace(title, @"\s+", " ").Trim();
        return HtmlDecode(title);
    }

    private static string HtmlToText(string html)
    {
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<nav[\s\S]*?</nav>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<footer[\s\S]*?</footer>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<header[\s\S]*?</header>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<aside[\s\S]*?</aside>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<noscript[\s\S]*?</noscript>", "", RegexOptions.IgnoreCase);

        html = Regex.Replace(html, @"<br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</p>", "\n\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</div>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</li>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</h[1-6]>", "\n\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</tr>", "\n", RegexOptions.IgnoreCase);

        html = Regex.Replace(html, @"<[^>]+>", "");

        html = System.Net.WebUtility.HtmlDecode(html);

        html = Regex.Replace(html, @"[ \t]+", " ");
        html = Regex.Replace(html, @"\n\s*\n\s*\n", "\n\n");

        return html.Trim();
    }

    [GeneratedRegex("""class="result__a"[^>]*href="([^"]*)"[^>]*>([\s\S]*?)</a>""", RegexOptions.IgnoreCase)]
    private static partial Regex DdgLinkPattern();

    [GeneratedRegex("""class="result__snippet"[^>]*>([\s\S]*?)</a>""", RegexOptions.IgnoreCase)]
    private static partial Regex DdgSnippetPattern();
}

public sealed class WebSearchResult
{
    public string Query { get; init; } = "";
    public List<WebSearchItem> Items { get; init; } = [];
}

public sealed class WebSearchItem
{
    public string Title { get; init; } = "";
    public string Snippet { get; set; } = "";
    public string Url { get; init; } = "";
    public string Source { get; init; } = "";
}

public sealed class WebPageContent
{
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public string Url { get; init; } = "";
    public string ContentType { get; init; } = "text";
}
