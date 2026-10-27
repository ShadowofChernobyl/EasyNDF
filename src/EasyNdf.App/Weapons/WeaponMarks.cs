using System.Windows.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>
/// How the Weapons sub-tab shows the open mod's changes to the weapons, the same way the rest of the Armory shows its
/// changes to the units (<see cref="ModMarks"/>): a changed value has a "*" after it in the theme's heading colour,
/// the value keeping its own colour; its tooltip names the mod and the rules, and gives the game's value. With the
/// setting "Mark changed values" off, the "*" is left out and the tooltip stays.
/// </summary>
public static class WeaponMarks
{
    /// <summary>The open mod's changes to the weapons shown; none until they have been worked out.</summary>
    public static WeaponChanges Changes { get; set; } = WeaponChanges.None;

    /// <summary>Whether the open mod changed a value of a weapon.</summary>
    public static bool IsChanged(Weapon weapon, string field) => Changes.Of(weapon)?.Changed(field) == true;

    /// <summary>Whether a value of a weapon is drawn marked: the open mod changed it, and the marks are shown.</summary>
    public static bool IsMarked(Weapon weapon, string field) => ModMarks.AreShown && IsChanged(weapon, field);

    /// <summary>Whether the open mod changes or adds the weapon at all, for the filter "Only weapons my mod adds or changes".</summary>
    public static bool IsChangedAtAll(Weapon weapon) => Changes.Of(weapon) is not null;

    /// <summary>
    /// Shows a value in a text block: as it is, in its own colour; or, when the open mod changed it, marked, with the
    /// tooltip saying what changed it. A text block the list reuses for another weapon is set back each time.
    /// </summary>
    /// <param name="block">Where the value is shown.</param>
    /// <param name="weapon">The weapon, as the mod makes it.</param>
    /// <param name="field">Which value it is (<see cref="WeaponField"/>).</param>
    /// <param name="text">The value, written as the place shows it.</param>
    /// <param name="write">Writes the same value of another weapon the same way: the game's, for the tooltip.</param>
    public static void Show(TextBlock block, Weapon weapon, string field, string text, Func<Weapon, string> write)
    {
        WeaponChange? change = Changes.Of(weapon);
        bool changed = change is not null && change.Changed(field) && text != "";
        ModMarks.ShowText(block, text, changed && ModMarks.AreShown);
        block.ToolTip = changed ? TipFor(change!, field, write) : null;
    }

    /// <summary>
    /// What a changed value's tooltip says: "Changed by My mod: Gun: changed in the Armory", then "Game value: 32";
    /// for a weapon the mod adds, "Added by My mod".
    /// </summary>
    public static string TipFor(WeaponChange change, string field, Func<Weapon, string> write)
    {
        string mod = Changes.ModName;
        if (change.Game is not Weapon game) return Texts.Get("Units.Changed.Added.Tip", ("mod", mod));
        string before = write(game);
        return ModMarks.ChangedBy(change.RulesFor(field), mod) + "\n" + Texts.Get("Units.Changed.Game.Tip", ("value", before == "" ? UnitNumbers.None : before));
    }
}
