using System.Globalization;
using System.Text.RegularExpressions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// A unit's changes to its weapons (<see cref="LoadoutChange"/>): what they make of its loadout, as the Armory shows it,
/// which swaps the game takes, and the actions that make them in the unit's own loadout. The changes are by places in
/// the loadout as the game has it, so the actions go in an order that keeps those places right: the weapons taken off
/// first (each found by its text), then the swaps and the numbers of weapons (each place less the weapons taken off
/// before it on its turret), then the ammo boxes' salvos, then the weapons added, each after its turret's own, with an
/// ammo box of its own. Steel Division 2's loadouts are written with its own names and forms: ammunition named "~/Ammo_X",
/// the box as SalvoStockIndex, in the first of the places its list of 29 leaves unused (-1), with an entry of
/// SalvoIsMainSalvo for it; it has no number of a weapon at one place (NbWeapons), which is part of the weapon itself
/// there ("Ammo_Thompson_x2").
/// </summary>
public static class LoadoutRules
{
    /// <summary>Where the game keeps its loadouts and ammunition, as a unit and a loadout name them.</summary>
    public const string ReferencePrefix = "$/GFX/Weapon/";

    /// <summary>
    /// How Steel Division 2 names them: a unit its loadout as "$/GFX/Everything/WeaponDescriptor_X", a loadout its
    /// ammunition as "~/Ammo_X" (see the Steel Division 2 notes, sections 2.1 and 3.1).
    /// </summary>
    private const string SteelDivisionLoadoutPrefix = "$/GFX/Everything/", SteelDivisionAmmunitionPrefix = "~/";

    /// <summary>A loadout as a unit names it in a game: "$/GFX/Weapon/X" in WARNO, "$/GFX/Everything/X" in Steel Division 2.</summary>
    public static string LoadoutReference(string loadout, GameTitle? game) =>
        (game == GameTitle.SteelDivision2 ? SteelDivisionLoadoutPrefix : ReferencePrefix) + loadout;

    /// <summary>An ammunition as a loadout names it in a game: "$/GFX/Weapon/X" in WARNO, "~/X" in Steel Division 2.</summary>
    public static string AmmunitionReference(string ammunition, GameTitle? game) =>
        (game == GameTitle.SteelDivision2 ? SteelDivisionAmmunitionPrefix : ReferencePrefix) + ammunition;

    /// <summary>
    /// Whether a kind of change (<see cref="LoadoutChange"/>) can be written for a game: all of them for WARNO; for
    /// Steel Division 2 all but the number of a weapon at one place, which it does not keep in the loadout.
    /// </summary>
    public static bool Writes(GameTitle game, string kind) => game != GameTitle.SteelDivision2 || kind != LoadoutChange.Count;

    /// <summary>What a weapon added writes for its ammo box and its number, filled in when the rules are made.</summary>
    public const string AmmoBoxBlank = "{AmmoBox}", NumberBlank = "{Number}";

    private const string Separator = ActionApplier.PathSeparator;

    /// <summary>
    /// The name of a unit's own loadout, made from its descriptor as the game names its own:
    /// "Descriptor_Unit_M1A1_Abrams_US" has "WeaponDescriptor_EZ_M1A1_Abrams_US", and a unit the mod adds,
    /// "Descriptor_Unit_EZ_Hunter_US", has "WeaponDescriptor_EZ_Hunter_US" ("Descriptor_Unit_EZ2_Hunter_US",
    /// "WeaponDescriptor_EZ2_Hunter_US").
    /// </summary>
    public static string OwnLoadoutName(string unitDescriptor)
    {
        string rest = unitDescriptor.StartsWith("Descriptor_Unit_", StringComparison.Ordinal) ? unitDescriptor["Descriptor_Unit_".Length..] : unitDescriptor;
        if (rest.StartsWith("EZ_", StringComparison.Ordinal)) rest = rest[3..];
        else if (System.Text.RegularExpressions.Regex.IsMatch(rest, @"^EZ\d+_")) return "WeaponDescriptor_" + rest;
        return "WeaponDescriptor_EZ_" + rest;
    }

    /// <summary>
    /// A loadout with the changes made, under another name: the weapons taken off gone, the swapped ones and the
    /// numbers changed, the boxes' salvos changed, and the weapons added after their turret's own, each with an ammo box
    /// of its own and the next weapon number.
    /// </summary>
    public static Loadout Changed(Loadout loadout, IReadOnlyList<LoadoutChange> changes, string name)
    {
        var boxes = new List<int>(loadout.AmmoBoxes);
        foreach (LoadoutChange salvos in changes.Where(change => change.Kind == LoadoutChange.Salvos && change.Mount < boxes.Count))
            boxes[salvos.Mount] = salvos.Number;
        int number = loadout.HighestWeaponNumber;
        using IEnumerator<int> freeBoxes = FreeBoxes(loadout).GetEnumerator();
        var turrets = new List<LoadoutTurret>();
        foreach (LoadoutTurret turret in loadout.Turrets)
        {
            var mounts = new List<LoadoutMount>();
            foreach (LoadoutMount mount in turret.Mounts)
            {
                if (changes.Any(change => change.Kind == LoadoutChange.Remove && change.Turret == turret.Index && change.Mount == mount.Index)) continue;
                LoadoutChange? swap = LastAt(changes, LoadoutChange.Swap, mount);
                LoadoutChange? count = LastAt(changes, LoadoutChange.Count, mount);
                mounts.Add(mount with { Index = mounts.Count, Ammunition = swap?.Ammunition ?? mount.Ammunition, Count = count?.Number ?? mount.Count });
            }
            foreach (LoadoutChange add in changes.Where(change => change.Kind == LoadoutChange.Add && change.Turret == turret.Index))
            {
                number++;
                freeBoxes.MoveNext();
                int box = freeBoxes.Current;
                if (box < boxes.Count) boxes[box] = add.Number;
                else boxes.Add(add.Number);
                mounts.Add(new LoadoutMount(turret.Index, mounts.Count, add.Ammunition, box, AddedCount(add), "", number));
            }
            turrets.Add(turret with { Mounts = mounts });
        }
        return loadout with { Descriptor = name, AmmoBoxes = boxes, Turrets = turrets };
    }

    /// <summary>
    /// The ammo boxes the weapons added get, in the order they are added: the places the loadout's list leaves unused
    /// (-1, as Steel Division 2 fills its list of 29), then new places after its last.
    /// </summary>
    public static IEnumerable<int> FreeBoxes(Loadout loadout)
    {
        for (int box = 0; box < loadout.AmmoBoxes.Count; box++)
            if (loadout.AmmoBoxes[box] == -1) yield return box;
        for (int box = loadout.AmmoBoxes.Count; ; box++) yield return box;
    }

    /// <summary>The ammo box a weapon added gets (<see cref="FreeBoxes"/>), by its place among the weapons added.</summary>
    public static int BoxOf(Loadout loadout, IReadOnlyList<LoadoutChange> changes, LoadoutChange add) =>
        FreeBoxes(loadout).ElementAt(changes.Where(change => change.Kind == LoadoutChange.Add).TakeWhile(change => change != add).Count());

    /// <summary>
    /// Puts a change in the place of another in a unit's list of changes: with no <paramref name="old"/> one, the new
    /// one is added; with no <paramref name="updated"/> one, the old one is taken out. The list's order is kept, as the
    /// weapons added are given their boxes and numbers in it.
    /// </summary>
    public static void Replace(List<LoadoutChange> changes, LoadoutChange? old, LoadoutChange? updated)
    {
        int place = old is null ? -1 : changes.IndexOf(old);
        if (place < 0)
        {
            if (updated is not null) changes.Add(updated);
        }
        else if (updated is null) changes.RemoveAt(place);
        else changes[place] = updated;
    }

    /// <summary>The last change of a kind at a place in the game's loadout; null when there is none.</summary>
    private static LoadoutChange? LastAt(IReadOnlyList<LoadoutChange> changes, string kind, LoadoutMount mount) =>
        changes.LastOrDefault(change => change.Kind == kind && change.Turret == mount.Turret && change.Mount == mount.Index);

    /// <summary>How many of a weapon added are mounted, as its text says ("NbWeapons = 2").</summary>
    public static int AddedCount(LoadoutChange add)
    {
        const string key = "NbWeapons = ";
        int start = add.MountText.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return 1;
        start += key.Length;
        int end = start;
        while (end < add.MountText.Length && char.IsAsciiDigit(add.MountText[end])) end++;
        return int.TryParse(add.MountText[start..end], NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : 1;
    }

    /// <summary>A loadout's weapons as a unit's list shows them, turret by turret, as the game's files are read.</summary>
    public static List<UnitWeapon> WeaponsOf(Loadout loadout, IReadOnlyDictionary<string, Weapon> weapons)
    {
        var carried = new List<UnitWeapon>();
        foreach (LoadoutMount mount in loadout.Mounts)
        {
            if (weapons.TryGetValue(mount.Ammunition, out Weapon? weapon)) carried.Add(weapon.ForUnit(mount.Count));
            else carried.Add(new UnitWeapon(Weapon.NameOf(mount.Ammunition), mount.Count, "", 0, null, null, null, null, mount.Ammunition, null, null));
        }
        return carried;
    }

    // ---- what can go where ----

    /// <summary>How well a weapon fits a place: as well as the weapon there, another kind of weapon, or not at all.</summary>
    public enum Fit { Fits, OtherKind, CannotGoThere }

    /// <summary>
    /// Whether a weapon can take the place of the one mounted somewhere, and why not: a missile needs a missile launcher
    /// (a place a missile is mounted); a weapon that must deploy to fire needs a place one is mounted; and weapons sharing
    /// an ammo box must fire the same number of shots a salvo. A weapon of another kind fires with the flash and sound
    /// of the one it replaces.
    /// </summary>
    /// <param name="loadout">The unit's loadout, as its changes make it.</param>
    /// <param name="mount">The place, in that loadout.</param>
    /// <param name="candidate">The weapon to put there.</param>
    /// <param name="weapons">Every weapon by descriptor, for those mounted now.</param>
    /// <returns>How it fits, and for one that cannot go there, the text key of the reason, with what the reason names.</returns>
    public static (Fit Fit, string Reason, string Other, double? Shots) FitOf(Loadout loadout, LoadoutMount mount, Weapon candidate, IReadOnlyDictionary<string, Weapon> weapons)
    {
        Weapon? now = weapons.GetValueOrDefault(mount.Ammunition);
        if (candidate.Missile is not null && now?.Missile is null) return (Fit.CannotGoThere, "Weapons.Swap.NeedsLauncher", "", null);
        if (candidate.MustDeploy && now?.MustDeploy != true) return (Fit.CannotGoThere, "Weapons.Swap.MustDeploy", "", null);
        foreach (LoadoutMount sharing in loadout.Mounts.Where(other => other != mount && other.AmmoBox == mount.AmmoBox))
        {
            if (weapons.GetValueOrDefault(sharing.Ammunition) is Weapon shared && shared.ShotsPerSalvo != candidate.ShotsPerSalvo)
                return (Fit.CannotGoThere, "Weapons.Swap.SharedBox", shared.Name, shared.ShotsPerSalvo);
        }
        return (now is null || now.Kind == candidate.Kind ? Fit.Fits : Fit.OtherKind, "", "", null);
    }

    /// <summary>
    /// Whether a weapon can be added to a turret, and why not: a missile only where a missile is mounted, and a weapon
    /// that must deploy only where one that must is.
    /// </summary>
    /// <returns>The text key of the reason; null when it can.</returns>
    public static string? WhyNotAdd(LoadoutTurret turret, Weapon candidate, IReadOnlyDictionary<string, Weapon> weapons)
    {
        IEnumerable<Weapon> mounted = turret.Mounts.Select(mount => weapons.GetValueOrDefault(mount.Ammunition)).OfType<Weapon>();
        if (candidate.Missile is not null && !mounted.Any(weapon => weapon.Missile is not null)) return "Weapons.Add.NeedsLauncher";
        if (candidate.MustDeploy && !mounted.Any(weapon => weapon.MustDeploy)) return "Weapons.Add.MustDeploy";
        return null;
    }

    /// <summary>
    /// A weapon added to a loadout, as NDF: a mounted weapon of its own ammo box and the loadout's next weapon number
    /// (filled in when the rules are made, from <see cref="AmmoBoxBlank"/> and <see cref="NumberBlank"/>), firing with
    /// the flash and sound the game gives the weapon elsewhere.
    /// </summary>
    /// <param name="ammunition">The weapon, by its ammunition's descriptor.</param>
    /// <param name="effectTag">Its flash and sound, from another unit carrying it.</param>
    /// <param name="count">How many of the weapon are mounted.</param>
    public static string NewMountText(string ammunition, string effectTag, int count = 1) =>
        "TMountedWeaponDescriptor\n(\n"
        + "    AmmoBoxIndex = " + AmmoBoxBlank + "\n"
        + "    Ammunition = " + ReferencePrefix + ammunition + "\n"
        + "    AnimateOnlyOneSoldier = True\n"
        + "    EffectTag = \"" + effectTag + "\"\n"
        + "    HandheldEquipmentKey = \"WeaponAlternative_" + NumberBlank + "\"\n"
        + "    NbWeapons = " + count.ToString(CultureInfo.InvariantCulture) + "\n"
        + "    ShowDispersion = False\n"
        + "    WeaponActiveAndCanShootPropertyName = \"WeaponActiveAndCanShoot_" + NumberBlank + "\"\n"
        + "    WeaponIgnoredPropertyName = \"WeaponIgnored_" + NumberBlank + "\"\n"
        + "    WeaponShootDataPropertyName = [\"WeaponShootData_0_" + NumberBlank + "\"]\n"
        + ")";

    /// <summary>
    /// A weapon added to a Steel Division 2 loadout, as NDF: the game's own mount of the weapon in another loadout, as
    /// the engine writes it, so it is shown and fires there as it does elsewhere (when it moves, at a position...); one
    /// with the game's plain values when no loadout has the weapon. It is given the weapon, the ammo box filled in when
    /// the rules are made (<see cref="AmmoBoxBlank"/>, in its SalvoStockIndex, and in SalvoStockIndex_ForInterface
    /// unless the game hides the weapon's box there with -1), and the effect tag of the turret's own weapon, a firing
    /// point the unit's model has.
    /// </summary>
    /// <param name="gameMount">The game's mount of the weapon (<see cref="MountTextOf"/>); null when none has it.</param>
    /// <param name="ammunition">The weapon, by its ammunition's descriptor.</param>
    /// <param name="effectTag">The effect tag of the turret's own weapon, such as "weapon_effet_tag1".</param>
    public static string SteelDivisionMountText(string? gameMount, string ammunition, string effectTag)
    {
        string text = gameMount ?? SteelDivisionPlainMount;
        text = Regex.Replace(text, @"(?m)^([ \t]*Ammunition[ \t]*=[ \t]*)[^ \t\r\n]+", "${1}" + SteelDivisionAmmunitionPrefix + ammunition);
        text = Regex.Replace(text, @"(?m)^([ \t]*EffectTag[ \t]*=[ \t]*)(['""])[^'""]*['""]", "${1}${2}" + effectTag + "${2}");
        text = Regex.Replace(text, @"(?m)^([ \t]*SalvoStockIndex[ \t]*=[ \t]*)-?[0-9]+", "${1}" + AmmoBoxBlank);
        // a colour on one line, as the game writes it: D3DRGBA[0,0,0,0]
        text = Regex.Replace(text, @"D3DRGBA\[\s*([0-9]+),\s*([0-9]+),\s*([0-9]+),\s*([0-9]+),?\s*\]", "D3DRGBA[$1,$2,$3,$4]");
        return Regex.Replace(text, @"(?m)^([ \t]*SalvoStockIndex_ForInterface[ \t]*=[ \t]*)[0-9]+", "${1}" + AmmoBoxBlank);
    }

    /// <summary>A Steel Division 2 mount with the game's plain values, as its loadouts write one that shows nothing extra.</summary>
    private const string SteelDivisionPlainMount = """
        TMountedWeaponDescriptor
        (
            Ammunition = ~/Ammo
            AnimateOnlyOneSoldier = False
            DispersionRadiusOffColor = D3DRGBA[0,0,0,0]
            DispersionRadiusOffThickness = -0.1
            DispersionRadiusOnColor = D3DRGBA[0,0,0,0]
            DispersionRadiusOnThickness = -0.1
            EffectTag = 'weapon_effet_tag1'
            IgnoreFriendlyFireOnMoveAndAttack = False
            IgnoreFriendlyFireOnReflexShoot = False
            MaxRangeDisplay_Color = D3DRGBA[0,0,0,0]
            MaxRangeDisplay_Thickness = -0.1
            MinRangeDisplay_Color = D3DRGBA[0,0,0,0]
            MinRangeDisplay_Thickness = -0.1
            OverwritedTrait_ForInterface = 0
            PowerIcon = ""
            Power_ForInterface = 0
            SalvoStockIndex = 0
            SalvoStockIndex_ForInterface = 0
            ShowDispersion = False
            ShowMaximumRange = False
            ShowMaximumRange_HA = False
            ShowMaximumRange_Projectile = False
            ShowMaximumRange_SuperWeapon = False
            ShowMaximumRange_TBA = False
            ShowMinimumRange = False
            ShowMinimumRange_HA = False
            ShowMinimumRange_Projectile = False
            ShowMinimumRange_SuperWeapon = False
            ShowMinimumRange_TBA = False
            ShowSplashRadius = False
            SplashRadiusOffColor = D3DRGBA[0,0,0,0]
            SplashRadiusOffThickness = -0.1
            SplashRadiusOnColor = D3DRGBA[0,0,0,0]
            SplashRadiusOnThickness = -0.1
            TirEnMouvement = True
            TirSurPosition = False
            WeaponNumber_ForInterface = 1
        )
        """;

    /// <summary>
    /// The first mount of a weapon in any loadout of WeaponDescriptor.ndf, written as the engine writes it: how the game
    /// mounts it, for a weapon added to a Steel Division 2 loadout (<see cref="SteelDivisionMountText"/>). Only the
    /// loadouts whose text names the weapon are read. Null when none has it.
    /// </summary>
    /// <param name="file">The file's text, read line by line.</param>
    /// <param name="ammunition">The weapon, by its ammunition's descriptor.</param>
    public static string? MountTextOf(TextReader file, string ammunition)
    {
        foreach ((string _, string text) in UnitReader.DeclarationTexts(file))
        {
            if (!text.Contains(ammunition, StringComparison.Ordinal)) continue;
            foreach (Engine.Ndf.Declaration declaration in Engine.Ndf.Parser.Parse(text).Declarations)
                foreach (Engine.Ndf.ObjectValue mount in NdfValues.AllObjects(declaration.Value))
                    if (NdfValues.Text(NdfValues.Own(mount, "Ammunition")) is string named && named == ammunition)
                        return Engine.Writing.CanonicalWriter.Write(mount).Trim();
        }
        return null;
    }

    /// <summary>
    /// A mounted weapon as WeaponDescriptor.ndf has it, written as the engine writes it: what a rule taking it out finds
    /// it by. Read from the file on the Files tab, the one the rule runs on, when the user takes the weapon off. Null when
    /// the file has no such loadout or place.
    /// </summary>
    /// <param name="file">The file's text, read line by line.</param>
    /// <param name="loadout">The loadout, by descriptor.</param>
    /// <param name="turret">The turret's place in it.</param>
    /// <param name="mount">The weapon's place on the turret.</param>
    public static string? MountText(TextReader file, string loadout, int turret, int mount)
    {
        foreach ((string name, string text) in UnitReader.DeclarationTexts(file))
        {
            if (name != loadout) continue;
            foreach (Engine.Ndf.Declaration declaration in Engine.Ndf.Parser.Parse(text).Declarations)
            {
                if (declaration.Value is not Engine.Ndf.ObjectValue manager) continue;
                IReadOnlyList<Engine.Ndf.NdfValue> turrets = NdfValues.ItemsOf(NdfValues.Own(manager, "TurretDescriptorList"));
                if (turret >= turrets.Count || turrets[turret] is not Engine.Ndf.ObjectValue turretObject) return null;
                IReadOnlyList<Engine.Ndf.NdfValue> mounts = NdfValues.ItemsOf(NdfValues.Own(turretObject, "MountedWeaponDescriptorList"));
                return mount < mounts.Count ? Engine.Writing.CanonicalWriter.Write(mounts[mount]).Trim() : null;
            }
        }
        return null;
    }

    // ---- the rules' actions ----

    /// <summary>One action of the rule that changes a unit's own loadout: its target (a path), operator and value.</summary>
    public sealed record LoadoutAction(string Target, string Operator, string Value);

    /// <summary>
    /// The actions that make the changes in the unit's own loadout, a copy of <paramref name="loadout"/>, in the order
    /// that keeps the places right (see the class's summary), as the game writes its loadouts.
    /// </summary>
    /// <param name="loadout">The loadout the changes are made on.</param>
    /// <param name="changes">The unit's changes to it.</param>
    /// <param name="game">The game the rules are for; WARNO when not given.</param>
    public static List<LoadoutAction> ActionsFor(Loadout loadout, IReadOnlyList<LoadoutChange> changes, GameTitle? game = null)
    {
        var actions = new List<LoadoutAction>();
        foreach (LoadoutChange remove in changes.Where(change => change.Kind == LoadoutChange.Remove && change.MountText != ""))
            actions.Add(new LoadoutAction(MountListPath(remove.Turret), "Remove [array]", remove.MountText));
        foreach (LoadoutChange swap in LastOfEachPlace(changes, LoadoutChange.Swap))
            actions.Add(new LoadoutAction(MountPath(changes, swap) + Separator + "Ammunition", SetTo, AmmunitionReference(swap.Ammunition, game)));
        // a game with no number of a weapon at one place gets none written
        IEnumerable<LoadoutChange> counts = Writes(game ?? GameTitle.Warno, LoadoutChange.Count) ? LastOfEachPlace(changes, LoadoutChange.Count) : [];
        foreach (LoadoutChange count in counts)
            actions.Add(new LoadoutAction(MountPath(changes, count) + Separator + "NbWeapons", SetTo, count.Number.ToString(CultureInfo.InvariantCulture)));
        foreach (LoadoutChange salvos in changes.Where(change => change.Kind == LoadoutChange.Salvos && change.Mount < loadout.AmmoBoxes.Count)
                     .GroupBy(change => change.Mount).Select(group => group.Last()))
            actions.Add(new LoadoutAction("Salves" + Separator + "Index" + salvos.Mount.ToString(CultureInfo.InvariantCulture), SetTo, salvos.Number.ToString(CultureInfo.InvariantCulture)));
        int number = loadout.HighestWeaponNumber;
        int mainSalvos = loadout.MainSalvoCount ?? 0;
        using IEnumerator<int> freeBoxes = FreeBoxes(loadout).GetEnumerator();
        foreach (LoadoutChange add in changes.Where(change => change.Kind == LoadoutChange.Add))
        {
            number++;
            freeBoxes.MoveNext();
            int box = freeBoxes.Current;
            string salvos = add.Number.ToString(CultureInfo.InvariantCulture);
            // a place the list leaves unused (-1) is filled; a list with none gets a place after its last
            if (box < loadout.AmmoBoxes.Count) actions.Add(new LoadoutAction("Salves" + Separator + "Index" + box.ToString(CultureInfo.InvariantCulture), SetTo, salvos));
            else actions.Add(new LoadoutAction("Salves", "Insert [array]", "LastIndex: " + salvos));
            // Steel Division 2 keeps an entry of SalvoIsMainSalvo for each box in use
            if (loadout.MainSalvoCount is not null)
                for (; mainSalvos <= box; mainSalvos++) actions.Add(new LoadoutAction("SalvoIsMainSalvo", "Insert [array]", "LastIndex: False"));
            string mount = add.MountText.Replace(AmmoBoxBlank, box.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace(NumberBlank, number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            actions.Add(new LoadoutAction(MountListPath(add.Turret), "Insert [array]", "LastIndex: " + mount));
        }
        return actions;
    }

    private const string SetTo = "Set To [int, float, string, boolean]";

    /// <summary>
    /// The changes of a kind that still count: the last of each place, and none of a place whose weapon is taken off.
    /// </summary>
    private static IEnumerable<LoadoutChange> LastOfEachPlace(IReadOnlyList<LoadoutChange> changes, string kind) =>
        changes.Where(change => change.Kind == kind)
            .GroupBy(change => (change.Turret, change.Mount)).Select(group => group.Last())
            .Where(last => !changes.Any(change => change.Kind == LoadoutChange.Remove && change.Turret == last.Turret && change.Mount == last.Mount));

    /// <summary>
    /// The path to a mounted weapon in the unit's own loadout, once the weapons taken off are gone: its place less those
    /// taken off before it on its turret.
    /// </summary>
    private static string MountPath(IReadOnlyList<LoadoutChange> changes, LoadoutChange at)
    {
        int removedBefore = changes.Count(change => change.Kind == LoadoutChange.Remove && change.Turret == at.Turret && change.Mount < at.Mount);
        return MountListPath(at.Turret) + Separator + "Index" + (at.Mount - removedBefore).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The path to a turret's list of mounted weapons: "TurretDescriptorList > Index1 > MountedWeaponDescriptorList".</summary>
    private static string MountListPath(int turret) =>
        "TurretDescriptorList" + Separator + "Index" + turret.ToString(CultureInfo.InvariantCulture) + Separator + "MountedWeaponDescriptorList";
}
