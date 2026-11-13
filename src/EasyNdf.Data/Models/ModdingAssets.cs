using System.IO.Compression;
using EasyNdf.Data.Game;

namespace EasyNdf.Data.Models;

/// <summary>
/// Steel Division 2's models, from its Modding Assets: a free Steam tool (Library, Tools, "Steel Division 2 Modding
/// Assets") installed beside the game. Its Assets\3D\Units folder holds a zip for each nation's category
/// (GER\Char.zip), whose entries are the models (.Ase2NdfBin, see <see cref="AseModel"/>) and their textures (.png),
/// each under the rest of its path in the game: "Assets/3D/Units/GER/Char/Tiger_E_GER/Tiger_E_GER.Ase2NdfBin" is
/// "Tiger_E_GER/Tiger_E_GER.Ase2NdfBin" in GER\Char.zip. Zips are opened as they are first needed.
/// </summary>
public sealed class ModdingAssets : IModelPack
{
    /// <summary>The tool's folder name under steamapps\common.</summary>
    public const string FolderName = "Steel Division 2 Modding Assets";

    private const string UnitsFolder = "Assets/3D/Units/";

    private readonly string folder;
    private readonly Dictionary<string, Opened?> zips = new(StringComparer.OrdinalIgnoreCase);
    private readonly object reading = new();

    private sealed record Opened(ZipArchive Zip, Dictionary<string, ZipArchiveEntry> Entries);

    private ModdingAssets(string folder) => this.folder = folder;

    /// <summary>
    /// The Modding Assets for a Steel Division 2 game folder: beside it under steamapps\common, else in any Steam
    /// library; null when none is installed.
    /// </summary>
    public static ModdingAssets? Find(string gameFolder)
    {
        string? beside = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(gameFolder));
        string? found = beside is not null && IsAssetsFolder(Path.Combine(beside, FolderName)) ? Path.Combine(beside, FolderName) : GameInstall.FindTool(FolderName);
        return found is not null && IsAssetsFolder(found) ? new ModdingAssets(found) : null;
    }

    private static bool IsAssetsFolder(string candidate) => Directory.Exists(Path.Combine(candidate, "Assets", "3D", "Units"));

    /// <summary>Whether it has the file at a path in the game, such as a model or a texture.</summary>
    public bool Has(string path) => Entry(path) is not null;

    /// <summary>The model at a path in the game; null when the Modding Assets have none, or it cannot be read.</summary>
    public ModelMesh? Read(string path)
    {
        if (ReadFile(path) is not byte[] file) return null;
        try
        {
            return AseModel.Read(file, Normalised(path));
        }
        catch (Exception problem) when (problem is InvalidDataException or EndOfStreamException or IOException)
        {
            return null;
        }
    }

    /// <summary>A file's bytes, such as a texture's (.png), by its path in the game; null when the Modding Assets have none.</summary>
    public byte[]? ReadFile(string path)
    {
        lock (reading)
        {
            if (Entry(path) is not ZipArchiveEntry entry) return null;
            using Stream stream = entry.Open();
            var bytes = new byte[entry.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
    }

    /// <summary>The Modding Assets list no models up front: they are found in their zips by path.</summary>
    public int ModelCount => 0;

    /// <summary>A path as the game's files write it ("GameData:Assets\3D\…"), from "Assets/" on, with forward slashes.</summary>
    public static string Normalised(string path)
    {
        string slashed = path.Replace('\\', '/');
        int colon = slashed.IndexOf(':');
        if (colon >= 0 && slashed[..colon].Equals("GameData", StringComparison.OrdinalIgnoreCase)) slashed = slashed[(colon + 1)..];
        return slashed.TrimStart('/');
    }

    /// <summary>The zip entry of a path: the zip of its nation's category, then the rest of its path; null when there is none.</summary>
    private ZipArchiveEntry? Entry(string path)
    {
        string normalised = Normalised(path);
        if (!normalised.StartsWith(UnitsFolder, StringComparison.OrdinalIgnoreCase)) return null;
        string[] steps = normalised[UnitsFolder.Length..].Split('/', 3);
        if (steps.Length < 3) return null;
        lock (reading)
        {
            string zipPath = steps[0] + "/" + steps[1];
            if (!zips.TryGetValue(zipPath, out Opened? opened)) zips[zipPath] = opened = Open(Path.Combine(folder, "Assets", "3D", "Units", steps[0], steps[1] + ".zip"));
            return opened is not null && opened.Entries.TryGetValue(steps[2], out ZipArchiveEntry? entry) ? entry : null;
        }
    }

    /// <summary>A zip and its entries by name (case aside); null when it is missing or cannot be opened.</summary>
    private static Opened? Open(string zipFile)
    {
        if (!File.Exists(zipFile)) return null;
        try
        {
            ZipArchive zip = ZipFile.OpenRead(zipFile);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in zip.Entries) entries.TryAdd(entry.FullName.Replace('\\', '/'), entry);
            return new Opened(zip, entries);
        }
        catch (Exception problem) when (problem is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Closes the zips.</summary>
    public void Dispose()
    {
        lock (reading)
        {
            foreach (Opened? opened in zips.Values) opened?.Zip.Dispose();
            zips.Clear();
        }
    }
}
