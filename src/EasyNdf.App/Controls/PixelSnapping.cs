using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

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
        // WPF raises LayoutUpdated on every element listening after any layout anywhere (each frame of a scroll, each
        // key typed), so this is kept cheap: only an element in a window listens (see CheckPosition), and one moved by
        // whole pixels rounds the same as before.
        layoutDone = (_, _) =>
        {
            // while a bar or grip is dragged, the panes are laid out again on every move of the mouse: checking a few
            // thousand edges each time halved the frame rate, so they are checked once, when the drag ends
            if (pausedForDrag > 0) return;
            CheckPosition();
        };
        element.Loaded += (_, _) => Listen(true);
        element.Unloaded += (_, _) => Listen(false);
        if (DrawsWithoutWindow || element.IsLoaded) Listen(true);
        element.IsVisibleChanged += ShownAgain;
        Remember(this);
    }

    /// <summary>What the element does after each layout.</summary>
    private readonly EventHandler layoutDone;

    /// <summary>Whether the element listens to layouts now.</summary>
    private bool isListening;

    /// <summary>Starts or stops listening to layouts: an element taken out of its window (a list's old row) stops.</summary>
    private void Listen(bool listen)
    {
        if (listen == isListening || (!listen && DrawsWithoutWindow)) return;
        isListening = listen;
        if (listen) element.LayoutUpdated += layoutDone;
        else element.LayoutUpdated -= layoutDone;
    }

    /// <summary>
    /// Whether elements in no window are drawn: the workbench's screenshots draw the app without one. In the app, an
    /// element in no window (not shown yet, put away, or thrown away and not yet cleared from memory) is not drawn, so
    /// it is not checked: thousands of them, such as a long list's old rows and cards, each walking up to the window
    /// after every layout, made every key typed and every step of a scroll slower.
    /// </summary>
    internal static bool DrawsWithoutWindow { get; set; }

    /// <summary>Redraws the element when it has moved by a fraction of a pixel since it was last drawn.</summary>
    private void CheckPosition()
    {
        // only an element shown is checked: in a window and not hidden there
        if (element.IsLoaded ? !element.IsVisible : !DrawsWithoutWindow) return;
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

    // ---- pausing while something scrolls ----

    /// <summary>How long a scrolled box must stand still before its edges are checked again.</summary>
    private static readonly TimeSpan StillAfterScrolling = TimeSpan.FromMilliseconds(120);

    /// <summary>Whether edges are paused for a scroll (counted once among the drags, however long it goes on).</summary>
    private static bool pausedForScroll;

    /// <summary>Started at each move of a scroll, so it fires once the scrolling has stopped.</summary>
    private static DispatcherTimer? scrollStill;

    /// <summary>
    /// A list or page scrolled: edges are not checked until it has stood still for a moment, then all once, as after a
    /// drag. Checking every card's and row's edges after each frame took more than half of each frame while the Units
    /// tab's cards scrolled (the more cards in view, the more so). A scroll by the wheel moves by whole pixels, so
    /// nothing goes out of line meanwhile; one by the scrollbar can leave an edge half a pixel off until it stops.
    /// </summary>
    public static void ScrollMoved()
    {
        if (!pausedForScroll)
        {
            pausedForScroll = true;
            PauseForDrag();
        }
        scrollStill ??= new DispatcherTimer(StillAfterScrolling, DispatcherPriority.Background, ScrollStopped, Dispatcher.CurrentDispatcher) { IsEnabled = false };
        scrollStill.Stop();
        scrollStill.Start();
    }

    private static void ScrollStopped(object? sender, EventArgs e)
    {
        scrollStill?.Stop();
        if (!pausedForScroll) return;
        pausedForScroll = false;
        ResumeAfterDrag();
    }

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
    /// <remarks>
    /// Every snapped element asks this after every layout, so it adds up the transforms and offsets on the way up
    /// itself, in the same order and with the same sums as WPF's TransformToAncestor, which gives exactly the same
    /// position. TransformToAncestor made a new frozen transform on each call (and walked up twice), which with a few
    /// thousand cards' edges took most of each layout. An element under an effect (a drop shadow) or in 3D is left to
    /// TransformToAncestor, which allows for those.
    /// </remarks>
    private Point OffsetOnWindow()
    {
        Matrix toWindow = Matrix.Identity;
        Visual current = element;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(current); parent is not null; parent = VisualTreeHelper.GetParent(current))
        {
            if (parent is not Visual visualParent || VisualTreeHelper.GetEffect(current) is not null) return OffsetByTransformToAncestor();
            if (VisualTreeHelper.GetTransform(current) is Transform transform) toWindow *= transform.Value;
            Vector offsetInParent = VisualTreeHelper.GetOffset(current);
            toWindow.Translate(offsetInParent.X, offsetInParent.Y);
            current = visualParent;
        }
        return toWindow.Transform(new Point());
    }

    /// <summary>
    /// <see cref="OffsetOnWindow"/> as WPF works it out, to the topmost visual above the element: for an element under
    /// an effect or in 3D.
    /// </summary>
    private Point OffsetByTransformToAncestor()
    {
        Visual root = element;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(element); parent is Visual visual; parent = VisualTreeHelper.GetParent(parent))
            root = visual;
        return ReferenceEquals(root, element) ? new Point() : element.TransformToAncestor(root).Transform(new Point());
    }
}
