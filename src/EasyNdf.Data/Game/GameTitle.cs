namespace EasyNdf.Data.Game;

/// <summary>One of the two games EasyNDF reads units from.</summary>
/// <param name="Id">A short name that never changes, kept in settings and in the picture cache: "WARNO" or "SD2".</param>
/// <param name="Name">The game's name as shown: "WARNO" or "Steel Division 2".</param>
/// <param name="SteamFolderName">Its folder under steamapps\common.</param>
/// <param name="RangeUnit">The unit its weapon ranges are in: WARNO's own "GRU", or metres.</param>
public sealed record GameTitle(string Id, string Name, string SteamFolderName, string RangeUnit)
{
    /// <summary>WARNO. Its ranges are in GRU, the game's own unit.</summary>
    public static readonly GameTitle Warno = new("WARNO", "WARNO", "WARNO", "GRU");

    /// <summary>Steel Division 2. Its ranges are in metres.</summary>
    public static readonly GameTitle SteelDivision2 = new("SD2", "Steel Division 2", "Steel Division 2", "m");

    /// <summary>Both games, WARNO first.</summary>
    public static IReadOnlyList<GameTitle> All { get; } = [Warno, SteelDivision2];

    /// <summary>The game with this <see cref="Id"/>, or null.</summary>
    public static GameTitle? WithId(string? id) => All.FirstOrDefault(game => game.Id == id);

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
