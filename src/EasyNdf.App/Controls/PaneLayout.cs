using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// How the Division, Unit and Weapon panes fit any width (Paul's choice, "Stack When Narrow"): in a narrow pane a
/// name sits over its value and the value tables show one value a row, so nothing is cut short; in a wide pane the
/// sections sit in two columns and the picture beside the name. Each panel here chooses by the width it is given,
/// never by its content's size, so it cannot flip back and forth.
/// </summary>
public static class PaneLayout
{
    /// <summary>Narrower than this, a table of two pairs a row shows one pair a row.</summary>
    public const double OnePairBelow = 420;

    /// <summary>Narrower than this, a name sits over its value.</summary>
    public const double NameOverValueBelow = 260;

    /// <summary>At least this wide, a pane's sections sit in two columns and its picture beside its name.</summary>
    public const double TwoColumnsFrom = 560;

    /// <summary>Narrower than this, a division's emblem sits over its name.</summary>
    public const double EmblemOverNameBelow = 300;

    /// <summary>Remembers where each child of a grid was put as it was built, so it can go back there.</summary>
    internal static Dictionary<UIElement, (int Row, int Column)> PlacesOf(Grid grid) =>
        grid.Children.Cast<UIElement>().ToDictionary(child => child, child => (Grid.GetRow(child), Grid.GetColumn(child)));

    /// <summary>Gives a cell its line under it, or none on the table's last row.</summary>
    internal static void LineUnder(UIElement child, bool lastRow)
    {
        if (child is Bevel cell) cell.Edges = new Thickness(cell.Edges.Left, cell.Edges.Top, cell.Edges.Right, lastRow ? 0 : 1);
    }

    /// <summary>Gives a grid as many rows as asked, each as tall as its content.</summary>
    internal static void RowsOf(Grid grid, int count)
    {
        while (grid.RowDefinitions.Count > count) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
        while (grid.RowDefinitions.Count < count) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }
}

/// <summary>
/// A table of values, two pairs of name and value a row (a name, its value, a name, its value), that shows one pair a
/// row when narrower than <see cref="PaneLayout.OnePairBelow"/>: the Unit pane's stats and the Weapon pane's values.
/// The empty pair that evens out an odd count is hidden then.
/// </summary>
public sealed class PairGrid : Grid
{
    private Dictionary<UIElement, (int Row, int Column)>? built;
    private bool? onePairARow;

    protected override Size MeasureOverride(Size constraint)
    {
        built ??= PaneLayout.PlacesOf(this);
        bool narrow = constraint.Width < PaneLayout.OnePairBelow;
        if (narrow != onePairARow)
        {
            onePairARow = narrow;
            Place(narrow);
        }
        return base.MeasureOverride(constraint);
    }

    /// <summary>Puts every cell where the layout wants it: its own place, or one pair a row.</summary>
    private void Place(bool narrow)
    {
        int builtRows = built!.Values.Select(place => place.Row).DefaultIfEmpty(0).Max() + 1;
        // the pairs in reading order, each its name and its value, the empty one left out when narrow
        var pairs = built.GroupBy(each => (each.Value.Row, Pair: each.Value.Column / 2)).OrderBy(group => group.Key.Row).ThenBy(group => group.Key.Pair).ToList();
        if (narrow) pairs.RemoveAll(IsEmpty);
        int rows = narrow ? pairs.Count : builtRows;
        PaneLayout.RowsOf(this, rows);
        foreach ((UIElement child, (int row, int column)) in built)
        {
            int pair = pairs.FindIndex(group => group.Any(each => each.Key == child));
            child.Visibility = pair < 0 ? Visibility.Collapsed : Visibility.Visible;
            int newRow = narrow ? pair : row;
            SetRow(child, Math.Max(0, newRow));
            SetColumn(child, narrow ? column % 2 : column);
            PaneLayout.LineUnder(child, newRow == rows - 1);
        }
        ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
    }

    /// <summary>Whether a pair is the empty one that evens out an odd count: its name is empty.</summary>
    private static bool IsEmpty(IGrouping<(int Row, int Pair), KeyValuePair<UIElement, (int Row, int Column)>> pair) =>
        pair.Any(each => each.Value.Column % 2 == 0 && each.Key is Bevel { Child: TextBlock { Text: "" } });
}

/// <summary>
/// A table of names and values, one a row, the name at the left: the Unit section and the Division section. Narrower
/// than <see cref="PaneLayout.NameOverValueBelow"/>, each name sits over its value, so the value has the whole width.
/// </summary>
public sealed class NameValueGrid : Grid
{
    private Dictionary<UIElement, (int Row, int Column)>? built;
    private bool? nameOverValue;

    protected override Size MeasureOverride(Size constraint)
    {
        built ??= PaneLayout.PlacesOf(this);
        bool narrow = constraint.Width < PaneLayout.NameOverValueBelow;
        if (narrow != nameOverValue)
        {
            nameOverValue = narrow;
            int rows = built.Values.Select(place => place.Row).DefaultIfEmpty(0).Max() + 1;
            PaneLayout.RowsOf(this, narrow ? rows * 2 : rows);
            foreach ((UIElement child, (int row, int column)) in built)
            {
                bool isName = column == 0;
                SetRow(child, narrow ? row * 2 + (isName ? 0 : 1) : row);
                SetColumn(child, narrow ? 0 : column);
                SetColumnSpan(child, narrow ? 2 : 1);
                // a name over its value has no line between them
                PaneLayout.LineUnder(child, narrow ? isName || row == rows - 1 : row == rows - 1);
            }
        }
        return base.MeasureOverride(constraint);
    }
}

/// <summary>
/// A pane's sections, one under the other; at least <see cref="PaneLayout.TwoColumnsFrom"/> wide, two side by side,
/// each pair as tall as the taller of the two. A section marked <see cref="FullWidthProperty"/> (one whose content
/// needs the room, such as a unit's weapons) takes a row of its own.
/// </summary>
public sealed class SectionColumns : Panel
{
    /// <summary>Whether a section takes the whole width, on a row of its own.</summary>
    public static readonly DependencyProperty FullWidthProperty =
        DependencyProperty.RegisterAttached("FullWidth", typeof(bool), typeof(SectionColumns), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static void SetFullWidth(UIElement element, bool value) => element.SetValue(FullWidthProperty, value);

    public static bool GetFullWidth(UIElement element) => (bool)element.GetValue(FullWidthProperty);

    /// <summary>The rows the sections are laid out in: one or two sections each, and how tall each row is.</summary>
    private readonly List<(UIElement[] Sections, double Height)> rows = [];

    protected override Size MeasureOverride(Size available)
    {
        rows.Clear();
        double width = available.Width;
        bool twoColumns = !double.IsInfinity(width) && width >= PaneLayout.TwoColumnsFrom;
        var shown = InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed).ToList();
        double height = 0, widest = 0;
        for (int at = 0; at < shown.Count; at++)
        {
            UIElement first = shown[at];
            bool pairs = twoColumns && !GetFullWidth(first) && at + 1 < shown.Count && !GetFullWidth(shown[at + 1]);
            UIElement[] row = pairs ? [first, shown[++at]] : [first];
            double each = pairs ? width / 2 : width;
            double rowHeight = 0;
            foreach (UIElement section in row)
            {
                section.Measure(new Size(each, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, section.DesiredSize.Height);
                widest = Math.Max(widest, section.DesiredSize.Width * row.Length);
            }
            rows.Add((row, rowHeight));
            height += rowHeight;
        }
        // hidden sections are measured too, so they have a size when shown
        foreach (UIElement hidden in InternalChildren.Cast<UIElement>().Where(child => child.Visibility == Visibility.Collapsed)) hidden.Measure(default);
        return new Size(double.IsInfinity(width) ? widest : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double top = 0;
        foreach ((UIElement[] sections, double height) in rows)
        {
            double each = finalSize.Width / sections.Length;
            for (int at = 0; at < sections.Length; at++) sections[at].Arrange(new Rect(at * each, top, each, height));
            top += height;
        }
        return finalSize;
    }
}

/// <summary>
/// A pane's head: its picture (the first child) beside the rest (name, what it is, buttons) when at least
/// <see cref="Threshold"/> wide, the picture at most <see cref="SideWidth"/> wide; under that, everything one under
/// the other. The Division pane's emblem stays beside its name down to 300 pixels, the pictures of the Unit and
/// Weapon panes go beside theirs from 560.
/// </summary>
public sealed class PictureBeside : Panel
{
    /// <summary>The narrowest the head is with the picture beside the rest.</summary>
    public double Threshold { get; set; } = PaneLayout.TwoColumnsFrom;

    /// <summary>The widest the picture's column is, beside the rest; NaN for the picture's own width.</summary>
    public double SideWidth { get; set; } = double.NaN;

    /// <summary>The space between the picture and the rest, beside it.</summary>
    public double Gap { get; set; } = 12;

    /// <summary>Whether the rest is centred top to bottom beside a taller picture (the Division pane's emblem).</summary>
    public bool CentresRest { get; set; }

    private double restHeight;

    private bool beside;
    private double sideWidth;

    protected override Size MeasureOverride(Size available)
    {
        UIElement[] children = [.. InternalChildren.Cast<UIElement>()];
        if (children.Length == 0) return default;
        double width = available.Width;
        beside = !double.IsInfinity(width) && width >= Threshold;
        if (beside)
        {
            double sideRoom = double.IsNaN(SideWidth) ? width / 2 : Math.Min(SideWidth, width * 0.45);
            children[0].Measure(new Size(sideRoom, double.PositiveInfinity));
            sideWidth = double.IsNaN(SideWidth) ? children[0].DesiredSize.Width : sideRoom;
            double restWidth = Math.Max(0, width - sideWidth - Gap);
            restHeight = 0;
            foreach (UIElement child in children.Skip(1))
            {
                child.Measure(new Size(restWidth, double.PositiveInfinity));
                restHeight += child.DesiredSize.Height;
            }
            return new Size(width, Math.Max(children[0].DesiredSize.Height, restHeight));
        }
        double height = 0, widest = 0;
        foreach (UIElement child in children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            height += child.DesiredSize.Height;
            widest = Math.Max(widest, child.DesiredSize.Width);
        }
        return new Size(double.IsInfinity(width) ? widest : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UIElement[] children = [.. InternalChildren.Cast<UIElement>()];
        if (children.Length == 0) return finalSize;
        double top = 0;
        if (beside)
        {
            children[0].Arrange(new Rect(0, 0, sideWidth, children[0].DesiredSize.Height));
            double left = sideWidth + Gap, restWidth = Math.Max(0, finalSize.Width - left);
            if (CentresRest) top = Math.Max(0, (children[0].DesiredSize.Height - restHeight) / 2);
            foreach (UIElement child in children.Skip(1))
            {
                child.Arrange(new Rect(left, top, restWidth, child.DesiredSize.Height));
                top += child.DesiredSize.Height;
            }
            return finalSize;
        }
        foreach (UIElement child in children)
        {
            child.Arrange(new Rect(0, top, finalSize.Width, child.DesiredSize.Height));
            top += child.DesiredSize.Height;
        }
        return finalSize;
    }
}
