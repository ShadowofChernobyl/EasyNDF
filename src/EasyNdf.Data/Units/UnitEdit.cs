using System.Text.Json.Nodes;

namespace EasyNdf.Data.Units;

/// <summary>
/// The open mod's changes to one of the game's own units, made in the Unit pane: a name, nation, role, card picture
/// or stats of its own. Only the values the user changes are kept; everything else stays the game's, and nothing is
/// taken from the unit. The Armory's Units preset writes them as a locked rule (<see cref="UnitChangeRules"/>).
/// </summary>
public sealed class UnitEdit : UnitValues
{
    /// <summary>The unit changed, by its descriptor, such as "Descriptor_Unit_M1A1_Abrams_US".</summary>
    public string Descriptor { get; set; } = "";

    /// <summary>Its name; null to keep the game's.</summary>
    public string? Name { get; set; }

    /// <summary>Whether it changes nothing, so it can be let go of.</summary>
    public bool ChangesNothing => Name is null && !GivesAnything;

    /// <summary>The fields it changes (<see cref="UnitField"/>), for the marks that show them as changed.</summary>
    public IEnumerable<string> FieldsChanged() => Name is null ? FieldsGiven() : FieldsGiven().Prepend(UnitField.Name);

    /// <summary>
    /// The unit as the Armory shows it, and as the game will have it: the game's unit with the values given in place
    /// of its own. Kept the same as what the rules write (<see cref="UnitChangeRules"/>).
    /// </summary>
    /// <param name="unit">The unit, as the game (and the mod's other rules) make it.</param>
    public Unit Apply(Unit unit) => Over(unit, unit.Descriptor, Name ?? unit.Name, unit.File, unit.DeckNumber, unit.Divisions);

    /// <summary>The open mod's change to a unit, by its descriptor; null when it changes none.</summary>
    public static UnitEdit? Of(Mod mod, string descriptor) => mod.UnitEdits.FirstOrDefault(edit => edit.Descriptor == descriptor);

    /// <summary>
    /// The mod's change to a unit, made when it has none yet; once changed, <see cref="LetGoOfIfEmpty"/> takes it out
    /// again when it changes nothing any more.
    /// </summary>
    public static UnitEdit For(Mod mod, string descriptor)
    {
        if (Of(mod, descriptor) is UnitEdit edit) return edit;
        edit = new UnitEdit { Descriptor = descriptor };
        mod.UnitEdits.Add(edit);
        return edit;
    }

    /// <summary>Takes the mod's change to a unit out when it changes nothing (each value set back to the game's).</summary>
    public static void LetGoOfIfEmpty(Mod mod, string descriptor) =>
        mod.UnitEdits.RemoveAll(edit => edit.Descriptor == descriptor && edit.ChangesNothing);

    /// <summary>A copy that shares nothing with this one, so changing one leaves the other as it was.</summary>
    public UnitEdit Copy() => FromJson(ToJson());

    // ---- the mod file ----

    /// <summary>The change as an entry of the mod file's <c>UnitEdits</c>; the values kept from the game's unit are left out.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject { ["Descriptor"] = Descriptor };
        AddIfGiven(json, "Name", Name);
        WriteValues(json);
        return json;
    }

    /// <summary>Reads an entry of the mod file's <c>UnitEdits</c>; what it does not have is kept from the game's unit.</summary>
    public static UnitEdit FromJson(JsonObject json)
    {
        var edit = new UnitEdit { Descriptor = JsonValues.AsText(json["Descriptor"]), Name = Text(json["Name"]) };
        edit.ReadValues(json);
        return edit;
    }
}
