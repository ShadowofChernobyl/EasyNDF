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
/// The Division pane's sections: its details to change (name, nation, type, points, place in the list), its standout
/// units, and its files (its names in the game's files, the file it is in, and its units the source does not have).
/// Each change is saved at once in the open mod (see <see cref="IDivisionEditing"/>). Its costs are changed in its unit
/// list (see <see cref="CostButtons"/>), and its emblem from the pane's title.
/// </summary>
public static class DivisionDetailsEditor
{
    /// <summary>How wide a box for a whole number is.</summary>
    private const double NumberWidth = 64;

    // ---- the details ----

    /// <summary>The details, one a row with its name at the left: the name, nation, type, activation points and place in the list.</summary>
    public static Grid Details(Division division, IDivisionEditing editing)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // each value marked when the open mod changed it (DivisionMarks)
        AddRow(grid, "Divisions.Pane.Name", DivisionMarks.Mark(new EditBox(division.Name, double.NaN, typed =>
        {
            if (typed == "") return false;
            return editing.Rename(typed);
        }), ["Name"], each => each.Name));
        AddRow(grid, "Divisions.Pane.Nation", DivisionMarks.Mark(NationChoice(division, editing), ["Nation"], each => UnitNames.Nation(each.Country) + " · " + UnitNames.Side(each.Side)));
        AddRow(grid, "Divisions.Pane.Type", DivisionMarks.Mark(TypeChoice(division, editing), ["Type"], each => DivisionNames.Type(each.TypeToken)));
        AddRow(grid, "Divisions.Pane.Points", DivisionMarks.Mark(new EditBox(Points(division), NumberWidth, typed =>
        {
            if (!int.TryParse(typed, NumberStyles.Integer, Texts.Catalog.Culture, out int points) || points < 0) return false;
            return editing.ChangeDetails(change => change.ActivationPoints = points);
        }) { HorizontalAlignment = HorizontalAlignment.Left }, ["Points"], Points));
        AddRow(grid, "Divisions.Pane.ListOrder", DivisionMarks.Mark(new EditBox(ListOrder(division), NumberWidth, typed =>
        {
            if (DivisionTableColumns.ReadDecimal(typed) is not double order) return false;
            return editing.ChangeDetails(change => change.ListOrder = order);
        }) { HorizontalAlignment = HorizontalAlignment.Left }, ["ListOrder"], ListOrder));
        return grid;
    }

    // ---- the texts ----

    /// <summary>The lines the summary's box and the history's show before they scroll.</summary>
    private const int SummaryLines = 4, HistoryLines = 7;

    /// <summary>
    /// The division's summary and history, as the game's armory shows them, each in a box of its own with how many
    /// characters it has under it. Each is kept in the mod when its box is left, marked when the mod changed it.
    /// </summary>
    public static StackPanel DivisionTexts(Division division, IDivisionEditing editing) => new()
    {
        Children =
        {
            TextPart("Divisions.Pane.Summary", "Divisions.Pane.Summary.Tip", "divisionSummary", division.Summary, SummaryLines, ["Summary"], each => each.Summary,
                typed => editing.ChangeDetails(change => change.Summary = typed)),
            TextPart("Divisions.Pane.History", "Divisions.Pane.History.Tip", "divisionHistory", division.History, HistoryLines, ["History"], each => each.History,
                typed => editing.ChangeDetails(change => change.History = typed)),
        },
    };

    /// <summary>
    /// One text: its name (with "*" in the headings' colour when the mod changed it, its tooltip saying so), a box that
    /// wraps and scrolls past its lines, a grip under it that drags it taller or shorter (as under the Files tab's edit
    /// list), and how many characters it has. A line break would be lost in the game's dictionary, so Enter does not
    /// make one.
    /// </summary>
    /// <param name="sizeKey">The name its height is remembered under, from one session to the next.</param>
    private static StackPanel TextPart(string nameKey, string tipKey, string sizeKey, string text, int lines, string[] changes, Func<Division, string> write, Func<string, bool> keep)
    {
        var name = new TextBlock { Text = Texts.Get(nameKey), ToolTip = Texts.Get(tipKey), Margin = new Thickness(0, 4, 0, 2) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DivisionMarks.MarkText(name, changes, write);
        var box = new TextBox
        {
            Text = text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, MinLines = lines, MaxLines = lines,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, ToolTip = name.ToolTip,
        };
        ResizeGrip grip = GripFor(box, sizeKey, lines);
        var count = new TextBlock { FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
        count.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        void ShowCount() => count.Text = Texts.Count("Divisions.Pane.Characters", box.Text.Length);
        ShowCount();
        box.TextChanged += (_, _) => ShowCount();
        box.LostKeyboardFocus += (_, _) =>
        {
            if (box.Text == text) return;
            // a refused change puts the text back, so the box never shows what was not saved
            if (keep(box.Text)) text = box.Text;
            else box.Text = text;
        };
        return new StackPanel { Children = { name, box, grip, count } };
    }

    /// <summary>The lowest height a text box can be dragged to: about two lines.</summary>
    private const double LowestTextHeight = 40;

    /// <summary>
    /// The grip under a text box: dragging it sets the box's height, which is kept for the next time the pane is drawn;
    /// a double-click gives the box back its own height.
    /// </summary>
    /// <param name="lines">How many lines the box shows at its own height.</param>
    private static ResizeGrip GripFor(TextBox box, string sizeKey, int lines)
    {
        ShowHeight(box, RememberedHeight(sizeKey), lines);
        var grip = new ResizeGrip { Target = box, ToolTip = Texts.Get("Common.ResizeGrip.Tip") };
        double? chosen = RememberedHeight(sizeKey);
        grip.HeightChosen += height =>
        {
            chosen = height is double wanted ? Math.Max(LowestTextHeight, wanted) : null;
            ShowHeight(box, chosen, lines);
        };
        grip.HeightSettled += () => Pages.FilesPage.RememberSize(sizeKey, chosen);
        return grip;
    }

    /// <summary>A text box at a height dragged to, or, with none, at its own number of lines.</summary>
    private static void ShowHeight(TextBox box, double? height, int lines)
    {
        box.Height = height ?? double.NaN;
        // a set height takes over from the lines the box starts with
        box.MinLines = height is null ? lines : 1;
        box.MaxLines = height is null ? lines : int.MaxValue;
    }

    /// <summary>The height a text box was dragged to, kept with the app's other remembered sizes; null for its own.</summary>
    private static double? RememberedHeight(string sizeKey) =>
        AppData.Current.State["sizes"]?[sizeKey] is System.Text.Json.Nodes.JsonValue value && value.TryGetValue(out double height) ? height : null;

    /// <summary>A division's activation points as its box shows them.</summary>
    private static string Points(Division division) => division.ActivationPoints?.ToString(Texts.Catalog.Culture) ?? "";

    /// <summary>A division's place in the list as its box shows it.</summary>
    private static string ListOrder(Division division) => division.ListOrder?.ToString("0.##", Texts.Catalog.Culture) ?? "";

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
            // a refused change puts the division's own nation back, so the dropdown never shows what was not saved
            if (!editing.ChangeDetails(change => (change.Country, change.Side) = (country, side)))
                choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => ((string, string))item.Tag! == (division.Country, division.Side));
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
            if (!editing.ChangeDetails(change => change.TypeToken = type))
                choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == division.TypeToken);
        };
        return choice;
    }

    // ---- the standout units ----

    /// <summary>
    /// The standout units, one a row: the unit's picture, its name and type, and × to take it away; then a dashed
    /// "+ Add standout unit" the width of the pane.
    /// </summary>
    public static StackPanel StandoutUnits(Division division, IReadOnlyDictionary<string, Unit> units, IDivisionEditing editing)
    {
        var list = new StackPanel();
        foreach (string standout in division.StandoutUnits)
        {
            string taken = standout;
            units.TryGetValue(standout, out Unit? unit);
            var remove = new Button { Content = "×", Style = (Style)Application.Current.FindResource("HeadingButton"), ToolTip = Texts.Get("Divisions.Pane.RemoveStandout.Tip"), VerticalAlignment = VerticalAlignment.Center };
            remove.Click += (_, _) => editing.ChangeDetails(change => change.StandoutUnits = [.. division.StandoutUnits.Where(each => each != taken)]);
            DockPanel.SetDock(remove, Dock.Right);
            var picture = new UnitPicture { Width = 40, Source = unit?.PictureSource, Factory = unit?.Factory ?? "", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(picture, Dock.Left);
            var name = new TextBlock { Text = unit?.Name ?? standout, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = standout };
            // a standout unit the open mod adds is marked; the tooltip gives the game's list
            DivisionMarks.MarkText(name, ["Standout"], each => each.StandoutUnits.Contains(taken) ? unit?.Name ?? taken : "");
            var type = new TextBlock { Text = unit is null ? "" : UnitNames.Factory(unit.Factory), FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            type.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            DockPanel.SetDock(name, Dock.Left);
            var row = new Bevel { Child = new DockPanel { Children = { remove, picture, name, type } }, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 4) };
            row.SetResourceReference(Bevel.TopLeftProperty, "line");
            row.SetResourceReference(Bevel.BottomRightProperty, "line");
            row.SetResourceReference(Bevel.BackgroundProperty, "panel");
            list.Children.Add(row);
        }
        var add = new Button { Content = Texts.Get("Divisions.Pane.AddStandout"), Style = (Style)Application.Current.FindResource("DashedButton"), ToolTip = Texts.Get("Divisions.Pane.AddStandout.Tip") };
        add.Click += (_, _) => editing.AddStandoutUnits();
        list.Children.Add(add);
        return list;
    }

    // ---- the files ----

    /// <summary>
    /// Where the division is in the game's files: its internal name, its descriptor, the file it is in (with Open, to
    /// show it in the File Editor), and the units its rule names that the source does not have (the game would refuse
    /// such a division). Each name is in a box of its own, under its label, with Copy at the label's right.
    /// </summary>
    public static StackPanel Files(Division division, IReadOnlyDictionary<string, Unit> units, IDivisionEditing editing)
    {
        var files = new StackPanel();
        if (division.CfgName != "") files.Children.Add(NamedValue(Texts.Get("Divisions.Pane.InternalName"), division.CfgName));
        files.Children.Add(NamedValue(Texts.Get("Divisions.Pane.Descriptor"), division.Descriptor));
        var open = new Button
        {
            Content = Texts.Get("Divisions.Pane.Open"), Style = (Style)Application.Current.FindResource("HeadingButton"), IsEnabled = editing.CanShowDivisionInEditor,
            ToolTip = editing.CanShowDivisionInEditor ? Texts.Get("Divisions.Pane.Open.Tip") : Texts.Get("Units.Menu.NotLoaded.Tip", ("file", UnitFiles.Divisions)),
        };
        ToolTipService.SetShowOnDisabled(open, true);
        open.Click += (_, _) => editing.ShowDivisionInEditor();
        files.Children.Add(NamedValue(Texts.Get("Divisions.Pane.File"), UnitFiles.DivisionsPath, open));
        List<string> missing = [.. division.Units.Select(unit => unit.Unit).Where(unit => !units.ContainsKey(unit))];
        if (missing.Count == 0) return files;
        var warning = new TextBlock { Text = "⚠ " + Texts.Count("Divisions.Pane.Missing", missing.Count), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "caution");
        files.Children.Add(warning);
        foreach (string unit in missing) files.Children.Add(NamedValue("", unit));
        return files;
    }

    /// <summary>
    /// A name the user may want to copy: its label, dim, with Copy (and any other button) at the right, then the name
    /// in a read-only box the width of the pane, broken over lines when long.
    /// </summary>
    private static StackPanel NamedValue(string label, string value, Button? extra = null)
    {
        var copy = new Button
        {
            Content = CopyIcon(), Style = (Style)Application.Current.FindResource("HeadingButton"), Padding = new Thickness(4, 0, 4, 0),
            ToolTip = Texts.Get("Divisions.Pane.Copy.Tip"),
        };
        System.Windows.Automation.AutomationProperties.SetName(copy, Texts.Get("Divisions.Pane.Copy"));
        copy.Click += (_, _) => CopyText(value);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { copy } };
        if (extra is not null)
        {
            extra.Margin = new Thickness(0, 0, 4, 0);
            buttons.Children.Insert(0, extra);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        var name = new TextBlock { Text = label, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Bottom };
        name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 2), Children = { buttons, name } };
        var box = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        box.SetResourceReference(FrameworkElement.StyleProperty, "MonoBox");
        return new StackPanel { Margin = new Thickness(0, 0, 0, 8), Children = { line, box } };
    }

    /// <summary>
    /// The copy icon: two squares, the front one lower left over the back one's corner. Only the part of the back one
    /// not behind the front one is drawn, so the icon needs no fill and looks right on any button colour.
    /// </summary>
    private static System.Windows.Shapes.Path CopyIcon()
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse("M 4.5,4.5 V 0.5 H 12.5 V 8.5 H 8.5 M 0.5,4.5 H 8.5 V 12.5 H 0.5 Z"),
            StrokeThickness = 1.2, Width = 13, Height = 13, SnapsToDevicePixels = true, VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ink");
        return icon;
    }

    /// <summary>Puts text on the clipboard; another program holding the clipboard just leaves it as it was.</summary>
    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // the clipboard was busy: nothing is copied, as when a copy in another program fails
        }
    }
}
