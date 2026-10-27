using System.Text.Json.Nodes;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// One change the user makes to a unit's weapons, in the Unit pane or the New Unit window's Weapons step, by its place
/// in the unit's loadout as the game has it (<see cref="Loadout"/>):
/// <list type="bullet">
/// <item><see cref="Swap"/>: the weapon mounted there is another, the mount keeping its turret, ammo box, flash and sound;</item>
/// <item><see cref="Count"/>: the mount has another number of the weapon (NbWeapons);</item>
/// <item><see cref="Salvos"/>: an ammo box holds another number of salvos (the loadout's Salves);</item>
/// <item><see cref="Remove"/>: the weapon mounted there is taken off (not yet tried in the game);</item>
/// <item><see cref="Add"/>: a weapon is mounted on a turret, after its own, with an ammo box of its own (not yet tried in the game).</item>
/// </list>
/// The unit's rules give it a loadout of its own, a copy of the one it had, and make the changes there only
/// (<see cref="LoadoutRules"/>).
/// </summary>
/// <param name="Kind"><see cref="Swap"/>, <see cref="Count"/>, <see cref="Salvos"/>, <see cref="Remove"/> or <see cref="Add"/>.</param>
/// <param name="Turret">The turret's place in the loadout; unused for <see cref="Salvos"/>.</param>
/// <param name="Mount">
/// The mount's place on the turret; for <see cref="Add"/>, the place the new weapon takes after the turret's own; for
/// <see cref="Salvos"/>, the ammo box's place.
/// </param>
/// <param name="Ammunition">The weapon put there, by its ammunition's descriptor; empty for the others.</param>
/// <param name="MountText">
/// For <see cref="Remove"/>: the mount as the game's file has it, written as the engine writes it, which the rule finds
/// to take out. For <see cref="Add"/>: the new mount, as <see cref="LoadoutRules.NewMountText"/> writes it.
/// </param>
/// <param name="Number">
/// For <see cref="Count"/>: how many of the weapon; for <see cref="Salvos"/>: the box's salvos; for <see cref="Add"/>:
/// the salvos of the new weapon's own ammo box.
/// </param>
public sealed record LoadoutChange(string Kind, int Turret, int Mount, string Ammunition = "", string MountText = "", int Number = 0)
{
    /// <summary>The kinds of change.</summary>
    public const string Swap = "Swap", Count = "Count", Salvos = "Salvos", Remove = "Remove", Add = "Add";

    /// <summary>The change as an entry of the mod file; what it does not need is left out.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject { ["Kind"] = Kind, ["Turret"] = Turret, ["Mount"] = Mount };
        if (Ammunition != "") json["Ammunition"] = Ammunition;
        if (MountText != "") json["MountText"] = MountText;
        if (Number != 0) json["Number"] = Number;
        return json;
    }

    /// <summary>Reads an entry of the mod file; null for one this version does not know.</summary>
    public static LoadoutChange? FromJson(JsonNode? node)
    {
        if (node is not JsonObject json) return null;
        string kind = JsonValues.AsText(json["Kind"]);
        if (kind is not (Swap or Count or Salvos or Remove or Add)) return null;
        // "Rounds" was the number's first name
        return new LoadoutChange(kind, WholeNumber(json["Turret"]), WholeNumber(json["Mount"]),
            JsonValues.AsText(json["Ammunition"]), JsonValues.AsText(json["MountText"]), WholeNumber(json["Number"] ?? json["Rounds"]));
    }

    private static int WholeNumber(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out double number) ? (int)Math.Round(number) : 0;
}
