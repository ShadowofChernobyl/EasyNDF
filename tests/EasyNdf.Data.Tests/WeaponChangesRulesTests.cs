using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.Data.Tests;

/// <summary>
/// The rules that changed a weapon's values are found from the edits sorted by weapon once, rather than by looking
/// through every edit for each weapon. Each value must still name the rules whose edits are in its weapon (an edit
/// "Ammo_X > TModule" is in Ammo_X), to the value's own property where there are such edits, in the order the edits
/// were made, each rule once.
/// </summary>
public class WeaponChangesRulesTests
{
    private static Weapon Ammunition(string descriptor, double damage, double suppression) =>
        new() { Descriptor = descriptor, File = "Ammunition.ndf", Name = descriptor, Damage = damage, Suppression = suppression };

    [Fact]
    public void Each_value_names_the_rules_of_its_own_weapons_edits_in_order()
    {
        Weapon[] game = [Ammunition("Ammo_A", 1, 10), Ammunition("Ammo_B", 2, 20), Ammunition("Ammo_C", 3, 30)];
        Weapon[] changed = [Ammunition("Ammo_A", 5, 10), Ammunition("Ammo_B", 6, 25), Ammunition("Ammo_C", 3, 30), Ammunition("Ammo_New", 1, 1)];
        RuleEdit[] edits =
        [
            new("Damage up", "Ammo_B", "PhysicalDamages"),
            new("Damage up", "Ammo_A", "PhysicalDamages"),
            new("Suppression", "Ammo_B > TSomething", "SuppressDamages", "Other mod"),
            new("Tidy", "Ammo_A", "SomethingElse"),
            new("Damage again", "Ammo_A", "PhysicalDamages"),
            new("Damage up", "Ammo_A", "PhysicalDamages"),
            new("Unrelated", "Ammo_C", "PhysicalDamages"),
            new("Prefix only", "Ammo_AB", "PhysicalDamages"),
        ];

        WeaponChanges changes = WeaponChanges.Between("My mod", game, changed, edits);

        Assert.Equal(3, changes.Count);
        WeaponChange a = changes.Of(changed[0])!;
        Assert.Equal([WeaponField.Damage], a.Fields);
        Assert.Equal([new RuleOfMod("My mod", "Damage up"), new RuleOfMod("My mod", "Damage again")], a.RulesFor(WeaponField.Damage));
        WeaponChange b = changes.Of(changed[1])!;
        Assert.Equal([new RuleOfMod("My mod", "Damage up")], b.RulesFor(WeaponField.Damage));
        Assert.Equal([new RuleOfMod("Other mod", "Suppression")], b.RulesFor(WeaponField.Suppression));
        Assert.Null(changes.Of(changed[2]));
        Assert.True(changes.Of(changed[3])!.IsNew);
    }

    [Fact]
    public void A_value_no_edit_names_gets_every_rule_of_its_weapon()
    {
        Weapon[] game = [Ammunition("Ammo_A", 1, 10)];
        Weapon[] changed = [Ammunition("Ammo_A", 5, 10)];
        RuleEdit[] edits = [new("First", "Ammo_A > TModule", "Other"), new("Elsewhere", "Ammo_B", "PhysicalDamages"), new("Second", "Ammo_A", "Another")];

        WeaponChanges changes = WeaponChanges.Between("My mod", game, changed, edits);

        Assert.Equal([new RuleOfMod("My mod", "First"), new RuleOfMod("My mod", "Second")], changes.Of(changed[0])!.RulesFor(WeaponField.Damage));
    }

    [Fact]
    public void A_weapon_changed_with_no_edit_in_it_names_no_rule()
    {
        Weapon[] game = [Ammunition("Ammo_A", 1, 10)];
        Weapon[] changed = [Ammunition("Ammo_A", 5, 10)];

        WeaponChanges changes = WeaponChanges.Between("My mod", game, changed, [new("Elsewhere", "Ammo_B", "PhysicalDamages")]);

        Assert.Empty(changes.Of(changed[0])!.RulesFor(WeaponField.Damage));
    }
}
