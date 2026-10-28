using System.Text.Json.Nodes;
using EasyNdf.App.Divisions;
using EasyNdf.App.Mods;
using EasyNdf.App.Rules;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Pages;

/// <summary>
/// Every division change made in the Armory goes through <see cref="ChangeDivisions"/>: it is refused when it needs a
/// file that is not on the Files tab (with a message saying which, every time); otherwise it is made, the mod's
/// Armory presets are made again from the changes (<see cref="ArmoryPresets"/>), and the mod is saved. Nothing
/// is kept that the Rules tab does not show.
/// </summary>
public partial class UnitsPage
{
    /// <summary>
    /// Makes a division change, unless a file it is written into is not on the Files tab. The change is tried on a
    /// copy of the mod first, so a refused one changes nothing. A change that only takes things back needs no file.
    /// </summary>
    /// <param name="change">The change, made to the mod given: once on the copy, then on the open mod.</param>
    /// <returns>Whether it was made.</returns>
    private bool ChangeDivisions(Action<Mod> change)
    {
        // a change made while it can't be seen would be made blind
        if (!ArmoryShowsModChanges)
        {
            _ = MainView.Of(this).Messages.Tell(Texts.Get("Units.ModChanges.OffEdit"));
            return false;
        }
        if (!ModStack.IsOn(OpenMod))
        {
            _ = MainView.Of(this).Messages.Tell(Texts.Get("Units.ModChanges.ModOffEdit", ("mod", OpenMod.Name)));
            return false;
        }
        if (WhyRulesCannotBeMade() is string reason)
        {
            _ = MainView.Of(this).Messages.Tell(reason);
            return false;
        }
        List<string> missing = FilesMissingFor(change);
        if (missing.Count > 0)
        {
            _ = MainView.Of(this).Messages.Tell(Texts.Count("Divisions.FilesMissing", missing.Count, ("files", string.Join(", ", missing))));
            return false;
        }
        change(OpenMod);
        SaveDivisionChanges();
        return true;
    }

    /// <summary>
    /// The files a change would write into that are not loaded on the Files tab: those of the rules it adds or
    /// changes in the Armory presets, and UniteDescriptor.ndf for a generated division, whose units are made from it.
    /// UNITS.csv is not among them: it is the mod's own, and the export makes it when the Files tab has none.
    /// </summary>
    private List<string> FilesMissingFor(Action<Mod> change)
    {
        Mod trial = OpenMod.DeepCopy();
        change(trial);
        // copies, as working the rules out gives new divisions their deck numbers
        HashSet<string> before = [.. RulesOf(OpenMod.DeepCopy()).Select(rule => rule!.ToJsonString())];
        List<JsonNode?> changed = [.. RulesOf(trial).Where(rule => !before.Contains(rule!.ToJsonString()))];
        List<string> files = [.. changed
            .Select(rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.FileKey])
            .OfType<string>()];
        // a generated division's units are made at export from the units on the Files tab
        if (GeneratedUnitLists.AnyIn(changed)) files.Add(Files.GeneratedListsMaker.UnitsFile);
        // and a generated battlegroup is chosen by its units' weapons, and written into Decks.ndf and DeckPacks.ndf
        if (GeneratedBattlegroups.AnyIn(changed)) files.AddRange(Files.GeneratedListsMaker.BattlegroupFiles);
        IEnumerable<string> needed = files.Distinct(StringComparer.OrdinalIgnoreCase);
        var entries = MainView.Of(this).FilesTab.Model.Entries;
        return [.. needed.Where(file => !entries.Any(entry => entry.Source is not null && string.Equals(entry.Name, file, StringComparison.OrdinalIgnoreCase)))];
    }

    private JsonArray RulesOf(Mod mod) => ArmoryPresets.RulesFor(mod, GameDivisions, Texts.Catalog, UnitNameOf, GameBattlegroupCards, GameUnits, GameWeapons, GameLoadouts);

    /// <summary>
    /// The source's loadouts before the open mod's rules, which units' weapon changes are made on: without them, no
    /// rule for a unit's weapons is made.
    /// </summary>
    private IReadOnlyDictionary<string, Data.Weapons.Loadout>? GameLoadouts => gameUnits?.Catalog.Loadouts;

    /// <summary>The source's units before the open mod's rules, which the units the mod adds are made from.</summary>
    private IReadOnlyList<Unit> GameUnits => gameUnits?.Catalog.Units ?? [];

    /// <summary>The battlegroup cards the game has, so a mod's rules add only those it lacks.</summary>
    private IReadOnlySet<string>? GameBattlegroupCards => gameUnits?.Catalog.BattlegroupCards;

    /// <summary>
    /// Why the Armory presets cannot be made again now; null when it can. Made again without a change's rules, the
    /// change would still be saved in the mod but shown nowhere and never exported, so it is not made at all:
    /// <list type="bullet">
    /// <item>Steel Division 2 lays its divisions out differently: its changes cannot be written as rules yet;</item>
    /// <item>without the source's divisions no rule can be made;</item>
    /// <item>a change whose starting division the source does not have (another source chosen, or a game update that
    /// renamed the division) cannot be written either; nor can a unit the mod adds whose unit it is made from the source
    /// does not have, nor a change to a unit the source does not have, nor a change to a weapon it does not have, nor a
    /// copy of one, nor a change to a unit's weapons whose loadout it does not have.</item>
    /// </list>
    /// </summary>
    private string? WhyRulesCannotBeMade()
    {
        if (Game != Data.Game.GameTitle.Warno) return Texts.Get("Divisions.NotForThisGame", ("game", Game.Name));
        if (GameDivisions.Count == 0) return Texts.Get("Rules.Locked.ArmoryNotRead");
        ModDivision? lost = OpenMod.Divisions.FirstOrDefault(entry => !GameDivisions.Any(division => division.Descriptor == entry.StartsFrom));
        if (lost is not null) return Texts.Get("Divisions.StartMissing", ("division", lost.Name != "" ? lost.Name : lost.Descriptor), ("start", lost.StartsFrom));
        ModUnit? lostUnit = OpenMod.Units.FirstOrDefault(unit => !GameUnits.Any(each => each.Descriptor == unit.MadeFrom));
        if (lostUnit is not null) return Texts.Get("Units.New.MadeFromMissing", ("unit", lostUnit.Name), ("from", lostUnit.MadeFrom));
        UnitEdit? lostEdit = OpenMod.UnitEdits.FirstOrDefault(edit => !GameUnits.Any(each => each.Descriptor == edit.Descriptor));
        if (lostEdit is not null) return Texts.Get("Units.Edit.UnitMissing", ("unit", lostEdit.Name ?? lostEdit.Descriptor));
        WeaponEdit? lostWeaponEdit = OpenMod.WeaponEdits.FirstOrDefault(edit => !GameWeapons.Any(each => each.Descriptor == edit.Descriptor));
        if (lostWeaponEdit is not null) return Texts.Get("Weapons.Edit.WeaponMissing", ("weapon", lostWeaponEdit.Descriptor));
        ModWeapon? lostCopy = OpenMod.Weapons.FirstOrDefault(copy =>
            !GameWeapons.Any(each => each.Descriptor == copy.MadeFrom) && !OpenMod.Weapons.Any(other => other.Descriptor == copy.MadeFrom));
        if (lostCopy is not null) return Texts.Get("Weapons.Copy.MadeFromMissing", ("weapon", lostCopy.Name), ("from", lostCopy.MadeFrom));
        UnitValues? lostLoadout = OpenMod.Units.Cast<UnitValues>().Concat(OpenMod.UnitEdits)
            .FirstOrDefault(values => values.LoadoutFrom is string from && gameUnits?.Catalog.Loadouts?.ContainsKey(from) != true);
        if (lostLoadout is not null) return Texts.Get("Weapons.Loadout.Missing", ("loadout", lostLoadout.LoadoutFrom));
        return null;
    }

    /// <summary>
    /// Why the Rules tab cannot change the Armory's rules now (delete them, or make them editable); null when it can.
    /// </summary>
    public string? ArmoryRulesBlockedReason => WhyRulesCannotBeMade();

    /// <summary>The Armory presets made again from the mod's changes, and the mod saved.</summary>
    private void SaveDivisionChanges()
    {
        // made again without some changes' rules, those changes would go unseen (see WhyRulesCannotBeMade)
        if (WhyRulesCannotBeMade() is not null) return;
        ArmoryPresets.Update(OpenMod, GameDivisions, Texts.Catalog, UnitNameOf, ArmoryPartTexts.Name, GameBattlegroupCards, GameUnits, GameWeapons, GameLoadouts);
        AppData.Current.Save(OpenMod);
    }

    /// <summary>
    /// A mod with division changes made before they were rules has no Armory preset yet, and one saved before the
    /// Armory had parts keeps all its rules in one: its presets are made the first time the Armory reads the mod's
    /// divisions, so its changes show on the Rules tab, each under its own part.
    /// </summary>
    private void MakeArmoryPresetsIfMissing()
    {
        if (GameDivisions.Count == 0 || !ArmoryPresetsNeedMaking()) return;
        // after the divisions are shown: saving tells every tab, this one too, which would draw it again meanwhile
        Dispatcher.BeginInvoke(() =>
        {
            if (ArmoryPresetsNeedMaking()) SaveDivisionChanges();
        });
    }

    /// <summary>
    /// Whether the open mod's Armory presets are missing for its changes, keep rules in the wrong part's preset, or lack
    /// the rules of units' weapon changes (an earlier version made the presets without them).
    /// </summary>
    private bool ArmoryPresetsNeedMaking() =>
        (HasArmoryChanges(OpenMod) && !ArmoryPresets.HasAny(OpenMod)) || ArmoryPresets.HasRulesOutOfPlace(OpenMod) || LacksLoadoutRules(OpenMod)
        || LacksDeckNumberRules(OpenMod);

    /// <summary>
    /// Whether a number for deck codes the mod gives a unit has no rule in its Armory presets: a mod file can carry the
    /// numbers without their rules (one made by another tool).
    /// </summary>
    private static bool LacksDeckNumberRules(Mod mod)
    {
        if (mod.UnitDeckNumbers.Count == 0) return false;
        HashSet<string> numbered = [.. ArmoryPresets.RulesOf(mod)
            .Where(rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.ChangeKey] == UnitChangeRules.DeckNumberChange)
            .Select(rule => (string?)rule?[DivisionChangeRules.ArmoryKey]?[UnitChangeRules.UnitKey]).OfType<string>()];
        return mod.UnitDeckNumbers.Keys.Any(unit => !numbered.Contains(unit));
    }

    /// <summary>
    /// Whether a unit of the mod has weapon changes the Armory presets have no rule for, in WeaponDescriptor.ndf, while
    /// the source has the loadout they are made on (so the rules can be made).
    /// </summary>
    private bool LacksLoadoutRules(Mod mod)
    {
        var unitsWithRules = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.Json.Nodes.JsonNode? rule in ArmoryPresets.RulesOf(mod))
            if ((string?)rule?["Armory"]?["File"] == UnitChangeRules.LoadoutsFile && (string?)rule?["Armory"]?["Unit"] is string unit) unitsWithRules.Add(unit);
        IEnumerable<(string Descriptor, UnitValues Values)> changed =
            mod.Units.Select(unit => (unit.Descriptor, (UnitValues)unit)).Concat(mod.UnitEdits.Select(edit => (edit.Descriptor, (UnitValues)edit)));
        return changed.Any(each => each.Values.LoadoutChanges.Count > 0 && !unitsWithRules.Contains(each.Descriptor)
            && each.Values.LoadoutFrom is string from && GameLoadouts?.ContainsKey(from) == true);
    }

    /// <summary>Whether a mod has changes made in the Armory, which its Armory presets hold as rules.</summary>
    private static bool HasArmoryChanges(Mod mod) =>
        mod.Divisions.Count > 0 || mod.Units.Count > 0 || mod.UnitEdits.Count > 0 || mod.WeaponEdits.Count > 0 || mod.Weapons.Count > 0;

    /// <summary>
    /// The division marks (<see cref="DivisionMarks"/>) compare the division shown with the source's own, before any
    /// rule, and name the Armory presets' rules.
    /// </summary>
    private void UseDivisionMarks() =>
        DivisionMarks.Use(gameUnits?.Catalog.Divisions ?? [], chosenDivision, OpenMod.Name, ShowsOpenMod ? ArmoryPresets.RulesOf(OpenMod) : [], GeneratedBeforeChanges());

    /// <summary>
    /// The generated division chosen as its generation makes it, before the user's own changes to its units; null when
    /// the division chosen is not a generated one.
    /// </summary>
    private Division? GeneratedBeforeChanges()
    {
        if (chosenDivision is null || EntryShown(chosenDivision.Descriptor) is not { IsGenerated: true } entry) return null;
        Division? start = GameDivisions.FirstOrDefault(division => division.Descriptor == entry.StartsFrom);
        if (start is null) return null;
        ModDivision plain = entry.Copy();
        plain.Added = [];
        plain.Removed = [];
        return plain.Apply(Rosters.StartOf(plain, start));
    }

    /// <summary>
    /// The units of the generated division chosen whose numbers are as its generation makes them, which the table shows
    /// dimmed; empty for any other division.
    /// </summary>
    private IReadOnlySet<string> UnitsAsGenerated()
    {
        if (chosenDivision is null || EntryShown(chosenDivision.Descriptor) is not { IsGenerated: true } entry) return new HashSet<string>();
        return new HashSet<string>(chosenDivision.Units.Select(unit => unit.Unit).Where(unit => !entry.Added.Any(added => added.Unit == unit)), StringComparer.Ordinal);
    }

    /// <summary>A unit's name as the Armory shows it, by its descriptor; the descriptor for a unit the source does not have.</summary>
    private string UnitNameOf(string descriptor) => unitsByDescriptor.TryGetValue(descriptor, out Unit? unit) ? unit.Name : descriptor;

    /// <summary>
    /// Whether the Armory has read a source's divisions: the Armory presets are made from them, so without them it
    /// could not be made again.
    /// </summary>
    public bool CanMakeEditable => WhyRulesCannotBeMade() is null;

    /// <summary>
    /// Make Editable, from the Rules tab: the change's rules are kept by hand from now on, and the Armory lets go of
    /// the change.
    /// </summary>
    public void MakeEditable(JsonNode rule)
    {
        if (!CanMakeEditable) return;
        ArmoryPresets.MakeEditable(OpenMod, GameDivisions, rule);
        SaveDivisionChanges();
    }

    /// <summary>
    /// Rules of an Armory preset deleted on the Rules tab: the Armory takes their changes back (see
    /// <see cref="ArmoryPresets.TakeBack"/>), and the presets are made again without them.
    /// </summary>
    public void TakeBack(IReadOnlyList<JsonNode?> rules)
    {
        if (!CanMakeEditable) return;
        ArmoryPresets.TakeBack(OpenMod, GameDivisions, rules);
        SaveDivisionChanges();
    }

    /// <summary>
    /// Show in Armory, from the Rules tab: the division the rule is for is chosen in the Armory, the unit it adds is
    /// selected on the Units sub-tab, or the weapon it changes on the Weapons sub-tab.
    /// </summary>
    public void ShowWhatRuleChanges(JsonNode rule)
    {
        if ((string?)rule[DivisionChangeRules.ArmoryKey]?[WeaponChangeRules.WeaponKey] is string weapon)
        {
            ShowWeapon(weapon);
            return;
        }
        if ((string?)rule[DivisionChangeRules.ArmoryKey]?[UnitChangeRules.UnitKey] is string added)
        {
            if (loaded?.Catalog.Units.FirstOrDefault(unit => unit.Descriptor == added) is Unit unit) ShowInUnits(unit);
            return;
        }
        if ((string?)rule[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.DivisionKey] is string division) ShowDivisionNamed(division);
    }
}
