using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Tubatools.AiXamlCard;

public enum CardGuardStatus { Ok, Incomplete, Invalid }

public sealed record CardGuardResult(CardGuardStatus Status, string Error)
{
    public bool Ok => Status == CardGuardStatus.Ok;
    public static CardGuardResult Success => new(CardGuardStatus.Ok, "");
    public static CardGuardResult Incomplete => new(CardGuardStatus.Incomplete, "");
    public static CardGuardResult Invalid(string error) => new(CardGuardStatus.Invalid, error);
}

/// <summary>
/// 卡片 XAML 的白名单校验。XAML 不是沙箱：加载后的界面对象在进程内以完全权限运行，
/// 因此唯一的防线是把不在清单里的元素与属性全部拒绝。编译型代码后置另由
/// <see cref="CardCompiler"/> 的引用白名单约束。
/// </summary>
public static partial class CardGuard
{
    public const int MaxCharacters = 192 * 1024;
    public const int MaxElements = 900;
    public const int MaxDepth = 40;
    public const string AssetsPrefix = "assets/";

    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>允许的元素。刻意不含 WebView2、MediaPlayerElement、Frame 等可加载外部内容的类型。</summary>
    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    {
        "Grid", "StackPanel", "Border", "ScrollViewer", "RelativePanel", "Canvas", "Viewbox", "Expander",
        "TextBlock", "Button", "ToggleButton", "CheckBox", "RadioButton", "ToggleSwitch", "TextBox", "NumberBox",
        "Slider", "ComboBox", "ComboBoxItem", "ProgressBar", "ProgressRing", "InfoBar", "FontIcon", "Image",
        "Rectangle", "Ellipse", "Line", "Path", "Polygon", "Polyline",
        "SolidColorBrush", "LinearGradientBrush", "GradientStop",
        "RotateTransform", "ScaleTransform", "TranslateTransform",
        "RowDefinition", "ColumnDefinition"
    };

    private static readonly HashSet<string> ValueProperties = new(StringComparer.Ordinal)
    {
        "Background", "Foreground", "BorderBrush", "Fill", "Stroke", "Content", "Child", "Children",
        "Header", "RowDefinitions", "ColumnDefinitions", "Source", "Data"
    };

    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
        "Margin", "Padding", "Spacing", "ColumnSpacing", "RowSpacing",
        "HorizontalAlignment", "VerticalAlignment", "HorizontalContentAlignment", "VerticalContentAlignment",
        "Opacity", "Visibility", "CornerRadius", "BorderThickness",
        "Background", "Foreground", "BorderBrush", "Fill", "Stroke",
        "FontSize", "FontWeight", "TextAlignment", "TextWrapping", "TextTrimming", "LineHeight",
        "CharacterSpacing", "MaxLines", "IsTextSelectionEnabled",
        "Text", "PlaceholderText", "IsReadOnly", "AcceptsReturn", "Header", "Content",
        "IsEnabled", "IsChecked", "IsSelected", "IsOn", "OnContent", "OffContent", "GroupName",
        "Value", "Minimum", "Maximum", "StepFrequency", "TickFrequency", "SmallChange", "LargeChange",
        "SpinButtonPlacementMode", "SelectedIndex", "IsIndeterminate", "ShowError", "ShowPaused",
        "IsActive", "IsOpen", "IsClosable", "Severity", "Title", "Message",
        "Glyph", "Source", "Stretch", "Data", "Points", "StrokeThickness",
        "StrokeStartLineCap", "StrokeEndLineCap", "StrokeLineJoin", "StrokeDashArray", "StrokeDashCap",
        "RadiusX", "RadiusY", "Orientation", "IsExpanded",
        "VerticalScrollBarVisibility", "HorizontalScrollBarVisibility",
        "VerticalScrollMode", "HorizontalScrollMode", "ZoomMode",
        "Color", "Offset", "StartPoint", "EndPoint",
        "Angle", "ScaleX", "ScaleY", "CenterX", "CenterY", "X", "Y",
        "Tag", "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan",
        "Canvas.Left", "Canvas.Top", "Canvas.ZIndex",
        "AutomationProperties.Name", "ToolTipService.ToolTip"
    };

    private static readonly HashSet<string> NumericAttributes = new(StringComparer.Ordinal)
    {
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "FontSize",
        "Opacity", "Spacing", "ColumnSpacing", "RowSpacing"
    };

    public static CardGuardResult Validate(string? xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml)) return CardGuardResult.Incomplete;
        if (xaml.Length > MaxCharacters) return CardGuardResult.Invalid($"卡片超过 {MaxCharacters / 1024} KB 上限。");

        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xaml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxCharacters,
                MaxCharactersFromEntities = 0,
                IgnoreWhitespace = true,
                IgnoreComments = true
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            // 正在写入或确有语法错误：由调用方区分「还不完整」与「确实坏了」。
            return CardGuardResult.Incomplete;
        }

        if (document.Root is null) return CardGuardResult.Incomplete;
        var count = 0;
        var problem = Visit(document.Root, null, 1, ref count);
        return problem is null ? CardGuardResult.Success : CardGuardResult.Invalid(problem);
    }

    private static string? Visit(XElement element, XElement? parent, int depth, ref int count)
    {
        if (depth > MaxDepth) return $"界面层级超过 {MaxDepth} 层。";
        if (++count > MaxElements) return $"界面元素超过 {MaxElements} 个。";

        var name = element.Name;
        if (name.NamespaceName != Presentation)
            return $"不支持的命名空间：{name.NamespaceName}。根元素需要 xmlns=\"{Presentation}\"。";

        var local = name.LocalName;
        var isValueProperty = false;
        if (local.Contains('.'))
        {
            var dot = local.IndexOf('.');
            var owner = local[..dot];
            var property = local[(dot + 1)..];
            if (!Elements.Contains(owner) || !ValueProperties.Contains(property))
                return $"不支持的属性元素：{local}。";
            if (parent is null || parent.Name.LocalName != owner)
                return $"属性元素 {local} 必须写在 <{owner}> 里。";
            isValueProperty = true;
        }
        else if (!Elements.Contains(local))
        {
            return $"不支持的元素：{local}。";
        }

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;
            var attributeName = attribute.Name;
            if (attributeName.NamespaceName.Length > 0)
            {
                if (attributeName.NamespaceName != XamlNs || attributeName.LocalName != "Name")
                    return $"不支持的属性：{attributeName.LocalName}。";
                continue;
            }

            var key = attributeName.LocalName;
            if (isValueProperty) return $"属性元素 {local} 上不能再写属性 {key}。";
            if (!Attributes.Contains(key))
            {
                if (key is "Click" or "Tapped" or "Loaded" or "Checked" or "SelectionChanged" or "TextChanged")
                    return "事件不写在 XAML 里：用 x:Name 命名控件，在代码后置里订阅。";
                return $"不支持的属性：{local}.{key}。";
            }
            if (key == "Source" && !IsAllowedSource(attribute.Value))
                return $"图片来源只能是 assets/ 下的图片，不能用 {Trim(attribute.Value)}。";
            if (NumericAttributes.Contains(key) && !IsNumberOrAuto(attribute.Value))
                return $"{local}.{key} 只能写数字或 Auto：{Trim(attribute.Value)}。";
        }

        foreach (var child in element.Elements())
        {
            var problem = Visit(child, element, depth + 1, ref count);
            if (problem is not null) return problem;
        }
        return null;
    }

    private static string Trim(string value) => value.Length <= 40 ? value : value[..40] + "…";

    private static bool IsNumberOrAuto(string value)
    {
        var text = value.Trim();
        if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return true;
        // GridLength：N* 与 * 是 Grid 列宽最常用的写法，卡片的布局几乎都会用到。
        if (text == "*") return true;
        if (text.EndsWith('*') && double.TryParse(text[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var stars) && double.IsFinite(stars) && stars >= 0)
            return true;
        return text.Length is > 0 and <= 16 &&
               double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) &&
               double.IsFinite(number);
    }

    /// <summary>卡片离线渲染：只能引用本会话 assets/ 里的图片，不接受任何路径或 URI。</summary>
    public static bool IsAllowedSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (!text.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        return AssetName().IsMatch(text[AssetsPrefix.Length..]);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,60}\.(?:png|jpg|jpeg|bmp|gif)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssetName();
}
