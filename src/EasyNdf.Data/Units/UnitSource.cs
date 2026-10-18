using EasyNdf.Data.Game;

namespace EasyNdf.Data.Units;

/// <summary>Where the Units tab reads units from: a game, or a mod in its Mods folder.</summary>
/// <param name="Game">The game.</param>
/// <param name="GameFolder">The game's folder.</param>
/// <param name="Mod">The mod, for a mod; null for the game itself.</param>
public sealed record UnitSource(GameTitle Game, string GameFolder, GameMod? Mod)
{
    /// <summary>What the source list shows: the game's name, or the mod's.</summary>
    public string Name => Mod?.Name ?? Game.Name;

    /// <summary>The folder it is in: the mod's, or the game's.</summary>
    public string Folder => Mod?.Folder ?? GameFolder;

    /// <summary>A key that stays the same between sessions, to remember the source last shown.</summary>
    public string Key => Game.Id + "|" + Folder;

    /// <summary>
    /// Every source on this computer: each game found in the Steam libraries or in <paramref name="chosenFolders"/>
    /// (game folders the user chose), followed by the mods in its Mods folder. Each game is listed once.
    /// </summary>
    public static List<UnitSource> FindAll(IEnumerable<string> chosenFolders, IEnumerable<string>? steamFolders = null)
    {
        var games = new List<(GameTitle Game, string Folder)>();
        foreach (GameTitle game in GameTitle.All)
            if (GameInstall.Find(game, steamFolders) is string found) games.Add((game, found));
        foreach (string chosen in chosenFolders)
            if (GameInstall.IsGameFolder(chosen) && !games.Any(g => SameFolder(g.Folder, chosen)))
                games.Add((GameTitle.OfFolder(chosen), chosen));

        var sources = new List<UnitSource>();
        foreach (var (game, folder) in games)
        {
            sources.Add(new UnitSource(game, folder, null));
            foreach (GameMod mod in GameInstall.ModsOf(folder)) sources.Add(new UnitSource(game, folder, mod));
        }
        return sources;
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>A source's units, ready to show: the units, and where their pictures come from.</summary>
/// <param name="Source">Where they were read from.</param>
/// <param name="Catalog">The units.</param>
/// <param name="Pictures">Their pictures (and the nations' flags) as .png files.</param>
/// <param name="Problems">What could not be read: archives, dictionaries, files.</param>
public sealed record LoadedUnits(UnitSource Source, UnitCatalog Catalog, UnitPictureStore Pictures, IReadOnlyList<string> Problems);

/// <summary>Reads a source's units, with the game's texts and pictures. Slow (seconds): run it in the background.</summary>
public static class UnitLibrary
{
    /// <summary>
    /// Reads a source's units. <paramref name="archives"/> are the game's archives when already open (a game and its
    /// mods share them); otherwise they are opened here. Throws <see cref="IOException"/> or
    /// <see cref="InvalidDataException"/> when the game's base.zip cannot be read.
    /// </summary>
    public static LoadedUnits Load(UnitSource source, string cacheRoot, GameArchives? archives = null)
    {
        var problems = new List<string>();
        try
        {
            archives ??= GameArchives.Open(source.GameFolder);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            problems.Add(problem.Message);
        }
        GameTexts texts = archives is null ? GameTexts.FromPairs([]) : GameTexts.Read(archives);
        if (archives is not null) problems.AddRange(archives.Problems);
        problems.AddRange(texts.Problems);

        UnitCatalog catalog;
        using (var files = new GameDataFiles(GameInstall.BaseZipOf(source.GameFolder), source.Mod?.Folder))
            catalog = UnitReader.Read(files, source.Game, texts);
        problems.AddRange(catalog.Problems);

        var pictures = new UnitPictureStore(cacheRoot, source.Game, archives?.Version ?? "", archives is null ? null : new GamePictures(archives), source.Mod?.Folder);
        return new LoadedUnits(source, catalog, pictures, problems);
    }
}
