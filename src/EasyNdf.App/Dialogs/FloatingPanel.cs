using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// A panel shown beside a button, drawn in the app's own window as the ? buttons' popup is, rather than as a WPF Popup:
/// a Popup is a window of its own, and a large one with a search box and dropdowns was drawn for a moment at the
/// screen's corner, and closed when a choice was made in one of its dropdowns. A click anywhere outside the panel
/// closes it, as a popup's would; its own keys (Esc, Enter) are the panel's to handle.
/// </summary>
public sealed class FloatingPanel : Grid
{
    /// <summary>How far the panel keeps from its button and from the window's edges.</summary>
    private const double Gap = 3, EdgeRoom = 8;

    private readonly Canvas layer = new();

    /// <summary>The panel shown; null while closed.</summary>
    private FrameworkElement? panel;

    /// <summary>What to do when the panel closes, whether by its own Done or a click outside it.</summary>
    private Action? closed;

    /// <summary>Builds the layer, closed: see-through, it takes the clicks outside the panel.</summary>
    public FloatingPanel()
    {
        Visibility = Visibility.Collapsed;
        Background = Brushes.Transparent;
        Children.Add(layer);
        MouseDown += (_, e) =>
        {
            if (panel is not null && panel.IsMouseOver) return;
            e.Handled = true;
            Close();
        };
    }

    /// <summary>Whether a panel is shown.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>
    /// Shows a panel at the right of a button, its top level with the button's; at its left when there is no room at
    /// the right, and moved up when there is none below, never closer than 8 pixels to the window's edges.
    /// </summary>
    /// <param name="content">The panel.</param>
    /// <param name="button">The button it belongs to.</param>
    /// <param name="whenClosed">What to do when it closes.</param>
    public void Show(FrameworkElement content, FrameworkElement button, Action? whenClosed = null)
    {
        Close();
        panel = content;
        closed = whenClosed;
        layer.Children.Add(content);
        Visibility = Visibility.Visible;
        UpdateLayout();
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = content.DesiredSize;
        Rect at = button.TransformToVisual(this).TransformBounds(new Rect(button.RenderSize));
        double x = at.Right + Gap;
        if (x + size.Width > ActualWidth - EdgeRoom) x = at.Left - Gap - size.Width;
        double y = Math.Min(at.Top, ActualHeight - EdgeRoom - size.Height);
        Canvas.SetLeft(content, Math.Round(Math.Max(EdgeRoom, x)));
        Canvas.SetTop(content, Math.Round(Math.Max(EdgeRoom, y)));
    }

    /// <summary>Closes the panel shown, if any.</summary>
    public void Close()
    {
        if (panel is null) return;
        layer.Children.Clear();
        panel = null;
        Visibility = Visibility.Collapsed;
        Action? whenClosed = closed;
        closed = null;
        whenClosed?.Invoke();
    }

    /// <summary>Esc closes the panel when the panel itself did not take the key.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape || !IsOpen) return;
        e.Handled = true;
        Close();
    }
}
