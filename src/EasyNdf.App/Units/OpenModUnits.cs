using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.App.Files;
using EasyNdf.App.Mods;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Rules;

namespace EasyNdf.App.Units;

/// <summary>The units as the open mod makes them, and how they differ from the game's.</summary>
/// <param name="Catalog">The units, read from the files as the mod's rules make them.</param>
/// <param name="Changes">What differs from the game's units, and which rules changed it.</param>
public sealed record ModUnits(UnitCatalog Catalog, UnitChanges Changes)
{
    /// <summary>What differs from the game's weapons, and which rules changed it.</summary>
    public WeaponChanges WeaponChanges { get; init; } = WeaponChanges.None;

    /// <summary>
    /// The files on the Files tab with the open mod's rules whose changes are not shown: with a game mod as the
    /// source, files that are not that mod's own copies (see <see cref="OpenModUnits"/>).
    /// </summary>
    public IReadOnlyList<string> FilesNotShown { get; init; } = [];
}

/// <summary>
/// The Armory shows the open mod's values: the open mod's rules are run on the files on the Files tab that the units
/// are read from (each with the preset it has there, as Preview runs them), the units are read again from what the
/// rules give, and compared with the game's. Only files on the Files tab are run; nothing is written.
/// </summary>
/// <remarks>
/// With a game mod as the source, a file the game mod has its own copy of is run only when the Files tab's copy is
/// that one: the Files tab's copy of another (vanilla WARNO's, say) would hide the game mod's units. Such files are
/// listed, so the Armory can say their changes are not shown.
/// </remarks>
public static class OpenModUnits
{
    /// <summary>One file to run: the file, and each mod's rules for it as the engine takes them, in the order they run.</summary>
    private sealed record FileToRun(FileEntry Entry, List<ModRun> Mods, Dictionary<string, string> OtherFiles);

    /// <summary>One mod's rules for a file, as the engine takes them.</summary>
    private sealed record ModRun(string ModName, List<Rule?> Rules);

    /// <summary>
    /// The files to run, read on the screen's thread (the rules may change while the run goes on): those on the
    /// Files tab that are on, loaded, have one of the open mod's presets, and are files the units are read from.
    /// </summary>
    /// <remarks>
    /// Tuned for speed, as this holds the window each time the mods' values are read: each file's rules are found once
    /// (<see cref="WithRules"/>), and the other files its Lookup rules read are found from those rules
    /// (<see cref="OtherFilesFor"/>) rather than from a fresh copy of every rule. Keep it that way when tidying.
    /// </remarks>
    /// <param name="game">The units as the source has them: another mod's generated divisions are made from them.</param>
    private static List<FileToRun> FilesToRun(IReadOnlyList<FileEntry> entries, Mod openMod, UnitSource source, List<string> notShown, UnitCatalog game)
    {
        var files = new List<FileToRun>();
        DivisionRosters? rosters = null;
        foreach ((FileEntry entry, List<(Mod Mod, List<JsonNode> Rules)> parts) in WithRules(entries, openMod, source.Game))
        {
            if (!IsTheSourcesCopy(entry, source))
            {
                notShown.Add(entry.Name);
                continue;
            }
            List<ModRun> mods = [.. parts.Select(part => new ModRun(part.Mod.Name, RuleLoading.LoadForEngine(WithGeneratedLists(part.Rules, game, ref rosters))))];
            files.Add(new FileToRun(entry, mods, OtherFilesFor(entry, parts, entries)));
        }
        return files;
    }

    /// <summary>
    /// The other listed files the Lookup rules run on a file read (<see cref="FileRunner.OtherFilesFor(FileEntry, IEnumerable{FileEntry})"/>),
    /// found from the rules the Armory runs on it. Those are an export's rules for the file without the open mod's
    /// locked ones, which are not run, so every file a rule run here looks up is found as an export finds it.
    /// </summary>
    private static Dictionary<string, string> OtherFilesFor(FileEntry entry, List<(Mod Mod, List<JsonNode> Rules)> parts, IReadOnlyList<FileEntry> entries) =>
        FileRunner.OtherFilesFor(parts.SelectMany(part => part.Rules), entries.Where(each => each.Game == entry.Game));

    /// <summary>
    /// A copy of a mod's rules for a file, with its generated divisions' unit lists made (another mod's: the open mod's
    /// are shown by the Armory itself), from the source's units, as the export makes them from the files.
    /// </summary>
    private static JsonArray WithGeneratedLists(List<JsonNode> rules, UnitCatalog game, ref DivisionRosters? rosters)
    {
        var copies = new JsonArray([.. rules.Select(rule => rule.DeepClone())]);
        if (!GeneratedUnitLists.AnyIn(copies) && !GeneratedBattlegroups.AnyIn(copies)) return copies;
        rosters ??= new DivisionRosters(game.Units, game.Divisions, game.BattlegroupCards);
        GeneratedUnitLists.Fill(copies, GeneratedUnitLists.UnitsOf(copies, rosters));
        GeneratedBattlegroups.Fill(copies, GeneratedBattlegroups.CardsOf(copies, rosters), rosters.PacksInFile);
        return copies;
    }

    /// <summary>
    /// The Files tab's files of the source's game that are on, loaded, units are read from, and have rules of a mod
    /// that is on, each with those rules (<see cref="PartsFor"/>). Another game's files are left out: their mods are
    /// not for the source's units.
    /// </summary>
    private static IEnumerable<(FileEntry Entry, List<(Mod Mod, List<JsonNode> Rules)> Parts)> WithRules(IReadOnlyList<FileEntry> entries, Mod openMod, GameTitle game)
    {
        foreach (FileEntry entry in entries)
        {
            if (!entry.On || entry.Source is null || entry.Game != game || !UnitReader.FilesWithValues.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)) continue;
            List<(Mod Mod, List<JsonNode> Rules)> parts = PartsFor(entry, openMod);
            if (parts.Count > 0) yield return (entry, parts);
        }
    }

    /// <summary>
    /// Whether a file on the Files tab stands for the file the source reads: always for the game itself; for a game
    /// mod, when the mod has no copy of its own of the file (it reads the game's), or when the Files tab's file is
    /// that copy.
    /// </summary>
    private static bool IsTheSourcesCopy(FileEntry entry, UnitSource source)
    {
        if (source.Mod is null) return true;
        List<string> ownCopies = OwnCopiesOf(source.Mod.Folder, entry.Name);
        if (ownCopies.Count == 0) return true;
        if (entry.ArchivedContent is not null || entry.Source is null) return false;
        string onDisk = Path.GetFullPath(entry.Source);
        return ownCopies.Any(copy => string.Equals(Path.GetFullPath(copy), onDisk, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A game mod's own copies of a file, by name, anywhere in its GameData folder.</summary>
    private static List<string> OwnCopiesOf(string modFolder, string fileName)
    {
        string gameData = Path.Combine(modFolder, "GameData");
        if (!Directory.Exists(gameData)) return [];
        try
        {
            return [.. Directory.EnumerateFiles(gameData, fileName, SearchOption.AllDirectories)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The rules run on a file for the Armory, mod by mod in the Mods tab's order: the open mod's (its preset's, then its
    /// Armory presets' rules kept by hand; its locked rules are not run, as the Armory shows those changes itself, as it
    /// makes them, <see cref="DivisionChanges.Apply"/>), and those of the other mods that are on; a mod that is off,
    /// the open one too, or one for another game than the file's, does not run, as on export. Each mod runs on its
    /// own, on what the one before gave, as export runs them (<see cref="FileRunner"/>), so a mod's variables and
    /// groups do not reach into the next. Mods with no rules for the file are left out.
    /// </summary>
    private static List<(Mod Mod, List<JsonNode> Rules)> PartsFor(FileEntry entry, Mod openMod)
    {
        var parts = new List<(Mod, List<JsonNode>)>();
        // only the mods of the file's game that are on, as export runs them: the open mod too, only when it is on
        foreach (Mod mod in ModStack.RunningFor(entry.Game))
        {
            List<JsonNode> rules = ReferenceEquals(mod, openMod) ? OpenModRulesFor(entry, openMod) : OtherModRulesFor(entry, mod);
            if (rules.Count > 0) parts.Add((mod, rules));
        }
        return parts;
    }

    /// <summary>
    /// The open mod's rules for a file: those of its rule sets that apply to the file, then its Armory presets' rules
    /// kept by hand. Its locked rules are not run: the Armory shows those changes itself.
    /// </summary>
    /// <remarks>
    /// Tuned for speed: the Armory presets' rules are copied for the file only when one of them for it is kept by hand
    /// (<see cref="HasRuleKeptByHand"/>). A mod's Armory rules are mostly locked, and a mod with thousands of them
    /// spent most of the window's time copying them only to leave them out. Keep it that way when tidying.
    /// </remarks>
    private static List<JsonNode> OpenModRulesFor(FileEntry entry, Mod openMod)
    {
        List<JsonNode> rules = [.. FileRunner.RuleSetsFor(entry, openMod).SelectMany(ruleSet => ruleSet.Rules).OfType<JsonNode>()];
        IReadOnlyList<JsonNode?> armoryRules = ArmoryPresets.RulesOf(openMod);
        if (!HasRuleKeptByHand(armoryRules, entry)) return rules;
        List<JsonNode> byHand = [.. DivisionChangeRules.ForFile(armoryRules, entry)
            .Where(rule => (string?)rule?["Kind"] == "Group" || !ArmoryPresets.IsLocked(rule)).OfType<JsonNode>()];
        if (byHand.Any(rule => (string?)rule["Kind"] != "Group")) rules.AddRange(byHand);
        return rules;
    }

    /// <summary>
    /// Whether an Armory preset's rule for a file is kept by hand (not locked), so that
    /// <see cref="DivisionChangeRules.ForFile(IEnumerable{JsonNode?}, FileEntry)"/> would give one to run; found without
    /// copying any rule. Each rule's file and kind are read as that method reads them.
    /// </summary>
    private static bool HasRuleKeptByHand(IReadOnlyList<JsonNode?> armoryRules, FileEntry entry)
    {
        if (!DivisionChangeRules.WritesTo(entry)) return false;
        foreach (JsonNode? rule in armoryRules)
        {
            string? file = (string?)rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.FileKey];
            bool isGroup = (string?)rule?["Kind"] == "Group";
            if (!isGroup && string.Equals(file, entry.Name, StringComparison.OrdinalIgnoreCase) && !ArmoryPresets.IsLocked(rule)) return true;
        }
        return false;
    }

    /// <summary>
    /// Another mod's rules for a file, as export runs them: those of its rule sets that apply to the file, then all its
    /// Armory presets' rules for it, locked ones too, as the Armory makes only the open mod's changes itself.
    /// </summary>
    private static List<JsonNode> OtherModRulesFor(FileEntry entry, Mod mod)
    {
        List<JsonNode> rules = [.. FileRunner.RuleSetsFor(entry, mod).SelectMany(ruleSet => ruleSet.Rules).OfType<JsonNode>()];
        rules.AddRange(DivisionChangeRules.ForFile(ArmoryPresets.RulesOf(mod), entry).OfType<JsonNode>());
        return rules;
    }

    /// <summary>
    /// What the units as the open mod makes them depend on, as text: each file to run (where it is, when it changed)
    /// with its rules, and the settings. While it stays the same, the units need not be read again; a division changed
    /// by hand, for one, changes the mod but not this.
    /// </summary>
    /// <param name="entries">The Files tab's files.</param>
    /// <param name="openMod">The mod open on the Rules tab.</param>
    /// <param name="game">The source's game: only its files and mods count.</param>
    /// <param name="settings">The settings Preview follows.</param>
    /// <remarks>
    /// Tuned for speed, as this holds the window at every change: each file's rules are found once, and written as
    /// they are, one after the other, rather than copied into a list first. Keep it that way when tidying.
    /// </remarks>
    public static string InputsOf(IReadOnlyList<FileEntry> entries, Mod openMod, GameTitle game, RunSettings settings)
    {
        var inputs = new System.Text.StringBuilder();
        inputs.Append(openMod.Name).Append('\n').Append(game.Id).Append('\n').Append(settings).Append('\n');
        foreach ((FileEntry entry, List<(Mod Mod, List<JsonNode> Rules)> parts) in WithRules(entries, openMod, game))
        {
            inputs.Append(entry.Source).Append('|').Append(entry.Modified?.Ticks);
            foreach ((Mod mod, List<JsonNode> rules) in parts)
            {
                // the same text as the rules in a JSON list: [rule,rule]
                inputs.Append('|').Append(mod.Name).Append("|[");
                for (int index = 0; index < rules.Count; index++)
                    inputs.Append(index == 0 ? "" : ",").Append(WithoutFold(rules[index]).ToJsonString());
                inputs.Append(']');
            }
            inputs.Append('\n');
        }
        return inputs.ToString();
    }

    /// <summary>A rule as it runs: a Group without whether it is folded on the Mods tab, which changes nothing it does.</summary>
    private static JsonNode WithoutFold(JsonNode rule)
    {
        if (rule is not JsonObject group || !group.ContainsKey("Collapsed")) return rule;
        var copy = (JsonObject)group.DeepClone();
        copy.Remove("Collapsed");
        return copy;
    }

    /// <summary>
    /// Reads the units as the open mod makes them. When none of its presets is on a unit file on the Files tab, the
    /// game's units are given back, with no changes. Slow (seconds) when there are files to run: the running and
    /// reading are done in the background.
    /// </summary>
    /// <param name="loaded">The units as the game (or the game mod chosen) has them.</param>
    /// <param name="entries">The Files tab's files.</param>
    /// <param name="openMod">The mod open on the Rules tab.</param>
    /// <param name="settings">The settings Preview follows, such as the decimal places.</param>
    /// <param name="progress">Told as the rules start running and as the changed units start being read.</param>
    public static async Task<ModUnits> Read(LoadedUnits loaded, IReadOnlyList<FileEntry> entries, Mod openMod, RunSettings settings,
        IProgress<ModApplyStep>? progress = null)
    {
        var notShown = new List<string>();
        List<FileToRun> files = FilesToRun(entries, openMod, loaded.Source, notShown, loaded.Catalog);
        if (files.Count == 0) return new ModUnits(loaded.Catalog, UnitChanges.None) { FilesNotShown = notShown };
        progress?.Report(ModApplyStep.RunningRules);
        ModUnits read = await Task.Run(() => RunAndRead(loaded, files, openMod.Name, settings, progress));
        return read with { FilesNotShown = notShown };
    }

    private static ModUnits RunAndRead(LoadedUnits loaded, List<FileToRun> files, string modName, RunSettings settings, IProgress<ModApplyStep>? progress)
    {
        var changedFiles = new List<ChangedFile>();
        var edits = new List<RuleEdit>();
        foreach (FileToRun file in files)
        {
            (string? text, List<RuleEdit> fileEdits) = RunMods(file, settings);
            // a file the rules leave unchanged, or make invalid, is read as the game has it
            if (text is null || fileEdits.Count == 0) continue;
            changedFiles.Add(new ChangedFile(Path.Combine(file.Entry.Folder, file.Entry.Name), text));
            edits.AddRange(fileEdits);
        }
        if (changedFiles.Count == 0) return new ModUnits(loaded.Catalog, UnitChanges.None);
        progress?.Report(ModApplyStep.ReadingUnits);
        UnitCatalog catalog = UnitLibrary.ReadChanged(loaded, changedFiles);
        return new ModUnits(catalog, UnitChanges.Between(modName, loaded.Catalog, catalog, edits))
        {
            WeaponChanges = WeaponChanges.Between(modName, loaded.Catalog.Weapons ?? [], catalog.Weapons ?? [], edits),
        };
    }

    /// <summary>
    /// Runs each mod's rules on a file, one mod after the other, each on what the one before gave, as Preview does,
    /// with every edit logged so each change can name its mod and rule. The text is null when the file cannot be read,
    /// or a mod's rules make it invalid.
    /// </summary>
    private static (string? Text, List<RuleEdit> Edits) RunMods(FileToRun file, RunSettings settings)
    {
        string text;
        try { text = FileContent.Text(file.Entry); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return (null, []); }
        var edits = new List<RuleEdit>();
        foreach (ModRun mod in file.Mods)
        {
            RunResult result = Run(text, mod.Rules, file.OtherFiles, settings);
            if (result.Text is null || result.ParseError != "") return (null, []);
            text = result.Text;
            edits.AddRange(RuleEdit.From(result, mod.ModName));
        }
        return (text, edits);
    }

    /// <summary>Runs one mod's rules on a text, with every edit logged.</summary>
    private static RunResult Run(string text, List<Rule?> rules, Dictionary<string, string> otherFiles, RunSettings settings)
    {
        var engine = new NdfEngine();
        engine.Load(text);
        return engine.Run(rules, new RunOptions
        {
            WantText = true,
            LogLimit = int.MaxValue,
            OtherFiles = otherFiles,
            KeepSourceComments = settings.KeepSourceComments,
            // the units are read from the text, so whether it is valid is found there
            CheckOutput = false,
            DecimalPlaces = settings.DecimalPlaces,
        });
    }
}

/// <summary>A step of working out the units from the mods that are on.</summary>
public enum ModApplyStep
{
    /// <summary>Running the mods' rules on the unit files.</summary>
    RunningRules,
    /// <summary>Reading the units from the files the rules changed.</summary>
    ReadingUnits,
    /// <summary>Showing them in the Armory.</summary>
    Showing,
}
