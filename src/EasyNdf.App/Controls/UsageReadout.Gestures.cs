using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyNdf.App.Text;

namespace EasyNdf.App.Controls;

/// <summary>
/// The usage readout's timing of drags and scrolls: each resize of the window, drag of a pane's edge and scroll is
/// timed step by step, and one whose steps take longer than a frame at 144 Hz on average is a slow moment, with how many
/// steps it took and the slowest. A step's time runs from its start until the screen is free for a click again, so it
/// counts the layout and the drawing it caused.
/// </summary>
public sealed partial class UsageReadout
{
    /// <summary>A frame at 144 Hz, in milliseconds: steps slower than this on average make a drag or a scroll look jerky.</summary>
    private const double OneFrame = 1000.0 / 144;

    /// <summary>How long without a step before a drag or a scroll counts as over.</summary>
    private static readonly TimeSpan GestureEnds = TimeSpan.FromMilliseconds(300);

    /// <summary>What a drag or a scroll moved.</summary>
    private enum GestureKind { Window, Panes, Scroll }

    /// <summary>A drag or a scroll so far: when it started and last stepped, its steps, their time and the slowest.</summary>
    private sealed class Gesture(GestureKind kind, DateTime started)
    {
        public GestureKind Kind { get; } = kind;
        public DateTime Started { get; } = started;
        public DateTime LastStep { get; set; } = started;
        public int Steps { get; set; }
        public double Busy { get; set; }
        public double Slowest { get; set; }
    }

    private Gesture? gesture;

    /// <summary>When the last drag or scroll ended; a wait for the screen during one is that gesture's, not a freeze.</summary>
    private DateTime lastGestureEnded;

    /// <summary>When the step being timed started, or 0 when none is.</summary>
    private long stepStartedAt;

    private DispatcherTimer? gestureQuiet;

    /// <summary>Whether the window-wide scroll handler is in place; it is put in once and looks at <see cref="watching"/>.</summary>
    private static bool scrollHandlerAdded;

    /// <summary>Times the drags and the scrolls while the readout is on.</summary>
    private void WatchGestures()
    {
        LayoutDrag.Stepped += kind => Step(kind == LayoutDragKind.Window ? GestureKind.Window : GestureKind.Panes);
        if (scrollHandlerAdded) return;
        scrollHandlerAdded = true;
        // every list's and page's scrolls, by the wheel, the scrollbar or the keys; a list that only changed size is no scroll
        EventManager.RegisterClassHandler(typeof(ScrollViewer), ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, e) => { if (e.VerticalChange != 0 || e.HorizontalChange != 0) Scrolled(); }), handledEventsToo: true);
    }

    private static void Scrolled()
    {
        // a list scrolls by itself as a drag changes its size: that step is the drag's
        if (LayoutDrag.IsUnderWay) return;
        lock (readouts)
            foreach (WeakReference<UsageReadout> reference in readouts)
                if (reference.TryGetTarget(out UsageReadout? readout)) readout.Step(GestureKind.Scroll);
    }

    /// <summary>A step of a drag or a scroll: it starts a new gesture when the last one is over or of another kind, and is timed.</summary>
    private void Step(GestureKind kind)
    {
        if (!watching) return;
        DateTime now = DateTime.Now;
        if (gesture is not null && (gesture.Kind != kind || now - gesture.LastStep > GestureEnds)) FinishGesture();
        gesture ??= new Gesture(kind, now);
        gesture.Steps++;
        gesture.LastStep = now;
        // the step is over once the screen is free for a click again: its layout and drawing done
        if (stepStartedAt == 0)
        {
            stepStartedAt = Stopwatch.GetTimestamp();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, StepDone);
        }
        gestureQuiet ??= new DispatcherTimer(GestureEnds, DispatcherPriority.Background, (_, _) => FinishGesture(), Dispatcher) { IsEnabled = false };
        gestureQuiet.Stop();
        gestureQuiet.Start();
    }

    private void StepDone()
    {
        double took = Stopwatch.GetElapsedTime(stepStartedAt).TotalMilliseconds;
        stepStartedAt = 0;
        if (gesture is null) return;
        gesture.Busy += took;
        gesture.Slowest = Math.Max(gesture.Slowest, took);
    }

    /// <summary>The gesture is over: a slow moment when its steps took longer than a frame on average.</summary>
    private void FinishGesture()
    {
        gestureQuiet?.Stop();
        if (gesture is not Gesture done) return;
        gesture = null;
        lastGestureEnded = DateTime.Now;
        if (done.Steps < 3) return;
        double each = done.Busy / done.Steps;
        if (each <= OneFrame) return;
        string kind = Texts.Get(done.Kind switch
        {
            GestureKind.Window => "Main.Usage.Gesture.Window",
            GestureKind.Panes => "Main.Usage.Gesture.Panes",
            _ => "Main.Usage.Gesture.Scroll",
        });
        string did = Texts.Count("Main.Usage.Gesture.Steps", done.Steps, ("gesture", kind), ("slowest", Math.Round(done.Slowest).ToString(CultureInfo.CurrentCulture)));
        AddSlowMoment(done.LastStep, each, did, Work.Current?.Name ?? "");
    }

    /// <summary>Whether a wait for the screen fell during a drag or a scroll, which counts it by its steps instead.</summary>
    private bool DuringGesture(DateTime waitStarted) => gesture is not null || lastGestureEnded >= waitStarted;
}
