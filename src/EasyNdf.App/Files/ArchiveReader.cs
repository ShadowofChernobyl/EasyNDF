using System.IO;
using System.IO.Compression;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;

namespace EasyNdf.App.Files;

/// <summary>
/// Opens a .zip or .rar that is added or dropped, and takes out the .ndf files inside, with the folders they sit in
/// (the HTML app's <c>readZip</c> and <c>readRar</c>). They are kept in memory; nothing is unpacked to disk. A .zip is
/// read by .NET itself, a .rar by SharpCompress.
/// </summary>
public static class ArchiveReader
{
    /// <summary>Whether a file is an archive this can open.</summary>
    public static bool IsArchive(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);

    /// <summary>The .ndf files in an archive. Throws <see cref="ArchiveProblem"/>, saying why, when it cannot be opened.</summary>
    public static List<FileToAdd> NdfFilesIn(string archivePath) =>
        archivePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ? FromRar(archivePath) : FromZip(archivePath);

    private static List<FileToAdd> FromZip(string archivePath)
    {
        var found = new List<FileToAdd>();
        int locked = 0, unreadable = 0;
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(archivePath);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (!IsWanted(entry.FullName)) continue;
                if (entry.IsEncrypted) { locked++; continue; }
                byte[] bytes;
                try
                {
                    using Stream stream = entry.Open();
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    bytes = memory.ToArray();
                }
                catch (NotSupportedException) { unreadable++; continue; } // packed with a method other than Deflate or none
                found.Add(Item(archivePath, entry.FullName, bytes, entry.LastWriteTime.LocalDateTime));
            }
        }
        catch (InvalidDataException) { throw new ArchiveProblem("it is not a zip file, or it is damaged"); }
        if (found.Count == 0 && locked > 0) throw new ArchiveProblem("its files are password-protected");
        if (found.Count == 0 && unreadable > 0) throw new ArchiveProblem("it is packed in a way EasyNDF cannot read. Extract it and add the folder");
        return found;
    }

    private static List<FileToAdd> FromRar(string archivePath)
    {
        if (new FileInfo(archivePath).Length > 1.5e9) throw new ArchiveProblem("it is over 1.5 GB, too large to open here. Extract it and add the folder");
        var found = new List<FileToAdd>();
        try
        {
            using var rar = RarArchive.OpenArchive(archivePath);
            foreach (RarArchiveEntry entry in rar.Entries)
            {
                if (entry.IsDirectory || entry.Key is not string name || !IsWanted(name)) continue;
                if (entry.IsEncrypted) throw new ArchiveProblem("it is password-protected");
                using Stream stream = entry.OpenEntryStream();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                found.Add(Item(archivePath, name, memory.ToArray(), entry.LastModifiedTime ?? File.GetLastWriteTime(archivePath)));
            }
        }
        catch (Exception error) when (error is InvalidFormatException or ArchiveException or ArchiveOperationException or IncompleteArchiveException or InvalidDataException or EndOfStreamException)
        {
            throw new ArchiveProblem("it is damaged, or not a .rar file");
        }
        catch (CryptographicException) { throw new ArchiveProblem("it is password-protected"); }
        return found;
    }

    /// <summary>A .ndf file, leaving out the copies macOS puts in a "__MACOSX" folder.</summary>
    private static bool IsWanted(string pathInArchive) =>
        pathInArchive.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)
        && !("/" + pathInArchive.Replace('\\', '/')).Contains("/__MACOSX/", StringComparison.Ordinal);

    /// <summary>One file from an archive, with the folders it sits in (the HTML app's <c>arcItem</c>).</summary>
    private static FileToAdd Item(string archivePath, string pathInArchive, byte[] bytes, DateTime modified)
    {
        string path = pathInArchive.Replace('\\', '/').TrimStart('/');
        int lastSlash = path.LastIndexOf('/');
        string name = path[(lastSlash + 1)..], folder = lastSlash > 0 ? path[..lastSlash] : "";
        return new FileToAdd(name, folder, Path.GetFullPath(archivePath) + " > " + path, bytes, modified);
    }
}

/// <summary>Why an archive could not be opened, worded to follow "... could not be opened: ".</summary>
public sealed class ArchiveProblem(string message) : Exception(message);
