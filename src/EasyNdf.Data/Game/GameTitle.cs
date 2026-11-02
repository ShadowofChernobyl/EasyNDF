namespace EasyNdf.Data.Game;

/// <summary>One of the games EasyNDF works with: reads units from, and makes mods for.</summary>
/// <param name="Id">A short name that never changes, kept in settings and in the picture cache: "WARNO" or "SD2".</param>
/// <param name="Name">The game's name as shown: "WARNO" or "Steel Division 2".</param>
/// <param name="SteamFolderName">Its folder under steamapps\common.</param>
/// <param name="RangeUnit">The unit its weapon ranges are in: WARNO's own "GRU", or metres.</param>
public sealed record GameTitle(string Id, string Name, string SteamFolderName, string RangeUnit)
{
    /// <summary>
    /// Whether its weapon pictures are drawn in colour, to be shown as they are: Steel Division 2's are; WARNO's are
    /// plain shapes the game fills with one colour.
    /// </summary>
    public bool WeaponPicturesInColour { get; init; }

    /// <summary>
    /// Whether its unit cards show their numbers in boxes at the picture's top left, the cost over the units in a card,
    /// with the phase letter at the top right (Steel Division 2's); WARNO's draw the cost alone at the top right.
    /// </summary>
    public bool CardMarksInBoxes { get; init; }

    /// <summary>Whether its files have ready-made battlegroups for the Armory's Battlegroups sub-tab: WARNO's do, Steel Division 2's don't.</summary>
    public bool HasBattlegroups { get; init; }

    /// <summary>WARNO. Its ranges are in GRU, the game's own unit.</summary>
    public static readonly GameTitle Warno = new("WARNO", "WARNO", "WARNO", "GRU") { HasBattlegroups = true };

    /// <summary>Steel Division 2. Its ranges are in metres.</summary>
    public static readonly GameTitle SteelDivision2 = new("SD2", "Steel Division 2", "Steel Division 2", "m") { WeaponPicturesInColour = true, CardMarksInBoxes = true };

    /// <summary>Every game, WARNO first. Nothing may count on there being two: more may come.</summary>
    public static IReadOnlyList<GameTitle> All { get; } = [Warno, SteelDivision2];

    /// <summary>The game with this <see cref="Id"/>, or null.</summary>
    public static GameTitle? WithId(string? id) => All.FirstOrDefault(game => game.Id == id);

    /// <summary>
    /// The game a folder's (or an archive's) name names, or null when it names none, or more than one: "WARNO",
    /// "Steel Division 2", "SD2 mods"... Spaces and case are not minded. Unlike <see cref="OfFolder"/>, a name that
    /// names no game is not taken for WARNO, so a file added by hand can be asked about instead of guessed.
    /// </summary>
    public static GameTitle? NamedBy(string folderName)
    {
        string squeezed = WithoutSpaces(folderName);
        List<GameTitle> named = [.. All.Where(game => new[] { game.Id, game.Name, game.SteamFolderName }
            .Any(name => squeezed.Contains(WithoutSpaces(name), StringComparison.OrdinalIgnoreCase)))];
        return named.Count == 1 ? named[0] : null;
    }

    /// <summary>
    /// The game a file is for, judged by where it is: inside a game's folder (its Mods folder too), the game; else
    /// the nearest folder or archive on its path whose name names one game (<see cref="NamedBy"/>), below the user's
    /// own folder (whose name is the user's, not a game's). Null when nothing says.
    /// </summary>
    /// <param name="path">Where the file is: a path on disk, or an archive's path and the path inside it ("C:\Mods\SD2.zip &gt; GameData/Units.ndf").</param>
    /// <param name="installs">The games found on this computer, with their folders.</param>
    public static GameTitle? OfPath(string path, IEnumerable<(GameTitle Game, string Folder)> installs)
    {
        // a file inside an archive is judged by the archive's path, then by the folders inside it
        int inside = path.IndexOf(" > ", StringComparison.Ordinal);
        string onDisk = inside < 0 ? path : path[..inside];
        foreach ((GameTitle game, string folder) in installs)
        {
            string start = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
            if (onDisk.StartsWith(start, StringComparison.OrdinalIgnoreCase)) return game;
        }
        var names = new List<string>();
        if (inside >= 0) names.AddRange(path[(inside + 3)..].Split('/', '\\').SkipLast(1).Reverse());
        // the archive's own name counts as a folder's, without its ending
        names.Add(inside >= 0 ? Path.GetFileNameWithoutExtension(onDisk) : "");
        string home = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        for (string? folder = Path.GetDirectoryName(onDisk); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
        {
            if (string.Equals(Path.TrimEndingDirectorySeparator(folder), home, StringComparison.OrdinalIgnoreCase)) break;
            names.Add(Path.GetFileName(folder));
        }
        foreach (string name in names.Where(name => name != ""))
            if (NamedBy(name) is GameTitle game) return game;
        return null;
    }

    private static string WithoutSpaces(string text) => string.Concat(text.Where(letter => !char.IsWhiteSpace(letter)));

    /// <summary>
    /// The game a folder holds, judged by its name: a folder named like Steel Division 2's is Steel Division 2, any
    /// other game folder is taken for WARNO. Their files are laid out the same way, so the name is all there is.
    /// </summary>
    public static GameTitle OfFolder(string folder)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return name.Contains("Steel", StringComparison.OrdinalIgnoreCase) || name.Contains("SD2", StringComparison.OrdinalIgnoreCase)
            ? SteelDivision2 : Warno;
    }
}
