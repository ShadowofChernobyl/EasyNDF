using System.Text.Json.Nodes;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// The open mod's changes to one of the game's weapons, made in the Weapon pane: values of its own. The weapon is
/// shared, so the change reaches every unit that carries it; the pane says how many before anything is changed. Only
/// the values the user changes are kept. The Armory's Weapons preset writes them as a locked rule
/// (<see cref="WeaponChangeRules"/>).
/// </summary>
public sealed class WeaponEdit : WeaponValues
{
    /// <summary>The weapon changed, by its ammunition's descriptor, such as "Ammo_Canon_AP_120mm".</summary>
    public string Descriptor { get; set; } = "";

    /// <summary>Whether it changes nothing, so it can be let go of.</summary>
    public bool ChangesNothing => !GivesAnything;

    /// <summary>
    /// The weapon as the Armory shows it, and as the game will have it: the game's weapon with the values given in
    /// place of its own. Kept the same as what the rules write (<see cref="WeaponChangeRules"/>).
    /// </summary>
    public Weapon Apply(Weapon weapon) => Over(weapon);

    /// <summary>The open mod's change to a weapon, by its descriptor; null when it changes none.</summary>
    public static WeaponEdit? Of(Mod mod, string descriptor) => mod.WeaponEdits.FirstOrDefault(edit => edit.Descriptor == descriptor);

    /// <summary>
    /// The mod's change to a weapon, made when it has none yet; once changed, <see cref="LetGoOfIfEmpty"/> takes it out
    /// again when it changes nothing any more.
    /// </summary>
    public static WeaponEdit For(Mod mod, string descriptor)
    {
        if (Of(mod, descriptor) is WeaponEdit edit) return edit;
        edit = new WeaponEdit { Descriptor = descriptor };
        mod.WeaponEdits.Add(edit);
        return edit;
    }

    /// <summary>Takes the mod's change to a weapon out when it changes nothing (each value set back to the game's).</summary>
    public static void LetGoOfIfEmpty(Mod mod, string descriptor) =>
        mod.WeaponEdits.RemoveAll(edit => edit.Descriptor == descriptor && edit.ChangesNothing);

    /// <summary>A copy that shares nothing with this one, so changing one leaves the other as it was.</summary>
    public WeaponEdit Copy() => FromJson(ToJson());

    // ---- the mod file ----

    /// <summary>The change as an entry of the mod file's <c>WeaponEdits</c>; the values kept from the game's weapon are left out.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject { ["Descriptor"] = Descriptor };
        WriteValues(json);
        return json;
    }

    /// <summary>Reads an entry of the mod file's <c>WeaponEdits</c>; what it does not have is kept from the game's weapon.</summary>
    public static WeaponEdit FromJson(JsonObject json)
    {
        var edit = new WeaponEdit { Descriptor = JsonValues.AsText(json["Descriptor"]) };
        edit.ReadValues(json);
        return edit;
    }
}
