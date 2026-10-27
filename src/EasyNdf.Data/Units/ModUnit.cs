using System.Text.Json.Nodes;

namespace EasyNdf.Data.Units;

/// <summary>
/// A unit the mod adds, made in the Armory: a copy of another unit (its 3D model, weapons, values and all), given a
/// name of its own and whatever the user changes. Nothing of the copy is taken away: a unit missing something its model
/// expects (its weapons, for one) can stop the game loading, so only the values the user gives are changed. A duplicate
/// is the same, made with Duplicate Unit…, where only the name is given. The mod keeps what it gives the unit; the Armory
/// presets' rules write it into the game's files.
/// </summary>
public sealed class ModUnit : UnitValues
{
    /// <summary>The prefix of every unit descriptor the app makes, so the game's own are never taken.</summary>
    public const string DescriptorPrefix = "Descriptor_Unit_EZ_";

    /// <summary>Its descriptor, such as "Descriptor_Unit_EZ_Abrams_Hunter_US"; it stays the same when the unit is renamed.</summary>
    public string Descriptor { get; set; } = "";

    /// <summary>The unit it is made from, by descriptor: everything not given here is that unit's.</summary>
    public string MadeFrom { get; set; } = "";

    /// <summary>Whether it was made with Duplicate Unit… rather than New Unit…; its rule is named after it.</summary>
    public bool IsDuplicate { get; set; }

    /// <summary>Its name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Its number for deck codes, given when it is made.</summary>
    public int? DeckNumber { get; set; }

    /// <summary>
    /// The unit as the Armory shows it, and as the game will have it: the unit it is made from with what this one
    /// gives it. Kept the same as what the rules write (<see cref="UnitChangeRules"/>).
    /// </summary>
    /// <param name="madeFrom">The unit it is made from, as the game has it.</param>
    public Unit Apply(Unit madeFrom) => Over(madeFrom, Descriptor, Name, UnitChangeRules.UnitsFile, DeckNumber, divisions: []);

    /// <summary>
    /// A descriptor for a unit named <paramref name="name"/>: its letters and digits, with "_" between words, then its
    /// nation, as the game names its own: "Abrams Hunter" of the US is "Descriptor_Unit_EZ_Abrams_Hunter_US".
    /// "_2", "_3"... are added before the nation when <paramref name="taken"/> has it already.
    /// </summary>
    public static string NewDescriptor(string name, string nation, IReadOnlyCollection<string> taken)
    {
        var letters = new System.Text.StringBuilder();
        foreach (char letter in name.Trim())
            letters.Append(char.IsAsciiLetterOrDigit(letter) ? letter : '_');
        string words = string.Join("_", letters.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (words == "") words = "Unit";
        string ending = nation == "" ? "" : "_" + nation;
        string descriptor = DescriptorPrefix + words + ending;
        for (int number = 2; taken.Contains(descriptor); number++)
            descriptor = DescriptorPrefix + words + "_" + number + ending;
        return descriptor;
    }

    /// <summary>
    /// The next deck number free for a unit: one more than the highest the game's units and the mod's new ones have.
    /// </summary>
    public static int NextDeckNumber(IEnumerable<Unit> game, Mod mod)
    {
        int highest = game.Select(unit => unit.DeckNumber ?? 0).Concat(mod.Units.Select(unit => unit.DeckNumber ?? 0)).DefaultIfEmpty(0).Max();
        return highest + 1;
    }

    /// <summary>
    /// Takes a unit the mod adds out of the mod: the unit, and every place the mod's divisions use it (their units,
    /// signature units, transports and battlegroup cards). Left there, they would point at a unit no longer made,
    /// and the game does not start with such a reference. A game division changed in nothing else is let go of.
    /// </summary>
    public static void RemoveFrom(Mod mod, string descriptor)
    {
        mod.Units.RemoveAll(unit => unit.Descriptor == descriptor);
        foreach (Divisions.ModDivision division in mod.Divisions)
        {
            division.Added.RemoveAll(unit => unit.Unit == descriptor);
            for (int i = 0; i < division.Added.Count; i++)
                if (division.Added[i].Transports.Contains(descriptor))
                    division.Added[i] = division.Added[i] with { Transports = [.. division.Added[i].Transports.Where(transport => transport != descriptor)] };
            division.StandoutUnits?.Remove(descriptor);
            division.BattlegroupCards?.RemoveAll(card => card.Unit == descriptor || card.Transport == descriptor);
        }
        mod.Divisions.RemoveAll(division => division.ChangesNothing);
    }

    /// <summary>A copy that shares nothing with this one, so changing one leaves the other as it was.</summary>
    public ModUnit Copy() => FromJson(ToJson());

    // ---- the mod file ----

    /// <summary>The unit as an entry of the mod file's <c>Units</c>; the values it keeps from the unit it is made from are left out.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["Descriptor"] = Descriptor,
            ["MadeFrom"] = MadeFrom,
            ["IsDuplicate"] = IsDuplicate,
            ["Name"] = Name,
        };
        AddIfGiven(json, "DeckNumber", DeckNumber);
        WriteValues(json);
        return json;
    }

    /// <summary>Reads an entry of the mod file's <c>Units</c>; what it does not have is kept from the unit it is made from.</summary>
    public static ModUnit FromJson(JsonObject json)
    {
        var unit = new ModUnit
        {
            Descriptor = JsonValues.AsText(json["Descriptor"]),
            MadeFrom = JsonValues.AsText(json["MadeFrom"]),
            IsDuplicate = JsonValues.IsTruthy(json["IsDuplicate"]),
            Name = JsonValues.AsText(json["Name"]),
            DeckNumber = WholeNumber(json["DeckNumber"]),
        };
        unit.ReadValues(json);
        return unit;
    }
}
