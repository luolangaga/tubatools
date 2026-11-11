using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tubatools.Xiaoheihe.Services;

/// <summary>
/// 小黑盒 Web API 的 hkey 签名（与网页 bundle 中的 Tr() 调用链等价）。
/// hkey = 5 字符主干 + 2 位十进制校验。
/// </summary>
internal static class XhhSigner
{
    internal const string Key = "AB45STUVWZEFGJ6CH01D237IXYPQRKLMN89";

    internal static string Hkey(string path, long time, string nonce)
    {
        string normalized = NormalizePath(path);
        string timePart = MapSlice(time.ToString(CultureInfo.InvariantCulture), Key, 2);
        string pathPart = Map(normalized, Key);
        string noncePart = Map(nonce, Key);

        string seed = Interleave(timePart, pathPart, noncePart);
        if (seed.Length > 20) seed = seed[..20];

        byte[] digestBytes = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        string digest = Convert.ToHexString(digestBytes).ToLowerInvariant();

        var codes = new List<int>(6);
        foreach (char c in digest[^6..]) codes.Add(c);
        int check = Mix(codes).Sum() % 100;
        string body = MapSlice(digest[..5], Key, 4);
        return body + check.ToString("00", CultureInfo.InvariantCulture);
    }

    internal static (string Hkey, long Time, string Nonce) Sign(string path, long? time = null, string? nonce = null)
    {
        long stamp = time ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string value = nonce ?? GenNonce();
        return (Hkey(path, stamp, value), stamp, value);
    }

    internal static string GenNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>网页端当前的 _rnd=16 签名（AES-256-GCM，附加数据 heybox-rnd:v16）。</summary>
    internal static string Rnd16(long time, string nonce, string path)
    {
        byte[] key = Convert.FromBase64String("HFFSTsqmvG3uGxPNXDQW+1VARfqgG4nT6LS1PX7F8NQ=");
        byte[] iv = RandomNumberGenerator.GetBytes(12);
        string normalized = NormalizePath(path);
        string json = JsonSerializer.Serialize(new[]
        {
            time.ToString(CultureInfo.InvariantCulture), nonce, normalized
        });
        byte[] plain = Encoding.UTF8.GetBytes(json);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(iv, plain, cipher, tag, Encoding.UTF8.GetBytes("heybox-rnd:v16"));

        byte[] output = new byte[iv.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(iv, 0, output, 0, iv.Length);
        Buffer.BlockCopy(cipher, 0, output, iv.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, output, iv.Length + cipher.Length, tag.Length);
        return "16:" + Convert.ToBase64String(output).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static string NormalizePath(string path)
    {
        int question = path.IndexOf('?', StringComparison.Ordinal);
        if (question >= 0) path = path[..question];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "/" + string.Join('/', parts) + "/";
    }

    private static string Map(string text, string table, int keep = 0)
    {
        if (text.Length == 0) return "";
        if (table.Length == 0) return text;
        var builder = new StringBuilder(text.Length);
        foreach (char c in text) builder.Append(table[c % table.Length]);
        return builder.ToString();
    }

    /// <summary>从映射表前面取字符；removeTail &gt; 0 时先切掉末尾 removeTail 个字符。</summary>
    private static string MapSlice(string text, string table, int removeTail)
        => Map(text, removeTail > 0 ? table[..^removeTail] : table);

    private static string Interleave(params string[] parts)
    {
        int max = parts.Max(p => p.Length);
        var builder = new StringBuilder(max * parts.Length);
        for (int i = 0; i < max; i++)
            foreach (string part in parts)
                if (i < part.Length) builder.Append(part[i]);
        return builder.ToString();
    }

    private static byte S3(byte e) => (e & 0x80) != 0 ? (byte)(((e << 1) ^ 0x1B) & 0xFF) : (byte)((e << 1) & 0xFF);
    private static byte Pc(byte e) => (byte)(S3(e) ^ e);
    private static byte Sf(byte e) => Pc(S3(e));
    private static byte Bh(byte e) => Sf(Pc(S3(e)));
    private static byte Lg(byte e) => (byte)(Bh(e) ^ Sf(e) ^ Pc(e));

    /// <summary>4x4 MDS 混合。必须原地改写前 4 个元素，调用方会对整个数组求和。</summary>
    private static List<int> Mix(List<int> codes)
    {
        int a = (byte)codes[0], b = (byte)codes[1], c = (byte)codes[2], d = (byte)codes[3];
        codes[0] = Lg((byte)a) ^ Bh((byte)b) ^ Sf((byte)c) ^ Pc((byte)d);
        codes[1] = Pc((byte)a) ^ Lg((byte)b) ^ Bh((byte)c) ^ Sf((byte)d);
        codes[2] = Sf((byte)a) ^ Pc((byte)b) ^ Lg((byte)c) ^ Bh((byte)d);
        codes[3] = Bh((byte)a) ^ Sf((byte)b) ^ Pc((byte)c) ^ Lg((byte)d);
        return codes;
    }

    internal static bool SelfTest()
    {
        (string Path, long Time, string Nonce, string Expect)[] samples =
        [
            ("/bbs/app/feeds", 1793159576, "E228D657D7AE3182EFDA4283E2C32F06", "VS2S006"),
            ("/bbs/app/feeds/banner", 1793159576, "B358F4066EC902AACCE68BBCA66EDDE2", "D7PV763"),
            ("/account/restore_login", 1793159576, "22BF3B0F660816C5929439F2E9D80299", "YT0TV70"),
            ("/bbs/app/topic/categories", 1793159576, "D78865C774303864E97ECB6119BC30C4", "VUP1022"),
            ("/bbs/app/api/search/found", 1793159576, "4399A965B2891DC60660993ADF5F3C44", "1371749"),
            ("/bbs/app/api/search/welcome_page/v2", 1793159576, "DAFC50032B4C5B13E91EB517AE9EE717", "U00UU96")
        ];
        return samples.All(s => Hkey(s.Path, s.Time, s.Nonce) == s.Expect);
    }
}
