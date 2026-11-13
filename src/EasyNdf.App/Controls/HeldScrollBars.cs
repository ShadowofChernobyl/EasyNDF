using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// While a bar between panes or the window's edge is dragged, a list whose scrollbar shows keeps it showing. A
/// ScrollViewer with an automatic scrollbar lays its content out without the bar and then again with it, every time
/// its size changes, so everything in a pane or a list of cards was measured twice at each step of a drag. Tuned for
/// speed: Keep it that way when tidying.
/// </summary>
public static class HeldScrollBars
{
    /// <summary>Whether a ScrollViewer takes part, set on every one by its style.</summary>
    public static readonly DependencyProperty HoldsProperty = DependencyProperty.RegisterAttached(
        "Holds", typeof(bool), typeof(HeldScrollBars), new PropertyMetadata(false, HoldsChanged));

    /// <summary>The ScrollViewers on screen that take part.</summary>
    private static readonly HashSet<ScrollViewer> OnScreen = [];

    /// <summary>The ones holding their scrollbar for the drag under way.</summary>
    private static readonly List<ScrollViewer> Holding = [];

    static HeldScrollBars()
    {
        LayoutDrag.Started += Hold;
        LayoutDrag.Ended += LetGo;
    }

    public static void SetHolds(ScrollViewer scroller, bool value) => scroller.SetValue(HoldsProperty, value);

    public static bool GetHolds(ScrollViewer scroller) => (bool)scroller.GetValue(HoldsProperty);

    private static void HoldsChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ScrollViewer scroller || e.NewValue is not true) return;
        scroller.Loaded += (_, _) => OnScreen.Add(scroller);
        scroller.Unloaded += (_, _) => OnScreen.Remove(scroller);
        if (scroller.IsLoaded) OnScreen.Add(scroller);
    }

    private static void Hold()
    {
        foreach (ScrollViewer scroller in OnScreen)
        {
            if (!scroller.IsVisible || scroller.VerticalScrollBarVisibility != ScrollBarVisibility.Auto || scroller.ComputedVerticalScrollBarVisibility != Visibility.Visible) continue;
            scroller.SetCurrentValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Visible);
            Holding.Add(scroller);
        }
    }

    private static void LetGo()
    {
        foreach (ScrollViewer scroller in Holding) scroller.SetCurrentValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        Holding.Clear();
    }
}
