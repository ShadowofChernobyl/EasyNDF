using System.IO;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Files;

/// <summary>
/// Reads a listed file, wherever it is: on disk, or in memory for one that came out of a .zip or .rar. Text is read as
/// a browser reads it: UTF-8, unless the file starts with a mark that says otherwise.
/// </summary>
public static class FileContent
{
    /// <summary>The row's file as bytes, exactly as they are.</summary>
    public static byte[] Bytes(FileEntry entry) => entry.ArchivedContent ?? File.ReadAllBytes(entry.Source!);

    /// <summary>The row's file as text.</summary>
    public static string Text(FileEntry entry) => entry.ArchivedContent is byte[] bytes ? TextOf(bytes) : File.ReadAllText(entry.Source!);

    /// <summary>A file about to be added, as text.</summary>
    public static string Text(FileToAdd file) => file.ArchivedContent is byte[] bytes ? TextOf(bytes) : File.ReadAllText(file.Source);

    /// <summary>Bytes as text, the way <see cref="File.ReadAllText(string)"/> reads a file.</summary>
    public static string TextOf(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
