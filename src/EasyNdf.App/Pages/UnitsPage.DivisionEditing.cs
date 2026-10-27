using System.IO;
using System.Windows;
using EasyNdf.Data;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// What changes a division on the Units tab: adding units to it (from a unit's menu, or + Add Units…), taking them out,
/// changing how it has them, a new division, renaming, duplicating or deleting one, and the Division pane's details.
/// Every change is saved in the mod open on the Rules tab.
/// </summary>
public partial class UnitsPage : IDivisionEditing
{
    // ---- the units' menus ----

    /// <summary>
    /// "Add to Division": one item with a menu of its own: New Division… first, then the mod's new divisions, then the
    /// game's by side and nation, each in a menu of its own; those that have every unit already are dim.
    /// </summary>
    /// <param name="units">The unit right-clicked, or the units selected.</param>
    private MenuChoice?[] AddToDivisionChoices(IReadOnlyList<Unit> units)
    {
        if (loaded is null) return [];
        var choices = new List<MenuChoice?> { new MenuChoice(Texts.Get("Divisions.Menu.AddToNew"), () => _ = AddToNewDivision(units)), Menus.Line };
        List<Division> modsOwn = [.. divisions.Where(IsNewInMod)];
        if (modsOwn.Count > 0)
        {
            choices.Add(Menus.Heading(OpenMod.Name));
            choices.AddRange(modsOwn.Select(division => AddToChoice(division, units)));
            choices.Add(Menus.Line);
        }
        foreach (IGrouping<string, Division> sideGroup in divisions.Where(division => !IsNewInMod(division) && !DivisionChanges.IsRemoved(OpenMod, division.Descriptor)).GroupBy(division => division.Side))
        {
            var byNation = sideGroup.GroupBy(division => division.Country)
                .OrderBy(group => UnitNames.Nation(group.Key), StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
            List<MenuChoice?> nations = [.. byNation.Select(nation => (MenuChoice?)MenuChoice.Submenu(
                nation.Key == "" ? UnitNames.Side(sideGroup.Key) : UnitNames.Nation(nation.Key),
                [.. nation.OrderBy(division => division.ListOrder ?? double.MaxValue).Select(division => AddToChoice(division, units))]))];
            choices.Add(MenuChoice.Submenu(UnitNames.Side(sideGroup.Key), nations));
        }
        return [MenuChoice.Submenu(Texts.Get("Divisions.Menu.AddTo"), choices)];
    }

    /// <summary>A division to add units to; dim, with a tip saying so, when it has every one of them already.</summary>
    private MenuChoice AddToChoice(Division division, IReadOnlyList<Unit> units)
    {
        bool hasAll = units.All(unit => division.Units.Any(rule => rule.Unit == unit.Descriptor));
        return new MenuChoice(division.Name, () => AddToDivision(division, units), IsEnabled: !hasAll,
            Tip: hasAll ? Texts.Get("Divisions.Menu.HasIt.Tip") : "");
    }

    // ---- a division's menu, in the list ----

    /// <summary>
    /// A division's menu: rename, duplicate or delete it (a game division the mod took out is restored instead); give
    /// every unit all its transports, or take them all away, as a freedom deck does; for a game division the mod
    /// changes, put it back as the game has it; copy its name.
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
            isRemoved ? null : new MenuChoice(Texts.Get("Divisions.Transports.GiveAll"), () => GiveEveryUnitAllTransports(division), Tip: Texts.Get("Divisions.Transports.GiveAll.Tip")),
            isRemoved ? null : new MenuChoice(Texts.Get("Divisions.Transports.TakeAll"), () => TakeEveryUnitsTransports(division), Tip: Texts.Get("Divisions.Transports.TakeAll.Tip")),
            isRemoved ? null : Menus.Line,
            inMod is { IsNew: false } ? new MenuChoice(Texts.Get("Divisions.Menu.Revert"), () => _ = RevertDivision(division, inMod)) : null,
            new MenuChoice(Texts.Get("Units.Menu.CopyName"), () => UnitsPage.Copy(division.Descriptor), Key: division.Descriptor),
        ];
        Menus.Show(at, items);
    }

    // ---- the changes ----

    /// <summary>
    /// Adds units to a division, those it has already left as they are, saved in the mod as one change. The user stays
    /// where they are, on whichever division: only a new division is brought into view.
    /// </summary>
    private void AddToDivision(Division division, IReadOnlyList<Unit> units)
    {
        ChangeDivisions(mod => DivisionChanges.AddUnits(mod, GameDivisions, division.Descriptor, units.Select(unit => unit.Descriptor), Rosters));
    }

    /// <summary>
    /// "Add to New Division…": asks for a name, then makes a new division in the mod with the unit alone. It starts with
    /// the details (points, costs, emblem, type) of a game division that has the unit, or else one of its nation.
    /// </summary>
    /// <param name="units">The unit right-clicked, or the units selected: the first one's nation is the division's.</param>
    private async Task AddToNewDivision(IReadOnlyList<Unit> units)
    {
        Unit first = units[0];
        if (DivisionChanges.StartFor(GameDivisions, first.Descriptor, first.Nation, first.Side) is not Division start) return;
        string nation = UnitNames.Nation(first.Nation);
        string question = units.Count == 1
            ? Texts.Get("Divisions.New.Ask", ("nation", nation), ("unit", first.Name))
            : Texts.Get("Divisions.New.AskMany", ("nation", nation), ("count", Texts.Number(units.Count)));
        string name = await MainView.Of(this).Messages.AskName(question, Texts.Get("Divisions.New.Name"));
        if (name != "") MakeNewDivision(name, start, units);
    }

    /// <summary>
    /// "+": asks for a name, then makes a new division in the mod with no units, in the fixed state a new division
    /// starts in (<see cref="NewDivisionStart"/>). Its nation is that of the division shown; with All units, the one
    /// nation ticked in the filter, if one is; else the first division's. It can be changed in the Division pane.
    /// </summary>
    private async void NewDivisionClicked(object sender, RoutedEventArgs e)
    {
        (string Country, string Side)? nation = chosenDivision is Division shown ? (shown.Country, shown.Side) : NationTicked();
        // the frame its files are made from: a game division of its nation (none of its values are kept)
        Division? start = GameDivisions.Where(division => division.IsForMultiplayer)
            .FirstOrDefault(division => nation is null || division.Country == nation.Value.Country)
            ?? GameDivisions.FirstOrDefault(division => division.IsForMultiplayer);
        if (start is null) return;
        nation ??= (start.Country, start.Side);
        string name = await MainView.Of(this).Messages.AskName(
            Texts.Get("Divisions.NewEmpty.Ask", ("nation", UnitNames.Nation(nation.Value.Country))), Texts.Get("Divisions.New.Name"));
        if (name == "") return;
        string descriptor = "";
        if (!ChangeDivisions(mod => descriptor = DivisionChanges.AddNew(mod, GameDivisions, name, start, [], Game, nation))) return;
        ShowDivisionNamed(descriptor);
    }

    /// <summary>The one nation ticked in the filter, with its side; null when none or several are.</summary>
    private (string Country, string Side)? NationTicked()
    {
        if (filter.Nations.Count != 1) return null;
        string country = filter.Nations.First();
        Unit? any = loaded?.Catalog.Units.FirstOrDefault(unit => unit.Nation == country);
        return any is null ? null : (country, any.Side);
    }

    /// <summary>A new division in the mod with some units, of the first one's nation, saved and shown.</summary>
    private void MakeNewDivision(string name, Division start, IReadOnlyList<Unit> units)
    {
        string descriptor = "";
        (string Country, string Side) nation = (units[0].Nation, units[0].Side);
        if (!ChangeDivisions(mod => descriptor = DivisionChanges.AddNew(mod, GameDivisions, name, start, [.. units.Select(unit => unit.Descriptor)], Game, nation))) return;
        ShowUnitIn(descriptor, units[^1]);
    }

    /// <summary>
    /// "+ Add Units…" with a division chosen: the Add Units window, with every unit of the source, those the division
    /// has already dim; the units ticked are added to it. The window's type filter starts on the type the units are
    /// for: the one whose "+ Add Units…" was clicked, else the type tab chosen; the next time it opens, it starts again.
    /// </summary>
    /// <param name="factory">The type to show first, such as "Tanks"; "" for every type.</param>
    private void AddUnitsToDivision(string factory)
    {
        if (loaded is null || chosenDivision is null) return;
        Division division = chosenDivision;
        HashSet<Unit> already = [.. DivisionGroups.Entries(division, unitsByDescriptor).Select(entry => entry.Unit)];
        MainView.Of(this).AddUnits.Open(Texts.Get("Divisions.AddUnits.Title", ("division", division.Name)), loaded.Catalog.Units, Game, already,
            units => AddUnits(division, units), factory, filter.VanillaUnits);
    }

    /// <summary>Adds units to a division, saved in the mod.</summary>
    private void AddUnits(Division division, IReadOnlyList<Unit> units)
    {
        ChangeDivisions(mod => DivisionChanges.AddUnits(mod, GameDivisions, division.Descriptor, units.Select(unit => unit.Descriptor), Rosters));
    }

    /// <summary>A unit's cards, units per card or veterancy as changed in the table, saved in the mod.</summary>
    private bool KeepRule(DivisionUnit rule)
    {
        if (chosenDivision is null) return false;
        string division = chosenDivision.Descriptor;
        return ChangeDivisions(mod => DivisionChanges.SetRule(mod, GameDivisions, division, rule, Rosters));
    }

    /// <inheritdoc/>
    public bool ChangeRule(DivisionUnit rule) => KeepRule(rule);

    /// <summary>
    /// "Give Every Unit All Its Transports", for a freedom deck: every unit of the division that can ride in a transport
    /// gets every transport the game accepts for it, after those it has. One change in the mod, undone as one.
    /// </summary>
    private void GiveEveryUnitAllTransports(Division division)
    {
        List<DivisionUnit> changed = [];
        foreach (DivisionUnit rule in division.Units)
        {
            if (!unitsByDescriptor.TryGetValue(rule.Unit, out Unit? unit)) continue;
            List<string> added = [.. TransportsFor(unit).Select(transport => transport.Descriptor).Where(transport => !rule.Transports.Contains(transport))];
            if (added.Count > 0) changed.Add(rule with { Transports = [.. rule.Transports, .. added] });
        }
        ChangeRules(division, changed);
    }

    /// <summary>
    /// "Take Every Unit's Transports Away": every unit of the division loses its transports, and can be taken without
    /// one, so none is left that cannot be taken at all. One change in the mod, undone as one.
    /// </summary>
    private void TakeEveryUnitsTransports(Division division)
    {
        List<DivisionUnit> changed = [.. division.Units
            .Where(rule => rule.Transports.Count > 0 || !rule.AvailableWithoutTransport)
            .Where(rule => unitsByDescriptor.TryGetValue(rule.Unit, out Unit? unit) && unit.SeatsTaken is not null)
            .Select(rule => rule with { Transports = [], AvailableWithoutTransport = true })];
        ChangeRules(division, changed);
    }

    /// <summary>Several units' lines of a division changed at once, saved in the mod as one change.</summary>
    private void ChangeRules(Division division, List<DivisionUnit> changed)
    {
        if (changed.Count == 0) return;
        ChangeDivisions(mod =>
        {
            foreach (DivisionUnit rule in changed) DivisionChanges.SetRule(mod, GameDivisions, division.Descriptor, rule, Rosters);
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<Unit> TransportsFor(Unit unit) => loaded is null ? [] : Transports.ValidFor(unit, loaded.Catalog);

    /// <summary>
    /// "Remove from Division" and the Delete key: asks first, the button that removes them taking the keyboard, so Enter
    /// confirms; then takes the units out.
    /// </summary>
    private async Task AskToRemoveFromDivision(Division division, IReadOnlyList<Unit> units)
    {
        string question = units.Count == 1
            ? Texts.Get("Divisions.Remove.Ask", ("unit", units[0].Name), ("division", division.Name))
            : Texts.Get("Divisions.Remove.AskMany", ("count", Texts.Number(units.Count)), ("division", division.Name));
        if (await MainView.Of(this).Messages.Confirm(question, Texts.Get("Divisions.Remove.Ok"))) RemoveFromDivision(division, units);
    }

    /// <summary>
    /// The Delete key, outside a box: with a division shown, its selected units are taken out of it; on the Units
    /// sub-tab, the selected units are deleted from the mod (<see cref="DeleteUnits"/>). Each once asked. The Weapons
    /// sub-tab has its own (see WeaponsArmory).
    /// </summary>
    private void DeleteKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None || part == Data.ArmoryPart.Weapons) return;
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        if (chosenDivision is Division division)
        {
            List<Unit> inDivision = [.. selection.All.Where(InChosenDivision)];
            if (inDivision.Count == 0) return;
            e.Handled = true;
            _ = AskToRemoveFromDivision(division, inDivision);
            return;
        }
        if (part != Data.ArmoryPart.Units || selection.All.Count == 0) return;
        e.Handled = true;
        _ = DeleteUnits([.. selection.All]);
    }

    /// <inheritdoc/>
    public void RemoveUnit(Unit unit)
    {
        if (chosenDivision is Division division) _ = AskToRemoveFromDivision(division, [unit]);
    }

    /// <summary>Takes units out of a division, saved in the mod as one change.</summary>
    private void RemoveFromDivision(Division division, IReadOnlyList<Unit> units)
    {
        bool removed = ChangeDivisions(mod =>
        {
            foreach (Unit unit in units) DivisionChanges.RemoveUnit(mod, GameDivisions, division.Descriptor, unit.Descriptor, Rosters);
        });
        if (removed) selection.Selected = null;
    }

    /// <summary>Renames a division, as the user types it; a game division keeps its renaming as a change in the mod.</summary>
    private async Task RenameDivision(Division division)
    {
        string name = await MainView.Of(this).Messages.AskName(Texts.Get("Divisions.Rename.Ask", ("division", division.Name)), division.Name);
        if (name == "" || name == division.Name) return;
        ChangeDivisions(mod => DivisionChanges.Rename(mod, GameDivisions, division.Descriptor, name));
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
        string descriptor = "";
        if (!ChangeDivisions(mod => descriptor = DivisionChanges.Duplicate(mod, GameDivisions, division.Descriptor, name))) return;
        ShowDivisionNamed(descriptor);
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
        ChangeDivisions(mod => DivisionChanges.Delete(mod, division.Descriptor));
    }

    /// <summary>Puts a game division the mod took out back in the game's list.</summary>
    private void RestoreDivision(Division division)
    {
        ChangeDivisions(mod => DivisionChanges.Restore(mod, division.Descriptor));
    }

    /// <summary>Lets go of the mod's changes to a game division, once the user says so: the game's division is shown again.</summary>
    private async Task RevertDivision(Division division, ModDivision inMod)
    {
        if (!await MainView.Of(this).Messages.Confirm(Texts.Get("Divisions.Revert.Ask", ("division", division.Name), ("mod", OpenMod.Name)), Texts.Get("Divisions.Revert.Do"))) return;
        // by its descriptor, as the change is tried on a copy of the mod first
        ChangeDivisions(mod => mod.Divisions.RemoveAll(each => each.Descriptor == inMod.Descriptor));
    }

    /// <summary>
    /// Chooses a division just made (or one a rule on the Rules tab is for), with all its categories, on the Divisions
    /// sub-tab unless Battlegroups shows, and remembers it for next time.
    /// </summary>
    private void ShowDivisionNamed(string descriptor)
    {
        if (part == ArmoryPart.Units)
        {
            part = ArmoryPart.Divisions;
            AppData.Current.State["armoryPart"] = part.ToString();
            ShowPartButtons();
        }
        AppData.Current.State["division"] = descriptor;
        AppData.Current.SaveState();
        filter.Factory = "";
        ApplyMod(descriptor);
    }

    /// <summary>A new division with a unit just made: it is chosen, the unit selected in it.</summary>
    private void ShowUnitIn(string descriptor, Unit unit)
    {
        ShowDivisionNamed(descriptor);
        selection.Selected = unit;
    }

    // ---- the Division pane's changes ----

    /// <inheritdoc/>
    public IReadOnlyList<(string Country, string Side)> Nations =>
        [.. GameDivisions.Where(division => division.Country != "").Select(division => (division.Country, division.Side)).Distinct()];

    /// <inheritdoc/>
    public IReadOnlyList<string> Types => [.. GameDivisions.Select(division => division.TypeToken).Where(type => type != "").Distinct()];

    /// <inheritdoc/>
    public bool Rename(string name)
    {
        if (chosenDivision is null) return false;
        string division = chosenDivision.Descriptor;
        return ChangeDivisions(mod => DivisionChanges.Rename(mod, GameDivisions, division, name));
    }

    /// <inheritdoc/>
    public bool ChangeDetails(Action<ModDivision> change)
    {
        if (chosenDivision is null) return false;
        string division = chosenDivision.Descriptor;
        return ChangeDivisions(mod => DivisionChanges.ChangeDetails(mod, GameDivisions, division, change));
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
    /// "Picture…": a picture of the user's own for the emblem, kept as a plain .png the game's tools read (see
    /// <see cref="GameReadyPicture"/>); its export will copy it into the mod's folder.
    /// </summary>
    public void ChooseEmblemPicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Texts.Get("Divisions.Pane.Emblem.Picture.Title"), Filter = Texts.Get("Divisions.Pane.Emblem.Picture.Filter") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        string? kept = KeepPicture(dialog.FileName);
        if (kept is null)
        {
            _ = MainView.Of(this).Messages.Tell(Texts.Get("Divisions.Pane.Emblem.Picture.NotKept", ("file", dialog.FileName)));
            return;
        }
        ChangeDetails(change => change.EmblemFile = kept);
    }

    /// <summary>
    /// A copy of a chosen picture, kept with the app's data (<see cref="AppFolders.Pictures"/>) as a plain .png, named
    /// after the open mod and the file; null when it cannot be read as a picture or written.
    /// </summary>
    private string? KeepPicture(string chosen)
    {
        string folder = AppData.Current.Folders?.Pictures ?? AppFolders.TemporaryPictures;
        string name = string.Concat((OpenMod.Name + " - " + Path.GetFileNameWithoutExtension(chosen)).Select(letter => Path.GetInvalidFileNameChars().Contains(letter) ? '_' : letter));
        // a name of its own: two pictures of the same name from different folders replaced each other, changing the
        // emblem of a division that used the first
        string kept = Path.Combine(folder, name + ".png");
        for (int copy = 2; File.Exists(kept); copy++) kept = Path.Combine(folder, name + " (" + copy + ").png");
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return GameReadyPicture.Write(chosen, kept) ? kept : null;
    }

    /// <summary>"+ Add…": the Add Units window, with the division's units; those standing out already dim.</summary>
    public void AddStandoutUnits()
    {
        if (chosenDivision is null) return;
        Division division = chosenDivision;
        List<Unit> ownUnits = [.. DivisionGroups.Entries(division, unitsByDescriptor).Select(entry => entry.Unit)];
        HashSet<Unit> already = [.. ownUnits.Where(unit => division.StandoutUnits.Contains(unit.Descriptor))];
        MainView.Of(this).AddUnits.Open(Texts.Get("Divisions.Standout.Title", ("division", division.Name)), ownUnits, Game, already,
            units => ChangeDetails(change => change.StandoutUnits = [.. division.StandoutUnits, .. units.Select(unit => unit.Descriptor)]), vanilla: filter.VanillaUnits);
    }

    // ---- the costs, from the cost buttons in the type headings ----

    /// <inheritdoc/>
    public void ChangeCost(string factory, int card, int cost) => ChangeCosts(factory, costs => costs[card] = cost);

    /// <inheritdoc/>
    public void RemoveCost(string factory, int card) => ChangeCosts(factory, costs => costs.RemoveAt(card));

    /// <inheritdoc/>
    public void AddCost(string factory) => ChangeCosts(factory, costs => costs.Add(costs.Count > 0 ? costs[^1] : 1));

    /// <summary>The chosen division's costs with one type's changed by <paramref name="change"/>, saved as its costs.</summary>
    private void ChangeCosts(string factory, Action<List<int>> change)
    {
        if (chosenDivision is null) return;
        Dictionary<string, List<int>> costs = chosenDivision.Costs.ToDictionary(cost => cost.Key, cost => cost.Value.ToList());
        if (!costs.TryGetValue(factory, out List<int>? list)) costs[factory] = list = [];
        change(list);
        // only this type's heading is drawn again when the mod is saved, not the whole tab
        costsChangedOf = factory;
        // a refused change saves nothing, so nothing clears it: the next save of the mod must draw the whole tab
        if (!ChangeDetails(each => each.Costs = costs)) costsChangedOf = null;
    }

    /// <summary>The type whose costs were just changed, while the change is saved; null otherwise.</summary>
    private string? costsChangedOf;

    /// <inheritdoc/>
    void IDivisionEditing.AddUnits(string factory) => AddUnitsToDivision(factory);

    /// <inheritdoc/>
    public bool CanShowDivisionInEditor => LoadedFile(UnitFiles.Divisions) is not null;

    /// <inheritdoc/>
    public void ShowDivisionInEditor()
    {
        if (chosenDivision is not null) ShowInEditor(LoadedFile(UnitFiles.Divisions), chosenDivision.Descriptor);
    }
}
