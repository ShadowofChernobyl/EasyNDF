using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Languages;

namespace EasyNdf.Data;

/// <summary>
/// A mod as a .mod.zip: its mod file ("mod.json", as Export Mod writes it, without the files inside it), the files of
/// its File rules ("files/", under their stored names) and the pictures its Armory changes name ("pictures/", by file
/// name). Read back, only those three kinds of entry are taken, each checked, within size limits, so a package cannot
/// put anything anywhere else or fill the disk.
/// </summary>
public static class ModPackage
{
    /// <summary>The end of a package's file name.</summary>
    public const string Extension = ".mod.zip";

    private const string ModEntry = "mod.json", FilesFolder = "files/", PicturesFolder = "pictures/";

    /// <summary>The largest mod.json read, and the largest file of a File rule.</summary>
    private const long MostModBytes = 2L * 1024 * 1024 * 1024 - 1, MostFileBytes = 1L * 1024 * 1024 * 1024;

    /// <summary>The most read from one package in all, and the most entries looked at.</summary>
    private const long MostTotalBytes = 8L * 1024 * 1024 * 1024;
    private const int MostEntries = 200_000;

    /// <summary>Whether a path is a package's, by its name.</summary>
    public static bool IsPackage(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes a mod as a package.</summary>
    /// <param name="output">Where to write it.</param>
    /// <param name="mod">The mod.</param>
    /// <param name="isOn">Whether it is switched on, so an import keeps its switch.</param>
    /// <param name="store">Where the files of its File rules are kept; null leaves them out.</param>
    /// <param name="picture">The bytes of a picture it names, by the path it names it by; null leaves the pictures out.</param>
    public static void Write(Stream output, Mod mod, bool isOn, ModFileStore? store, Func<string, byte[]?>? picture)
    {
        string text = ModFiles.ExportText(mod, isOn);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        using (Stream entry = zip.CreateEntry(ModEntry, CompressionLevel.Optimal).Open())
            entry.Write(Encoding.UTF8.GetBytes(text));
        if (store is not null)
            foreach (string name in FileAndTextRules.StoredNamesOf(mod.Presets.SelectMany(preset => preset.Rules)).Distinct(StringComparer.Ordinal))
                if (store.Read(name) is byte[] content)
                    using (Stream entry = zip.CreateEntry(FilesFolder + name, CompressionLevel.Optimal).Open()) entry.Write(content);
        if (picture is not null)
            foreach ((string name, byte[] bytes) in ModFiles.PicturesOf(JsonNode.Parse(text), picture))
                using (Stream entry = zip.CreateEntry(PicturesFolder + name, CompressionLevel.NoCompression).Open()) entry.Write(bytes);
    }

    /// <summary>
    /// Reads a package: its mods (one, or a backup's), each with the files and pictures the package carries. Throws
    /// <see cref="ModFileException"/> for a file that is not a package or holds no mod.
    /// </summary>
    /// <param name="input">The package.</param>
    /// <param name="fileName">Its file name: a mod without a name is named after it.</param>
    public static List<Mod> Read(Stream input, string fileName)
    {
        string? text = null;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var pictures = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > MostEntries) throw new ModFileException(DataTexts.Get("Rules.ModFile.PackageTooLarge"));
            long total = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (name == ModEntry) text = Encoding.UTF8.GetString(ReadEntry(entry, MostModBytes, ref total));
                else if (name.StartsWith(FilesFolder, StringComparison.Ordinal) && ModFileStore.IsStoredName(name[FilesFolder.Length..]))
                    files[name[FilesFolder.Length..]] = ReadEntry(entry, MostFileBytes, ref total);
                else if (name.StartsWith(PicturesFolder, StringComparison.Ordinal) && ModFiles.IsPictureName(name[PicturesFolder.Length..]))
                {
                    byte[] bytes = ReadEntry(entry, ModFiles.MostPictureBytes, ref total);
                    if (ModFiles.IsPng(bytes)) pictures[name[PicturesFolder.Length..]] = bytes;
                }
                // anything else in the package is not read
            }
        }
        catch (InvalidDataException error) { throw new ModFileException(DataTexts.Get("Rules.ModFile.NotPackage", ("problem", error.Message))); }
        if (text is null) throw new ModFileException(DataTexts.Get("Rules.ModFile.NotPackage", ("problem", ModEntry)));
        string asModFile = IsPackage(fileName) ? fileName[..^Extension.Length] + ".mod.json" : fileName;
        List<Mod> mods = ModFiles.Read(text, asModFile);
        foreach (Mod mod in mods)
        {
            // the files are kept by the name their content gives, so one too many does no harm: the import checks each
            mod.CarriedFiles = files;
            if (pictures.Count > 0) mod.CarriedPictures = pictures;
        }
        return mods;
    }

    /// <summary>An entry's bytes, read no further than the limits whatever its header says (a zip can lie about its sizes).</summary>
    private static byte[] ReadEntry(ZipArchiveEntry entry, long most, ref long total)
    {
        using Stream stream = entry.Open();
        using var copy = new MemoryStream();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (copy.Length + read > most || total > MostTotalBytes) throw new ModFileException(DataTexts.Get("Rules.ModFile.PackageTooLarge"));
            copy.Write(buffer, 0, read);
        }
        return copy.ToArray();
    }
}
