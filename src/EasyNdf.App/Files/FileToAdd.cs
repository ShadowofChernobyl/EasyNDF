using System.IO;

namespace EasyNdf.App.Files;

/// <summary>
/// A file about to be added to the list: a file on disk, or one taken out of a .zip or .rar, which is kept in memory.
/// </summary>
/// <param name="Name">The file's name, such as "Ammunition.ndf".</param>
/// <param name="Folder">The folders it sat in under the folder or archive it came from, such as "Generated/Gameplay/Gfx"; "" for none.</param>
/// <param name="Source">Its path on disk, or the archive's path and the path inside it ("C:\Mods\Game.zip > Gameplay/Ammunition.ndf").</param>
/// <param name="ArchivedContent">Its bytes, for a file from an archive; null for a file on disk.</param>
/// <param name="ArchivedModified">When it was last changed, as the archive says, for a file from an archive.</param>
public sealed record FileToAdd(string Name, string Folder, string Source, byte[]? ArchivedContent = null, DateTime? ArchivedModified = null)
{
    /// <summary>A file on disk.</summary>
    /// <param name="path">Its path.</param>
    /// <param name="folder">The folders it sat in under the folder it was added from; "" for a file added on its own.</param>
    public static FileToAdd OnDisk(string path, string folder = "") => new(Path.GetFileName(path), folder, Path.GetFullPath(path));

    /// <summary>Its size in bytes.</summary>
    public long Size => ArchivedContent?.LongLength ?? new FileInfo(Source).Length;

    /// <summary>When it was last changed.</summary>
    public DateTime Modified => ArchivedModified ?? File.GetLastWriteTime(Source);
}
