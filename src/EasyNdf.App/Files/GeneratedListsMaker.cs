using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Game;
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
    /// These are WARNO's; Steel Division 2's are <see cref="SteelDivisionInputFiles"/>.
    /// </summary>
    public static IReadOnlyList<string> InputFiles { get; } =
    [
        "UniteDescriptor.ndf", "BuildingDescriptors.ndf", "DeckSerializer.ndf", "Divisions.ndf", "DivisionRules.ndf",
        "DeckPacks.ndf", "WeaponDescriptor.ndf", "Ammunition.ndf", "AmmunitionMissiles.ndf",
    ];

    /// <summary>
    /// Steel Division 2's files the lists are made from, as WARNO's (<see cref="InputFiles"/>) where it has them: its
    /// units, and its defences in DefenseProductionsDescriptors.ndf, which its divisions list as units; it has no
    /// battlegroup cards (DeckPacks.ndf) and no buildings file.
    /// </summary>
    public static IReadOnlyList<string> SteelDivisionInputFiles { get; } =
    [
        "UniteDescriptor.ndf", "DefenseProductionsDescriptors.ndf", "DeckSerializer.ndf", "Divisions.ndf", "DivisionRules.ndf",
        "WeaponDescriptor.ndf", "Ammunition.ndf", "AmmunitionMissiles.ndf",
    ];

    /// <summary>A game's files the lists are made from (<see cref="InputFiles"/>, <see cref="SteelDivisionInputFiles"/>).</summary>
    public static IReadOnlyList<string> InputFilesOf(GameTitle game) => game == GameTitle.SteelDivision2 ? SteelDivisionInputFiles : InputFiles;

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
    private const string MakerVersion = "3";

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
    /// <param name="Units">
    /// Every unit read, by its descriptor, with what Run export needs to give numbers for deck codes; kept with the
    /// lists so that an export with nothing changed does not read every unit again just for them.
    /// </param>
    internal sealed record Results(
        Dictionary<string, IReadOnlyList<DivisionUnit>> Lists, Dictionary<string, IReadOnlyList<BattlegroupCard>> Battlegroups, HashSet<string> PacksInFile,
        Dictionary<string, KnownUnit> Units);

    /// <summary>A unit read from the files, as Run export's numbers for deck codes need it.</summary>
    /// <param name="Name">Its name in the game; empty when it has none.</param>
    /// <param name="DeckNumber">Its number for deck codes (DeckSerializer.ndf's UnitIds); null when it has none.</param>
    internal sealed record KnownUnit(string Name, int? DeckNumber);

    /// <summary>The units read last, with the checksum of the files they were read from.</summary>
    private static (string Key, DivisionRosters Rosters)? lastRosters;

    /// <summary>The results made lately, by the checksum of what they were made from, the newest last.</summary>
    private static readonly List<(string Key, Results Results)> recent = [];

    private static readonly Lock Making = new();

    /// <summary>
    /// A game's files on the Files tab the lists are made from, each with the rules run on it; null when its
    /// UniteDescriptor.ndf is not there. Files of other games are left out.
    /// </summary>
    /// <param name="entries">The files on the Files tab.</param>
    /// <param name="game">The game whose files are read, and whose units the lists are made of.</param>
    internal static List<Input>? InputsFrom(IReadOnlyList<FileEntry> entries, GameTitle game)
    {
        var inputs = new List<Input>();
        foreach (string name in InputFilesOf(game))
        {
            FileEntry? entry = InputFile(entries, name, game);
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
    /// The file of a name on the Files tab the lists are made from; null when there is none of the game. Of two of one
    /// name, the one in the game's Generated folder is taken (<see cref="DivisionChangeRules.WritesTo"/>), whether it is
    /// on or not, as the Armory reads that one: Steel Division 2 has a 60-byte GameData/Gameplay/Gfx/UniteDescriptor.ndf
    /// beside its Generated one, and a generated division made from it got its defences alone. Then one that is on.
    /// </summary>
    public static FileEntry? InputFile(IEnumerable<FileEntry> entries, string name, GameTitle game) =>
        entries.Where(each => each.Source is not null && each.Game == game && string.Equals(each.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(DivisionChangeRules.WritesTo).ThenByDescending(each => each.On).FirstOrDefault();

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
            Results? cached = ReadCached(cacheFolder, key);
            Results results = WithNamesShared(cached ?? Make(rules, RostersFor(filesKey, inputs, textAfterRules)));
            Remember(key, results);
            // results read from the cache are kept there as they are: writing them again (some 100 MB, a second or
            // more) would give the same file, so it is only marked as the newest, as writing it did. Tuned for speed:
            // Keep it that way when tidying.
            if (cached is null) WriteCached(cacheFolder, key, results);
            else KeepAsNewest(cacheFolder, key);
            return results;
        }
    }

    /// <summary>The units of the files, read once for every file of an export that asks.</summary>
    private static DivisionRosters RostersFor(string filesKey, IReadOnlyList<Input> inputs, Func<Input, string> textAfterRules)
    {
        if (lastRosters is { } read && read.Key == filesKey) return read.Rosters;
        List<ChangedFile> files = [.. inputs.Select(input => new ChangedFile(input.Entry.Source!, textAfterRules(input)))];
        // the inputs are all of one game (InputsFrom), UniteDescriptor.ndf always among them
        DivisionRosters rosters = DivisionRosters.FromFiles(files, GameOf(inputs));
        lastRosters = (filesKey, rosters);
        return rosters;
    }

    /// <summary>The game the files the lists are made from are of; WARNO when there are none.</summary>
    private static GameTitle GameOf(IReadOnlyList<Input> inputs) => inputs.Count > 0 ? inputs[0].Entry.Game : GameTitle.Warno;

    /// <summary>Makes the lists and battlegroups the rules are for from the units read.</summary>
    private static Results Make(JsonArray rules, DivisionRosters rosters)
    {
        var units = new Dictionary<string, KnownUnit>(StringComparer.Ordinal);
        foreach (Unit unit in rosters.AllUnits) units.TryAdd(unit.Descriptor, new KnownUnit(unit.Name, unit.HasDeckNumber ? unit.DeckNumber : null));
        return new(GeneratedUnitLists.UnitsOf(rules, rosters), GeneratedBattlegroups.CardsOf(rules, rosters), [.. rosters.PacksInFile], units);
    }

    /// <summary>
    /// Tuned for memory: the results with each unit name kept once, shared by every list that names it. Each unit of each
    /// list had its own copy of its name and its transports' names, read from the cache folder or made apart: some 3
    /// million texts, 320 MB with Paul's mods, kept in memory and shown as "Other .NET data". Keep it that way when tidying.
    /// </summary>
    private static Results WithNamesShared(Results results)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        string Shared(string name)
        {
            if (names.TryGetValue(name, out string? kept)) return kept;
            names[name] = name;
            return name;
        }
        // units that ride in the same transports share one list of them too: some 40 MB more
        var transportLists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        IReadOnlyList<string> SharedTransports(IReadOnlyList<string> transports)
        {
            string key = string.Join('\n', transports);
            if (transportLists.TryGetValue(key, out IReadOnlyList<string>? kept)) return kept;
            IReadOnlyList<string> list = [.. transports.Select(Shared)];
            transportLists[key] = list;
            return list;
        }
        var lists = new Dictionary<string, IReadOnlyList<DivisionUnit>>(results.Lists.Count, StringComparer.Ordinal);
        foreach ((string division, IReadOnlyList<DivisionUnit> units) in results.Lists)
            lists[division] = [.. units.Select(unit => unit with { Unit = Shared(unit.Unit), Transports = SharedTransports(unit.Transports) })];
        return results with { Lists = lists };
    }

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
        // another game's files are read as that game's: its results are never another's (WARNO's checksums stay as they were)
        if (GameOf(inputs) != GameTitle.Warno) Add(GameOf(inputs).Id);
        foreach (Input input in inputs)
        {
            Add(input.Entry.Name);
            sum.AppendData(SHA256.HashData(FileContent.Bytes(input.Entry)));
            foreach (FileRunner.ModRules part in input.Stack)
            {
                sum.AppendData(JsonFiles.Utf8Of(part.Rules));
                sum.AppendData("\n"u8);
            }
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
            // each unit as [name, number for deck codes or null]
            var knownUnits = new Dictionary<string, KnownUnit>(StringComparer.Ordinal);
            foreach ((string descriptor, JsonNode? unit) in json?["Units"] as JsonObject ?? [])
                knownUnits[descriptor] = new KnownUnit(JsonValues.AsText(unit?[0]), unit?[1]?.GetValue<int>());
            return new Results(lists, battlegroups, packs, knownUnits);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Marks the results kept for a checksum as the newest, as writing them again would, so they are the last to be let
    /// go of (<see cref="LetGoOfOldest"/>).
    /// </summary>
    private static void KeepAsNewest(string? folder, string key)
    {
        if (folder is null) return;
        try
        {
            File.SetLastWriteTimeUtc(CachePath(folder, key), DateTime.UtcNow);
            LetGoOfOldest(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // the cache only saves time: an export goes on without it
        }
    }

    /// <summary>Deletes the results kept longest ago, past <see cref="ResultsKept"/>.</summary>
    private static void LetGoOfOldest(string folder)
    {
        foreach (FileInfo old in new DirectoryInfo(CacheFolderOf(folder)).GetFiles("*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(ResultsKept))
            old.Delete();
    }

    /// <summary>
    /// A value taken out of the object that holds it, to be put into another as it is: a value can belong to one object
    /// only, and copying each list instead took a good part of writing the results.
    /// </summary>
    private static JsonNode? TakenOut(JsonObject json, string name)
    {
        JsonNode? value = json[name];
        json.Remove(name);
        return value;
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
                lists[division] = TakenOut(new ModDivision { Added = [.. units] }.ToJson(), "Added");
            var battlegroups = new JsonObject();
            foreach ((string division, IReadOnlyList<BattlegroupCard> cards) in results.Battlegroups)
                battlegroups[division] = new ModDivision { BattlegroupCards = [.. cards] }.ToJson()["Battlegroup"]?["Cards"]?.DeepClone() ?? new JsonArray();
            var json = new JsonObject
            {
                ["Lists"] = lists,
                ["Battlegroups"] = battlegroups,
                ["PacksInFile"] = new JsonArray([.. results.PacksInFile.Order(StringComparer.Ordinal).Select(pack => (JsonNode)JsonValue.Create(pack))]),
                ["Units"] = new JsonObject(results.Units.Select(unit => KeyValuePair.Create<string, JsonNode?>(
                    unit.Key, new JsonArray(JsonValue.Create(unit.Value.Name), JsonValue.Create(unit.Value.DeckNumber))))),
            };
            // written straight into the file, rather than made into one text first: the results are some 100 MB. Tuned
            // for speed, as are the lists taken out rather than copied above: Keep it that way when tidying.
            using (FileStream file = File.Create(path))
            using (var writer = new Utf8JsonWriter(file))
                json.WriteTo(writer);
            LetGoOfOldest(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // the cache only saves time: an export goes on without it
        }
    }
}
