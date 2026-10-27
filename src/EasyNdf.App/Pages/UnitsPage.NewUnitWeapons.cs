using System.Windows;
using EasyNdf.App.Text;
using EasyNdf.App.Weapons;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Pages;

/// <summary>
/// The New Unit window's Weapons step: the same weapons editor as the Unit pane's, on the model unit's loadout, with
/// the same Swap and Add windows; the changes wait in a <see cref="PendingLoadout"/> until the unit is created.
/// </summary>
public partial class UnitsPage : INewUnitWeapons
{
    /// <inheritdoc/>
    public Loadout? LoadoutOfModel(Unit model) =>
        model.WeaponSet is string loadout ? gameUnits?.Catalog.Loadouts?.GetValueOrDefault(loadout) : null;

    /// <inheritdoc/>
    public FrameworkElement? WeaponsEditorFor(Unit model, PendingLoadout pending, Action changed)
    {
        if (LoadoutOfModel(model) is not Loadout original) return null;
        return LoadoutEditor.Build(original, pending.Changes, WeaponsWithPending(pending), new NewUnitLoadoutActions(this, original, pending, changed));
    }

    /// <inheritdoc/>
    public FrameworkElement? WeaponChecksFor(Unit model, PendingLoadout pending)
    {
        if (LoadoutOfModel(model) is not Loadout original) return null;
        string ownLoadout = LoadoutRules.OwnLoadoutName(pending.UnitDescriptor);
        return LoadoutEditor.Checks(original, pending.Changes, WeaponsWithPending(pending), LoadoutFactsFor(model, original, isNewUnit: true, model.File, ownLoadout));
    }

    /// <summary>The weapons as the Armory shows them, with the copies pending, each as its original under its own name.</summary>
    private Dictionary<string, Weapon> WeaponsWithPending(PendingLoadout pending)
    {
        Dictionary<string, Weapon> weapons = ShownWeapons();
        foreach (ModWeapon copy in pending.Copies)
            if (weapons.GetValueOrDefault(copy.MadeFrom) is Weapon original) weapons[copy.Descriptor] = copy.Apply(original);
        return weapons;
    }

    /// <summary>A copy of a weapon for the unit being made, kept until it is created; its descriptor.</summary>
    private string PendingCopyOf(Weapon weapon, PendingLoadout pending)
    {
        string unitName = pending.UnitName.Trim() == "" ? Texts.Get("Units.New.Title") : pending.UnitName.Trim();
        string name = Texts.Get("Weapons.Loadout.CopyName", ("weapon", weapon.Name), ("unit", unitName));
        IEnumerable<string> taken = ShownWeapons().Keys.Concat(OpenMod.Weapons.Select(copy => copy.Descriptor)).Concat(pending.Copies.Select(copy => copy.Descriptor));
        string descriptor = ModWeapon.NewDescriptor(name, [.. taken]);
        string madeFrom = pending.Copies.FirstOrDefault(copy => copy.Descriptor == weapon.Descriptor)?.MadeFrom ?? weapon.Descriptor;
        pending.Copies.Add(new ModWeapon { Descriptor = descriptor, MadeFrom = madeFrom, File = weapon.File, Name = name });
        return descriptor;
    }

    /// <summary>A swap kept in the pending changes: the model's own weapon there again lets go of it.</summary>
    private static void PendingSwap(PendingLoadout pending, LoadoutMount mount, string ammunition)
    {
        pending.Changes.RemoveAll(change => change.Kind == LoadoutChange.Swap && change.Turret == mount.Turret && change.Mount == mount.Index);
        if (ammunition != mount.Ammunition) pending.Changes.Add(new LoadoutChange(LoadoutChange.Swap, mount.Turret, mount.Index, ammunition));
        pending.LetGoOfUnusedCopies();
    }

    /// <summary>The Weapons step's buttons: each changes the pending changes, then the step is drawn again.</summary>
    private sealed class NewUnitLoadoutActions(UnitsPage page, Loadout original, PendingLoadout pending, Action changed) : ILoadoutActions
    {
        public void Swap(LoadoutMount mount) =>
            page.OpenSwapWindow(original, pending.Changes, page.WeaponsWithPending(pending), pending.UnitName, mount, (weapon, asCopy) =>
            {
                PendingSwap(pending, mount, asCopy ? page.PendingCopyOf(weapon, pending) : weapon.Descriptor);
                changed();
            });

        public void CopyAndChange(LoadoutMount mount)
        {
            Loadout now = LoadoutRules.Changed(original, pending.Changes, "");
            if (NowOf(now, mount) is not LoadoutMount place || page.WeaponsWithPending(pending).GetValueOrDefault(place.Ammunition) is not Weapon weapon) return;
            PendingSwap(pending, mount, page.PendingCopyOf(weapon, pending));
            changed();
        }

        public async void Remove(LoadoutMount mount)
        {
            string weaponName = page.WeaponsWithPending(pending).GetValueOrDefault(mount.Ammunition)?.Name ?? Weapon.NameOf(mount.Ammunition);
            string unitName = pending.UnitName.Trim() == "" ? Texts.Get("Units.New.Title") : pending.UnitName.Trim();
            if (!await MainView.Of(page).Messages.Confirm(Texts.Get("Weapons.Loadout.RemoveAsk", ("weapon", weaponName), ("unit", unitName)), Texts.Get("Weapons.Loadout.RemoveOk"))) return;
            if (await page.MountTextOf(original, mount) is not string text) return;
            pending.Changes.Add(new LoadoutChange(LoadoutChange.Remove, mount.Turret, mount.Index, MountText: text));
            changed();
        }

        public void TakeBack(LoadoutChange change)
        {
            pending.Changes.Remove(change);
            pending.LetGoOfUnusedCopies();
            changed();
        }

        public void Add(LoadoutTurret turret) =>
            page.OpenAddWindow(original, pending.Changes, page.WeaponsWithPending(pending), pending.UnitName, turret, (weapon, asCopy) =>
            {
                string ammunition = asCopy ? page.PendingCopyOf(weapon, pending) : weapon.Descriptor;
                pending.Changes.Add(page.AddChange(turret, pending.Changes, weapon, ammunition, pending));
                changed();
            });

        public void Replace(LoadoutChange? old, LoadoutChange? updated)
        {
            LoadoutRules.Replace(pending.Changes, old, updated);
            changed();
        }
    }
}
