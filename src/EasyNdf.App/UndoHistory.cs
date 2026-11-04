using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using EasyNdf.App.Files;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using EasyNdf.App.Text;

namespace EasyNdf.App;

/// <summary>
/// Undo and Redo (the HTML app's <c>hist*</c> functions). Everything kept in the mods (their presets and rules, and the
/// Armory's division changes), each mod's folders (the export folder its new files start with, and the WARNO mod folder
/// its UNITS.csv goes into), and the file list (which rows it has, in which order, and each row's preset, On checkbox
/// and export folder), is one state; each change files the state before it, and Undo puts a filed state back. Undo on
/// one tab can so take back a change made on another. A removed file row is kept whole in the history, so Undo brings it
/// back as it was. The open mod and the watermarks are not part of the state, as in the HTML app. The division changes
/// are: the Armory presets' rules are made from them, so putting back one without the other would part the two.
/// </summary>
public sealed class UndoHistory
{
    /// <summary>How many changes can be undone ("Undo steps", 60 unless set); older ones are forgotten.</summary>
    public static int MaximumSteps => int.TryParse(AppData.Current.Settings["undoSteps"]?.ToString(), out int steps) && steps > 0 ? steps : 60;

    private readonly AppData data;
    private readonly FileListModel files;
    private readonly List<UndoState> undoSteps = [];
    private readonly List<UndoState> redoSteps = [];
    private UndoState now;

    /// <summary>The mods' fingerprints (see <see cref="PrintOf"/>) as they are now, worked out again only after the mods change.</summary>
    private IReadOnlyList<string>? modsPrints;

    /// <summary>
    /// The mods as they were when <see cref="modsPrints"/> was worked out (see <see cref="ShapeOf"/>), to tell whether
    /// opening another mod came with a mod added or taken away; null when not known.
    /// </summary>
    private List<object?>? modsPrintsShape;

    /// <summary>While a filed state is put back, the changes that makes are not filed.</summary>
    private bool isPuttingBack;

    /// <summary>Set while one action is being filed: what it changes until the app next rests is one step.</summary>
    private bool isInOneAction;

    /// <summary>Starts with nothing to undo, from the mods and the file list as they are.</summary>
    public UndoHistory(AppData data, FileListModel files)
    {
        this.data = data;
        this.files = files;
        now = Here();
        marking = new RunOnceSoon(Mark);
    }

    /// <summary>Raised when <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed.</summary>
    public event Action? Changed;

    /// <summary>Whether there is a change to undo.</summary>
    public bool CanUndo => undoSteps.Count > 0;

    /// <summary>Whether there is an undone change to redo.</summary>
    public bool CanRedo => redoSteps.Count > 0;

    /// <summary>
    /// Set while the rule window is open: its changes are not saved until Done, so nothing is filed meanwhile. Done
    /// files them as one step.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// The mods have changed: the next <see cref="Mark"/> compares them again. Tuned for speed: opening another mod
    /// keeps the mods' fingerprints rather than working them out again (see <see cref="ShapeOf"/>). Keep it that way when tidying.
    /// </summary>
    /// <param name="change">What changed. Opening another mod changes nothing in the mods themselves, so their fingerprints
    /// are kept: writing out every mod's rules again (some megabytes for a large mod) after each click on a mod took about
    /// a third of the click, with the memory it took to clean up. They are kept only while the mods are the same ones,
    /// holding as many rule sets, rules and changes as before (see <see cref="ShapeOf"/>), as a mod is opened just
    /// after it is added (New, Make Copy) without telling of the mod added.</param>
    public void ModsChanged(ModsChange change)
    {
        bool onlyOpened = change.OpenModChanged && !change.PresetsAdded && modsPrintsShape is not null && ShapeOf(data.Mods).SequenceEqual(modsPrintsShape);
        if (!onlyOpened) modsPrints = null;
        Mark();
    }

    /// <summary>
    /// <see cref="Mark"/> once for a burst of changes to the file list: comparing the whole list after each of a
    /// thousand files read took seconds. Undo and Redo mark first if one is waiting.
    /// </summary>
    public void MarkSoon() => marking.Ask();

    private readonly RunOnceSoon marking;

    /// <summary>
    /// Files the state before a change, if the mods or the file list differ from the last state filed (the HTML app's
    /// <c>histMark</c>). Call it after every change; nothing is filed when nothing changed.
    /// </summary>
    public void Mark()
    {
        if (isPuttingBack || IsPaused) return;
        IReadOnlyList<string> modsNow = ModsPrintsNow();
        string foldersText = FoldersText(data);
        // most changes to the list are not the user's (a file read, a status shown): this check needs no copying
        bool sameMods = SamePrints(modsNow, now.ModPrints);
        if (sameMods && foldersText == now.FoldersText && RowsAreAsFiled()) return;
        List<UndoRow> rows = [.. files.Entries.Select(UndoRow.Of)];
        string rowsText = RowsText(rows);
        if (sameMods && foldersText == now.FoldersText && rowsText == now.RowsText) return;

        // the mods are copied only now that they are filed: a click that changes nothing copies nothing
        List<FiledMod> filed = File(data.Mods, modsNow, now);
        // one action is one step, even when it saves in several parts
        if (!isInOneAction)
        {
            undoSteps.Add(now);
            while (undoSteps.Count > MaximumSteps) undoSteps.RemoveAt(0);
        }
        if (!isInOneAction)
        {
            isInOneAction = true;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => isInOneAction = false);
        }
        redoSteps.Clear();
        now = new UndoState(filed, rows, rowsText, foldersText);
        Changed?.Invoke();
    }

    /// <summary>Undo: puts back the state before the last change.</summary>
    public UndoResult Undo()
    {
        marking.RunIfAsked();
        if (files.IsBusy) return new UndoResult(Texts.Get("Main.Undo.Busy"), IsDone: false);
        if (undoSteps.Count == 0) return new UndoResult(Texts.Get("Main.Undo.Nothing"), IsDone: false);
        UndoState was = now;
        redoSteps.Add(now);
        now = undoSteps[^1];
        undoSteps.RemoveAt(undoSteps.Count - 1);
        PutBack(now);
        OpenModChanged(was, now);
        // the message names where the change was, since Undo on one tab can take back a change made on the other
        string undone = WhereChanged(was, now) switch
        {
            ChangePlace.Armory => Texts.Get("Main.Undo.Armory"),
            ChangePlace.Rules => Texts.Get("Main.Undo.Rules"),
            _ => Texts.Get("Main.Undo.Files"),
        };
        SessionLog.Write("Undo", undone);
        return new UndoResult(undone, IsDone: true);
    }

    /// <summary>Redo: puts back the state an Undo took away.</summary>
    public UndoResult Redo()
    {
        marking.RunIfAsked();
        if (files.IsBusy) return new UndoResult(Texts.Get("Main.Undo.Busy"), IsDone: false);
        if (redoSteps.Count == 0) return new UndoResult(Texts.Get("Main.Redo.Nothing"), IsDone: false);
        UndoState was = now;
        undoSteps.Add(now);
        now = redoSteps[^1];
        redoSteps.RemoveAt(redoSteps.Count - 1);
        PutBack(now);
        OpenModChanged(was, now);
        string redone = WhereChanged(was, now) switch
        {
            ChangePlace.Armory => Texts.Get("Main.Redo.Armory"),
            ChangePlace.Rules => Texts.Get("Main.Redo.Rules"),
            _ => Texts.Get("Main.Redo.Files"),
        };
        SessionLog.Write("Redo", redone);
        return new UndoResult(redone, IsDone: true);
    }

    /// <summary>
    /// Opens the mod whose change Undo or Redo just took back or put back, when another mod is open: a change made in
    /// mod A, then mod B opened, is undone with mod A shown again, so the change can be seen going.
    /// </summary>
    private void OpenModChanged(UndoState one, UndoState other)
    {
        Dictionary<string, string> before = PrintByMod(one), after = PrintByMod(other);
        string? changed = after.Keys.FirstOrDefault(name => before.TryGetValue(name, out string? print) && print != after[name]);
        if (changed is null || changed == data.OpenMod.Name || data.ModStore.Find(changed) is not Mod mod) return;
        data.Open(mod);
    }

    /// <summary>A state's mods' fingerprints (see <see cref="PrintOf"/>), by the mod's name.</summary>
    private static Dictionary<string, string> PrintByMod(UndoState state)
    {
        var byMod = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (FiledMod mod in state.Mods) byMod[mod.Copy.Name] = mod.Print;
        return byMod;
    }

    /// <summary>Where a change was made, for the message saying what Undo or Redo did.</summary>
    private enum ChangePlace { Files, Rules, Armory }

    /// <summary>
    /// Where the change between two states was: the Armory for a division change or a unit added (which change the
    /// Armory presets too), the Rules tab for another change to the mods, else the file list (a mod's folders are set
    /// from it).
    /// </summary>
    private static ChangePlace WhereChanged(UndoState one, UndoState other)
    {
        if (ArmoryPart(one.Mods) != ArmoryPart(other.Mods)) return ChangePlace.Armory;
        if (!SamePrints(one.ModPrints, other.ModPrints) || StackPart(one.FoldersText) != StackPart(other.FoldersText)) return ChangePlace.Rules;
        return ChangePlace.Files;
    }

    /// <summary>The mods' order and which are on, from a <see cref="FoldersText"/>.</summary>
    private static string StackPart(string foldersText) => JsonNode.Parse(foldersText)?["stack"]?.ToJsonString() ?? "";

    /// <summary>The filed mods' division changes, units added and unit changes, with the mods they are in.</summary>
    private static string ArmoryPart(IEnumerable<FiledMod> mods)
    {
        // each line with its mod's name, so adding or renaming a mod without divisions or units is not an Armory change
        var part = new StringBuilder();
        foreach (Mod mod in mods.Select(filed => filed.Copy))
        {
            foreach (Data.Divisions.ModDivision division in mod.Divisions)
                part.Append(mod.Name).Append("|division ").Append(division.ToJson().ToJsonString()).Append('\n');
            foreach (Data.Units.ModUnit unit in mod.Units)
                part.Append(mod.Name).Append("|unit ").Append(unit.ToJson().ToJsonString()).Append('\n');
            foreach (Data.Units.UnitEdit edit in mod.UnitEdits)
                part.Append(mod.Name).Append("|unit edit ").Append(edit.ToJson().ToJsonString()).Append('\n');
        }
        return part.ToString();
    }

    /// <summary>Forgets every change, after "Clear all data": there is nothing to undo.</summary>
    public void Forget()
    {
        undoSteps.Clear();
        redoSteps.Clear();
        // the next change is a step of its own, even within the moment of the last one
        isInOneAction = false;
        modsPrints = null;
        modsPrintsShape = null;
        now = Here();
        Changed?.Invoke();
    }

    /// <summary>The state as it is now.</summary>
    private UndoState Here()
    {
        IReadOnlyList<string> modsNow = ModsPrintsNow();
        List<UndoRow> rows = [.. files.Entries.Select(UndoRow.Of)];
        return new UndoState(File(data.Mods, modsNow, null), rows, RowsText(rows), FoldersText(data));
    }

    /// <summary>
    /// Puts a filed state back (the HTML app's <c>histApply</c>). The mods get unpacked copies of the filed ones, so the
    /// history keeps its own, packed. A mod that comes back is opened; when the open mod is gone, the first one is.
    /// </summary>
    private void PutBack(UndoState state)
    {
        isPuttingBack = true;
        try
        {
            var hadMods = data.Mods.Select(m => m.Name).ToHashSet();
            List<Mod> mods = [.. state.Mods.Select(m => m.Copy.DeepCopy())];
            // the watermarks stay as they are now, as in the HTML app
            foreach (Mod mod in mods)
                if (data.ModStore.Find(mod.Name) is Mod current) mod.Watermark = current.Watermark;
            data.ModStore.Restore(mods);

            PutFoldersBack(state.FoldersText);
            string? cameBack = data.Mods.Select(m => m.Name).FirstOrDefault(name => !hadMods.Contains(name));
            if (cameBack is not null) data.State["openMod"] = cameBack;
            else if (data.ModStore.Find((string?)data.State["openMod"] ?? "") is null) data.State["openMod"] = data.Mods[0].Name;
            data.SaveState();

            files.PutBack([.. state.Rows.Select(row => row.ToSaved())]);
            modsPrints = state.ModPrints;
            modsPrintsShape = null;
            // not as another mod opened: the rows keep the presets they had
            data.TellModsChanged();
        }
        finally
        {
            isPuttingBack = false;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Each mod's folders as one text, to tell whether they changed and to put them back: the folder its new files
    /// start with ("exportFolders") and the WARNO mod folder its UNITS.csv goes into ("gameMods"), both kept by mod name;
    /// and the mods' order on the Mods tab and which of them are on (<see cref="Mods.ModStack"/>).
    /// </summary>
    private static string FoldersText(AppData data) => new JsonObject
    {
        ["exportFolders"] = data.State["exportFolders"]?.DeepClone(),
        ["gameMods"] = data.State["gameMods"]?.DeepClone(),
        // the mods' order and which are on, which change what an export writes
        ["stack"] = Mods.ModStack.Saved(),
    }.ToJsonString();

    /// <summary>Puts each mod's folders back as filed (see <see cref="FoldersText"/>).</summary>
    private void PutFoldersBack(string foldersText)
    {
        if (JsonNode.Parse(foldersText) is not JsonObject folders) return;
        data.State["exportFolders"] = folders["exportFolders"]?.DeepClone();
        data.State["gameMods"] = folders["gameMods"]?.DeepClone();
        if (folders["stack"] is JsonObject stack) Mods.ModStack.PutBack(stack);
    }

    /// <summary>The mods' fingerprints (see <see cref="PrintOf"/>), worked out again when the mods have changed since.</summary>
    private IReadOnlyList<string> ModsPrintsNow()
    {
        if (modsPrints is null)
        {
            modsPrints = [.. data.Mods.Select(PrintOf)];
            modsPrintsShape = ShapeOf(data.Mods);
        }
        return modsPrints;
    }

    /// <summary>
    /// What the mods are made of, without their contents: each mod, its name and game, and each of its lists (rule
    /// sets, each with its rules and files; division, unit and weapon changes) with how many it holds. The same shape
    /// means no mod was added, taken away or put back from Undo.
    /// </summary>
    private static List<object?> ShapeOf(IEnumerable<Mod> mods)
    {
        var shape = new List<object?>();
        foreach (Mod mod in mods)
        {
            shape.AddRange([mod, mod.Name, mod.Game, mod.Presets, mod.Presets.Count]);
            foreach (ModPreset preset in mod.Presets)
                // the version and count rather than the rules themselves: reading them would unpack every rule set put away
                shape.AddRange([preset, preset.Name, preset.RulesVersion, preset.RuleCount, preset.AppliesTo, preset.AppliesTo?.Count]);
            shape.AddRange([mod.Divisions, mod.Divisions.Count, mod.Units, mod.Units.Count, mod.UnitEdits, mod.UnitEdits.Count,
                mod.WeaponEdits, mod.WeaponEdits.Count, mod.Weapons, mod.Weapons.Count]);
        }
        return shape;
    }

    /// <summary>
    /// A fingerprint of a mod's name, game, presets, rules, division, unit and weapon changes, to tell whether it changed.
    /// Tuned for memory: each state's mods were compared by their whole text, held unpacked for the state now even with
    /// nothing to undo: some 90 MB for mods of 65 MB on disk. A fingerprint takes a few bytes, and no such text is ever
    /// put together. Keep it that way when tidying.
    /// </summary>
    private static string PrintOf(Mod mod)
    {
        using var print = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text) => print.AppendData(MemoryMarshal.AsBytes(text.AsSpan()));
        Add("mod " + mod.Name + "\n");
        Add("game " + mod.Game.Id + "\n");
        foreach (ModPreset preset in mod.Presets)
        {
            Add("preset " + preset.Name + "\n");
            Add(preset.RulesText());
            Add("\napplies to " + (preset.AppliesTo is null ? "-" : string.Join("|", preset.AppliesTo)) + "\n");
        }
        foreach (Data.Divisions.ModDivision division in mod.Divisions) Add("division " + division.ToJson().ToJsonString() + "\n");
        foreach (Data.Units.ModUnit unit in mod.Units) Add("unit " + unit.ToJson().ToJsonString() + "\n");
        foreach (Data.Units.UnitEdit edit in mod.UnitEdits) Add("unit edit " + edit.ToJson().ToJsonString() + "\n");
        foreach (Data.Weapons.WeaponEdit edit in mod.WeaponEdits) Add("weapon edit " + edit.ToJson().ToJsonString() + "\n");
        foreach (Data.Weapons.ModWeapon weapon in mod.Weapons) Add("weapon " + weapon.ToJson().ToJsonString() + "\n");
        return Convert.ToHexString(print.GetHashAndReset());
    }

    /// <summary>
    /// Whether the file list is still as last filed: the same rows in the same order, each with the same On box and
    /// export folder.
    /// </summary>
    private bool RowsAreAsFiled()
    {
        IReadOnlyList<FileEntry> entries = files.Entries;
        IReadOnlyList<UndoRow> filed = now.Rows;
        if (entries.Count != filed.Count) return false;
        for (int i = 0; i < filed.Count; i++)
        {
            FileEntry entry = entries[i];
            UndoRow row = filed[i];
            if (!ReferenceEquals(entry, row.Entry) || entry.On != row.On || entry.ExportFolder != row.ExportFolder)
                return false;
        }
        return true;
    }

    /// <summary>The file rows as one text, to tell whether they changed.</summary>
    private static string RowsText(IEnumerable<UndoRow> rows) =>
        string.Join("\n", rows.Select(r => r.Entry.Id + "|" + (r.On ? 1 : 0) + "|" + r.ExportFolder));

    /// <summary>Whether two lists of the mods' fingerprints are the same.</summary>
    private static bool SamePrints(IReadOnlyList<string> one, IReadOnlyList<string> other) =>
        ReferenceEquals(one, other) || one.Count == other.Count && one.Zip(other).All(pair => pair.First == pair.Second);

    /// <summary>
    /// The mods as filed: copies with their rules packed. Tuned for speed and memory: each step kept every mod as JSON
    /// objects, some hundreds of MB a step for a mod of 8,000 rules (WARNO ACTUAL); packed, a few MB. A mod as it was in
    /// the state before shares that state's packed rules, so a change to one mod packs that one only. Keep it that way
    /// when tidying.
    /// </summary>
    /// <param name="mods">The mods now.</param>
    /// <param name="prints">Their fingerprints (see <see cref="PrintOf"/>).</param>
    /// <param name="before">The state filed before, whose packed mods can be shared; null for none.</param>
    private static List<FiledMod> File(IReadOnlyList<Mod> mods, IReadOnlyList<string> prints, UndoState? before)
    {
        var earlier = new Dictionary<string, FiledMod>(StringComparer.Ordinal);
        if (before is not null)
            foreach (FiledMod mod in before.Mods) earlier[mod.Copy.Name] = mod;
        var filed = new List<FiledMod>(mods.Count);
        for (int i = 0; i < mods.Count; i++)
        {
            bool same = earlier.TryGetValue(mods[i].Name, out FiledMod? was) && was.Print == prints[i];
            filed.Add(new FiledMod(mods[i].DeepCopy(packRules: true, sameRulesAs: same ? was!.Copy : null), prints[i]));
        }
        return filed;
    }

    /// <summary>A mod as filed: a copy with its rules packed, and its fingerprint (see <see cref="PrintOf"/>).</summary>
    private sealed record FiledMod(Mod Copy, string Print);

    /// <summary>One filed state: the mods (packed copies), the file rows, the mods' folders, and what compares them.</summary>
    private sealed class UndoState(IReadOnlyList<FiledMod> mods, IReadOnlyList<UndoRow> rows, string rowsText, string foldersText)
    {
        public IReadOnlyList<FiledMod> Mods => mods;

        /// <summary>Each mod's fingerprint (see <see cref="PrintOf"/>), in the mods' order.</summary>
        public IReadOnlyList<string> ModPrints { get; } = [.. mods.Select(mod => mod.Print)];

        public IReadOnlyList<UndoRow> Rows => rows;

        public string RowsText => rowsText;

        public string FoldersText => foldersText;
    }

    /// <summary>
    /// About how much memory the history takes, for the usage readout: the packed rules of each step's mods (counted once
    /// where steps share them), and the texts the file rows are compared by. The copies' division, unit and weapon changes are left out: they are small beside the rules.
    /// </summary>
    public long AboutBytes()
    {
        var counted = new HashSet<object>(ReferenceEqualityComparer.Instance);
        long bytes = 0;
        foreach (UndoState state in undoSteps.Concat(redoSteps).Append(now))
        {
            foreach (FiledMod mod in state.Mods)
            {
                foreach (ModPreset preset in mod.Copy.Presets)
                    if (preset.PackedRules is PackedBytes packed && counted.Add(packed)) bytes += packed.PackedLength;
            }
            if (counted.Add(state.RowsText)) bytes += 2L * state.RowsText.Length;
        }
        return bytes;
    }

    /// <summary>A file row as filed: the row, its On box and its export folder.</summary>
    private sealed record UndoRow(FileEntry Entry, bool On, string? ExportFolder)
    {
        /// <summary>The row as it is now.</summary>
        public static UndoRow Of(FileEntry entry) => new(entry, entry.On, entry.ExportFolder);

        /// <summary>The row as it was filed, for the file list to put back.</summary>
        public SavedFileRow ToSaved() => new(Entry, On, ExportFolder);
    }
}

/// <summary>What Undo or Redo did.</summary>
/// <param name="Message">What to tell the user, such as "Undone: a change to the file list." or "Nothing to undo."</param>
/// <param name="IsDone">Whether a change was undone or redone.</param>
public sealed record UndoResult(string Message, bool IsDone);
