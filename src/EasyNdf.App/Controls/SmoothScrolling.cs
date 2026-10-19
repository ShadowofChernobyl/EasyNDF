using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// Scrolls with the mouse wheel the way Chrome does, everywhere in the app. WPF jumps 48 pixels per wheel notch at once;
/// Chrome glides 100 pixels per notch over a fifth of a second, and a notch turned while it glides makes it go further
/// without a jolt. The wheel scrolls the innermost list or box under the mouse that can still move that way, and the
/// page once that one is at its end, as in a browser. Shift and the wheel scroll sideways.
/// </summary>
public static class SmoothScrolling
{
    /// <summary>How far one notch of the wheel scrolls, as in Chrome on Windows.</summary>
    private const double PixelsPerNotch = 100;

    /// <summary>What Windows reports for one notch of the wheel.</summary>
    private const double DeltaPerNotch = 120;

    /// <summary>A glide of a notch or less takes 200 ms; one of four notches or more 100 ms, as in Chrome.</summary>
    private const double LongestGlide = 0.2, ShortestGlide = 0.1;

    /// <summary>The glide each scrolling box is on, if any.</summary>
    private static readonly ConditionalWeakTable<ScrollViewer, Glide> glides = [];

    /// <summary>
    /// Set on a box inside a page that scrolls, such as the Theme Designer's sample editor: the wheel passes over it and
    /// scrolls the page until the box is clicked into, so a mouse resting on it while the page is scrolled is not
    /// caught by it.
    /// </summary>
    public static readonly DependencyProperty ScrollsOnceClickedProperty = DependencyProperty.RegisterAttached(
        "ScrollsOnceClicked", typeof(bool), typeof(SmoothScrolling), new PropertyMetadata(false));

    /// <inheritdoc cref="ScrollsOnceClickedProperty"/>
    public static bool GetScrollsOnceClicked(DependencyObject element) => (bool)element.GetValue(ScrollsOnceClickedProperty);

    /// <inheritdoc cref="ScrollsOnceClickedProperty"/>
    public static void SetScrollsOnceClicked(DependencyObject element, bool value) => element.SetValue(ScrollsOnceClickedProperty, value);

    /// <summary>Starts smooth scrolling for every scrolling box of the app; called once at start-up.</summary>
    public static void Register()
    {
        // Wheel events tunnel down from the window, so the outermost box hears them first; it finds the box that
        // should move and marks the event as handled, so no other box moves too.
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(WheelTurned));
        // while any box scrolls, by the wheel, its scrollbar, the keyboard or a touchpad, edges are not checked on each
        // frame (see PixelSnapping.ScrollMoved)
        EventManager.RegisterClassHandler(typeof(ScrollViewer), ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(BoxScrolled));
    }

    private static void BoxScrolled(object sender, ScrollChangedEventArgs e)
    {
        // only a move: a box growing or shrinking also raises this, with nothing moved
        if (e.VerticalChange != 0 || e.HorizontalChange != 0) PixelSnapping.ScrollMoved();
    }

    private static void WheelTurned(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        // Ctrl and the wheel change an editor's text size; the editor hears it only if no box around it has taken it first
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && IsInEditor(e.OriginalSource as DependencyObject) && !IsWaitingForClick(e.OriginalSource as DependencyObject)) return;
        bool sideways = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        // turning the wheel towards you (a negative delta) scrolls down
        double distance = -e.Delta / DeltaPerNotch * PixelsPerNotch;
        ScrollViewer? box = BoxToScroll(e.OriginalSource as DependencyObject, distance, sideways);
        // the File Editor scrolls itself: gliding a long file pixel by pixel lays it out again on every frame
        if (box?.TemplatedParent is ICSharpCode.AvalonEdit.TextEditor) return;
        e.Handled = true;
        if (box is null) return;

        // a touchpad sends small steps of its own, already smooth: they move the box straight away, as in Chrome
        bool isWholeNotch = e.Delta % DeltaPerNotch == 0;
        Glide glide = glides.GetValue(box, _ => new Glide(box));
        glide.Add(distance, sideways, animate: isWholeNotch);
    }

    /// <summary>Whether an element is inside an editor (the File Editor's, or the Add Descriptor rule's).</summary>
    private static bool IsInEditor(DependencyObject? element)
    {
        for (DependencyObject? at = element; at is not null; at = Parent(at))
            if (at is ICSharpCode.AvalonEdit.TextEditor) return true;
        return false;
    }

    /// <summary>Whether an element is inside a box that scrolls once clicked (<see cref="ScrollsOnceClickedProperty"/>) and has not been clicked into.</summary>
    private static bool IsWaitingForClick(DependencyObject? element)
    {
        for (DependencyObject? at = element; at is not null; at = Parent(at))
            if (at is UIElement box && GetScrollsOnceClicked(box)) return !box.IsKeyboardFocusWithin;
        return false;
    }

    /// <summary>The innermost scrolling box around an element that can still move that way.</summary>
    private static ScrollViewer? BoxToScroll(DependencyObject? element, double distance, bool sideways)
    {
        for (DependencyObject? at = element; at is not null; at = Parent(at))
        {
            if (at is ScrollViewer box && CanMove(box, distance, sideways) && !IsWaitingForClick(box)) return box;
            // a dropdown's list at its end leaves the page behind it alone, as in a browser
            if (at is System.Windows.Controls.Primitives.Popup) return null;
        }
        return null;
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    /// <summary>Whether a box can scroll further that way, counting where its glide is already headed.</summary>
    private static bool CanMove(ScrollViewer box, double distance, bool sideways)
    {
        double scrollable = sideways ? box.ScrollableWidth : box.ScrollableHeight;
        if (scrollable <= 0) return false;
        double headedTo = glides.TryGetValue(box, out Glide? glide) && glide.IsGliding ? glide.Target : sideways ? box.HorizontalOffset : box.VerticalOffset;
        return distance > 0 ? headedTo < scrollable : headedTo > 0;
    }

    /// <summary>One box's glide from where it is to where the wheel sent it, drawn frame by frame.</summary>
    private sealed class Glide(ScrollViewer box)
    {
        private bool sideways;
        private double start, duration, velocityAtStart;
        private readonly Stopwatch clock = new();

        /// <summary>Whether the box is still moving.</summary>
        public bool IsGliding { get; private set; }

        /// <summary>Where the box is headed.</summary>
        public double Target { get; private set; }

        private double Offset => sideways ? box.HorizontalOffset : box.VerticalOffset;
        private double Scrollable => sideways ? box.ScrollableWidth : box.ScrollableHeight;

        /// <summary>Sends the box further; a glide under way heads for the new place from where it is, at the speed it has.</summary>
        public void Add(double distance, bool isSideways, bool animate)
        {
            if (IsGliding && isSideways != sideways) Stop();
            double from = IsGliding ? Target : Offset;
            Target = Math.Clamp(from + distance, 0, Scrollable);
            sideways = isSideways;
            if (!animate)
            {
                Stop();
                ScrollTo(Target);
                return;
            }

            double velocity = IsGliding ? Velocity() : 0;
            start = Offset;
            double length = Math.Abs(Target - start);
            // Chrome glides a short way slowly and a long way quickly: 200 ms for one notch, down to 100 ms for four
            double notches = Math.Clamp((length - PixelsPerNotch * 1.2) / (PixelsPerNotch * 3.6), 0, 1);
            duration = LongestGlide - (LongestGlide - ShortestGlide) * notches;
            velocityAtStart = velocity;
            clock.Restart();
            if (!IsGliding)
            {
                IsGliding = true;
                CompositionTarget.Rendering += NextFrame;
            }
        }

        private void NextFrame(object? sender, EventArgs e)
        {
            double t = Math.Min(1, clock.Elapsed.TotalSeconds / duration);
            ScrollTo(PositionAt(t));
            if (t >= 1) Stop();
        }

        /// <summary>
        /// Where the box is at a part of the glide, from 0 to 1. A glide from rest eases in and out; one that took over
        /// from another starts at that one's speed and eases out, so the box never jolts.
        /// </summary>
        private double PositionAt(double t)
        {
            double length = Target - start;
            if (velocityAtStart == 0 || length == 0)
            {
                // ease-in-out, smoothstep
                return start + length * t * t * (3 - 2 * t);
            }
            // a curve that starts at the speed it was given and comes to rest at the target (cubic Hermite)
            // at most 3, or the box would pass the target and come back; going the other way starts from rest
            double startSlope = Math.Clamp(velocityAtStart * duration / length, 0, 3);
            double eased = t * t * (3 - 2 * t) + startSlope * t * (1 - t) * (1 - t);
            return start + length * eased;
        }

        /// <summary>The box's speed now, in pixels per second.</summary>
        private double Velocity()
        {
            double t = Math.Min(1, clock.Elapsed.TotalSeconds / duration);
            const double step = 0.01;
            double before = PositionAt(Math.Max(0, t - step)), after = PositionAt(Math.Min(1, t + step));
            double span = (Math.Min(1, t + step) - Math.Max(0, t - step)) * duration;
            return span > 0 ? (after - before) / span : 0;
        }

        /// <summary>
        /// Moves the box, to a whole pixel: the page then only shifts, and nothing on it has to be drawn again (see
        /// PixelSnapping), which keeps each frame of the glide quick.
        /// </summary>
        private void ScrollTo(double offset)
        {
            offset = Math.Round(offset);
            if (sideways) box.ScrollToHorizontalOffset(offset);
            else box.ScrollToVerticalOffset(offset);
        }

        private void Stop()
        {
            if (!IsGliding) return;
            IsGliding = false;
            CompositionTarget.Rendering -= NextFrame;
        }
    }
}
