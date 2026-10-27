using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>
/// 房间码，形如 <c>U/XXXX-XXXX-XXXX-XXXX</c>。
/// </summary>
/// <remarks>
/// 16 个 base-34 字符是一个 7 的倍数的数（低位在前）。前 8 位是虚拟网络名，后 8 位是网络密钥——
/// 所以光凭房间码就能入网，不需要任何账号或服务器。7 的倍数规则能挡掉绝大多数手抄错误。
/// </remarks>
public sealed class TunnelRoomCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string NetworkPrefix = "tuba-";
    private const int DigitCount = 16;
    private const int Checksum = 7;

    /// <summary>"U/XXXX-XXXX-XXXX-XXXX" 的长度。</summary>
    private const int TextLength = 21;

    private static readonly BigInteger Range = BigInteger.Pow(Alphabet.Length, DigitCount);

    private TunnelRoomCode(BigInteger value)
    {
        var digits = new char[DigitCount];
        for (var i = 0; i < DigitCount; i++)
        {
            digits[i] = Alphabet[(int)(value % Alphabet.Length)];
            value /= Alphabet.Length;
        }

        var code = new StringBuilder("U/", TextLength);
        for (var i = 0; i < DigitCount; i++)
        {
            if (i > 0 && i % 4 == 0) code.Append('-');
            code.Append(digits[i]);
        }

        Code = code.ToString();
        NetworkName = NetworkPrefix + Group(digits, 0) + "-" + Group(digits, 4);
        NetworkSecret = Group(digits, 8) + "-" + Group(digits, 12);
    }

    /// <summary>给玩家看的房间码，例如 <c>U/1A2B-3C4D-5E6F-7G8H</c>。</summary>
    public string Code { get; }

    public string NetworkName { get; }

    public string NetworkSecret { get; }

    public override string ToString() => Code;

    /// <summary>为新房间生成随机房间码。</summary>
    public static TunnelRoomCode Create()
    {
        // 多一个 0 字节，保证按小端解释时是正数。
        var bytes = new byte[17];
        using (var random = RandomNumberGenerator.Create())
            random.GetBytes(bytes, 0, 16);

        var value = new BigInteger(bytes) % Range;
        return new TunnelRoomCode(value - value % Checksum);
    }

    /// <summary>
    /// 在 <paramref name="text"/> 里找第一个合法房间码（可以是一整段聊天消息）。
    /// 大小写不敏感，形近的 I / O 分别按 1 / 0 读。
    /// </summary>
    public static TunnelRoomCode? Find(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var upper = text.ToUpperInvariant();
        for (var start = 0; start + TextLength <= upper.Length; start++)
        {
            if (upper[start] == 'U' && upper[start + 1] == '/' && TryDecode(upper, start + 2, out var value))
                return new TunnelRoomCode(value);
        }
        return null;
    }

    /// <summary>解析整段文本是否恰好是一个房间码。</summary>
    public static TunnelRoomCode? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        if (value.Length != TextLength) return null;
        var upper = value.ToUpperInvariant();
        return upper[0] == 'U' && upper[1] == '/' && TryDecode(upper, 2, out var number)
            ? new TunnelRoomCode(number)
            : null;
    }

    private static bool TryDecode(string text, int offset, out BigInteger value)
    {
        value = BigInteger.Zero;

        // 正文 "XXXX-XXXX-XXXX-XXXX" 的最右一位是最高位。
        for (var i = TextLength - 3; i >= 0; i--)
        {
            var c = text[offset + i];
            if (i == 4 || i == 9 || i == 14)
            {
                if (c != '-') return false;
                continue;
            }

            var digit = DigitOf(c);
            if (digit < 0) return false;
            value = value * Alphabet.Length + digit;
        }
        return value % Checksum == 0;
    }

    private static int DigitOf(char c)
    {
        if (c == 'I') c = '1';
        else if (c == 'O') c = '0';
        return Alphabet.IndexOf(c);
    }

    private static string Group(char[] digits, int start) => new(digits, start, 4);
}
