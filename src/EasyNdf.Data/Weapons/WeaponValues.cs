using System.Text.Json.Nodes;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// The values the Armory lets the user give a weapon (<see cref="WeaponStat.All"/>, <see cref="WeaponChoice.All"/>), by
/// field. A value not given keeps the weapon's own, so only what the user changes is written. Shared by the open mod's
/// changes to the game's weapons (<see cref="WeaponEdit"/>) and the weapons it adds as copies (<see cref="ModWeapon"/>).
/// </summary>
public abstract class WeaponValues
{
    private readonly Dictionary<string, double> given = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> chosen = new(StringComparer.Ordinal);

    /// <summary>A value given, by its field (<see cref="WeaponField"/>); null when it is not given.</summary>
    public double? Given(string field) => given.TryGetValue(field, out double value) ? value : null;

    /// <summary>
    /// Gives a value, by its field (<see cref="WeaponField"/>), or takes it back with null. Those the game writes as
    /// whole numbers are rounded to one; a field that cannot be changed is not taken.
    /// </summary>
    public void Give(string field, double? value)
    {
        if (WeaponStat.Of(field) is not WeaponStat stat) return;
        if (value is double number) given[field] = stat.IsWhole ? Math.Round(number) : number;
        else given.Remove(field);
    }

    /// <summary>A switch or choice given (<see cref="WeaponChoice"/>), as the file writes it; null when it is not given.</summary>
    public string? Chosen(string field) => chosen.GetValueOrDefault(field);

    /// <summary>
    /// Gives a switch or choice (<see cref="WeaponChoice"/>), as the file writes it ("True", "EFXPower/Moyen"), or takes
    /// it back with null; a field that is not one is not taken.
    /// </summary>
    public void Choose(string field, string? written)
    {
        if (WeaponChoice.Of(field) is null) return;
        if (written is not null) chosen[field] = written;
        else chosen.Remove(field);
    }

    /// <summary>
    /// The picture given, by the game's texture name ("Texture_Interface_Weapon_M68_120"): another weapon's, chosen
    /// with the Weapon pane's ✎; null to keep the weapon's own.
    /// </summary>
    public string? Picture { get; set; }

    /// <summary>The fields of the values given: the picture first, then the numbers, then the switches and choices.</summary>
    public IEnumerable<string> FieldsGiven()
    {
        if (Picture is not null) yield return WeaponField.Picture;
        foreach (WeaponStat stat in WeaponStat.All)
            if (given.ContainsKey(stat.Field)) yield return stat.Field;
        foreach (WeaponChoice choice in WeaponChoice.All)
            if (chosen.ContainsKey(choice.Field)) yield return choice.Field;
    }

    /// <summary>Whether it gives any value at all.</summary>
    public bool GivesAnything => given.Count > 0 || chosen.Count > 0 || Picture is not null;

    /// <summary>
    /// A weapon as it is with these values: <paramref name="weapon"/> with the values given in place of its own, and
    /// everything else (its traits, missile, the units carrying it) its own. A picture given changes the texture's
    /// name only: where the texture's file is, the game's list says (<see cref="ModWeaponView"/> looks it up).
    /// </summary>
    protected Weapon Over(Weapon weapon)
    {
        if (Picture is not null) weapon = weapon with { PictureTexture = Picture, PictureSource = null };
        foreach (WeaponStat stat in WeaponStat.All)
            if (Given(stat.Field) is double value) weapon = stat.Set(weapon, value);
        // a rule cannot set what the ammunition does not have, so neither does the Armory
        foreach (WeaponChoice choice in WeaponChoice.All)
            if (Chosen(choice.Field) is string written && choice.Read(weapon) is not null) weapon = choice.Set(weapon, written);
        return weapon;
    }

    // ---- the mod file ----

    /// <summary>
    /// Adds the values given to an entry of the mod file, as <c>Values: {Damage: 1.5, ...}</c>, and the switches and
    /// choices as <c>Choices: {FxPower: "EFXPower/Moyen", ...}</c>.
    /// </summary>
    protected void WriteValues(JsonObject json)
    {
        if (Picture is not null) json["Picture"] = Picture;
        if (given.Count > 0)
        {
            var values = new JsonObject();
            foreach (string field in FieldsGiven().Where(given.ContainsKey)) values[field] = given[field];
            json["Values"] = values;
        }
        if (chosen.Count > 0)
        {
            var choices = new JsonObject();
            foreach (string field in FieldsGiven().Where(chosen.ContainsKey)) choices[field] = chosen[field];
            json["Choices"] = choices;
        }
    }

    /// <summary>Reads the values of an entry of the mod file; a field it does not know is left out.</summary>
    protected void ReadValues(JsonObject json)
    {
        if (JsonValues.AsText(json["Picture"]) is string picture && picture != "") Picture = picture;
        if (json["Values"] is JsonObject values)
            foreach ((string field, JsonNode? node) in values)
                if (node is JsonValue value && value.TryGetValue(out double number)) Give(field, number);
        if (json["Choices"] is JsonObject choices)
            foreach ((string field, JsonNode? node) in choices)
                if (JsonValues.AsText(node) is string written && written != "") Choose(field, written);
    }
}
