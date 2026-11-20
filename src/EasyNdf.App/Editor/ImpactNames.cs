using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Effects;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Editor;

/// <summary>
/// The impact names an ammunition's ImpactHappening can take, for the value check (the names MimeticImpactMapping.ndf
/// maps, <see cref="FxImpacts.NamesIn"/>): the mod's own copy of the file, as the game loads a mod's, or else the
/// game's. Each file is read once, and again only when it changes.
/// </summary>
public static class ImpactNames
{
    /// <summary>The names read from each file, by its path, with when it was written.</summary>
    private static readonly Dictionary<string, (DateTime Written, IReadOnlySet<string> Names)> read = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The impact names for a file: those of the MimeticImpactMapping.ndf in the first of <paramref name="files"/>' GameData
    /// folders that has one (where an export writes it first, then where it is read from), or else those of the game's
    /// own (its Mods\ModData\base.zip). Null when none is found, or for Steel Division 2, which is not checked: the file
    /// then goes unchecked.
    /// </summary>
    /// <param name="game">The file's game.</param>
    /// <param name="files">Where the file is, or will be written; null or archive places are passed over.</param>
    public static IReadOnlySet<string>? For(GameTitle game, params string?[] files)
    {
        if (game != GameTitle.Warno) return null;
        try
        {
            foreach (string? file in files)
                if (file is not null && DivisionExtras.GameDataFolderOf(file) is string gameData
                    && Path.Combine([gameData, .. FxImpacts.ImpactsFile.Split('/')]) is string own && File.Exists(own))
                    return Read(own, () => File.ReadAllText(own));
            if (GameFolder(files) is not string gameFolder || GameInstall.BaseZipOf(gameFolder) is not string baseZip || !File.Exists(baseZip)) return null;
            return Read(baseZip, () =>
            {
                using var gameFiles = new GameDataFiles(baseZip);
                return gameFiles.Read(FxImpacts.ImpactsFile);
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A file's names, read again only when it was written since; null when it has none.</summary>
    private static IReadOnlySet<string>? Read(string path, Func<string?> text)
    {
        DateTime written = File.GetLastWriteTimeUtc(path);
        lock (read)
            if (read.TryGetValue(path, out (DateTime Written, IReadOnlySet<string> Names) kept) && kept.Written == written)
                return kept.Names.Count > 0 ? kept.Names : null;
        IReadOnlySet<string> names = text() is string mapping ? FxImpacts.NamesIn(mapping) : new HashSet<string>();
        lock (read) read[path] = (written, names);
        return names.Count > 0 ? names : null;
    }

    /// <summary>WARNO's folder in the Steam libraries, looked for once.</summary>
    private static readonly Lazy<string?> steamGame = new(() => GameInstall.Find(GameTitle.Warno));

    /// <summary>
    /// WARNO's folder: the one a file is in (a mod's GameData sits in the game's Mods folder), else the one Steam has,
    /// else one chosen in the Armory's "Choose a Game Folder…".
    /// </summary>
    internal static string? GameFolder(string?[] files)
    {
        foreach (string? file in files)
            if (file is not null && DivisionExtras.GameDataFolderOf(file) is string gameData
                && Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(gameData))) is string above && GameInstall.IsGameFolder(above))
                return above;
        if (steamGame.Value is string found) return found;
        IEnumerable<string> chosen = AppData.Current.State["unitGameFolders"] is JsonArray folders ? folders.Select(folder => (string?)folder).OfType<string>() : [];
        return chosen.FirstOrDefault(folder => GameInstall.IsGameFolder(folder) && GameTitle.OfFolder(folder) == GameTitle.Warno);
    }
}
