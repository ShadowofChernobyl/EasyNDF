using System.Text.Json.Nodes;

namespace EasyNdf.Data;

/// <summary>What scanning a file found: the names that fill the rule window's dropdowns.</summary>
/// <param name="Names">Every property name and top-level constant, sorted.</param>
/// <param name="Titles">The descriptors' names, in file order.</param>
/// <param name="Types">Every object type, sorted.</param>
public sealed record ScanInfo(IReadOnlyList<string> Names, IReadOnlyList<string> Titles, IReadOnlyList<string> Types)
{
    /// <summary>Nothing scanned.</summary>
    public static readonly ScanInfo Empty = new([], [], []);
}

/// <summary>
/// The property scans (the HTML app's <c>scans</c>): for each scanned file, by its folder and name ("Gameplay/Gfx/Ammunition.ndf",
/// or just the name for a file scanned on its own), what the file holds. They are kept between sessions, in one file, so
/// the rule window's dropdowns are filled before the game's files are added again.
/// </summary>
public sealed class ScanStore
{
    private readonly string? path;

    /// <summary>Scans kept in memory only, never saved: for drawing the app without touching saved data.</summary>
    public ScanStore() { }

    /// <summary>Reads the scans kept in a file, if it exists.</summary>
    public ScanStore(string scansFile)
    {
        path = scansFile;
        if (JsonFiles.TryRead(path) is not JsonObject json) return;
        foreach ((string key, JsonNode? value) in json)
            if (value is JsonObject scan) Scans[key] = new ScanInfo(Texts(scan["names"]), Texts(scan["titles"]), Texts(scan["types"]));
    }

    /// <summary>The scans, by file.</summary>
    public Dictionary<string, ScanInfo> Scans { get; } = new(StringComparer.Ordinal);

    /// <summary>Writes the scans to their file.</summary>
    public void Save()
    {
        if (path is null) return;
        var json = new JsonObject();
        foreach ((string key, ScanInfo scan) in Scans)
            json[key] = new JsonObject
            {
                ["names"] = new JsonArray([.. scan.Names.Select(n => JsonValue.Create(n))]),
                ["titles"] = new JsonArray([.. scan.Titles.Select(n => JsonValue.Create(n))]),
                ["types"] = new JsonArray([.. scan.Types.Select(n => JsonValue.Create(n))]),
            };
        JsonFiles.Write(path, json);
    }

    private static List<string> Texts(JsonNode? list) => list is JsonArray items ? [.. items.Select(JsonValues.AsText)] : [];
}
