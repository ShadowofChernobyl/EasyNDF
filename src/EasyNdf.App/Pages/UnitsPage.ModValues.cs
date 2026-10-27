using EasyNdf.App.Files;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.App.Weapons;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Armory shows the open mod's values (<see cref="OpenModUnits"/>): after a source is read, and whenever the open
/// mod, its rules or the Files tab's files change, the units are read again as the mod's rules make them, and the
/// values they change are marked (<see cref="ModMarks"/>). It happens only while the tab shows; a change made
/// elsewhere is caught up with when it is shown again.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The units as the source has them, before the open mod's rules; null until a source has been read.</summary>
    private LoadedUnits? gameUnits;

    /// <summary>What the units shown were last worked out from (<see cref="OpenModUnits.InputsOf"/>).</summary>
    private string modValuesFrom = "";

    /// <summary>The units as the open mod's rules make them, before the units it adds in the Armory are put in; null until read.</summary>
    private ModUnits? unitsByRules;

    /// <summary>The units the open mod adds and changes in the Armory as they were last shown, as the mod file keeps them.</summary>
    private string addedUnitsShown = "";

    /// <summary>Counts the reads started, so one that a newer one has overtaken is ignored when it ends.</summary>
    private int modValueReads;

    private RunOnceSoon modValuesSoon = null!;

    /// <summary>Listens for changes to the mods; the Files tab's changes are passed on by the main view.</summary>
    private void PrepareModValues()
    {
        modValuesSoon = new RunOnceSoon(ShowModValuesSoon);
        AppData.Current.ModsChanged += _ => ModValuesMayHaveChanged();
        // "Show mods' changes" turned on or off: the divisions follow at once, the units once read again
        ModChangesSwitch.Changed += () =>
        {
            if (loaded is null) return;
            ModChanged();
            ModValuesMayHaveChanged();
        };
    }

    /// <summary>
    /// Something the mod's values depend on may have changed: they are worked out again soon while the tab shows,
    /// or when it is next shown.
    /// </summary>
    public void ModValuesMayHaveChanged()
    {
        if (IsVisible && gameUnits is not null) modValuesSoon.Ask();
    }

    /// <summary>
    /// <see cref="ShowModValues"/>, asked for by a change. Awaited here, so an error in it reaches the app's error
    /// message (and Errors.txt) instead of being lost with its task.
    /// </summary>
    private async void ShowModValuesSoon() => await ShowModValues();

    /// <summary>
    /// The setting "Mark changed values" turned on or off: everything is drawn again, with the marks or without them;
    /// while the tab is hidden, when it is next shown.
    /// </summary>
    public void MarksSettingChanged()
    {
        if (loaded is null) return;
        if (!IsVisible)
        {
            redrawWhenShown = true;
            return;
        }
        ShowAll();
    }

    /// <summary>
    /// Reads the units as the open mod makes them, unless nothing they depend on has changed, and shows them with
    /// their changes marked, and the units the mod adds in the Armory with them. The unit selected, the units compared
    /// and the division chosen stay.
    /// </summary>
    private async Task ShowModValues()
    {
        if (gameUnits is not LoadedUnits game || MainView.Find(this) is not MainView main) return;
        var entries = main.FilesTab.Model.Entries.ToList();
        var settings = RunSettings.From(AppData.Current.Settings);
        // with "Show mods' changes" off, the units are shown as the source has them
        string inputs = ArmoryShowsModChanges ? OpenModUnits.InputsOf(entries, AppData.Current.OpenMod, settings) : "as the source has them";
        bool readNow = inputs != modValuesFrom;
        if (readNow && !ArmoryShowsModChanges)
        {
            modValuesFrom = inputs;
            ++modValueReads;
            unitsByRules = new ModUnits(game.Catalog, UnitChanges.None);
        }
        else if (readNow)
        {
            modValuesFrom = inputs;
            int mine = ++modValueReads;
            ModUnits read;
            try
            {
                read = await OpenModUnits.Read(game, entries, AppData.Current.OpenMod, settings);
            }
            catch (Exception problem) when (problem is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException
                or Engine.Ndf.NdfParseException)
            {
                // the game's files could not be read again (moved, or in use), or a file on the Files tab is not valid NDF
                // (the Files tab says so when it is run): the values stay as they are, and are worked out again at the
                // next change
                modValuesFrom = "";
                return;
            }
            // a newer read, or another source read meanwhile, wins
            if (mine != modValueReads || !ReferenceEquals(game, gameUnits)) return;
            unitsByRules = read;
        }
        // the units the mod adds and changes are not in its rules' run (their rules are locked), so they are looked at on their own
        string added = AddedUnitsText();
        if (!readNow && added == addedUnitsShown) return;
        addedUnitsShown = added;
        ShowUnitsMadeBy(unitsByRules ?? new ModUnits(game.Catalog, UnitChanges.None));
    }

    /// <summary>
    /// "Some of your mod's changes aren't shown", while a game mod is the source and files on the Files tab with the
    /// open mod's rules are not that game mod's own copies; the tooltip names them and says why.
    /// </summary>
    private void ShowFilesNotShown(IReadOnlyList<string> files, Data.Units.UnitSource source)
    {
        NotShownText.Visibility = files.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        NotShownText.ToolTip = files.Count == 0 ? null
            : Texts.Get("Units.ModFilesNotShown.Tip", ("files", string.Join(", ", files.Distinct(StringComparer.OrdinalIgnoreCase))), ("source", source.Name));
    }

    /// <summary>The units and weapons the open mod adds and changes in the Armory, as the mod file keeps them, to tell when they change.</summary>
    private static string AddedUnitsText() =>
        string.Join("\n", new[] { "shown " + ShowsOpenMod }.Concat(OpenMod.Units.Select(unit => unit.ToJson().ToJsonString())
            .Concat(OpenMod.UnitEdits.Select(edit => "edit " + edit.ToJson().ToJsonString()))
            .Concat(OpenMod.WeaponEdits.Select(edit => "weapon edit " + edit.ToJson().ToJsonString()))
            .Concat(OpenMod.Weapons.Select(weapon => "weapon " + weapon.ToJson().ToJsonString()))));

    /// <summary>
    /// Shows the units as the open mod's rules make them, with the units it adds in the Armory; nothing is drawn again
    /// when neither before nor now does the mod change anything.
    /// </summary>
    private void ShowUnitsMadeBy(ModUnits byRules)
    {
        if (gameUnits is not LoadedUnits game) return;
        ShowFilesNotShown(byRules.FilesNotShown, game.Source);
        ModUnits shown = WithWeaponChanges(WithAddedUnits(WithEditedUnits(byRules, game.Catalog)), game.Catalog);
        bool nothingBefore = ModMarks.Changes.Count == 0 && WeaponMarks.Changes.Count == 0 && ReferenceEquals(loaded?.Catalog, game.Catalog);
        bool nothingNow = shown.Changes.Count == 0 && shown.WeaponChanges.Count == 0 && ReferenceEquals(shown.Catalog, game.Catalog);
        if (nothingBefore && nothingNow) return;
        ModMarks.Changes = shown.Changes;
        WeaponMarks.Changes = shown.WeaponChanges;
        loaded = game with { Catalog = shown.Catalog };
        ShowUnitsAgain();
    }

    /// <summary>
    /// The units with the open mod's changes to them in the Armory (<see cref="UnitEdit"/>), each made on the unit as
    /// the mod's rules make it, and marked against the game's. The same units are given back when the mod changes none.
    /// </summary>
    /// <param name="byRules">The units as the mod's rules make them.</param>
    /// <param name="game">The units as the source has them, before any rule, for the marks.</param>
    private static ModUnits WithEditedUnits(ModUnits byRules, UnitCatalog game)
    {
        if (!ShowsOpenMod || OpenMod.UnitEdits.Count == 0) return byRules;
        var gameByDescriptor = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in game.Units) gameByDescriptor.TryAdd(unit.Descriptor, unit);
        var editsByDescriptor = new Dictionary<string, UnitEdit>(StringComparer.Ordinal);
        foreach (UnitEdit edit in OpenMod.UnitEdits) editsByDescriptor.TryAdd(edit.Descriptor, edit);

        var units = new List<Unit>(byRules.Catalog.Units.Count);
        var marks = new List<(Unit Game, Unit Shown, IReadOnlyCollection<string> Fields, string Rule)>();
        Dictionary<string, Weapon> weapons = WeaponsByDescriptor(byRules.Catalog);
        foreach (Unit unit in byRules.Catalog.Units)
        {
            if (!editsByDescriptor.TryGetValue(unit.Descriptor, out UnitEdit? edit) || !gameByDescriptor.TryGetValue(unit.Descriptor, out Unit? gameUnit))
            {
                units.Add(unit);
                continue;
            }
            Unit shown = edit.WithLoadoutChanges(edit.Apply(unit), unit.Descriptor, byRules.Catalog.Loadouts, weapons);
            units.Add(shown);
            // the weapons its changes swap, take off or add are its change's too
            IEnumerable<string> weaponFields = edit.LoadoutChanges.Count == 0 ? [] : UnitChanges.FieldsChanged(unit, shown).Where(IsWeaponField);
            marks.Add((gameUnit, shown, [.. edit.FieldsChanged().Concat(weaponFields)], UnitChangeRules.EditRuleName(edit, gameUnit, Texts.Catalog)));
        }
        if (marks.Count == 0) return byRules;
        UnitCatalog catalog = byRules.Catalog with { Units = units };
        return byRules with { Catalog = catalog, Changes = byRules.Changes.WithEdited(OpenMod.Name, marks) };
    }

    /// <summary>
    /// The units with those the open mod adds in the Armory, each made from its unit as the mod's rules make that one,
    /// as the export copies it from the file those rules give. A unit the source has already, because the source is
    /// the mod's own export, is shown as the mod has it now, in its place: the export is an older copy, without the
    /// changes made since. The others are put in after the source's. A unit made from one the source does not have is
    /// left out. The same units are given back when the mod adds none.
    /// </summary>
    private static ModUnits WithAddedUnits(ModUnits byRules)
    {
        if (!ShowsOpenMod || OpenMod.Units.Count == 0) return byRules;
        var byDescriptor = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in byRules.Catalog.Units) byDescriptor.TryAdd(unit.Descriptor, unit);
        var made = new Dictionary<string, Unit>(StringComparer.Ordinal);
        Dictionary<string, Weapon> weapons = WeaponsByDescriptor(byRules.Catalog);
        foreach (ModUnit unit in OpenMod.Units)
            if (byDescriptor.TryGetValue(unit.MadeFrom, out Unit? madeFrom))
                made.TryAdd(unit.Descriptor, unit.WithLoadoutChanges(unit.Apply(madeFrom), unit.Descriptor, byRules.Catalog.Loadouts, weapons));
        if (made.Count == 0) return byRules;

        // an earlier export of a unit gives way to the unit as the mod has it, where the export put it
        List<Unit> units = [.. byRules.Catalog.Units.Select(unit => made.GetValueOrDefault(unit.Descriptor) ?? unit)];
        units.AddRange(made.Values.Where(unit => !byDescriptor.ContainsKey(unit.Descriptor)));
        UnitCatalog catalog = byRules.Catalog with { Units = units };
        return byRules with { Catalog = catalog, Changes = byRules.Changes.WithAdded(OpenMod.Name, made.Keys) };
    }

    /// <summary>Whether a unit's field is one of its weapons' (<see cref="UnitField.OfWeapon"/>), their count or longest range.</summary>
    private static bool IsWeaponField(string field) =>
        field.StartsWith("Weapon", StringComparison.Ordinal) || field == UnitField.LongestRange;

    /// <summary>A catalog's weapons by descriptor.</summary>
    private static Dictionary<string, Weapon> WeaponsByDescriptor(UnitCatalog catalog)
    {
        var weapons = new Dictionary<string, Weapon>(StringComparer.Ordinal);
        foreach (Weapon weapon in catalog.Weapons ?? []) weapons.TryAdd(weapon.Descriptor, weapon);
        return weapons;
    }

    /// <summary>
    /// The units and weapons with the open mod's weapon changes in the Armory (<see cref="ModWeaponView"/>): its
    /// changes to the game's weapons and the copies it adds, carried by the units given them, all marked against the
    /// game's. The same units are given back when the mod changes no weapon.
    /// </summary>
    /// <param name="shown">The units as the mod's rules and its other Armory changes make them.</param>
    /// <param name="game">The units as the source has them, before any rule, for the marks.</param>
    private static ModUnits WithWeaponChanges(ModUnits shown, UnitCatalog game)
    {
        if (!ShowsOpenMod) return shown;
        ModWeaponView.Result result = ModWeaponView.Apply(OpenMod, shown.Catalog, game, Texts.Catalog);
        if (ReferenceEquals(result.Catalog, shown.Catalog)) return shown;
        return shown with
        {
            Catalog = result.Catalog,
            Changes = shown.Changes.WithEdited(OpenMod.Name, result.UnitMarks),
            WeaponChanges = shown.WeaponChanges.WithArmory(OpenMod.Name, result.WeaponMarks),
        };
    }

    /// <summary>
    /// Shows the units just read in place of those shown, keeping what was selected and compared: the same units, by
    /// their descriptors, as read now.
    /// </summary>
    private void ShowUnitsAgain()
    {
        if (loaded is null) return;
        var byDescriptor = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in loaded.Catalog.Units) byDescriptor.TryAdd(unit.Descriptor, unit);
        Unit? Now(Unit? unit) => unit is not null && byDescriptor.TryGetValue(unit.Descriptor, out Unit? found) ? found : null;

        List<Unit> stillCompared = [.. compared.Select(Now).OfType<Unit>()];
        compared.Clear();
        compared.AddRange(stillCompared);
        pinned = Now(pinned);
        // the units selected are their new copies before the view is drawn, which keeps only units it shows
        selection.Renew(Now);
        // the same source read again: the types folded away stay folded
        HashSet<string> folded = [.. foldedCategories];
        HashSet<string> foldedInBattlegroup = [.. foldedBattlegroupCategories];
        ReadDivisions();
        foldedCategories.UnionWith(folded);
        foldedBattlegroupCategories.UnionWith(foldedInBattlegroup);
        ShowAll();
    }
}
