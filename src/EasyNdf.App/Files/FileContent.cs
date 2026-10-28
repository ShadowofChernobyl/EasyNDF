using System.IO;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Files;

/// <summary>
/// Reads a listed file, wherever it is: on disk, or packed in memory for one that came out of a .zip or .rar. Text is read as
/// a browser reads it: UTF-8, unless the file starts with a mark that says otherwise.
/// </summary>
public static class FileContent
{
    /// <summary>The row's file as bytes, exactly as they are.</summary>
    public static byte[] Bytes(FileEntry entry) => entry.ArchivedContent?.Unpack() ?? File.ReadAllBytes(entry.Source!);

    /// <summary>The row's file as text.</summary>
    public static string Text(FileEntry entry) => entry.ArchivedContent is PackedBytes packed ? TextOf(packed.Unpack()) : File.ReadAllText(entry.Source!);

    /// <summary>
    /// Where a row's file is, taken at one moment: Run export takes it for every file when it starts, so a file the File
    /// Editor gives the row while the export runs reaches the next export, not this one.
    /// </summary>
    /// <param name="Source">The file on disk, for a row read from one.</param>
    /// <param name="Archived">The file's bytes, for a row read from an archive or given by the File Editor.</param>
    public sealed record Snapshot(string? Source, PackedBytes? Archived)
    {
        /// <summary>The file as bytes, exactly as they are.</summary>
        public byte[] Bytes() => Archived?.Unpack() ?? File.ReadAllBytes(Source!);

        /// <summary>The file as text.</summary>
        public string Text() => Archived is PackedBytes packed ? TextOf(packed.Unpack()) : File.ReadAllText(Source!);
    }

    /// <summary>Where a row's file is now (<see cref="Snapshot"/>).</summary>
    public static Snapshot SnapshotOf(FileEntry entry) => new(entry.Source, entry.ArchivedContent);

    /// <summary>A file about to be added, as text.</summary>
    public static string Text(FileToAdd file) => file.ArchivedContent is PackedBytes packed ? TextOf(packed.Unpack()) : File.ReadAllText(file.Source);

    /// <summary>Bytes as text, the way <see cref="File.ReadAllText(string)"/> reads a file.</summary>
    public static string TextOf(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
