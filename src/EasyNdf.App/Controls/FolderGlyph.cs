using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// The Group rule's symbol: the outline of a manila folder, a tab on its top left and the front flap's edge across it,
/// drawn in a theme colour as the other rule types' symbols are. It is drawn rather than written, as no font the app
/// can count on has a folder outline. <see cref="For"/> gives the folder for the Group symbol and text for the others.
/// </summary>
public sealed class FolderGlyph : FrameworkElement
{
    /// <summary>The colour the outline is drawn in.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(FolderGlyph), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    /// <inheritdoc cref="ForegroundProperty"/>
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>The outline is 13 by 11 pixels, the size of the other symbols at 13 pixels.</summary>
    private const double OutlineWidth = 13, OutlineHeight = 11;

    /// <summary>The symbol for a rule type: the folder for Group's (<see cref="Rules.RuleRow.FolderSymbol"/>), otherwise its text.</summary>
    /// <param name="symbol">The rule type's symbol.</param>
    /// <param name="colour">The theme colour it is drawn in.</param>
    /// <param name="width">How wide it is: the symbols sit in a column of their own.</param>
    public static FrameworkElement For(string symbol, string colour, double width)
    {
        FrameworkElement shown = symbol == Rules.RuleRow.FolderSymbol
            ? new FolderGlyph { Width = width, Height = 17 }
            : new TextBlock { Text = symbol, Width = width, TextAlignment = TextAlignment.Center, FontSize = 13 };
        shown.SetResourceReference(shown is TextBlock ? TextBlock.ForegroundProperty : ForegroundProperty, colour);
        return shown;
    }

    protected override Size MeasureOverride(Size constraint) => new(OutlineWidth + 1, OutlineHeight + 1);

    protected override void OnRender(DrawingContext drawing)
    {
        // the outline in the middle, its 1-pixel lines on whole pixels
        double left = Math.Round((ActualWidth - OutlineWidth) / 2) + 0.5, top = Math.Round((ActualHeight - OutlineHeight) / 2) + 0.5;
        double right = left + OutlineWidth - 1, bottom = top + OutlineHeight - 1;
        var outline = new StreamGeometry();
        using (StreamGeometryContext path = outline.Open())
        {
            // the tab, then the top of the folder, its right side, bottom and left side
            path.BeginFigure(new Point(left, bottom), isFilled: false, isClosed: true);
            path.LineTo(new Point(left, top), true, false);
            path.LineTo(new Point(left + 4, top), true, false);
            path.LineTo(new Point(left + 6, top + 2), true, false);
            path.LineTo(new Point(right, top + 2), true, false);
            path.LineTo(new Point(right, bottom), true, false);
            // the front flap's top edge
            path.BeginFigure(new Point(left, top + 4), isFilled: false, isClosed: false);
            path.LineTo(new Point(right, top + 4), true, false);
        }
        outline.Freeze();
        drawing.DrawGeometry(null, new Pen(Foreground, 1), outline);
    }
}
