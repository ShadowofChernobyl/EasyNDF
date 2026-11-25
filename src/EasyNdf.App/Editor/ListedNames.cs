using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using EasyNdf.App.Files;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Editor;

/// <summary>
/// The names every file of the Files tab declares, read or not: each top-level name, its type ("" for a constant) and
/// its line. The File Editor's tips, Go to Descriptor and the Descriptor Tree look here for a name the files read don't
/// declare (a game lists some 4,900 files and reads only those opened, run or scanned). It is made away from the
/// screen's thread the first time it is needed, a file at a time; a file's part is kept while the file stays as it was.
/// </summary>
public static partial class ListedNames
{
    /// <summary>Where a name is declared: its file, its type ("" for a constant) and its line, from 1.</summary>
    public readonly record struct Place(FileEntry Entry, string Type, int Line);

    /// <summary>A file's declarations, kept with what the file was when they were read.</summary>
    private sealed record FilePart(string? Source, object? Archived, Dictionary<string, (string Type, int Line)> Names);

    private static readonly Dictionary<FileEntry, FilePart> parts = [];
    private static Dictionary<string, Place>? merged;
    private static List<(FileEntry Entry, string? Source, object? Archived)>? mergedFrom;
    private static Task? building;

    /// <summary>How far the making has got, for "Looking in 1,200 of 4,930 listed files".</summary>
    public static (int Done, int Total) Progress { get; private set; }

    /// <summary>Whether the names are being gathered now.</summary>
    public static bool IsBuilding => building is { IsCompleted: false };

    /// <summary>
    /// Where a listed file declares a name: the first in the Files tab's order. Null when none does, or when the names are
    /// not gathered yet for these files (then the gathering starts; <paramref name="ready"/> says which).
    /// </summary>
    public static Place? Find(IReadOnlyList<FileEntry> entries, string name, out bool ready)
    {
        ready = IsCurrent(entries);
        if (!ready)
        {
            Start(entries);
            return null;
        }
        return merged!.TryGetValue(name, out Place place) ? place : null;
    }

    /// <summary>Where a listed file declares a name, by the names last gathered, whatever the files are now; null for none.</summary>
    public static Place? FindGathered(string name) => merged is { } names && names.TryGetValue(name, out Place place) ? place : null;

    /// <summary>Whether a name is declared by a listed file, when the names are gathered; false otherwise.</summary>
    public static bool Declares(IReadOnlyList<FileEntry> entries, string name) => Find(entries, name, out _) is not null;

    /// <summary>Starts gathering the names of these files, unless they are gathered or being gathered.</summary>
    public static void Start(IReadOnlyList<FileEntry> entries)
    {
        if (IsBuilding || IsCurrent(entries)) return;
        List<(FileEntry Entry, string? Source, object? Archived)> now = Snapshot(entries);
        building = Task.Run(() => Build(now));
    }

    private static List<(FileEntry, string?, object?)> Snapshot(IReadOnlyList<FileEntry> entries) =>
        [.. entries.Where(entry => entry.Source is not null)
            .OrderBy(FileListModel.ScanKey, StringComparer.Ordinal)
            .Select(entry => (entry, entry.Source, (object?)entry.ArchivedContent))];

    private static bool IsCurrent(IReadOnlyList<FileEntry> entries)
    {
        if (merged is null || mergedFrom is null) return false;
        List<(FileEntry Entry, string? Source, object? Archived)> now = Snapshot(entries);
        return now.Count == mergedFrom.Count && now.Zip(mergedFrom).All(pair =>
            ReferenceEquals(pair.First.Entry, pair.Second.Entry) && pair.First.Source == pair.Second.Source && ReferenceEquals(pair.First.Archived, pair.Second.Archived));
    }

    /// <summary>
    /// Reads each file not read before (or changed since) for its declarations, then puts them together. Tuned for speed:
    /// a plain scan of the text, no parse, and each file's part kept, so a file added to the list is the only one read.
    /// </summary>
    private static void Build(List<(FileEntry Entry, string? Source, object? Archived)> files)
    {
        var clock = Stopwatch.StartNew();
        Progress = (0, files.Count);
        for (int n = 0; n < files.Count; n++)
        {
            (FileEntry entry, string? source, object? archived) = files[n];
            lock (parts)
                if (parts.TryGetValue(entry, out FilePart? kept) && kept.Source == source && ReferenceEquals(kept.Archived, archived))
                {
                    Progress = (n + 1, files.Count);
                    continue;
                }
            Dictionary<string, (string, int)> names;
            try { names = DeclarationsIn(FileContent.Text(entry)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { names = []; }
            lock (parts) parts[entry] = new FilePart(source, archived, names);
            Progress = (n + 1, files.Count);
        }
        var all = new Dictionary<string, Place>(StringComparer.Ordinal);
        lock (parts)
        {
            foreach ((FileEntry entry, _, _) in files)
                if (parts.TryGetValue(entry, out FilePart? part))
                    foreach ((string name, (string type, int line)) in part.Names) all.TryAdd(name, new Place(entry, type, line));
            // files no longer listed let go of
            var listed = files.Select(file => file.Entry).ToHashSet();
            foreach (FileEntry gone in parts.Keys.Where(entry => !listed.Contains(entry)).ToList()) parts.Remove(gone);
        }
        merged = all;
        mergedFrom = files;
        SessionLog.Write("Editor", $"Listed names: {all.Count} names in {files.Count} files, {clock.ElapsedMilliseconds} ms");
    }

    /// <summary>The top-level names a text declares, each with its type ("" for a constant) and line.</summary>
    public static Dictionary<string, (string Type, int Line)> DeclarationsIn(string text)
    {
        var names = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        int line = 1, counted = 0;
        foreach (Match declaration in Declaration().Matches(text))
        {
            for (int at = text.IndexOf('\n', counted); at >= 0 && at < declaration.Index; at = text.IndexOf('\n', at + 1)) line++;
            counted = declaration.Index;
            string type = declaration.Groups["type"].Value;
            names.TryAdd(declaration.Groups["name"].Value, (type.Length > 1 && type[0] == 'T' && char.IsAsciiLetterUpper(type[1]) ? type : "", line));
        }
        return names;
    }

    /// <summary>A declaration at the start of a line: "Name is TType", "export Name is 92", "template Name".</summary>
    [GeneratedRegex(@"(?m)^(?:(?:export|private|public)[ \t]+)?(?:(?<name>[A-Za-z_]\w*)[ \t]+is\b[ \t]*(?<type>[A-Za-z_]\w*)?|template[ \t]+(?<name>[A-Za-z_]\w*))")]
    private static partial Regex Declaration();
}
