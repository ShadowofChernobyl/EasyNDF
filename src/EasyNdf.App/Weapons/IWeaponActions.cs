using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>What the Weapons sub-tab's parts can do with a weapon; the Armory tab does it.</summary>
public interface IWeaponActions
{
    /// <summary>The open mod's name, which the sub-tab's heading says its changes are saved in.</summary>
    string OpenModName { get; }

    /// <summary>
    /// "+ New Weapon…": a weapon of the open mod's own, a copy of one the user picks (an ammunition has some 60 values,
    /// many of them references, so a new one is never blank), carried by no unit until given; it is then selected.
    /// </summary>
    void NewWeapon();

    /// <summary>Whether a weapon's file is on the Files tab, so it can be shown in the File Editor.</summary>
    bool CanShowInEditor(Weapon weapon);

    /// <summary>Shows a weapon's descriptor in the File Editor.</summary>
    void ShowInEditor(Weapon weapon);

    /// <summary>Makes a new rule for a weapon, on the Rules tab.</summary>
    void NewRuleFor(Weapon weapon);

    /// <summary>Shows a unit, by its descriptor, on the Units sub-tab.</summary>
    void ShowUnit(string descriptor);

    /// <summary>Whether a weapon's values can be changed in the Weapon pane: one of the source's own, or a copy of the open mod's.</summary>
    bool CanChangeWeapon(Weapon weapon);

    /// <summary>Whether a weapon is a copy the open mod adds, which can be renamed.</summary>
    bool IsCopy(Weapon weapon);

    /// <summary>
    /// Changes a value of a weapon (<see cref="WeaponField"/>), as typed, or a switch or choice (<see cref="WeaponChoice"/>),
    /// as the file writes it; for a copy of the mod's, its name too.
    /// Returns false when the change was refused (a file it needs is not on the Files tab, say).
    /// </summary>
    bool ChangeWeapon(Weapon weapon, string field, string typed);

    /// <summary>
    /// The values a switch or choice of a weapon can be given (<see cref="WeaponChoice.Offered"/>), as the file writes
    /// them: on and off, or each value the game's weapons have.
    /// </summary>
    IReadOnlyList<string> ChoicesFor(Weapon weapon, string field);

    /// <summary>Every weapon picture the game lists: its file, by texture name, for the Weapon pane's ✎.</summary>
    IReadOnlyDictionary<string, string> WeaponPictures { get; }

    /// <summary>
    /// Gives a weapon another weapon's picture, by texture name, or its own again with null. Returns false when the
    /// change was refused.
    /// </summary>
    bool ChangePicture(Weapon weapon, string? texture);

    /// <summary>Whether the open mod gives a weapon another picture.</summary>
    bool HasOtherPicture(Weapon weapon);

    /// <summary>The levels each kind of damage has, from the game's DamageResistance.ndf; null when not known.</summary>
    IReadOnlyDictionary<string, int>? DamageLevels { get; }

    /// <summary>Makes a copy of a weapon, named <paramref name="name"/>, carried by the units with the loadouts given in place of the weapon.</summary>
    void CopyWeapon(Weapon weapon, string name, IReadOnlyList<string> loadouts);

    /// <summary>The copies the open mod has made of a weapon, which units carrying it can be given.</summary>
    IReadOnlyList<Weapon> CopiesOf(Weapon weapon);

    /// <summary>For a copy of the open mod's, the weapon it was copied from; null for any other weapon.</summary>
    Weapon? OriginalOf(Weapon weapon);

    /// <summary>
    /// Gives the units with the loadouts given <paramref name="to"/> in place of <paramref name="from"/>: one of the
    /// mod's copies of it, or, for a copy, the weapon it was copied from again.
    /// </summary>
    void GiveCopy(Weapon to, Weapon from, IReadOnlyList<string> loadouts);

    /// <summary>
    /// The Delete key on a weapon, once the user says so: a copy of the open mod's is deleted (the units given it carry
    /// its original again); its changes to one of the game's weapons are taken back. The game's own weapons are never
    /// deleted, which a message says.
    /// </summary>
    Task DeleteWeapon(Weapon weapon);
}
