using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace TubaWinUi3.Pages;

public sealed partial class FavGlyphConverter : IValueConverter
{
    private const string StarGlyph = "\uE735";
    private const string StarOutlineGlyph = "\uE734";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? StarGlyph : StarOutlineGlyph;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed partial class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed partial class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isNull = value is null || (value is string s && string.IsNullOrEmpty(s));
        var invert = parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return (isNull, invert) switch
        {
            (true, false) => Visibility.Collapsed,
            (true, true) => Visibility.Visible,
            (false, false) => Visibility.Visible,
            (false, true) => Visibility.Collapsed,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed partial class HasAlternatesToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class NoAlternatesToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
