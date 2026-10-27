using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The game's veterancy marks, drawn in the theme's text colour: a dash for the lowest level, then one upward chevron
/// more for each level above it, stacked, as on the game's cards.
/// </summary>
public static class VeterancyMarks
{
    /// <summary>How dim a level the division does not offer (0 units) is drawn.</summary>
    private const double NotOfferedOpacity = 0.38;

    /// <summary>One level's mark, 18 by 16 pixels, or smaller.</summary>
    /// <param name="level">The level: 0 for the dash, 1 and up for that many chevrons.</param>
    /// <param name="height">How tall: 16 pixels in the Division pane, less on a tile's strip; it is 9 eighths as wide.</param>
    public static Path Mark(int level, double height = 16)
    {
        double width = height * 9 / 8;
        var mark = new Path
        {
            Data = MarkShape(level, width, height),
            Width = width,
            Height = height,
            StrokeThickness = 2.1 * height / 16,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square,
            StrokeLineJoin = PenLineJoin.Miter,
            VerticalAlignment = VerticalAlignment.Center,
        };
        mark.SetResourceReference(Shape.StrokeProperty, "ink");
        return mark;
    }

    /// <summary>The marks' shapes made so far, by level and height: every tile shows the same few, so each is made once.</summary>
    private static readonly Dictionary<(int Level, double Height), Geometry> shapes = [];

    /// <summary>A level's mark as a shape, made the first time and shared from then on.</summary>
    private static Geometry MarkShape(int level, double width, double height)
    {
        if (shapes.TryGetValue((level, height), out Geometry? shape)) return shape;
        shape = Geometry.Parse(MarkPath(level, width, height));
        shape.Freeze();
        shapes[(level, height)] = shape;
        return shape;
    }

    /// <summary>
    /// The mark's lines: a dash across the middle; or chevrons a quarter of the height apart (4.2 pixels at 16), each
    /// rising a little less to its point, the stack centred top to bottom.
    /// </summary>
    private static string MarkPath(int level, double width, double height)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        if (level == 0) return string.Format(invariant, "M 3,{0} H {1:0.##}", height / 2, width - 3);
        double step = 4.2 * height / 16, rise = 3.6 * height / 16;
        double top = (height - ((level - 1) * step + rise)) / 2;
        var lines = new List<string>();
        for (int chevron = 0; chevron < level; chevron++)
        {
            double point = top + chevron * step;
            lines.Add(string.Format(invariant, "M 2.5,{0:0.##} L {1:0.##},{2:0.##} L {3:0.##},{0:0.##}", point + rise, width / 2, point, width - 2.5));
        }
        return string.Join(" ", lines);
    }

    /// <summary>
    /// A tile's strip: the marks one under the other, the highest level at the top as on the game's cards, sunken on
    /// the list's dark colour, each mark dimmed when its level has no units and saying its level and count as a tooltip.
    /// </summary>
    public static Bevel Strip(IReadOnlyList<int> counts)
    {
        var marks = new System.Windows.Controls.Primitives.UniformGrid { Columns = 1 };
        for (int level = counts.Count - 1; level >= 0; level--)
        {
            Path mark = StripMark(level);
            var place = new Border
            {
                Background = Brushes.Transparent,
                Child = mark,
                Opacity = counts[level] == 0 ? NotOfferedOpacity : 1,
                ToolTip = CountTip(level, counts[level]),
            };
            marks.Children.Add(place);
        }
        return StripAround(marks);
    }

    /// <summary>
    /// A battlegroup card's strip: the same marks as <see cref="Strip"/>, each a level to choose. The card's level is
    /// outlined in the theme's success colour; under the mouse, a level it can be given is on the hover colour with its
    /// mark in the accent colour, as the version button is, to show it can be clicked; a level the division does not
    /// offer is dim and cannot be chosen.
    /// </summary>
    /// <param name="counts">The units per card at each level, lowest first.</param>
    /// <param name="chosen">The card's level.</param>
    /// <param name="choose">What a click on another level does, given that level.</param>
    public static Bevel ChoiceStrip(IReadOnlyList<int> counts, int chosen, Action<int> choose)
    {
        var marks = new System.Windows.Controls.Primitives.UniformGrid { Columns = 1 };
        for (int level = counts.Count - 1; level >= 0; level--)
        {
            Path mark = StripMark(level);
            var place = new Border { Background = Brushes.Transparent, Child = mark, ToolTip = CountTip(level, counts[level]) };
            bool offered = counts[level] > 0;
            bool isChosen = level == chosen;
            if (isChosen)
            {
                place.BorderThickness = new Thickness(1);
                place.SetResourceReference(Border.BorderBrushProperty, "ok");
            }
            if (!offered) place.Opacity = NotOfferedOpacity;
            if (offered && !isChosen)
            {
                int level0 = level;
                place.Cursor = System.Windows.Input.Cursors.Hand;
                place.MouseEnter += (_, _) =>
                {
                    place.SetResourceReference(Border.BackgroundProperty, "hover");
                    mark.SetResourceReference(Shape.StrokeProperty, "accent");
                };
                place.MouseLeave += (_, _) =>
                {
                    place.Background = Brushes.Transparent;
                    mark.SetResourceReference(Shape.StrokeProperty, "ink");
                };
                place.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    choose(level0);
                };
            }
            // a click on the strip is the strip's own: it does not select the card's unit as well
            place.MouseLeftButtonDown += (_, e) => e.Handled = true;
            marks.Children.Add(place);
        }
        return StripAround(marks);
    }

    /// <summary>A mark for a tile's strip, centred across; how far down its place it sits is set by <see cref="SpreadMarks"/>.</summary>
    private static Path StripMark(int level)
    {
        Path mark = Mark(level, StripMarkHeight);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Top;
        return mark;
    }

    /// <summary>The strip's sunken box on the list's dark colour, around the levels' places, one under the other.</summary>
    private static Bevel StripAround(System.Windows.Controls.Primitives.UniformGrid marks)
    {
        marks.SizeChanged += (_, _) => SpreadMarks(marks);
        var strip = new Bevel { Child = marks, Width = StripWidth, Padding = new Thickness(0, 2, 0, 2) };
        strip.SetResourceReference(Bevel.TopLeftProperty, "lo");
        strip.SetResourceReference(Bevel.BottomRightProperty, "hi");
        strip.SetResourceReference(Bevel.BackgroundProperty, "well");
        return strip;
    }

    /// <summary>
    /// Spreads the marks evenly from the strip's top to its bottom: the top mark at the top of its place, the bottom
    /// one at the bottom of its place, the others evenly between. Centred in their places, the marks left room
    /// above the top one and below the bottom one.
    /// </summary>
    private static void SpreadMarks(System.Windows.Controls.Primitives.UniformGrid marks)
    {
        int count = marks.Children.Count;
        if (count < 2) return;
        double placeHeight = marks.ActualHeight / count;
        for (int place = 0; place < count; place++)
        {
            if (marks.Children[place] is not Border { Child: Path mark } border) continue;
            // inside the chosen level's outline, its place is a pixel smaller at each edge
            double room = Math.Max(0, placeHeight - border.BorderThickness.Top - border.BorderThickness.Bottom - StripMarkHeight);
            mark.Margin = new Thickness(0, room * place / (count - 1), 0, 0);
        }
    }

    /// <summary>How wide a tile's strip is, and how tall its marks.</summary>
    public const double StripWidth = 18, StripMarkHeight = 12;

    /// <summary>A level's tooltip: its number, from 1 as the Table view's boxes count, and its units per card.</summary>
    private static string CountTip(int level, int count) =>
        Texts.Get("Divisions.Veterancy.Count.Tip", ("level", Texts.Number(level + 1)), ("units", Texts.Number(count)));
}
