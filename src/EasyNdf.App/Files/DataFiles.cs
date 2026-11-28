using System.IO;

namespace EasyNdf.App.Files;

/// <summary>
/// The data files Table and Variable rules read ("Read from a file"), read again at every run so another tool can
/// make them. A path is the file's full path, or a path inside the Mods folder ("My mod\suppression.csv"), which goes
/// with the mod to another computer. Only text tables are read: .csv, .tsv and .txt files, up to 50 MB.
/// </summary>
public static class DataFiles
{
    /// <summary>The kinds of file a rule may read.</summary>
    public static readonly string[] Extensions = [".csv", ".tsv", ".txt"];

    private const long MostBytes = 50L * 1024 * 1024;

    /// <summary>The file's text; null when the path is not one a rule may read, or the file cannot be read.</summary>
    public static string? Read(string path)
    {
        if (FullPathOf(path) is not string full) return null;
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists || info.Length > MostBytes) return null;
            return File.ReadAllText(full);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Where a rule's path points: the path itself when it is a full one, else inside the Mods folder; null for a kind of file not read.</summary>
    public static string? FullPathOf(string path)
    {
        path = path.Trim().Trim('"');
        if (path == "" || !Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) return null;
        try
        {
            if (Path.IsPathFullyQualified(path)) return Path.GetFullPath(path);
            return AppData.Current.Folders?.Mods is string mods ? Path.GetFullPath(Path.Combine(mods, path)) : null;
        }
        catch (Exception problem) when (problem is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// The path to keep in a rule for a file chosen: inside the Mods folder, the part below it, so the path goes with the
    /// mod to another computer; anywhere else, the full path.
    /// </summary>
    public static string PathToKeep(string chosen)
    {
        if (AppData.Current.Folders?.Mods is string mods)
        {
            string relative = Path.GetRelativePath(mods, chosen);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative)) return relative;
        }
        return chosen;
    }
}
