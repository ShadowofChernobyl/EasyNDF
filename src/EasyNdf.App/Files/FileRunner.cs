using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.FileList;
using EasyNdf.Engine;
using EasyNdf.Engine.Export;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Rules;
using EasyNdf.App.Mods;
using EasyNdf.App.Text;

namespace EasyNdf.App.Files;

/// <summary>
/// Preview and Run export (the HTML app's <c>runJobs</c>): runs each file's preset on it, one file at a time, and says
/// on each row what happened. Run export writes each edited file into its export folder, keeping the folders it sat in.
/// </summary>
public sealed class FileRunner(FileListModel model, RunSettings settings)
{
    /// <summary>
    /// Asks which WARNO mod an export of UNITS.csv is for, by the mod's folder name, when its export folder is in no
    /// mod (see <see cref="GameModFor"/>); null when the user cancels, which stops the export.
    /// </summary>
    public Func<string, Task<string?>>? AskGameMod { get; init; }

    /// <summary>The WARNO mod this export's UNITS.csv goes into, by its folder name; null when it writes none.</summary>
    private string? namesFileMod;

    /// <summary>
    /// Runs the rules on every file that is on and has rules to run (<see cref="FilesThatRun"/>).
    /// </summary>
    /// <param name="write">True for Run export, false for Preview.</param>
    /// <param name="chooseExportFolder">
    /// Asks for an export folder, for every file when "Ask for the export folder each time" is on, else for the files
    /// that have none; null when the user cancels, which stops the export.
    /// </param>
    /// <param name="skipped">Files left out of this run, such as files EasyNDF exported before that the user chose to skip.</param>
    public async Task Run(bool write, Func<Task<string?>> chooseExportFolder, IReadOnlySet<FileEntry>? skipped = null)
    {
        if (model.IsBusy) return;
        bool everyFile = write && settings.ExportEveryFile;
        List<FileEntry> list = FilesThatRun(model.Entries, everyFile).Where(e => skipped?.Contains(e) != true).ToList();
        if (list.Count == 0)
        {
            model.Warn(model.Entries.Any(e => e.On && e.Source is null)
                ? Texts.Get("Files.Run.NotLoaded")
                : Texts.Get("Files.Run.NothingToRun"));
            return;
        }

        if (write)
        {
            // Every file gets the folder chosen now, when the user wants to be asked each time; otherwise the files with
            // no export folder get one, chosen once for all of them.
            List<FileEntry> withoutFolder = settings.AskExportFolder ? list : list.Where(e => e.ExportFolder is null).ToList();
            if (withoutFolder.Count > 0)
            {
                string? folder = await chooseExportFolder();
                if (folder is null) return;
                model.SetExportFolder(withoutFolder, folder);
            }
            // (Asking about the mod's watermark first, when it says to ask, comes with the watermark window.)
            FileEntry? namesFile = list.FirstOrDefault(entry => WritesDictionary(entry) && GoesIntoLocalisation(entry));
            if (namesFile is not null)
            {
                namesFileMod = await GameModFor(namesFile.ExportFolder!);
                if (namesFileMod is null) return;
            }
        }

        model.StartProgress(write);
        var finished = new List<FileEntry>();
        model.IsBusy = true;
        // the window stays smooth while the files run, and what running them left behind is given back at the end
        using IDisposable longWork = Memory.DuringLongWork();
        try
        {
            model.Say("");
            foreach (FileEntry entry in list) entry.ClearMessage();
            // A row with no rules that is not part of this run drops the message left by an earlier "every file" export.
            foreach (FileEntry entry in model.Entries.Except(list))
                if (entry.Message != "" && !entry.IsError && !entry.HasRules) entry.ClearMessage();

            // Every row says where it is in the run: waiting, working, then what happened.
            foreach (FileEntry entry in list)
            {
                entry.Message = Texts.Get("Files.Run.Waiting");
                model.RowChanged(entry);
            }
            foreach (FileEntry entry in list)
            {
                if (model.IsCancelRequested) break;
                model.ShowProgress(entry.Name, finished.Count, list.Count);
                entry.Message = Texts.Get("Files.Run.Working");
                model.RowChanged(entry);
                FileWork work = WorkFor(entry, model.Entries);
                await Task.Run(() => RunOne(entry, write, everyFile, work));
                finished.Add(entry);
                model.DetailsRow ??= entry;
                model.RowChanged(entry);
            }
        }
        finally
        {
            model.IsBusy = false;
        }
        // after the buttons come back, which clears the note
        if (finished.Count < list.Count) SayCancelled(list.Skip(finished.Count), finished.Count, list.Count);
        lock (conflictsFound)
        {
            ModConflictList.Found(conflictsFound);
            conflictsFound.Clear();
        }
        if (write)
        {
            WriteDivisionExtras(finished);
            RemoveModFilesNoLongerUsed(finished);
            SayModFilesWritten();
        }
    }

    /// <summary>
    /// After Cancel: the files the run did not reach say so, and the note says how many were done. Files already
    /// exported stay as they are.
    /// </summary>
    private void SayCancelled(IEnumerable<FileEntry> notReached, int finished, int total)
    {
        foreach (FileEntry entry in notReached)
        {
            entry.Message = Texts.Get("Files.Run.Cancelled");
            model.RowChanged(entry);
        }
        model.Warn(Texts.Get("Files.Run.CancelledNote", ("done", finished), ("total", total)));
    }

    /// <summary>
    /// The files a run takes: those that are on and read, with rules to run. That is a preset of the open mod's, rules
    /// of the Armory presets of the mods that are on, or a preset of another mod that is on; with "every file" exports,
    /// every file that is on. Run export's questions beforehand ask about these same files.
    /// </summary>
    internal static List<FileEntry> FilesThatRun(IEnumerable<FileEntry> entries, bool everyFile)
    {
        // worked out once for the whole list, which can hold thousands of files
        IReadOnlySet<string> armoryFiles = FilesWithArmoryRules();
        IReadOnlyList<Mod> running = ModStack.Running;
        bool byName = RuleSetsApplyByName;
        return [.. entries.Where(entry => entry.On && entry.Source is not null
            && (everyFile || armoryFiles.Contains(entry.Name) || running.Any(mod => RuleSetFiles.For(entry.Name, entry.Folder, mod.Presets, byName).Count > 0)))];
    }

    /// <summary>
    /// What one file's run needs, worked out on the app's own thread before the run goes to the background, so that a
    /// rule changed or a mod switched during an export cannot change a file half way through its run.
    /// </summary>
    /// <param name="Stack">What each mod runs on the file (<see cref="StackFor"/>).</param>
    /// <param name="IsNamesFile">Whether it is a dictionary file (UNITS.csv...) with texts from Text rules or the Armory presets (<see cref="RunNamesFile"/>).</param>
    /// <param name="Texts">For a dictionary file, the texts it is given, in the order they run; otherwise none.</param>
    /// <param name="ModFiles">The files the File rules that run on it write into the mod on export (<see cref="FileAndTextRules"/>).</param>
    /// <param name="GeneratedInputs">
    /// For a file with generated divisions' unit lists to make (DivisionRules.ndf), the files they are made from
    /// (<see cref="GeneratedListsMaker"/>); empty when UniteDescriptor.ndf is not on the Files tab; null for any other file.
    /// </param>
    /// <param name="RuleSets">The rule sets Details names for the run, joined with commas.</param>
    /// <param name="HeaderMod">The mod the export's header names (<see cref="HeaderModOf"/>); null when no mod runs.</param>
    /// <param name="OpenModName">The open mod's name, for the message when no mod that is on has rules for the file.</param>
    /// <param name="OtherFiles">The other files the Lookup rules read (<see cref="OtherFilesFor(IEnumerable{JsonNode?}, IEnumerable{FileEntry})"/>).</param>
    private sealed record FileWork(List<ModRules> Stack, bool IsNamesFile, IReadOnlyList<DictionaryText> Texts, string RuleSets, Mod? HeaderMod,
        string OpenModName, Dictionary<string, string> OtherFiles, IReadOnlyList<FileAndTextRules.CarriedFile> ModFiles,
        List<GeneratedListsMaker.Input>? GeneratedInputs = null);

    /// <summary>What a file's run needs (<see cref="FileWork"/>), with its own copies of the rules.</summary>
    private static FileWork WorkFor(FileEntry entry, IEnumerable<FileEntry> entries)
    {
        string openModName = AppData.Current.OpenMod.Name;
        List<ModRules> stack = StackFor(entry);
        List<FileAndTextRules.CarriedFile> modFiles = FileAndTextRules.FilesOf(stack.SelectMany(part => part.Rules));
        if (WritesDictionary(entry))
            return new FileWork([], IsNamesFile: true, DictionaryTextsFor(entry), "", null, openModName, [], modFiles);
        if (stack.Count == 0) return new FileWork(stack, IsNamesFile: false, [], "", null, openModName, [], []);
        Dictionary<string, string> otherFiles = OtherFilesFor(stack.SelectMany(part => part.Rules), entries);
        List<GeneratedListsMaker.Input>? generated = HasGeneratedRules(stack)
            ? GeneratedListsMaker.InputsFrom([.. entries]) ?? []
            : null;
        return new FileWork(stack, IsNamesFile: false, [], RuleSetNames(stack), HeaderModOf(stack), openModName, otherFiles, modFiles, generated);
    }

    /// <summary>Whether a file's rules include those a generated division or battlegroup fills at export.</summary>
    private static bool HasGeneratedRules(List<ModRules> stack)
    {
        List<JsonNode> rules = [.. stack.SelectMany(part => part.Rules)];
        return GeneratedUnitLists.AnyIn(rules) || GeneratedBattlegroups.AnyIn(rules);
    }

    /// <summary>
    /// Gives the rules that set generated divisions' unit lists, and those that add generated battlegroups, what is
    /// made from the files on the Files tab (<see cref="GeneratedListsMaker"/>). The rules are the run's own copies.
    /// Returns why it cannot, or null.
    /// </summary>
    private string? FillGeneratedLists(List<ModRules> stack, List<GeneratedListsMaker.Input>? inputs)
    {
        if (inputs is null) return null;
        if (inputs.Count == 0) return Texts.Get("Files.Run.Generated.NoUnits", ("file", GeneratedListsMaker.UnitsFile));
        var rules = new JsonArray([.. stack.SelectMany(part => part.Rules).Select(rule => rule?.DeepClone())]);
        if (GeneratedBattlegroups.AnyIn(rules) && MissingWeaponFile(inputs) is string missing)
            return Texts.Get("Files.Run.Generated.NoWeapons", ("file", missing));
        GeneratedListsMaker.Results made = GeneratedListsMaker.ResultsFor(rules, inputs, TextAfterRules, AppData.Current.Folders?.Cache);
        foreach (ModRules part in stack)
        {
            GeneratedUnitLists.Fill(part.Rules, made.Lists);
            GeneratedBattlegroups.Fill(part.Rules, made.Battlegroups, made.PacksInFile);
        }
        return null;
    }

    /// <summary>
    /// The first file a generated battlegroup's units are marked from (their weapons) that is not on the Files tab;
    /// null when all are. Without them, the battlegroup would be chosen otherwise than the Armory shows it.
    /// </summary>
    private static string? MissingWeaponFile(List<GeneratedListsMaker.Input> inputs) =>
        new[] { "WeaponDescriptor.ndf", "Ammunition.ndf", "AmmunitionMissiles.ndf" }
            .FirstOrDefault(file => !inputs.Any(input => string.Equals(input.Entry.Name, file, StringComparison.OrdinalIgnoreCase)));

    /// <summary>A file's text as the mods' rules make it, for the generated divisions' lists to be made from.</summary>
    private string TextAfterRules(GeneratedListsMaker.Input input)
    {
        string text = FileContent.Text(input.Entry);
        if (input.Stack.Count == 0) return text;
        (RunResult result, _) = RunStack(input.Stack, text, wantText: true, logLimit: 0, input.OtherFiles);
        return result.ParseError == "" && result.Text is not null ? result.Text : text;
    }

    // ---- dictionary files and the mod's own files ----

    /// <summary>
    /// Whether a file is a dictionary file (a .csv, such as UNITS.csv) that gets texts: from the Text rules of rule sets
    /// that apply to it, or, for UNITS.csv, from the Armory presets. It is then written with the texts added, not run by
    /// the NDF engine.
    /// </summary>
    internal static bool WritesDictionary(FileEntry entry) =>
        FileAndTextRules.IsDictionaryFile(entry.Name) && (HasDivisionRules(entry) || HasRuleSetsFor(entry.Name, entry.Folder));

    /// <summary>
    /// Whether a dictionary file goes into the mod's Localisation folder (GameData\Localisation\&lt;mod&gt;), the only
    /// place the game reads it from: UNITS.csv, and any .csv added from a Localisation folder. Another .csv is
    /// exported in the folders it was added with.
    /// </summary>
    internal static bool GoesIntoLocalisation(FileEntry entry) =>
        DivisionExtras.IsNamesFile(entry.Name) || entry.Folder.Split('/').Contains("Localisation", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The texts a dictionary file is given, mod by mod in the order they run: each mod's Text rules for it, then, for
    /// UNITS.csv, its Armory presets' texts (division names and the like). A token given twice takes the later text.
    /// </summary>
    private static List<DictionaryText> DictionaryTextsFor(FileEntry entry)
    {
        var texts = new List<DictionaryText>();
        bool byName = RuleSetsApplyByName;
        foreach (Mod mod in ModStack.Running)
        {
            texts.AddRange(FileAndTextRules.TextsOf(RuleSetFiles.For(entry.Name, entry.Folder, mod.Presets, byName).SelectMany(ruleSet => ruleSet.Rules)));
            if (DivisionExtras.IsNamesFile(entry.Name)) texts.AddRange(DivisionExtras.ArmoryTextsOf(ArmoryPresets.RulesOf(mod)));
        }
        return texts;
    }

    /// <summary>The places this export's File rules wrote, under each mod folder, so a file several exported files name is written once.</summary>
    private readonly HashSet<string> modFilesWritten = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What stopped File rules in this export, said once the export ends.</summary>
    private readonly List<string> modFileProblems = [];

    /// <summary>
    /// Writes the files the File rules that ran on an exported file carry, into the mod's folder: the folder that holds
    /// the GameData folder the file was exported into. Runs in the background, after the file itself.
    /// </summary>
    private void WriteModFiles(FileEntry entry, IReadOnlyList<FileAndTextRules.CarriedFile> files, string? gameMod)
    {
        if (files.Count == 0 || entry.IsError || entry.ExportFolder is null) return;
        string exportPath = ExportPathOf(entry, gameMod);
        if (FileAndTextRules.ModFolderOf(exportPath) is not string modFolder)
        {
            lock (modFileProblems) modFileProblems.Add(Texts.Get("Files.Run.ModFiles.NoGameData", ("path", exportPath)));
            return;
        }
        List<FileAndTextRules.CarriedFile> notYetWritten;
        lock (modFilesWritten)
            notYetWritten = [.. files.Where(file => modFilesWritten.Add(Path.Combine(modFolder, file.Destination.Replace('\\', '/'))))];
        try
        {
            FileAndTextRules.Outcome outcome = FileAndTextRules.WriteFiles(notYetWritten, modFolder, AppData.Current.ModFileStore,
                Texts.Get("Files.Run.ModFiles.BadPlace"), Texts.Get("Files.Run.ModFiles.Missing"));
            lock (modFileProblems) modFileProblems.AddRange(outcome.Problems);
            if (outcome.Problems.Count > 0)
                lock (modFilesWritten) modFilesWritten.ExceptWith(notYetWritten.Select(file => Path.Combine(modFolder, file.Destination.Replace('\\', '/'))));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            lock (modFileProblems) modFileProblems.Add(Texts.Get("Files.Run.ModFiles.Failed", ("problem", error.Message)));
        }
    }

    /// <summary>After an export: how many files the File rules wrote, or what stopped them. The counts start again for the next export.</summary>
    private void SayModFilesWritten()
    {
        int written;
        lock (modFilesWritten) written = modFilesWritten.Count;
        List<string> problems;
        lock (modFileProblems) problems = [.. modFileProblems.Distinct()];
        if (problems.Count > 0) model.Warn(string.Join(" ", problems));
        else if (written > 0) model.Say(Texts.Count("Files.Run.ModFiles.Written", written));
        lock (modFilesWritten) modFilesWritten.Clear();
        lock (modFileProblems) modFileProblems.Clear();
    }

    /// <summary>
    /// Files an earlier export wrote for File rules now switched off, or of a mod now off (and written by no rule that is
    /// on), are taken out of the mod's folder again, so they do not stay in the mod unseen. A file changed since, or put
    /// there by the user, is left alone. The Files tab says how many.
    /// </summary>
    private void RemoveModFilesNoLongerUsed(List<FileEntry> run)
    {
        List<JsonNode?> rules = RuleSetRulesOfEveryMod();
        if (!rules.Any(rule => FileAndTextRules.CarriedFileOf(rule) is not null)) return;
        string? modFolder = run.Where(entry => entry.ExportFolder is not null)
            .Select(entry => FileAndTextRules.ModFolderOf(ExportPathOf(entry))).FirstOrDefault(folder => folder is not null);
        if (modFolder is null) return;
        try
        {
            int removed = FileAndTextRules.RemoveFilesOfRulesOff(rules, modFolder);
            if (removed > 0) model.Say(Texts.Count("Files.Run.ModFiles.Removed", removed));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            model.Warn(Texts.Get("Files.Run.ModFiles.Failed", ("problem", error.Message)));
        }
    }

    /// <summary>
    /// The rules of every mod's rule sets, with those of the mods that are off copied as switched off. Their group
    /// headings are left out, so they do not switch off the rules of the mods that are on after them.
    /// </summary>
    private static List<JsonNode?> RuleSetRulesOfEveryMod()
    {
        var rules = new List<JsonNode?>();
        foreach (Mod mod in ModStack.Ordered)
        {
            bool isOn = ModStack.IsOn(mod);
            foreach (JsonNode? rule in mod.Presets.Where(preset => preset.ArmoryPart is null).SelectMany(preset => preset.Rules))
            {
                if (isOn) rules.Add(rule);
                else if (rule is not null && (string?)rule["Kind"] != "Group") rules.Add(SwitchedOff(rule));
            }
        }
        return rules;
    }

    // ---- the Armory presets ----

    /// <summary>
    /// The rules of the Armory presets of the mods that are on (<see cref="ModStack"/>), in the order they run; none
    /// when they have none.
    /// </summary>
    private static IReadOnlyList<JsonNode?> ArmoryRulesOfOpenMod => [.. ModStack.Running.SelectMany(ArmoryPresets.RulesOf)];

    /// <summary>Whether the Armory presets have rules for a file: it is then run, with or without a preset of its own.</summary>
    internal static bool HasDivisionRules(FileEntry entry) => HasDivisionRules(entry.Name);

    /// <summary>Whether the Armory presets have rules for a file of this name, such as "Divisions.ndf" or "UNITS.csv".</summary>
    internal static bool HasDivisionRules(string fileName)
    {
        IReadOnlyList<JsonNode?> rules = ArmoryRulesOfOpenMod;
        return rules.Count > 0 && HasRulesFor(rules, fileName);
    }

    /// <summary>
    /// The names of the files the open mod's Armory presets have rules for, UNITS.csv among them when they give texts:
    /// worked out once for a whole file list, as <see cref="HasDivisionRules(string)"/> for each of thousands of files
    /// was slow.
    /// </summary>
    internal static IReadOnlySet<string> FilesWithArmoryRules()
    {
        IReadOnlyList<JsonNode?> rules = ArmoryRulesOfOpenMod;
        HashSet<string> files = DivisionChangeRules.FilesOf(rules);
        if (rules.Any(DivisionExtras.NeedsNamesFile)) files.Add(DivisionExtras.NamesFile);
        return files;
    }

    /// <summary>One mod's part of what runs on a file: the mod, its rule sets for the file (none when only its Armory presets have rules for it), and the rules.</summary>
    internal sealed record ModRules(Mod Mod, IReadOnlyList<ModPreset> RuleSets, JsonArray Rules);

    /// <summary>
    /// The setting "Apply rule sets by file name": whether a rule set with no files of its own applies to the files its
    /// name holds (see <see cref="RuleSetFiles"/>). On unless turned off.
    /// </summary>
    internal static bool RuleSetsApplyByName => AppData.Current.Settings["autoAssign"]?.GetValue<bool>() != false;

    /// <summary>
    /// The rule sets that run on a file, by name, for Details' heading: each mod's in turn; the Armory presets' when
    /// they alone run.
    /// </summary>
    private static string RuleSetNames(List<ModRules> stack)
    {
        List<string> names = [.. stack.SelectMany(part => part.RuleSets).Select(ruleSet => ruleSet.Name)];
        if (names.Count == 0) names = [.. stack.SelectMany(part => ArmoryPresets.AllOf(part.Mod)).Select(preset => preset.Name).Distinct()];
        return string.Join(", ", names);
    }

    /// <summary>
    /// What runs on a file, mod by mod, for each mod that is on, in the Mods tab's order (<see cref="ModStack"/>): the
    /// rules of its rule sets that apply to the file (<see cref="RuleSetFiles"/>), in its list order, then its Armory
    /// presets' rules for the file, so its Armory changes come after its other changes to the same file. Mods with no
    /// rules for the file are left out. Every rule shows in Details.
    /// </summary>
    internal static List<ModRules> StackFor(FileEntry entry)
    {
        var stack = new List<ModRules>();
        bool byName = RuleSetsApplyByName;
        foreach (Mod mod in ModStack.Running)
        {
            List<ModPreset> ruleSets = RuleSetFiles.For(entry.Name, entry.Folder, mod.Presets, byName);
            var rules = new JsonArray();
            foreach (ModPreset ruleSet in ruleSets)
                foreach (JsonNode? rule in ruleSet.Rules) rules.Add(rule?.DeepClone());
            foreach (JsonNode? rule in DivisionChangeRules.ForFile(ArmoryPresets.RulesOf(mod), entry.Name)) rules.Add(rule!.DeepClone());
            if (rules.Count > 0) stack.Add(new ModRules(mod, ruleSets, rules));
        }
        return stack;
    }

    /// <summary>
    /// The mods that run on a file, in the order they run, each with its rule sets for it: what the Files tab's Mods
    /// column shows. Cheaper than <see cref="StackFor"/>, which copies the rules, for a list of thousands.
    /// </summary>
    /// <param name="armoryFiles">The files the Armory presets of each mod that is on have rules for, by mod (see <see cref="ArmoryFilesByMod"/>).</param>
    internal static List<ModOnFile> ModsOn(FileEntry entry, IReadOnlyList<(Mod Mod, IReadOnlySet<string> Files)> armoryFiles)
    {
        var mods = new List<ModOnFile>();
        bool byName = RuleSetsApplyByName;
        foreach ((Mod mod, IReadOnlySet<string> files) in armoryFiles)
        {
            List<string> ruleSets = [.. RuleSetFiles.For(entry.Name, entry.Folder, mod.Presets, byName).Select(ruleSet => ruleSet.Name)];
            bool hasArmoryChanges = files.Contains(entry.Name);
            if (ruleSets.Count > 0 || hasArmoryChanges) mods.Add(new ModOnFile(mod.Name, ruleSets, hasArmoryChanges));
        }
        return mods;
    }

    /// <summary>Each mod that is on, in the order they run, with the files its Armory presets have rules for (UNITS.csv among them when they give texts).</summary>
    internal static List<(Mod Mod, IReadOnlySet<string> Files)> ArmoryFilesByMod()
    {
        var byMod = new List<(Mod, IReadOnlySet<string>)>();
        foreach (Mod mod in ModStack.Running)
        {
            IReadOnlyList<JsonNode?> rules = ArmoryPresets.RulesOf(mod);
            HashSet<string> files = DivisionChangeRules.FilesOf(rules);
            if (rules.Any(DivisionExtras.NeedsNamesFile)) files.Add(DivisionExtras.NamesFile);
            byMod.Add((mod, files));
        }
        return byMod;
    }

    /// <summary>A mod's rule sets that apply to a file, in the mod's list order (<see cref="RuleSetFiles"/>).</summary>
    internal static List<ModPreset> RuleSetsFor(FileEntry entry, Mod mod) => RuleSetFiles.For(entry.Name, entry.Folder, mod.Presets, RuleSetsApplyByName);

    /// <summary>Whether a rule set applies to a file.</summary>
    internal static bool AppliesTo(ModPreset ruleSet, FileEntry entry) =>
        AppData.Current.ModOf(ruleSet) is Mod mod && RuleSetsFor(entry, mod).Contains(ruleSet);

    /// <summary>Whether a mod that is on has rule sets that apply to a file of this name and folder.</summary>
    internal static bool HasRuleSetsFor(string fileName, string folder)
    {
        bool byName = RuleSetsApplyByName;
        return ModStack.Running.Any(mod => RuleSetFiles.For(fileName, folder, mod.Presets, byName).Count > 0);
    }

    /// <summary>Every rule run on a file, the mods' one after the other (<see cref="StackFor"/>).</summary>
    internal static JsonArray RulesToRun(FileEntry entry)
    {
        var rules = new JsonArray();
        foreach (ModRules part in StackFor(entry))
            foreach (JsonNode? rule in part.Rules) rules.Add(rule!.DeepClone());
        return rules;
    }

    /// <summary>
    /// How many rules each part of the Armory runs on a file of this name, its headings left out; the parts with none
    /// are left out.
    /// </summary>
    internal static IReadOnlyDictionary<ArmoryPart, int> ArmoryRuleCountsFor(string fileName)
    {
        var counts = new Dictionary<ArmoryPart, int>();
        foreach (ModPreset preset in ModStack.Running.SelectMany(ArmoryPresets.AllOf))
        {
            int count = DivisionExtras.IsNamesFile(fileName)
                ? preset.Rules.Count(DivisionExtras.NeedsNamesFile)
                : DivisionChangeRules.ForFile(preset.Rules, fileName).Count(rule => (string?)rule?["Kind"] != "Group");
            if (count > 0 && preset.ArmoryPart is ArmoryPart part) counts[part] = counts.GetValueOrDefault(part) + count;
        }
        return counts;
    }

    /// <summary>What a file is sorted by in the Armory column: the letters of the parts with rules for it, such as "DU".</summary>
    internal static string ArmorySortKey(string fileName) =>
        string.Concat(ArmoryRuleCountsFor(fileName).Keys.OrderBy(part => part).Select(Rules.ArmoryLetterTexts.Letter));

    /// <summary>The parts of the Armory whose presets have rules for a file of this name, in the order they run.</summary>
    internal static IReadOnlyList<ArmoryPart> ArmoryPartsFor(string fileName) =>
        [.. ModStack.Running.SelectMany(ArmoryPresets.AllOf).Where(preset => HasRulesFor(preset.Rules, fileName)).Select(preset => preset.ArmoryPart!.Value).Distinct()];

    /// <summary>Whether rules of the Armory are for a file of this name; for UNITS.csv, whether they give it texts.</summary>
    private static bool HasRulesFor(IEnumerable<JsonNode?> rules, string fileName) =>
        DivisionExtras.IsNamesFile(fileName) ? rules.Any(DivisionExtras.NeedsNamesFile) : DivisionChangeRules.FilesOf(rules).Contains(fileName);

    /// <summary>
    /// After an export: the Armory's pictures of the user's own (division emblems, unit card pictures) copied into the
    /// GameData folder of a file with picture rules just exported, such as Divisions.ndf. The Files tab says how many,
    /// or what stopped it.
    /// </summary>
    private void WriteDivisionExtras(List<FileEntry> run)
    {
        RemovePicturesNoLongerUsed(run);
        IReadOnlyList<JsonNode?> armoryRules = ArmoryRulesOfOpenMod;
        if (armoryRules.Count == 0) return;
        HashSet<string> filesWithPictures = DivisionExtras.FilesWithPictures(armoryRules);
        FileEntry? exported = run.FirstOrDefault(entry => entry.Done && entry.ExportedPath is not null && filesWithPictures.Contains(entry.Name));
        if (exported is null) return;
        // a picture kept before the app made every picture a plain .png may be one the game's tools refuse
        foreach (string picture in DivisionExtras.PictureFiles(armoryRules, PictureFolders).Where(File.Exists))
            if (!Divisions.GameReadyPicture.MakeReady(picture)) model.Warn(Texts.Get("Files.Run.Divisions.PictureNotReady", ("file", picture)));
        try
        {
            DivisionExtras.Outcome outcome = DivisionExtras.CopyPictures(armoryRules, exported.ExportedPath!, PictureFolders,
                Texts.Get("Files.Run.Divisions.NoGameData"), Texts.Get("Files.Run.Divisions.PictureMissing"));
            if (outcome.Problem != "") model.Warn(outcome.Problem);
            else if (outcome.Pictures > 0) model.Say(Texts.Get("Files.Run.Divisions.Written", ("pictures", outcome.Pictures)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            model.Warn(Texts.Get("Files.Run.Divisions.Failed", ("problem", error.Message)));
        }
    }

    /// <summary>
    /// The Armory presets' rules of every mod, with those of the mods that are off copied as switched off. Their group
    /// headings are left out, so they do not switch off the rules of the mods that are on after them.
    /// </summary>
    private static List<JsonNode?> ArmoryRulesOfEveryMod()
    {
        var rules = new List<JsonNode?>();
        foreach (Mod mod in ModStack.Ordered)
        {
            bool isOn = ModStack.IsOn(mod);
            foreach (JsonNode? rule in ArmoryPresets.RulesOf(mod))
            {
                if (isOn) rules.Add(rule);
                else if (rule is not null && (string?)rule["Kind"] != "Group") rules.Add(SwitchedOff(rule));
            }
        }
        return rules;
    }

    /// <summary>A copy of a rule, switched off.</summary>
    private static JsonNode SwitchedOff(JsonNode rule)
    {
        JsonNode copy = rule.DeepClone();
        copy["Enabled"] = false;
        return copy;
    }

    /// <summary>The folders the app keeps chosen pictures in: the only ones a mod's pictures are copied from.</summary>
    private static IReadOnlyList<string> PictureFolders =>
        AppFolders.PictureFoldersOf(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// Pictures an earlier export copied for rules now switched off, or of a mod now off (and used by no rule that is
    /// on), are taken out of the export's GameData folder again, so they do not stay in the mod unseen. The Files tab
    /// says how many.
    /// </summary>
    private void RemovePicturesNoLongerUsed(List<FileEntry> run)
    {
        List<JsonNode?> armoryRules = ArmoryRulesOfEveryMod();
        if (armoryRules.Count == 0) return;
        string? gameData = run.Where(entry => entry.Done && entry.ExportedPath is not null)
            .Select(entry => DivisionExtras.GameDataFolderOf(entry.ExportedPath!)).FirstOrDefault(folder => folder is not null);
        if (gameData is null) return;
        try
        {
            int removed = DivisionExtras.RemovePicturesOfRulesOff(armoryRules, gameData);
            if (removed > 0) model.Say(Texts.Count("Files.Run.PicturesRemoved", removed));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            model.Warn(Texts.Get("Files.Run.Divisions.Failed", ("problem", error.Message)));
        }
    }

    /// <summary>
    /// The WARNO mod UNITS.csv is exported for, by its folder name: the game reads it only from
    /// GameData\Localisation\&lt;that name&gt;. Found from the export folder when it is in a mod's folder (…\Mods\WTO\…);
    /// otherwise the name kept for the open mod, asked for (and kept) the first time. Null when the user cancels.
    /// </summary>
    private async Task<string?> GameModFor(string exportFolder)
    {
        if (DivisionExtras.GameModOf(exportFolder) is string found) return found;
        string modName = AppData.Current.OpenMod.Name;
        if (KeptGameMod(modName) is string kept) return kept;
        if (AskGameMod is null || await AskGameMod(modName) is not string typed || typed == "") return null;
        KeepGameMod(modName, typed);
        return typed;
    }

    /// <summary>The WARNO mod folder kept for an EasyNDF mod's UNITS.csv; null when none is.</summary>
    public static string? KeptGameMod(string modName) => (string?)AppData.Current.State["gameMods"]?[modName];

    /// <summary>Keeps the WARNO mod folder an EasyNDF mod's UNITS.csv goes into, for its next exports.</summary>
    public static void KeepGameMod(string modName, string gameMod)
    {
        if (AppData.Current.State["gameMods"] is not JsonObject kept) AppData.Current.State["gameMods"] = kept = [];
        kept[modName] = gameMod;
        AppData.Current.SaveState();
        AppData.Current.TellModFoldersChanged();
    }

    /// <summary>Runs one file's preset, and writes the result when exporting. It runs in the background.</summary>
    private void RunOne(FileEntry entry, bool write, bool everyFile, FileWork work)
    {
        try
        {
            if (work.IsNamesFile)
            {
                string? gameMod = GoesIntoLocalisation(entry) ? namesFileMod : null;
                RunNamesFile(entry, write, gameMod, work.Texts);
                if (write) WriteModFiles(entry, work.ModFiles, gameMod);
                return;
            }
            List<ModRules> stack = work.Stack;
            if (stack.Count == 0)
            {
                RunWithModsOff(entry, write, everyFile, work.OpenModName);
                return;
            }
            Mod? mod = work.HeaderMod;
            Watermark? watermark = WatermarkOf(mod);
            var clock = Stopwatch.StartNew();
            if (FillGeneratedLists(stack, work.GeneratedInputs) is string cannot)
            {
                entry.Message = cannot;
                entry.IsError = true;
                return;
            }
            (RunResult result, LoadSummary info) = RunStack(stack, FileContent.Text(entry), write, settings.LogLimit, work.OtherFiles);
            clock.Stop();
            lock (conflictsFound) conflictsFound[entry.Key] = ConflictsIn(entry.Name, stack, result);

            string? text = result.Text;
            if (write && text is not null)
            {
                // The export comes back with the source's line endings; the setting decides what is saved.
                text = ExportFinishing.WithLineEndings(text, settings.LineEndings);
                if (result.Edits > 0 && result.ParseError == "")
                    text = ExportFinishing.AddHeader(text, MainView.AppVersion, settings.EditedWithLine, mod?.Name, watermark);
            }

            entry.Scanned = true;
            entry.Lines = info.Lines;
            entry.Info = FileStatus.Holds(info.Descriptors, info.Constants, info.Unnamed);
            entry.LastRun = new RunRecord
            {
                Result = ForDetails(result),
                // Details shows only the counts; the names the file declares are not kept with every file run
                Info = info with { Names = [], Titles = [], Types = [], References = [] },
                Milliseconds = clock.ElapsedMilliseconds,
                RuleSets = work.RuleSets,
                Held = write && result.ParseError != "",
                Unchanged = write && result.ParseError == "" && result.Edits == 0,
            };
            entry.Edits = result.Edits;
            entry.WarningCount = result.Warnings.Count;
            // When nothing at all changed, the status already says so.
            entry.IdleRuleCount = result.Edits > 0 ? result.Stats.Count(RunRecord.IsIdle) : 0;

            string edits = Texts.Count("Files.Run.Edits", result.Edits, ("count", FileStatus.Count(result.Edits)));
            if (result.ParseError != "")
            {
                // held back: not written to the export folder
                entry.IsError = true;
                if (write) entry.HeldOutput = text;
                entry.Message = write ? Texts.Get("Files.Run.InvalidNotExported", ("problem", result.ParseError)) : Texts.Get("Files.Run.InvalidPreview", ("problem", result.ParseError));
            }
            else if (!write) entry.Message = Texts.Get("Files.Run.Preview", ("edits", edits));
            else if (result.Edits == 0 && !everyFile && HoldsEarlierExport(entry))
                Deliver(entry, FileContent.Bytes(entry), Texts.Get("Files.Run.Restored"));
            else if (result.Edits == 0 && !everyFile) entry.Message = Texts.Get("Files.Run.NoChanges");
            else if (result.Edits == 0) Deliver(entry, FileContent.Bytes(entry), Texts.Get("Files.Run.UnchangedCopyNoChanges"));
            else Deliver(entry, System.Text.Encoding.UTF8.GetBytes(text!), edits);
            if (write) WriteModFiles(entry, work.ModFiles, null);
        }
        catch (Exception error)
        {
            // Whatever goes wrong with one file is said on its row, and the run goes on with the next, as in the HTML app.
            entry.Message = Texts.Get("Files.Run.Failed", ("problem", error.Message));
            entry.IsError = true;
        }
    }

    /// <summary>
    /// A file no mod that is on has rules for: its preset's mod is off on the Mods tab. With every file exported, it is
    /// copied as it is. Otherwise a copy an earlier export edited would stay in the mod, its changes unseen, so the
    /// file as it is goes over it, as when a mod's rules are all switched off (<see cref="HoldsEarlierExport"/>).
    /// </summary>
    private static void RunWithModsOff(FileEntry entry, bool write, bool everyFile, string openModName)
    {
        if (write && everyFile) Deliver(entry, FileContent.Bytes(entry), Texts.Get("Files.Run.UnchangedCopy"));
        else if (write && HoldsEarlierExport(entry)) Deliver(entry, FileContent.Bytes(entry), Texts.Get("Files.Run.Restored"));
        else entry.Message = Texts.Get("Files.Run.ModOff", ("mod", openModName));
    }

    // ---- running the mods one after the other ----

    /// <summary>What each file run found of mods changing the same values, by the file's key, given to the Mods tab when the run ends.</summary>
    private readonly Dictionary<string, List<ModConflict>> conflictsFound = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs each mod's rules on a file, one mod after the other, each on what the one before gave (<see cref="StackFor"/>).
    /// A mod's own name goes into the comments its rules write. With one mod it is one run, as before mods could run
    /// together. Stops at a mod whose rules give a file that does not read back. The results are put together: every
    /// rule's counts and edits, in the order they ran, and the last text.
    /// </summary>
    private (RunResult Result, LoadSummary Info) RunStack(List<ModRules> stack, string text, bool wantText, int logLimit, Dictionary<string, string> otherFiles)
    {
        LoadSummary? info = null;
        var results = new List<RunResult>();
        for (int at = 0; at < stack.Count; at++)
        {
            ModRules part = stack[at];
            var engine = new NdfEngine();
            LoadSummary loaded = engine.Load(text);
            info ??= loaded;
            RunResult result = engine.Run(RuleLoading.LoadForEngine(part.Rules), new RunOptions
            {
                // a mod before the last needs its text, for the next mod to run on
                WantText = wantText || at < stack.Count - 1,
                LogLimit = logLimit,
                OtherFiles = otherFiles,
                KeepSourceComments = settings.KeepSourceComments,
                CheckOutput = true,
                ModName = ExportFinishing.ModNameForComments(part.Mod.Name, WatermarkOf(part.Mod)),
                DecimalPlaces = settings.DecimalPlaces,
            });
            results.Add(result);
            if (result.ParseError != "" || result.Text is null) break;
            text = result.Text;
        }
        return (Together(results), info!);
    }

    /// <summary>Several runs as one result: their rules' counts, edits and warnings in order, and the last run's text and error.</summary>
    private static RunResult Together(List<RunResult> results)
    {
        if (results.Count == 1) return results[0];
        return new RunResult
        {
            Text = results[^1].Text,
            ParseError = results[^1].ParseError,
            Stats = [.. results.SelectMany(result => result.Stats)],
            Logs = [.. results.SelectMany(result => result.Logs)],
            Warnings = [.. results.SelectMany(result => result.Warnings)],
        };
    }

    /// <summary>
    /// The mod a file's export header names, with its watermark: the open mod when it ran on the file, otherwise the
    /// last mod that did.
    /// </summary>
    private static Mod HeaderModOf(List<ModRules> stack) =>
        stack.FirstOrDefault(part => ReferenceEquals(part.Mod, AppData.Current.OpenMod))?.Mod ?? stack[^1].Mod;

    /// <summary>
    /// The values more than one mod changed in a file, from each rule's edit log (the edits past the log's limit are
    /// not seen). The rules' logs follow the mods' order, as they ran.
    /// </summary>
    private static List<ModConflict> ConflictsIn(string fileName, List<ModRules> stack, RunResult result)
    {
        if (stack.Count < 2) return [];
        var runs = new List<(string Mod, IEnumerable<(string Descriptor, string Property)> Changed)>();
        int rule = 0;
        foreach (ModRules part in stack)
        {
            int count = Math.Min(RuleLoading.LoadForEngine(part.Rules).Count, result.Logs.Count - rule);
            IEnumerable<(string, string)> changed = result.Logs.Skip(rule).Take(count).SelectMany(log => log).Select(edit => (edit.Descriptor, edit.Target)).ToList();
            runs.Add((part.Mod.Name, changed));
            rule += count;
        }
        return ModConflictFinder.Find(fileName, runs);
    }

    /// <summary>
    /// A dictionary file such as UNITS.csv, with the texts of the Text rules and of the Armory presets (division names,
    /// a new division's summary and history...): a new one is written into the export folder, as an exported .ndf file
    /// is; the one on the Files tab is left as it is.
    /// </summary>
    /// <param name="gameMod">The WARNO mod it is for, by its folder name: it goes into Localisation\&lt;that name&gt;; null keeps its folders.</param>
    /// <param name="dictionaryTexts">The texts it is given (<see cref="DictionaryTextsFor"/>).</param>
    private static void RunNamesFile(FileEntry entry, bool write, string? gameMod, IReadOnlyList<DictionaryText> dictionaryTexts)
    {
        byte[] source = FileContent.Bytes(entry);
        (string text, int count) = DivisionExtras.WithTexts(FileContent.Text(entry), dictionaryTexts);
        string texts = Texts.Count("Files.Run.DivisionTexts", count, ("count", FileStatus.Count(count)));
        if (!write)
        {
            entry.Message = Texts.Get("Files.Run.Preview", ("edits", texts));
            return;
        }
        // the file's own byte order mark is kept, or left out when it had none
        bool hasMark = source.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]);
        byte[] mark = hasMark ? [0xEF, 0xBB, 0xBF] : [];
        Deliver(entry, [.. mark, .. System.Text.Encoding.UTF8.GetBytes(text)], gameMod is null ? texts : Texts.Get("Files.Run.NamesInto", ("texts", texts), ("mod", gameMod)), gameMod);
    }

    /// <summary>
    /// What a file's preset would export now (the File Editor's "Show export"): the rules run on the given text, with
    /// the header lines added when they edit something. Nothing is written, and the file's row is left as it is.
    /// </summary>
    /// <param name="entry">The file, which has a preset, or rules of the Armory presets.</param>
    /// <param name="text">The text to run the rules on: the file's, or the edits typed in the editor.</param>
    /// <returns>What the engine gave back, and the text it would export.</returns>
    public (RunResult Result, string Text) WouldExport(FileEntry entry, string text)
    {
        List<ModRules> stack = StackFor(entry);
        if (stack.Count == 0) return (new RunResult { Text = text, Stats = [], Logs = [], Warnings = [] }, text);
        List<GeneratedListsMaker.Input>? generated = HasGeneratedRules(stack)
            ? GeneratedListsMaker.InputsFrom(model.Entries) ?? []
            : null;
        if (FillGeneratedLists(stack, generated) is string cannot)
            return (new RunResult { Text = text, ParseError = cannot, Stats = [], Logs = [], Warnings = [] }, text);
        Mod mod = HeaderModOf(stack);
        Watermark? watermark = WatermarkOf(mod);
        (RunResult result, _) = RunStack(stack, text, wantText: true, logLimit: 0, OtherFilesFor(entry, model.Entries));
        string exported = result.Text ?? "";
        if (result.Edits > 0 && result.ParseError == "")
            exported = ExportFinishing.AddHeader(exported, MainView.AppVersion, settings.EditedWithLine, mod.Name, watermark);
        return (result, exported);
    }

    /// <summary>
    /// Writes a finished file into its export folder, in the folders it sat in when it was added from a folder.
    /// </summary>
    /// <param name="what">What the status says was written, such as "12 edits" or "unchanged copy".</param>
    /// <param name="gameMod">For UNITS.csv, the WARNO mod it is for: it goes into Localisation\&lt;that name&gt;.</param>
    private static void Deliver(FileEntry entry, byte[] content, string what, string? gameMod = null)
    {
        string exportFolder = entry.ExportFolder!;
        string path = ExportPathOf(entry, gameMod);
        string folder = Path.GetDirectoryName(path)!;
        string shownName = FileListView.FolderName(exportFolder);
        // a file in an archive has no file on disk to overwrite; one sent from the File Editor's "Use in file list" still does
        bool isOnDisk = !entry.Source!.Contains(" > ", StringComparison.Ordinal);
        if (isOnDisk && string.Equals(Path.GetFullPath(path), Path.GetFullPath(entry.Source), StringComparison.OrdinalIgnoreCase))
        {
            entry.Message = Texts.Get("Files.Run.FolderHoldsSource");
            entry.IsError = true;
            return;
        }
        // the folders the file was added with, before UNITS.csv is moved to the mod's Localisation folder on purpose
        if (!IsInside(ExportPathOf(entry), exportFolder))
        {
            entry.Message = Texts.Get("Files.Run.OutsideFolder");
            entry.IsError = true;
            return;
        }
        try
        {
            Directory.CreateDirectory(folder);
            // "Back up overwritten files": the file this export replaces is kept beside it first
            if (AppData.Current.Settings["backupOverwritten"]?.GetValue<bool>() == true && File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            WriteWhole(path, content);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            entry.Message = Texts.Get("Files.Run.WriteFailed", ("folder", shownName), ("reason", error.Message));
            entry.IsError = true;
            return;
        }
        if (entry.LastRun is not null) entry.LastRun.Wrote = true;
        entry.Done = true;
        entry.ExportedPath = path;
        entry.Message = Texts.Get("Files.Run.Exported", ("folder", shownName), ("what", what));
    }

    /// <summary>
    /// Writes a file whole or not at all: into a file beside it first, which then takes its place. A crash, a full disk
    /// or the app being closed half way then never leaves half a file in the mod.
    /// </summary>
    private static void WriteWhole(string path, byte[] content)
    {
        string partial = path + ".easyndf-partial";
        try
        {
            File.WriteAllBytes(partial, content);
            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    /// <summary>
    /// Whether a path lies inside a folder, once ".." and the like are worked out. A file's folders come from where it
    /// was added, and must never take an export outside the export folder.
    /// </summary>
    private static bool IsInside(string path, string folder)
    {
        string fullFolder = Path.GetFullPath(folder);
        if (!Path.EndsInDirectorySeparator(fullFolder)) fullFolder += Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Where a file is exported: its export folder, in the folders it sat in when it was added from a folder.</summary>
    /// <param name="gameMod">For UNITS.csv, the WARNO mod it is for: it goes into Localisation\&lt;that name&gt;.</param>
    private static string ExportPathOf(FileEntry entry, string? gameMod = null)
    {
        string exportFolder = entry.ExportFolder!;
        string folder = entry.Folder == "" ? exportFolder : Path.Combine([exportFolder, .. entry.Folder.Split('/')]);
        string path = Path.Combine(folder, entry.Name);
        return gameMod is null ? path : DivisionExtras.NamesFilePathFor(path, gameMod);
    }

    /// <summary>
    /// Whether the export folder holds a copy of the file that an earlier export edited (it starts with the "Edited with
    /// EasyNDF" line), when its rules now change nothing, as when they are all switched off. That copy would stay in
    /// the mod, its changes unseen: the export writes the file as it is over it instead. A copy without the line, or
    /// one the user wrote, is left alone.
    /// </summary>
    private static bool HoldsEarlierExport(FileEntry entry)
    {
        if (entry.ExportFolder is null) return false;
        string path = ExportPathOf(entry);
        if (!File.Exists(path)) return false;
        try
        {
            using var reader = new StreamReader(path);
            char[] start = new char[1000];
            int read = reader.ReadBlock(start, 0, start.Length);
            return ExportStamp.Of(new string(start, 0, read)) is not null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The other listed files the Lookup rules run on a file read, by the name the rule gives (the HTML app's <c>jobByRef</c>):
    /// a file whose folder and name are that name, or end with it.
    /// </summary>
    internal static Dictionary<string, string> OtherFilesFor(FileEntry entry, IEnumerable<FileEntry> entries) => OtherFilesFor(RulesToRun(entry), entries);

    /// <summary>The other listed files these rules' Lookup rules read, by the name each rule gives.</summary>
    internal static Dictionary<string, string> OtherFilesFor(IEnumerable<JsonNode?> rules, IEnumerable<FileEntry> entries)
    {
        var found = new Dictionary<string, string>();
        List<FileEntry>? loaded = null;
        foreach (JsonNode? rule in rules)
        {
            if ((string?)rule?["Kind"] != "Lookup" || rule["Enabled"]?.GetValue<bool>() == false || rule["Params"] is not JsonObject parameters) continue;
            string key = ((string?)parameters["SourceFile"] ?? "").Trim().ToLowerInvariant();
            if (key == "" || found.ContainsKey(key)) continue;
            loaded ??= entries.Where(e => e.Source is not null).ToList();
            FileEntry? source = loaded.FirstOrDefault(e => e.Key == key) ?? loaded.FirstOrDefault(e => e.Key.EndsWith("/" + key, StringComparison.Ordinal));
            if (source is null) continue;
            try { found[key] = FileContent.Text(source); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return found;
    }

    /// <summary>
    /// A run's result as a file's Details shows it: its counts, edits, warnings and any error, without the exported
    /// text, which is saved or held already and would otherwise stay in memory for every file run.
    /// </summary>
    private static RunResult ForDetails(RunResult result) =>
        new() { Stats = result.Stats, Logs = result.Logs, Warnings = result.Warnings, ParseError = result.ParseError };

    /// <summary>A mod's watermark as the engine takes it, or null when it has none.</summary>
    private static Watermark? WatermarkOf(Mod? mod) =>
        mod?.Watermark is ModWatermark mark ? new Watermark(mark.On, mark.Author, mark.Version, mark.Game) : null;
}
