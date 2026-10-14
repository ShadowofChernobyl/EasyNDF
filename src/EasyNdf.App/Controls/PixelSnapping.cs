using System.Windows;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// Puts drawn edges on whole pixels the way a browser does. The layout places elements at fractions of a pixel (see
/// MainView.xaml); an element that draws lines asks here where its own top left is on the window, rounds each edge's
/// position on the window to the nearest pixel (halfway goes down, as in a browser), and draws there.
/// </summary>
public sealed class PixelSnapping
{
    private readonly FrameworkElement element;

    private Point offset;

    /// <summary>Watches the element's position, and redraws it when it moves by a fraction of a pixel.</summary>
    public PixelSnapping(FrameworkElement element)
    {
        this.element = element;
        // Moving an element does not redraw it, so its rounding would go stale; this redraws it when its position changes.
        // WPF raises LayoutUpdated on every element after any layout anywhere (each frame of a scroll, for one), so this
        // is kept cheap: an element hidden in the window is skipped (drawn without a window, nothing counts as shown), and
        // one moved by whole pixels rounds the same as before.
        element.LayoutUpdated += (_, _) =>
        {
            // while a bar or grip is dragged, the panes are laid out again on every move of the mouse: checking a few
            // thousand edges each time halved the frame rate, so they are checked once, when the drag ends
            if (pausedForDrag > 0) return;
            CheckPosition();
        };
        element.IsVisibleChanged += ShownAgain;
        Remember(this);
    }

    /// <summary>Redraws the element when it has moved by a fraction of a pixel since it was last drawn.</summary>
    private void CheckPosition()
    {
        if (element.IsLoaded && !element.IsVisible) return;
        Point now = OffsetOnWindow();
        bool sameFractions = Fraction(now.X) == Fraction(offset.X) && Fraction(now.Y) == Fraction(offset.Y);
        offset = now;
        if (!sameFractions) element.InvalidateVisual();
    }

    // ---- pausing while something is dragged ----

    /// <summary>How many drags are under way (only one, unless a drag starts while another ends).</summary>
    private static int pausedForDrag;

    /// <summary>Every element snapped, to check them all when a drag ends; dropped as they are thrown away.</summary>
    private static readonly List<WeakReference<PixelSnapping>> everyOne = [];

    private static void Remember(PixelSnapping snapping)
    {
        // the list is tidied now and then, so thrown-away elements do not pile up in it
        if (everyOne.Count % 1000 == 999) everyOne.RemoveAll(reference => !reference.TryGetTarget(out _));
        everyOne.Add(new WeakReference<PixelSnapping>(snapping));
    }

    /// <summary>A drag starts (a pane's bar, a list's grip): edges are not checked until it ends.</summary>
    public static void PauseForDrag() => pausedForDrag++;

    /// <summary>The drag has ended: every edge is checked once, where it is now.</summary>
    public static void ResumeAfterDrag()
    {
        if (pausedForDrag == 0 || --pausedForDrag > 0) return;
        foreach (WeakReference<PixelSnapping> reference in everyOne)
            if (reference.TryGetTarget(out PixelSnapping? snapping)) snapping.CheckPosition();
    }

    /// <summary>An element shown again may have moved while it was hidden: it is drawn again where it is now.</summary>
    private void ShownAgain(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        offset = OffsetOnWindow();
        element.InvalidateVisual();
    }

    /// <summary>The part of a position after the whole pixels, which is all the rounding depends on.</summary>
    private static double Fraction(double position) => Math.Round(position - Math.Floor(position), 6);

    /// <summary>A rectangle in the element, with its edges moved to whole pixels on the window.</summary>
    public Rect Snap(double left, double top, double width, double height)
    {
        offset = OffsetOnWindow();
        double x1 = SnapX(left), y1 = SnapY(top), x2 = SnapX(left + width), y2 = SnapY(top + height);
        return new Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    private double SnapX(double x) => Math.Round(x + offset.X, MidpointRounding.AwayFromZero) - offset.X;

    private double SnapY(double y) => Math.Round(y + offset.Y, MidpointRounding.AwayFromZero) - offset.Y;

    /// <summary>Where the element's top left is, from the top left of the window (or of whatever it is drawn in).</summary>
    private Point OffsetOnWindow()
    {
        Visual root = element;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(element); parent is Visual visual; parent = VisualTreeHelper.GetParent(parent))
            root = visual;
        return ReferenceEquals(root, element) ? new Point() : element.TransformToAncestor(root).Transform(new Point());
    }
}
