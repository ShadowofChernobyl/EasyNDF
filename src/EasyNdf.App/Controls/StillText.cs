using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// A line of text that does not wrap, on a card or in a pane whose width changes while a bar is dragged. WPF lays a
/// text block's line out again each time it is given another width, and again to draw it, though a line that fits
/// comes out the same at any width. Here a line that fits is measured once and placed at its own width, so changing
/// the width does nothing to it. A line that does not fit is measured at the room there is, as only that makes WPF end
/// it in "…"; during a drag, that room goes in steps of a few pixels, so its "…" is worked out now and then rather
/// than at every step, and is put right when the drag ends. Tuned for speed: Keep it that way when tidying.
/// </summary>
internal sealed class StillLine(UIElement holder)
{
    /// <summary>The steps a line that does not fit changes its width in, during a drag.</summary>
    private const double DragStep = 3;

    /// <summary>The text's own width on one line; NaN until it is known.</summary>
    private double own = double.NaN;

    /// <summary>Whether the width it stands at was rounded for a drag, to be put right when the drag ends.</summary>
    private bool roundedForDrag;

    /// <summary>The width the text was last measured to stand at.</summary>
    public double Width { get; private set; }

    /// <summary>Measures the text for the room there is.</summary>
    public void Measure(UIElement text, double room)
    {
        var anyRoom = new Size(double.PositiveInfinity, double.PositiveInfinity);
        // given new words, or with room for all it had: its own width, which WPF need not work out again if nothing changed
        if (double.IsNaN(own) || !text.IsMeasureValid || own <= room)
        {
            text.Measure(anyRoom);
            own = text.DesiredSize.Width;
        }
        if (own <= room)
        {
            Width = own;
            return;
        }
        Width = LayoutDrag.IsUnderWay ? Math.Max(0, Math.Floor(room / DragStep) * DragStep) : room;
        if (Width < room && !roundedForDrag)
        {
            roundedForDrag = true;
            LayoutDrag.Ended += PutRight;
        }
        text.Measure(new Size(Width, double.PositiveInfinity));
    }

    /// <summary>The text asked for another size by itself, as with new words: its own width is worked out again.</summary>
    public void Forget() => own = double.NaN;

    /// <summary>
    /// What a mouse anywhere in the room hits: the text, though it stands at its own width, so its tooltip and what a
    /// click on it does reach as far as when it was stretched across the room.
    /// </summary>
    public static HitTestResult HitTest(UIElement holder, UIElement text, Point point) =>
        new Rect(holder.RenderSize).Contains(point) ? new PointHitTestResult(text, point) : null!;

    private void PutRight()
    {
        LayoutDrag.Ended -= PutRight;
        roundedForDrag = false;
        holder.InvalidateMeasure();
    }
}

/// <summary>A line of text that stays still while the width around it changes, as <see cref="StillLine"/> says.</summary>
public sealed class StillText : Decorator
{
    private readonly StillLine line;

    /// <summary>Holds a line of text.</summary>
    public StillText(TextBlock text)
    {
        line = new StillLine(this);
        Child = text;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return default;
        line.Measure(Child, constraint.Width);
        return new Size(Math.Min(line.Width, constraint.Width), Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(0, 0, Math.Min(line.Width, arrangeSize.Width), arrangeSize.Height));
        return arrangeSize;
    }

    protected override void OnChildDesiredSizeChanged(UIElement child)
    {
        line.Forget();
        base.OnChildDesiredSizeChanged(child);
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        Child is null ? base.HitTestCore(hitTestParameters) : StillLine.HitTest(this, Child, hitTestParameters.HitPoint);
}
