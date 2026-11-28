using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;

namespace EasyNdf.Data;

/// <summary>
/// The pictures a mod's Armory changes name (a division's emblem, a unit's card picture): a mod names each by the
/// path it had on the computer it was made on, and the app finds it by its file name (ModFileViews.FindPicture). A
/// mod file and a backup carry them under "Pictures", by file name, so the mod arrives whole on another computer.
/// </summary>
public static partial class ModFiles
{
    /// <summary>The key the pictures ride under in a mod file or a backup's first entry.</summary>
    public const string PicturesKey = "Pictures";

    /// <summary>The properties that name a picture, wherever they are in a mod: divisions, unit changes, new units, their rules.</summary>
    private static readonly string[] PictureProperties = ["EmblemFile", DivisionChangeRules.PictureFileKey];

    /// <summary>The largest picture carried, in bytes: an emblem or a card picture is far smaller.</summary>
    public const int MostPictureBytes = 32 * 1024 * 1024;

    /// <summary>
    /// Whether a name is one a carried picture may have: a plain file name (no folder), a .png, nothing hidden. Only
    /// such names are read or written, so a mod file cannot put a picture anywhere else.
    /// </summary>
    public static bool IsPictureName(string name) =>
        name.Length is > 4 and < 200 && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.StartsWith('.') && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether bytes start as a PNG does.</summary>
    public static bool IsPng(byte[] bytes) =>
        bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    /// <summary>Every picture path a mod's JSON names, once each, in order.</summary>
    public static List<string> PicturePathsOf(JsonNode? json)
    {
        var found = new List<string>();
        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach ((string key, JsonNode? value) in obj)
                    {
                        if (PictureProperties.Contains(key) && value is JsonValue text && text.TryGetValue(out string? path) && path.Trim() != "" && !found.Contains(path))
                            found.Add(path);
                        else Walk(value);
                    }
                    break;
                case JsonArray list:
                    foreach (JsonNode? item in list) Walk(item);
                    break;
            }
        }
        Walk(json);
        return found;
    }

    /// <summary>The pictures a mod's JSON names that can be found, by file name; the first of two with one name is kept.</summary>
    public static Dictionary<string, byte[]> PicturesOf(JsonNode? json, Func<string, byte[]?> picture)
    {
        var pictures = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in PicturePathsOf(json))
        {
            string name = Path.GetFileName(path.Replace('/', '\\').Split('\\')[^1]);
            if (!IsPictureName(name) || pictures.ContainsKey(name)) continue;
            if (picture(path) is byte[] bytes && IsPng(bytes) && bytes.Length <= MostPictureBytes) pictures[name] = bytes;
        }
        return pictures;
    }

    private static JsonObject? CarriedPicturesToJson(Dictionary<string, byte[]> pictures)
    {
        if (pictures.Count == 0) return null;
        var json = new JsonObject();
        foreach ((string name, byte[] bytes) in pictures) json[name] = Convert.ToBase64String(bytes);
        return json;
    }

    /// <summary>The pictures a mod file or backup carries; one with a name it may not have, or that is not a PNG, is left out.</summary>
    private static Dictionary<string, byte[]> CarriedPicturesFromJson(JsonNode? pictures)
    {
        var carried = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, JsonNode? text) in pictures as JsonObject ?? [])
        {
            if (!IsPictureName(name)) continue;
            try
            {
                byte[] bytes = Convert.FromBase64String(JsonValues.AsText(text));
                if (IsPng(bytes) && bytes.Length <= MostPictureBytes) carried[name] = bytes;
            }
            catch (FormatException) { /* a damaged picture is left out; its unit or division shows none */ }
        }
        return carried;
    }

    /// <summary>
    /// Points a mod's pictures at their new places: each picture path whose file name is a key becomes the value. For
    /// a picture an import had to keep under another name, as one of its name was already here and different.
    /// </summary>
    public static void RepointPictures(Mod mod, IReadOnlyDictionary<string, string> byFileName)
    {
        if (byFileName.Count == 0) return;
        string? Moved(string? path) =>
            path is not null && byFileName.TryGetValue(Path.GetFileName(path.Replace('/', '\\').Split('\\')[^1]), out string? now) ? now : path;
        foreach (ModDivision division in mod.Divisions) division.EmblemFile = Moved(division.EmblemFile);
        foreach (Units.UnitEdit edit in mod.UnitEdits) edit.PictureFile = Moved(edit.PictureFile);
        foreach (Units.ModUnit unit in mod.Units) unit.PictureFile = Moved(unit.PictureFile);
        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (string key in obj.Select(pair => pair.Key).ToList())
                    {
                        if (PictureProperties.Contains(key) && obj[key] is JsonValue text && text.TryGetValue(out string? path)) obj[key] = Moved(path);
                        else Walk(obj[key]);
                    }
                    break;
                case JsonArray list:
                    foreach (JsonNode? item in list) Walk(item);
                    break;
            }
        }
        foreach (ModPreset ruleSet in mod.Presets) Walk(ruleSet.Rules);
    }
}
