using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// The corner of a text box of several lines that makes it taller or shorter when dragged (a web page's
/// <c>textarea{resize:vertical}</c>), drawn pixel for pixel as Chrome draws it: two short diagonal lines, light, each with a
/// dark line beside it. It sits in the box's bottom right corner (Styles/Controls.xaml, TextArea), or over the corner of
/// another box given as its <see cref="Target"/>.
/// </summary>
public sealed class ResizeCorner : Thumb
{
    /// <summary>The light pixels, from the corner: x and y count back from the box's inside bottom right.</summary>
    private static readonly (int X, int Y)[] Light = [(-3, -8), (-4, -7), (-5, -6), (-6, -5), (-7, -4), (-8, -3), (-3, -4), (-4, -3)];

    /// <summary>The dark pixels beside them.</summary>
    private static readonly (int X, int Y)[] Dark = [(-3, -9), (-4, -8), (-5, -7), (-6, -6), (-7, -5), (-8, -4), (-9, -3), (-3, -5), (-4, -4), (-5, -3)];

    private static readonly Brush LightInk = Frozen(Color.FromRgb(0xA1, 0xA1, 0xA1));
    private static readonly Brush DarkInk = Frozen(Color.FromRgb(0x08, 0x08, 0x08));

    private readonly PixelSnapping snapping;

    /// <summary>The box it makes taller or shorter, when it is not in a text box's template.</summary>
    public FrameworkElement? Target { get; set; }

    /// <summary>A corner 9 pixels square, in the bottom right of what holds it.</summary>
    public ResizeCorner()
    {
        Width = Height = 9;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Cursor = Cursors.SizeNS;
        Template = null;
        snapping = new PixelSnapping(this);
        ToolTip = Text.Texts.Get("Common.ResizeGrip.Tip");
        DragDelta += Dragged;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        // all of it takes the mouse, not only the lines
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        Rect corner = snapping.Snap(0, 0, ActualWidth, ActualHeight);
        foreach ((int x, int y) in Dark) drawing.DrawRectangle(DarkInk, null, new Rect(corner.Right + x, corner.Bottom + y, 1, 1));
        foreach ((int x, int y) in Light) drawing.DrawRectangle(LightInk, null, new Rect(corner.Right + x, corner.Bottom + y, 1, 1));
    }

    /// <summary>The height the box given as <see cref="Target"/> had before it was first dragged, which a double-click puts back.</summary>
    private double? targetHeightAtFirst;

    /// <summary>A double-click puts the box back to the height it had at first, as double-clicking any bar between panes does.</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            base.OnMouseLeftButtonDown(e);
            return;
        }
        e.Handled = true;
        if (Target is FrameworkElement target)
        {
            if (targetHeightAtFirst is double first && !double.IsNaN(first)) target.Height = first;
            else target.ClearValue(HeightProperty);
            return;
        }
        if (TemplatedParent is TextBox box) box.ClearValue(HeightProperty);
    }

    /// <summary>Dragging makes the text box taller or shorter; never shorter than one line. A box given instead keeps to its own least and most height.</summary>
    private void Dragged(object sender, DragDeltaEventArgs e)
    {
        if (Target is FrameworkElement target)
        {
            targetHeightAtFirst ??= target.Height;
            target.Height = Math.Clamp(target.ActualHeight + e.VerticalChange, target.MinHeight, Math.Max(target.MinHeight, target.MaxHeight));
            return;
        }
        if (TemplatedParent is not TextBox box) return;
        double oneLine = (double)box.GetValue(TextBlock.LineHeightProperty) + box.Padding.Top + box.Padding.Bottom + 2;
        box.Height = Math.Max(oneLine, box.ActualHeight + e.VerticalChange);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
