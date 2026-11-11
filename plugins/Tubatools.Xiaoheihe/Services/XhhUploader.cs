using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Tubatools.Xiaoheihe.Services;

internal sealed record XhhUploadedImage(string Url, string Thumb, int Width, int Height);

/// <summary>小黑盒图片上传：upload/info → upload/token → 腾讯云 COS PUT → upload/callback。</summary>
internal static class XhhUploader
{
    internal static async Task<XhhUploadedImage> UploadAsync(XhhClient client, string path, CancellationToken ct)
    {
        if (!File.Exists(path)) throw new XhhException("图片文件不存在：" + path);
        byte[] bytes = await File.ReadAllBytesAsync(path, ct);
        string name = Path.GetFileName(path);
        string mime = MimeType(name);
        (int width, int height) = await DimensionsAsync(path, ct);

        string fileInfo = JsonSerializer.Serialize(new[]
        {
            new { name, mimetype = mime, fsize = bytes.Length, width, height, duration = 0 }
        });
        JsonElement info = await client.PostAsync("/bbs/app/api/qcloud/cos/upload/info/v2",
            new Dictionary<string, string?>
            {
                ["file_infos"] = fileInfo,
                ["scope"] = "bbs",
                ["need_cache"] = "0"
            }, null, ct);
        string key = info.Arr("keys").FirstOrDefault().AsText();
        string bucket = info.Str("bucket");
        string region = info.Str("region");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(region))
            throw new XhhException("图片上传登记失败：服务器没有返回完整的对象信息。");

        JsonElement token = await client.PostAsync("/bbs/app/api/qcloud/cos/upload/token/v2",
            new Dictionary<string, string?>
            {
                ["bucket"] = bucket,
                ["keys"] = JsonSerializer.Serialize(new[] { key }),
                ["mimetypes"] = JsonSerializer.Serialize(new[] { mime }),
                ["is_multipart_upload"] = "0"
            }, null, ct);
        JsonElement credentials = token.Obj("credentials");
        string secretId = credentials.Str("tmpSecretId");
        string secretKey = credentials.Str("tmpSecretKey");
        string sessionToken = credentials.Str("sessionToken");
        string startTime = token.Str("startTime");
        string expiredTime = token.Str("expiredTime");
        if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(secretKey) ||
            string.IsNullOrWhiteSpace(sessionToken))
            throw new XhhException("图片上传凭证获取失败。");

        await CosPutAsync(region, bucket, key, bytes, mime, startTime, expiredTime,
            secretId, secretKey, sessionToken, ct);

        JsonElement callback = await client.PostAsync("/bbs/app/api/qcloud/cos/upload/callback/v2",
            new Dictionary<string, string?>
            {
                ["is_finished"] = "true",
                ["keys"] = JsonSerializer.Serialize(new[] { key })
            }, null, ct);
        string url = callback.Arr("preview_urls").FirstOrDefault().AsText();
        string thumb = callback.Arr("thumbs").FirstOrDefault().AsText();
        if (string.IsNullOrWhiteSpace(url))
            throw new XhhException("图片上传完成，但服务器没有返回图片地址。");
        return new XhhUploadedImage(url, thumb, width, height);
    }

    private static async Task CosPutAsync(string region, string bucket, string key, byte[] bytes, string mime,
        string startTime, string expiredTime, string secretId, string secretKey, string sessionToken, CancellationToken ct)
    {
        string host = $"{bucket}.cos.{region}.myqcloud.com";
        string uri = key.StartsWith('/') ? key : "/" + key;
        string signTime = $"{startTime};{expiredTime}";
        string signKey = HmacSha1Hex(secretKey, signTime);
        string httpString = $"put\n{uri}\n\nhost={host}\n";
        string stringToSign = $"sha1\n{signTime}\n{Sha1Hex(httpString)}\n";
        string signature = HmacSha1Hex(signKey, stringToSign);
        string authorization =
            $"q-sign-algorithm=sha1&q-ak={secretId}&q-sign-time={signTime}&q-key-time={signTime}" +
            $"&q-header-list=host&q-url-param-list=&q-signature={signature}";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        using var request = new HttpRequestMessage(HttpMethod.Put, $"https://{host}{uri}");
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-cos-security-token", sessionToken);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            string text = await response.Content.ReadAsStringAsync(ct);
            throw new XhhException($"图片上传失败（HTTP {(int)response.StatusCode}）：{XhhFormat.Clip(text, 200)}");
        }
    }

    private static async Task<(int Width, int Height)> DimensionsAsync(string path, CancellationToken ct)
    {
        try
        {
            using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    private static string MimeType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".mp4" => "video/mp4",
        _ => "image/jpeg"
    };

    private static string Sha1Hex(string text)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string HmacSha1Hex(string key, string text)
        => HmacSha1Hex(Encoding.UTF8.GetBytes(key), text);

    private static string HmacSha1Hex(byte[] key, string text)
    {
        using var hmac = new HMACSHA1(key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
