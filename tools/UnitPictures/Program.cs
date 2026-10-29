using System.IO.Compression;
using System.Text.Json;
using UnitPictures;

// Reads each unit's picture and in-game name out of the installed games (WARNO and Steel Division 2) and saves them
// in a folder beside this tool:
//   dotnet run                                  both games, found in the Steam libraries
//   dotnet run -- "D:\Games\WARNO"              one game, from the folder given
// The output is unit-pictures\WARNO and unit-pictures\SD2: the pictures, units.json and report.txt.
// Nothing in the games' folders is changed.

string outputRoot = Path.GetFullPath("unit-pictures");
var games = new List<(string Short, string Folder)>();
if (args.Length > 0)
{
    if (!GameFolder.IsGame(args[0]))
    {
        Console.WriteLine("That folder has no Data\\PC and Mods\\ModData\\base.zip: " + args[0]);
        return 1;
    }
    games.Add((Path.GetFileName(Path.TrimEndingDirectorySeparator(args[0])).Contains("Steel", StringComparison.OrdinalIgnoreCase) ? "SD2" : "WARNO", args[0]));
}
else
{
    foreach (var (shortName, steamFolder) in new[] { ("WARNO", "WARNO"), ("SD2", "Steel Division 2") })
        if (GameFolder.Find(steamFolder) is string found) games.Add((shortName, found));
}
if (games.Count == 0)
{
    Console.WriteLine("Neither game was found. Give a game's folder: dotnet run -- \"C:\\...\\steamapps\\common\\WARNO\"");
    return 1;
}
foreach (var (shortName, folder) in games) Extract(shortName, folder, Path.Combine(outputRoot, shortName));
Console.WriteLine("Saved in: " + outputRoot);
return 0;

// one game: its units' pictures, names and a report, in its own folder
static void Extract(string game, string gameFolder, string output)
{
    Console.WriteLine();
    Console.WriteLine(game + ": " + gameFolder);
    Directory.CreateDirectory(output);
    var report = new List<string> { "Game: " + gameFolder };

    // 1. the units, and each picture key's file, from the game's clean NDF files
    string baseZipPath = Path.Combine(gameFolder, "Mods", "ModData", "base.zip");
    if (!File.Exists(baseZipPath))
    {
        Console.WriteLine("  no Mods\\ModData\\base.zip, so the unit list cannot be read; skipped");
        return;
    }
    using ZipArchive baseZip = ZipFile.OpenRead(baseZipPath);
    List<Unit> units = GameData.Units(baseZip);
    Dictionary<string, string> pictureFiles = GameData.PictureFiles(baseZip);
    Console.WriteLine("  " + units.Count + " units, " + pictureFiles.Count + " picture keys");
    report.Add(units.Count + " units, " + pictureFiles.Count + " picture keys");

    // 2. every file in the game's archives; a newer version's file replaces an older one of the same name
    var archiveFiles = new Dictionary<string, ArchiveFile>(StringComparer.OrdinalIgnoreCase);
    foreach (string archive in Archives(gameFolder))
    {
        try
        {
            List<ArchiveFile> files = EdatArchive.ListFiles(archive, out int entriesCut, out byte[] tree);
            foreach (ArchiveFile file in files) archiveFiles[file.Name] = file;
            report.Add("read " + Path.GetRelativePath(gameFolder, archive) + ": " + files.Count + " files" + (entriesCut > 0 ? ", " + entriesCut + " entries cut short" : ""));
            // the trees of archives whose entries ran past their parents, to see how they are laid out
            if (entriesCut > 0) SaveTree(output, gameFolder, archive, tree);
        }
        catch (Exception problem) when (problem is InvalidDataException or IOException)
        {
            report.Add("skipped " + Path.GetRelativePath(gameFolder, archive) + ": " + problem.Message);
        }
    }

    // for checking where things are: the archive paths of the unit pictures, atlases and dictionaries
    File.WriteAllLines(Path.Combine(output, "archive-paths.txt"), archiveFiles.Keys
        .Where(name => name.Contains("UnitsIcons", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dic", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase) && name.Contains("Interface", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.OrdinalIgnoreCase));
    // and a summary of everything in the archives: how many files of each kind, under each folder
    File.WriteAllLines(Path.Combine(output, "archive-summary.txt"), archiveFiles.Keys
        .GroupBy(name => string.Join("/", name.Split('/').SkipLast(1).Take(4)) + "  *" + Path.GetExtension(name).ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Count().ToString().PadLeft(7) + "  " + group.Key + "   e.g. " + group.First()));

    // Steel Division 2 keeps its interface textures in packs (.ppk) inside the archives; their files join the list
    AddPackFiles(archiveFiles, report);

    // 3. the names, from the English UNITS dictionary
    Dictionary<ulong, string> names = ReadUnitNames(archiveFiles, report);
    Console.WriteLine("  " + names.Count + " unit names");

    // 4. where each small picture is inside the interface's atlases (those of 3D models and effects are not needed)
    var atlasParts = new Dictionary<string, AtlasPart>(StringComparer.OrdinalIgnoreCase);
    foreach (ArchiveFile atlas in archiveFiles.Values.Where(IsInterfaceAtlas))
    {
        try
        {
            foreach (AtlasPart part in Textures.ReadAtlas(atlas.Read())) atlasParts.TryAdd(part.Source, part);
        }
        catch (Exception problem) when (problem is InvalidDataException or IOException or KeyNotFoundException or ArgumentException or InvalidCastException or ZstdSharp.ZstdException or EndOfStreamException)
        {
            report.Add("could not read the atlas " + atlas.Name + ": " + problem.Message);
        }
    }

    // 5. each picture, saved once however many units share it
    var decoded = new Dictionary<string, Picture>(StringComparer.OrdinalIgnoreCase);
    var savedAs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    var unitsOut = new List<object>();
    int saved = 0, missing = 0, named = 0;
    foreach (Unit unit in units)
    {
        string? name = Dictionaries.KeyOf(unit.NameToken) is ulong key && names.TryGetValue(key, out string? text) ? text : null;
        if (name is not null) named++;
        string? pictureName = null;
        if (!pictureFiles.TryGetValue(unit.PictureKey, out string? source))
            report.Add(unit.Descriptor + ": its picture key " + unit.PictureKey + " is not in ButtonTexturesUnites.ndf");
        else if (savedAs.TryGetValue(source, out string? already))
            pictureName = already;
        else
        {
            try
            {
                Picture? picture = PictureFor(source, archiveFiles, atlasParts, decoded);
                if (picture is null)
                {
                    report.Add("no picture found for " + source);
                    missing++;
                }
                else
                {
                    pictureName = Path.GetFileName(source);
                    File.WriteAllBytes(Path.Combine(output, pictureName), PngFile.Write(picture));
                    saved++;
                }
            }
            catch (Exception problem) when (problem is InvalidDataException or IOException or ZstdSharp.ZstdException or ArgumentException or EndOfStreamException)
            {
                report.Add("could not read the picture " + source + ": " + problem.Message + TextureHeaderOf(source, archiveFiles));
                missing++;
            }
            savedAs[source] = pictureName;
        }
        unitsOut.Add(new { descriptor = unit.Descriptor, name, picture = pictureName });
    }

    // 6. each nation's flag, in a "flags" folder, named by the nation's code
    Dictionary<string, string> flagFiles = GameData.FlagFiles(baseZip);
    string flagFolder = Path.Combine(output, "flags");
    Directory.CreateDirectory(flagFolder);
    int flagsSaved = 0;
    foreach (var (code, source) in flagFiles)
    {
        try
        {
            Picture? flag = PictureFor(source, archiveFiles, atlasParts, decoded);
            if (flag is null)
            {
                report.Add("no picture found for the flag " + source);
                continue;
            }
            File.WriteAllBytes(Path.Combine(flagFolder, code + ".png"), PngFile.Write(flag));
            flagsSaved++;
        }
        catch (Exception problem) when (problem is InvalidDataException or IOException or ZstdSharp.ZstdException or ArgumentException or EndOfStreamException)
        {
            report.Add("could not read the flag " + source + ": " + problem.Message + TextureHeaderOf(source, archiveFiles));
        }
    }
    report.Add(flagsSaved + " of " + flagFiles.Count + " flags saved: " + string.Join(", ", flagFiles.Keys.Order(StringComparer.Ordinal)));
    Console.WriteLine("  " + flagsSaved + " flags");

    string summary = saved + " pictures saved, " + missing + " not found; " + named + " of " + units.Count + " units named";
    report.Insert(1, summary);
    Console.WriteLine("  " + summary);
    File.WriteAllText(Path.Combine(output, "units.json"), JsonSerializer.Serialize(unitsOut, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllLines(Path.Combine(output, "report.txt"), report);
}

// an archive's file tree (only the tree, not the files), compressed, in the output's "trees" folder
static void SaveTree(string output, string gameFolder, string archive, byte[] tree)
{
    string folder = Path.Combine(output, "trees");
    Directory.CreateDirectory(folder);
    string name = Path.GetRelativePath(Path.Combine(gameFolder, "Data", "PC"), archive).Replace(Path.DirectorySeparatorChar, '_') + ".tree.gz";
    using FileStream file = File.Create(Path.Combine(folder, name));
    using var zip = new GZipStream(file, CompressionLevel.Optimal);
    zip.Write(tree);
}

// the picture at a "GameData:/Assets/2D/....png" path: first as its own texture, then as a part of an atlas
static Picture? PictureFor(string source, Dictionary<string, ArchiveFile> archiveFiles, Dictionary<string, AtlasPart> atlasParts, Dictionary<string, Picture> decoded)
{
    string ownTexture = Path.ChangeExtension(source.Replace("GameData:/", "PC/Texture/", StringComparison.OrdinalIgnoreCase), ".tgv").Replace('\\', '/');
    if (archiveFiles.TryGetValue(ownTexture, out ArchiveFile? own)) return Textures.ReadTgv(own.Read());
    if (!atlasParts.TryGetValue(source, out AtlasPart? part)) return null;
    if (!decoded.TryGetValue(part.Texture, out Picture? texture))
    {
        if (!archiveFiles.TryGetValue(part.Texture, out ArchiveFile? file)) return null;
        // atlas textures are large: keep only a few in memory
        if (decoded.Count >= 4) decoded.Clear();
        texture = Textures.ReadTgv(file.Read());
        decoded[part.Texture] = texture;
    }
    return texture.Crop(part.X, part.Y, part.Width, part.Height);
}

// atlases of the interface's pictures; those of 3D models and effects hold no unit pictures
static bool IsInterfaceAtlas(ArchiveFile file) =>
    file.Name.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase)
    && !file.Name.Contains("/Assets/3D/", StringComparison.OrdinalIgnoreCase)
    && !file.Name.Contains("/FX-", StringComparison.OrdinalIgnoreCase);

// the game's archives, oldest version first, so a newer one's files win
static IEnumerable<string> Archives(string gameFolder)
{
    static string VersionOf(string path) =>
        string.Join("/", path.Split(Path.DirectorySeparatorChar, '/').Where(part => ulong.TryParse(part, out _)).Select(part => ulong.Parse(part).ToString("D20")));
    return Directory.EnumerateFiles(Path.Combine(gameFolder, "Data", "PC"), "ZZ_*.dat", SearchOption.AllDirectories)
        .OrderBy(VersionOf, StringComparer.Ordinal).ThenBy(path => path, StringComparer.Ordinal);
}

// the English UNITS dictionary: a .dic under a "US" folder, or named "UNITS-US" in newer archives
static Dictionary<ulong, string> ReadUnitNames(Dictionary<string, ArchiveFile> archiveFiles, List<string> report)
{
    var names = new Dictionary<ulong, string>();
    foreach (ArchiveFile file in archiveFiles.Values)
    {
        string[] parts = file.Name.ToUpperInvariant().Split('/');
        string last = parts[^1];
        // WARNO: UNITS.dic under US, or UNITS-US.dic; Steel Division 2: unites.dic (French for "units") under us
        bool isEnglishUnits = (last is "UNITS.DIC" or "UNITES.DIC") && parts.Contains("US") && !parts.Contains("DEPRECATED") || last == "UNITS-US.DIC";
        if (!isEnglishUnits) continue;
        try
        {
            foreach (var (key, text) in Dictionaries.Read(file.Read())) names[key] = text;
            report.Add("names from " + file.Name);
        }
        catch (Exception problem) when (problem is InvalidDataException or IOException or ArgumentException)
        {
            report.Add("could not read " + file.Name + ": " + problem.Message);
        }
    }
    return names;
}

// the files inside the texture packs (pc/texture/pack/*.ppk, Steel Division 2), added to the archives' files; the
// packs under pc/textureproxy hold only 3D textures
static void AddPackFiles(Dictionary<string, ArchiveFile> archiveFiles, List<string> report)
{
    var packs = archiveFiles.Values.Where(file => file.Name.StartsWith("pc/texture/pack/", StringComparison.OrdinalIgnoreCase) && file.Name.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase)).ToList();
    foreach (ArchiveFile pack in packs)
    {
        try
        {
            List<ArchiveFile> files = EdatArchive.ListFiles(pack.Archive, pack.Offset, out int entriesCut, out _);
            foreach (ArchiveFile file in files) archiveFiles[file.Name] = file;
            report.Add("read the pack " + pack.Name + ": " + files.Count + " files" + (entriesCut > 0 ? ", " + entriesCut + " entries cut short" : ""));
        }
        catch (Exception problem) when (problem is InvalidDataException or IOException)
        {
            report.Add("skipped the pack " + pack.Name + ": " + problem.Message);
        }
    }
}

// the start of a picture's own .tgv, for the report when it cannot be read, so its layout can be looked at
static string TextureHeaderOf(string source, Dictionary<string, ArchiveFile> archiveFiles)
{
    string ownTexture = Path.ChangeExtension(source.Replace("GameData:/", "PC/Texture/", StringComparison.OrdinalIgnoreCase), ".tgv").Replace('\\', '/');
    return archiveFiles.TryGetValue(ownTexture, out ArchiveFile? own) ? " (header " + Textures.TgvHeader(own.Read()) + ")" : "";
}
