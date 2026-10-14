using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EasyNdf.Data;

/// <summary>A language pack: the English texts of the app, each with its translation.</summary>
public sealed class LanguagePack
{
    /// <summary>A short language code such as "fr". Never "en": English is the app's own text.</summary>
    public string Code { get; set; } = "";

    /// <summary>The language's name, shown in the Language dropdown.</summary>
    public string Name { get; set; } = "";

    /// <summary>The app version the pack was made for, so the app can say how much is missing.</summary>
    public string MadeFor { get; set; } = "";

    /// <summary>Each English text, with its translation.</summary>
    public Dictionary<string, string> Strings { get; set; } = [];
}

/// <summary>
/// The imported language packs, kept in the Languages folder: one file per pack, named after its code
/// (<c>{code, name, madeFor, strings}</c>). The built-in packs are part of the app, not files.
/// </summary>
public sealed partial class LanguageStore
{
    private readonly string folder;

    /// <summary>Reads every pack in the folder.</summary>
    public LanguageStore(string languagesFolder)
    {
        folder = languagesFolder;
        Directory.CreateDirectory(folder);
        foreach (string path in Directory.GetFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (JsonFiles.TryRead(path) is not JsonObject json) continue;
                LanguagePack pack = Read(json);
                Packs[pack.Code] = pack;
            }
            catch (ModFileException) { } // a pack edited by hand into something unreadable is left out
        }
    }

    /// <summary>The packs by code.</summary>
    public Dictionary<string, LanguagePack> Packs { get; } = [];

    /// <summary>
    /// "Import..." on the Settings tab: reads a language pack and keeps it, replacing a pack of the same code.
    /// Throws <see cref="ModFileException"/> with the HTML app's reason when the file is not a usable pack.
    /// </summary>
    public LanguagePack Import(string text)
    {
        JsonNode? json = null;
        try { json = JsonFiles.Parse(text); } catch (JsonException) { }
        if (json is not JsonObject file) throw new ModFileException("It is not a language pack.");

        LanguagePack pack = Read(file);
        Packs[pack.Code] = pack;
        JsonFiles.Write(Path.Combine(folder, pack.Code + ".json"), new JsonObject
        {
            ["code"] = pack.Code,
            ["name"] = pack.Name,
            ["madeFor"] = pack.MadeFor,
            ["strings"] = new JsonObject(pack.Strings.Select(s => KeyValuePair.Create(s.Key, (JsonNode?)s.Value))),
        });
        return pack;
    }

    /// <summary>Removes a pack and its file.</summary>
    public void Delete(string code)
    {
        Packs.Remove(code);
        File.Delete(Path.Combine(folder, code + ".json"));
    }

    /// <summary>
    /// Reads a pack's JSON as the HTML app does. Its texts may be under "strings", or under "strings" in each of its
    /// "sections" (the layout of the English pack people translate). Texts left empty or the same as the English are left out.
    /// </summary>
    public static LanguagePack Read(JsonObject json)
    {
        if (json["strings"] is null && json["sections"] is not JsonArray) throw new ModFileException("It is not a language pack.");

        string code = NotCodeCharacters().Replace(JsonValues.AsText(json["code"]).ToLowerInvariant(), "");
        if (code.Length > 12) code = code[..12];
        if (code == "" || code == "en")
            throw new ModFileException("Its \"code\" must be a short language code other than \"en\", such as \"fr\".");

        string name = JsonValues.IsTruthy(json["name"]) ? JsonValues.AsText(json["name"]) : code;
        var pack = new LanguagePack { Code = code, Name = name.Length > 40 ? name[..40] : name, MadeFor = JsonValues.AsText(json["madeFor"]) };
        TakeStrings(json["strings"], pack.Strings);
        if (json["sections"] is JsonArray sections)
            foreach (JsonNode? section in sections) TakeStrings((section as JsonObject)?["strings"], pack.Strings);

        if (pack.Strings.Count == 0) throw new ModFileException("None of its texts are translated yet.");
        return pack;
    }

    private static void TakeStrings(JsonNode? strings, Dictionary<string, string> into)
    {
        if (strings is not JsonObject texts) return;
        foreach (var (english, translation) in texts)
        {
            if (translation is JsonValue value && value.GetValueKind() == JsonValueKind.String)
            {
                string text = value.GetValue<string>();
                if (text != "" && text != english) into[english] = text;
            }
        }
    }

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex NotCodeCharacters();
}
