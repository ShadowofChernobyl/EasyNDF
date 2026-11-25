using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EasyNdf.Data.Units;

/// <summary>A file of a unit's own 3D model: where the app keeps it, and its name in the mod's model folder.</summary>
/// <param name="Kept">Where the app keeps it (its picture folders), found again by name like a unit's own picture.</param>
/// <param name="Name">Its name in the mod's model folder, such as "Tank.fbx" or "Tank_NM.png": a model's pictures keep the names it gives them.</param>
/// <param name="Level">For a model file, its detail level: "High" (up close), "Mid" or "Low"; null for a picture.</param>
public sealed record OwnModelFile(string Kept, string Name, string? Level = null);

/// <summary>
/// A unit's own 3D model, chosen in the New Unit window or the Armory's Change 3D Model window: the .fbx files and
/// pictures the export copies into the mod, and what the copied unit's look was when it was chosen. The unit's look
/// is filed under a key of its own (<see cref="Key"/>), so the game draws this model for it with the copied unit's
/// turrets, guns, effects and sounds:
/// <list type="bullet">
/// <item>its models are declared in SharedResources.ndf (Modele_&lt;key&gt;, and _MID and _LOW when given);</item>
/// <item>its detail levels are a copy of the copied unit's in DepictionAlternatives.ndf, with its own models;</item>
/// <item>its depiction is a copy of the copied unit's in DepictionVehicles.ndf (what turns, fires and moves);</item>
/// <item>its placement ghost and its showroom entry follow (DepictionGhosts.ndf, DepictionVehiclesShowroom.ndf);</item>
/// <item>the unit's MimeticName, BlackHoleKey, ReferenceMesh and ShowRoomBlackHoleIdentifier name them.</item>
/// </list>
/// This is how mods with models of their own add them (World in Flames' and YSM's units). Ground vehicles only: their
/// depictions are TacticVehicleDepictionRegistration.
/// </summary>
public sealed partial class OwnModel
{
    /// <summary>The key the unit's look is filed under, such as "EZ_Abrams_Hunter_US".</summary>
    public string Key { get; set; } = "";

    /// <summary>The folder under GameData its files go into, such as "Assets/3D/Units/US/Char/EZ_Abrams_Hunter_US".</summary>
    public string Folder { get; set; } = "";

    /// <summary>Its files: the models (one for each detail level given) and their pictures.</summary>
    public List<OwnModelFile> Files { get; set; } = [];

    /// <summary>The copied unit's look's key, such as "M1A1_Abrams_US".</summary>
    public string CopiedKey { get; set; } = "";

    /// <summary>The copied unit's depiction, as DepictionVehicles.ndf had it when the model was chosen (its whole registration).</summary>
    public string Depiction { get; set; } = "";

    /// <summary>The copied unit's detail levels, as DepictionAlternatives.ndf had them (its whole Alternatives_ list).</summary>
    public string Alternatives { get; set; } = "";

    /// <summary>The model file of a detail level; null when none is given for it.</summary>
    public OwnModelFile? Model(string level) => Files.FirstOrDefault(file => file.Level == level);

    /// <summary>The key a unit's own look gets: "EZ_" and its descriptor without "Descriptor_Unit_" (and without its own "EZ_").</summary>
    public static string KeyFor(string descriptor) => "EZ_" + ModUnit.ShortName(descriptor);

    /// <summary>The model resource's name for a detail level: Modele_&lt;key&gt;, Modele_&lt;key&gt;_MID, Modele_&lt;key&gt;_LOW.</summary>
    public string Resource(string level) => "Modele_" + Key + level switch { "Mid" => "_MID", "Low" => "_LOW", _ => "" };

    /// <summary>A copy that shares nothing with this one.</summary>
    public OwnModel Copy() => FromJson(ToJson())!;

    // ---- the mod file ----

    public JsonObject ToJson() => new()
    {
        ["Key"] = Key,
        ["Folder"] = Folder,
        ["Files"] = new JsonArray([.. Files.Select(file =>
        {
            var json = new JsonObject { ["Kept"] = file.Kept, ["Name"] = file.Name };
            if (file.Level is not null) json["Level"] = file.Level;
            return (JsonNode)json;
        })]),
        ["CopiedKey"] = CopiedKey,
        ["Depiction"] = Depiction,
        ["Alternatives"] = Alternatives,
    };

    public static OwnModel? FromJson(JsonNode? node)
    {
        if (node is not JsonObject json) return null;
        return new OwnModel
        {
            Key = JsonValues.AsText(json["Key"]),
            Folder = JsonValues.AsText(json["Folder"]),
            Files = [.. (json["Files"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(file => new OwnModelFile(JsonValues.AsText(file["Kept"]), JsonValues.AsText(file["Name"]), file["Level"] is null ? null : JsonValues.AsText(file["Level"])))],
            CopiedKey = JsonValues.AsText(json["CopiedKey"]),
            Depiction = JsonValues.AsText(json["Depiction"]),
            Alternatives = JsonValues.AsText(json["Alternatives"]),
        };
    }

    // ---- what the rules add ----

    /// <summary>The model declarations for SharedResources.ndf: each detail level's path and its TResourceMesh.</summary>
    public string ResourcesText()
    {
        var lines = new List<string>();
        foreach (string level in new[] { "High", "Mid", "Low" })
        {
            if (Model(level) is not OwnModelFile file) continue;
            string constant = "ModelFile_" + Key + level switch { "Mid" => "_MID", "Low" => "_LOW", _ => "" };
            lines.Add("export " + constant + " is 'GameData:/" + Folder.TrimEnd('/') + "/" + file.Name + "'");
            lines.Add("export " + Resource(level) + " is TResourceMesh\n(\n    Mesh = " + constant + "\n)");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The unit's detail levels for DepictionAlternatives.ndf: the copied unit's, renamed, each level's model its own
    /// (the model up close where no Mid or Low is given).
    /// </summary>
    public string AlternativesText()
    {
        string text = AlternativesHead().Replace(Alternatives, "Alternatives_" + Key + " is", 1);
        return MeshDescriptor().Replace(text, match =>
        {
            string level = match.Groups[1].Value;
            string own = Model(level) is null ? Resource("High") : Resource(level);
            return match.Value[..(match.Groups[2].Index - match.Index)] + "$/GFX/DepictionResources/" + own;
        });
    }

    /// <summary>The unit's depiction for DepictionVehicles.ndf: the copied unit's, under its own key, with its own detail levels and model.</summary>
    public string DepictionText()
    {
        string text = Depiction;
        text = BlackHoleKey().Replace(text, match => match.Groups[1].Value + match.Groups[2].Value + Key + match.Groups[2].Value, 1);
        text = AlternativesValue().Replace(text, match => match.Groups[1].Value + "Alternatives_" + Key, 1);
        // a carried squad sits in the vehicle's own model
        text = text.Replace("$/GFX/DepictionResources/Modele_" + CopiedKey + ")", "$/GFX/DepictionResources/" + Resource("High") + ")", StringComparison.Ordinal);
        return text;
    }

    /// <summary>The unit's placement ghost, for DepictionGhosts.ndf.</summary>
    public string GhostText() =>
        "unnamed GhostVehicleDepictionRegistration\n(\n    Alternatives = Alternatives_" + Key + "\n    BlackHoleKey = \"" + Key + "\"\n    Selector = SpecificMechanicalDepictionSelector\n)";

    /// <summary>The unit's look in the showroom, for DepictionVehiclesShowroom.ndf.</summary>
    public string ShowroomText() =>
        "unnamed ShowroomVehicleDepictionRegistration\n(\n    BlackHoleKey = \"showroom_" + Key + "\"\n    Alternatives = Alternatives_" + Key + "\n)";

    // ---- reading the copied unit's look ----

    /// <summary>
    /// The copied unit's depiction (its whole TacticVehicleDepictionRegistration in DepictionVehicles.ndf) and its
    /// detail levels (its Alternatives_ list in DepictionAlternatives.ndf); null when either is not found: a unit whose
    /// look is not a ground vehicle's (an aircraft, a helicopter, infantry) cannot take a model of its own here.
    /// </summary>
    /// <param name="vehicles">DepictionVehicles.ndf's text.</param>
    /// <param name="alternatives">DepictionAlternatives.ndf's text.</param>
    /// <param name="key">The copied unit's BlackHoleKey.</param>
    public static (string Depiction, string Alternatives)? ReadLook(string vehicles, string alternatives, string key)
    {
        string? depiction = null;
        foreach (Match start in Registration().Matches(vehicles))
        {
            int open = vehicles.IndexOf('(', start.Index);
            int close = open < 0 ? -1 : Closing(vehicles, open, '(', ')');
            if (close < 0) continue;
            string block = vehicles[start.Index..(close + 1)];
            if (BlackHoleKey().Match(block) is { Success: true } found && found.Groups[3].Value == key)
            {
                depiction = block;
                break;
            }
        }
        if (depiction is null) return null;
        Match list = Regex.Match(alternatives, @"(?m)^\s*(?:export\s+)?Alternatives_" + Regex.Escape(key) + @"\s+is\s*\[");
        if (!list.Success) return null;
        int end = Closing(alternatives, list.Index + list.Length - 1, '[', ']');
        if (end < 0) return null;
        return (depiction, alternatives[list.Index..(end + 1)].Trim());
    }

    /// <summary>Where a bracket opened at <paramref name="open"/> closes, past strings and comments; -1 when it does not.</summary>
    private static int Closing(string text, int open, char opening, char closing)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                int end = text.IndexOf(c, i + 1);
                if (end < 0) return -1;
                i = end;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int end = text.IndexOf('\n', i);
                if (end < 0) return -1;
                i = end;
            }
            else if (c == opening) depth++;
            else if (c == closing && --depth == 0) return i;
        }
        return -1;
    }

    [GeneratedRegex(@"(?m)^\s*unnamed\s+TacticVehicleDepictionRegistration\b")]
    private static partial Regex Registration();

    /// <summary>BlackHoleKey = 'key' or "key": what comes before the key (group 1), the quote (group 2), the key (group 3).</summary>
    [GeneratedRegex(@"(BlackHoleKey\s*=\s*)(['""])([^'""]*)\2")]
    private static partial Regex BlackHoleKey();

    [GeneratedRegex(@"(Alternatives\s*=\s*)Alternatives_[A-Za-z0-9_]+")]
    private static partial Regex AlternativesValue();

    [GeneratedRegex(@"^\s*(?:export\s+)?Alternatives_[A-Za-z0-9_]+\s+is")]
    private static partial Regex AlternativesHead();

    /// <summary>A detail level's model: DepictionVisual_LOD_High( ... MeshDescriptor = $/GFX/DepictionResources/Modele_x: the level (group 1), the reference (group 2).</summary>
    [GeneratedRegex(@"DepictionVisual_LOD_(High|Mid|Low)\b[^)]*?MeshDescriptor\s*=\s*(\$/GFX/DepictionResources/[A-Za-z0-9_]+)")]
    private static partial Regex MeshDescriptor();
}
