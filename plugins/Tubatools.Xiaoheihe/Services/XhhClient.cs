using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tubatools.Xiaoheihe.Services;

internal class XhhException : Exception
{
    public XhhException(string message, string? status = null, string? path = null, Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
        Path = path;
    }

    public string? Status { get; }
    public string? Path { get; }
}

internal sealed class XhhAuthException(string message, string? status = null, string? path = null)
    : XhhException(message, status, path);

internal sealed class XhhCaptchaException(string message, string? path = null)
    : XhhException(message, "show_captcha", path);

internal sealed class XhhTwoFactorException(string message, string challengeJson)
    : XhhException(message, "need_google_check")
{
    public string ChallengeJson { get; } = challengeJson;
}

internal sealed record XhhQrState(JsonElement Result, JsonElement Payload);

/// <summary>小黑盒 API 客户端：签名、公共参数、cookie、表单 POST 与业务状态检查。</summary>
internal sealed class XhhClient
{
    internal const string ApiBase = "https://api.xiaoheihe.cn";
    internal const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";

    private static readonly HttpClient Http = CreateHttpClient();
    private readonly Dictionary<string, string> _cookies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _cookieGate = new();

    private static readonly Dictionary<string, string?> BaseParams = new()
    {
        ["app"] = "heybox",
        ["os_type"] = "web",
        ["x_app"] = "heybox_website",
        ["x_client_type"] = "web",
        ["x_os_type"] = "Windows",
        ["x_client_version"] = "",
        ["client_type"] = "web",
        ["web_version"] = "3.0",
        ["version"] = "999.0.4"
    };

    private static readonly string[] LoginCookies =
    [
        "heybox_id", "user_heybox_id", "pkey", "user_pkey", "nickname", "avatar", "level",
        "wa", "wr", "ws", "wj", "wc", "wiki_power", "permission", "ac", "br",
        "bes", "bea", "beb", "bms", "bma", "bmb", "fss", "fsa", "fsb"
    ];

    public XhhClient(string? cookie = "", string? heyboxId = "")
    {
        if (!string.IsNullOrWhiteSpace(cookie)) LoadCookie(cookie);
        HeyboxId = heyboxId ?? GetCookie("user_heybox_id") ?? GetCookie("heybox_id") ?? "";
    }

    public string HeyboxId { get; set; }
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(GetCookie("user_pkey")) ||
                              !string.IsNullOrWhiteSpace(GetCookie("pkey"));
    public bool HasRiskToken => !string.IsNullOrWhiteSpace(GetCookie("x_xhh_tokenid"));

    private static readonly HashSet<string> SendCookieNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "x_xhh_tokenid", "pkey", "user_pkey", "heybox_id", "user_heybox_id",
        "wa", "wr", "ws", "wj", "wc", "wiki_power", "permission", "ac", "br",
        "bes", "bea", "beb", "bms", "bma", "bmb", "fss", "fsa", "fsb"
    };

    public string CookieHeader
    {
        get
        {
            lock (_cookieGate)
                return string.Join("; ", _cookies
                    .Where(kv => SendCookieNames.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    .Select(kv => $"{kv.Key}={kv.Value}"));
        }
    }

    public IReadOnlyDictionary<string, string> Cookies
    {
        get
        {
            lock (_cookieGate) return new Dictionary<string, string>(_cookies, StringComparer.OrdinalIgnoreCase);
        }
    }

    public string? GetCookie(string name)
    {
        lock (_cookieGate)
            return _cookies.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    public void SetCookie(string name, string? value)
    {
        lock (_cookieGate)
        {
            if (string.IsNullOrWhiteSpace(value)) _cookies.Remove(name);
            else _cookies[name] = value;
        }
    }

    public void LoadCookie(string cookieHeader)
    {
        lock (_cookieGate)
        {
            foreach (string part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int index = part.IndexOf('=');
                if (index <= 0) continue;
                string name = part[..index].Trim();
                string value = part[(index + 1)..].Trim();
                if (name.Length > 0) _cookies[name] = value;
            }
        }
        if (string.IsNullOrWhiteSpace(HeyboxId))
            HeyboxId = GetCookie("user_heybox_id") ?? GetCookie("heybox_id") ?? "";
    }

    public void ClearLogin()
    {
        lock (_cookieGate)
        {
            foreach (string name in LoginCookies) _cookies.Remove(name);
        }
        HeyboxId = "";
    }

    public XhhClient Clone()
    {
        var clone = new XhhClient();
        lock (_cookieGate)
        {
            foreach (var (key, value) in _cookies) clone._cookies[key] = value;
        }
        clone.HeyboxId = HeyboxId;
        return clone;
    }

    public XhhClient WithPkey(string pkey, string? heyboxId)
    {
        XhhClient clone = Clone();
        clone.SetCookie("pkey", pkey);
        clone.SetCookie("user_pkey", pkey);
        if (!string.IsNullOrWhiteSpace(heyboxId))
        {
            clone.HeyboxId = heyboxId;
            clone.SetCookie("heybox_id", heyboxId);
            clone.SetCookie("user_heybox_id", heyboxId);
        }
        return clone;
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        IReadOnlyDictionary<string, string?>? form = null,
        CancellationToken ct = default)
    {
        var merged = new Dictionary<string, string?>(BaseParams, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(HeyboxId)) merged["heybox_id"] = HeyboxId;
        if (query is not null)
            foreach (var (key, value) in query)
                merged[key] = value;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string nonce = XhhSigner.GenNonce();
        merged["hkey"] = XhhSigner.Hkey(path, now, nonce);
        merged["_time"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture);
        merged["nonce"] = nonce;
        merged["_rnd"] = XhhSigner.Rnd16(now, nonce, path);

        string queryString = string.Join('&', merged
            .Where(kv => kv.Value is not null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));
        string url = $"{ApiBase}{path}?{queryString}";

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.xiaoheihe.cn/");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.xiaoheihe.cn");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        if (!string.IsNullOrWhiteSpace(CookieHeader))
            request.Headers.TryAddWithoutValidation("Cookie", CookieHeader);

        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            string body = string.Join('&', (form ?? new Dictionary<string, string?>())
                .Where(kv => kv.Value is not null)
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));
            request.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded")
            {
                CharSet = "UTF-8"
            };
        }

        string text;
        int statusCode;
        try
        {
            using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
            statusCode = (int)response.StatusCode;
            MergeSetCookies(response);
            text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new XhhException("请求超时。请检查网络或代理后重试。", path: path, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new XhhException($"网络请求失败：{ex.Message}。请检查网络、代理或防火墙设置。", path: path, inner: ex);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new XhhException($"服务器返回了非 JSON 内容（HTTP {statusCode}）。", path: path);
        }
    }

    public async Task<JsonElement> ResultAsync(
        string method,
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        IReadOnlyDictionary<string, string?>? form = null,
        CancellationToken ct = default)
    {
        JsonElement payload = await RequestAsync(method, path, query, form, ct).ConfigureAwait(false);
        if (payload.ValueKind != JsonValueKind.Object) return payload;

        string status = payload.TryGetProperty("status", out JsonElement statusElement)
            ? JsonExtensions.AsText(statusElement)
            : "";

        if (string.IsNullOrWhiteSpace(status))
            return payload;

        if (status == "ok") return payload.TryGetProperty("result", out JsonElement result) ? result : payload;
        if (status == "show_captcha")
            throw new XhhCaptchaException(
                "小黑盒要求风控校验：请到「账号 → 网页登录」打开一次官方页面并读取登录状态，取得 x_xhh_tokenid 后再试。", path);
        if (status == "need_google_check")
        {
            string challenge = payload.TryGetProperty("result", out JsonElement challengeElement)
                ? challengeElement.GetRawText()
                : "{}";
            throw new XhhTwoFactorException("该账号开启了 Google 两步验证，请使用网页登录完成验证。", challenge);
        }
        if (status is "relogin" or "login" or "expired")
            throw new XhhAuthException("登录态已失效，请重新登录。", status, path);

        string message = payload.TryGetProperty("msg", out JsonElement msg) ? JsonExtensions.AsText(msg) : "";
        throw new XhhException(string.IsNullOrWhiteSpace(message) ? $"接口返回失败（status={status}）。" : message, status, path);
    }

    public Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default)
        => ResultAsync("GET", path, query, null, ct);

    public Task<JsonElement> PostAsync(string path, IReadOnlyDictionary<string, string?>? form = null,
        IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default)
        => ResultAsync("POST", path, query, form, ct);

    public Task<JsonElement> RestoreLoginAsync(CancellationToken ct = default)
        => GetAsync("/account/restore_login", null, ct);

    public Task<JsonElement> LogoutAsync(CancellationToken ct = default)
        => GetAsync("/account/logout", null, ct);

    public Task<JsonElement> QrcodeUrlAsync(CancellationToken ct = default)
        => GetAsync("/account/get_qrcode_url/", new Dictionary<string, string?> { ["app"] = "web" }, ct);

    public async Task<XhhQrState> QrStateAsync(string qr, CancellationToken ct = default)
    {
        JsonElement payload = await RequestAsync("GET", "/account/qr_state/",
            new Dictionary<string, string?> { ["qr"] = qr, ["app"] = "web" }, null, ct).ConfigureAwait(false);

        string status = payload.TryGetProperty("status", out JsonElement statusElement)
            ? JsonExtensions.AsText(statusElement)
            : "";
        if (status == "need_google_check")
        {
            string challenge = payload.TryGetProperty("result", out JsonElement challengeElement)
                ? challengeElement.GetRawText()
                : "{}";
            throw new XhhTwoFactorException("该账号开启了 Google 两步验证，请使用网页登录完成验证。", challenge);
        }
        if (status != "ok")
        {
            string message = payload.TryGetProperty("msg", out JsonElement msg) ? JsonExtensions.AsText(msg) : "";
            throw new XhhException(string.IsNullOrWhiteSpace(message) ? $"二维码状态查询失败（status={status}）。" : message,
                status, "/account/qr_state/");
        }
        JsonElement result = payload.TryGetProperty("result", out JsonElement value) ? value : payload;
        return new XhhQrState(result, payload);
    }

    public Task<JsonElement> SearchAsync(string q, string type = "general", int offset = 0, int limit = 20,
        CancellationToken ct = default)
        => GetAsync("/bbs/app/api/general/search/v1", new Dictionary<string, string?>
        {
            ["q"] = q,
            ["search_type"] = type,
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["dw"] = "628",
            ["is_pull_down"] = "0"
        }, ct);

    public Task<JsonElement> FeedsAsync(int offset, int pull = 0, CancellationToken ct = default)
        => GetAsync("/bbs/app/feeds", new Dictionary<string, string?>
        {
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["pull"] = pull.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["dw"] = "604"
        }, ct);

    public Task<JsonElement> LinkTreeAsync(string linkId, int page = 1, int limit = 20, string sort = "hot",
        bool first = true, CancellationToken ct = default)
        => GetAsync("/bbs/app/link/tree", new Dictionary<string, string?>
        {
            ["link_id"] = linkId,
            ["is_first"] = first ? "1" : "0",
            ["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["index"] = "1",
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sort_filter"] = sort
        }, ct);

    public Task<JsonElement> SubCommentsAsync(string linkId, string rootCommentId, int offset = 0, int limit = 10,
        CancellationToken ct = default)
        => GetAsync("/bbs/app/comment/sub/comments", new Dictionary<string, string?>
        {
            ["link_id"] = linkId,
            ["root_comment_id"] = rootCommentId,
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }, ct);

    public Task<JsonElement> CommentCreateAsync(string linkId, string text, string replyId = "-1",
        string rootId = "-1", CancellationToken ct = default)
        => PostAsync("/bbs/app/comment/create", new Dictionary<string, string?>
        {
            ["is_cy"] = "0",
            ["link_id"] = linkId,
            ["reply_id"] = replyId,
            ["root_id"] = rootId,
            ["text"] = text
        }, null, ct);

    public Task<JsonElement> FavourAsync(string linkId, bool favour, int favourType, CancellationToken ct = default)
        => PostAsync("/bbs/app/link/favour", new Dictionary<string, string?>
        {
            ["favour_type"] = favourType.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["link_id"] = linkId,
            ["favour"] = favour ? "1" : "0"
        }, null, ct);

    public Task<JsonElement> CommentSupportAsync(string commentId, string linkId, int supportType = 1,
        CancellationToken ct = default)
        => PostAsync("/bbs/app/comment/support", new Dictionary<string, string?>
        {
            ["comment_id"] = commentId,
            ["link_id"] = linkId,
            ["support_type"] = supportType.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }, null, ct);

    public Task<JsonElement> AwardAsync(string linkId, bool award, CancellationToken ct = default)
        => PostAsync("/bbs/app/profile/award/link", new Dictionary<string, string?>
        {
            ["link_id"] = linkId,
            ["award_type"] = award ? "1" : "0",
            ["target"] = "heybox_app"
        }, null, ct);

    public Task<JsonElement> FollowUserAsync(string userId, bool follow, CancellationToken ct = default)
        => PostAsync(follow ? "/bbs/app/profile/follow/user" : "/bbs/app/profile/follow/user/cancel",
            new Dictionary<string, string?>
            {
                ["following_id"] = userId,
                ["follower_id"] = HeyboxId
            }, null, ct);

    public Task<JsonElement> FollowGameAsync(string steamAppId, bool follow, CancellationToken ct = default)
        => PostAsync(follow ? "/account/follow_game/" : "/account/unfollow_game/",
            new Dictionary<string, string?> { ["steam_appid"] = steamAppId }, null, ct);

    public Task<JsonElement> PublishPostAsync(IReadOnlyDictionary<string, string?> form, CancellationToken ct = default)
        => RequestAsync("POST", "/bbs/app/api/link/post", null, form, ct);

    public Task<JsonElement> SignInAsync(CancellationToken ct = default)
        => PostAsync("/task/sign_v3/sign", new Dictionary<string, string?> { ["is_cy"] = "0" }, null, ct);

    public Task<JsonElement> SignListAsync(CancellationToken ct = default)
        => GetAsync("/task/sign_list/", null, ct);

    public Task<JsonElement> TaskListAsync(CancellationToken ct = default)
        => GetAsync("/task/list/", null, ct);

    public Task<JsonElement> TaskListV2Async(CancellationToken ct = default)
        => GetAsync("/task/list_v2/", null, ct);

    public Task<JsonElement> UserProfileAsync(string userId, CancellationToken ct = default)
        => GetAsync("/bbs/app/profile/user/profile", new Dictionary<string, string?> { ["userid"] = userId }, ct);

    public Task<JsonElement> UserLinksAsync(string userId, int offset = 0, int limit = 20, CancellationToken ct = default)
        => GetAsync("/bbs/app/profile/user/link/list_v2", new Dictionary<string, string?>
        {
            ["userid"] = userId,
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }, ct);

    public Task<JsonElement> FavLinksAsync(int offset = 0, int limit = 30, CancellationToken ct = default)
        => GetAsync("/bbs/app/profile/fav/folder/v2/links", new Dictionary<string, string?>
        {
            ["enable_new_style_collect"] = "1",
            ["dw"] = "1072",
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }, ct);

    public Task<JsonElement> HistoryAsync(int offset = 0, int limit = 30, CancellationToken ct = default)
        => GetAsync("/bbs/app/profile/history/visit", new Dictionary<string, string?>
        {
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["type"] = "all",
            ["dw"] = "636"
        }, ct);

    public Task<JsonElement> MessagesAsync(int listType, int offset = 0, int limit = 20, CancellationToken ct = default)
        => GetAsync("/bbs/app/user/message", new Dictionary<string, string?>
        {
            ["list_type"] = listType.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }, ct);

    public Task<JsonElement> OwnedGamesAsync(int offset = 0, int limit = 20, CancellationToken ct = default)
        => GetAsync("/game/owned_game/game_list", new Dictionary<string, string?>
        {
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["game_type"] = "steam"
        }, ct);

    public Task<JsonElement> HotGamesAsync(int offset = 0, int limit = 20, CancellationToken ct = default)
        => GetAsync("/game/owned_game/hot_list", new Dictionary<string, string?>
        {
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }, ct);

    public Task<JsonElement> GameDetailAsync(string appId, string gameType = "", CancellationToken ct = default)
    {
        string path = gameType == "mobile" ? "/game/mobile/get_game_detail/" : "/game/get_game_detail/";
        return GetAsync(path, new Dictionary<string, string?> { ["appid"] = appId }, ct);
    }

    public Task<JsonElement> GameIntroductionAsync(string steamAppId, CancellationToken ct = default)
        => GetAsync("/game/game_introduction/", new Dictionary<string, string?>
        {
            ["steam_appid"] = steamAppId,
            ["return_json"] = "1"
        }, ct);

    public Task<JsonElement> TopicCategoriesAsync(CancellationToken ct = default)
        => GetAsync("/bbs/app/topic/categories", null, ct);

    public Task<JsonElement> TopicFeedsAsync(string topicId, int offset = 0, int limit = 20, string sort = "",
        CancellationToken ct = default)
        => GetAsync("/bbs/app/topic/feeds", new Dictionary<string, string?>
        {
            ["topic_id"] = topicId,
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sort_filter"] = sort
        }, ct);

    public Task<JsonElement> SearchWelcomeAsync(CancellationToken ct = default)
        => GetAsync("/bbs/app/api/search/welcome_page/v2", null, ct);

    public Task<JsonElement> EditorTopicsAsync(CancellationToken ct = default)
        => GetAsync("/bbs/app/api/post_editor/topic_selection/index", new Dictionary<string, string?>
        {
            ["is_post"] = "1",
            ["post_tab"] = "1",
            ["is_new_style"] = "1",
            ["type"] = "list"
        }, ct);

    public Task<JsonElement> SearchEditorTopicsAsync(string query, CancellationToken ct = default)
        => GetAsync("/bbs/app/api/post_editor/topic_selection/search", new Dictionary<string, string?>
        {
            ["q"] = query,
            ["limit"] = "20"
        }, ct);

    public Task<JsonElement> SearchFoundAsync(CancellationToken ct = default)
        => GetAsync("/bbs/app/api/search/found", null, ct);

    private void MergeSetCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;
        foreach (string raw in values)
        {
            int semicolon = raw.IndexOf(';');
            string pair = semicolon >= 0 ? raw[..semicolon] : raw;
            int equals = pair.IndexOf('=');
            if (equals <= 0) continue;
            string name = pair[..equals].Trim();
            string value = pair[(equals + 1)..].Trim();
            bool expired = raw.Contains("Max-Age=0", StringComparison.OrdinalIgnoreCase) ||
                           raw.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
            SetCookie(name, expired || value.Length == 0 ? null : value);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(12),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AllowAutoRedirect = true,
            UseCookies = false
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
    }
}

internal static class JsonExtensions
{
    public static string AsText(this JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => ""
    };

    public static string Str(this JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            ? value.AsText()
            : "";

    public static long Long(this JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
            return 0;
        long number;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out number)) return number;
            if (value.TryGetDouble(out double real)) return (long)real;
        }
        return long.TryParse(value.AsText(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    public static bool Bool(this JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.TryGetInt64(out long number) && number != 0,
            JsonValueKind.String => value.GetString() is "1" or "true" or "True",
            _ => false
        };
    }

    public static JsonElement Obj(this JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) &&
           value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    public static IReadOnlyList<JsonElement> Arr(this JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) &&
           value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [];

    public static string FirstStr(this JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            string value = element.Str(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    public static long FirstLong(this JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            long value = element.Long(name);
            if (value != 0) return value;
        }
        return 0;
    }
}
