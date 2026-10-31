using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TubaWinUi3.Services.Ai;

/// <summary>Per-Windows-user DPAPI protection; legacy plaintext is read and migrated on save.</summary>
public sealed class ProtectedApiKeyConverter : JsonConverter<string>
{
    private const string Prefix = "dpapi:v1:";
    public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => Decode(reader.GetString() ?? "");

    internal static string Decode(string value)
    {
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        try { return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value[Prefix.Length..]), false)); }
        catch (Exception ex) { throw new JsonException("无法解密 AI 密钥，请在原 Windows 用户下恢复配置，或重新填写密钥。", ex); }
    }
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Encode(value));

    internal static string Encode(string value) => string.IsNullOrEmpty(value) ? "" :
        Prefix + Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (var i = 0; i < input.Size; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
