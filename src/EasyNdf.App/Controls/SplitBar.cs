using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// The bar between two boxes that shares the room between them (the HTML app's <c>#dvSplit</c>): 9 pixels across, with
/// two short lines in its middle, in the line colour, or the accent colour under the mouse and while dragged. It stands
/// upright between boxes side by side, or lies flat between boxes one above the other (<see cref="LiesFlat"/>).
/// Dragging it tells where the mouse is; double-clicking it asks to share the room evenly again.
/// </summary>
public sealed class SplitBar : FrameworkElement
{
    private readonly PixelSnapping snapping;
    private bool isDragging;

    /// <summary>Raised while the bar is dragged, with the mouse's place across what holds the bar.</summary>
    public event Action<double>? Moved;

    /// <summary>Raised on a double-click: the width is shared evenly again.</summary>
    public event Action? Reset;

    /// <summary>Raised when a drag ends: the share can be kept for next time.</summary>
    public event Action? Settled;

    /// <summary>Builds the bar, upright.</summary>
    public SplitBar()
    {
        snapping = new PixelSnapping(this);
        LiesFlat = false;
    }

    /// <summary>Whether the bar lies flat, between a box above and a box below; it then tells the mouse's height.</summary>
    public bool LiesFlat
    {
        get => liesFlat;
        set
        {
            liesFlat = value;
            Width = value ? double.NaN : 9;
            Height = value ? 9 : double.NaN;
            Cursor = value ? Cursors.SizeNS : Cursors.SizeWE;
            InvalidateVisual();
        }
    }

    private bool liesFlat;

    protected override void OnMouseEnter(MouseEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeave(MouseEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            Reset?.Invoke();
            Settled?.Invoke();
            return;
        }
        isDragging = true;
        PixelSnapping.PauseForDrag();
        CaptureMouse(); // the drag carries on when the pointer leaves the bar
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!isDragging || Parent is not IInputElement holder) return;
        Point at = e.GetPosition(holder);
        Moved?.Invoke(liesFlat ? at.Y : at.X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (isDragging) Settled?.Invoke();
        if (isDragging) PixelSnapping.ResumeAfterDrag();
        isDragging = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        // a transparent background, so the whole bar can be grabbed
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var brush = (Brush)FindResource(IsMouseOver || isDragging ? "accent" : "line");
        if (liesFlat)
        {
            double left = (ActualWidth - 28) / 2;
            drawing.DrawRectangle(brush, null, snapping.Snap(left, 3, 28, 1));
            drawing.DrawRectangle(brush, null, snapping.Snap(left, 6, 28, 1));
            return;
        }
        double top = (ActualHeight - 28) / 2;
        drawing.DrawRectangle(brush, null, snapping.Snap(3, top, 1, 28));
        drawing.DrawRectangle(brush, null, snapping.Snap(6, top, 1, 28));
    }
}
