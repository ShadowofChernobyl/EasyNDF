using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// The Unit pane beside or below the list: the selected unit's picture, name and what it is, buttons to show it in
/// the File Editor, make a rule for it, compare or duplicate it, then its sections, each folding as the Division
/// pane's do: Unit (name, nation, role), Stats, Armor, Weapons, Specialties, Divisions and Files. On the Armory tab a
/// value reads as text, and a click on it changes it (<see cref="ClickToChange"/>); the change is saved in the open mod.
/// </summary>
public static class UnitPane
{
    /// <summary>The widest the unit's picture is drawn: about the pane's width beside the list.</summary>
    private const double PictureWidth = 330;

    /// <summary>What the folding of the pane's sections is kept under, apart from the Division pane's.</summary>
    private const string FoldsKey = "unitPane";

    /// <summary>The pane's content for a unit; for none, a line saying to click one.</summary>
    public static FrameworkElement Build(Unit? unit, IUnitActions actions)
    {
        if (unit is null) return Muted(Texts.Get("Units.Pane.ClickAUnit"), new Thickness(10));
        return new WideOrStacked(Stacked(unit, actions), Wide(unit, actions));
    }

    /// <summary>
    /// The pane beside the list, or below it when it is narrow: the picture as wide as the pane (up to
    /// <see cref="PictureWidth"/>), the name and buttons, then every section one under the other, the whole pane wide.
    /// </summary>
    private static StackPanel Stacked(Unit unit, IUnitActions actions)
    {
        var pane = new StackPanel();
        pane.Children.Add(Head(unit, actions, PictureWidth));
        pane.Children.Add(IdentitySection(unit, actions));
        pane.Children.Add(StatsSection(unit, actions));
        pane.Children.Add(ArmorSection(unit, actions));
        pane.Children.Add(WeaponsSection(unit, actions, narrow: true));
        AddSpecialtiesAndDivisions(pane, unit, actions);
        pane.Children.Add(FilesSection(unit));
        return pane;
    }

    /// <summary>
    /// The pane below the list when it is wide, in three columns: the picture, name and buttons at the left, with the
    /// Unit and Files sections under them; the Stats and Armor sections in the middle; the weapons across the rest,
    /// with the specialties and divisions under them.
    /// </summary>
    private static Grid Wide(Unit unit, IUnitActions actions)
    {
        var identity = new StackPanel { Children = { Head(unit, actions, double.PositiveInfinity), IdentitySection(unit, actions), FilesSection(unit) } };
        var stats = new StackPanel { Children = { StatsSection(unit, actions), ArmorSection(unit, actions) } };
        var rest = new StackPanel { Children = { WeaponsSection(unit, actions, narrow: false) } };
        AddSpecialtiesAndDivisions(rest, unit, actions);

        // the sections' headers line up with the top of the picture
        stats.Margin = new Thickness(WideGap, 10, 0, 0);
        rest.Margin = new Thickness(WideGap, 10, 0, 0);
        var grid = new Grid { Margin = new Thickness(0, 0, 10, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WideIdentityWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WideStatsWidth + WideGap) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(stats, 1);
        Grid.SetColumn(rest, 2);
        grid.Children.Add(identity);
        grid.Children.Add(stats);
        grid.Children.Add(rest);
        return grid;
    }

    /// <summary>The picture (with its ✎ where it can be changed), the name and what the unit is, and the buttons, 10 pixels in from the pane's sides.</summary>
    private static StackPanel Head(Unit unit, IUnitActions actions, double widest)
    {
        // larger than the game's picture, so it is enlarged sharply
        var picture = new UnitPicture { Source = unit.PictureSource, Factory = unit.Factory, EnlargesSharply = true };
        var head = new StackPanel
        {
            Margin = new Thickness(10, 10, 10, 12),
            Children = { WithPencil(picture, unit, actions, widest), Title(unit), Buttons(unit, actions) },
        };
        if (LinkedTo(unit, actions) is Border links) head.Children.Add(links);
        return head;
    }

    /// <summary>
    /// "Linked To": what else a change to the unit can reach, before it is made. The divisions it is in; the units
    /// sharing its loadout (a unit made in the Armory shares its model unit's), which carry the same weapons; and its
    /// weapons other units carry too, which a change on the Weapons sub-tab changes for all of them. Nothing when the
    /// tab does not know the other units.
    /// </summary>
    private static Border? LinkedTo(Unit unit, IUnitActions actions)
    {
        if (!actions.KnowsOtherUnits) return null;
        var lines = new StackPanel();
        void Line(string text) => lines.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
        Line(unit.Divisions.Count == 0 ? Texts.Get("Units.Pane.Linked.NoDivision") : Texts.Count("Units.Pane.Linked.Divisions", unit.Divisions.Count));
        IReadOnlyList<Unit> sharing = actions.UnitsSharingLoadout(unit);
        if (sharing.Count > 0) Line(Texts.Get("Units.Pane.Linked.SharedLoadout", ("units", string.Join(", ", sharing.Select(other => other.Name)))));
        var sharedWeapons = new List<string>();
        foreach (UnitWeapon weapon in unit.Weapons.DistinctBy(weapon => weapon.Ammunition))
        {
            int carriers = actions.CarriersOf(weapon.Ammunition);
            if (carriers > 1) sharedWeapons.Add(Texts.Count("Units.Pane.Linked.Carriers", carriers, ("weapon", weapon.Name)));
        }
        if (sharedWeapons.Count > 0) Line(Texts.Get("Units.Pane.Linked.SharedWeapons", ("weapons", string.Join(", ", sharedWeapons))));
        var title = new TextBlock { Text = Texts.Get("Units.Pane.Linked"), FontWeight = FontWeights.Bold, FontSize = 12 };
        var box = new Border
        {
            Child = new StackPanel { Children = { title, lines } },
            BorderThickness = new Thickness(1), Padding = new Thickness(8, 5, 8, 6), Margin = new Thickness(0, 8, 0, 0),
        };
        box.SetResourceReference(Border.BorderBrushProperty, "line");
        box.SetResourceReference(Border.BackgroundProperty, "panel");
        return box;
    }

    /// <summary>
    /// The unit's picture, at most <paramref name="widest"/> wide; for a unit whose picture the tab can change, with a
    /// small ✎ on its bottom right corner, as a division's emblem has, whose menu offers a picture of the user's own
    /// or the unit's own picture again.
    /// </summary>
    private static FrameworkElement WithPencil(UnitPicture picture, Unit unit, IUnitActions actions, double widest)
    {
        var holder = new Grid { MaxWidth = widest, HorizontalAlignment = HorizontalAlignment.Left, Children = { picture } };
        if (!actions.CanChangePicture(unit)) return holder;
        var pencil = new Button
        {
            Content = "✎", Style = (Style)Application.Current.FindResource("HeadingButton"), Padding = new Thickness(0), Width = 18, MinHeight = 18, Height = 18,
            FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 4),
            ToolTip = Texts.Get("Units.Pane.Picture.Tip"),
        };
        pencil.Click += (_, _) => actions.OpenPictureMenu(unit, pencil);
        holder.Children.Add(pencil);
        return holder;
    }

    /// <summary>The wide pane's left column (picture, name, buttons), its stats column, and the gap after each.</summary>
    private const double WideIdentityWidth = 220, WideStatsWidth = 330, WideGap = 16;

    /// <summary>The pane is this wide at least to be laid out wide: room for the weapons beside the stats, as on Compare.</summary>
    private const double WidestStacked = 900;

    /// <summary>
    /// Shows the wide layout or the stacked one, by the width the pane gives (never by the content's own size, so it
    /// cannot flip back and forth). Both are built, and the one not shown is hidden.
    /// </summary>
    internal sealed class WideOrStacked : Grid
    {
        private readonly FrameworkElement stacked, wide;

        /// <summary>Holds the two layouts of the same unit.</summary>
        public WideOrStacked(FrameworkElement stacked, FrameworkElement wide)
        {
            this.stacked = stacked;
            this.wide = wide;
            Children.Add(stacked);
            Children.Add(wide);
        }

        /// <summary>Whether the wide layout is the one shown.</summary>
        internal bool IsWide => wide.Visibility == Visibility.Visible;

        /// <summary>Chooses the layout for the width given, then measures as a grid does.</summary>
        protected override Size MeasureOverride(Size available)
        {
            bool showWide = available.Width >= WidestStacked;
            // both start shown, so each is set; setting the value it already has asks for no other measure
            wide.Visibility = showWide ? Visibility.Visible : Visibility.Collapsed;
            stacked.Visibility = showWide ? Visibility.Collapsed : Visibility.Visible;
            return base.MeasureOverride(available);
        }
    }

    /// <summary>
    /// The unit's specialties (when it has any) and divisions, as sections at the foot of a column. Those the open mod
    /// adds are marked, as changed values are. On the Armory tab a division is a link: a click opens it.
    /// </summary>
    private static void AddSpecialtiesAndDivisions(Panel column, Unit unit, IUnitActions actions)
    {
        if (unit.Specialties.Count > 0)
            column.Children.Add(new FoldSection("specialties", Texts.Get("Units.Pane.Specialties"), Texts.Number(unit.Specialties.Count),
                Tags(unit.Specialties.Select(SpecialtyName), "", AddedTags(unit, UnitField.Specialties, each => each.Specialties.Select(SpecialtyName))), FoldsKey));
        column.Children.Add(new FoldSection("divisions", Texts.Get("Units.Pane.Divisions"), Texts.Number(unit.Divisions.Count),
            Tags(unit.Divisions, Texts.Get("Units.Pane.InNoDivision"), AddedTags(unit, UnitField.Divisions, each => each.Divisions),
                actions.CanShowDivisions ? actions.ShowDivisionCalled : null), FoldsKey));
    }

    /// <summary>
    /// The weapons. On the Armory tab, for a unit whose weapons can be changed, its loadout turret by turret, to swap,
    /// change, take off and add weapons (<see cref="Weapons.LoadoutEditor"/>); elsewhere, as Compare's cards list them.
    /// One section either way, as the two showed the same weapons twice.
    /// </summary>
    private static FoldSection WeaponsSection(Unit unit, IUnitActions actions, bool narrow)
    {
        var contents = new StackPanel();
        if (actions.LoadoutEditorFor(unit) is FrameworkElement editor)
        {
            contents.Children.Add(Muted(Texts.Get("Units.Pane.Loadout.Note"), new Thickness(0, 0, 0, 8)));
            contents.Children.Add(editor);
        }
        else contents.Children.Add(WeaponLedger.Build(unit, actions.Game.RangeUnit, narrow, bestRange: null, (weapon, at) => actions.OpenWeaponMenu(unit, weapon, at),
            weapon => actions.ShowWeaponInArmory(weapon.Ammunition)));
        return new FoldSection("weapons", Texts.Get("Units.Weapons"), Texts.Count("Units.Pane.Weapons.Count", unit.Weapons.Count), contents, FoldsKey);
    }

    /// <summary>
    /// Where the unit is in the game's files: its descriptor, its file and its weapon set, in the code font. Folded at
    /// first.
    /// </summary>
    private static FoldSection FilesSection(Unit unit)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Add(string nameKey, string value)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock name = Muted(Texts.Get(nameKey), new Thickness(0, 1, 10, 1));
            var text = new TextBlock { Text = value, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
            text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(name);
            grid.Children.Add(text);
        }
        Add("Units.Pane.Descriptor", unit.Descriptor);
        Add("Units.Pane.File", unit.File);
        if (unit.WeaponSet is string weaponSet) Add("Units.Pane.WeaponSet", weaponSet);
        return new FoldSection("files", Texts.Get("Units.Pane.Files"), "", grid, FoldsKey, foldedAtFirst: true);
    }

    /// <summary>
    /// The words of a list the open mod adds to the unit, each with its tooltip (the game's list); none when the mod
    /// does not change the list.
    /// </summary>
    private static Dictionary<string, string> AddedTags(Unit unit, string field, Func<Unit, IEnumerable<string>> read)
    {
        var added = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ModMarks.Changes.Of(unit) is not UnitChange change || !change.Changed(field)) return added;
        HashSet<string> before = change.Game is Unit game ? [.. read(game)] : [];
        string tip = ModMarks.TipFor(change, field, each => string.Join(", ", read(each)));
        foreach (string word in read(unit))
            if (!before.Contains(word)) added[word] = tip;
        return added;
    }

    /// <summary>The name, 14 pixels and bold, and under it the nation, side, factory and role, dim.</summary>
    private static StackPanel Title(Unit unit)
    {
        var name = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        ModMarks.Show(name, unit, UnitField.Name, unit.Name, game => game.Name);
        string what = string.Join(" · ", UnitNames.Nation(unit.Nation), UnitNames.Side(unit.Side), UnitNames.Factory(unit.Factory), UnitNames.RoleOf(unit));
        return new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { name, Muted(what, new Thickness(0)) } };
    }

    /// <summary>
    /// The Unit section: its name, nation (with its side) and role, one a row; folded, the name and nation. Each
    /// changes with a click where the tab lets it: the name in a box, the nation and role from lists.
    /// </summary>
    private static FoldSection IdentitySection(Unit unit, IUnitActions actions)
    {
        string NationOf(Unit each) => UnitNames.Nation(each.Nation) + " · " + UnitNames.Side(each.Side);
        var rows = new (string NameKey, string Field, Func<Unit, string> Write)[]
        {
            ("Units.Pane.Name", UnitField.Name, each => each.Name),
            ("Units.Pane.Nation", UnitField.Nation, NationOf),
            ("Units.Pane.Role", UnitField.Role, UnitNames.RoleOf),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int row = 0; row < rows.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool lastRow = row == rows.Length - 1;
            grid.Children.Add(Cell(Texts.Get(rows[row].NameKey), row, 0, isName: true, lastRow));
            Bevel value = Cell("", row, 1, isName: false, lastRow);
            var text = (TextBlock)value.Child;
            text.TextWrapping = TextWrapping.Wrap;
            ModMarks.Show(text, unit, rows[row].Field, rows[row].Write(unit), rows[row].Write);
            if (actions.CanChangeUnit(unit)) MakeChangeable(value, unit, rows[row].Field, actions);
            grid.Children.Add(value);
        }
        return new FoldSection("unit", Texts.Get("Units.Pane.Title"), unit.Name + " · " + UnitNames.Nation(unit.Nation), Boxed(grid), FoldsKey);
    }

    /// <summary>A name, nation or role made changeable with a click: the name in a box, the others from a list.</summary>
    private static void MakeChangeable(Bevel cell, Unit unit, string field, IUnitActions actions)
    {
        Func<string, bool> keep = value => actions.ChangeUnit(unit, field, value);
        switch (field)
        {
            case UnitField.Name:
                ClickToChange.WithBox(cell, unit.Name, typed => typed != "", keep, Texts.Get("Units.Pane.Name.Tip"));
                break;
            case UnitField.Nation:
                ClickToChange.WithChoice(cell, actions.NationChoices, unit.Nation, keep);
                break;
            case UnitField.Role:
                ClickToChange.WithChoice(cell, actions.RoleChoices, unit.Role, keep);
                break;
        }
    }

    /// <summary>Show in File Editor (dim while the unit's file is not on the Files tab), New Rule…, Compare and, where the tab offers it, Duplicate….</summary>
    private static WrapRow Buttons(Unit unit, IUnitActions actions)
    {
        var showInEditor = new Button { Content = Texts.Get("Units.Pane.ShowInEditor"), IsEnabled = actions.CanShowInEditor(unit) };
        showInEditor.ToolTip = showInEditor.IsEnabled ? Texts.Get("Units.Pane.ShowInEditor.Tip") : Texts.Get("Units.Menu.NotLoaded.Tip", ("file", unit.File));
        // a disabled button still shows why
        ToolTipService.SetShowOnDisabled(showInEditor, true);
        showInEditor.Click += (_, _) => actions.ShowInEditor(unit);
        var newRule = new Button { Content = Texts.Get("Units.Pane.NewRule"), ToolTip = Texts.Get("Units.Pane.NewRule.Tip") };
        newRule.Click += (_, _) => actions.NewRuleFor(unit);
        var compare = new Button { Content = Texts.Get("Units.Pane.Compare"), ToolTip = Texts.Get("Units.Pane.Compare.Tip") };
        compare.Click += (_, _) => actions.Compare(unit);
        var buttons = new WrapRow { Margin = new Thickness(0, 12, 0, 0), Children = { showInEditor, newRule, compare } };
        if (actions.CanDuplicate(unit))
        {
            var duplicate = new Button { Content = Texts.Get("Units.Pane.Duplicate"), ToolTip = Texts.Get("Units.Pane.Duplicate.Tip") };
            duplicate.Click += (_, _) => actions.Duplicate(unit);
            buttons.Children.Add(duplicate);
        }
        // made always, and hidden where it is not offered, so a sub-tab switch shows or hides it without drawing the pane again
        var showInUnits = new Button
        {
            Content = Texts.Get("Units.Pane.ShowInUnits"), ToolTip = Texts.Get("Units.Pane.ShowInUnits.Tip"), Tag = ShowInUnitsTag,
            Visibility = actions.OffersShowInUnits ? Visibility.Visible : Visibility.Collapsed,
        };
        showInUnits.Click += (_, _) => actions.ShowInUnits(unit);
        buttons.Children.Insert(0, showInUnits);
        return buttons;
    }

    /// <summary>Marks the Show in Units button, so <see cref="ShowShowInUnits"/> finds it.</summary>
    private static readonly object ShowInUnitsTag = new();

    /// <summary>Shows a pane's Show in Units button where the tab offers it, and hides it elsewhere.</summary>
    /// <param name="pane">The pane, as <see cref="Build"/> made it.</param>
    /// <param name="offers">Whether the tab offers it now (not on the Units sub-tab, which shows every unit already).</param>
    public static void ShowShowInUnits(object? pane, bool offers)
    {
        if (pane is not DependencyObject root || FindTagged(root, ShowInUnitsTag) is not Button button) return;
        button.Visibility = offers ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The element under another, in the pane as it was built, marked with a tag; null when there is none.</summary>
    private static FrameworkElement? FindTagged(DependencyObject inside, object tag)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(inside))
        {
            if (child is FrameworkElement { Tag: var found } element && ReferenceEquals(found, tag)) return element;
            if (child is DependencyObject deeper && FindTagged(deeper, tag) is FrameworkElement inDeeper) return inDeeper;
        }
        return null;
    }

    /// <summary>
    /// The Stats section: two pairs of name and value a row, the names dim on the panel colour. Speed is in km/h in
    /// WARNO; Steel Division 2 gives it in its own unit. Where the tab lets it, a click on a value changes it.
    /// </summary>
    private static FoldSection StatsSection(Unit unit, IUnitActions actions)
    {
        string KilometresPerHour(double? speed) => Texts.Get("Units.KilometresPerHour", ("speed", UnitNumbers.Of(speed)));
        // each value with its field, and how to write it, so the game's value is written the same way
        var pairs = new List<(string Name, string Field, Func<Unit, string> Write)>();
        void Add(string key, string field, Func<Unit, double?> read, Func<double?, string>? write = null)
        {
            if (read(unit) is null) return;
            pairs.Add((Texts.Get(key), field, each => read(each) is null ? "" : write is null ? UnitNumbers.Of(read(each)) : write(read(each))));
        }
        Add("Units.Stat.Cost", UnitField.Cost, each => each.Cost);
        Add("Units.Stat.Strength", UnitField.Strength, each => each.Strength);
        Add("Units.Stat.Speed", UnitField.Speed, each => each.Speed, actions.Game == GameTitle.Warno ? KilometresPerHour : null);
        Add("Units.Stat.RoadSpeed", UnitField.RoadSpeed, each => each.RoadSpeed, KilometresPerHour);
        Add("Units.Stat.Optics", UnitField.Optics, each => each.Optics);
        Add("Units.Stat.Stealth", UnitField.Stealth, each => each.Stealth);
        Add("Units.Pane.Fuel", UnitField.Fuel, each => each.Fuel);
        Add("Units.Pane.Vision", UnitField.Vision, each => each.Vision);
        if (pairs.Count % 2 == 1) pairs.Add(("", "", _ => ""));

        bool canChange = actions.CanChangeUnit(unit);
        var grid = new Grid();
        foreach (GridLength width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        int rows = pairs.Count / 2;
        for (int at = 0; at < pairs.Count; at++)
        {
            int row = at / 2, column = at % 2 * 2;
            if (column == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool lastRow = row == rows - 1;
            Bevel name = Cell(pairs[at].Name, row, column, isName: true, lastRow);
            AddFileName((TextBlock)name.Child, UnitChanges.FileNameOf(pairs[at].Field));
            grid.Children.Add(name);
            Bevel value = Cell("", row, column + 1, isName: false, lastRow);
            ModMarks.Show((TextBlock)value.Child, unit, pairs[at].Field, pairs[at].Write(unit), pairs[at].Write);
            if (canChange && EditableStat.Of(pairs[at].Field) is EditableStat stat) MakeChangeable(value, unit, stat, actions);
            grid.Children.Add(value);
        }
        var contents = new StackPanel { Children = { Boxed(grid) } };
        if (canChange) contents.Children.Add(Muted(Texts.Get("Units.Pane.ClickToChange"), new Thickness(0, 6, 0, 0)));
        return new FoldSection("stats", Texts.Get("Units.Compare.Stats"), "", contents, FoldsKey);
    }

    /// <summary>
    /// A stat made changeable with a click: a box with its value, outlined in the caution colour while what is typed
    /// is not a value the game takes (for an armour side, a level its armour family allows).
    /// </summary>
    private static void MakeChangeable(Bevel cell, Unit unit, EditableStat stat, IUnitActions actions)
    {
        if (stat.Read(unit) is not double value) return;
        string typedNow = value.ToString("0.##########", Texts.Catalog.Culture);
        ClickToChange.WithBox(cell, typedNow, typed => stat.ValueOf(typed, unit, actions.ArmorLevels, Texts.Catalog.Culture) is not null,
            typed => actions.ChangeUnit(unit, stat.Field, typed), StatTip(unit, stat, actions));
    }

    /// <summary>What a stat's box takes: a whole number, a number, or for an armour side the levels its family allows.</summary>
    private static string StatTip(Unit unit, EditableStat stat, IUnitActions actions)
    {
        if (stat.FamilyOf(unit) is string family && ArmorLevels.RangeOf(actions.ArmorLevels, family) is (int lowest, int highest))
            return Texts.Get("Units.Pane.Stat.Armor.Tip", ("family", UnitNames.ArmorFamily(family)), ("lowest", lowest), ("highest", highest));
        return Texts.Get(stat.IsWhole ? "Units.Pane.Stat.Whole.Tip" : "Units.Pane.Stat.Decimal.Tip");
    }

    /// <summary>
    /// Under a value's name, the property the game's files keep it in, small and dim in the code font, when the
    /// setting "Show names in the files" is on; nothing for a value with no single property.
    /// </summary>
    internal static void AddFileName(TextBlock name, string? property)
    {
        if (!ModMarks.ShowFileNames || string.IsNullOrEmpty(property)) return;
        var fileName = new System.Windows.Documents.Run(property) { FontSize = 10 };
        fileName.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "mono");
        name.Inlines.Add(new System.Windows.Documents.LineBreak());
        name.Inlines.Add(fileName);
    }

    /// <summary>A cell of the overview: 2 by 7 pixels in, a line under it unless it is on the last row.</summary>
    internal static Bevel Cell(string text, int row, int column, bool isName, bool lastRow)
    {
        var block = new TextBlock { Text = text };
        if (isName) block.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var cell = new Bevel { Child = block, Padding = new Thickness(7, 2, 7, 2), Edges = new Thickness(0, 0, 0, lastRow ? 0 : 1) };
        cell.SetResourceReference(Bevel.BottomRightProperty, "line");
        if (isName) cell.SetResourceReference(Bevel.BackgroundProperty, "panel");
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        return cell;
    }

    /// <summary>
    /// The Armor section: the armour on each side, in a row of four cells under their names; "–" for a side the game
    /// gives none. Folded, the four levels. Where the tab lets it, a click on a level changes it.
    /// </summary>
    private static FoldSection ArmorSection(Unit unit, IUnitActions actions)
    {
        var grid = new Grid();
        string[] names = [Texts.Get("Units.Pane.Front"), Texts.Get("Units.Pane.Sides"), Texts.Get("Units.Pane.Rear"), Texts.Get("Units.Pane.Top")];
        string[] fields = [UnitField.FrontArmor, UnitField.SideArmor, UnitField.RearArmor, UnitField.TopArmor];
        Func<UnitArmor, int?>[] sides = [armor => armor.Front, armor => armor.Sides, armor => armor.Rear, armor => armor.Top];
        bool canChange = actions.CanChangeUnit(unit);
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        for (int side = 0; side < 4; side++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var name = new TextBlock { Text = names[side], TextAlignment = TextAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            AddFileName(name, UnitChanges.FileNameOf(fields[side]));
            var value = new TextBlock { TextAlignment = TextAlignment.Center, FontWeight = FontWeights.Bold, FontSize = 13.5 };
            Func<UnitArmor, int?> read = sides[side];
            string Write(Unit each) => read(each.Armor)?.ToString(Texts.Catalog.Culture) ?? UnitNumbers.None;
            ModMarks.Show(value, unit, fields[side], Write(unit), Write);
            grid.Children.Add(TableCell(name, 0, side, onPanel: true));
            Bevel valueCell = TableCell(value, 1, side, onPanel: false);
            if (canChange) MakeChangeable(valueCell, unit, EditableStat.Of(fields[side])!, actions);
            grid.Children.Add(valueCell);
        }
        string summary = string.Join(" · ", sides.Select(read => read(unit.Armor)?.ToString(Texts.Catalog.Culture) ?? UnitNumbers.None));
        return new FoldSection("armor", Texts.Get("Units.Pane.Armor"), summary, grid, FoldsKey);
    }

    /// <summary>A cell of the armour table, with a line on every side, shared with its neighbours.</summary>
    private static Bevel TableCell(TextBlock text, int row, int column, bool onPanel)
    {
        // each cell draws its left and top lines; the last ones also their right and bottom lines
        var cell = new Bevel { Child = text, Padding = new Thickness(4, 2, 4, 2), Edges = new Thickness(1, 1, column == 3 ? 1 : 0, row == 1 ? 1 : 0) };
        cell.SetResourceReference(Bevel.TopLeftProperty, "line");
        cell.SetResourceReference(Bevel.BottomRightProperty, "line");
        if (onPanel) cell.SetResourceReference(Bevel.BackgroundProperty, "panel");
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        return cell;
    }

    /// <summary>
    /// Small boxed words, wrapping: the specialties, the divisions; <paramref name="none"/> when there are none. Words
    /// in <paramref name="added"/> (the open mod's) are marked as changed values are, with their tooltips. With
    /// <paramref name="clicked"/>, each word is a link: the accent colour under the mouse, and a click passes it on.
    /// </summary>
    internal static FrameworkElement Tags(IEnumerable<string> words, string none, IReadOnlyDictionary<string, string>? added = null, Action<string>? clicked = null)
    {
        var row = new WrapRow();
        foreach (string word in words)
        {
            var text = new TextBlock { Text = word };
            text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            if (added?.GetValueOrDefault(word) is string tip)
            {
                text.ToolTip = tip;
                if (ModMarks.AreShown) text.Inlines.Add(ModMarks.Star());
            }
            var tag = new Bevel { Child = text, Padding = new Thickness(6, 0, 6, 0) };
            tag.SetResourceReference(Bevel.TopLeftProperty, "line");
            tag.SetResourceReference(Bevel.BottomRightProperty, "line");
            if (clicked is not null) MakeLink(tag, text, word, clicked);
            row.Children.Add(tag);
        }
        return row.Children.Count == 0 ? Muted(none, new Thickness(0)) : row;
    }

    /// <summary>
    /// A boxed word as a link: under the mouse its edges and text in the accent colour, and a click passes the word on.
    /// Its tooltip says where it leads, unless it is a changed value's, which says what changed it.
    /// </summary>
    private static void MakeLink(Bevel tag, TextBlock text, string word, Action<string> clicked)
    {
        tag.Cursor = System.Windows.Input.Cursors.Hand;
        tag.Background = Brushes.Transparent;
        text.ToolTip ??= Texts.Get("Units.Pane.Division.Tip", ("division", word));
        tag.MouseEnter += (_, _) =>
        {
            tag.SetResourceReference(Bevel.TopLeftProperty, "accent");
            tag.SetResourceReference(Bevel.BottomRightProperty, "accent");
        };
        tag.MouseLeave += (_, _) =>
        {
            tag.SetResourceReference(Bevel.TopLeftProperty, "line");
            tag.SetResourceReference(Bevel.BottomRightProperty, "line");
        };
        tag.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            clicked(word);
        };
    }

    /// <summary>A specialty as the game names it, made readable: "_ifv" is "ifv", "reco_inf" is "reco inf".</summary>
    private static string SpecialtyName(string specialty) => specialty.TrimStart('_').Replace('_', ' ');

    /// <summary>A grid in a box of lines.</summary>
    internal static Bevel Boxed(UIElement content)
    {
        var box = new Bevel { Child = content };
        box.SetResourceReference(Bevel.TopLeftProperty, "line");
        box.SetResourceReference(Bevel.BottomRightProperty, "line");
        return box;
    }

    internal static TextBlock Muted(string text, Thickness margin)
    {
        var block = new TextBlock { Text = text, Margin = margin, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return block;
    }
}
