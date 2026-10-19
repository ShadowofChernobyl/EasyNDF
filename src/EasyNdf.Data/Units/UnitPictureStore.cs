using EasyNdf.Data.Game;

namespace EasyNdf.Data.Units;

/// <summary>
/// The Units tab's pictures (units and flags) as .png files, read out of a game's archives once and kept in the app's
/// cache folder, in a folder for the game's version: "Cache\Unit pictures\WARNO 175910\Assets\2D\...". A game update
/// changes the version, so its pictures are read again, and the older version's folder is deleted. A mod's own
/// picture (a .png in its GameData) is used where it is, without a copy.
/// </summary>
public sealed class UnitPictureStore
{
    private readonly string folder;
    private readonly GamePictures? pictures;
    private readonly string? modFolder;
    private readonly HashSet<string> missing = new(StringComparer.OrdinalIgnoreCase);
    private readonly object busy = new();

    /// <summary>A store for one game (and mod): its pictures go in <paramref name="cacheRoot"/>\Unit pictures\[game] [version].</summary>
    /// <param name="cacheRoot">The app's cache folder.</param>
    /// <param name="game">The game.</param>
    /// <param name="version">The game's version (see <see cref="GameArchives.Version"/>).</param>
    /// <param name="pictures">The game's pictures; null when its archives cannot be read, so only saved pictures and the mod's own are found.</param>
    /// <param name="modFolder">The mod's folder, for a mod.</param>
    public UnitPictureStore(string cacheRoot, GameTitle game, string version, GamePictures? pictures, string? modFolder)
    {
        string all = Path.Combine(cacheRoot, "Unit pictures");
        // a game without version folders (its archives straight in Data\PC) is version 0
        folder = Path.Combine(all, game.Id + " " + (version == "" ? "0" : version));
        this.pictures = pictures;
        this.modFolder = modFolder;
        DeleteOlderVersions(all, game, folder);
    }

    /// <summary>
    /// How many pictures have been taken out of the game's archives so far. Each one decodes part of a large texture,
    /// so after a batch of them the app gives the memory that took back.
    /// </summary>
    public int PicturesTakenOut { get; private set; }

    /// <summary>
    /// The .png file of a "GameData:/....png" picture: the mod's own, an already saved one, or one read out of the
    /// game's archives now and saved. Null when the picture cannot be found or read. Called from one task at a time is
    /// best, as reading a picture can be slow; it is safe from several.
    /// </summary>
    public string? FileFor(string source)
    {
        string relative = GamePictures.Normalised(source)["GameData:/".Length..];
        string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Contains("..")) return null;

        if (modFolder is not null)
        {
            string own = Path.Combine([modFolder, "GameData", .. parts]);
            if (File.Exists(own)) return own;
        }
        string saved = Path.ChangeExtension(Path.Combine([folder, .. parts]), ".png");
        if (File.Exists(saved)) return saved;

        lock (busy)
        {
            if (File.Exists(saved)) return saved;
            if (pictures is null || missing.Contains(relative)) return null;
            Picture? picture;
            try
            {
                picture = pictures.Read(source);
            }
            catch (Exception problem) when (IsUnreadable(problem))
            {
                picture = null;
            }
            if (picture is null)
            {
                missing.Add(relative);
                return null;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                // written beside it first, so a picture cut short (the app closed while writing) is never taken for a whole one
                string writing = saved + ".part";
                File.WriteAllBytes(writing, PngFile.Write(picture));
                File.Move(writing, saved, overwrite: true);
                PicturesTakenOut++;
                return saved;
            }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
            {
                missing.Add(relative);
                return null;
            }
        }
    }

    /// <summary>The errors a texture in a form not expected can give while it is read.</summary>
    private static bool IsUnreadable(Exception problem) =>
        problem is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException
            or IndexOutOfRangeException or EndOfStreamException or OverflowException or ZstdSharp.ZstdException;

    /// <summary>
    /// Deletes the pictures saved for other versions of the same game: folders named "[game] [version]" beside this
    /// one. Nothing else in the cache is touched.
    /// </summary>
    private static void DeleteOlderVersions(string all, GameTitle game, string current)
    {
        if (!Directory.Exists(all)) return;
        foreach (string other in Directory.EnumerateDirectories(all, game.Id + " *"))
        {
            string version = Path.GetFileName(other)[(game.Id.Length + 1)..];
            if (string.Equals(other, current, StringComparison.OrdinalIgnoreCase) || version == "" || !version.All(char.IsAsciiDigit)) continue;
            try { Directory.Delete(other, recursive: true); }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
        }
    }
}
