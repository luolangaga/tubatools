using System.Net.Http;

namespace TubaWinUi3.Services;

public static class SteamCompanionService
{
    public static async Task<string> ProbeAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var results = await Task.WhenAll(new[] { "https://github.com", "https://steamcommunity.com", "https://store.steampowered.com" }.Select(async url =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                return $"{new Uri(url).Host}  ·  {(response.IsSuccessStatusCode ? "可访问" : $"HTTP {(int)response.StatusCode}")}  ·  {watch.ElapsedMilliseconds} ms";
            }
            catch (HttpRequestException) { return $"{new Uri(url).Host}  ·  连接失败"; }
            catch (TaskCanceledException) { return $"{new Uri(url).Host}  ·  连接超时"; }
        }));
        return string.Join("\n", results);
    }
}
