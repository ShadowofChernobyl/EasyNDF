using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// A mouse, drawn as an outline with its left button filled in, for the Help pages' keyboard shortcuts: a click, or with
/// <see cref="IsDouble"/> a double-click, marked with a small "×2" at its top right like an exponent. It is drawn
/// rather than written, as the mouse character only some Windows fonts have would look different from PC to PC.
/// </summary>
public sealed class MouseGlyph : FrameworkElement
{
    /// <summary>The colour the mouse is drawn in.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(MouseGlyph), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    /// <inheritdoc cref="ForegroundProperty"/>
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>A double-click: the mouse with "×2" at its top right.</summary>
    public bool IsDouble { get; init; }

    /// <summary>The mouse's outline is 9 by 13 pixels, about a capital letter's height in the key font.</summary>
    private const double BodyWidth = 9, BodyHeight = 13;

    /// <summary>The "×2" beside a double-click's mouse.</summary>
    private FormattedText? times;

    protected override Size MeasureOverride(Size constraint)
    {
        times = IsDouble ? TimesText() : null;
        return new Size(BodyWidth + 1 + (times is null ? 0 : times.WidthIncludingTrailingWhitespace + 1), BodyHeight + 1);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        // the outline on whole pixels, its 1-pixel lines half a pixel in
        double left = 0.5, top = Math.Round((ActualHeight - BodyHeight) / 2) + 0.5;
        var pen = new Pen(Foreground, 1);
        var body = new Rect(left, top, BodyWidth - 1, BodyHeight - 1);
        drawing.DrawRoundedRectangle(null, pen, body, 4, 4);
        // the buttons: a line across the top third, split down the middle; the left one, which is clicked, filled in
        double split = top + 5, middle = left + (BodyWidth - 1) / 2;
        drawing.DrawLine(pen, new Point(left, split), new Point(left + BodyWidth - 1, split));
        drawing.DrawLine(pen, new Point(middle, top), new Point(middle, split));
        var leftButton = new StreamGeometry();
        using (StreamGeometryContext path = leftButton.Open())
        {
            path.BeginFigure(new Point(left + 0.5, split - 0.5), isFilled: true, isClosed: true);
            path.LineTo(new Point(left + 0.5, top + 3.5), false, false);
            path.QuadraticBezierTo(new Point(left + 0.5, top + 0.5), new Point(middle - 0.5, top + 0.5), false, false);
            path.LineTo(new Point(middle - 0.5, split - 0.5), false, false);
        }
        leftButton.Freeze();
        drawing.DrawGeometry(Foreground, null, leftButton);
        if (times is not null) drawing.DrawText(times, new Point(BodyWidth + 1, top - 3));
    }

    /// <summary>"×2" in a small size of the interface font, in the mouse's colour.</summary>
    private FormattedText TimesText()
    {
        var font = (FontFamily)(TryFindResource("ui-font") ?? new FontFamily("Segoe UI"));
        return new FormattedText("×2", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 8.5, Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
