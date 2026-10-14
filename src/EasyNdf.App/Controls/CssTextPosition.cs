using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// Puts text where a web page puts it inside a line taller than the font. A web page shares the extra height equally
/// above and below the font (Chrome rounds the font's ascent and descent to whole pixels, and the half above down to a
/// whole pixel); WPF's "block" line height shares
/// it in proportion to the font's exact ascent and descent. For Tahoma at 12.5 pixels in an 18.125-pixel line, WPF's
/// text is 1 pixel lower. Every text block is moved up by the difference, worked out from its own font, without
/// changing the layout.
/// </summary>
public static class CssTextPosition
{
    /// <summary>Starts moving every text block of the app; called once at start-up.</summary>
    public static void Register()
    {
        // a text block's size is known once it is laid out
        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler((sender, _) => Place((TextBlock)sender)));
        EventManager.RegisterClassHandler(typeof(TextBox), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler((sender, _) => Place((TextBox)sender)));
    }

    /// <summary>A text box's text is moved the same way: its scrolling part, which holds the text and the caret, moves up.</summary>
    private static void Place(TextBox box)
    {
        if (box.Template?.FindName("PART_ContentHost", box) is not FrameworkElement host) return;
        double shift = Shift(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch, box.FontSize,
            (double)box.GetValue(TextBlock.LineHeightProperty), (LineStackingStrategy)box.GetValue(TextBlock.LineStackingStrategyProperty));
        host.RenderTransform = shift == 0 ? Transform.Identity : new TranslateTransform(0, -shift);
    }

    private static void Place(TextBlock text)
    {
        double shift = Shift(text);
        // A text block is resized on every step of a pane being dragged, and its font stays the same: its transform is
        // only replaced when the shift changes, so a drag makes no new objects here and redraws nothing for it.
        if (shift == 0)
        {
            if (text.RenderTransform is TranslateTransform) text.RenderTransform = Transform.Identity;
            return;
        }
        if (text.RenderTransform is TranslateTransform { X: 0 } moved && moved.Y == -shift) return;
        text.RenderTransform = new TranslateTransform(0, -shift);
    }

    /// <summary>The shift for each font, size and line worked out so far: working it out reads the font file's measurements.</summary>
    private static readonly Dictionary<(FontFamily, FontStyle, FontWeight, FontStretch, double, double, LineStackingStrategy), double> shifts = [];

    /// <summary>How much lower WPF draws the text than a web page would, in pixels.</summary>
    public static double Shift(TextBlock text) =>
        Shift(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch, text.FontSize, text.LineHeight, text.LineStackingStrategy);

    /// <summary>How much lower WPF draws text of a font in a line than a web page would, in pixels.</summary>
    public static double Shift(FontFamily family, FontStyle style, FontWeight weight, FontStretch stretch, double fontSize,
        double lineHeight, LineStackingStrategy strategy)
    {
        if (double.IsNaN(lineHeight) || strategy != LineStackingStrategy.BlockLineHeight) return 0;
        var key = (family, style, weight, stretch, fontSize, lineHeight, strategy);
        if (shifts.TryGetValue(key, out double known)) return known;
        double shift = WorkOutShift(family, style, weight, stretch, fontSize, lineHeight);
        shifts[key] = shift;
        return shift;
    }

    private static double WorkOutShift(FontFamily family, FontStyle style, FontWeight weight, FontStretch stretch, double fontSize, double lineHeight)
    {
        var typeface = new Typeface(family, style, weight, stretch);
        if (!typeface.TryGetGlyphTypeface(out GlyphTypeface font) || font.Height <= 0) return 0;
        double wpfBaseline = lineHeight * font.Baseline / font.Height;
        return wpfBaseline - BrowserBaseline(font, fontSize, lineHeight);
    }

    /// <summary>
    /// Where a web page puts the baseline of text of a font in a line of a given height, from the line's top: Chrome
    /// rounds the font's ascent and descent to whole pixels, and adds half the spare height above, rounded down.
    /// </summary>
    public static double BrowserBaseline(Typeface typeface, double fontSize, double lineHeight) =>
        typeface.TryGetGlyphTypeface(out GlyphTypeface font) && font.Height > 0 ? BrowserBaseline(font, fontSize, lineHeight) : lineHeight * 0.8;

    private static double BrowserBaseline(GlyphTypeface font, double fontSize, double lineHeight)
    {
        double ascent = font.Baseline * fontSize, height = font.Height * fontSize;
        double browserAscent = Math.Round(ascent), browserDescent = Math.Round(height - ascent);
        return browserAscent + Math.Floor((lineHeight - browserAscent - browserDescent) / 2);
    }
}
