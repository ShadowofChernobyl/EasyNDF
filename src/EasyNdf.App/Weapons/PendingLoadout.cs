using System.Windows;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>
/// The New Unit window's Weapons step, before the unit is made: the changes to its model's weapons, and the copies of
/// weapons they mount, which are added to the mod with the unit when it is created, and dropped when it is not.
/// </summary>
public sealed class PendingLoadout
{
    /// <summary>The changes to the model's weapons, by their places in its loadout.</summary>
    public List<LoadoutChange> Changes { get; } = [];

    /// <summary>The copies of weapons the changes mount, made with the unit.</summary>
    public List<ModWeapon> Copies { get; } = [];

    /// <summary>The new unit's name as typed, for the copies' names.</summary>
    public string UnitName { get; set; } = "";

    /// <summary>The descriptor the new unit will have, for the name of its own loadout in the Checks.</summary>
    public string UnitDescriptor { get; set; } = "";

    /// <summary>Starts again: no changes, no copies.</summary>
    public void Clear()
    {
        Changes.Clear();
        Copies.Clear();
    }

    /// <summary>Lets go of the copies no change mounts any more.</summary>
    public void LetGoOfUnusedCopies() =>
        Copies.RemoveAll(copy => !Changes.Any(change => change.Ammunition == copy.Descriptor));
}

/// <summary>What the New Unit window's Weapons step asks of the Armory tab, which knows the loadouts and weapons.</summary>
public interface INewUnitWeapons
{
    /// <summary>
    /// The weapons editor for a model unit's loadout with the changes pending; null when its loadout is not known.
    /// </summary>
    /// <param name="model">The model unit chosen.</param>
    /// <param name="pending">The changes and copies so far, which the editor's buttons change.</param>
    /// <param name="changed">Called after each change, for the step to be drawn again.</param>
    FrameworkElement? WeaponsEditorFor(Unit model, PendingLoadout pending, Action changed);

    /// <summary>The Checks beside the editor (<see cref="LoadoutEditor.Checks"/>); null when the model's loadout is not known.</summary>
    FrameworkElement? WeaponChecksFor(Unit model, PendingLoadout pending);

    /// <summary>The loadout the model unit has, which the changes' places are in; null when not known.</summary>
    Loadout? LoadoutOfModel(Unit model);
}
