using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>How the Weapons sub-tab writes a weapon's values.</summary>
public static class WeaponNames
{
    /// <summary>
    /// Its kind of damage and level as the game's armory shows them: "AP 32", "HE"; the game's own name, in capitals,
    /// for the others ("FMBALLE"). Empty when the game gives none.
    /// </summary>
    public static string Damage(Weapon weapon)
    {
        string family = weapon.DamageFamily.Replace('_', ' ').ToUpperInvariant();
        return weapon.DamageLevel > 0 ? family + " " + Texts.Number(weapon.DamageLevel) : family;
    }

    /// <summary>The categories' text keys: the weapon's kind ("Gun"), the tab's short name ("GUN") and the table's heading ("GUNS").</summary>
    private static readonly Dictionary<WeaponCategory, (string Kind, string Tab, string Heading)> CategoryKeys = new()
    {
        [WeaponCategory.Gun] = ("Weapons.Category.Gun", "Weapons.Category.Gun.Tab", "Weapons.Category.Gun.Heading"),
        [WeaponCategory.Autocannon] = ("Weapons.Category.Autocannon", "Weapons.Category.Autocannon.Tab", "Weapons.Category.Autocannon.Heading"),
        [WeaponCategory.MachineGun] = ("Weapons.Category.MachineGun", "Weapons.Category.MachineGun.Tab", "Weapons.Category.MachineGun.Heading"),
        [WeaponCategory.SmallArms] = ("Weapons.Category.SmallArms", "Weapons.Category.SmallArms.Tab", "Weapons.Category.SmallArms.Heading"),
        [WeaponCategory.AntiTankMissile] = ("Weapons.Category.AntiTankMissile", "Weapons.Category.AntiTankMissile.Tab", "Weapons.Category.AntiTankMissile.Heading"),
        [WeaponCategory.Rocket] = ("Weapons.Category.Rocket", "Weapons.Category.Rocket.Tab", "Weapons.Category.Rocket.Heading"),
        [WeaponCategory.AntiAir] = ("Weapons.Category.AntiAir", "Weapons.Category.AntiAir.Tab", "Weapons.Category.AntiAir.Heading"),
        [WeaponCategory.Artillery] = ("Weapons.Category.Artillery", "Weapons.Category.Artillery.Tab", "Weapons.Category.Artillery.Heading"),
        [WeaponCategory.Air] = ("Weapons.Category.Air", "Weapons.Category.Air.Tab", "Weapons.Category.Air.Heading"),
        [WeaponCategory.Smoke] = ("Weapons.Category.Smoke", "Weapons.Category.Smoke.Tab", "Weapons.Category.Smoke.Heading"),
    };

    /// <summary>A category's type tab: "GUN", "MG", "ATGM".</summary>
    public static string CategoryTab(WeaponCategory category) => Texts.Get(CategoryKeys[category].Tab);

    /// <summary>A category's heading in the table, and the tab's tooltip: "GUNS", "MACHINE GUNS".</summary>
    public static string CategoryHeading(WeaponCategory category) => Texts.Get(CategoryKeys[category].Heading);

    /// <summary>What a weapon is, as the Weapons sub-tab writes it: "Gun", "MG", "Heavy MG", "ATGM".</summary>
    public static string Kind(Weapon weapon) =>
        WeaponCategories.IsHeavyMachineGun(weapon.Descriptor) ? Texts.Get("Weapons.Category.HeavyMachineGun") : Texts.Get(CategoryKeys[weapon.Category].Kind);

    /// <summary>What a weapon is and its damage: "Gun · AP 32"; "Heavy MG" when the game gives no level.</summary>
    public static string KindAndDamage(Weapon weapon)
    {
        string damage = Damage(weapon);
        return damage == "" ? Kind(weapon) : Kind(weapon) + " · " + damage;
    }

    /// <summary>A number as the Armory writes it; "–" for none.</summary>
    public static string Number(double? value, int decimals = 2) => UnitNumbers.Of(value, decimals);

    /// <summary>A chance in percent: "65%"; "–" for none.</summary>
    public static string Percent(double? value) => value is null ? UnitNumbers.None : Texts.Get("Weapons.Percent", ("value", UnitNumbers.Of(value)));

    /// <summary>Seconds: "6.5 s"; "–" for none.</summary>
    public static string Seconds(double? value) => UnitNumbers.Seconds(value, narrow: false);

    /// <summary>A range: "2,275"; "–" for none or 0 (a weapon that cannot shoot that far).</summary>
    public static string Range(double? value) => value is > 0 ? UnitNumbers.Of(value, 0) : UnitNumbers.None;
}
