using System.IO;
using EasyNdf.Data;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;
using EasyNdf.Engine.Editing;

namespace EasyNdf.App.Editor;

/// <summary>
/// The classes WARNO's own files use (every T name before an object's bracket, in every .ndf of its base.zip), for
/// Export's check of a class the game doesn't have, which stops mod generation ("Unknown class"). Read once for each
/// version of base.zip, and kept in the app's Cache folder until the game updates it, as reading it takes a while.
/// </summary>
public static class GameClasses
{
    private static readonly object gate = new();
    private static (string Key, IReadOnlySet<string> Classes)? kept;

    /// <summary>The classes for a WARNO file; null for Steel Division 2, or when the game's base.zip is not found.</summary>
    /// <param name="game">The file's game.</param>
    /// <param name="files">Where the file is, or will be written, to find the game's folder by.</param>
    public static IReadOnlySet<string>? For(GameTitle game, params string?[] files)
    {
        if (game != GameTitle.Warno) return null;
        try
        {
            if (ImpactNames.GameFolder(files) is not string gameFolder || GameInstall.BaseZipOf(gameFolder) is not string baseZip || !File.Exists(baseZip)) return null;
            var info = new FileInfo(baseZip);
            string key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            lock (gate)
            {
                if (kept is { } now && now.Key == key) return now.Classes;
                IReadOnlySet<string> classes = FromCache(key) ?? Read(baseZip, key);
                kept = (key, classes);
                return classes;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? CacheFile => AppData.Current.Folders is { } folders ? Path.Combine(folders.Cache, "Game classes.txt") : null;

    /// <summary>The classes kept from an earlier reading of the same base.zip; null when there is none.</summary>
    private static IReadOnlySet<string>? FromCache(string key)
    {
        if (CacheFile is not string file || !File.Exists(file)) return null;
        string[] lines = File.ReadAllLines(file);
        return lines.Length > 1 && lines[0] == key ? new HashSet<string>(lines.Skip(1), StringComparer.Ordinal) : null;
    }

    /// <summary>Reads every .ndf of base.zip for its classes, and keeps them in the Cache folder.</summary>
    private static IReadOnlySet<string> Read(string baseZip, string key)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        using (var files = new GameDataFiles(baseZip))
            foreach (string path in files.PathsWhere(path => path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)).ToList())
                if (files.ReadPath(path) is string text) ExportValueCheck.AddClassesOf(text, classes);
        if (classes.Count > 0 && CacheFile is string file)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllLines(file, [key, .. classes.Order(StringComparer.Ordinal)]);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return classes;
    }
}
