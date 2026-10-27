using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Files;

/// <summary>
/// Makes what generated divisions need at export: their unit lists (<see cref="GeneratedUnitLists"/>) and, for those
/// whose battlegroup is generated too, their battlegroups' cards (<see cref="GeneratedBattlegroups"/>), from the units of
/// the files on the Files tab as the mods' rules make them. Reading every unit takes seconds, so what they are made from
/// is summed up in checksums: when nothing has changed since an earlier export (the files, the rules run on them, and
/// how the divisions are made), what was made then is used again, from memory or from the cache folder. Each file the
/// export writes asks for them, so the units read are kept for the files after it.
/// </summary>
public static class GeneratedListsMaker
{
    /// <summary>
    /// The files the lists are made from: the units (UniteDescriptor.ndf, and BuildingDescriptors.ndf for WARNO's FOBs),
    /// their numbers in deck codes (DeckSerializer.ndf), the game's divisions, for the numbers they give each unit, and,
    /// for generated battlegroups, the units' weapons (WeaponDescriptor.ndf and the ammunition files) and the cards the
    /// game has (DeckPacks.ndf). UniteDescriptor.ndf must be on the Files tab; the others are read when they are.
    /// </summary>
    public static IReadOnlyList<string> InputFiles { get; } =
    [
        "UniteDescriptor.ndf", "BuildingDescriptors.ndf", "DeckSerializer.ndf", "Divisions.ndf", "DivisionRules.ndf",
        "DeckPacks.ndf", "WeaponDescriptor.ndf", "Ammunition.ndf", "AmmunitionMissiles.ndf",
    ];

    /// <summary>The file without which no list can be made.</summary>
    public const string UnitsFile = "UniteDescriptor.ndf";

    /// <summary>
    /// The files a generated battlegroup needs on the Files tab besides those of its division: its deck and cards are
    /// written into the first two, and its units are marked by their weapons, read from the others.
    /// </summary>
    public static IReadOnlyList<string> BattlegroupFiles { get; } = ["Decks.ndf", "DeckPacks.ndf", "WeaponDescriptor.ndf", "Ammunition.ndf", "AmmunitionMissiles.ndf"];

    /// <summary>
    /// The game's divisions are read as the file has them, before the mods' rules: the rules on it are what fill the
    /// generated divisions' lists, which cannot be made from themselves.
    /// </summary>
    private const string ReadAsItIs = "DivisionRules.ndf";

    /// <summary>Changes when the way lists are made changes, so what an older version made is never used.</summary>
    private const string MakerVersion = "2";

    /// <summary>How many sets of results are kept, in memory and in the cache folder: one for each file that asks, and more.</summary>
    private const int ResultsKept = 8;

    /// <summary>One file the lists are made from, worked out on the app's own thread before the export goes to the background.</summary>
    /// <param name="Entry">The file on the Files tab.</param>
    /// <param name="Stack">The mods' rules run on it, with which it is read; none for a file read as it is.</param>
    /// <param name="OtherFiles">The other files the Lookup rules among them read.</param>
    internal sealed record Input(FileEntry Entry, List<FileRunner.ModRules> Stack, Dictionary<string, string> OtherFiles);

    /// <summary>What is made for an export.</summary>
    /// <param name="Lists">The unit list of each generated division, by its descriptor.</param>
    /// <param name="Battlegroups">The cards of each generated battlegroup, by its division's descriptor.</param>
    /// <param name="PacksInFile">The battlegroup cards DeckPacks.ndf has, as the mods' rules make it.</param>
    internal sealed record Results(
        Dictionary<string, IReadOnlyList<DivisionUnit>> Lists, Dictionary<string, IReadOnlyList<BattlegroupCard>> Battlegroups, HashSet<string> PacksInFile);

    /// <summary>The units read last, with the checksum of the files they were read from.</summary>
    private static (string Key, DivisionRosters Rosters)? lastRosters;

    /// <summary>The results made lately, by the checksum of what they were made from, the newest last.</summary>
    private static readonly List<(string Key, Results Results)> recent = [];

    private static readonly Lock Making = new();

    /// <summary>The files on the Files tab the lists are made from, each with the rules run on it; null when UniteDescriptor.ndf is not there.</summary>
    internal static List<Input>? InputsFrom(IReadOnlyList<FileEntry> entries)
    {
        var inputs = new List<Input>();
        foreach (string name in InputFiles)
        {
            FileEntry? entry = entries.Where(each => each.Source is not null && string.Equals(each.Name, name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(each => each.On).FirstOrDefault();
            if (entry is null)
            {
                if (name == UnitsFile) return null;
                continue;
            }
            List<FileRunner.ModRules> stack = name == ReadAsItIs ? [] : FileRunner.StackFor(entry);
            inputs.Add(new Input(entry, stack, FileRunner.OtherFilesFor(stack.SelectMany(part => part.Rules), entries)));
        }
        return inputs;
    }

    /// <summary>
    /// The unit lists and battlegroups the rules are for: those made earlier when nothing they are made from has
    /// changed, else made now. It runs in the background.
    /// </summary>
    /// <param name="rules">The rules, among them those that set generated divisions' unit lists or add generated battlegroups.</param>
    /// <param name="inputs">The files they are made from (<see cref="InputsFrom"/>).</param>
    /// <param name="textAfterRules">A file's text after the mods' rules run on it.</param>
    /// <param name="cacheFolder">Where results are kept between sessions; null to keep them only in memory.</param>
    internal static Results ResultsFor(JsonArray rules, IReadOnlyList<Input> inputs, Func<Input, string> textAfterRules, string? cacheFolder)
    {
        string filesKey = FilesChecksum(inputs);
        string key = ResultsChecksum(filesKey, rules);
        lock (Making)
        {
            if (recent.FirstOrDefault(made => made.Key == key) is { Results: not null } known) return known.Results;
            Results results = ReadCached(cacheFolder, key) ?? Make(rules, RostersFor(filesKey, inputs, textAfterRules));
            Remember(key, results);
            WriteCached(cacheFolder, key, results);
            return results;
        }
    }

    /// <summary>The units of the files, read once for every file of an export that asks.</summary>
    private static DivisionRosters RostersFor(string filesKey, IReadOnlyList<Input> inputs, Func<Input, string> textAfterRules)
    {
        if (lastRosters is { } read && read.Key == filesKey) return read.Rosters;
        List<ChangedFile> files = [.. inputs.Select(input => new ChangedFile(input.Entry.Source!, textAfterRules(input)))];
        DivisionRosters rosters = DivisionRosters.FromFiles(files, Data.Game.GameTitle.Warno);
        lastRosters = (filesKey, rosters);
        return rosters;
    }

    /// <summary>Makes the lists and battlegroups the rules are for from the units read.</summary>
    private static Results Make(JsonArray rules, DivisionRosters rosters) =>
        new(GeneratedUnitLists.UnitsOf(rules, rosters), GeneratedBattlegroups.CardsOf(rules, rosters), [.. rosters.PacksInFile]);

    /// <summary>Keeps results in memory, the oldest let go of past <see cref="ResultsKept"/>.</summary>
    private static void Remember(string key, Results results)
    {
        recent.RemoveAll(made => made.Key == key);
        recent.Add((key, results));
        if (recent.Count > ResultsKept) recent.RemoveAt(0);
    }

    /// <summary>A checksum of the files the units are read from: each file, with the rules run on it.</summary>
    private static string FilesChecksum(IReadOnlyList<Input> inputs)
    {
        using var sum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text) => sum.AppendData(Encoding.UTF8.GetBytes(text + "\n"));
        Add(MakerVersion);
        foreach (Input input in inputs)
        {
            Add(input.Entry.Name);
            sum.AppendData(SHA256.HashData(FileContent.Bytes(input.Entry)));
            foreach (FileRunner.ModRules part in input.Stack) Add(part.Rules.ToJsonString());
        }
        return Convert.ToHexStringLower(sum.GetHashAndReset());
    }

    /// <summary>A checksum of what the results are made from: the files, and how each generated division and battlegroup is made (with the user's changes).</summary>
    private static string ResultsChecksum(string filesKey, JsonArray rules)
    {
        using var sum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text) => sum.AppendData(Encoding.UTF8.GetBytes(text + "\n"));
        Add(filesKey);
        foreach (JsonObject entry in GeneratedUnitLists.EntriesIn(rules)) Add("list " + entry.ToJsonString());
        foreach (JsonObject entry in GeneratedBattlegroups.EntriesIn(rules)) Add("battlegroup " + entry.ToJsonString());
        return Convert.ToHexStringLower(sum.GetHashAndReset());
    }

    /// <summary>The folder results are kept in.</summary>
    private static string CacheFolderOf(string folder) => Path.Combine(folder, "Generated divisions");

    /// <summary>Where the results made for a checksum are kept.</summary>
    private static string CachePath(string folder, string key) => Path.Combine(CacheFolderOf(folder), key + ".json");

    /// <summary>The results kept for a checksum; null when none are, or they cannot be read.</summary>
    private static Results? ReadCached(string? folder, string key)
    {
        if (folder is null || !File.Exists(CachePath(folder, key))) return null;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(CachePath(folder, key))) as JsonObject;
            var lists = new Dictionary<string, IReadOnlyList<DivisionUnit>>(StringComparer.Ordinal);
            // each list is kept as the units of a division entry, read back the way the mod file reads them
            foreach ((string division, JsonNode? units) in json?["Lists"] as JsonObject ?? [])
                lists[division] = ModDivision.FromJson(new JsonObject { ["Added"] = units?.DeepClone() }).Added;
            // and each battlegroup as a division entry's battlegroup
            var battlegroups = new Dictionary<string, IReadOnlyList<BattlegroupCard>>(StringComparer.Ordinal);
            foreach ((string division, JsonNode? cards) in json?["Battlegroups"] as JsonObject ?? [])
                battlegroups[division] = ModDivision.FromJson(new JsonObject { ["Battlegroup"] = new JsonObject { ["Cards"] = cards?.DeepClone() } }).BattlegroupCards ?? [];
            HashSet<string> packs = [.. (json?["PacksInFile"] as JsonArray ?? []).Select(JsonValues.AsText)];
            return new Results(lists, battlegroups, packs);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Keeps the results made for a checksum, the oldest let go of past <see cref="ResultsKept"/>.</summary>
    private static void WriteCached(string? folder, string key, Results results)
    {
        if (folder is null) return;
        try
        {
            string path = CachePath(folder, key);
            Directory.CreateDirectory(CacheFolderOf(folder));
            var lists = new JsonObject();
            foreach ((string division, IReadOnlyList<DivisionUnit> units) in results.Lists)
                lists[division] = new ModDivision { Added = [.. units] }.ToJson()["Added"]!.DeepClone();
            var battlegroups = new JsonObject();
            foreach ((string division, IReadOnlyList<BattlegroupCard> cards) in results.Battlegroups)
                battlegroups[division] = new ModDivision { BattlegroupCards = [.. cards] }.ToJson()["Battlegroup"]?["Cards"]?.DeepClone() ?? new JsonArray();
            var json = new JsonObject
            {
                ["Lists"] = lists,
                ["Battlegroups"] = battlegroups,
                ["PacksInFile"] = new JsonArray([.. results.PacksInFile.Order(StringComparer.Ordinal).Select(pack => (JsonNode)JsonValue.Create(pack))]),
            };
            File.WriteAllText(path, json.ToJsonString());
            foreach (FileInfo old in new DirectoryInfo(CacheFolderOf(folder)).GetFiles("*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(ResultsKept))
                old.Delete();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // the cache only saves time: an export goes on without it
        }
    }
}
