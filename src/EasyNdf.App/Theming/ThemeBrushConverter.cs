using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace EasyNdf.App.Theming;

/// <summary>Turns a theme colour's name, such as "accent" or "s-func", into the theme's brush for it.</summary>
public sealed class ThemeBrushConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key && Application.Current.TryFindResource(key) is Brush brush ? brush : Brushes.Transparent;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
