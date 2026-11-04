namespace EasyNdf.App.Controls;

/// <summary>What is being dragged that lays the screen out again at every step.</summary>
public enum LayoutDragKind
{
    /// <summary>The window's edge or corner (Windows' messages for it, see <see cref="Windowing.CustomTitleBar"/>).</summary>
    Window,

    /// <summary>A bar between two panes, or a list's grip (<see cref="SplitBar"/>, <see cref="ResizeGrip"/>).</summary>
    Panes,
}

/// <summary>
/// A drag of the window's edge or of a bar between panes. While one is under way, the heavier views hold their layout and
/// lay out again when the mouse pauses or the drag ends: a big division's cards were laid out again at every step, some
/// 100 ms each. The usage readout times the steps.
/// </summary>
public static class LayoutDrag
{
    /// <summary>The drag under way, or null when none is.</summary>
    public static LayoutDragKind? UnderWay { get; private set; }

    /// <summary>Whether a drag is under way.</summary>
    public static bool IsUnderWay => UnderWay is not null;

    /// <summary>Raised at each step of a drag, before the screen is laid out for it.</summary>
    public static event Action<LayoutDragKind>? Stepped;

    /// <summary>Raised as a drag ends, the screen at its new size.</summary>
    public static event Action? Ended;

    /// <summary>A drag starts.</summary>
    public static void Start(LayoutDragKind kind) => UnderWay ??= kind;

    /// <summary>A step of the drag: the window or a pane changes size.</summary>
    public static void Step()
    {
        if (UnderWay is LayoutDragKind kind) Stepped?.Invoke(kind);
    }

    /// <summary>The drag ends.</summary>
    public static void End()
    {
        if (UnderWay is null) return;
        UnderWay = null;
        Ended?.Invoke();
    }
}
