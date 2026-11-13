using System.IO;
using System.IO.Compression;
using EasyNdf.Engine.Editing;

namespace EasyNdf.App.Editor;

/// <summary>
/// The File Editor's guide to WARNO's names (what each type's names take: value types, descriptions, choices and the
/// limits that stop mod generation), read from the data the app carries the first time it is needed, away from the
/// screen's thread. Steel Division 2's files get no guide: their names and values differ.
/// </summary>
public static class EditorGuide
{
    private static readonly Lazy<Task<ValueGuide>> reading = new(() => Task.Run(Read));

    /// <summary>The guide, once read; null until then (the editor shows it as soon as it is).</summary>
    public static ValueGuide? IfRead => reading.IsValueCreated && reading.Value.IsCompletedSuccessfully ? reading.Value.Result : null;

    /// <summary>The guide, read the first time it is asked for.</summary>
    public static Task<ValueGuide> Get() => reading.Value;

    /// <summary>Reads the guide from the app's data.</summary>
    private static ValueGuide Read()
    {
        using Stream packed = typeof(EditorGuide).Assembly.GetManifestResourceStream("EditorGuide.json.gz")
            ?? throw new InvalidOperationException("The editor's guide data is missing from the app.");
        using var json = new GZipStream(packed, CompressionMode.Decompress);
        return ValueGuide.Read(json);
    }
}
