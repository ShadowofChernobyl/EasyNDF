using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using EasyNdf.App.Controls;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// Draws a folded block as the HTML app does (its <c>.edfold</c>): its first line, then, 6 pixels on, "⋯" in the dim
/// colour in a small box with a border in the line colour. Clicking the box unfolds the block. It takes the place of
/// AvalonEdit's own folded-block marker, whose look cannot be set.
/// </summary>
public sealed class FoldedBlocks : VisualLineElementGenerator
{
    /// <summary>The folded blocks of the text shown; null while it cannot fold ("Changes only").</summary>
    public FoldingManager? Folding { get; set; }

    /// <summary>The next place, from <paramref name="startOffset"/>, where a folded block starts.</summary>
    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (Folding is null) return -1;
        // a block folded over this place, which the line before ended inside
        if (Folding.GetFoldingsContaining(startOffset).Any(f => f.IsFolded && f.StartOffset < startOffset && f.EndOffset > startOffset)) return startOffset;
        return Folding.GetNextFoldedFoldingStart(startOffset);
    }

    /// <summary>The box, standing for all of the folded text from here, including blocks folded inside it or overlapping its end.</summary>
    public override VisualLineElement? ConstructElement(int offset)
    {
        if (Folding is null) return null;
        FoldingSection? section = null;
        int foldedUntil = -1;
        foreach (FoldingSection fold in Folding.GetFoldingsContaining(offset))
        {
            if (fold.IsFolded && fold.EndOffset > foldedUntil)
            {
                foldedUntil = fold.EndOffset;
                section = fold;
            }
        }
        if (section is null || foldedUntil <= offset) return null;
        bool extended;
        do
        {
            extended = false;
            foreach (FoldingSection fold in Folding.GetFoldingsContaining(foldedUntil))
            {
                if (!fold.IsFolded || fold.EndOffset <= foldedUntil) continue;
                foldedUntil = fold.EndOffset;
                extended = true;
            }
        } while (extended);
        return new FoldedElement(section, foldedUntil - offset);
    }

    /// <summary>The folded text as one piece of the line: the box, which unfolds the block when clicked.</summary>
    public sealed class FoldedElement(FoldingSection section, int documentLength) : VisualLineElement(1, documentLength)
    {
        /// <summary>The block it stands for.</summary>
        public FoldingSection Section => section;

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) => new Box(TextRunProperties);

        protected override void OnQueryCursor(QueryCursorEventArgs e)
        {
            e.Cursor = Cursors.Hand;
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            section.IsFolded = false;
            e.Handled = true;
        }
    }

    /// <summary>
    /// The box as the text formatter sees it: 6 pixels of space, then a border round "⋯" with 5 pixels each side,
    /// as tall as the font, on the line's baseline.
    /// </summary>
    private sealed class Box(TextRunProperties properties) : TextEmbeddedObject
    {
        private const double Space = 6, Padding = 5, Border = 1;

        private FormattedText Dots() => new("⋯", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, properties.Typeface,
            properties.FontRenderingEmSize, (Brush)Application.Current.Resources["muted"], 1);

        /// <summary>The font's height above and below the baseline, as a browser draws an inline box's background.</summary>
        private (double Above, double Below) FontHeight()
        {
            FontFamily family = properties.Typeface.FontFamily;
            double size = properties.FontRenderingEmSize;
            return (family.Baseline * size, (family.LineSpacing - family.Baseline) * size);
        }

        private double Width => Space + 2 * (Border + Padding) + Dots().WidthIncludingTrailingWhitespace;

        public override LineBreakCondition BreakBefore => LineBreakCondition.BreakPossible;
        public override LineBreakCondition BreakAfter => LineBreakCondition.BreakPossible;
        public override bool HasFixedSize => true;
        public override CharacterBufferReference CharacterBufferReference => new("⋯", 0);
        public override int Length => 1;
        public override TextRunProperties Properties => properties;

        public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth)
        {
            (double above, double below) = FontHeight();
            return new TextEmbeddedObjectMetrics(Width, above + below + 2 * Border, above + Border);
        }

        public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways)
        {
            (double above, double below) = FontHeight();
            return new Rect(0, -above - Border, Width, above + below + 2 * Border);
        }

        /// <summary>Drawn with its left end at <paramref name="origin"/>, on the baseline.</summary>
        public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
        {
            (double above, double below) = FontHeight();
            var box = new Rect(origin.X + Space, origin.Y - above - Border, Width - Space, above + below + 2 * Border);
            box = new Rect(Math.Round(box.X), Math.Round(box.Y), Math.Round(box.Width), Math.Round(box.Height));
            drawingContext.DrawRectangle((Brush)Application.Current.Resources["line"], null, box);
            drawingContext.DrawRectangle((Brush)Application.Current.Resources["panel"], null, new Rect(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2));
            FormattedText dots = Dots();
            drawingContext.DrawText(dots, new Point(origin.X + Space + Border + Padding, origin.Y - dots.Baseline));
        }
    }
}
