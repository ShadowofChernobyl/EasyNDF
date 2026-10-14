using System.Text;
using System.Windows.Threading;
using EasyNdf.App.Files;
using EasyNdf.Data;
using EasyNdf.Data.FileList;

namespace EasyNdf.App;

/// <summary>
/// Undo and Redo (the HTML app's <c>hist*</c> functions). Everything kept in the mods, and the file list (which rows it
/// has, in which order, and each row's preset, On checkbox and export folder), is one state; each change files the state
/// before it, and Undo puts a filed state back. Undo on one tab can so take back a change made on the other. A removed
/// file row is kept whole in the history, so Undo brings it back as it was. The open mod and the watermarks are not part
/// of the state, as in the HTML app.
/// </summary>
public sealed class UndoHistory
{
    /// <summary>How many changes can be undone ("Undo steps", 60 unless set); older ones are forgotten.</summary>
    public static int MaximumSteps => int.TryParse(AppData.Current.Settings["undoSteps"]?.ToString(), out int steps) && steps > 0 ? steps : 60;

    /// <summary>Typing in one box within this time of the last key counts as the same change.</summary>
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(1500);

    private readonly AppData data;
    private readonly FileListModel files;
    private readonly List<UndoState> undoSteps = [];
    private readonly List<UndoState> redoSteps = [];
    private UndoState now;

    /// <summary>The mods' part of <see cref="now"/>'s comparison text, worked out again only after the mods change.</summary>
    private string? modsText;

    /// <summary>While a filed state is put back, the changes that makes are not filed.</summary>
    private bool isPuttingBack;

    /// <summary>The box being typed in (see <see cref="TypingIn"/>), and the one typed in last, and when.</summary>
    private string typingKey = "", lastTypingKey = "";
    private DateTime lastTypingAt;

    /// <summary>Set while one action is being filed: what it changes until the app next rests is one step.</summary>
    private bool isInOneAction;

    /// <summary>Starts with nothing to undo, from the mods and the file list as they are.</summary>
    public UndoHistory(AppData data, FileListModel files)
    {
        this.data = data;
        this.files = files;
        now = Here();
    }

    /// <summary>Raised when <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed.</summary>
    public event Action? Changed;

    /// <summary>Whether there is a change to undo.</summary>
    public bool CanUndo => undoSteps.Count > 0;

    /// <summary>Whether there is an undone change to redo.</summary>
    public bool CanRedo => redoSteps.Count > 0;

    /// <summary>
    /// Says which box the next change is typed in, such as a rule's name: keys typed into one box, one after another,
    /// are one change to undo, not one per key.
    /// </summary>
    public void TypingIn(string key) => typingKey = key;

    /// <summary>The mods have changed: the next <see cref="Mark"/> compares them again.</summary>
    public void ModsChanged()
    {
        modsText = null;
        Mark();
    }

    /// <summary>
    /// Files the state before a change, if the mods or the file list differ from the last state filed (the HTML app's
    /// <c>histMark</c>). Call it after every change; nothing is filed when nothing changed.
    /// </summary>
    public void Mark()
    {
        if (isPuttingBack) return;
        modsText ??= ModsText(data.Mods);
        List<UndoRow> rows = [.. files.Entries.Select(entry => UndoRow.Of(entry, data))];
        string rowsText = RowsText(rows);
        if (modsText == now.ModsText && rowsText == now.RowsText) return;

        string key = typingKey;
        typingKey = "";
        DateTime time = DateTime.UtcNow;
        bool sameTyping = key != "" && key == lastTypingKey && time - lastTypingAt < TypingPause;
        // one action is one step, even when it saves in several parts
        if (!sameTyping && !isInOneAction)
        {
            undoSteps.Add(now);
            while (undoSteps.Count > MaximumSteps) undoSteps.RemoveAt(0);
        }
        if (!isInOneAction)
        {
            isInOneAction = true;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => isInOneAction = false);
        }
        lastTypingKey = key;
        lastTypingAt = time;
        redoSteps.Clear();
        // the mods are copied only now that they are filed: a click that changes nothing copies nothing
        now = new UndoState(modsText, [.. data.Mods.Select(m => m.DeepCopy())], rows, rowsText);
        Changed?.Invoke();
    }

    /// <summary>Undo: puts back the state before the last change.</summary>
    public UndoResult Undo()
    {
        if (files.IsBusy) return new UndoResult("Wait for the run to finish.", IsDone: false);
        if (undoSteps.Count == 0) return new UndoResult("Nothing to undo.", IsDone: false);
        UndoState was = now;
        redoSteps.Add(now);
        now = undoSteps[^1];
        undoSteps.RemoveAt(undoSteps.Count - 1);
        PutBack(now);
        // the message names where the change was, since Undo on one tab can take back a change made on the other
        return new UndoResult(was.ModsText != now.ModsText ? "Undone: a change on the Rules tab." : "Undone: a change to the file list.", IsDone: true);
    }

    /// <summary>Redo: puts back the state an Undo took away.</summary>
    public UndoResult Redo()
    {
        if (files.IsBusy) return new UndoResult("Wait for the run to finish.", IsDone: false);
        if (redoSteps.Count == 0) return new UndoResult("Nothing to redo.", IsDone: false);
        UndoState was = now;
        undoSteps.Add(now);
        now = redoSteps[^1];
        redoSteps.RemoveAt(redoSteps.Count - 1);
        PutBack(now);
        return new UndoResult(was.ModsText != now.ModsText ? "Redone: a change on the Rules tab." : "Redone: a change to the file list.", IsDone: true);
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
        List<UndoRow> rows = [.. files.Entries.Select(entry => UndoRow.Of(entry, data))];
        return new UndoState(modsText, [.. data.Mods.Select(m => m.DeepCopy())], rows, RowsText(rows));
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
            // the watermarks stay as they are now
            foreach (Mod mod in mods)
                if (data.ModStore.Find(mod.Name) is Mod current) mod.Watermark = current.Watermark;
            data.ModStore.Restore(mods);

            string? cameBack = data.Mods.Select(m => m.Name).FirstOrDefault(name => !hadMods.Contains(name));
            if (cameBack is not null) data.State["openMod"] = cameBack;
            else if (data.ModStore.Find((string?)data.State["openMod"] ?? "") is null) data.State["openMod"] = data.Mods[0].Name;
            data.SaveState();

            files.PutBack([.. state.Rows.Select(row => row.ToSaved(data))]);
            modsText = state.ModsText;
            // typing after Undo or Redo is a new step, even in the box typed in before
            lastTypingKey = "";
            // not as another mod opened: the rows keep the presets they had
            data.TellModsChanged();
        }
        finally
        {
            isPuttingBack = false;
        }
        Changed?.Invoke();
    }

    /// <summary>The mods' names, presets and rules as one text, to tell whether they changed.</summary>
    private static string ModsText(IEnumerable<Mod> mods)
    {
        var text = new StringBuilder();
        foreach (Mod mod in mods)
        {
            text.Append("mod ").Append(mod.Name).Append('\n');
            foreach (ModPreset preset in mod.Presets)
                text.Append("preset ").Append(preset.Name).Append('\n').Append(preset.Rules.ToJsonString()).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>The file rows as one text, to tell whether they changed.</summary>
    private static string RowsText(IEnumerable<UndoRow> rows) =>
        string.Join("\n", rows.Select(r => r.Entry.Id + "|" + r.ModName + "|" + r.PresetName + "|" + r.MissingPreset + "|" + (r.On ? 1 : 0) + "|" + r.ExportFolder));

    /// <summary>One filed state: the mods (copies), the file rows, and the texts that compare them.</summary>
    private sealed record UndoState(string ModsText, IReadOnlyList<Mod> Mods, IReadOnlyList<UndoRow> Rows, string RowsText);

    /// <summary>
    /// A file row as filed. Its preset is kept by name, since putting the mods back makes new presets.
    /// </summary>
    private sealed record UndoRow(FileEntry Entry, string? ModName, string? PresetName, string? MissingPreset, bool On, string? ExportFolder)
    {
        /// <summary>The row as it is now.</summary>
        public static UndoRow Of(FileEntry entry, AppData data)
        {
            Mod? mod = entry.Preset is null ? null : data.ModOf(entry.Preset);
            return new UndoRow(entry, mod?.Name, mod is null ? null : entry.Preset!.Name, entry.MissingPreset, entry.On, entry.ExportFolder);
        }

        /// <summary>The row with its preset found again among the mods put back.</summary>
        public SavedFileRow ToSaved(AppData data)
        {
            ModPreset? preset = ModName is null ? null : data.ModStore.Find(ModName)?.Presets.FirstOrDefault(p => p.Name == PresetName);
            return new SavedFileRow(Entry, preset, MissingPreset, On, ExportFolder);
        }
    }
}

/// <summary>What Undo or Redo did.</summary>
/// <param name="Message">What to tell the user, such as "Undone: a change to the file list." or "Nothing to undo."</param>
/// <param name="IsDone">Whether a change was undone or redone.</param>
public sealed record UndoResult(string Message, bool IsDone);
