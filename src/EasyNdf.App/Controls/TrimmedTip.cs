using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// Any text the app cuts short with "…" shows itself in full as a tooltip while the mouse is over it, everywhere at
/// once: a text block that trims, has no tooltip of its own and does not fit is given its whole text as its tooltip when
/// the mouse comes over it, and loses it again when it fits. Without it, a long mod, rule or file name cut short could
/// not be read anywhere.
/// </summary>
public static class TrimmedTip
{
    /// <summary>
    /// The tooltip this class gave, so it is taken away when the text fits again; one other code set since (a reused row's
    /// own tooltip) is not the same object, so it is never touched.
    /// </summary>
    private static readonly DependencyProperty GivenProperty =
        DependencyProperty.RegisterAttached("Given", typeof(string), typeof(TrimmedTip), new PropertyMetadata(null));

    /// <summary>Starts watching every text block of the app; called once at start-up.</summary>
    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(TextBlock), UIElement.MouseEnterEvent, new MouseEventHandler((sender, _) => Check((TextBlock)sender)));

    /// <summary>The mouse came over a text block: it gets its whole text as its tooltip if it is cut short, and loses it if not.</summary>
    private static void Check(TextBlock text)
    {
        bool given = text.GetValue(GivenProperty) is string tip && ReferenceEquals(text.ToolTip, tip);
        if (!given) text.ClearValue(GivenProperty);
        if (text.ToolTip is not null && !given) return;
        bool cut = text.TextTrimming != TextTrimming.None && text.Text.Length > 0 && IsCut(text);
        if (cut)
        {
            // a copy, so it is told apart from the same words set as a tooltip by other code
            string whole = new(text.Text.AsSpan());
            text.ToolTip = whole;
            text.SetValue(GivenProperty, whole);
        }
        else if (given)
        {
            text.ClearValue(FrameworkElement.ToolTipProperty);
            text.ClearValue(GivenProperty);
        }
    }

    /// <summary>Whether the whole text needs more room than the block has: wider on one line, or taller when it wraps.</summary>
    private static bool IsCut(TextBlock text)
    {
        double width = text.ActualWidth - text.Padding.Left - text.Padding.Right;
        if (width <= 0) return true;
        var whole = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(text).PixelsPerDip);
        if (text.TextWrapping == TextWrapping.NoWrap) return whole.WidthIncludingTrailingWhitespace > width + 0.5;
        whole.MaxTextWidth = width;
        return whole.Height > text.ActualHeight - text.Padding.Top - text.Padding.Bottom + 0.5;
    }
}
