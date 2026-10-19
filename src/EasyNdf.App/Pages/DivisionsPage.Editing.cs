using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Divisions tab's menus, and what changes a division: adding a unit (from here or from the Units tab), taking one
/// out, a new division. Every change is saved in the mod open on the Rules tab. What the menus share with the Units
/// tab (the File Editor, new rules, Compare) is done by the Units tab.
/// </summary>
public partial class DivisionsPage : IUnitActions, IDivisionEditing
{
    // ---- a unit's menu ----

    /// <summary>
    /// A unit's menu: show it or its weapon set in the File Editor; compare it; new rules for it or for the units shown;
    /// copy its name; take it out of the division, or add it to another.
    /// </summary>
    public ContextMenu OpenUnitMenu(Unit unit, FrameworkElement at)
    {
        MenuChoice?[] items =
        [
            Menus.Heading(unit.Name),
            new MenuChoice(Texts.Get("Divisions.Menu.ShowInUnits"), () => ShowInUnitsTab(unit), Tip: Texts.Get("Divisions.Menu.ShowInUnits.Tip")),
            .. unitsTab?.ShowInEditorChoices(unit) ?? [],
            Menus.Line,
            new MenuChoice(Texts.Get("Units.Menu.Compare"), () => Compare(unit)),
            new MenuChoice(Texts.Get("Units.Menu.NewRuleForUnit"), () => NewRuleFor(unit)),
            new MenuChoice(Texts.Get("Units.Menu.NewRuleForShown"), NewRuleForShown),
            new MenuChoice(Texts.Get("Units.Menu.CopyName"), () => UnitsPage.Copy(unit.Descriptor), Key: unit.Descriptor),
            Menus.Line,
            chosen is null ? null : new MenuChoice(Texts.Get("Divisions.Menu.Remove"), () => RemoveFromDivision(chosen, unit)),
            .. AddToDivisionChoices(unit),
        ];
        return at is Button ? Menus.ShowBelow(at, items) : Menus.Show(at, items);
    }

    /// <summary>
    /// "Add to Division": one item with a menu of its own: New Division… first, then the mod's new divisions, then the
    /// game's by side and nation, each in a menu of its own; those that have the unit already are dim. On the Units
    /// tab's menu too.
    /// </summary>
    public MenuChoice?[] AddToDivisionChoices(Unit unit)
    {
        if (loaded is null) return [];
        var choices = new List<MenuChoice?> { new MenuChoice(Texts.Get("Divisions.Menu.AddToNew"), () => _ = AddToNewDivision(unit)), Menus.Line };
        List<Division> modsOwn = [.. divisions.Where(IsNewInMod)];
        if (modsOwn.Count > 0)
        {
            choices.Add(Menus.Heading(OpenMod.Name));
            choices.AddRange(modsOwn.Select(division => AddToChoice(division, unit)));
            choices.Add(Menus.Line);
        }
        foreach (IGrouping<string, Division> sideGroup in divisions.Where(division => !IsNewInMod(division) && !DivisionChanges.IsRemoved(OpenMod, division.Descriptor)).GroupBy(division => division.Side))
        {
            var byNation = sideGroup.GroupBy(division => division.Country)
                .OrderBy(group => UnitNames.Nation(group.Key), StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
            List<MenuChoice?> nations = [.. byNation.Select(nation => (MenuChoice?)MenuChoice.Submenu(
                nation.Key == "" ? UnitNames.Side(sideGroup.Key) : UnitNames.Nation(nation.Key),
                [.. nation.OrderBy(division => division.ListOrder ?? double.MaxValue).Select(division => AddToChoice(division, unit))]))];
            choices.Add(MenuChoice.Submenu(UnitNames.Side(sideGroup.Key), nations));
        }
        return [MenuChoice.Submenu(Texts.Get("Divisions.Menu.AddTo"), choices)];
    }

    /// <summary>A division to add a unit to; dim, with a tip saying so, when it has the unit already.</summary>
    private MenuChoice AddToChoice(Division division, Unit unit)
    {
        bool hasIt = division.Units.Any(rule => rule.Unit == unit.Descriptor);
        return new MenuChoice(division.Name, () => AddToDivision(division, unit), IsEnabled: !hasIt,
            Tip: hasIt ? Texts.Get("Divisions.Menu.HasIt.Tip") : "");
    }

    // ---- a division's menu, in the list ----

    /// <summary>
    /// A division's menu: rename, duplicate or delete it (a game division the mod took out is restored instead); for a
    /// game division the mod changes, put it back as the game has it; copy its name.
    /// </summary>
    private void OpenDivisionMenu(Division division, FrameworkElement at)
    {
        ModDivision? inMod = DivisionChanges.EntryOf(OpenMod, division.Descriptor);
        bool isRemoved = inMod is { IsRemoved: true };
        MenuChoice?[] items =
        [
            Menus.Heading(division.Name),
            new MenuChoice(Texts.Get("Divisions.Menu.Rename"), () => _ = RenameDivision(division)),
            new MenuChoice(Texts.Get("Divisions.Menu.Duplicate"), () => _ = DuplicateDivision(division)),
            isRemoved
                ? new MenuChoice(Texts.Get("Divisions.Menu.Restore"), () => RestoreDivision(division))
                : new MenuChoice(Texts.Get("Divisions.Menu.Delete"), () => _ = DeleteDivision(division)),
            Menus.Line,
            inMod is { IsNew: false } ? new MenuChoice(Texts.Get("Divisions.Menu.Revert"), () => _ = RevertDivision(division, inMod)) : null,
            new MenuChoice(Texts.Get("Units.Menu.CopyName"), () => UnitsPage.Copy(division.Descriptor), Key: division.Descriptor),
        ];
        Menus.Show(at, items);
    }

    // ---- the changes ----

    /// <summary>
    /// Adds a unit to a division, saved in the mod. The user stays where they are, on whichever tab and division: only
    /// a new division is brought into view.
    /// </summary>
    private void AddToDivision(Division division, Unit unit)
    {
        DivisionChanges.AddUnits(OpenMod, GameDivisions, division.Descriptor, [unit.Descriptor]);
        AppData.Current.Save(OpenMod);
    }

    /// <summary>
    /// "Add to New Division…": asks for a name, then makes a new division in the mod with the unit alone. It starts with
    /// the details (points, costs, emblem, type) of a game division that has the unit, or else one of its nation.
    /// </summary>
    private async Task AddToNewDivision(Unit unit)
    {
        if (DivisionChanges.StartFor(GameDivisions, unit.Descriptor, unit.Nation, unit.Side) is not Division start) return;
        string name = await MainView.Of(this).Messages.AskName(
            Texts.Get("Divisions.New.Ask", ("division", start.Name), ("unit", unit.Name)), Texts.Get("Divisions.New.Name"));
        if (name != "") MakeNewDivision(name, start, unit);
    }

    /// <summary>
    /// "+": asks for a name, then makes a new division in the mod with no units. It starts with the details (points,
    /// costs, emblem, type) of the division shown, or of the game division a new one of the mod's started from.
    /// </summary>
    private async void NewDivisionClicked(object sender, RoutedEventArgs e)
    {
        if (chosen is null) return;
        string startsFrom = DivisionChanges.EntryOf(OpenMod, chosen.Descriptor)?.StartsFrom ?? chosen.Descriptor;
        if (GameDivisions.FirstOrDefault(division => division.Descriptor == startsFrom) is not Division start) return;
        string name = await MainView.Of(this).Messages.AskName(Texts.Get("Divisions.NewEmpty.Ask", ("division", start.Name)), Texts.Get("Divisions.New.Name"));
        if (name == "") return;
        string descriptor = DivisionChanges.AddNew(OpenMod, GameDivisions, name, start, []);
        AppData.Current.Save(OpenMod);
        AppData.Current.State["division"] = descriptor;
        AppData.Current.SaveState();
        ApplyMod(descriptor);
    }

    /// <summary>A new division in the mod with one unit, saved and shown.</summary>
    private void MakeNewDivision(string name, Division start, Unit unit)
    {
        string descriptor = DivisionChanges.AddNew(OpenMod, GameDivisions, name, start, [unit.Descriptor]);
        AppData.Current.Save(OpenMod);
        ShowUnitIn(descriptor, unit);
    }

    /// <summary>
    /// "+ Add Units…": the Add Units window, with every unit of the source, those the division has already dim; the
    /// units ticked are added to the division shown.
    /// </summary>
    private void AddUnitsClicked(object sender, RoutedEventArgs e)
    {
        if (loaded is null || chosen is null) return;
        Division division = chosen;
        HashSet<Unit> already = [.. DivisionGroups.Entries(division, unitsByDescriptor).Select(entry => entry.Unit)];
        MainView.Of(this).AddUnits.Open(Texts.Get("Divisions.AddUnits.Title", ("division", division.Name)), loaded.Catalog.Units, Game, already,
            units => AddUnits(division, units));
    }

    /// <summary>Adds units to a division, saved in the mod.</summary>
    private void AddUnits(Division division, IReadOnlyList<Unit> units)
    {
        DivisionChanges.AddUnits(OpenMod, GameDivisions, division.Descriptor, units.Select(unit => unit.Descriptor));
        AppData.Current.Save(OpenMod);
    }

    /// <summary>A unit's cards, units per card or veterancy as changed in the table, saved in the mod.</summary>
    private void KeepRule(DivisionUnit rule)
    {
        if (chosen is null) return;
        DivisionChanges.SetRule(OpenMod, GameDivisions, chosen.Descriptor, rule);
        AppData.Current.Save(OpenMod);
    }

    /// <summary>Takes a unit out of a division, saved in the mod.</summary>
    private void RemoveFromDivision(Division division, Unit unit)
    {
        DivisionChanges.RemoveUnit(OpenMod, GameDivisions, division.Descriptor, unit.Descriptor);
        selection.Selected = null;
        AppData.Current.Save(OpenMod);
    }

    /// <summary>Renames a division, as the user types it; a game division keeps its renaming as a change in the mod.</summary>
    private async Task RenameDivision(Division division)
    {
        string name = await MainView.Of(this).Messages.AskName(Texts.Get("Divisions.Rename.Ask", ("division", division.Name)), division.Name);
        if (name == "" || name == division.Name) return;
        DivisionChanges.Rename(OpenMod, GameDivisions, division.Descriptor, name);
        AppData.Current.Save(OpenMod);
    }

    /// <summary>A copy of a division, as a new division in the mod named as the user types, shown once made.</summary>
    private async Task DuplicateDivision(Division division)
    {
        string name = await MainView.Of(this).Messages.AskName(Texts.Get("Divisions.Duplicate.Ask", ("division", division.Name)), FreeName(division.Name));
        if (name != "") DuplicateAs(division, name);
    }

    /// <summary>Makes and saves the copy, and shows it.</summary>
    private void DuplicateAs(Division division, string name)
    {
        string descriptor = DivisionChanges.Duplicate(OpenMod, GameDivisions, division.Descriptor, name);
        AppData.Current.Save(OpenMod);
        AppData.Current.State["division"] = descriptor;
        AppData.Current.SaveState();
        ApplyMod(descriptor);
    }

    /// <summary>A name no division shown has: "3rd Armored (2)", "3rd Armored (3)"...</summary>
    private string FreeName(string name)
    {
        int number = 2;
        while (divisions.Any(division => division.Name == name + " (" + number + ")")) number++;
        return name + " (" + number + ")";
    }

    /// <summary>
    /// Deletes a division, once the user says so: the mod's new division goes from the mod; a game division is taken
    /// out of the game's list, as decks made with it would no longer open.
    /// </summary>
    private async Task DeleteDivision(Division division)
    {
        bool isNew = IsNewInMod(division);
        string question = isNew
            ? Texts.Get("Divisions.Delete.Ask", ("division", division.Name))
            : Texts.Get("Divisions.DeleteGame.Ask", ("division", division.Name), ("mod", OpenMod.Name));
        if (!await MainView.Of(this).Messages.Confirm(question, Texts.Get("Common.Menu.Delete"))) return;
        DivisionChanges.Delete(OpenMod, division.Descriptor);
        AppData.Current.Save(OpenMod);
    }

    /// <summary>Puts a game division the mod took out back in the game's list.</summary>
    private void RestoreDivision(Division division)
    {
        DivisionChanges.Restore(OpenMod, division.Descriptor);
        AppData.Current.Save(OpenMod);
    }

    /// <summary>Lets go of the mod's changes to a game division, once the user says so: the game's division is shown again.</summary>
    private async Task RevertDivision(Division division, ModDivision inMod)
    {
        if (!await MainView.Of(this).Messages.Confirm(Texts.Get("Divisions.Revert.Ask", ("division", division.Name), ("mod", OpenMod.Name)), Texts.Get("Divisions.Revert.Do"))) return;
        OpenMod.Divisions.Remove(inMod);
        AppData.Current.Save(OpenMod);
    }

    /// <summary>Shows the Divisions tab on a division, with all its categories and the unit selected.</summary>
    private void ShowUnitIn(string descriptor, Unit unit)
    {
        AppData.Current.State["division"] = descriptor;
        AppData.Current.SaveState();
        factory = "";
        ApplyMod(descriptor);
        selection.Selected = unit;
        MainView.Of(this).Tabs.SelectedItem = MainView.Of(this).DivisionsTabItem;
    }

    // ---- the Division pane's changes ----

    /// <inheritdoc/>
    public IReadOnlyList<(string Country, string Side)> Nations =>
        [.. GameDivisions.Where(division => division.Country != "").Select(division => (division.Country, division.Side)).Distinct()];

    /// <inheritdoc/>
    public IReadOnlyList<string> Types => [.. GameDivisions.Select(division => division.TypeToken).Where(type => type != "").Distinct()];

    /// <inheritdoc/>
    public void Rename(string name)
    {
        if (chosen is null) return;
        DivisionChanges.Rename(OpenMod, GameDivisions, chosen.Descriptor, name);
        AppData.Current.Save(OpenMod);
    }

    /// <inheritdoc/>
    public void ChangeDetails(Action<ModDivision> change)
    {
        if (chosen is null) return;
        DivisionChanges.ChangeDetails(OpenMod, GameDivisions, chosen.Descriptor, change);
        AppData.Current.Save(OpenMod);
    }

    /// <summary>
    /// "Game's…": every emblem of the game's divisions, as small pictures in a box under the button; a click takes it,
    /// in place of a picture of the user's own.
    /// </summary>
    public void ChooseGameEmblem(FrameworkElement button)
    {
        var emblems = new WrapPanel { Width = 330 };
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
        };
        IEnumerable<Division> withEmblem = GameDivisions.Where(division => division.EmblemSource is not null).GroupBy(division => division.EmblemTexture).Select(group => group.First());
        foreach (Division owner in withEmblem.OrderBy(division => division.Name, StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true)))
        {
            var choice = new Button { Content = new DivisionEmblem(owner.EmblemSource, 36, keepsRoom: true), Padding = new Thickness(2), Margin = new Thickness(1), ToolTip = owner.Name };
            Division taken = owner;
            choice.Click += (_, _) =>
            {
                popup.IsOpen = false;
                ChangeDetails(change => (change.EmblemTexture, change.EmblemSource, change.EmblemFile) = (taken.EmblemTexture, taken.EmblemSource, null));
            };
            emblems.Children.Add(choice);
        }
        var frame = new Controls.Bevel { Padding = new Thickness(3), Child = new ScrollViewer { MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = emblems } };
        frame.SetResourceReference(Controls.Bevel.TopLeftProperty, "hi");
        frame.SetResourceReference(Controls.Bevel.BottomRightProperty, "lo");
        frame.SetResourceReference(Controls.Bevel.BackgroundProperty, "panel");
        popup.Child = frame;
        popup.IsOpen = true;
    }

    /// <summary>
    /// "Picture…": a .png of the user's own for the emblem. The mod keeps where it is; its export will copy it into the
    /// mod's folder.
    /// </summary>
    public void ChooseEmblemPicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Texts.Get("Divisions.Pane.Emblem.Picture.Title"), Filter = Texts.Get("Divisions.Pane.Emblem.Picture.Filter") + "|*.png" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        ChangeDetails(change => change.EmblemFile = dialog.FileName);
    }

    /// <summary>"+ Add…": the Add Units window, with the division's units; those standing out already dim.</summary>
    public void AddStandoutUnits()
    {
        if (chosen is null) return;
        Division division = chosen;
        List<Unit> ownUnits = [.. DivisionGroups.Entries(division, unitsByDescriptor).Select(entry => entry.Unit)];
        HashSet<Unit> already = [.. ownUnits.Where(unit => division.StandoutUnits.Contains(unit.Descriptor))];
        MainView.Of(this).AddUnits.Open(Texts.Get("Divisions.Standout.Title", ("division", division.Name)), ownUnits, Game, already,
            units => ChangeDetails(change => change.StandoutUnits = [.. division.StandoutUnits, .. units.Select(unit => unit.Descriptor)]));
    }

    // ---- what the Units tab does for this tab ----

    /// <inheritdoc/>
    public ContextMenu OpenWeaponMenu(Unit unit, UnitWeapon weapon, FrameworkElement at) => unitsTab!.OpenWeaponMenu(unit, weapon, at);

    /// <inheritdoc/>
    public bool CanShowInEditor(Unit unit) => unitsTab?.CanShowInEditor(unit) == true;

    /// <inheritdoc/>
    public void ShowInEditor(Unit unit) => unitsTab?.ShowInEditor(unit);

    /// <inheritdoc/>
    public void NewRuleFor(Unit unit) => unitsTab?.NewRuleFor(unit);

    /// <summary>"Show in Units Tab": the Units tab, with the unit selected and in view, its details in the Unit pane.</summary>
    private void ShowInUnitsTab(Unit unit)
    {
        ShowUnitsTab();
        unitsTab?.ShowUnit(unit);
    }

    /// <summary>Adds a unit to the Units tab's Compare, and shows it there.</summary>
    public void Compare(Unit unit)
    {
        unitsTab?.Compare(unit);
        ShowUnitsTab();
    }

    /// <summary>"New Rule for the Units Shown…": one rule matching the division's units shown, named after the division.</summary>
    private void NewRuleForShown()
    {
        if (chosen is null) return;
        List<string> shownUnits = [.. DivisionGroups.Of(chosen, unitsByDescriptor, Game, factory).SelectMany(group => group.Entries).Select(entry => entry.Unit.Descriptor)];
        unitsTab?.NewRule(chosen.Name, shownUnits);
    }
}
