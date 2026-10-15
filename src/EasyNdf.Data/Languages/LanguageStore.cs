using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EasyNdf.Data.Languages;

/// <summary>
/// The language packs the user has imported, kept in the Languages folder, one file per pack named after its code.
/// </summary>
/// <remarks>
/// A pack file looks like this; Export writes it with every name of the app, and an empty "text" for each one not yet
/// translated, so a translator fills in the blanks and imports it again:
/// <code>
/// {
///   "code": "fr",
///   "name": "Français",
///   "madeFor": "1.0.0",
///   "texts": {
///     "Files.AddFolder": { "english": "Add folder...", "text": "Ajouter un dossier..." }
///   }
/// }
/// </code>
/// A pack made for the HTML app, whose texts are listed by their English instead of by name, can be imported too.
/// </remarks>
public sealed partial class LanguageStore
{
    private readonly string? folder;

    /// <summary>Packs in memory only, never saved: for the designer and for drawing the app without touching saved data.</summary>
    public LanguageStore() { }

    /// <summary>Reads every pack in the folder. A file that cannot be read as a pack is left out.</summary>
    public LanguageStore(string languagesFolder)
    {
        folder = languagesFolder;
        Directory.CreateDirectory(folder);
        foreach (string path in Directory.GetFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (JsonFiles.TryRead(path) is JsonObject json) Keep(Read(json, english: null));
            }
            catch (ModFileException) { } // a pack edited by hand into something unreadable is left out
        }
    }

    /// <summary>The packs by code.</summary>
    public Dictionary<string, LanguagePack> Packs { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// "Import...": reads a language pack and keeps it, replacing a pack of the same code. Throws
    /// <see cref="ModFileException"/>, with the reason, when the file is not a usable pack.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <param name="english">The app's English texts by name, to read an HTML app pack, which lists its texts by their English.</param>
    public LanguagePack Import(string text, IReadOnlyDictionary<string, string> english)
    {
        JsonNode? json = null;
        try { json = JsonFiles.Parse(text); } catch (JsonException) { }
        if (json is not JsonObject file) throw new ModFileException("It is not a language pack.");

        LanguagePack pack = Read(file, english);
        Keep(pack);
        if (folder is not null) JsonFiles.Write(Path.Combine(folder, pack.Code + ".json"), ToJson(pack, english: null));
        return pack;
    }

    /// <summary>
    /// Reads a pack file without keeping it: for the languages built into the app. Throws <see cref="ModFileException"/>
    /// when the file is not a usable pack.
    /// </summary>
    public static LanguagePack ReadPack(string text)
    {
        if (JsonFiles.Parse(text) is not JsonObject file) throw new ModFileException("It is not a language pack.");
        return Read(file, english: null);
    }

    /// <summary>
    /// A built-in language with the user's own pack for it laid over it: each text the user's pack has wins, and the
    /// built-in pack fills in the rest. Either may be missing.
    /// </summary>
    public static LanguagePack? Layered(LanguagePack? builtIn, LanguagePack? own)
    {
        if (builtIn is null || own is null) return own ?? builtIn;
        var layered = new LanguagePack { Code = own.Code, Name = own.Name, MadeFor = own.MadeFor, Texts = new(builtIn.Texts, StringComparer.Ordinal) };
        foreach ((string name, PackText text) in own.Texts) layered.Texts[name] = text;
        return layered;
    }

    /// <summary>"Remove": deletes a pack and its file.</summary>
    public void Delete(string code)
    {
        Packs.Remove(code);
        if (folder is not null) File.Delete(Path.Combine(folder, code + ".json"));
    }

    /// <summary>
    /// "Export": a pack file with every text of the app, ready to translate. For English it is a blank pack; for another
    /// language it holds that pack's translations, with the texts it lacks left blank.
    /// </summary>
    public static string Export(LanguagePack? pack, IReadOnlyDictionary<string, string> english, string appVersion)
    {
        var copy = new LanguagePack { Code = pack?.Code ?? "", Name = pack?.Name ?? "", MadeFor = appVersion, Texts = pack?.Texts ?? [] };
        return JsonFiles.ToText(ToJson(copy, english));
    }

    private void Keep(LanguagePack pack) => Packs[pack.Code] = pack;

    /// <summary>
    /// A pack as JSON. With <paramref name="english"/>, every name of the app is listed in its order, and texts the pack
    /// lacks have an empty "text"; without it, only the pack's own texts are listed.
    /// </summary>
    private static JsonObject ToJson(LanguagePack pack, IReadOnlyDictionary<string, string>? english)
    {
        var texts = new JsonObject();
        if (english is null)
        {
            foreach ((string name, PackText text) in pack.Texts) texts[name] = TextToJson(text);
        }
        else
        {
            foreach ((string name, string englishText) in english)
            {
                PackText text = pack.Texts.GetValueOrDefault(name) ?? new PackText("", englishText);
                // the English is always the app's current English, so a translator sees what to translate now
                texts[name] = TextToJson(text with { English = englishText });
            }
        }
        return new JsonObject { ["code"] = pack.Code, ["name"] = pack.Name, ["madeFor"] = pack.MadeFor, ["texts"] = texts };
    }

    private static JsonObject TextToJson(PackText text)
    {
        var json = new JsonObject { ["english"] = text.English, ["text"] = text.Text };
        if (text.Machine) json["machine"] = true;
        return json;
    }

    /// <summary>
    /// Reads a pack's JSON. Its texts are under "texts", by name; or, in a pack made for the HTML app, under "strings"
    /// (or under "strings" in each of its "sections") by their English, which <paramref name="english"/> turns into names.
    /// Texts left empty are left out, and so are an HTML app pack's texts that are the same as the English.
    /// </summary>
    private static LanguagePack Read(JsonObject json, IReadOnlyDictionary<string, string>? english)
    {
        bool byName = json["texts"] is JsonObject;
        bool byEnglish = json["strings"] is JsonObject || json["sections"] is JsonArray;
        if (!byName && !byEnglish) throw new ModFileException("It is not a language pack.");

        string code = NotCodeCharacters().Replace(JsonValues.AsText(json["code"]).ToLowerInvariant(), "");
        if (code.Length > 12) code = code[..12];
        if (code == "" || code == LanguagePack.EnglishCode)
            throw new ModFileException("Its \"code\" must be a short language code other than \"en\", such as \"fr\".");

        string name = JsonValues.IsTruthy(json["name"]) ? JsonValues.AsText(json["name"]) : code;
        var pack = new LanguagePack { Code = code, Name = name.Length > 40 ? name[..40] : name, MadeFor = JsonValues.AsText(json["madeFor"]) };
        if (byName) TakeTextsByName((JsonObject)json["texts"]!, pack.Texts);
        else if (english is not null) TakeTextsByEnglish(json, english, pack.Texts);

        if (pack.Texts.Count == 0) throw new ModFileException("None of its texts are translated yet.");
        return pack;
    }

    private static void TakeTextsByName(JsonObject texts, Dictionary<string, PackText> into)
    {
        foreach ((string name, JsonNode? entry) in texts)
        {
            if (entry is not JsonObject fields) continue;
            string text = JsonValues.AsText(fields["text"]), english = JsonValues.AsText(fields["english"]);
            // a translation may be the same as the English ("OK" in French): it still counts as translated
            if (text != "") into[name] = new PackText(text, english, JsonValues.IsTruthy(fields["machine"]));
        }
    }

    /// <summary>An HTML app pack: each English text it translates gives its translation to every name with that English.</summary>
    private static void TakeTextsByEnglish(JsonObject json, IReadOnlyDictionary<string, string> english, Dictionary<string, PackText> into)
    {
        var byEnglish = new Dictionary<string, string>(StringComparer.Ordinal);
        TakeStrings(json["strings"], byEnglish);
        if (json["sections"] is JsonArray sections)
            foreach (JsonNode? section in sections) TakeStrings((section as JsonObject)?["strings"], byEnglish);
        foreach ((string name, string englishText) in english)
            if (byEnglish.TryGetValue(englishText, out string? text)) into[name] = new PackText(text, englishText);
    }

    private static void TakeStrings(JsonNode? strings, Dictionary<string, string> into)
    {
        if (strings is not JsonObject texts) return;
        foreach ((string englishText, JsonNode? translation) in texts)
        {
            if (translation is JsonValue value && value.GetValueKind() == JsonValueKind.String)
            {
                string text = value.GetValue<string>();
                if (text != "" && text != englishText) into[englishText] = text;
            }
        }
    }

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex NotCodeCharacters();
}
