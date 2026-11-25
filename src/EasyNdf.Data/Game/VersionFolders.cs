namespace EasyNdf.Data.Game;

/// <summary>
/// The cache's folders kept for each version of a game ("[game] [version]", such as the unit pictures and the game's
/// fonts), and when an older version's folder may go. One still in use must stay: a font file deleted under an app that
/// shows it breaks the app's whole window until it is restarted (menus at the screen's corner, panels and pictures
/// missing), and pictures deleted under it leave blank cards. It can be in use by another source of the same game at
/// another version, or by another copy of the app running at the same time, so a folder goes only once no app has used
/// it for <see cref="KeptUnused"/>: each use marks it.
/// </summary>
public static class VersionFolders
{
    /// <summary>How long a version's folder is kept after it was last used.</summary>
    public static readonly TimeSpan KeptUnused = TimeSpan.FromDays(14);

    private const string UsedMark = "used.txt";

    /// <summary>Marks a version's folder as used now, making it if need be.</summary>
    public static void MarkUsed(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string mark = Path.Combine(folder, UsedMark);
            if (!File.Exists(mark)) File.WriteAllText(mark, "When an app last used this folder; folders unused for a while are deleted.");
            File.SetLastWriteTimeUtc(mark, DateTime.UtcNow);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>When a version's folder was last used: its mark, or the folder's own time for one made before marks.</summary>
    public static DateTime LastUsed(string folder)
    {
        string mark = Path.Combine(folder, UsedMark);
        return File.Exists(mark) ? File.GetLastWriteTimeUtc(mark) : Directory.GetLastWriteTimeUtc(folder);
    }

    /// <summary>
    /// Deletes the folders of the game's other versions beside <paramref name="current"/> that no app has used for
    /// <see cref="KeptUnused"/>. Only "[game] [digits]" folders are touched.
    /// </summary>
    public static void DeleteUnused(string all, GameTitle game, string current, DateTime? now = null)
    {
        if (!Directory.Exists(all)) return;
        DateTime time = now ?? DateTime.UtcNow;
        foreach (string other in Directory.EnumerateDirectories(all, game.Id + " *"))
        {
            string version = Path.GetFileName(other)[(game.Id.Length + 1)..];
            if (string.Equals(Path.GetFullPath(other), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase)
                || version == "" || !version.All(char.IsAsciiDigit)) continue;
            try
            {
                if (time - LastUsed(other) < KeptUnused) continue;
                Directory.Delete(other, recursive: true);
            }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
        }
    }
}
