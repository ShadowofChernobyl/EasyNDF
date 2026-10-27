using System.Text.Json.Nodes;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// A weapon the mod adds, made in the Armory: a copy of another weapon (its ammunition, with its picture, flash, sound
/// and every value), given a name of its own and whatever the user changes, so the user can change it without changing
/// every unit that carries the original. Never blank: an ammunition has some 60 values the game needs, many of them
/// references. The loadouts chosen (units' WeaponDescriptors) are pointed at the copy in place of the original; the
/// other units keep the original. The Armory's Weapons preset writes it as locked rules (<see cref="WeaponChangeRules"/>).
/// </summary>
public sealed class ModWeapon : WeaponValues
{
    /// <summary>The prefix of every ammunition descriptor the app makes, so the game's own are never taken.</summary>
    public const string DescriptorPrefix = "Ammo_EZ_";

    /// <summary>Its descriptor, such as "Ammo_EZ_M256_DU"; it stays the same when the weapon is renamed.</summary>
    public string Descriptor { get; set; } = "";

    /// <summary>The weapon it is a copy of, by descriptor: everything not given here is that weapon's.</summary>
    public string MadeFrom { get; set; } = "";

    /// <summary>The file it is written into, the one its original is in: "Ammunition.ndf" or "AmmunitionMissiles.ndf".</summary>
    public string File { get; set; } = "";

    /// <summary>Its name, as the Armory shows it.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The loadouts given it in place of the original, by descriptor ("WeaponDescriptor_M1A1_Abrams_US"): each unit with
    /// one of these loadouts carries the copy. A unit the mod adds shares the loadout of the unit it is made from.
    /// </summary>
    public List<string> Loadouts { get; set; } = [];

    /// <summary>
    /// The weapon as the Armory shows it, and as the game will have it: the weapon it is made from with what this one
    /// gives it, under its own descriptor, name and file. Kept the same as what the rules write.
    /// </summary>
    /// <param name="madeFrom">The weapon it is made from, as the mod shows it.</param>
    public Weapon Apply(Weapon madeFrom) => Over(madeFrom) with { Descriptor = Descriptor, Name = Name, File = File };

    /// <summary>
    /// A descriptor for a weapon named <paramref name="name"/>: its letters and digits, with "_" between words:
    /// "M256 (DU)" is "Ammo_EZ_M256_DU". "_2", "_3"... are added when <paramref name="taken"/> has it already.
    /// </summary>
    public static string NewDescriptor(string name, IReadOnlyCollection<string> taken)
    {
        var letters = new System.Text.StringBuilder();
        foreach (char letter in name.Trim())
            letters.Append(char.IsAsciiLetterOrDigit(letter) ? letter : '_');
        string words = string.Join("_", letters.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (words == "") words = "Weapon";
        string descriptor = DescriptorPrefix + words;
        for (int number = 2; taken.Contains(descriptor); number++)
            descriptor = DescriptorPrefix + words + "_" + number;
        return descriptor;
    }

    /// <summary>
    /// Takes a weapon the mod adds out of the mod, and the copies made from it after: their original gone, they could
    /// not be made either. The loadouts given it carry their original weapon again.
    /// </summary>
    public static void RemoveFrom(Mod mod, string descriptor)
    {
        foreach (ModWeapon copy in mod.Weapons.Where(weapon => weapon.MadeFrom == descriptor).ToList()) RemoveFrom(mod, copy.Descriptor);
        mod.Weapons.RemoveAll(weapon => weapon.Descriptor == descriptor);
    }

    /// <summary>
    /// Gives a copy to the units with the loadouts given, in place of the weapon it was copied from. A loadout carries
    /// one copy of a weapon at most: one given another copy of the same weapon before is taken from that one (and from
    /// the copies made from it).
    /// </summary>
    public static void GiveTo(Mod mod, ModWeapon copy, IEnumerable<string> loadouts)
    {
        foreach (string loadout in loadouts)
        {
            foreach (ModWeapon other in mod.Weapons.Where(other => other.MadeFrom == copy.MadeFrom && other != copy).ToList())
                TakeBack(mod, other, [loadout]);
            if (!copy.Loadouts.Contains(loadout)) copy.Loadouts.Add(loadout);
        }
    }

    /// <summary>
    /// The units with the loadouts given carry the weapon a copy was copied from again: the loadouts are taken from the
    /// copy, and from the copies made from it, which they could not reach any more.
    /// </summary>
    public static void TakeBack(Mod mod, ModWeapon copy, IReadOnlyCollection<string> loadouts)
    {
        copy.Loadouts.RemoveAll(loadouts.Contains);
        foreach (ModWeapon madeFromIt in mod.Weapons.Where(other => other.MadeFrom == copy.Descriptor).ToList()) TakeBack(mod, madeFromIt, loadouts);
    }

    /// <summary>
    /// A unit is given a loadout of its own, a copy of <paramref name="loadout"/>, to change its weapons: the copies
    /// given that loadout are given the unit's own too, so the unit keeps carrying them.
    /// </summary>
    public static void FollowOwnLoadout(Mod mod, string loadout, string ownLoadout)
    {
        foreach (ModWeapon copy in mod.Weapons)
            if (copy.Loadouts.Contains(loadout) && !copy.Loadouts.Contains(ownLoadout)) copy.Loadouts.Add(ownLoadout);
    }

    /// <summary>A copy that shares nothing with this one, so changing one leaves the other as it was.</summary>
    public ModWeapon Copy() => FromJson(ToJson());

    // ---- the mod file ----

    /// <summary>The weapon as an entry of the mod file's <c>Weapons</c>; the values it keeps from its original are left out.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["Descriptor"] = Descriptor,
            ["MadeFrom"] = MadeFrom,
            ["File"] = File,
            ["Name"] = Name,
            ["Loadouts"] = new JsonArray([.. Loadouts.Select(loadout => (JsonNode)loadout)]),
        };
        WriteValues(json);
        return json;
    }

    /// <summary>Reads an entry of the mod file's <c>Weapons</c>; what it does not have is kept from its original.</summary>
    public static ModWeapon FromJson(JsonObject json)
    {
        var weapon = new ModWeapon
        {
            Descriptor = JsonValues.AsText(json["Descriptor"]),
            MadeFrom = JsonValues.AsText(json["MadeFrom"]),
            File = JsonValues.AsText(json["File"]),
            Name = JsonValues.AsText(json["Name"]),
            Loadouts = [.. (json["Loadouts"] as JsonArray ?? []).Select(JsonValues.AsText).Where(loadout => loadout != "")],
        };
        weapon.ReadValues(json);
        return weapon;
    }
}
