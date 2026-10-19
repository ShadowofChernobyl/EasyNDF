using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The Division pane's parts that change the division: its details (name, nation, type, points, place in the list,
/// emblem), its activation-point costs, slot by slot, and its standout units. Each change is saved at once in the open
/// mod (see <see cref="IDivisionEditing"/>).
/// </summary>
public static class DivisionDetailsEditor
{
    /// <summary>How wide a box for a whole number is, and a slot's cost box.</summary>
    private const double NumberWidth = 64, SlotWidth = 24;

    // ---- the details ----

    /// <summary>
    /// The details, one a row with its name at the left: the name, nation and type to change, the activation points and
    /// place in the list, the emblem's buttons; then the number of units and the internal names, which only show.
    /// </summary>
    public static StackPanel Details(Division division, IReadOnlyDictionary<string, Unit> units, IDivisionEditing editing)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddRow(grid, "Divisions.Pane.Name", new EditBox(division.Name, double.NaN, typed =>
        {
            if (typed == "") return false;
            editing.Rename(typed);
            return true;
        }));
        AddRow(grid, "Divisions.Pane.Nation", NationChoice(division, editing));
        AddRow(grid, "Divisions.Pane.Type", TypeChoice(division, editing));
        AddRow(grid, "Divisions.Pane.Points", new EditBox(division.ActivationPoints?.ToString(Texts.Catalog.Culture) ?? "", NumberWidth, typed =>
        {
            if (!int.TryParse(typed, NumberStyles.Integer, Texts.Catalog.Culture, out int points) || points < 0) return false;
            editing.ChangeDetails(change => change.ActivationPoints = points);
            return true;
        }) { HorizontalAlignment = HorizontalAlignment.Left });
        AddRow(grid, "Divisions.Pane.ListOrder", new EditBox(division.ListOrder?.ToString("0.##", Texts.Catalog.Culture) ?? "", NumberWidth, typed =>
        {
            if (!double.TryParse(typed, NumberStyles.Float, Texts.Catalog.Culture, out double order)) return false;
            editing.ChangeDetails(change => change.ListOrder = order);
            return true;
        }) { HorizontalAlignment = HorizontalAlignment.Left });
        AddRow(grid, "Divisions.Pane.Emblem", EmblemButtons(division, editing));
        AddRow(grid, "Divisions.Pane.Units", Shown(Texts.Number(division.Units.Count)));
        if (division.CfgName != "") AddRow(grid, "Divisions.Pane.InternalName", Shown(division.CfgName));

        var details = new StackPanel { Children = { grid } };
        // the units its rule names that the source does not have: the game would refuse such a division
        int missing = division.Units.Count(unit => !units.ContainsKey(unit.Unit));
        if (missing > 0) details.Children.Add(UnitPane.Muted(Texts.Count("Divisions.Pane.Missing", missing), new Thickness(0, 4, 0, 0)));
        details.Children.Add(new TextBox { Text = division.Descriptor, IsReadOnly = true, Margin = new Thickness(0, 6, 0, 0), ToolTip = Texts.Get("Divisions.Pane.Descriptor.Tip") });
        return details;
    }

    /// <summary>A row of the details: its name, dim, and what changes it.</summary>
    private static void AddRow(Grid grid, string nameKey, FrameworkElement control)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = new TextBlock { Text = Texts.Get(nameKey), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 10, 2) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        Grid.SetRow(name, row);
        control.Margin = new Thickness(0, 2, 0, 2);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(name);
        grid.Children.Add(control);
    }

    /// <summary>A value that only shows.</summary>
    private static TextBlock Shown(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text };

    /// <summary>The nation, from those of the game's divisions, A to Z; choosing one sets the side its divisions are on.</summary>
    private static ComboBox NationChoice(Division division, IDivisionEditing editing)
    {
        var choice = new ComboBox();
        IEnumerable<(string Country, string Side)> nations = editing.Nations
            .OrderBy(nation => UnitNames.Nation(nation.Country), StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
        foreach ((string country, string side) in nations)
            choice.Items.Add(new ComboBoxItem { Content = UnitNames.Nation(country) + "  ·  " + UnitNames.Side(side), Tag = (country, side) });
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => ((string, string))item.Tag! == (division.Country, division.Side));
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedItem is not ComboBoxItem { Tag: (string country, string side) } || country == division.Country) return;
            editing.ChangeDetails(change => (change.Country, change.Side) = (country, side));
        };
        return choice;
    }

    /// <summary>The type, from the game's.</summary>
    private static ComboBox TypeChoice(Division division, IDivisionEditing editing)
    {
        var choice = new ComboBox();
        foreach (string type in editing.Types.Append(division.TypeToken).Where(type => type != "").Distinct())
            choice.Items.Add(new ComboBoxItem { Content = DivisionNames.Type(type), Tag = type });
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == division.TypeToken);
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedItem is not ComboBoxItem { Tag: string type } || type == division.TypeToken) return;
            editing.ChangeDetails(change => change.TypeToken = type);
        };
        return choice;
    }

    /// <summary>"Game's…" and "Picture…"; with a picture of the user's own, its file's name after them, dim.</summary>
    private static WrapRow EmblemButtons(Division division, IDivisionEditing editing)
    {
        var gameEmblem = new Button { Content = Texts.Get("Divisions.Pane.Emblem.Game"), ToolTip = Texts.Get("Divisions.Pane.Emblem.Game.Tip") };
        gameEmblem.Click += (_, _) => editing.ChooseGameEmblem(gameEmblem);
        var picture = new Button { Content = Texts.Get("Divisions.Pane.Emblem.Picture"), ToolTip = Texts.Get("Divisions.Pane.Emblem.Picture.Tip") };
        picture.Click += (_, _) => editing.ChooseEmblemPicture();
        var row = new WrapRow { Children = { gameEmblem, picture } };
        if (division.EmblemFile is string file) row.Children.Add(UnitPane.Muted(System.IO.Path.GetFileName(file), new Thickness(0)));
        return row;
    }

    // ---- the costs ----

    /// <summary>
    /// Each of the game's categories, in the game's order: its name, a box for each slot's cost, and + and − to add a
    /// slot (costing as the last one) or take the last one away.
    /// </summary>
    public static Grid Costs(Division division, GameTitle game, IDivisionEditing editing)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        IEnumerable<string> categories = UnitNames.FactoriesOf(game).Concat(division.Costs.Keys).Distinct();
        foreach (string category in categories)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = UnitNames.Factory(category), Margin = new Thickness(0, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            Grid.SetRow(name, row);
            WrapRow slots = SlotBoxes(division, category, editing);
            Grid.SetRow(slots, row);
            Grid.SetColumn(slots, 1);
            grid.Children.Add(name);
            grid.Children.Add(slots);
        }
        return grid;
    }

    /// <summary>A category's slots: a box for each cost, then + and −.</summary>
    private static WrapRow SlotBoxes(Division division, string category, IDivisionEditing editing)
    {
        IReadOnlyList<int> costs = division.Costs.GetValueOrDefault(category) ?? [];
        var row = new WrapRow { Margin = new Thickness(0, 2, 0, 2) };
        for (int slot = 0; slot < costs.Count; slot++)
        {
            int changed = slot;
            row.Children.Add(new EditBox(Texts.Number(costs[slot]), SlotWidth, typed =>
            {
                if (!int.TryParse(typed, NumberStyles.Integer, Texts.Catalog.Culture, out int cost) || cost < 0) return false;
                ChangeCosts(division, category, editing, list => list[changed] = cost);
                return true;
            }) { ToolTip = Texts.Get("Divisions.Pane.Slot.Tip", ("slot", Texts.Number(slot + 1))) });
        }
        var add = new Button { Content = "+", Style = (Style)Application.Current.FindResource("HeadingButton"), ToolTip = Texts.Get("Divisions.Pane.AddSlot.Tip") };
        add.Click += (_, _) => ChangeCosts(division, category, editing, list => list.Add(list.Count > 0 ? list[^1] : 1));
        var remove = new Button { Content = "−", Style = (Style)Application.Current.FindResource("HeadingButton"), ToolTip = Texts.Get("Divisions.Pane.RemoveSlot.Tip"), IsEnabled = costs.Count > 0 };
        remove.Click += (_, _) => ChangeCosts(division, category, editing, list => list.RemoveAt(list.Count - 1));
        row.Children.Add(add);
        row.Children.Add(remove);
        return row;
    }

    /// <summary>The division's costs with one category's changed by <paramref name="change"/>, saved as the division's costs.</summary>
    private static void ChangeCosts(Division division, string category, IDivisionEditing editing, Action<List<int>> change)
    {
        Dictionary<string, List<int>> costs = division.Costs.ToDictionary(cost => cost.Key, cost => cost.Value.ToList());
        if (!costs.TryGetValue(category, out List<int>? list)) costs[category] = list = [];
        change(list);
        editing.ChangeDetails(each => each.Costs = costs);
    }

    // ---- the standout units ----

    /// <summary>The standout units as small boxed names, each with × to take it away, then "+ Add…".</summary>
    public static WrapRow StandoutUnits(Division division, IReadOnlyDictionary<string, Unit> units, IDivisionEditing editing)
    {
        var row = new WrapRow();
        foreach (string standout in division.StandoutUnits)
        {
            string taken = standout;
            var name = new TextBlock { Text = units.TryGetValue(standout, out Unit? unit) ? unit.Name : standout, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            var remove = new Button { Content = "×", Style = (Style)Application.Current.FindResource("HeadingButton"), Margin = new Thickness(4, 0, 0, 0), ToolTip = Texts.Get("Divisions.Pane.RemoveStandout.Tip") };
            remove.Click += (_, _) => editing.ChangeDetails(change => change.StandoutUnits = [.. division.StandoutUnits.Where(each => each != taken)]);
            var tag = new Bevel { Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, remove } }, Padding = new Thickness(6, 1, 2, 1) };
            tag.SetResourceReference(Bevel.TopLeftProperty, "line");
            tag.SetResourceReference(Bevel.BottomRightProperty, "line");
            row.Children.Add(tag);
        }
        var add = new Button { Content = Texts.Get("Divisions.Pane.AddStandout"), Style = (Style)Application.Current.FindResource("HeadingButton"), ToolTip = Texts.Get("Divisions.Pane.AddStandout.Tip") };
        add.Click += (_, _) => editing.AddStandoutUnits();
        row.Children.Add(add);
        return row;
    }
}
