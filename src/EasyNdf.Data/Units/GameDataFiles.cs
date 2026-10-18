using System.IO.Compression;
using System.Text;

namespace EasyNdf.Data.Units;

/// <summary>
/// A game's GameData files as text: the clean copies in the game's Mods\ModData\base.zip, or, for a mod, the mod's own
/// copy of a file where it has one (a mod made with the game's CreateNewMod holds its own copy of every file).
/// </summary>
public sealed class GameDataFiles : IDisposable
{
    private readonly ZipArchive baseZip;
    private readonly string? modFolder;

    /// <summary>Opens a game's base.zip; with a mod's folder, that mod's files come first.</summary>
    public GameDataFiles(string baseZipPath, string? modFolder = null)
    {
        baseZip = ZipFile.OpenRead(baseZipPath);
        this.modFolder = modFolder;
    }

    /// <summary>
    /// The text of the file whose path ends with <paramref name="endOfPath"/>, such as
    /// "Generated/Gameplay/Gfx/UniteDescriptor.ndf"; null when there is none.
    /// </summary>
    public string? Read(string endOfPath)
    {
        string wanted = "/" + endOfPath.Replace('\\', '/').TrimStart('/');
        ZipArchiveEntry? entry = baseZip.Entries.FirstOrDefault(e => ("/" + e.FullName.Replace('\\', '/')).EndsWith(wanted, StringComparison.OrdinalIgnoreCase));
        return entry is null ? null : TextOf(entry);
    }

    /// <summary>
    /// The file whose path ends with <paramref name="endOfPath"/>, opened to be read line by line (the mod's copy when it
    /// has one); null when there is none. The caller closes it.
    /// </summary>
    public TextReader? Open(string endOfPath)
    {
        string wanted = "/" + endOfPath.Replace('\\', '/').TrimStart('/');
        ZipArchiveEntry? entry = baseZip.Entries.FirstOrDefault(e => ("/" + e.FullName.Replace('\\', '/')).EndsWith(wanted, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        if (ModPathOf(entry.FullName) is string own && File.Exists(own)) return new StreamReader(own, Encoding.UTF8);
        return new StreamReader(entry.Open(), Encoding.UTF8);
    }

    /// <summary>The paths (inside base.zip, with forward slashes) of the files a test picks, such as all the .ndf files under UserInterface.</summary>
    public IEnumerable<string> PathsWhere(Func<string, bool> picked) =>
        baseZip.Entries.Select(e => e.FullName.Replace('\\', '/')).Where(path => !path.EndsWith('/') && picked(path));

    /// <summary>The text of a file by its full path inside base.zip (as <see cref="PathsWhere"/> gives it).</summary>
    public string? ReadPath(string path)
    {
        ZipArchiveEntry? entry = baseZip.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase));
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
    public void Dispose() => baseZip.Dispose();
}
