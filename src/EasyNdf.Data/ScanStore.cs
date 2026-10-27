using System.Text.Json;

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

    /// <summary>
    /// Reads the scans kept in a file, if it exists. It is read as a stream, each name kept once for all files: a whole
    /// game's scans name the same properties thousands of times. A file that cannot be read leaves no scans.
    /// </summary>
    public ScanStore(string scansFile)
    {
        path = scansFile;
        try
        {
            if (File.Exists(path)) Read(File.ReadAllBytes(path));
        }
        catch (Exception problem) when (problem is JsonException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Scans.Clear();
        }
    }

    /// <summary>The scans, by file.</summary>
    public Dictionary<string, ScanInfo> Scans { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Writes the scans to their file, straight to it rather than building the whole file in memory first: in the
    /// same form as before, {"file": {"names": [...], "titles": [...], "types": [...]}}.
    /// </summary>
    public void Save() => Write(Copy());

    /// <summary>The scans as they are now, to be written while they go on changing (see <see cref="Write"/>).</summary>
    public KeyValuePair<string, ScanInfo>[] Copy() => [.. Scans];

    /// <summary>Writes scans taken with <see cref="Copy"/> to their file; it may run on any thread.</summary>
    public void Write(IReadOnlyList<KeyValuePair<string, ScanInfo>> scans)
    {
        if (path is null) return;
        JsonFiles.WriteStreamed(path, writer =>
        {
            writer.WriteStartObject();
            foreach ((string key, ScanInfo scan) in scans)
            {
                writer.WriteStartObject(key);
                WriteList(writer, "names", scan.Names);
                WriteList(writer, "titles", scan.Titles);
                WriteList(writer, "types", scan.Types);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        });
    }

    private static void WriteList(Utf8JsonWriter writer, string name, IReadOnlyList<string> texts)
    {
        writer.WriteStartArray(name);
        foreach (string text in texts) writer.WriteStringValue(text);
        writer.WriteEndArray();
    }

    /// <summary>Reads the scans file's object of files, each an object of lists of names; anything else in it is passed over.</summary>
    private void Read(byte[] json)
    {
        ReadOnlySpan<byte> text = json;
        // the byte order mark a hand-saved file may start with
        if (text.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) text = text[3..];
        var reader = new Utf8JsonReader(text, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string file = reader.GetString()!;
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }
            List<string> names = [], titles = [], types = [];
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string list = reader.GetString()!;
                reader.Read();
                List<string>? into = list switch { "names" => names, "titles" => titles, "types" => types, _ => null };
                if (into is null || reader.TokenType != JsonTokenType.StartArray) reader.Skip();
                else ReadList(ref reader, into);
            }
            Scans[file] = new ScanInfo(names, titles, types);
        }
    }

    /// <summary>A list of names, each kept once for all files; anything in it that is not a text is passed over.</summary>
    private static void ReadList(ref Utf8JsonReader reader, List<string> into)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String) into.Add(string.Intern(reader.GetString()!));
            else reader.Skip();
        }
    }
}
