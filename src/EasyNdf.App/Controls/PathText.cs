using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// A file's path on one line that keeps its end in view: when it does not fit, its start gives way to "…"
/// ("…\Abrams_Custom\Abrams_Custom.fbx"), since a file's name says more than the drive it is on. WPF's own trimming
/// only cuts a text's end. The whole path is its tooltip.
/// </summary>
public sealed class PathText : Decorator
{
    /// <summary>The whole path.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PathText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((PathText)d).TextChanged()));

    private const string Ellipsis = "…";

    public PathText()
    {
        Shown.TextTrimming = TextTrimming.None;
        Shown.TextWrapping = TextWrapping.NoWrap;
        Child = Shown;
    }

    /// <summary>The text block that draws the path: its font and colour are the path's.</summary>
    public TextBlock Shown { get; } = new();

    /// <inheritdoc cref="TextProperty"/>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private void TextChanged()
    {
        Shown.Text = Text;
        ToolTip = string.IsNullOrEmpty(Text) ? null : Text;
    }

    /// <summary>
    /// Asks for no width of its own, so the space it is given decides how much of the path shows. The text shown is
    /// left as it is here: set whole here and cut in <see cref="ArrangeOverride"/>, each change asked for the other
    /// again, and a path too long for its box kept the window laying itself out for ever (the panel stopped updating).
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        Shown.Measure(new Size(double.PositiveInfinity, constraint.Height));
        return new Size(0, Shown.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        string fitted = Fitted(arrangeSize.Width);
        // set only when it changes, so arranging again with the same width asks for no new layout
        if (Shown.Text != fitted) Shown.Text = fitted;
        Shown.Measure(new Size(double.PositiveInfinity, arrangeSize.Height));
        Shown.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }

    /// <summary>The path as it fits a width: whole, or "…" and as much of its end as fits.</summary>
    private string Fitted(double width)
    {
        string text = Text ?? "";
        if (text.Length == 0 || WidthOf(text) <= width) return text;
        int low = 0, high = text.Length;
        // the most letters of the end that fit after "…"
        while (low < high)
        {
            int keep = (low + high + 1) / 2;
            if (WidthOf(Ellipsis + text[^keep..]) <= width) low = keep;
            else high = keep - 1;
        }
        return Ellipsis + text[^low..];
    }

    private double WidthOf(string text) =>
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Shown.FontFamily, Shown.FontStyle, Shown.FontWeight, Shown.FontStretch), Shown.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
}
