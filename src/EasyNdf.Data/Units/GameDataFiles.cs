using System.IO.Compression;
using System.Text;

namespace EasyNdf.Data.Units;

/// <summary>
/// A file as an EasyNDF mod's rules make it: its path (on the Files tab) and its text after the rules.
/// </summary>
/// <param name="Path">Where the file is, such as "C:/Game/GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf".</param>
/// <param name="Text">Its text with the rules applied.</param>
public sealed record ChangedFile(string Path, string Text);

/// <summary>
/// A game's GameData files as text: the clean copies in the game's Mods\ModData\base.zip, or, for a mod, the mod's own
/// copy of a file where it has one (a mod made with the game's CreateNewMod holds its own copy of every file). Files an
/// EasyNDF mod's rules change (<see cref="ChangedFile"/>) come before both.
/// </summary>
public sealed class GameDataFiles : IDisposable
{
    /// <summary>The game's base.zip; null for files on their own (<see cref="GameDataFiles(IReadOnlyList{ChangedFile})"/>).</summary>
    private readonly ZipArchive? baseZip;
    private readonly string? modFolder;
    private readonly IReadOnlyList<ChangedFile> changedFiles;

    /// <summary>
    /// base.zip's files by path (with forward slashes) and by file name, made once: looking through every entry for each
    /// file read made reading the units again, at each change of the mod, take seconds.
    /// </summary>
    private readonly Dictionary<string, ZipArchiveEntry> entryByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ZipArchiveEntry>> entriesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opens a game's base.zip; with a mod's folder, that mod's files come first.</summary>
    /// <param name="baseZipPath">The game's base.zip.</param>
    /// <param name="modFolder">A game mod's folder, or null.</param>
    /// <param name="changedFiles">Files as an EasyNDF mod's rules make them, which come first of all; none by default.</param>
    public GameDataFiles(string baseZipPath, string? modFolder = null, IReadOnlyList<ChangedFile>? changedFiles = null)
    {
        baseZip = ZipFile.OpenRead(baseZipPath);
        this.modFolder = modFolder;
        this.changedFiles = changedFiles ?? [];
        foreach (ZipArchiveEntry entry in baseZip.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');
            entryByPath.TryAdd(path, entry);
            string name = System.IO.Path.GetFileName(path);
            if (!entriesByName.TryGetValue(name, out List<ZipArchiveEntry>? named)) entriesByName[name] = named = [];
            named.Add(entry);
        }
    }

    /// <summary>
    /// Files on their own, with no game behind them: the files on the Files tab, as an export makes generated divisions
    /// from them (<see cref="Divisions.DivisionRosters"/>). A file not among them is not found.
    /// </summary>
    /// <param name="files">The files, each with its path and text.</param>
    public GameDataFiles(IReadOnlyList<ChangedFile> files)
    {
        baseZip = null;
        modFolder = null;
        changedFiles = files;
    }

    /// <summary>The first of base.zip's files whose path ends with <paramref name="endOfPath"/>; null when there is none.</summary>
    private ZipArchiveEntry? EntryEndingWith(string endOfPath)
    {
        string wanted = "/" + endOfPath.Replace('\\', '/').TrimStart('/');
        if (!entriesByName.TryGetValue(System.IO.Path.GetFileName(wanted), out List<ZipArchiveEntry>? named)) return null;
        return named.FirstOrDefault(e => ("/" + e.FullName.Replace('\\', '/')).EndsWith(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The changed file standing for the game's file whose path ends with <paramref name="endOfPath"/>: one whose path
    /// ends the same way, or else the only one with the same file name (a file added to the Files tab on its own,
    /// outside its game folders). Null when there is none.
    /// </summary>
    private ChangedFile? ChangedFileFor(string endOfPath)
    {
        if (changedFiles.Count == 0) return null;
        string wanted = "/" + endOfPath.Replace('\\', '/').TrimStart('/');
        static string Slashed(string path) => "/" + path.Replace('\\', '/').TrimStart('/');
        ChangedFile? samePath = changedFiles.FirstOrDefault(file => Slashed(file.Path).EndsWith(wanted, StringComparison.OrdinalIgnoreCase));
        if (samePath is not null) return samePath;
        string name = System.IO.Path.GetFileName(wanted);
        var sameName = changedFiles.Where(file => string.Equals(System.IO.Path.GetFileName(file.Path), name, StringComparison.OrdinalIgnoreCase)).ToList();
        return sameName.Count == 1 ? sameName[0] : null;
    }

    /// <summary>
    /// The text of the file whose path ends with <paramref name="endOfPath"/>, such as
    /// "Generated/Gameplay/Gfx/UniteDescriptor.ndf"; null when there is none.
    /// </summary>
    public string? Read(string endOfPath)
    {
        if (ChangedFileFor(endOfPath) is ChangedFile changed) return changed.Text;
        ZipArchiveEntry? entry = EntryEndingWith(endOfPath);
        return entry is null ? null : TextOf(entry);
    }

    /// <summary>
    /// The file whose path ends with <paramref name="endOfPath"/>, opened to be read line by line (the mod's copy when it
    /// has one); null when there is none. The caller closes it.
    /// </summary>
    public TextReader? Open(string endOfPath)
    {
        if (ChangedFileFor(endOfPath) is ChangedFile changed) return new StringReader(changed.Text);
        ZipArchiveEntry? entry = EntryEndingWith(endOfPath);
        if (entry is null) return null;
        if (ModPathOf(entry.FullName) is string own && File.Exists(own)) return new StreamReader(own, Encoding.UTF8);
        return new StreamReader(entry.Open(), Encoding.UTF8);
    }

    /// <summary>The paths (inside base.zip, with forward slashes) of the files a test picks, such as all the .ndf files under UserInterface.</summary>
    public IEnumerable<string> PathsWhere(Func<string, bool> picked) =>
        (baseZip?.Entries ?? []).Select(e => e.FullName.Replace('\\', '/')).Where(path => !path.EndsWith('/') && picked(path));

    /// <summary>The text of a file by its full path inside base.zip (as <see cref="PathsWhere"/> gives it).</summary>
    public string? ReadPath(string path)
    {
        if (ChangedFileFor(path) is ChangedFile changed) return changed.Text;
        ZipArchiveEntry? entry = entryByPath.GetValueOrDefault(path.Replace('\\', '/'));
        return entry is null ? null : TextOf(entry);
    }

    /// <summary>
    /// Where a file of the mod would be: base.zip's "GameData/Generated/..." is the mod folder's
    /// "GameData\Generated\...". Null when no mod is open.
    /// </summary>
    public string? ModPathOf(string zipPath) =>
        modFolder is null ? null : Path.Combine([modFolder, .. zipPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)]);

    /// <summary>A file's text: the mod's copy when it has one, else base.zip's.</summary>
    private string TextOf(ZipArchiveEntry entry)
    {
        if (ModPathOf(entry.FullName) is string own && File.Exists(own)) return File.ReadAllText(own, Encoding.UTF8);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Closes base.zip.</summary>
    public void Dispose() => baseZip?.Dispose();
}
