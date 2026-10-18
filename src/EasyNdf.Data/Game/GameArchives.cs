namespace EasyNdf.Data.Game;

/// <summary>
/// Every file in a game's archives (its Data\PC\...\ZZ_*.dat files), by its path inside them. Each game update adds
/// archives in a newer version folder; a newer archive's file replaces an older one of the same path. Steel Division
/// 2 keeps its interface textures in packs (pc/texture/pack/*.ppk) inside the archives; their files are listed too.
/// </summary>
public sealed class GameArchives
{
    private readonly Dictionary<string, ArchiveFile> files = new(StringComparer.OrdinalIgnoreCase);

    private GameArchives(string version) => Version = version;

    /// <summary>
    /// The game's newest version number, such as "175910": the newest version folder in Data\PC. It changes with every
    /// game update, so pictures saved from an older version are read again.
    /// </summary>
    public string Version { get; }

    /// <summary>The archives that could not be read, each with what went wrong.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>All the files, by path.</summary>
    public IReadOnlyDictionary<string, ArchiveFile> Files => files;

    /// <summary>Reads the file lists of all of a game's archives; the files themselves are read when they are needed.</summary>
    public static GameArchives Open(string gameFolder)
    {
        string dataFolder = Path.Combine(gameFolder, "Data", "PC");
        List<string> archives = ArchivesOldestFirst(dataFolder);
        // the newest archive's last version folder, without the padding
        string newest = archives.Select(VersionOf).DefaultIfEmpty("").Max(StringComparer.Ordinal) ?? "";
        var opened = new GameArchives(newest.Split('/')[^1].TrimStart('0'));
        foreach (string archive in archives)
        {
            try
            {
                foreach (ArchiveFile file in EdatArchive.ListFiles(archive, out _, out _)) opened.files[file.Name] = file;
            }
            catch (Exception problem) when (problem is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                opened.Problems.Add(Path.GetRelativePath(gameFolder, archive) + ": " + problem.Message);
            }
        }
        opened.AddTexturePacks();
        return opened;
    }

    /// <summary>A file by its path inside the archives, in any capitals; null when there is none.</summary>
    public ArchiveFile? Find(string path) => files.TryGetValue(path.Replace('\\', '/'), out ArchiveFile? file) ? file : null;

    /// <summary>
    /// The files inside the texture packs (pc/texture/pack/*.ppk) of Steel Division 2: archives of the same kind,
    /// kept inside the game's archives. The packs under pc/textureproxy hold only 3D textures.
    /// </summary>
    private void AddTexturePacks()
    {
        List<ArchiveFile> packs = files.Values
            .Where(file => file.Name.StartsWith("pc/texture/pack/", StringComparison.OrdinalIgnoreCase) && file.Name.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (ArchiveFile pack in packs)
        {
            try
            {
                foreach (ArchiveFile file in EdatArchive.ListFiles(pack.Archive, pack.Offset, out _, out _)) files[file.Name] = file;
            }
            catch (Exception problem) when (problem is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Problems.Add(pack.Name + ": " + problem.Message);
            }
        }
    }

    /// <summary>The game's archives, oldest version first, so a newer one's files win.</summary>
    private static List<string> ArchivesOldestFirst(string dataFolder)
    {
        if (!Directory.Exists(dataFolder)) return [];
        return Directory.EnumerateFiles(dataFolder, "ZZ_*.dat", SearchOption.AllDirectories)
            .OrderBy(VersionOf, StringComparer.Ordinal)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The version folders an archive is in, each padded to 20 digits so they sort as numbers do:
    /// Data\PC\21263\46442\ZZ_1.dat gives "00000000000000021263/00000000000000046442".
    /// </summary>
    private static string VersionOf(string path) =>
        string.Join("/", path.Split(Path.DirectorySeparatorChar, '/')
            .Where(part => part.Length > 0 && part.All(char.IsAsciiDigit))
            .Select(part => part.PadLeft(20, '0')));
}
