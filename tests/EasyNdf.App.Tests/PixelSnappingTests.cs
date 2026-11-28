using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Controls;

namespace EasyNdf.App.Tests;

/// <summary>
/// An edge drawn on whole pixels (a <see cref="Bevel"/>) is drawn again after a layout that moves it by a fraction of a
/// pixel on the window, however the move comes about (a margin above it, a transform above it), and not after one that
/// moves it by whole pixels.
/// </summary>
public sealed class PixelSnappingTests
{
    [Fact]
    public void A_move_by_whole_pixels_draws_nothing_again() => WithEdges((holder, edge, root) =>
    {
        int drawn = edge.Draws;
        holder.Margin = new Thickness(3.25, 2, 0, 0);
        root.UpdateLayout();
        Assert.Equal(drawn, edge.Draws);
    });

    [Fact]
    public void A_move_by_a_fraction_of_a_pixel_draws_the_edge_again() => WithEdges((holder, edge, root) =>
    {
        int drawn = edge.Draws;
        holder.Margin = new Thickness(0.75, 0, 0, 0);
        root.UpdateLayout();
        Assert.True(edge.Draws > drawn);
    });

    [Fact]
    public void A_transform_above_that_moves_it_by_a_fraction_draws_it_again() => WithEdges((holder, edge, root) =>
    {
        int drawn = edge.Draws;
        holder.RenderTransform = new TranslateTransform(4, 0);
        root.UpdateLayout();
        Assert.Equal(drawn, edge.Draws);
        // scaled twice over, a quarter of a pixel inside is half a pixel on the window
        holder.RenderTransform = new ScaleTransform(2, 2);
        root.InvalidateArrange();
        root.UpdateLayout();
        int afterScale = edge.Draws;
        holder.Padding = new Thickness(0.25, 0, 0, 0);
        root.UpdateLayout();
        Assert.True(edge.Draws > afterScale);
    });

    /// <summary>An edge that counts how often it is drawn.</summary>
    private sealed class CountingBevel : Bevel
    {
        public int Draws { get; private set; }

        protected override void OnRender(DrawingContext drawing)
        {
            Draws++;
            base.OnRender(drawing);
        }
    }

    /// <summary>
    /// Lays out an edge in a box a quarter of a pixel from the left, drawn without a window as the workbench's
    /// screenshots draw the app, and runs a check on it.
    /// </summary>
    private static void WithEdges(Action<Border, CountingBevel, FrameworkElement> check) => WindowThread.Run(() =>
    {
        PropertyInfo withoutWindow = typeof(PixelSnapping).GetProperty("DrawsWithoutWindow", BindingFlags.Static | BindingFlags.NonPublic)!;
        withoutWindow.SetValue(null, true);
        try
        {
            var edge = new CountingBevel { Width = 20, Height = 10, TopLeft = Brushes.Red, BottomRight = Brushes.Blue };
            var holder = new Border { Child = edge, Margin = new Thickness(0.25, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var root = new Grid { Children = { holder } };
            root.Measure(new Size(200, 100));
            root.Arrange(new Rect(0, 0, 200, 100));
            root.UpdateLayout();
            check(holder, edge, root);
        }
        finally
        {
            withoutWindow.SetValue(null, false);
        }
    });
}
