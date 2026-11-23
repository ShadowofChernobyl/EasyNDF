using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// A text box of several lines that does not wrap (Styles/Controls.xaml, TextArea) shows its sideways scrollbar only
/// while it is tall enough for a line of text above it. One line tall, as a rule's Value box starts, the bar lay over
/// the text; the text still scrolls sideways with the caret, and the bar comes once the box is dragged taller or the
/// text has more lines.
/// </summary>
public static class TextAreaScroll
{
    /// <summary>The height of the app's scrollbars.</summary>
    private const double BarHeight = 14;

    /// <summary>Registered once, when the app starts: every such text box is looked at as its size changes.</summary>
    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(TextBox), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler((sender, _) => Fit((TextBox)sender)));

    /// <summary>The bar when there is room for a line above it, and none when there is not.</summary>
    private static void Fit(TextBox box)
    {
        if (!box.AcceptsReturn || box.TextWrapping != TextWrapping.NoWrap) return;
        double line = box.FontFamily.LineSpacing * box.FontSize;
        double room = box.ActualHeight - box.Padding.Top - box.Padding.Bottom - box.BorderThickness.Top - box.BorderThickness.Bottom;
        ScrollBarVisibility wanted = room >= line + BarHeight ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        if (box.HorizontalScrollBarVisibility != wanted) box.HorizontalScrollBarVisibility = wanted;
    }
}
