using System.IO;
using System.Windows;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Files;
using EasyNdf.App.Text;
using EasyNdf.App.Weapons;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Unit pane's weapons editor (<see cref="LoadoutEditor"/>): a unit's loadout, turret by turret, its weapons swapped,
/// changed as copies of the user's own, taken off and added. Each change is kept with the unit's other changes (its
/// <see cref="UnitValues.LoadoutChanges"/>) and goes through <see cref="ChangeDivisions"/>, as every Armory change
/// does: refused when a file it is written into (WeaponDescriptor.ndf, the unit's file) is not on the Files tab,
/// otherwise saved as locked rules in the Units preset, with Undo. The unit gets a loadout of its own, a copy of the
/// one it had, so no other unit's weapons change.
/// </summary>
public partial class UnitsPage
{
    /// <inheritdoc/>
    public FrameworkElement? LoadoutEditorFor(Unit unit)
    {
        if (!CanChangeUnit(unit) || OriginalLoadoutOf(unit) is not Loadout original) return null;
        IReadOnlyList<LoadoutChange> changes = LoadoutChangesOf(unit);
        Dictionary<string, Weapon> weapons = ShownWeapons();
        // in the pane, the Checks go under the turrets
        FrameworkElement checks = LoadoutEditor.Checks(original, changes, weapons, LoadoutFactsFor(unit, original, isNewUnit: false, unit.File));
        checks.Margin = new Thickness(0, 6, 0, 0);
        return new System.Windows.Controls.StackPanel { Children = { LoadoutEditor.Build(original, changes, weapons, new UnitLoadoutActions(this, unit)), checks } };
    }

    /// <summary>What the Checks say about a unit's loadout: whose it is, what it becomes, and the files it is written into.</summary>
    /// <param name="unit">The unit; for a unit being made, its model.</param>
    /// <param name="original">The loadout the changes are made on.</param>
    /// <param name="isNewUnit">Whether the unit is being made in the New Unit window.</param>
    /// <param name="unitFile">The file the unit is written into.</param>
    /// <param name="ownLoadout">The unit's own loadout, when the unit is not <paramref name="unit"/>.</param>
    private LoadoutFacts LoadoutFactsFor(Unit unit, Loadout original, bool isNewUnit, string unitFile, string? ownLoadout = null)
    {
        List<string> files = [UnitChangeRules.LoadoutsFile, Path.GetFileName(unitFile)];
        List<string> missing = [.. files.Distinct().Where(file => LoadedFile(file) is null)];
        return new LoadoutFacts(unit.Name, isNewUnit, original.Descriptor, ownLoadout ?? LoadoutRules.OwnLoadoutName(unit.Descriptor), [.. files.Distinct()], missing);
    }

    /// <summary>
    /// The loadout a unit's weapon changes are made on: the one they were first made on, else the unit's own as the
    /// source has it, or for a unit the mod adds, its model's. Null when the source has none the editor can lay out.
    /// </summary>
    private Loadout? OriginalLoadoutOf(Unit unit)
    {
        string? from = ValuesOf(OpenMod, unit.Descriptor)?.LoadoutFrom;
        if (from is null)
        {
            string modelOrUnit = OpenMod.Units.FirstOrDefault(each => each.Descriptor == unit.Descriptor)?.MadeFrom ?? unit.Descriptor;
            from = GameUnits.FirstOrDefault(each => each.Descriptor == modelOrUnit)?.WeaponSet;
        }
        return from is null ? null : gameUnits?.Catalog.Loadouts?.GetValueOrDefault(from);
    }

    /// <summary>The open mod's changes to a unit's weapons; none when it has none.</summary>
    private IReadOnlyList<LoadoutChange> LoadoutChangesOf(Unit unit) => ValuesOf(OpenMod, unit.Descriptor)?.LoadoutChanges ?? [];

    /// <summary>The values a mod gives a unit: those of a unit it adds, or its change to one of the source's; null for neither.</summary>
    private static UnitValues? ValuesOf(Mod mod, string descriptor) =>
        (UnitValues?)mod.Units.FirstOrDefault(each => each.Descriptor == descriptor) ?? UnitEdit.Of(mod, descriptor);

    /// <summary>The weapons as the Armory shows them, the open mod's copies among them, by descriptor.</summary>
    private Dictionary<string, Weapon> ShownWeapons()
    {
        var weapons = new Dictionary<string, Weapon>(StringComparer.Ordinal);
        foreach (Weapon weapon in loaded?.Catalog.Weapons ?? []) weapons.TryAdd(weapon.Descriptor, weapon);
        return weapons;
    }

    /// <summary>
    /// Changes a unit's weapon changes in the open mod: those of a unit it adds, or its change to one of the source's,
    /// which is let go of once it changes nothing. The first change gives the unit its own loadout: the mod's copies of
    /// weapons given the loadout it had are given its own too, so it keeps carrying them.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="change">Changes the list, given the mod being changed (a trial copy first, then the open mod).</param>
    private bool ChangeLoadout(Unit unit, Action<List<LoadoutChange>, Mod> change)
    {
        string descriptor = unit.Descriptor;
        string? from = OriginalLoadoutOf(unit)?.Descriptor;
        string own = LoadoutRules.OwnLoadoutName(descriptor);
        bool isAdded = IsAddedUnit(unit);
        return ChangeDivisions(mod =>
        {
            UnitValues values = isAdded ? mod.Units.First(each => each.Descriptor == descriptor) : UnitEdit.For(mod, descriptor);
            bool first = values.LoadoutChanges.Count == 0;
            values.LoadoutFrom ??= from;
            change(values.LoadoutChanges, mod);
            if (values.LoadoutChanges.Count == 0) values.LoadoutFrom = null;
            else if (first && from is not null) ModWeapon.FollowOwnLoadout(mod, from, own);
            if (!isAdded) UnitEdit.LetGoOfIfEmpty(mod, descriptor);
        });
    }

    /// <summary>
    /// A copy of a weapon, of the open mod's own, for a unit: named after the weapon and the unit, given to no loadout
    /// (the unit's change mounts it). Its descriptor; it is added to the mod given.
    /// </summary>
    private string NewCopyFor(Weapon weapon, Unit unit, Mod mod)
    {
        string name = Texts.Get("Weapons.Loadout.CopyName", ("weapon", weapon.Name), ("unit", unit.Name));
        IEnumerable<string> taken = ShownWeapons().Keys.Concat(mod.Weapons.Select(copy => copy.Descriptor));
        string descriptor = ModWeapon.NewDescriptor(name, [.. taken]);
        mod.Weapons.Add(new ModWeapon { Descriptor = descriptor, MadeFrom = weapon.Descriptor, File = weapon.File, Name = name });
        return descriptor;
    }

    /// <summary>A mount's place in the loadout as changed: the same weapon number on the same turret.</summary>
    private static LoadoutMount? NowOf(Loadout changed, LoadoutMount mount) =>
        changed.Mounts.FirstOrDefault(each => each.Turret == mount.Turret && each.WeaponNumber == mount.WeaponNumber);

    /// <summary>A reason's words, from its text key and what it names.</summary>
    private static string ReasonText(string key, string other, double? shots) =>
        key == "" ? "" : Texts.Get(key, ("weapon", other), ("shots", Units.UnitNumbers.Of(shots)));

    // ---- what the editor's buttons do ----

    /// <summary>Swap…: the Swap window for a place; the weapon picked goes there, as it is or as a copy.</summary>
    private void SwapWeapon(Unit unit, LoadoutMount mount)
    {
        if (OriginalLoadoutOf(unit) is not Loadout original) return;
        OpenSwapWindow(original, LoadoutChangesOf(unit), ShownWeapons(), unit.Name, mount, (weapon, asCopy) => Swapped(unit, mount, weapon, asCopy));
    }

    /// <summary>
    /// The Swap window for a place in a loadout as its changes make it: every weapon, sorted by whether it fits there
    /// (<see cref="LoadoutRules.FitOf"/>); the one picked is used as it is, or as a copy.
    /// </summary>
    /// <param name="original">The loadout the changes are made on.</param>
    /// <param name="changes">The unit's changes to its weapons so far.</param>
    /// <param name="weapons">Every weapon by descriptor, the copies made or to be made among them.</param>
    /// <param name="unitName">The unit's name, for the window's second line.</param>
    /// <param name="mount">The place, in the loadout the changes are made on.</param>
    /// <param name="use">What is done with the weapon picked, and whether a copy of it is wanted.</param>
    private void OpenSwapWindow(Loadout original, IReadOnlyList<LoadoutChange> changes, IReadOnlyDictionary<string, Weapon> weapons, string unitName,
        LoadoutMount mount, Action<Weapon, bool> use)
    {
        Loadout changed = LoadoutRules.Changed(original, changes, "");
        if (NowOf(changed, mount) is not LoadoutMount now) return;
        Weapon? current = weapons.GetValueOrDefault(now.Ammunition);
        List<SwapChoice> choices = [.. weapons.Values.Where(weapon => weapon.Descriptor != now.Ammunition).Select(weapon =>
        {
            var fit = LoadoutRules.FitOf(changed, now, weapon, weapons);
            return new SwapChoice(weapon, fit.Fit, ReasonText(fit.Reason, fit.Other, fit.Shots));
        })];
        string title = Texts.Get("Weapons.Loadout.SwapTitle", ("weapon", current?.Name ?? Weapon.NameOf(now.Ammunition)));
        string where = Texts.Get("Weapons.Loadout.Where", ("unit", unitName), ("turret", mount.Turret + 1));
        // the place's ammo box, and the weapons firing from it with this one, which must fire the same a salvo
        List<Weapon> sharing = [.. changed.Mounts.Where(other => other != now && other.AmmoBox == now.AmmoBox).Select(other => weapons.GetValueOrDefault(other.Ammunition)).OfType<Weapon>()];
        MainView.Of(this).SwapWeapon.Open(title, where, current, choices, IsCopy, offerCopy: true, use, SwapKind.Swap, new SwapBox(now.AmmoBox + 1, sharing));
    }

    /// <summary>A weapon picked for a place: the swap kept (the game's own weapon there again lets go of it), a copy made first when wanted.</summary>
    private void Swapped(Unit unit, LoadoutMount mount, Weapon weapon, bool asCopy)
    {
        ChangeLoadout(unit, (changes, mod) =>
        {
            string ammunition = asCopy ? NewCopyFor(weapon, unit, mod) : weapon.Descriptor;
            changes.RemoveAll(change => change.Kind == LoadoutChange.Swap && change.Turret == mount.Turret && change.Mount == mount.Index);
            if (ammunition != mount.Ammunition) changes.Add(new LoadoutChange(LoadoutChange.Swap, mount.Turret, mount.Index, ammunition));
        });
    }

    /// <summary>Copy and Change…: a copy of the weapon there now, mounted in its place, shown on the Weapons sub-tab to change.</summary>
    private void CopyAndChange(Unit unit, LoadoutMount mount)
    {
        if (OriginalLoadoutOf(unit) is not Loadout original) return;
        Loadout changed = LoadoutRules.Changed(original, LoadoutChangesOf(unit), "");
        if (NowOf(changed, mount) is not LoadoutMount now || ShownWeapons().GetValueOrDefault(now.Ammunition) is not Weapon weapon) return;
        string? made = null;
        bool done = ChangeLoadout(unit, (changes, mod) =>
        {
            made = NewCopyFor(weapon, unit, mod);
            changes.RemoveAll(change => change.Kind == LoadoutChange.Swap && change.Turret == mount.Turret && change.Mount == mount.Index);
            changes.Add(new LoadoutChange(LoadoutChange.Swap, mount.Turret, mount.Index, made));
        });
        if (done && made is not null) ShowWeapon(made);
    }

    /// <summary>
    /// ✕: the weapon taken off, once asked, as it has not been tried in the game. The rule finds it by its text, read
    /// from WeaponDescriptor.ndf on the Files tab, which it is written into: without the file, the change is refused.
    /// </summary>
    private async Task RemoveWeapon(Unit unit, LoadoutMount mount)
    {
        if (OriginalLoadoutOf(unit) is not Loadout original) return;
        string weaponName = ShownWeapons().GetValueOrDefault(mount.Ammunition)?.Name ?? Weapon.NameOf(mount.Ammunition);
        Dialogs.MessageOverlay messages = MainView.Of(this).Messages;
        if (!await messages.Confirm(Texts.Get("Weapons.Loadout.RemoveAsk", ("weapon", weaponName), ("unit", unit.Name)), Texts.Get("Weapons.Loadout.RemoveOk"))) return;
        if (await MountTextOf(original, mount) is not string text) return;
        ChangeLoadout(unit, (changes, _) => changes.Add(new LoadoutChange(LoadoutChange.Remove, mount.Turret, mount.Index, MountText: text)));
    }

    /// <summary>
    /// A mounted weapon's text, read from WeaponDescriptor.ndf on the Files tab, for the rule that takes it off; null,
    /// with a message saying why, when the file is not there or does not have it.
    /// </summary>
    private async Task<string?> MountTextOf(Loadout original, LoadoutMount mount)
    {
        Dialogs.MessageOverlay messages = MainView.Of(this).Messages;
        if (LoadedFile(UnitChangeRules.LoadoutsFile) is not FileEntry file)
        {
            await messages.Tell(Texts.Count("Divisions.FilesMissing", 1, ("files", UnitChangeRules.LoadoutsFile)));
            return null;
        }
        string? text = await Task.Run(() => LoadoutRules.MountText(new StringReader(FileContent.Text(file)), original.Descriptor, mount.Turret, mount.Index));
        if (text is null) await messages.Tell(Texts.Get("Weapons.Loadout.NoMount", ("loadout", original.Descriptor)));
        return text;
    }

    /// <summary>+ Add to This Turret…: the window to pick a weapon to add; it gets an ammo box of its own.</summary>
    private void AddWeapon(Unit unit, LoadoutTurret turret)
    {
        if (OriginalLoadoutOf(unit) is not Loadout original) return;
        OpenAddWindow(original, LoadoutChangesOf(unit), ShownWeapons(), unit.Name, turret, (weapon, asCopy) => Added(unit, turret, weapon, asCopy));
    }

    /// <summary>
    /// The window to pick a weapon to add to a turret: those of the kinds it carries first, then others, then those it
    /// cannot take (<see cref="LoadoutRules.WhyNotAdd"/>); the one picked is used as it is, or as a copy.
    /// </summary>
    private void OpenAddWindow(Loadout original, IReadOnlyList<LoadoutChange> changes, IReadOnlyDictionary<string, Weapon> weapons, string unitName,
        LoadoutTurret turret, Action<Weapon, bool> use)
    {
        Loadout changed = LoadoutRules.Changed(original, changes, "");
        LoadoutTurret now = changed.Turrets[turret.Index];
        IEnumerable<Data.Units.WeaponKind> kinds = now.Mounts.Select(mount => weapons.GetValueOrDefault(mount.Ammunition)?.Kind).OfType<Data.Units.WeaponKind>();
        List<SwapChoice> choices = [.. weapons.Values.Select(weapon =>
        {
            string? why = LoadoutRules.WhyNotAdd(now, weapon, weapons);
            LoadoutRules.Fit fit = why is not null ? LoadoutRules.Fit.CannotGoThere : kinds.Contains(weapon.Kind) ? LoadoutRules.Fit.Fits : LoadoutRules.Fit.OtherKind;
            return new SwapChoice(weapon, fit, why is null ? "" : Texts.Get(why));
        })];
        string title = Texts.Get("Weapons.Loadout.AddTitle", ("number", turret.Index + 1));
        string where = Texts.Get("Weapons.Loadout.Where", ("unit", unitName), ("turret", turret.Index + 1));
        MainView.Of(this).SwapWeapon.Open(title, where, null, choices, IsCopy, offerCopy: true, use, SwapKind.Add);
    }

    /// <summary>
    /// A weapon picked to add: mounted after the turret's own, with an ammo box of the salvos another unit carrying it
    /// has, and the flash and sound the game gives it there; a copy made first when wanted.
    /// </summary>
    private void Added(Unit unit, LoadoutTurret turret, Weapon weapon, bool asCopy)
    {
        ChangeLoadout(unit, (changes, mod) => changes.Add(AddChange(turret, changes, weapon, asCopy ? NewCopyFor(weapon, unit, mod) : weapon.Descriptor)));
    }

    /// <summary>
    /// The change adding a weapon to a turret, after its own and those added before: with an ammo box of the salvos
    /// another unit carrying it has, and the flash and sound the game gives it there (a copy's, its original's).
    /// </summary>
    /// <param name="turret">The turret, in the loadout the changes are made on.</param>
    /// <param name="changes">The unit's changes so far.</param>
    /// <param name="weapon">The weapon picked.</param>
    /// <param name="ammunition">What is mounted: the weapon, or a copy of it just made.</param>
    /// <param name="pending">The New Unit window's copies not made yet, which may be the weapon picked.</param>
    private LoadoutChange AddChange(LoadoutTurret turret, IReadOnlyList<LoadoutChange> changes, Weapon weapon, string ammunition, PendingLoadout? pending = null)
    {
        string original = OpenMod.Weapons.Concat(pending?.Copies ?? []).FirstOrDefault(copy => copy.Descriptor == weapon.Descriptor)?.MadeFrom ?? weapon.Descriptor;
        (int salvos, string effect) = HowGameCarries(original);
        int place = turret.Mounts.Count + changes.Count(change => change.Kind == LoadoutChange.Add && change.Turret == turret.Index);
        return new LoadoutChange(LoadoutChange.Add, turret.Index, place, ammunition, LoadoutRules.NewMountText(ammunition, effect), salvos);
    }

    /// <summary>
    /// How the game carries a weapon elsewhere: the salvos in its ammo box and its flash and sound, from the first
    /// loadout with it; a box of 1 and an effect named as the game names its own when none has it.
    /// </summary>
    private (int Salvos, string Effect) HowGameCarries(string ammunition)
    {
        foreach (Loadout loadout in gameUnits?.Catalog.Loadouts?.Values ?? [])
            foreach (LoadoutMount mount in loadout.Mounts)
                if (mount.Ammunition == ammunition && mount.AmmoBox < loadout.AmmoBoxes.Count)
                    return (Math.Max(1, loadout.AmmoBoxes[mount.AmmoBox]), mount.EffectTag);
        string name = ammunition.StartsWith("Ammo_", StringComparison.Ordinal) ? ammunition[5..] : ammunition;
        return (1, "FireEffect_" + name);
    }

    /// <summary>Takes back a change: the weapon put back, the swap undone, the weapon added taken away.</summary>
    private void TakeBackLoadoutChange(Unit unit, LoadoutChange taken) =>
        ChangeLoadout(unit, (changes, _) => changes.Remove(taken));

    /// <summary>The editor's buttons for one unit.</summary>
    private sealed class UnitLoadoutActions(UnitsPage page, Unit unit) : ILoadoutActions
    {
        public void Swap(LoadoutMount mount) => page.SwapWeapon(unit, mount);

        public void CopyAndChange(LoadoutMount mount) => page.CopyAndChange(unit, mount);

        public void Remove(LoadoutMount mount) => _ = page.RemoveWeapon(unit, mount);

        public void TakeBack(LoadoutChange change) => page.TakeBackLoadoutChange(unit, change);

        public void Add(LoadoutTurret turret) => page.AddWeapon(unit, turret);

        public void Replace(LoadoutChange? old, LoadoutChange? updated) =>
            page.ChangeLoadout(unit, (changes, _) => LoadoutRules.Replace(changes, old, updated));

        public void ShowWeapon(string ammunition) => page.ShowWeaponInArmory(ammunition);

        public void OpenWeaponMenu(string ammunition, FrameworkElement at)
        {
            if (unit.Weapons.FirstOrDefault(weapon => weapon.Ammunition == ammunition) is UnitWeapon carried) page.OpenWeaponMenu(unit, carried, at);
        }
    }
}
