using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EasyNdf.App.Files;
using EasyNdf.App.Mods;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Game;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Rules;

namespace EasyNdf.App.Models;

/// <summary>
/// With the game itself as the Armory's source and "Show mods' changes" on, the model viewer shows a unit as the mods
/// that run make it, the way an export would: the files it reads its models from (depictions, model resources,
/// weapons, missiles, sounds) are run through the mods' rules, the .ndf files their File rules bring are read as new
/// files, and the models and textures the game's mod tools built into each export folder's Gen folder are found there.
/// A new unit's model shows once its mod has been exported and generated.
/// </summary>
/// <remarks>
/// Tuned for memory, after the work that brought the app's memory down: nothing is kept in memory between openings.
/// The files the rules change are written once into the app's cache folder (one set only, replaced when the rules or
/// the files change) and read from there when the viewer asks for them. Keep it that way when tidying.
/// </remarks>
internal static class ModdedModelFiles
{
    /// <summary>What the viewer reads from the mods: changed files on the disk, and the Gen folders to look in for models.</summary>
    public sealed record Found(IReadOnlyList<(string Path, string OnDisk)> Files, IReadOnlyList<string> GenFolders);

    /// <summary>What is worked out on the screen's thread, then run in the background (the rules may change meanwhile).</summary>
    public sealed record Work(List<(FileEntry Entry, List<JsonArray> Rules, Dictionary<string, string> OtherFiles)> Runs,
        List<(string Destination, string OnDisk)> Carried, List<string> GenFolders, string Fingerprint);

    /// <summary>The names of the files the viewer reads (see ModelLinks and DepictionReader), and the folders whose files it reads.</summary>
    private static readonly HashSet<string> ViewerFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "DepictionVehicles.ndf", "DepictionAerialUnits.ndf", "DepictionOperators.ndf", "DepictionWeaponBlock.ndf", "DepictionFXWeapons.ndf",
        "SoundDescriptors.ndf", "SM_Drive.ndf", "SM_Fly.ndf", "SM_Constant.ndf", "ST_Generic.ndf", "SoundSettings.ndf",
        "MissileCarriage.ndf", "MissileCarriageDepiction.ndf", "Pylons.ndf", "GeneratedDepictionVehicles.ndf", "DepictionAlternatives.ndf",
        "MissileDescriptors.ndf", "WeaponDescriptor.ndf", "DepictionInfantry.ndf",
    };

    private static bool ViewerReads(FileEntry entry) =>
        ViewerFiles.Contains(entry.Name) || entry.Folder.Contains("Gfx/DepictionResources", StringComparison.OrdinalIgnoreCase);

    private static bool ViewerReads(string destination) =>
        destination.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)
        && (ViewerFiles.Contains(Path.GetFileName(destination)) || destination.Contains("Gfx/DepictionResources", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What to run for the viewer, read on the screen's thread; null when no mod that runs changes what the viewer
    /// reads. Only for a WARNO source that is the game itself (a game mod as the source has its own files and Gen).
    /// </summary>
    public static Work? Prepare(IReadOnlyList<FileEntry> entries, GameTitle game, string? cacheFolder)
    {
        if (game != GameTitle.Warno) return null;
        var runs = new List<(FileEntry, List<JsonArray>, Dictionary<string, string>)>();
        var genFolders = new List<string>();
        var print = new StringBuilder();
        foreach (FileEntry entry in entries)
        {
            if (!entry.On || entry.Source is null || entry.Game != game) continue;
            List<FileRunner.ModRules> stack = FileRunner.StackFor(entry);
            if (stack.Count == 0) continue;
            // the folder each mod exports into: its Gen holds the models the game's mod tools built from its files
            if (entry.ExportFolder is not null && FileAndTextRules.ModFolderOf(FileRunner.ExportPathOf(entry)) is string modFolder)
            {
                string gen = Path.Combine(modFolder, "Gen");
                if (!genFolders.Contains(gen, StringComparer.OrdinalIgnoreCase) && Directory.Exists(gen)) genFolders.Add(gen);
            }
            if (!ViewerReads(entry)) continue;
            List<JsonArray> rules = [.. stack.Select(part => part.Rules)];
            runs.Add((entry, rules, FileRunner.OtherFilesFor(entry, entries)));
            print.Append(entry.Source).Append('|').Append(entry.Modified?.Ticks);
            foreach (JsonArray part in rules) print.Append('|').Append(part.ToJsonString());
            print.Append('\n');
        }
        // the new .ndf files the mods' File rules bring, read from the app's file store where they are kept
        var carried = new List<(string, string)>();
        var store = AppData.Current.ModFileStore;
        foreach (var mod in ModStack.RunningFor(game))
            foreach (var ruleSet in mod.Presets)
                foreach (FileAndTextRules.CarriedFile file in FileAndTextRules.FilesOf(ruleSet.Rules))
                    if (ViewerReads(file.Destination) && store.PathOf(file.StoredName) is string onDisk)
                    {
                        carried.Add((file.Destination, onDisk));
                        print.Append(file.Destination).Append('>').Append(file.StoredName).Append('\n');
                    }
        if (runs.Count == 0 && carried.Count == 0 && genFolders.Count == 0) return null;
        string fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(print.ToString())))[..24];
        return cacheFolder is null ? null : new Work(runs, carried, genFolders, fingerprint);
    }

    /// <summary>
    /// Runs the rules (in the background), or finds the files they made last time in the cache, and gives what the
    /// viewer reads. One set is kept: the cache of other rules or files is deleted.
    /// </summary>
    public static Found Run(Work work, string cacheFolder)
    {
        string root = Path.Combine(cacheFolder, "Model Files");
        string folder = Path.Combine(root, work.Fingerprint);
        string list = Path.Combine(folder, "files.txt");
        var files = new List<(string Path, string OnDisk)>();
        if (File.Exists(list))
        {
            foreach (string line in File.ReadAllLines(list))
                if (line.Split('\t') is [string path, string name]) files.Add((path, Path.Combine(folder, name)));
        }
        else
        {
            if (Directory.Exists(root))
                foreach (string old in Directory.GetDirectories(root))
                    try { Directory.Delete(old, recursive: true); }
                    catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
            Directory.CreateDirectory(folder);
            var lines = new List<string>();
            for (int i = 0; i < work.Runs.Count; i++)
            {
                (FileEntry entry, List<JsonArray> rules, Dictionary<string, string> otherFiles) = work.Runs[i];
                if (TextAfterRules(entry, rules, otherFiles) is not string text) continue;
                string name = i + ".ndf", path = entry.Folder == "" ? entry.Name : entry.Folder + "/" + entry.Name;
                File.WriteAllText(Path.Combine(folder, name), text, new UTF8Encoding(false));
                lines.Add(path + "\t" + name);
                files.Add((path, Path.Combine(folder, name)));
            }
            // written last, so a cache cut short is made again next time
            File.WriteAllLines(list, lines);
        }
        // the carried files are read from the store, where they already are
        files.AddRange(work.Carried.Select(file => (file.Destination, file.OnDisk)));
        return new Found(files, work.GenFolders);
    }

    /// <summary>A file's text after each mod's rules, one mod after the other as export runs them; null when it cannot be read or a mod's rules make it invalid.</summary>
    private static string? TextAfterRules(FileEntry entry, List<JsonArray> rules, Dictionary<string, string> otherFiles)
    {
        string text;
        try { text = FileContent.Text(entry); }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { return null; }
        foreach (JsonArray part in rules)
        {
            var engine = new NdfEngine();
            engine.Load(text);
            RunResult result = engine.Run(RuleLoading.LoadForEngine(part), new RunOptions { WantText = true, LogLimit = 0, OtherFiles = otherFiles, CheckOutput = false });
            if (result.Text is null || result.ParseError != "") return null;
            text = result.Text;
        }
        return text;
    }
}
