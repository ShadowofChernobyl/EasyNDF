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
/// <param name="Texts">The game's texts the units and divisions use, to read them again as a mod changes them.</param>
public sealed record LoadedUnits(UnitSource Source, UnitCatalog Catalog, UnitPictureStore Pictures, IReadOnlyList<string> Problems, GameTexts Texts)
{
    /// <summary>
    /// The descriptors of the game's own units, before any mod (the source's, for the game itself; base.zip's, for a
    /// game mod): a unit not among them is modded.
    /// </summary>
    public IReadOnlySet<string> VanillaUnits { get; init; } = new HashSet<string>();

    /// <summary>The descriptors of the game's own weapons, before any mod, as <see cref="VanillaUnits"/>: a weapon not among them is modded.</summary>
    public IReadOnlySet<string> VanillaWeapons { get; init; } = new HashSet<string>();

    /// <summary>The game's font for a card's number of units, copied into the cache; null when the game has none (see <see cref="GameFonts"/>).</summary>
    public string? UnitCountFontFile { get; init; }
}

/// <summary>The steps of reading a source's units, in order, for a loading bar.</summary>
public enum UnitReadStep
{
    /// <summary>Opening the game's archives and listing what is in them.</summary>
    FindingFiles,
    /// <summary>Reading the game's names for units, nations and the like.</summary>
    ReadingNames,
    /// <summary>Reading the units from the game's (and the mod's) files.</summary>
    ReadingUnits,
    /// <summary>Getting the units' pictures ready.</summary>
    PreparingPictures,
}

/// <summary>Reads a source's units, with the game's texts and pictures. Slow (seconds): run it in the background.</summary>
public static class UnitLibrary
{
    /// <summary>
    /// Reads a source's units. <paramref name="archives"/> are the game's archives when already open (a game and its
    /// mods share them); otherwise they are opened here. Throws <see cref="IOException"/> or
    /// <see cref="InvalidDataException"/> when the game's base.zip cannot be read.
    /// </summary>
    public static LoadedUnits Load(UnitSource source, string cacheRoot, GameArchives? archives = null, IProgress<UnitReadStep>? progress = null)
    {
        var problems = new List<string>();
        progress?.Report(UnitReadStep.FindingFiles);
        try
        {
            archives ??= GameArchives.Open(source.GameFolder);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            problems.Add(problem.Message);
        }
        progress?.Report(UnitReadStep.ReadingNames);
        GameTexts texts = archives is null ? GameTexts.FromPairs([]) : GameTexts.Read(archives);
        if (archives is not null) problems.AddRange(archives.Problems);
        problems.AddRange(texts.Problems);

        progress?.Report(UnitReadStep.ReadingUnits);
        UnitCatalog catalog;
        using (var files = new GameDataFiles(GameInstall.BaseZipOf(source.GameFolder), source.Mod?.Folder))
            catalog = UnitReader.Read(files, source.Game, texts);
        problems.AddRange(catalog.Problems);
        (IReadOnlySet<string> vanilla, IReadOnlySet<string> vanillaWeapons) = VanillaOf(source, catalog);

        progress?.Report(UnitReadStep.PreparingPictures);
        var pictures = new UnitPictureStore(cacheRoot, source.Game, archives?.Version ?? "", archives is null ? null : new GamePictures(archives), source.Mod?.Folder);
        string? countFont = archives is null ? null : GameFonts.UnitCountFontFile(source.GameFolder, cacheRoot, source.Game, archives.Version);
        return new LoadedUnits(source, catalog, pictures, problems, texts.OnlyThoseAsked()) { UnitCountFontFile = countFont, VanillaUnits = vanilla, VanillaWeapons = vanillaWeapons };
    }

    /// <summary>
    /// The game's own units and weapons: for the game itself, those just read; for a game mod, those of base.zip
    /// alone, found from their declaration lines, which takes a moment rather than a full read.
    /// </summary>
    private static (IReadOnlySet<string> Units, IReadOnlySet<string> Weapons) VanillaOf(UnitSource source, UnitCatalog catalog)
    {
        if (source.Mod is null)
            return (new HashSet<string>(catalog.Units.Select(unit => unit.Descriptor), StringComparer.Ordinal),
                new HashSet<string>((catalog.Weapons ?? []).Select(weapon => weapon.Descriptor), StringComparer.Ordinal));
        using var vanillaFiles = new GameDataFiles(GameInstall.BaseZipOf(source.GameFolder), null);
        return (UnitReader.UnitDescriptorsIn(vanillaFiles), UnitReader.AmmunitionDescriptorsIn(vanillaFiles));
    }

    /// <summary>
    /// Reads a source's units again with some of its files as an EasyNDF mod's rules make them. The game's texts read
    /// the first time are used again. Slow (seconds): run it in the background.
    /// </summary>
    public static UnitCatalog ReadChanged(LoadedUnits loaded, IReadOnlyList<ChangedFile> changedFiles)
    {
        UnitSource source = loaded.Source;
        using var files = new GameDataFiles(GameInstall.BaseZipOf(source.GameFolder), source.Mod?.Folder, changedFiles);
        return UnitReader.Read(files, source.Game, loaded.Texts, loaded.Catalog.FlagSources);
    }
}
