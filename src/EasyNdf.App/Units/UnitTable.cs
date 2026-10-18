using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>A column of the Table view: its key, its width, and for a number column how to read the number.</summary>
/// <param name="Key">The column's key, which names its heading in the texts ("Units.Table.Cost") and its sort.</param>
/// <param name="Width">Its width; the Unit column takes the room left, with this at the least.</param>
/// <param name="Number">How to read a unit's number; null for a column of words.</param>
public sealed record UnitTableColumn(string Key, double Width, Func<Unit, double?>? Number)
{
    /// <summary>The columns, in order: who the unit is, then its numbers, each with a small bar.</summary>
    public static IReadOnlyList<UnitTableColumn> All { get; } =
    [
        new("Unit", 220, null),
        new("Nation", 64, null),
        new("Role", 150, null),
        new("Cost", 76, unit => unit.Cost),
        new("Strength", 76, unit => unit.Strength),
        new("FrontArmor", 92, unit => unit.Armor.Front),
        new("Speed", 76, unit => unit.Speed),
        new("Optics", 76, unit => unit.Optics),
        new("Range", 96, unit => unit.LongestRange),
        new("Weapons", 80, unit => unit.Weapons.Count),
    ];

    /// <summary>Whether it is a number column, right-aligned with a bar.</summary>
    public bool IsNumber => Number is not null;

    /// <summary>Its heading: "Cost", "HP", "Range (GRU)"...</summary>
    public string Heading(GameTitle game) => Key switch
    {
        "Unit" => Texts.Get("Units.Table.Unit"),
        "Nation" => Texts.Get("Units.Table.Nation"),
        "Role" => Texts.Get("Units.Table.Role"),
        "Cost" => Texts.Get("Units.Stat.Cost"),
        "Strength" => Texts.Get("Units.Table.Strength"),
        "FrontArmor" => Texts.Get("Units.Stat.FrontArmor"),
        "Speed" => Texts.Get("Units.Stat.Speed"),
        "Optics" => Texts.Get("Units.Stat.Optics"),
        "Range" => Texts.Get("Units.Weapons.RangeIn", ("unit", game.RangeUnit)),
        _ => Texts.Get("Units.Weapons"),
    };

    /// <summary>The widths of all the columns, the Unit column given the room the others leave in a list this wide.</summary>
    public static double[] Widths(double listWidth)
    {
        double others = All.Skip(1).Sum(column => column.Width);
        return [Math.Max(All[0].Width, listWidth - others), .. All.Skip(1).Select(column => column.Width)];
    }

    /// <summary>
    /// Sorts units by a column: words A to Z, numbers biggest first, or the other way when <paramref name="ascending"/>
    /// is the other way round. Units without the number go last either way.
    /// </summary>
    public List<Unit> Sort(IEnumerable<Unit> units, bool ascending)
    {
        if (Number is Func<Unit, double?> read)
        {
            List<Unit> withNumber = units.Where(unit => read(unit) is not null).ToList();
            List<Unit> sorted = ascending ? [.. withNumber.OrderBy(unit => read(unit))] : [.. withNumber.OrderByDescending(unit => read(unit))];
            sorted.AddRange(units.Where(unit => read(unit) is null));
            return sorted;
        }
        Func<Unit, string> word = Key switch
        {
            "Nation" => unit => UnitNames.Nation(unit.Nation),
            "Role" => UnitNames.RoleOf,
            _ => unit => unit.Name,
        };
        StringComparer byName = StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true);
        return ascending ? [.. units.OrderBy(word, byName)] : [.. units.OrderByDescending(word, byName)];
    }
}

/// <summary>A row of the Table view.</summary>
/// <param name="Unit">The unit.</param>
/// <param name="Widths">The columns' widths.</param>
/// <param name="Most">Each number column's biggest number among the units listed, which fills its bar.</param>
/// <param name="Selection">Which unit is selected.</param>
/// <param name="Actions">The tab the row is on.</param>
public sealed record TableRowLine(Unit Unit, double[] Widths, IReadOnlyDictionary<string, double> Most, UnitSelection Selection, IUnitActions Actions);

/// <summary>
/// A unit's row in the Table view, about 38 pixels high: its picture with its name and nation, its nation's mark, its
/// role, then its numbers, each over a small six-block bar. Under the mouse it takes the selection colour; selected,
/// the hover colour.
/// </summary>
public sealed class UnitTableRowView : Bevel
{
    private readonly Grid cells = new();
    private TableRowLine? line;

    /// <summary>Builds the row's cells when the list gives it its line.</summary>
    public UnitTableRowView()
    {
        Edges = new Thickness(0, 0, 0, 1);
        SetResourceReference(BottomRightProperty, "line");
        Child = cells;
        Cursor = Cursors.Hand;
        DataContextChanged += (_, _) => Build();
        MouseEnter += (_, _) => ShowState();
        MouseLeave += (_, _) => ShowState();
        Loaded += (_, _) => { if (line is not null) line.Selection.Changed += ShowState; ShowState(); };
        Unloaded += (_, _) => { if (line is not null) line.Selection.Changed -= ShowState; };
    }

    /// <summary>The unit shown.</summary>
    public Unit? Unit => line?.Unit;

    private void Build()
    {
        if (line is not null && IsLoaded) line.Selection.Changed -= ShowState;
        cells.Children.Clear();
        cells.ColumnDefinitions.Clear();
        line = DataContext as TableRowLine;
        if (line is null) return;
        if (IsLoaded) line.Selection.Changed += ShowState;
        foreach (double width in line.Widths) cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        Unit unit = line.Unit;
        Add(0, WhoCell(unit));
        Add(1, new NationMark(unit.Nation, line.Actions.IsFirstSide(unit)) { Margin = new Thickness(6, 0, 6, 0) });
        Add(2, Words(UnitNames.RoleOf(unit)));
        for (int column = 3; column < UnitTableColumn.All.Count; column++)
        {
            UnitTableColumn number = UnitTableColumn.All[column];
            Add(column, NumberCell(number.Number!(unit), line.Most.GetValueOrDefault(number.Key)));
        }
        ShowState();
    }

    private void Add(int column, FrameworkElement cell)
    {
        Grid.SetColumn(cell, column);
        cells.Children.Add(cell);
    }

    /// <summary>The picture, 54 pixels wide, with the name in bold and the nation under it, dim.</summary>
    private static DockPanel WhoCell(Unit unit)
    {
        var picture = new UnitPicture { Source = unit.PictureSource, Factory = unit.Factory, Width = 54, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(picture, Dock.Left);
        var name = new TextBlock { Text = unit.Name, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 14.4, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, ToolTip = unit.Name };
        var nation = new TextBlock { Text = UnitNames.Nation(unit.Nation), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 12.7, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        nation.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return new DockPanel { Margin = new Thickness(6, 2, 6, 2), Children = { picture, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, nation } } } };
    }

    private static TextBlock Words(string text) => new()
    {
        Text = text, Margin = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text,
    };

    /// <summary>A number, right-aligned, over a thin six-block bar filled as far as the biggest number listed.</summary>
    private static StackPanel NumberCell(double? value, double most)
    {
        var number = new TextBlock { Text = UnitNumbers.Of(value), TextAlignment = TextAlignment.Right, LineHeight = 14.4, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        var meter = new BlockMeter { Value = value, FullBar = most, Blocks = 6, IsThin = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 1, 0, 0) };
        return new StackPanel { Margin = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center, Children = { number, meter } };
    }

    /// <summary>The hover colour when selected, the selection colour under the mouse, else none.</summary>
    private void ShowState()
    {
        if (line is not null && ReferenceEquals(line.Selection.Selected, line.Unit)) SetResourceReference(BackgroundProperty, "hover");
        else if (IsMouseOver) SetResourceReference(BackgroundProperty, "sel");
        else Background = Brushes.Transparent;
    }
}

/// <summary>
/// The Table view's headings, above its rows: dim on the panel colour, a line between them and a dark line under
/// them. A click sorts by the column; the column sorted by is in the text colour, with ▼ or ▲ after it.
/// </summary>
public sealed class UnitTableHeadings : Bevel
{
    private readonly Grid cells = new();

    /// <summary>Raised when a heading is clicked, with its column's key.</summary>
    public event Action<string>? SortClicked;

    /// <summary>Builds empty headings; <see cref="Show"/> fills them.</summary>
    public UnitTableHeadings()
    {
        Edges = new Thickness(0, 0, 0, 1);
        SetResourceReference(BottomRightProperty, "lo");
        SetResourceReference(BackgroundProperty, "panel");
        Child = cells;
    }

    /// <summary>Shows the headings at the columns' widths, marking the one sorted by.</summary>
    public void Show(GameTitle game, double[] widths, string sortKey, bool ascending)
    {
        cells.Children.Clear();
        cells.ColumnDefinitions.Clear();
        for (int column = 0; column < UnitTableColumn.All.Count; column++)
        {
            UnitTableColumn heading = UnitTableColumn.All[column];
            cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[column]) });
            bool sorted = heading.Key == sortKey;
            var text = new TextBlock { Text = heading.Heading(game), TextTrimming = TextTrimming.CharacterEllipsis };
            text.SetResourceReference(TextBlock.ForegroundProperty, sorted ? "ink" : "muted");
            var row = new DockPanel { HorizontalAlignment = heading.IsNumber ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            if (sorted)
            {
                var arrow = new TextBlock { Text = ascending ? " ▲" : " ▼", FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                arrow.SetResourceReference(TextBlock.ForegroundProperty, "accent");
                DockPanel.SetDock(arrow, Dock.Right);
                row.Children.Add(arrow);
            }
            row.Children.Add(text);
            var cell = new Bevel { Child = row, Padding = new Thickness(7, 3, 7, 3), Edges = new Thickness(0, 0, 1, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = Texts.Get("Units.Table.Sort.Tip") };
            cell.SetResourceReference(BottomRightProperty, "line");
            string key = heading.Key;
            cell.MouseLeftButtonUp += (_, _) => SortClicked?.Invoke(key);
            Grid.SetColumn(cell, column);
            cells.Children.Add(cell);
        }
    }
}
