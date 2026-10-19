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
/// <param name="Smallest">The narrowest it can be dragged to.</param>
/// <param name="Largest">The widest it can be dragged to.</param>
public sealed record UnitTableColumn(string Key, double Width, Func<Unit, double?>? Number, double Smallest = 56, double Largest = 160)
{
    /// <summary>The columns, in order: who the unit is, then its numbers, each with a small bar.</summary>
    public static IReadOnlyList<UnitTableColumn> All { get; } =
    [
        new("Unit", 220, null, Smallest: 160, Largest: 1000),
        new("Nation", 64, null, Smallest: 44, Largest: 120),
        new("Role", 150, null, Smallest: 80, Largest: 300),
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

    /// <summary>A width within the column's limits.</summary>
    public double Fit(double width) => Math.Clamp(width, Smallest, Largest);

    /// <summary>
    /// The widths of all the columns: each as the user dragged it (by key in <paramref name="chosen"/>), else as at
    /// first. Until the user drags a column, the Unit column also takes any room the others leave in a list this wide.
    /// </summary>
    public static double[] Widths(double listWidth, IReadOnlyDictionary<string, double> chosen)
    {
        double[] widths = [.. All.Select(column => column.Fit(chosen.TryGetValue(column.Key, out double width) ? width : column.Width))];
        // the Unit column fills the room until a column is dragged; from then on every column keeps its own width, and
        // dragging an edge moves only that edge
        if (!chosen.ContainsKey(All[0].Key)) widths[0] = Math.Max(widths[0], listWidth - widths.Skip(1).Sum());
        return widths;
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

    // the cells, made once and filled in for each unit shown: the list reuses rows while it scrolls
    private readonly UnitPicture picture = new() { Width = 54, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock name = new() { FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 14.4, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock nation = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 12.7, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly NationMark nationMark = new("", firstSide: true) { Margin = new Thickness(6, 0, 6, 0) };
    private readonly TextBlock role = new() { Margin = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<(TextBlock Number, BlockMeter Meter)> numbers = [];

    /// <summary>Builds the row's cells; they show a unit when the list gives the row its line.</summary>
    public UnitTableRowView()
    {
        Edges = new Thickness(0, 0, 0, 1);
        SetResourceReference(BottomRightProperty, "line");
        Child = cells;
        Cursor = Cursors.Hand;
        BuildCells();
        DataContextChanged += (_, _) => Show();
        MouseEnter += (_, _) => ShowState();
        MouseLeave += (_, _) => ShowState();
        Loaded += (_, _) => { if (line is not null) line.Selection.Changed += ShowState; ShowState(); };
        Unloaded += (_, _) => { if (line is not null) line.Selection.Changed -= ShowState; };
    }

    /// <summary>The unit shown.</summary>
    public Unit? Unit => line?.Unit;

    /// <summary>
    /// The cells in their columns: the picture with the name and nation, the nation's mark, the role, then each
    /// number over a small six-block bar.
    /// </summary>
    private void BuildCells()
    {
        DockPanel.SetDock(picture, Dock.Left);
        nation.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        Add(0, new DockPanel { Margin = new Thickness(6, 2, 6, 2), Children = { picture, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, nation } } } });
        Add(1, nationMark);
        Add(2, role);
        for (int column = 3; column < UnitTableColumn.All.Count; column++)
        {
            var number = new TextBlock { TextAlignment = TextAlignment.Right, LineHeight = 14.4, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
            var meter = new BlockMeter { Blocks = 6, IsThin = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 1, 0, 0) };
            numbers.Add((number, meter));
            Add(column, new StackPanel { Margin = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center, Children = { number, meter } });
        }
    }

    /// <summary>Shows the unit of the line the list gave the row, in columns as wide as the table's.</summary>
    private void Show()
    {
        if (line is not null && IsLoaded) line.Selection.Changed -= ShowState;
        line = DataContext as TableRowLine;
        if (line is null) return;
        if (IsLoaded) line.Selection.Changed += ShowState;
        SetWidths(line.Widths);
        Unit unit = line.Unit;
        picture.Source = unit.PictureSource;
        picture.Factory = unit.Factory;
        name.Text = unit.Name;
        name.ToolTip = unit.Name;
        nation.Text = UnitNames.Nation(unit.Nation);
        nationMark.Show(unit.Nation, line.Actions.IsFirstSide(unit));
        role.Text = UnitNames.RoleOf(unit);
        role.ToolTip = role.Text;
        for (int column = 3; column < UnitTableColumn.All.Count; column++)
        {
            UnitTableColumn shown = UnitTableColumn.All[column];
            double? value = shown.Number!(unit);
            (TextBlock number, BlockMeter meter) = numbers[column - 3];
            number.Text = UnitNumbers.Of(value);
            meter.Value = value;
            meter.FullBar = line.Most.GetValueOrDefault(shown.Key);
        }
        ShowState();
    }

    /// <summary>Gives the columns the table's widths, changing only those that differ.</summary>
    private void SetWidths(IReadOnlyList<double> widths)
    {
        while (cells.ColumnDefinitions.Count < widths.Count) cells.ColumnDefinitions.Add(new ColumnDefinition());
        for (int column = 0; column < widths.Count; column++)
            if (cells.ColumnDefinitions[column].Width.Value != widths[column]) cells.ColumnDefinitions[column].Width = new GridLength(widths[column]);
    }

    private void Add(int column, FrameworkElement cell)
    {
        Grid.SetColumn(cell, column);
        cells.Children.Add(cell);
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

    /// <summary>Raised while a column's right edge is dragged, with its key and its new width (not yet within its limits).</summary>
    public event Action<string, double>? ColumnResized;

    /// <summary>Raised when a drag of a column's edge starts, so the columns' widths can be fixed where they are.</summary>
    public event Action? ResizeStarted;

    /// <summary>Raised when a drag of a column's edge ends, so its width can be kept.</summary>
    public event Action? ResizeEnded;

    /// <summary>Raised when a column's edge is double-clicked, with its key: the column goes back to its first width.</summary>
    public event Action<string>? ColumnReset;

    /// <summary>The column whose edge is being dragged: its key, where the drag started and the width it had.</summary>
    private (string Key, double StartX, double StartWidth)? resizing;

    /// <summary>Builds empty headings; <see cref="Show"/> fills them.</summary>
    public UnitTableHeadings()
    {
        Edges = new Thickness(0, 0, 0, 1);
        SetResourceReference(BottomRightProperty, "lo");
        SetResourceReference(BackgroundProperty, "panel");
        Child = cells;
    }

    /// <summary>
    /// The columns at new widths while an edge is dragged: the headings stay as they are, so the one being dragged
    /// keeps the mouse.
    /// </summary>
    public void SetWidths(double[] widths)
    {
        for (int column = 0; column < cells.ColumnDefinitions.Count && column < widths.Length; column++)
            cells.ColumnDefinitions[column].Width = new GridLength(widths[column]);
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
            var place = new Grid { Children = { cell, ResizeLine(key, widths[column]) } };
            Grid.SetColumn(place, column);
            cells.Children.Add(place);
        }
    }

    /// <summary>
    /// The strip over the line at a heading's right, 7 pixels wide: dragged, it makes the column wider or narrower;
    /// double-clicked, it puts the column back as at first. It shows in the accent colour under the mouse and while dragged.
    /// </summary>
    private Border ResizeLine(string key, double width)
    {
        var line = new Border { Width = 7, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, Cursor = Cursors.SizeWE, ToolTip = Texts.Get("Units.Table.Resize.Tip") };
        void ShowState()
        {
            if (line.IsMouseOver || line.IsMouseCaptured) line.SetResourceReference(Border.BackgroundProperty, "accent");
            else line.Background = Brushes.Transparent;
            line.Opacity = 0.55;
        }
        line.MouseEnter += (_, _) => ShowState();
        line.MouseLeave += (_, _) => ShowState();
        line.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                ColumnReset?.Invoke(key);
                return;
            }
            ResizeStarted?.Invoke();
            resizing = (key, e.GetPosition(this).X, width);
            line.CaptureMouse();
        };
        line.MouseMove += (_, e) =>
        {
            if (resizing is var (dragged, startX, startWidth)) ColumnResized?.Invoke(dragged, startWidth + e.GetPosition(this).X - startX);
        };
        // a release ends the drag without sorting by the column
        line.MouseLeftButtonUp += (_, e) => { e.Handled = true; line.ReleaseMouseCapture(); };
        line.LostMouseCapture += (_, _) =>
        {
            bool wasResizing = resizing is not null;
            resizing = null;
            if (wasResizing) ResizeEnded?.Invoke();
            ShowState();
        };
        return line;
    }
}
