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
    public static string ToText(JsonNode? value) => value is null ? "null" : TextOf(value, Options);

    /// <summary>
    /// Tuned for memory: a JSON value's text as UTF-8, as <see cref="JsonNode.ToJsonString"/> writes it, but in memory of
    /// its own. ToJsonString borrows .NET's shared buffers and hands them back for reuse, where they stay: writing out
    /// WARNO ACTUAL's rules left some 33 MB of them held for good (Paul: "Other .NET data" should stay small). Keep it
    /// that way when tidying.
    /// </summary>
    public static byte[] Utf8Of(JsonNode value, JsonSerializerOptions? options = null)
    {
        var written = new System.Buffers.ArrayBufferWriter<byte>();
        var writerOptions = new JsonWriterOptions
        {
            Encoder = options?.Encoder,
            Indented = options?.WriteIndented ?? false,
            IndentSize = options?.IndentSize ?? 2,
            // as deep as ToJsonString writes: 64 unless the options say otherwise (a writer's own default is 1,000)
            MaxDepth = options?.MaxDepth is int depth and > 0 ? depth : 64,
        };
        using (var writer = new Utf8JsonWriter(written, writerOptions))
            value.WriteTo(writer, options);
        return written.WrittenSpan.ToArray();
    }

    /// <summary>A JSON value's text, as <see cref="JsonNode.ToJsonString"/> writes it, without .NET's shared buffers (see <see cref="Utf8Of"/>).</summary>
    public static string TextOf(JsonNode value, JsonSerializerOptions? options = null) => System.Text.Encoding.UTF8.GetString(Utf8Of(value, options));

    /// <summary>
    /// JSON text made by <see cref="ToText"/> as it is when read back and made again: the same text, unless it holds half
    /// of a pair of UTF-16 characters (a broken emoji, say), which is made as the escape of the replacement character
    /// (<see cref="ReplacementEscape"/>), read back as that character itself, and then made as the character. Only such
    /// text is read back and made again.
    /// </summary>
    public static string AsMadeAgain(string text) =>
        text.Contains(ReplacementEscape, StringComparison.OrdinalIgnoreCase) ? ToText(Parse(text)) : text;

    /// <summary>How <see cref="ToText"/> writes half of a pair of UTF-16 characters: the escape of the replacement character.</summary>
    private const string ReplacementEscape = "\\uFFFD";

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
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Writes a file safely: the text goes to a temporary file first, which then replaces the old one.
    /// If the app or the PC stops halfway, the old file is still whole. The new text is on the disk before it replaces
    /// the old file: otherwise a power cut just after could leave the new name on an empty file.
    /// </summary>
    public static void Write(string path, JsonNode? value) => WriteText(path, ToText(value));

    /// <summary>
    /// Writes JSON text the app has already made with <see cref="ToText"/>, safely, as <see cref="Write"/> does.
    /// </summary>
    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".saving";
        using (FileStream file = File.Create(temporary))
        {
            file.Write(System.Text.Encoding.UTF8.GetBytes(text));
            file.Flush(flushToDisk: true);
        }
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
        {
            using (var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true, IndentSize = Options.IndentSize, Encoder = Options.Encoder }))
                write(writer);
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
