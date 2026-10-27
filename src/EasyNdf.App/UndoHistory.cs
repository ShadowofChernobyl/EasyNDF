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

    /// <summary>The mods' part of <see cref="now"/>'s comparison text, worked out again only after the mods change.</summary>
    private string? modsText;

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

    /// <summary>The mods have changed: the next <see cref="Mark"/> compares them again.</summary>
    public void ModsChanged()
    {
        modsText = null;
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
        modsText ??= ModsText(data.Mods);
        string foldersText = FoldersText(data);
        // most changes to the list are not the user's (a file read, a status shown): this check needs no copying
        if (modsText == now.ModsText && foldersText == now.FoldersText && RowsAreAsFiled()) return;
        List<UndoRow> rows = [.. files.Entries.Select(UndoRow.Of)];
        string rowsText = RowsText(rows);
        if (modsText == now.ModsText && foldersText == now.FoldersText && rowsText == now.RowsText) return;

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
        // the mods are copied only now that they are filed: a click that changes nothing copies nothing
        now = new UndoState(modsText, [.. data.Mods.Select(m => m.DeepCopy())], rows, rowsText, foldersText);
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
        return new UndoResult(redone, IsDone: true);
    }

    /// <summary>
    /// Opens the mod whose change Undo or Redo just took back or put back, when another mod is open: a change made in
    /// mod A, then mod B opened, is undone with mod A shown again, so the change can be seen going.
    /// </summary>
    private void OpenModChanged(UndoState one, UndoState other)
    {
        Dictionary<string, string> before = TextByMod(one.ModsText), after = TextByMod(other.ModsText);
        string? changed = after.Keys.FirstOrDefault(name => before.TryGetValue(name, out string? text) && text != after[name]);
        if (changed is null || changed == data.OpenMod.Name || data.ModStore.Find(changed) is not Mod mod) return;
        data.Open(mod);
    }

    /// <summary>A <see cref="ModsText"/> split into each mod's part, by the mod's name.</summary>
    private static Dictionary<string, string> TextByMod(string modsText)
    {
        var byMod = new Dictionary<string, string>(StringComparer.Ordinal);
        string? mod = null;
        var text = new StringBuilder();
        foreach (string line in modsText.Split('\n'))
        {
            if (line.StartsWith("mod ", StringComparison.Ordinal))
            {
                if (mod is not null) byMod[mod] = text.ToString();
                mod = line[4..];
                text.Clear();
                continue;
            }
            text.Append(line).Append('\n');
        }
        if (mod is not null) byMod[mod] = text.ToString();
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
        if (ArmoryPart(one.ModsText) != ArmoryPart(other.ModsText)) return ChangePlace.Armory;
        if (one.ModsText != other.ModsText || StackPart(one.FoldersText) != StackPart(other.FoldersText)) return ChangePlace.Rules;
        return ChangePlace.Files;
    }

    /// <summary>The mods' order and which are on, from a <see cref="FoldersText"/>.</summary>
    private static string StackPart(string foldersText) => JsonNode.Parse(foldersText)?["stack"]?.ToJsonString() ?? "";

    /// <summary>The lines of a <see cref="ModsText"/> for the division changes and units added, with the mods they are in.</summary>
    private static string ArmoryPart(string modsText)
    {
        // each line with its mod's name, so adding or renaming a mod without divisions or units is not an Armory change
        var part = new StringBuilder();
        string mod = "";
        foreach (string line in modsText.Split('\n'))
        {
            if (line.StartsWith("mod ", StringComparison.Ordinal)) mod = line;
            else if (line.StartsWith("division ", StringComparison.Ordinal) || line.StartsWith("unit ", StringComparison.Ordinal))
                part.Append(mod).Append('|').Append(line).Append('\n');
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
        modsText = null;
        now = Here();
        Changed?.Invoke();
    }

    /// <summary>The state as it is now.</summary>
    private UndoState Here()
    {
        modsText ??= ModsText(data.Mods);
        List<UndoRow> rows = [.. files.Entries.Select(UndoRow.Of)];
        return new UndoState(modsText, [.. data.Mods.Select(m => m.DeepCopy())], rows, RowsText(rows), FoldersText(data));
    }

    /// <summary>
    /// Puts a filed state back (the HTML app's <c>histApply</c>). The mods get copies of the filed ones, so the history
    /// keeps its own. A mod that comes back is opened; when the open mod is gone, the first one is.
    /// </summary>
    private void PutBack(UndoState state)
    {
        isPuttingBack = true;
        try
        {
            var hadMods = data.Mods.Select(m => m.Name).ToHashSet();
            List<Mod> mods = [.. state.Mods.Select(m => m.DeepCopy())];
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
            modsText = state.ModsText;
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

    /// <summary>The mods' names, presets, rules, division, unit and weapon changes as one text, to tell whether they changed.</summary>
    private static string ModsText(IEnumerable<Mod> mods)
    {
        var text = new StringBuilder();
        foreach (Mod mod in mods)
        {
            text.Append("mod ").Append(mod.Name).Append('\n');
            foreach (ModPreset preset in mod.Presets)
                text.Append("preset ").Append(preset.Name).Append('\n').Append(preset.Rules.ToJsonString()).Append('\n')
                    .Append("applies to ").Append(preset.AppliesTo is null ? "-" : string.Join("|", preset.AppliesTo)).Append('\n');
            foreach (Data.Divisions.ModDivision division in mod.Divisions)
                text.Append("division ").Append(division.ToJson().ToJsonString()).Append('\n');
            foreach (Data.Units.ModUnit unit in mod.Units)
                text.Append("unit ").Append(unit.ToJson().ToJsonString()).Append('\n');
            foreach (Data.Units.UnitEdit edit in mod.UnitEdits)
                text.Append("unit edit ").Append(edit.ToJson().ToJsonString()).Append('\n');
            foreach (Data.Weapons.WeaponEdit edit in mod.WeaponEdits)
                text.Append("weapon edit ").Append(edit.ToJson().ToJsonString()).Append('\n');
            foreach (Data.Weapons.ModWeapon weapon in mod.Weapons)
                text.Append("weapon ").Append(weapon.ToJson().ToJsonString()).Append('\n');
        }
        return text.ToString();
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

    /// <summary>One filed state: the mods (copies), the file rows, the mods' folders, and the texts that compare them.</summary>
    private sealed record UndoState(string ModsText, IReadOnlyList<Mod> Mods, IReadOnlyList<UndoRow> Rows, string RowsText, string FoldersText);

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
