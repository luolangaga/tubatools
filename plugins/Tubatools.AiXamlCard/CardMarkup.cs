using System.Security;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Tubatools.AiXamlCard;

/// <summary>
/// 加载前对模型写的 XAML 做三处改写：配色令牌换成当前主题的具体颜色、assets/ 图片换成
/// 卡片目录下的绝对 URI（XamlReader 会按应用包解析相对路径）、标题从根元素取出。
/// 全部是纯字符串/XML 操作，可单独测试。
/// </summary>
public static partial class CardMarkup
{
    public const string AssetsFolder = "assets";

    public static string Rewrite(string xaml, string? cardDirectory)
    {
        if (string.IsNullOrEmpty(xaml)) return "";

        var text = TokenAttribute().Replace(xaml, match =>
        {
            var token = match.Groups["token"].Value;
            var color = CardPalette.Token(token);
            return color is null ? match.Value : $"\"{color}\"";
        });

        if (cardDirectory is null) return text;

        text = AssetSourceAttribute().Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            var folder = Path.Combine(cardDirectory, AssetsFolder);
            var path = Path.GetFullPath(Path.Combine(folder, name));
            // 只允许落在本卡片的 assets 目录内；CardGuard 已在纯文本层面限制过名字。
            if (!path.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) return match.Value;
            if (!File.Exists(path)) return match.Value;
            return "Source=\"" + SecurityElement.Escape(new Uri(path).AbsoluteUri) + "\"";
        });
        return text;
    }

    /// <summary>标题取根元素的 AutomationProperties.Name，其次 x:Name，最后用文件夹名。</summary>
    public static string TitleOf(string? xaml, string fallback)
    {
        if (string.IsNullOrWhiteSpace(xaml)) return fallback;
        try
        {
            var root = XDocument.Parse(xaml).Root;
            if (root is null) return fallback;
            var value = (root.Attribute("AutomationProperties.Name")?.Value
                         ?? root.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                         ?? "").Trim();
            if (value.Length == 0) return fallback;
            return value.Length > 40 ? value[..40] : value;
        }
        catch (XmlException) { return fallback; }
    }

    [GeneratedRegex("\"(?<token>Tuba[A-Za-z]{2,20})\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenAttribute();

    [GeneratedRegex("Source\\s*=\\s*\"assets/(?<name>[A-Za-z0-9][A-Za-z0-9._-]{0,60}\\.(?:png|jpg|jpeg|bmp|gif))\"",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AssetSourceAttribute();
}
