using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyNdf.App.Controls;

/// <summary>
/// The Division Generator's icon: a seven-tooth gear with a spark at its top right, drawn as lines on a 16 by 16 grid
/// (Paul's choice, option C of the mock). It marks the button that opens the generator, generated divisions in the
/// list, and the line above a generated division's units, sharp at any zoom where the ⚙ character was blurry.
/// </summary>
public static class GeneratorIcon
{
    /// <summary>The gear's lines and the spark's, made once and shared by every icon.</summary>
    private static readonly Geometry Lines = MakeShape();

    /// <summary>The icon, <paramref name="size"/> pixels square, its lines in a theme colour.</summary>
    /// <param name="size">How big: 14 on a heading button, a little less beside a name.</param>
    /// <param name="colour">The theme colour of its lines: "ink", or "accent" where the ⚙ was in the accent colour.</param>
    public static Viewbox Make(double size, string colour = "ink")
    {
        var lines = new Path
        {
            Data = Lines, Width = 16, Height = 16, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        lines.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, colour);
        return new Viewbox { Width = size, Height = size, Child = lines, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Gives an icon's lines another theme colour: the accent colour under the mouse, its own after.</summary>
    public static void Colour(Viewbox icon, string colour) => ((Path)icon.Child).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, colour);

    /// <summary>
    /// The gear, a little smaller and nudged down and left to leave room for the spark: seven teeth, each a flat top
    /// on the outer circle and a flat foot on the inner one, round a hole; then the spark, a plus.
    /// </summary>
    private static Geometry MakeShape()
    {
        const int teeth = 7;
        const double outer = 7.2, inner = 5.3, hole = 2.2, scale = 0.82;
        Point Moved(double x, double y) => new((x + 1.8) * scale - 1.6, (y + 1.8) * scale + 1.6);
        var gear = new StreamGeometry();
        using (StreamGeometryContext draw = gear.Open())
        {
            int corners = teeth * 2;
            double step = Math.PI * 2 / corners, inset = step * 0.18;
            for (int corner = 0; corner < corners; corner++)
            {
                double from = corner * step - Math.PI / 2, to = from + step, radius = corner % 2 == 0 ? outer : inner;
                Point start = Moved(8 + radius * Math.Cos(from + inset), 8 + radius * Math.Sin(from + inset));
                Point end = Moved(8 + radius * Math.Cos(to - inset), 8 + radius * Math.Sin(to - inset));
                if (corner == 0) draw.BeginFigure(start, false, true);
                else draw.LineTo(start, true, true);
                draw.LineTo(end, true, true);
            }
        }
        Point centre = Moved(8, 8);
        var shape = new GeometryGroup
        {
            Children =
            {
                gear,
                new EllipseGeometry(centre, hole * scale, hole * scale),
                Geometry.Parse("M 13.2,0.9 V 5.5 M 10.9,3.2 H 15.5"),
            },
        };
        shape.Freeze();
        return shape;
    }
}
