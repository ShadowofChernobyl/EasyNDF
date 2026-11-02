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
                // files added from other folders of the same GameData (its Generated folder, and GameData itself for
                // MODDING.csv) each go into their own folder beside the others, as in the game's mod, not all into one
                if (withoutFolder.Select(ExportGameDataOf).FirstOrDefault(found => found is not null) is string gameData)
                    foreach (FileEntry entry in withoutFolder)
                        if (AddedFromInGameData(entry) is string added && Path.Combine(gameData, added) is string own
                            && !string.Equals(Path.TrimEndingDirectorySeparator(own), Path.TrimEndingDirectorySeparator(entry.ExportFolder!), StringComparison.OrdinalIgnoreCase))
                            model.SetExportFolder([entry], own);
            }
            // (Asking about the mod's watermark first, when it says to ask, comes with the watermark window.)
            FileEntry? namesFile = list.FirstOrDefault(entry => WritesDictionary(entry) && GoesIntoGameModFolder(entry));
            dictionariesToMake = DictionariesToMake(list);
            // a dictionary on the Files tab, or one the export makes, goes into the WARNO mod's Localisation folder;
            // Steel Division 2's goes into GameData\Localisation itself, so no mod folder is asked for it
            bool makesWarnoDictionary = dictionariesToMake.Any(dictionary => dictionary.Game == Data.Game.GameTitle.Warno);
            string? dictionaryExportFolder = namesFile?.ExportFolder ?? (makesWarnoDictionary ? list.Select(entry => entry.ExportFolder).FirstOrDefault(folder => folder is not null) : null);
            if (dictionaryExportFolder is not null)
            {
                namesFileMod = await GameModFor(dictionaryExportFolder);
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
            if (write)
            {
                await GiveDeckNumbers(list);
                // DeckSerializer.ndf may run now only for the rules just added: it goes where the other files go
                foreach (FileEntry entry in FilesThatRun(model.Entries, everyFile).Where(e => skipped?.Contains(e) != true).Except(list).ToList())
                {
                    if (entry.ExportFolder is null && list.Select(each => each.ExportFolder).FirstOrDefault(folder => folder is not null) is string folder)
                        model.SetExportFolder([entry], folder);
                    list.Add(entry);
                }
            }
            model.Say("");
            // Every file's work is taken now, before the first one runs: its rules and where its text is. A rule or a
            // file changed meanwhile (on the Mods tab, in the Armory or the File Editor) then reaches the next export
            // whole, not just the files this one has not reached yet.
            var works = new Dictionary<FileEntry, FileWork>();
            // the files generated divisions are made from are worked out once for each game, for every file that makes them
            var generatedInputs = new Dictionary<Data.Game.GameTitle, List<GeneratedListsMaker.Input>>();
            foreach (FileEntry entry in list) works[entry] = WorkFor(entry, model.Entries, generatedInputs);
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
                FileWork work = works[entry];
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
            MakeDictionaries(dictionariesToMake, finished);
            if (deckNumbersGiven.Count > 0)
                model.SayAlso(Texts.Get("Files.Run.DeckNumbersGiven", ("units", string.Join(", ", deckNumbersGiven))));
        }
    }

    /// <summary>The units this export gave a number for deck codes (<see cref="GiveDeckNumbers"/>), by name, for the note after it.</summary>
    private List<string> deckNumbersGiven = [];

    /// <summary>
    /// Before Run export writes anything: each unit a mod that is on uses in its divisions (set by hand, or in the lists
    /// of its generated divisions) that has no number for deck codes is given the next free one, kept in the mod
    /// (<see cref="Mod.UnitDeckNumbers"/>) so it stays the same at every export, with its rule in the mod's Units
    /// preset. The game refuses a deck with a unit missing from DeckSerializer.ndf; units the Armory makes have a
    /// number from the start, but units a mod's rules add (Clone rules) have none. The units are read from the files
    /// on the Files tab as the mods' rules make them, as the generated divisions' lists are, so no source needs to be
    /// read in the Armory; nothing is given when UniteDescriptor.ndf is not on the Files tab. Only the mods of the
    /// games of the files exported take part, and the units are read from those games' files.
    /// </summary>
    /// <param name="run">The files the export runs.</param>
    private async Task GiveDeckNumbers(List<FileEntry> run)
    {
        model.ShowStep(Texts.Get("Files.Run.Card.DeckNumbers"));
        deckNumbersGiven = [];
        List<Mod> mods = [.. ModStack.RunningOn(run).Where(mod => mod.Divisions.Count > 0 || mod.UnitDeckNumbers.Count > 0)];
        if (mods.Count == 0) return;
        // numbers a mod file carries without their rules get them first, so the units are read with those numbers
        foreach (Mod mod in mods.Where(mod => mod.UnitDeckNumbers.Count > 0)) AddDeckNumberRules(mod, Divisions.BattlegroupView.ShortName);
        // each game's units are read from its own files, and numbered among its own mods' units
        foreach (IGrouping<Data.Game.GameTitle, Mod> ofGame in mods.GroupBy(mod => mod.Game))
            await GiveDeckNumbers([.. ofGame], ofGame.Key);
    }

    /// <summary>The numbers for deck codes the units of one game's mods need (see <see cref="GiveDeckNumbers(List{FileEntry})"/>).</summary>
    /// <param name="mods">The game's mods that are on, with divisions or numbers of their own.</param>
    /// <param name="game">The game.</param>
    private async Task GiveDeckNumbers(List<Mod> mods, Data.Game.GameTitle game)
    {
        List<GeneratedListsMaker.Input>? inputs = GeneratedListsMaker.InputsFrom(model.Entries, game);
        if (inputs is null) return;
        var rules = new JsonArray([.. mods.SelectMany(mod => ArmoryPresets.RulesOf(mod)).Select(rule => rule?.DeepClone())]);
        string? cache = AppData.Current.Folders?.Cache;
        // the units come with the generated lists, so when nothing has changed since the last export both are read
        // from the cache instead of running the rules on every unit file again
        GeneratedListsMaker.Results made = await Task.Run(() => GeneratedListsMaker.ResultsFor(rules, inputs, TextAfterRules, cache));

        Dictionary<string, GeneratedListsMaker.KnownUnit> units = made.Units;
        List<Mod> gameMods = [.. AppData.Current.Mods.Where(mod => mod.Game == game)];
        bool Numbered(string unit) => gameMods.Any(mod => mod.UnitDeckNumbers.ContainsKey(unit) || mod.Units.Any(own => own.Descriptor == unit));
        int next = units.Values.Select(unit => unit.DeckNumber ?? 0)
            .Concat(gameMods.SelectMany(mod => mod.Units.Select(unit => unit.DeckNumber ?? 0).Concat(mod.UnitDeckNumbers.Values)))
            .DefaultIfEmpty(0).Max() + 1;
        string NameOf(string unit) => units.TryGetValue(unit, out GeneratedListsMaker.KnownUnit? known) && known.Name != "" ? known.Name : Divisions.BattlegroupView.ShortName(unit);
        foreach (Mod mod in mods)
        {
            // the units and their transports; in name order, so the numbers given do not depend on the divisions' order
            List<string> needing = [.. mod.Divisions.Where(entry => !entry.IsRemoved)
                .SelectMany(entry => entry.Added.Concat(made.Lists.GetValueOrDefault(entry.Descriptor) ?? []))
                .SelectMany(rule => rule.Transports.Prepend(rule.Unit))
                .Distinct(StringComparer.Ordinal)
                .Where(unit => units.TryGetValue(unit, out GeneratedListsMaker.KnownUnit? known) && known.DeckNumber is null && !Numbered(unit))
                .Order(StringComparer.Ordinal)];
            if (needing.Count == 0) continue;
            foreach (string unit in needing) mod.UnitDeckNumbers[unit] = next++;
            AddDeckNumberRules(mod, NameOf);
            deckNumbersGiven.AddRange(needing.Select(NameOf));
        }
    }

    /// <summary>The mod's numbers for deck codes that have no rule yet get one, at the end of its Units preset; the mod is saved when one is added.</summary>
    private static void AddDeckNumberRules(Mod mod, Func<string, string> unitName)
    {
        int before = ArmoryPresets.RulesOf(mod).Count;
        ArmoryPresets.AddDeckNumberRules(mod, Texts.Catalog, unitName, EasyNdf.App.Rules.ArmoryPartTexts.Name(ArmoryPart.Units));
        if (ArmoryPresets.RulesOf(mod).Count != before) AppData.Current.Save(mod);
    }

    /// <summary>The dictionaries this export makes, as the Files tab has none of them (see <see cref="DictionariesToMake"/>).</summary>
    private List<DictionaryToMake> dictionariesToMake = [];

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
        List<(Mod Mod, IReadOnlySet<string> Files)> armoryFiles = ArmoryFilesByMod();
        return [.. entries.Where(entry => entry.On && entry.Source is not null && (everyFile || ModsOn(entry, armoryFiles).Count > 0))];
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
    private sealed record FileWork(FileContent.Snapshot Content, List<ModRules> Stack, bool IsNamesFile, IReadOnlyList<DictionaryText> Texts, string RuleSets, Mod? HeaderMod,
        string OpenModName, Dictionary<string, string> OtherFiles, IReadOnlyList<FileAndTextRules.CarriedFile> ModFiles,
        List<GeneratedListsMaker.Input>? GeneratedInputs = null);

    /// <summary>What a file's run needs (<see cref="FileWork"/>), with its own copies of the rules.</summary>
    /// <param name="generatedInputs">
    /// The files generated divisions are made from, by game, worked out for an earlier file of the same run: they are
    /// only read, and working them out again for each file that makes generated divisions held the window up. Tuned for
    /// speed: Keep it that way when tidying.
    /// </param>
    private static FileWork WorkFor(FileEntry entry, IEnumerable<FileEntry> entries, Dictionary<Data.Game.GameTitle, List<GeneratedListsMaker.Input>> generatedInputs)
    {
        string openModName = AppData.Current.OpenMod.Name;
        List<ModRules> stack = StackFor(entry);
        // the other files a file's rules read, and its generated lists, come from files of its own game
        List<FileEntry> sameGame = [.. entries.Where(each => each.Game == entry.Game)];
        // each rule set's File rules judged on their own: a group switched off in one does not reach into the next
        List<FileAndTextRules.CarriedFile> modFiles = FileAndTextRules.FilesOf(
            FileAndTextRules.EachInItsOwnRuleSet(stack.SelectMany(part => part.RuleSets.Select(ruleSet => ruleSet.Rules))));
        FileContent.Snapshot content = FileContent.SnapshotOf(entry);
        if (WritesDictionary(entry))
            return new FileWork(content, [], IsNamesFile: true, DictionaryTextsFor(entry), "", null, openModName, [], modFiles);
        if (stack.Count == 0) return new FileWork(content, stack, IsNamesFile: false, [], "", null, openModName, [], []);
        Dictionary<string, string> otherFiles = OtherFilesFor(stack.SelectMany(part => part.Rules), sameGame);
        List<GeneratedListsMaker.Input>? generated = null;
        if (HasGeneratedRules(stack) && !generatedInputs.TryGetValue(entry.Game, out generated))
            generatedInputs[entry.Game] = generated = GeneratedListsMaker.InputsFrom(sameGame, entry.Game) ?? [];
        return new FileWork(content, stack, IsNamesFile: false, [], RuleSetNames(stack), HeaderModOf(stack), openModName, otherFiles, modFiles, generated);
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
        FileAndTextRules.IsDictionaryFile(entry.Name) && (HasDivisionRules(entry) || HasRuleSetsFor(entry.Name, entry.Folder, entry.Game));

    /// <summary>
    /// Whether a dictionary file goes into the mod's Localisation folder (GameData\Localisation\&lt;mod&gt;), the only
    /// place the game reads it from: UNITS.csv, and any .csv added from a Localisation folder. Another .csv is
    /// exported in the folders it was added with.
    /// </summary>
    internal static bool GoesIntoLocalisation(FileEntry entry) =>
        DivisionExtras.IsNamesFile(entry.Name, entry.Game) || entry.Folder.Split('/').Contains("Localisation", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a dictionary file is exported into the WARNO mod's own folder under Localisation (<see cref="GoesIntoLocalisation"/>):
    /// a WARNO file only. Steel Division 2 reads its mod's MODDING.csv from GameData\Localisation itself, so its
    /// dictionaries keep the folders they were added with.
    /// </summary>
    internal static bool GoesIntoGameModFolder(FileEntry entry) => entry.Game == Data.Game.GameTitle.Warno && GoesIntoLocalisation(entry);

    /// <summary>
    /// The texts a dictionary file is given, mod by mod in the order they run: each mod's Text rules for it, then, for
    /// the game's dictionary (UNITS.csv, MODDING.csv), its Armory presets' texts (division and unit names and the
    /// like). A token given twice takes the later text.
    /// </summary>
    private static List<DictionaryText> DictionaryTextsFor(FileEntry entry) => DictionaryTextsFor(entry.Name, entry.Folder, ModStack.RunningFor(entry.Game));

    /// <summary>The texts these mods give a dictionary file of this name and folder (see <see cref="DictionaryTextsFor(FileEntry)"/>).</summary>
    private static List<DictionaryText> DictionaryTextsFor(string fileName, string folder, IReadOnlyList<Mod> mods)
    {
        var texts = new List<DictionaryText>();
        bool byName = RuleSetsApplyByName;
        foreach (Mod mod in mods)
        {
            texts.AddRange(FileAndTextRules.TextsOf(RuleSetFiles.For(fileName, folder, mod.Presets, byName).SelectMany(ruleSet => ruleSet.Rules)));
            if (DivisionExtras.IsNamesFile(fileName, mod.Game)) texts.AddRange(DivisionExtras.ArmoryTextsOf(ArmoryPresets.RulesOf(mod)));
        }
        return texts;
    }

    // ---- dictionaries the export makes ----

    /// <summary>A dictionary file the export makes, as the Files tab has none: its name, the texts the mods give it, and the game it is for.</summary>
    private sealed record DictionaryToMake(string Name, IReadOnlyList<DictionaryText> Texts, Data.Game.GameTitle Game);

    /// <summary>
    /// The dictionaries (.csv) the mods that are on give texts to that are not on the Files tab: those their Text rules
    /// are for, and UNITS.csv for their divisions' names. A dictionary is the mod's own, not a game file (the game has
    /// none of the mod's), so the export makes it rather than leave the texts out, which the game refuses to start with.
    /// Only the mods of the games of the files exported give texts.
    /// </summary>
    /// <param name="run">The files the export runs.</param>
    private List<DictionaryToMake> DictionariesToMake(List<FileEntry> run)
    {
        IReadOnlyList<Mod> mods = ModStack.RunningOn(run);
        var names = new List<(string Name, string Folder, Data.Game.GameTitle Game)>();
        void Note(string name, string folder, Data.Game.GameTitle game)
        {
            if (!names.Any(each => each.Game == game && string.Equals(each.Name, name, StringComparison.OrdinalIgnoreCase))) names.Add((name, folder, game));
        }
        foreach (Mod mod in mods)
        {
            foreach (ModPreset ruleSet in mod.Presets.Where(ruleSet => !ruleSet.IsArmoryPreset && FileAndTextRules.TextsOf(ruleSet.Rules).Count > 0))
                foreach (string file in RuleSetFiles.Named(ruleSet).Where(FileAndTextRules.IsDictionaryFile))
                    Note(Path.GetFileName(file), Path.GetDirectoryName(file)?.Replace('\\', '/') ?? "", mod.Game);
            if (ArmoryPresets.RulesOf(mod).Any(DivisionExtras.NeedsNamesFile)) Note(DivisionExtras.NamesFileOf(mod.Game), "", mod.Game);
        }
        // each game's dictionaries get the texts of its own mods, and are made when no file of that game has the name
        return [.. names
            .Where(file => !model.Entries.Any(entry => entry.Game == file.Game && string.Equals(entry.Name, file.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(file => new DictionaryToMake(file.Name, DictionaryTextsFor(file.Name, file.Folder, [.. mods.Where(mod => mod.Game == file.Game)]), file.Game))
            .Where(file => file.Texts.Count > 0)];
    }

    /// <summary>
    /// Writes the dictionaries the export makes into the mod's Localisation folder (GameData\Localisation\&lt;mod&gt;),
    /// beside the GameData folder of the files just exported. A file already there, from an earlier export or put there
    /// by hand, keeps its lines and is given the texts, as one on the Files tab would be. The Files tab says which were
    /// made, so none is made unseen. Each game's go beside its own files.
    /// </summary>
    private void MakeDictionaries(List<DictionaryToMake> toMake, List<FileEntry> run)
    {
        foreach (IGrouping<Data.Game.GameTitle, DictionaryToMake> ofGame in toMake.GroupBy(dictionary => dictionary.Game))
            MakeDictionaries([.. ofGame], [.. run.Where(entry => entry.Game == ofGame.Key)], ofGame.Key);
    }

    /// <summary>
    /// Writes one game's dictionaries the export makes: WARNO's into its mod's own folder under Localisation, Steel
    /// Division 2's (its MODDING.csv) into GameData\Localisation itself, where each game reads them.
    /// </summary>
    private void MakeDictionaries(List<DictionaryToMake> toMake, List<FileEntry> run, Data.Game.GameTitle game)
    {
        bool intoGameModFolder = game == Data.Game.GameTitle.Warno;
        if (toMake.Count == 0 || (intoGameModFolder && namesFileMod is null)) return;
        string? gameData = run.Select(ExportGameDataOf).FirstOrDefault(folder => folder is not null);
        string? exportFolder = run.Select(entry => entry.ExportFolder).FirstOrDefault(folder => folder is not null);
        gameData ??= exportFolder is null ? null
            : string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(exportFolder)), "GameData", StringComparison.OrdinalIgnoreCase) ? exportFolder
            : Path.Combine(exportFolder, "GameData");
        if (gameData is null) return;
        string shownFolder = intoGameModFolder ? "Localisation\\" + namesFileMod : "Localisation";
        string folder = intoGameModFolder ? Path.Combine(gameData, "Localisation", namesFileMod!) : Path.Combine(gameData, "Localisation");
        try
        {
            Directory.CreateDirectory(folder);
            foreach (DictionaryToMake dictionary in toMake)
            {
                string path = Path.Combine(folder, dictionary.Name);
                byte[] old = File.Exists(path) ? File.ReadAllBytes(path) : [];
                // as a dictionary on the Files tab is written: its byte order mark kept (or left out when it had none),
                // and kept beside it first with "Back up overwritten files"
                bool hasMark = old.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]);
                string oldText = System.Text.Encoding.UTF8.GetString(old, hasMark ? 3 : 0, old.Length - (hasMark ? 3 : 0));
                (string text, _) = DivisionExtras.WithTexts(oldText, dictionary.Texts, game);
                if (old.Length > 0 && AppData.Current.Settings["backupOverwritten"]?.GetValue<bool>() == true) File.Copy(path, path + ".bak", overwrite: true);
                byte[] mark = hasMark ? [0xEF, 0xBB, 0xBF] : [];
                WriteWhole(path, [.. mark, .. System.Text.Encoding.UTF8.GetBytes(text)]);
            }
            model.SayAlso(Texts.Get("Files.Run.DictionariesMade", ("files", string.Join(", ", toMake.Select(each => each.Name))),
                ("folder", shownFolder), ("texts", toMake.Sum(each => each.Texts.Count))));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            model.Warn(Texts.Get("Files.Run.DictionariesNotMade", ("reason", error.Message)));
        }
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
        List<JsonNode?> rules = RuleSetRulesOfEveryMod(run);
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
    /// The rules of every mod's rule sets, each rule set judged on its own (<see cref="FileAndTextRules.EachInItsOwnRuleSet"/>),
    /// with those of the mods that do not run on these files (off, or for another game) copied as switched off.
    /// </summary>
    private static List<JsonNode?> RuleSetRulesOfEveryMod(List<FileEntry> run)
    {
        IReadOnlyList<Mod> running = ModStack.RunningOn(run);
        return [.. ModStack.Ordered.SelectMany(mod => FileAndTextRules.EachInItsOwnRuleSet(
            mod.Presets.Where(preset => preset.ArmoryPart is null).Select(preset => preset.Rules), allOff: !running.Contains(mod)))];
    }

    // ---- the Armory presets ----

    /// <summary>The rules of the Armory presets of these mods, in the order they run; none when they have none.</summary>
    private static IReadOnlyList<JsonNode?> ArmoryRulesOf(IEnumerable<Mod> mods) => [.. mods.SelectMany(ArmoryPresets.RulesOf)];

    /// <summary>Whether the Armory presets of the mods of its game have rules for a file: it is then run, with or without a preset of its own.</summary>
    internal static bool HasDivisionRules(FileEntry entry) => HasDivisionRules(entry.Name, entry.Game);

    /// <summary>Whether the Armory presets of a game's mods have rules for a file of this name, such as "Divisions.ndf" or "UNITS.csv".</summary>
    internal static bool HasDivisionRules(string fileName, Data.Game.GameTitle game)
    {
        IReadOnlyList<JsonNode?> rules = ArmoryRulesOf(ModStack.RunningFor(game));
        return rules.Count > 0 && HasRulesFor(rules, fileName, game);
    }

    /// <summary>
    /// The names of the files the Armory presets of a game's mods that are on have rules for, UNITS.csv among them when
    /// they give texts: worked out once for a whole file list, as <see cref="HasDivisionRules(string, Data.Game.GameTitle)"/>
    /// for each of thousands of files was slow.
    /// </summary>
    internal static IReadOnlySet<string> FilesWithArmoryRules(Data.Game.GameTitle game)
    {
        IReadOnlyList<JsonNode?> rules = ArmoryRulesOf(ModStack.RunningFor(game));
        HashSet<string> files = DivisionChangeRules.FilesOf(rules);
        if (rules.Any(DivisionExtras.NeedsNamesFile)) files.Add(DivisionExtras.NamesFileOf(game));
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
    /// What runs on a file, mod by mod, for each mod of the file's game that is on, in the Mods tab's order
    /// (<see cref="ModStack"/>): a mod for another game never runs on it. The rules of its rule sets that apply to the file (<see cref="RuleSetFiles"/>), in its list order, then its Armory
    /// presets' rules for the file, so its Armory changes come after its other changes to the same file. Mods with no
    /// rules for the file are left out. Every rule shows in Details.
    /// </summary>
    internal static List<ModRules> StackFor(FileEntry entry)
    {
        var stack = new List<ModRules>();
        bool byName = RuleSetsApplyByName;
        foreach (Mod mod in ModStack.RunningFor(entry.Game))
        {
            List<ModPreset> ruleSets = RuleSetFiles.For(entry.Name, entry.Folder, mod.Presets, byName);
            var rules = new JsonArray();
            foreach (ModPreset ruleSet in ruleSets)
                foreach (JsonNode? rule in ruleSet.Rules) rules.Add(rule?.DeepClone());
            foreach (JsonNode? rule in DivisionChangeRules.ForFile(ArmoryPresets.RulesOf(mod), entry)) rules.Add(rule!.DeepClone());
            if (rules.Count > 0) stack.Add(new ModRules(mod, ruleSets, rules));
        }
        return stack;
    }

    /// <summary>
    /// The mods that run on a file, in the order they run, each with its rule sets for it: what the Files tab's Mods
    /// column shows. Mods for another game than the file's are left out. Cheaper than <see cref="StackFor"/>, which
    /// copies the rules, for a list of thousands.
    /// </summary>
    /// <param name="armoryFiles">The files the Armory presets of each mod that is on have rules for, by mod (see <see cref="ArmoryFilesByMod"/>).</param>
    internal static List<ModOnFile> ModsOn(FileEntry entry, IReadOnlyList<(Mod Mod, IReadOnlySet<string> Files)> armoryFiles)
    {
        var mods = new List<ModOnFile>();
        bool byName = RuleSetsApplyByName;
        foreach ((Mod mod, IReadOnlySet<string> files) in armoryFiles)
        {
            if (mod.Game != entry.Game) continue;
            List<string> ruleSets = [.. RuleSetFiles.For(entry.Name, entry.Folder, mod.Presets, byName).Select(ruleSet => ruleSet.Name)];
            bool hasArmoryChanges = files.Contains(entry.Name) && (DivisionExtras.IsNamesFile(entry.Name, entry.Game) || DivisionChangeRules.WritesTo(entry));
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
            if (rules.Any(DivisionExtras.NeedsNamesFile)) files.Add(DivisionExtras.NamesFileOf(mod.Game));
            byMod.Add((mod, files));
        }
        return byMod;
    }

    /// <summary>A mod's rule sets that apply to a file, in the mod's list order (<see cref="RuleSetFiles"/>).</summary>
    internal static List<ModPreset> RuleSetsFor(FileEntry entry, Mod mod) => RuleSetFiles.For(entry.Name, entry.Folder, mod.Presets, RuleSetsApplyByName);

    /// <summary>Whether a rule set applies to a file: never to a file of another game than its mod's.</summary>
    internal static bool AppliesTo(ModPreset ruleSet, FileEntry entry) =>
        AppData.Current.ModOf(ruleSet) is Mod mod && mod.Game == entry.Game && RuleSetsFor(entry, mod).Contains(ruleSet);

    /// <summary>Whether a mod of a game that is on has rule sets that apply to a file of this name and folder.</summary>
    internal static bool HasRuleSetsFor(string fileName, string folder, Data.Game.GameTitle game)
    {
        bool byName = RuleSetsApplyByName;
        return ModStack.RunningFor(game).Any(mod => RuleSetFiles.For(fileName, folder, mod.Presets, byName).Count > 0);
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
    /// How many rules each part of the Armory runs on a file of this name and game, its headings left out; the parts
    /// with none are left out.
    /// </summary>
    internal static IReadOnlyDictionary<ArmoryPart, int> ArmoryRuleCountsFor(string fileName, Data.Game.GameTitle game)
    {
        var counts = new Dictionary<ArmoryPart, int>();
        foreach (ModPreset preset in ModStack.RunningFor(game).SelectMany(ArmoryPresets.AllOf))
        {
            int count = DivisionExtras.IsNamesFile(fileName, game)
                ? preset.Rules.Count(DivisionExtras.NeedsNamesFile)
                : DivisionChangeRules.ForFile(preset.Rules, fileName).Count(rule => (string?)rule?["Kind"] != "Group");
            if (count > 0 && preset.ArmoryPart is ArmoryPart part) counts[part] = counts.GetValueOrDefault(part) + count;
        }
        return counts;
    }

    /// <summary>How many rules each part of the Armory runs on a file of the list: none on one it never writes to (<see cref="DivisionChangeRules.WritesTo"/>).</summary>
    internal static IReadOnlyDictionary<ArmoryPart, int> ArmoryRuleCountsFor(FileEntry entry) =>
        DivisionExtras.IsNamesFile(entry.Name, entry.Game) || DivisionChangeRules.WritesTo(entry)
            ? ArmoryRuleCountsFor(entry.Name, entry.Game) : new Dictionary<ArmoryPart, int>();

    /// <summary>What a file is sorted by in the Armory column: the letters of the parts with rules for it, such as "DU".</summary>
    internal static string ArmorySortKey(FileEntry entry) =>
        string.Concat(ArmoryRuleCountsFor(entry).Keys.OrderBy(part => part).Select(Rules.ArmoryLetterTexts.Letter));

    /// <summary>The parts of the Armory whose presets, of a game's mods, have rules for a file of this name, in the order they run.</summary>
    internal static IReadOnlyList<ArmoryPart> ArmoryPartsFor(string fileName, Data.Game.GameTitle game) =>
        [.. ModStack.RunningFor(game).SelectMany(ArmoryPresets.AllOf).Where(preset => HasRulesFor(preset.Rules, fileName, game)).Select(preset => preset.ArmoryPart!.Value).Distinct()];

    /// <summary>Whether rules of the Armory are for a file of this name; for the game's dictionary (UNITS.csv, MODDING.csv), whether they give it texts.</summary>
    private static bool HasRulesFor(IEnumerable<JsonNode?> rules, string fileName, Data.Game.GameTitle game) =>
        DivisionExtras.IsNamesFile(fileName, game) ? rules.Any(DivisionExtras.NeedsNamesFile) : DivisionChangeRules.FilesOf(rules).Contains(fileName);

    /// <summary>
    /// After an export: the Armory's pictures of the user's own (division emblems, unit card pictures) copied into the
    /// GameData folder of a file with picture rules just exported, such as Divisions.ndf. The Files tab says how many,
    /// or what stopped it.
    /// </summary>
    private void WriteDivisionExtras(List<FileEntry> run)
    {
        RemovePicturesNoLongerUsed(run);
        IReadOnlyList<JsonNode?> armoryRules = ArmoryRulesOf(ModStack.RunningOn(run));
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
            else if (outcome.Pictures > 0) model.Say(PicturesCopied(outcome.Emblems, outcome.Pictures - outcome.Emblems));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            model.Warn(Texts.Get("Files.Run.Divisions.Failed", ("problem", error.Message)));
        }
    }

    /// <summary>What the Files tab says of the pictures an export copied: "2 emblems and 1 unit picture copied."</summary>
    private static string PicturesCopied(int emblems, int unitPictures)
    {
        var kinds = new List<string>();
        if (emblems > 0) kinds.Add(Texts.Count("Files.Run.Pictures.Emblems", emblems, ("count", FileStatus.Count(emblems))));
        if (unitPictures > 0) kinds.Add(Texts.Count("Files.Run.Pictures.UnitPictures", unitPictures, ("count", FileStatus.Count(unitPictures))));
        string what = kinds.Count == 2 ? Texts.Get("Files.Run.Pictures.Both", ("first", kinds[0]), ("second", kinds[1])) : kinds[0];
        return Texts.Get("Files.Run.Pictures.Copied", ("what", what));
    }

    /// <summary>
    /// The Armory presets' rules of every mod, with those of the mods that do not run on these files (off, or for
    /// another game) copied as switched off. Their group headings are left out, so they do not switch off the rules of
    /// the mods that are on after them.
    /// </summary>
    private static List<JsonNode?> ArmoryRulesOfEveryMod(List<FileEntry> run)
    {
        var rules = new List<JsonNode?>();
        IReadOnlyList<Mod> running = ModStack.RunningOn(run);
        foreach (Mod mod in ModStack.Ordered)
        {
            bool isOn = running.Contains(mod);
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
        List<JsonNode?> armoryRules = ArmoryRulesOfEveryMod(run);
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
        // one kept before names were checked (a whole path, say) is asked again: it is joined to the Localisation folder
        if (KeptGameMod(modName) is string kept && IsFolderName(kept)) return kept;
        if (AskGameMod is null || await AskGameMod(modName) is not string typed || typed == "") return null;
        KeepGameMod(modName, typed);
        return typed;
    }

    /// <summary>Whether a text is one folder's name, which joined to a folder stays inside it: no path, no "..".</summary>
    public static bool IsFolderName(string name) =>
        name is not ("" or "." or "..") && name.Trim() == name && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

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
                string? gameMod = GoesIntoGameModFolder(entry) ? namesFileMod : null;
                RunNamesFile(entry, work.Content, write, gameMod, work.Texts);
                if (write) WriteModFiles(entry, work.ModFiles, gameMod);
                return;
            }
            List<ModRules> stack = work.Stack;
            if (stack.Count == 0)
            {
                RunWithModsOff(entry, work.Content, write, everyFile, work.OpenModName);
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
            // with more than one mod, each mod's text is kept for the Mod Conflicts window's panels, and every change is
            // logged so no conflict is missed; Details keeps its usual number of them (ForDetails)
            List<string>? stages = stack.Count > 1 ? [] : null;
            int logLimit = stack.Count > 1 ? int.MaxValue : settings.LogLimit;
            var ruleCounts = new List<int>();
            (RunResult result, LoadSummary info) = RunStack(stack, work.Content.Text(), write, logLimit, work.OtherFiles, stages, ruleCounts);
            clock.Stop();
            lock (conflictsFound) conflictsFound[entry.Key] = ConflictsIn(entry.Name, stack, result, stages ?? [], ruleCounts);

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
                Result = ForDetails(result, settings.LogLimit),
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
                Deliver(entry, work.Content.Bytes(), Texts.Get("Files.Run.Restored"));
            else if (result.Edits == 0 && !everyFile) entry.Message = Texts.Get("Files.Run.NoChanges");
            else if (result.Edits == 0) Deliver(entry, work.Content.Bytes(), Texts.Get("Files.Run.UnchangedCopyNoChanges"));
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
    /// <param name="content">The file, as it was when the run started.</param>
    private static void RunWithModsOff(FileEntry entry, FileContent.Snapshot content, bool write, bool everyFile, string openModName)
    {
        if (write && everyFile) Deliver(entry, content.Bytes(), Texts.Get("Files.Run.UnchangedCopy"));
        else if (write && HoldsEarlierExport(entry)) Deliver(entry, content.Bytes(), Texts.Get("Files.Run.Restored"));
        else entry.Message = Texts.Get("Files.Run.ModOff", ("mod", openModName));
    }

    // ---- running the mods one after the other ----

    /// <summary>What each file run found of mods changing the same values, by the file's key, given to the Mods tab when the run ends.</summary>
    private readonly Dictionary<string, List<ConflictedValue>> conflictsFound = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs each mod's rules on a file, one mod after the other, each on what the one before gave (<see cref="StackFor"/>).
    /// A mod's own name goes into the comments its rules write. With one mod it is one run, as before mods could run
    /// together. Stops at a mod whose rules give a file that does not read back. The results are put together: every
    /// rule's counts and edits, in the order they ran, and the last text. Info counts what the file held before the first
    /// mod (its lists of names and the like are left empty: no caller shows them, and going through a large file for
    /// them took a good part of reading it).
    /// </summary>
    /// <param name="stages">
    /// When given, filled with the text before the first mod and after each mod that ran, for the Mod Conflicts window;
    /// the last mod then makes its text even for a Preview.
    /// </param>
    /// <param name="ruleCounts">When given, filled with how many rules the engine ran for each mod that ran, in order.</param>
    private (RunResult Result, LoadSummary Info) RunStack(List<ModRules> stack, string text, bool wantText, int logLimit, Dictionary<string, string> otherFiles,
        List<string>? stages = null, List<int>? ruleCounts = null)
    {
        LoadSummary? info = null;
        var results = new List<RunResult>();
        stages?.Add(text);
        for (int at = 0; at < stack.Count; at++)
        {
            ModRules part = stack[at];
            var engine = new NdfEngine();
            LoadSummary loaded = engine.LoadForRun(text);
            info ??= loaded;
            List<Rule?> rules = RuleLoading.LoadForEngine(part.Rules);
            ruleCounts?.Add(rules.Count);
            RunResult result = engine.Run(rules, new RunOptions
            {
                // a mod before the last needs its text, for the next mod to run on
                WantText = wantText || at < stack.Count - 1 || stages is not null,
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
            stages?.Add(text);
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
    /// The values more than one mod changed in a file, from each rule's edit log (with more than one mod, every change is
    /// logged), each with the lines around it before the first mod and after each mod (<paramref name="stages"/>). The
    /// rules' logs follow the mods' order, as they ran.
    /// </summary>
    /// <param name="ruleCounts">
    /// How many rules the engine ran for each mod that ran (<see cref="RunStack"/>), which splits the logs between the
    /// mods: kept from the run, as loading the rules again only to count them took most of this for a large file. Tuned
    /// for speed: Keep it that way when tidying.
    /// </param>
    private static List<ConflictedValue> ConflictsIn(string fileName, List<ModRules> stack, RunResult result, List<string> stages, List<int> ruleCounts)
    {
        if (stack.Count < 2) return [];
        var runs = new List<(string Mod, IEnumerable<(string Rule, int RuleIndex, EditLogEntry Entry)> Logged)>();
        var stageOf = new Dictionary<string, int>(StringComparer.Ordinal);
        int rule = 0;
        for (int at = 0; at < stack.Count; at++)
        {
            ModRules part = stack[at];
            // a mod after one whose rules gave a file that does not read back did not run, and has no logs
            int ran = at < ruleCounts.Count ? ruleCounts[at] : 0;
            int first = rule, count = Math.Min(ran, result.Logs.Count - rule);
            var logged = new List<(string, int, EditLogEntry)>();
            for (int i = 0; i < count; i++)
                foreach (EditLogEntry entry in result.Logs[first + i]) logged.Add((result.Stats[first + i].Name, i, entry));
            runs.Add((part.Mod.Name, logged));
            stageOf[part.Mod.Name] = at + 1;
            rule += count;
        }
        List<ConflictedValue> found = ModConflictFinder.Find(fileName, runs);
        if (found.Count == 0 || stages.Count == 0) return found;
        // each text is read for its lines only when the file has conflicts, and once for all of them
        var texts = new ValueTexts?[stages.Count];
        ValueTexts TextAt(int stage) => texts[stage] ??= new ValueTexts(stages[stage]);
        ValueText? Lines(int stage, ConflictedValue value) => stage < stages.Count ? TextAt(stage).Find(value.Descriptor, value.Property) : null;
        return [.. found.Select(value => value with
        {
            GameText = value.IsNameClash ? null : Lines(0, value),
            // a mod refused a name it wanted shows its refusal, not the other mod's descriptor
            Changes = [.. value.Changes.Select(change => change with
            {
                Text = value.IsNameClash && !ReferenceEquals(change, value.Kept) ? null : Lines(stageOf[change.Mod], value),
            })],
        })];
    }

    /// <summary>
    /// A dictionary file such as UNITS.csv, with the texts of the Text rules and of the Armory presets (division names,
    /// a new division's summary and history...): a new one is written into the export folder, as an exported .ndf file
    /// is; the one on the Files tab is left as it is.
    /// </summary>
    /// <param name="gameMod">The WARNO mod it is for, by its folder name: it goes into Localisation\&lt;that name&gt;; null keeps its folders.</param>
    /// <param name="dictionaryTexts">The texts it is given (<see cref="DictionaryTextsFor"/>).</param>
    /// <param name="content">The file, as it was when the run started.</param>
    private static void RunNamesFile(FileEntry entry, FileContent.Snapshot content, bool write, string? gameMod, IReadOnlyList<DictionaryText> dictionaryTexts)
    {
        byte[] source = content.Bytes();
        (string text, int count) = DivisionExtras.WithTexts(content.Text(), dictionaryTexts, entry.Game);
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
            ? GeneratedListsMaker.InputsFrom(model.Entries, entry.Game) ?? []
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
        // the Export folder column says where: the status says only what happened
        entry.Message = Texts.Get("Files.Run.Exported", ("what", what));
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

    /// <summary>
    /// The folder of the export that stands for the source's GameData folder: the GameData folder the file is exported
    /// into; else, when the export folder stands for a folder under GameData (its Generated folder, say, exported to
    /// "…\Generated"), the folder the same steps up from the exported file reach. Null when neither shows it.
    /// </summary>
    public static string? ExportGameDataOf(FileEntry entry)
    {
        if (entry.ExportFolder is null) return null;
        string path = Path.GetFullPath(ExportPathOf(entry));
        if (DivisionExtras.GameDataFolderOf(path) is string found) return found;
        if (InGameData(entry) is not string inside) return null;
        string end = Path.DirectorySeparatorChar + inside;
        return path.EndsWith(end, StringComparison.OrdinalIgnoreCase) ? path[..^end.Length] : null;
    }

    /// <summary>A file's place in its source's GameData folder, such as Generated\Gameplay\Decks\Divisions.ndf; null for one outside a GameData folder, or in an archive.</summary>
    private static string? InGameData(FileEntry entry) =>
        entry.Source is string source && entry.ArchivedContent is null && DivisionExtras.GameDataFolderOf(source) is string gameData
            ? Path.GetRelativePath(gameData, source) : null;

    /// <summary>The folder a file was added from, in its source's GameData folder: "Generated" for one added with the Generated folder, "" for GameData itself; null when unknown.</summary>
    public static string? AddedFromInGameData(FileEntry entry)
    {
        if (InGameData(entry) is not string inside) return null;
        string[] steps = inside.Split(Path.DirectorySeparatorChar);
        int own = (entry.Folder == "" ? 0 : entry.Folder.Split('/').Length) + 1;
        return steps.Length < own ? null : string.Join(Path.DirectorySeparatorChar, steps[..^own]);
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
    /// a file of the same game whose folder and name are that name, or end with it.
    /// </summary>
    internal static Dictionary<string, string> OtherFilesFor(FileEntry entry, IEnumerable<FileEntry> entries) =>
        OtherFilesFor(RulesToRun(entry), entries.Where(each => each.Game == entry.Game));

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
    /// <param name="logLimit">How many edits Details lists for each rule: a run with every change logged, for the conflicts, is cut back to it.</param>
    private static RunResult ForDetails(RunResult result, int logLimit) =>
        new()
        {
            Stats = result.Stats, Warnings = result.Warnings, ParseError = result.ParseError,
            Logs = [.. result.Logs.Select(log => log.Count > logLimit ? log.GetRange(0, logLimit) : log)],
        };

    /// <summary>A mod's watermark as the engine takes it, or null when it has none.</summary>
    private static Watermark? WatermarkOf(Mod? mod) =>
        mod?.Watermark is ModWatermark mark ? new Watermark(mark.On, mark.Author, mark.Version, mark.Game) : null;
}
