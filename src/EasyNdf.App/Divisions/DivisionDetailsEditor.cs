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
        // in a narrow pane each name sits over its value
        var grid = new NameValueGrid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // each value marked when the open mod changed it (DivisionMarks); one the game's divisions lack, or the Armory
        // does not write for the game, switched off with the reason
        AddRow(grid, "Divisions.Pane.Name", DivisionMarks.Mark(OffUnless(new EditBox(division.Name, double.NaN, typed =>
        {
            if (typed == "") return false;
            return editing.Rename(typed);
        }), editing, DivisionDetail.Name), ["Name"], each => each.Name));
        AddRow(grid, "Divisions.Pane.Nation", DivisionMarks.Mark(OffUnless(NationChoice(division, editing), editing, DivisionDetail.Nation),
            ["Nation"], each => UnitNames.Nation(each.Country) + " · " + UnitNames.Side(each.Side)));
        AddRow(grid, "Divisions.Pane.Type", DivisionMarks.Mark(OffUnless(TypeChoice(division, editing), editing, DivisionDetail.Type), ["Type"], each => DivisionNames.Type(each.TypeToken)));
        AddRow(grid, "Divisions.Pane.Tags", DivisionMarks.Mark(OffUnless(new EditBox(TagsText(division.Tags), double.NaN, typed =>
        {
            List<string> tags = [.. typed.Split(',').Select(tag => tag.Trim()).Where(tag => tag != "")];
            return editing.ChangeDetails(change => change.Tags = tags);
        }) { ToolTip = Texts.Get("Divisions.Pane.Tags.Tip") }, editing, DivisionDetail.Tags), ["Tags"], each => TagsText(each.Tags)));
        AddRow(grid, "Divisions.Pane.Points", DivisionMarks.Mark(OffUnless(new EditBox(Points(division), NumberWidth, typed =>
        {
            if (!int.TryParse(typed, NumberStyles.Integer, Texts.Catalog.Culture, out int points) || points < 0) return false;
            return editing.ChangeDetails(change => change.ActivationPoints = points);
        }) { HorizontalAlignment = HorizontalAlignment.Left }, editing, DivisionDetail.Points), ["Points"], Points));
        AddRow(grid, "Divisions.Pane.ListOrder", DivisionMarks.Mark(OffUnless(new EditBox(ListOrder(division), NumberWidth, typed =>
        {
            if (DivisionTableColumns.ReadDecimal(typed) is not double order) return false;
            return editing.ChangeDetails(change => change.ListOrder = order);
        }) { HorizontalAlignment = HorizontalAlignment.Left }, editing, DivisionDetail.ListOrder), ["ListOrder"], ListOrder));
        return grid;
    }

    // ---- Steel Division 2's phases ----

    /// <summary>
    /// Steel Division 2's phases and income choices: a small table, a row for each phase with when it starts (in
    /// seconds), its income and its bonus, each in a box kept when it is left, as the details are; then the income
    /// choices. The section is the pane's width, as three boxes in a row do not fit beside the details' names.
    /// </summary>
    public static StackPanel Phases(Division division, IDivisionEditing editing)
    {
        var grid = new Grid { ToolTip = Texts.Get("Divisions.Pane.Phase.Tip") };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int column = 0; column < 3; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] headings = [Texts.Get("Divisions.Pane.Phase"), Texts.Get("Divisions.Pane.Phase.Starts"), Texts.Get("Divisions.Pane.Phase.Income"), Texts.Get("Divisions.Pane.Phase.Bonus")];
        for (int column = 0; column < headings.Length; column++) Place(grid, MutedText(headings[column]), 0, column);
        for (int phase = 0; phase < division.Phases.Count; phase++)
        {
            int at = phase;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(grid, DivisionMarks.Mark(MutedText(PhaseLetter(at)), ["Phases"], each => PhaseText(each, at)), at + 1, 0);
            Place(grid, OffUnless(PhaseBox(division, at, division.Phases[at].Start, (each, number) => each with { Start = number }, editing), editing, DivisionDetail.Phases), at + 1, 1);
            Place(grid, OffUnless(PhaseBox(division, at, division.Phases[at].Income, (each, number) => each with { Income = number }, editing), editing, DivisionDetail.Phases), at + 1, 2);
            Place(grid, OffUnless(PhaseBox(division, at, division.Phases[at].Bonus, (each, number) => each with { Bonus = number }, editing), editing, DivisionDetail.Phases), at + 1, 3);
        }
        var section = new StackPanel { Children = { grid } };
        if (division.Economy == "") return section;
        // the income choices on a line of their own under the table, so their name does not widen its first column
        var name = MutedText(Texts.Get("Divisions.Pane.Economy"));
        name.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(name, Dock.Left);
        FrameworkElement choice = DivisionMarks.Mark(OffUnless(EconomyChoice(division, editing), editing, DivisionDetail.Economy), ["Economy"], each => EconomyName(each.Economy));
        section.Children.Add(new DockPanel { Margin = new Thickness(0, 4, 0, 2), Children = { name, choice } });
        return section;
    }

    /// <summary>Puts a part in a cell of a grid, with a little room around it.</summary>
    private static void Place(Grid grid, FrameworkElement part, int row, int column)
    {
        part.Margin = new Thickness(column == 0 ? 0 : 3, 2, column == 0 ? 8 : 0, 2);
        Grid.SetRow(part, row);
        Grid.SetColumn(part, column);
        grid.Children.Add(part);
    }

    /// <summary>A dim name in the phases' table.</summary>
    private static TextBlock MutedText(string text)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return block;
    }

    /// <summary>A phase's letter, as the game names them: A, B, C.</summary>
    private static string PhaseLetter(int phase) => ((char)('A' + phase)).ToString();

    /// <summary>A phase of a division as its row shows it, to compare with the game's: "600 s · 140 · +0".</summary>
    private static string PhaseText(Division division, int phase) =>
        phase < division.Phases.Count ? Texts.Number(division.Phases[phase].Start) + " s · " + Texts.Number(division.Phases[phase].Income) + " · +" + Texts.Number(division.Phases[phase].Bonus) : "";

    /// <summary>
    /// A box for one value of one phase: the division's phases with that value changed are kept in the mod when it is
    /// left; a value that is not a whole number of 0 or more is refused, the box showing the value kept.
    /// </summary>
    private static EditBox PhaseBox(Division division, int phase, int value, Func<DivisionPhase, int, DivisionPhase> with, IDivisionEditing editing) =>
        new(Texts.Number(value), double.NaN, typed =>
        {
            if (!int.TryParse(typed, NumberStyles.Integer, Texts.Catalog.Culture, out int number) || number < 0) return false;
            List<DivisionPhase> phases = [.. division.Phases];
            phases[phase] = with(phases[phase], number);
            return editing.ChangeDetails(change => change.Phases = phases);
        });

    /// <summary>An income choices list's name without the game's prefix: "DivisionEconomy_German" is "German".</summary>
    public static string EconomyName(string economy) =>
        economy.StartsWith("DivisionEconomy_", StringComparison.Ordinal) ? economy["DivisionEconomy_".Length..].TrimEnd('_') : economy;

    /// <summary>The income choices, from the lists the game's divisions use.</summary>
    private static ComboBox EconomyChoice(Division division, IDivisionEditing editing)
    {
        var choice = new ComboBox { ToolTip = Texts.Get("Divisions.Pane.Economy.Tip") };
        foreach (string economy in editing.Economies.Append(division.Economy).Distinct())
            choice.Items.Add(new ComboBoxItem { Content = EconomyName(economy), Tag = economy, ToolTip = economy });
        ComboBoxItem? Shown() => choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == division.Economy);
        choice.SelectedItem = Shown();
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedItem is not ComboBoxItem { Tag: string economy } || economy == division.Economy) return;
            // a refused change puts the division's own back, so the dropdown never shows what was not saved
            if (!editing.ChangeDetails(change => change.Economy = economy)) choice.SelectedItem = Shown();
        };
        return choice;
    }

    /// <summary>
    /// A control that changes one of the division's details, switched off when the detail cannot be changed, its
    /// tooltip then saying why (<see cref="IDivisionEditing.WhyDetailCannotChange"/>).
    /// </summary>
    public static T OffUnless<T>(T control, IDivisionEditing editing, string detail) where T : FrameworkElement
    {
        if (editing.WhyDetailCannotChange(detail) is not string why) return control;
        control.IsEnabled = false;
        OffReason.SetText(control, why);
        return control;
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
                typed => editing.ChangeDetails(change => change.Summary = typed), editing.WhyDetailCannotChange(DivisionDetail.Summary)),
            TextPart("Divisions.Pane.History", "Divisions.Pane.History.Tip", "divisionHistory", division.History, HistoryLines, ["History"], each => each.History,
                typed => editing.ChangeDetails(change => change.History = typed), editing.WhyDetailCannotChange(DivisionDetail.History)),
        },
    };

    /// <summary>
    /// One text: its name (with "*" in the headings' colour when the mod changed it, its tooltip saying so), a box that
    /// wraps and scrolls past its lines, a grip under it that drags it taller or shorter (as under the Files tab's edit
    /// list), and how many characters it has. A line break would be lost in the game's dictionary, so Enter does not
    /// make one. A text that cannot be changed is shown in a box switched off, its tooltip saying why.
    /// Folded at first (Paul's choice, option A of the mock): ▸ and its name, its count at the right, and its first line
    /// cut short with …, so the pane keeps its height while it is resized; a click on the line or the name opens the box,
    /// ready to type, and ▾ folds it again. Each stays as it is left while the app is open.
    /// </summary>
    /// <param name="sizeKey">The name its height is remembered under, from one session to the next.</param>
    /// <param name="whyOff">Why the text cannot be changed; null when it can.</param>
    private static StackPanel TextPart(string nameKey, string tipKey, string sizeKey, string text, int lines, string[] changes, Func<Division, string> write, Func<string, bool> keep,
        string? whyOff)
    {
        var name = new TextBlock { Text = Texts.Get(nameKey), ToolTip = Texts.Get(tipKey), Margin = new Thickness(0, 4, 0, 2) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DivisionMarks.MarkText(name, changes, write);
        var box = new TextBox
        {
            Text = text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, MinLines = lines, MaxLines = lines,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, ToolTip = name.ToolTip,
        };
        if (whyOff is not null)
        {
            box.IsEnabled = false;
            OffReason.SetText(box, whyOff);
        }
        ResizeGrip grip = GripFor(box, sizeKey, lines);
        var count = new TextBlock { FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
        count.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var foldedCount = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        foldedCount.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(foldedCount, Dock.Right);
        void ShowCount() => count.Text = foldedCount.Text = Texts.Count("Divisions.Pane.Characters", box.Text.Length);
        ShowCount();
        box.TextChanged += (_, _) => ShowCount();
        box.LostKeyboardFocus += (_, _) =>
        {
            if (box.Text == text) return;
            // a refused change puts the text back, so the box never shows what was not saved
            if (keep(box.Text)) text = box.Text;
            else box.Text = text;
        };

        // folded: its first line, cut short, in a sunken line a click opens
        var firstLine = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        firstLine.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var peek = new Bevel { Child = firstLine, Padding = new Thickness(6, 3, 6, 3), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = name.ToolTip };
        peek.SetResourceReference(Bevel.TopLeftProperty, "lo");
        peek.SetResourceReference(Bevel.BottomRightProperty, "hi");
        peek.SetResourceReference(Bevel.BackgroundProperty, "well");
        peek.MouseEnter += (_, _) => peek.SetResourceReference(Bevel.TopLeftProperty, "accent");
        peek.MouseLeave += (_, _) => peek.SetResourceReference(Bevel.TopLeftProperty, "lo");
        var arrow = new TextBlock { Width = 12, VerticalAlignment = VerticalAlignment.Center, FontSize = 10 };
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        name.Margin = new Thickness(0);
        var heading = new DockPanel { Margin = new Thickness(0, 4, 0, 2), Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Children = { foldedCount, arrow, name } };
        DockPanel.SetDock(arrow, Dock.Left);
        var open = new StackPanel { Children = { box, grip, count } };
        void Show(bool isOpen, bool focus)
        {
            OpenTexts[sizeKey] = isOpen;
            arrow.Text = isOpen ? "▾" : "▸";
            firstLine.Text = box.Text == "" ? Texts.Get("Divisions.Pane.Text.Empty") : box.Text;
            open.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            peek.Visibility = foldedCount.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
            if (isOpen && focus && box.IsEnabled) box.Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.CaretIndex = box.Text.Length;
            });
        }
        heading.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Show(!OpenTexts.GetValueOrDefault(sizeKey), focus: true);
        };
        peek.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Show(true, focus: true);
        };
        Show(OpenTexts.GetValueOrDefault(sizeKey), focus: false);
        return new StackPanel { Children = { heading, peek, open } };
    }

    /// <summary>The texts opened while the app runs, by name; folded at first.</summary>
    private static readonly Dictionary<string, bool> OpenTexts = [];

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

    /// <summary>A division's tags as their box shows them: "DEFAULT, US, NATO, ARMORED".</summary>
    private static string TagsText(IEnumerable<string> tags) => string.Join(", ", tags);

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

    /// <summary>
    /// The nation, from those of the game's divisions, A to Z, the division's own among them; choosing one sets the side
    /// its divisions are on. Steel Division 2's German divisions have no nation code of their own (no CountryId), so a
    /// generated German division, of its units' nation (GER), would otherwise show none.
    /// </summary>
    private static ComboBox NationChoice(Division division, IDivisionEditing editing)
    {
        var choice = new ComboBox();
        IEnumerable<(string Country, string Side)> own = division.Country != "" ? [(division.Country, division.Side)] : [];
        IEnumerable<(string Country, string Side)> nations = editing.Nations.Concat(own).Distinct()
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
            System.Windows.Automation.AutomationProperties.SetName(remove, Texts.Get("Divisions.Pane.RemoveStandout.Tip"));
            remove.Click += (_, _) => editing.ChangeDetails(change => change.StandoutUnits = [.. division.StandoutUnits.Where(each => each != taken)]);
            OffUnless(remove, editing, DivisionDetail.Standout);
            DockPanel.SetDock(remove, Dock.Right);
            var picture = new UnitPicture { Width = 40, Source = unit?.PictureSource, Factory = unit?.Factory ?? "", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(picture, Dock.Left);
            // the whole name first, as it is cut short in a narrow pane, then the descriptor
            var name = new TextBlock { Text = unit?.Name ?? standout, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = unit is null ? standout : unit.Name + "\n" + standout };
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
        list.Children.Add(OffUnless(add, editing, DivisionDetail.Standout));
        return list;
    }

    // ---- the files ----

    /// <summary>
    /// Where the division is in the game's files: its internal name, its descriptor and the file it is in, as the Unit
    /// pane shows a unit's (<see cref="FileValueRows"/>: a click shows the descriptor or file in the File Editor, and
    /// copies the internal name), and the units its rule names that the source does not have (the game would refuse
    /// such a division), which a click copies.
    /// </summary>
    public static StackPanel Files(Division division, IReadOnlyDictionary<string, Unit> units, IDivisionEditing editing)
    {
        List<FileValue> values = [];
        if (division.CfgName != "") values.Add(new(Texts.Get("Divisions.Pane.InternalName"), division.CfgName));
        values.Add(new(Texts.Get("Divisions.Pane.Descriptor"), division.Descriptor, editing.ShowDivisionInEditor, editing.CanShowDivisionInEditor, UnitFiles.Divisions));
        values.Add(new(Texts.Get("Divisions.Pane.File"), UnitFiles.DivisionsPath, editing.ShowDivisionInEditor, editing.CanShowDivisionInEditor, UnitFiles.Divisions));
        var files = new StackPanel { Children = { FileValueRows.Build(values) } };
        List<string> missing = [.. division.Units.Select(unit => unit.Unit).Where(unit => !units.ContainsKey(unit))];
        if (missing.Count == 0) return files;
        var warning = new TextBlock { Text = "⚠ " + Texts.Count("Divisions.Pane.Missing", missing.Count), FontSize = 11.5, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "caution");
        files.Children.Add(warning);
        files.Children.Add(FileValueRows.Build(missing.Select(unit => new FileValue("", unit))));
        return files;
    }
}
