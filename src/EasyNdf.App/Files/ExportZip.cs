using System.IO;
using System.IO.Compression;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Files;

/// <summary>
/// "Save as zip...": the exported files in one .zip (the HTML app's <c>sendOut</c>), each in the folders it sat in. Two
/// files with the same path get "(2)", "(3)"... before their extension.
/// </summary>
public static class ExportZip
{
    /// <summary>Writes the .zip.</summary>
    public static void Write(string zipPath, IReadOnlyList<FileEntry> files)
    {
        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FileEntry file in files)
        {
            string fullName = (file.Folder != "" ? file.Folder + "/" : "") + file.Name;
            string name = fullName;
            for (int number = 2; used.Contains(name); number++)
                name = Path.ChangeExtension(fullName, null) + " (" + number + ")" + Path.GetExtension(fullName);
            used.Add(name);
            zip.CreateEntryFromFile(file.ExportedPath!, name);
        }
    }
}
