using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed record McDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    TimeSpan Interval,
    TimeSpan ExpiresIn,
    string ClientId = "");

internal sealed record McMicrosoftSession(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string Uuid,
    string Name,
    string ClientId = "");

/// <summary>
/// 微软账户设备代码登录：Microsoft OAuth → Xbox Live → XSTS → Minecraft Services。
/// Minecraft 登录要求应用在 Xbox Developer program 中获批；普通 Azure 应用即使开启公共客户端流，
/// 也会在 Minecraft Services 返回 403。因此默认使用已公开的、获批的公共客户端；
/// 如果你的 Azure 应用已获批，把 UserClientId 放到 ClientIds 的第一位即可。
/// </summary>
internal static class McMicrosoftAuth
{
    public const string UserClientId = "e04809bd-5675-406f-850d-e69b7ae0387e";
    public const string FallbackClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";
    private static readonly string[] ClientIds = [UserClientId, FallbackClientId];
    private const string Scope = "XboxLive.signin offline_access";
    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XboxUserUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string MinecraftProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    public static Task<McDeviceCode> StartDeviceCodeAsync(HttpClient http, CancellationToken ct) =>
        StartDeviceCodeAsync(http, ct, ClientIds);

    public static IReadOnlyList<string> ResolveClientIds(string? customClientId) =>
        string.IsNullOrWhiteSpace(customClientId) ? ClientIds : [customClientId.Trim()];

    internal static async Task<McDeviceCode> StartDeviceCodeAsync(HttpClient http, CancellationToken ct,
        IReadOnlyList<string> clientIds)
    {
        McAuthException? last = null;
        foreach (var clientId in clientIds)
        {
            try
            {
                var json = await PostFormAsync(http, DeviceCodeUrl, new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["scope"] = Scope
                }, ct, allowOAuthError: true);
                var code = json["device_code"]?.GetValue<string>();
                var userCode = json["user_code"]?.GetValue<string>();
                var uri = json["verification_uri"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(userCode) || string.IsNullOrWhiteSpace(uri))
                    throw new McAuthException("微软没有返回设备代码，请稍后重试。");
                var interval = json["interval"]?.GetValue<int>() ?? 5;
                var expires = json["expires_in"]?.GetValue<int>() ?? 900;
                return new McDeviceCode(code, userCode, uri, TimeSpan.FromSeconds(Math.Max(2, interval)),
                    TimeSpan.FromSeconds(Math.Max(60, expires)), clientId);
            }
            catch (McAuthException ex) when (!string.IsNullOrEmpty(ex.ErrorCode))
            {
                last = ex;
            }
        }

        throw new McAuthException(
            "微软应用尚未启用设备代码登录（AADSTS70002）。请让应用管理员在 Azure 门户的“身份验证”里勾选“允许公共客户端流”，或改用离线账户。",
            last?.ErrorCode ?? "", null, last?.Status ?? 0);
    }

    public static async Task<McMicrosoftSession> CompleteLoginAsync(
        HttpClient http, McDeviceCode code, IProgress<string>? progress, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + code.ExpiresIn;
        var interval = code.Interval;
        while (DateTime.UtcNow < deadline)
        {
            progress?.Report("等待浏览器确认登录…");
            await Task.Delay(interval, ct);
            JsonNode json;
            try
            {
                json = await PostFormAsync(http, TokenUrl, new Dictionary<string, string>
                {
                    ["client_id"] = code.ClientId,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["device_code"] = code.DeviceCode
                }, ct, allowOAuthError: true);
            }
            catch (McAuthException ex) when (ex.ErrorCode == "authorization_pending")
            {
                continue;
            }
            catch (McAuthException ex) when (ex.ErrorCode == "slow_down")
            {
                interval += TimeSpan.FromSeconds(5);
                continue;
            }
            catch (McAuthException ex) when (ex.ErrorCode == "expired_token")
            {
                throw new McAuthException("登录代码已过期，请重新点击微软登录。");
            }
            catch (McAuthException ex) when (ex.ErrorCode == "authorization_declined")
            {
                throw new McAuthException("你取消了微软登录授权。");
            }

            var access = json["access_token"]?.GetValue<string>();
            var refresh = json["refresh_token"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(access))
                throw new McAuthException("微软登录没有返回访问令牌，请重试。");
            return await CompleteFromMicrosoftAsync(http, access, refresh ?? "", code.ClientId,
                progress, ct);
        }

        throw new McAuthException("登录超时，请重新获取设备代码。");
    }

    public static async Task<McMicrosoftSession> RefreshAsync(HttpClient http, string refreshToken,
        string clientId, IProgress<string>? progress, CancellationToken ct)
    {
        var candidates = string.IsNullOrWhiteSpace(clientId)
            ? ClientIds
            : new[] { clientId }.Concat(ClientIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        McAuthException? last = null;
        foreach (var candidate in candidates)
        {
            try
            {
                var json = await PostFormAsync(http, TokenUrl, new Dictionary<string, string>
                {
                    ["client_id"] = candidate,
                    ["grant_type"] = "refresh_token",
                    ["scope"] = Scope,
                    ["refresh_token"] = refreshToken
                }, ct, allowOAuthError: true);
                var access = json["access_token"]?.GetValue<string>();
                var refresh = json["refresh_token"]?.GetValue<string>() ?? refreshToken;
                if (string.IsNullOrWhiteSpace(access))
                    throw new McAuthException("微软登录已过期，请重新登录。");
                return await CompleteFromMicrosoftAsync(http, access, refresh, candidate, progress, ct);
            }
            catch (McAuthException ex) when (!string.IsNullOrEmpty(ex.ErrorCode))
            {
                last = ex;
            }
        }

        if (last?.ErrorCode == "invalid_grant")
            throw new McAuthException("微软登录已过期，请重新登录。");
        throw last ?? new McAuthException("微软登录已过期，请重新登录。");
    }

    private static async Task<McMicrosoftSession> CompleteFromMicrosoftAsync(HttpClient http,
        string msAccessToken, string refreshToken, string clientId,
        IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("正在验证 Xbox Live…");
        var xbl = await PostJsonAsync(http, XboxUserUrl, new JsonObject
        {
            ["Properties"] = new JsonObject
            {
                ["AuthMethod"] = "RPS",
                ["SiteName"] = "user.auth.xboxlive.com",
                ["RpsTicket"] = "d=" + msAccessToken
            },
            ["RelyingParty"] = "http://auth.xboxlive.com",
            ["TokenType"] = "JWT"
        }, ct);
        var xblToken = xbl["Token"]?.GetValue<string>();
        var uhs = xbl["DisplayClaims"]?["xui"]?.AsArray().FirstOrDefault()?["uhs"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(xblToken) || string.IsNullOrWhiteSpace(uhs))
            throw new McAuthException("Xbox Live 验证失败，请重试。");

        progress?.Report("正在验证 Xbox 安全令牌…");
        JsonNode xsts;
        try
        {
            xsts = await PostJsonAsync(http, XstsUrl, new JsonObject
            {
                ["Properties"] = new JsonObject
                {
                    ["SandboxId"] = "RETAIL",
                    ["UserTokens"] = new JsonArray(xblToken)
                },
                ["RelyingParty"] = "rp://api.minecraftservices.com/",
                ["TokenType"] = "JWT"
            }, ct);
        }
        catch (McAuthException ex) when (ex.Status == 401)
        {
            throw new McAuthException(DescribeXstsError(ex.Response));
        }

        var xstsToken = xsts["Token"]?.GetValue<string>();
        var xstsUhs = xsts["DisplayClaims"]?["xui"]?.AsArray().FirstOrDefault()?["uhs"]?.GetValue<string>() ?? uhs;
        if (string.IsNullOrWhiteSpace(xstsToken))
            throw new McAuthException("Xbox 安全令牌验证失败，请重试。");

        progress?.Report("正在获取 Minecraft 档案…");
        JsonNode mc;
        try
        {
            mc = await PostJsonAsync(http, MinecraftLoginUrl, new JsonObject
            {
                ["identityToken"] = $"XBL3.0 x={xstsUhs};{xstsToken}"
            }, ct);
        }
        catch (McAuthException ex) when (ex.Status == 403)
        {
            throw new McAuthException(
                "该 Azure 应用没有通过 Minecraft Java Edition Game Service API 审核（Invalid app registration）。请在设置里清空自定义客户端 ID，或等待 Mojang 审核通过后再使用。",
                ex.ErrorCode, ex.Response, ex.Status);
        }
        var mcToken = mc["access_token"]?.GetValue<string>();
        var mcExpires = mc["expires_in"]?.GetValue<int>() ?? 86400;
        if (string.IsNullOrWhiteSpace(mcToken))
            throw new McAuthException("Minecraft 登录失败，请确认账户已购买 Java 版。");

        using var request = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcToken);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new McAuthException("该微软账户没有 Minecraft: Java Edition 的游戏所有权。");
        if (!response.IsSuccessStatusCode)
            throw new McAuthException($"获取 Minecraft 档案失败（HTTP {(int)response.StatusCode}）。");
        var profile = ParseJson(body);
        var uuid = profile["id"]?.GetValue<string>();
        var name = profile["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
            throw new McAuthException("Minecraft 档案缺少玩家 ID 或名字。");

        return new McMicrosoftSession(mcToken, refreshToken,
            DateTime.UtcNow.AddSeconds(mcExpires), uuid, name, clientId);
    }

    private static string DescribeXstsError(JsonNode? response)
    {
        var code = response?["XErr"]?.GetValue<long>() ?? 0;
        return code switch
        {
            2148916233 => "该微软账户还没有 Xbox 档案，请先在 xbox.com 创建档案。",
            2148916235 => "Xbox Live 在当前地区不可用。",
            2148916236 or 2148916237 => "该账户需要在 Xbox 页面完成年龄验证。",
            2148916238 => "该账户是未成年人账户，需要家长在 Microsoft 家庭中批准。",
            _ => "Xbox Live 拒绝了登录，请确认账户已开通 Xbox 服务。"
        };
    }

    private static async Task<JsonNode> PostJsonAsync(HttpClient http, string url, JsonObject body,
        CancellationToken ct)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(url, content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        var json = ParseJsonOrNull(text);
        if (!response.IsSuccessStatusCode)
            throw new McAuthException($"认证请求失败（HTTP {(int)response.StatusCode}）。", string.Empty, json, (int)response.StatusCode);
        return json ?? throw new McAuthException("认证服务器返回了空响应。");
    }

    private static async Task<JsonNode> PostFormAsync(HttpClient http, string url,
        Dictionary<string, string> form, CancellationToken ct, bool allowOAuthError = false)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(url, content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        var json = ParseJsonOrNull(text);
        if (!response.IsSuccessStatusCode)
        {
            var code = json?["error"]?.GetValue<string>() ?? "";
            if (allowOAuthError && !string.IsNullOrWhiteSpace(code))
                throw new McAuthException(code, code, json, (int)response.StatusCode);
            var description = json?["error_description"]?.GetValue<string>();
            throw new McAuthException(string.IsNullOrWhiteSpace(description)
                ? $"微软登录失败（HTTP {(int)response.StatusCode}）。"
                : description);
        }
        return json ?? throw new McAuthException("微软登录返回了空响应。");
    }

    private static JsonNode? ParseJsonOrNull(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text); }
        catch { return null; }
    }

    private static JsonNode ParseJson(string text) =>
        ParseJsonOrNull(text) ?? throw new McAuthException("认证服务器返回了无法解析的响应。");
}

internal sealed class McAuthException : Exception
{
    public McAuthException(string message, string errorCode = "", JsonNode? response = null, int status = 0)
        : base(message)
    {
        ErrorCode = errorCode;
        Response = response;
        Status = status;
    }

    public string ErrorCode { get; }
    public JsonNode? Response { get; }
    public int Status { get; }
}
