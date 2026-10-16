using System.Globalization;
using System.Windows.Data;

namespace EasyNdf.App.Editor;

/// <summary>
/// The most width a property's key takes in the property list: 55% of the list's width, and at least 40 pixels (the
/// HTML app's <c>.dvk</c>). The rows bind to the list's width through it, so when the window or the panes are resized
/// each row shown follows at once, and the list is not built again.
/// </summary>
public sealed class KeyWidth : IValueConverter
{
    /// <summary>From the list's width to the key's most width.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double listWidth ? Math.Max(40, 0.55 * Math.Max(0, listWidth - 4)) : 40.0;

    /// <summary>Not used: the width only goes one way.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
