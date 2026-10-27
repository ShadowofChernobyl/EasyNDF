namespace EasyNdf.Data.Game;

/// <summary>
/// The fonts the game's interface draws with, read from its own archives (Assets/Fonts/*.ttf in Data\PC\...\*.dat) and
/// kept in the app's cache, one folder for each game version: the app ships none of them. The Armory draws a card's
/// number of units in the game's font for it, Liberator, as the game does (UISpecificUnitButtonView.ndf,
/// NbUnitInThePack, says so).
/// </summary>
public static class GameFonts
{
    /// <summary>The font the game draws a card's number of units in, by its path inside the archives.</summary>
    public const string UnitCountFont = "Assets/Fonts/Liberator-Medium.ttf";

    /// <summary>
    /// The game's font for a card's number of units, copied into the cache the first time; null when the game has none
    /// (Steel Division 2 draws it otherwise) or it cannot be read. Slow (seconds) the first time, as every archive is
    /// looked through: run it in the background.
    /// </summary>
    /// <param name="gameFolder">The game's folder.</param>
    /// <param name="cacheRoot">The app's cache folder.</param>
    /// <param name="game">The game.</param>
    /// <param name="version">The game's version, as <see cref="GameArchives.Version"/> gives it.</param>
    public static string? UnitCountFontFile(string gameFolder, string cacheRoot, GameTitle game, string version)
    {
        string all = Path.Combine(cacheRoot, "Game fonts");
        string folder = Path.Combine(all, game.Id + " " + (version == "" ? "0" : version));
        string copy = Path.Combine(folder, Path.GetFileName(UnitCountFont));
        // a game looked through before without finding the font is not looked through again until it is updated
        string noneFound = Path.Combine(folder, "none.txt");
        if (File.Exists(copy)) return copy;
        if (File.Exists(noneFound)) return null;
        try
        {
            ArchiveFile? font = Newest(gameFolder, UnitCountFont);
            Directory.CreateDirectory(folder);
            DeleteOlderVersions(all, game, folder);
            if (font is null)
            {
                File.WriteAllText(noneFound, UnitCountFont + " is not in the game's archives.");
                return null;
            }
            // written beside it first, then named: a copy cut short would otherwise be found and used from then on
            File.WriteAllBytes(copy + ".part", font.Read());
            File.Move(copy + ".part", copy, overwrite: true);
            return copy;
        }
        catch (Exception problem) when (problem is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A file in the game's archives, from the newest archive that has it; null when none has it.</summary>
    private static ArchiveFile? Newest(string gameFolder, string path)
    {
        string dataFolder = Path.Combine(gameFolder, "Data", "PC");
        if (!Directory.Exists(dataFolder)) return null;
        // every archive, not only the ZZ_ ones the rest of the app reads: the fonts are in Data.dat
        List<string> archives = [.. Directory.EnumerateFiles(dataFolder, "*.dat", SearchOption.AllDirectories)
            .OrderBy(GameArchives.VersionOf, StringComparer.Ordinal)
            .ThenBy(archive => archive, StringComparer.Ordinal)];
        ArchiveFile? found = null;
        foreach (string archive in archives)
        {
            try
            {
                found = EdatArchive.FindFile(archive, path) ?? found;
            }
            catch (Exception problem) when (problem is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // not an archive of the kind the app reads (or one too short, or in use): the others may still have it
            }
        }
        return found;
    }

    /// <summary>Takes away the fonts kept for the game's older versions.</summary>
    private static void DeleteOlderVersions(string all, GameTitle game, string current)
    {
        foreach (string folder in Directory.EnumerateDirectories(all, game.Id + " *"))
            if (!string.Equals(folder, current, StringComparison.OrdinalIgnoreCase)) Directory.Delete(folder, recursive: true);
    }
}
