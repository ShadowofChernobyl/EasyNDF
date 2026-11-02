using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EasyNdf.App.Units;

/// <summary>
/// Rows, tiles and headings picked with the mouse, made so the keyboard can pick them too: Tab reaches them, the arrow
/// keys move between those of one list (Up and Down to the one above or below, Left and Right to the one before or
/// after), and Enter or Space picks the one that has the focus, as a click does. Without it the Armory's nations,
/// divisions and popups could only be used with a mouse.
/// </summary>
public static class KeyPick
{
    /// <summary>Marks an element made pickable, so the arrow keys find its neighbours.</summary>
    private static readonly DependencyProperty IsPickableProperty =
        DependencyProperty.RegisterAttached("IsPickable", typeof(bool), typeof(KeyPick), new PropertyMetadata(false));

    /// <summary>What an element is, so the one picked gets the focus back when its list is drawn again.</summary>
    private static readonly DependencyProperty PickKeyProperty =
        DependencyProperty.RegisterAttached("PickKey", typeof(string), typeof(KeyPick), new PropertyMetadata(null));

    /// <summary>
    /// The outline that shows where the keyboard is, drawn just inside the element: the app's usual one is drawn just
    /// outside, which a list's edges cut off.
    /// </summary>
    public static Style InsideOutline { get; } = MakeInsideOutline();

    /// <summary>
    /// The element picked last, by its key, while its list is drawn again: the element made for it next takes the
    /// focus, so picking a nation (which draws the list again) leaves the keyboard where it was.
    /// </summary>
    private static string? focusWhenMade;

    /// <summary>
    /// Makes a row, tile or heading pickable with the keyboard.
    /// </summary>
    /// <param name="element">The row, tile or heading.</param>
    /// <param name="pick">What a click on it does.</param>
    /// <param name="key">What it is (a nation's code, a column's), for it to keep the focus when its list is drawn again; null for none.</param>
    public static void Make(FrameworkElement element, Action pick, string? key = null)
    {
        element.Focusable = true;
        element.FocusVisualStyle = InsideOutline;
        element.SetValue(IsPickableProperty, true);
        element.SetValue(PickKeyProperty, key);
        element.KeyDown += (_, e) => KeyPressed(element, pick, e);
        if (key is not null && key == focusWhenMade)
            element.Loaded += (_, _) =>
            {
                if (focusWhenMade != key) return;
                focusWhenMade = null;
                element.Focus();
            };
    }

    /// <summary>Enter and Space pick; the arrows, Home and End move to another element of the list.</summary>
    private static void KeyPressed(FrameworkElement element, Action pick, KeyEventArgs e)
    {
        // a box or button inside the row keeps its own keys
        if (!ReferenceEquals(e.OriginalSource, element) || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return;
        switch (e.Key)
        {
            case Key.Enter or Key.Space:
                e.Handled = true;
                Pick(element, pick);
                break;
            case Key.Up or Key.Down:
                e.Handled = true;
                MoveTo(Above(element, down: e.Key == Key.Down));
                break;
            case Key.Left or Key.Right:
                e.Handled = true;
                MoveTo(Beside(element, after: e.Key == Key.Right));
                break;
            case Key.Home or Key.End:
                e.Handled = true;
                List<FrameworkElement> all = PickablesBeside(element);
                MoveTo(all.Count == 0 ? null : e.Key == Key.Home ? all[0] : all[^1]);
                break;
        }
    }

    /// <summary>Picks an element; when picking it draws its list again, the element made in its place takes the focus.</summary>
    private static void Pick(FrameworkElement element, Action pick)
    {
        var key = (string?)element.GetValue(PickKeyProperty);
        focusWhenMade = key;
        pick();
        // the list was not drawn again, or its new element has the focus already: nothing is waiting for it
        element.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => { if (focusWhenMade == key) focusWhenMade = null; });
    }

    private static void MoveTo(FrameworkElement? element) => element?.Focus();

    /// <summary>The element before or after this one in its list.</summary>
    private static FrameworkElement? Beside(FrameworkElement element, bool after)
    {
        List<FrameworkElement> all = PickablesBeside(element);
        int at = all.IndexOf(element);
        int next = at + (after ? 1 : -1);
        return at < 0 || next < 0 || next >= all.Count ? null : all[next];
    }

    /// <summary>
    /// The element below (or above) this one in its list: the nearest whose top is under its bottom (or whose bottom is
    /// over its top), the one most nearly in line with it first, so it also works across a grid of tiles.
    /// </summary>
    private static FrameworkElement? Above(FrameworkElement element, bool down)
    {
        List<FrameworkElement> all = PickablesBeside(element);
        Visual? holder = HolderOf(element);
        if (holder is null) return null;
        Rect from = BoundsIn(element, holder);
        FrameworkElement? best = null;
        double bestGap = double.MaxValue, bestAcross = double.MaxValue;
        foreach (FrameworkElement other in all)
        {
            if (ReferenceEquals(other, element)) continue;
            Rect to = BoundsIn(other, holder);
            double gap = down ? to.Top - from.Bottom : from.Top - to.Bottom;
            if (gap < -1) continue;
            double across = Math.Abs((to.Left + to.Right) / 2 - (from.Left + from.Right) / 2);
            // the nearest row first, then the element of it most nearly in line
            if (gap < bestGap - 1 || (Math.Abs(gap - bestGap) <= 1 && across < bestAcross))
            {
                best = other;
                bestGap = gap;
                bestAcross = across;
            }
        }
        return best;
    }

    /// <summary>Where an element is in what holds its list.</summary>
    private static Rect BoundsIn(FrameworkElement element, Visual holder) =>
        element.TransformToAncestor(holder).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    /// <summary>What holds an element's list: the scrolling box it is in, or else the top of what it is drawn in (a popup's).</summary>
    private static Visual? HolderOf(DependencyObject element)
    {
        DependencyObject? top = null;
        for (DependencyObject? at = VisualTreeHelper.GetParent(element); at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is ScrollViewer) return (Visual)at;
            top = at;
        }
        return top as Visual;
    }

    /// <summary>The elements of the list this one is in that show, in the order they are drawn.</summary>
    private static List<FrameworkElement> PickablesBeside(FrameworkElement element)
    {
        var found = new List<FrameworkElement>();
        if (HolderOf(element) is Visual holder) Collect(holder, found);
        return found;
    }

    private static void Collect(DependencyObject parent, List<FrameworkElement> found)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is UIElement { IsVisible: false }) continue;
            // a list inside the list (a scrolling box of its own) is another list
            if (child is ScrollViewer) continue;
            if (child is FrameworkElement element && (bool)element.GetValue(IsPickableProperty) && element.IsEnabled) found.Add(element);
            Collect(child, found);
        }
    }

    /// <summary>A thin accent outline, a pixel inside the element's edges.</summary>
    private static Style MakeInsideOutline()
    {
        var outline = new FrameworkElementFactory(typeof(Rectangle));
        outline.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
        outline.SetValue(Shape.StrokeThicknessProperty, 1.5);
        outline.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        outline.SetResourceReference(Shape.StrokeProperty, "accent");
        var style = new Style { Setters = { new Setter(Control.TemplateProperty, new ControlTemplate { VisualTree = outline }) } };
        style.Seal();
        return style;
    }
}
