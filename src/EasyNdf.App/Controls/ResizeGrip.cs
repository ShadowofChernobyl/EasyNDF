using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// The bar under a list that drags it taller or shorter (the HTML app's <c>.vgrip</c>): 9 pixels tall, with two short
/// lines in its middle, in the line colour, or the accent colour under the mouse and while dragged. Dragging it asks
/// for a new height for <see cref="Target"/>; double-clicking it asks to go back to the list's own height.
/// </summary>
public sealed class ResizeGrip : FrameworkElement
{
    private readonly PixelSnapping snapping;
    private double dragStartY, dragStartHeight;
    private bool isDragging;

    /// <summary>The box the grip resizes.</summary>
    public FrameworkElement? Target { get; set; }

    /// <summary>
    /// Raised while the grip is dragged, with the height the box should have, and on a double-click with null: the box
    /// goes back to its own height. The page applies it, as it knows the box's limits.
    /// </summary>
    public event Action<double?>? HeightChosen;

    /// <summary>Raised when a drag ends or the grip is double-clicked: the height can be kept for next time.</summary>
    public event Action? HeightSettled;

    /// <summary>Builds the grip.</summary>
    public ResizeGrip()
    {
        Height = 9;
        Cursor = System.Windows.Input.Cursors.SizeNS;
        snapping = new PixelSnapping(this);
    }

    protected override void OnMouseEnter(MouseEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeave(MouseEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            HeightChosen?.Invoke(null);
            HeightSettled?.Invoke();
            return;
        }
        if (Target is null) return;
        dragStartY = e.GetPosition(null).Y;
        dragStartHeight = Target.ActualHeight;
        isDragging = true;
        PixelSnapping.PauseForDrag();
        CaptureMouse(); // the drag carries on when the pointer leaves the window
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!isDragging) return;
        HeightChosen?.Invoke(Math.Round(dragStartHeight + e.GetPosition(null).Y - dragStartY, MidpointRounding.AwayFromZero));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (isDragging) HeightSettled?.Invoke();
        if (isDragging) PixelSnapping.ResumeAfterDrag();
        isDragging = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        // a transparent background, so the whole bar can be grabbed
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var brush = (Brush)FindResource(IsMouseOver || isDragging ? "accent" : "line");
        double left = (ActualWidth - 28) / 2;
        drawing.DrawRectangle(brush, null, snapping.Snap(left, 3, 28, 1));
        drawing.DrawRectangle(brush, null, snapping.Snap(left, 6, 28, 1));
    }
}
