using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyNdf.Data;

/// <summary>Reading and writing the app's JSON files.</summary>
public static class JsonFiles
{
    /// <summary>
    /// Written like the HTML app's <c>JSON.stringify(value, null, 2)</c>: two-space indents and letters such as é or 中 as they
    /// are, not as \u escapes, so the files look the same from both apps.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The text of a JSON value, as the app writes it.</summary>
    public static string ToText(JsonNode? value) => value is null ? "null" : value.ToJsonString(Options);

    /// <summary>
    /// Parses JSON text. A byte order mark at the start is ignored, as the HTML app ignores it.
    /// Throws <see cref="JsonException"/> when the text is not JSON.
    /// </summary>
    public static JsonNode? Parse(string text) => JsonNode.Parse(text.TrimStart('﻿'));

    /// <summary>Reads and parses a file, or returns null when it is missing or is not JSON.</summary>
    public static JsonNode? TryRead(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    /// <summary>
    /// Writes a file safely: the text goes to a temporary file first, which then replaces the old one.
    /// If the app or the PC stops halfway, the old file is still whole.
    /// </summary>
    public static void Write(string path, JsonNode? value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".saving";
        File.WriteAllText(temporary, ToText(value));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Writes a large file straight from what <paramref name="write"/> writes, as <see cref="Write"/> writes one (indented,
    /// through a temporary file so a cut-off save leaves the old file whole), without building it in memory first.
    /// </summary>
    public static void WriteStreamed(string path, Action<Utf8JsonWriter> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".saving";
        using (FileStream file = File.Create(temporary))
        using (var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true, IndentSize = Options.IndentSize, Encoder = Options.Encoder }))
            write(writer);
        File.Move(temporary, path, overwrite: true);
    }
}
