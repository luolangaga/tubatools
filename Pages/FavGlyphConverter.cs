using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace TubaWinUi3.Pages;

public sealed class FavGlyphConverter : IValueConverter
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

public sealed class InvertBoolToVisibilityConverter : IValueConverter
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

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is null || (value is string s && string.IsNullOrEmpty(s))
            ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
