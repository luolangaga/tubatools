using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tubatools.MinecraftLauncher.Services;

/// <summary>按 Windows 用户用 DPAPI 保护微软令牌，避免 accounts.json 里出现明文刷新令牌。</summary>
internal sealed class McSecretConverter : JsonConverter<string>
{
    private const string Prefix = "dpapi:v1:";

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value)) return null;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        try
        {
            return Encoding.UTF8.GetString(McDpapi.Transform(Convert.FromBase64String(value[Prefix.Length..]), false));
        }
        catch
        {
            // 换 Windows 用户或令牌损坏时要求重新登录，而不是让整个账户文件读取失败。
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.WriteStringValue("");
            return;
        }

        try
        {
            writer.WriteStringValue(Prefix + Convert.ToBase64String(
                McDpapi.Transform(Encoding.UTF8.GetBytes(value), true)));
        }
        catch
        {
            writer.WriteStringValue("");
        }
    }
}

internal static class McDpapi
{
    public static byte[] Transform(byte[] bytes, bool protect)
    {
        try
        {
            return protect
                ? ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)
                : ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
