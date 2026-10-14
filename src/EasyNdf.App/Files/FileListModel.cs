using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using EasyNdf.Engine;
using EasyNdf.Engine.Export;
using EasyNdf.Engine.Ndf;

namespace EasyNdf.App.Files;

/// <summary>
/// The file list behind the Files tab: its rows, which are shown and in what order, which are selected, and the
/// buttons' states. The screen (Pages/FilesPage.xaml) shows it and passes the clicks on.
/// </summary>
public sealed class FileListModel : INotifyPropertyChanged
{
    private readonly List<FileEntry> entries = [];
    private readonly Dictionary<FileEntry, FileRow> rowOf = [];
    private FileRow? lastClicked;
    private int nextId = 1;

    /// <summary>Starts an empty list for the open mod's presets.</summary>
    /// <param name="openModPresets">The open mod's presets.</param>
    /// <param name="allPresetNames">The names of every mod's presets: the longest sets the Preset column's width.</param>
    public FileListModel(IReadOnlyList<ModPreset> openModPresets, IReadOnlyList<string> allPresetNames)
    {
        AllPresetNames = allPresetNames;
        OpenModPresets = openModPresets;
        PresetChoices = [PresetChoice.None, .. openModPresets.Select(p => new PresetChoice(p))];
        Refilter(); // so the empty list says "No files yet"
    }

    /// <summary>
    /// Raised when something the app keeps between sessions has changed: the columns, their order or widths, the sort,
    /// or "Group by folder". The page saves them (see <see cref="SaveView"/>).
    /// </summary>
    public event Action? ViewChanged;

    /// <summary>
    /// Takes the view the app saved last time: columns, order and sort from the settings (the HTML app's <c>cols</c>,
    /// <c>sort</c> and <c>groupFolders</c>), and column widths from what it remembers (<c>cols.jobsCols</c>).
    /// </summary>
    public void LoadView(JsonObject settings, JsonObject state)
    {
        if (settings["cols"] is JsonObject columns)
        {
            List<FileColumn> order = IdsToColumns(columns["order"] as JsonArray);
            foreach (FileColumn column in DefaultColumnOrder) if (!order.Contains(column)) order.Add(column);
            columnOrder = order;
            hiddenColumns = [.. IdsToColumns(columns["hidden"] as JsonArray)];
        }
        if (settings["sort"] is JsonObject sort && FileColumnIds.FromId((string?)sort["id"]) is FileColumn sortColumn && FileListView.CanSort(sortColumn))
        {
            SortColumn = sortColumn;
            SortAscending = sort["dir"]?.GetValue<int>() != -1;
        }
        groupByFolder = settings["groupFolders"]?.GetValue<bool>() == true;
        if (state["cols"]?["jobsCols"] is JsonObject widths)
        {
            manualWidths = [];
            foreach (var (id, width) in widths)
                if (FileColumnIds.FromId(id) is FileColumn column && width?.GetValue<double>() is double value && value >= NarrowestColumn)
                    manualWidths[column] = value;
        }
        Refilter();
    }

    /// <summary>Writes the view into the settings and what the app remembers, in the HTML app's form.</summary>
    public void SaveView(JsonObject settings, JsonObject state)
    {
        settings["cols"] = new JsonObject
        {
            ["order"] = new JsonArray([.. columnOrder.Select(c => (JsonNode)FileColumnIds.Id(c))]),
            ["hidden"] = new JsonArray([.. columnOrder.Where(hiddenColumns.Contains).Select(c => (JsonNode)FileColumnIds.Id(c))]),
        };
        settings["sort"] = new JsonObject { ["id"] = FileColumnIds.Id(SortColumn), ["dir"] = SortAscending ? 1 : -1 };
        settings["groupFolders"] = groupByFolder;
        if (state["cols"] is not JsonObject remembered) state["cols"] = remembered = [];
        if (manualWidths is null) remembered.Remove("jobsCols");
        else remembered["jobsCols"] = new JsonObject(manualWidths.Select(pair => KeyValuePair.Create(FileColumnIds.Id(pair.Key), (JsonNode?)pair.Value)));
    }

    private static List<FileColumn> IdsToColumns(JsonArray? ids) =>
        ids is null ? [] : [.. ids.Select(id => FileColumnIds.FromId((string?)id)).OfType<FileColumn>().Distinct()];

    /// <summary>Another mod was opened: new files get its presets, and the rows' dropdowns offer them.</summary>
    public void OpenMod(IReadOnlyList<ModPreset> presets)
    {
        OpenModPresets = presets;
        PresetChoices = [PresetChoice.None, .. presets.Select(p => new PresetChoice(p))];
        Changed();
    }

    /// <summary>The names of every mod's presets.</summary>
    public IReadOnlyList<string> AllPresetNames { get; private set; }

    /// <summary>
    /// The mods changed (see <see cref="AppData.ModsChanged"/>): a row whose preset was deleted has none now; when another
    /// mod was opened, every row gets the preset of that mod its name points to (the HTML app's <c>syncFilesToMod</c>);
    /// when presets were pasted or imported, rows without one may get one. Returns the rows to read now: those with a
    /// preset that have not been read.
    /// </summary>
    public IReadOnlyList<FileEntry> FollowMods(IReadOnlyList<Mod> mods, Mod openMod, ModsChange change)
    {
        var allPresets = mods.SelectMany(m => m.Presets).ToHashSet();
        AllPresetNames = [.. allPresets.Select(p => p.Name)];
        OpenMod(openMod.Presets);
        bool autoAssign = AppData.Current.Settings["autoAssign"]?.GetValue<bool>() != false;
        foreach (FileEntry entry in entries)
        {
            ModPreset? preset = entry.Preset;
            if (preset is not null && !allPresets.Contains(preset)) preset = null;
            if (change.OpenModChanged) preset = autoAssign ? PresetMatching.Match(entry.Name, entry.Folder, OpenModPresets) : null;
            else if (change.PresetsAdded && preset is null && autoAssign) preset = PresetMatching.Match(entry.Name, entry.Folder, OpenModPresets);
            if (ReferenceEquals(preset, entry.Preset)) continue;
            entry.Preset = preset;
            entry.ClearMessage();
        }
        Refilter();
        return entries.Where(e => e.Source is not null && e.Preset is not null && !e.Scanned).ToList();
    }

    /// <summary>Every column, in the order they come in; the Columns popup lists them so.</summary>
    public IReadOnlyList<FileColumn> ColumnOrder => columnOrder;
    private List<FileColumn> columnOrder = [.. DefaultColumnOrder];

    /// <summary>The columns in the order a new list has them, as the HTML app's <c>COLS</c>.</summary>
    private static readonly FileColumn[] DefaultColumnOrder =
        [FileColumn.On, FileColumn.Number, FileColumn.File, FileColumn.Size, FileColumn.Lines, FileColumn.Modified, FileColumn.Preset, FileColumn.ExportFolder, FileColumn.Status];

    /// <summary>The columns the Columns popup has hidden. Source file is always shown.</summary>
    private HashSet<FileColumn> hiddenColumns = [FileColumn.Lines, FileColumn.Modified, FileColumn.ExportFolder];

    /// <summary>The columns shown, in their order.</summary>
    public IReadOnlyList<FileColumn> ShownColumns => columnOrder.Where(c => c == FileColumn.File || !hiddenColumns.Contains(c)).ToList();

    /// <summary>Whether a column is shown.</summary>
    public bool IsShown(FileColumn column) => column == FileColumn.File || !hiddenColumns.Contains(column);

    /// <summary>
    /// Column widths set by dragging the lines between the headings; null while the columns fit themselves. Once one
    /// line is dragged, every column keeps a width of its own.
    /// </summary>
    private Dictionary<FileColumn, double>? manualWidths;

    /// <summary>The narrowest a dragged column can be.</summary>
    public const double NarrowestColumn = 28;

    /// <summary>A column's width when it has none of its own yet (the HTML app's <c>COLS[id].w</c>).</summary>
    private static double StartingWidth(FileColumn column) => column switch
    {
        FileColumn.On => 40, FileColumn.Number => 44, FileColumn.File => 240, FileColumn.Size => 80, FileColumn.Lines => 70,
        FileColumn.Modified => 130, FileColumn.Preset => 240, FileColumn.ExportFolder => 220, _ => 300,
    };

    /// <summary>A Columns popup checkbox: shows or hides a column. A hidden column the list was sorted by gives way to Source file.</summary>
    public void SetColumnShown(FileColumn column, bool shown)
    {
        if (column == FileColumn.File) return;
        if (shown) hiddenColumns.Remove(column);
        else hiddenColumns.Add(column);
        if (!shown && SortColumn == column) { SortColumn = FileColumn.File; SortAscending = true; }
        Refilter();
        ViewChanged?.Invoke();
    }

    /// <summary>A heading dropped on another: moves its column before that one, or after it when dropped on its right half.</summary>
    public void MoveColumn(FileColumn moved, FileColumn target, bool after)
    {
        if (moved == target) return;
        columnOrder.Remove(moved);
        columnOrder.Insert(columnOrder.IndexOf(target) + (after ? 1 : 0), moved);
        Refilter();
        ViewChanged?.Invoke();
    }

    /// <summary>"Reset columns": every column shown, in the first order, sorted by Source file, with automatic widths.</summary>
    public void ResetColumns()
    {
        columnOrder = [.. DefaultColumnOrder];
        hiddenColumns = [];
        SortColumn = FileColumn.File;
        SortAscending = true;
        manualWidths = null;
        Refilter();
        ViewChanged?.Invoke();
    }

    /// <summary>
    /// A column's line was dragged: from now on every column keeps its own width, starting from what it has now.
    /// </summary>
    /// <param name="current">Each shown column's width as drawn.</param>
    public void KeepColumnWidths(IReadOnlyDictionary<FileColumn, double> current) =>
        manualWidths ??= current.ToDictionary(pair => pair.Key, pair => Math.Max(NarrowestColumn, Math.Round(pair.Value, MidpointRounding.AwayFromZero)));

    /// <summary>A column's line was let go: its width is kept for next time.</summary>
    public void ColumnWidthsSettled() => ViewChanged?.Invoke();

    /// <summary>The width a dragged column now has.</summary>
    public void SetColumnWidth(FileColumn column, double width)
    {
        manualWidths ??= [];
        manualWidths[column] = Math.Max(NarrowestColumn, Math.Round(width, MidpointRounding.AwayFromZero));
        Changed();
    }

    /// <summary>The width a column has, for dragging its line: its own when it has one, else where it starts from.</summary>
    public double ColumnWidthFor(FileColumn column) => manualWidths?.GetValueOrDefault(column, StartingWidth(column)) ?? StartingWidth(column);

    /// <summary>Whether a column was given a width by hand, which "Reset column widths" would undo.</summary>
    public bool HasColumnWidthsByHand => manualWidths is { Count: > 0 };

    /// <summary>A double-click on a column's line: the columns fit themselves again.</summary>
    public void FitColumnsAgain()
    {
        manualWidths = null;
        Changed();
        ViewChanged?.Invoke();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The open mod's presets: the ones a new file can get, and the rows' dropdowns offer.</summary>
    public IReadOnlyList<ModPreset> OpenModPresets { get; private set; }

    /// <summary>What each row's preset dropdown offers: "(none)", then the open mod's presets.</summary>
    public IReadOnlyList<PresetChoice> PresetChoices { get; private set; }

    /// <summary>The file rows on screen, in the order shown: those that match the search and filter, less those in closed folders.</summary>
    public ObservableCollection<FileRow> Shown { get; } = [];

    /// <summary>What the list shows, in order: file rows, and with "Group by folder" a <see cref="FolderRow"/> above each folder's files.</summary>
    public ObservableCollection<object> ListItems { get; } = [];

    /// <summary>
    /// The rows that match the search and "Show only", in the order shown, in closed folders too. Select all, the
    /// selection count and Remove act on these, as in the HTML app.
    /// </summary>
    private List<FileRow> matching = [];

    /// <summary>"Group by folder": the files under a line for each folder, which opens and closes.</summary>
    public bool GroupByFolder { get => groupByFolder; set { groupByFolder = value; Refilter(); ViewChanged?.Invoke(); } }
    private bool groupByFolder;

    /// <summary>The folders opened with "Group by folder", by their path ("" for files added on their own).</summary>
    private readonly HashSet<string> openFolders = [];

    /// <summary>How many lines the list can have before it is drawn as a long list (the HTML app's <c>VIRT_MIN</c>).</summary>
    public const int LongListRows = 120;

    /// <summary>What the list says when it has no rows to show.</summary>
    public string EmptyText => entries.Count == 0
        ? "No files yet. Add .ndf files or a folder, or drop them here. A .zip or .rar works too."
        : "No files match the search and filter.";

    /// <summary>Whether the list has no rows to show: then it says <see cref="EmptyText"/>.</summary>
    public bool ShowsEmptyText => ListItems.Count == 1 && ListItems[0] is EmptyListItem;

    /// <summary>The column the list is sorted by, and which way.</summary>
    public FileColumn SortColumn { get; private set; } = FileColumn.File;

    /// <summary>Whether the list is sorted A to Z (or smallest first).</summary>
    public bool SortAscending { get; private set; } = true;

    /// <summary>The "Show only" choice.</summary>
    public ShowOnly ShowOnly { get => showOnly; set { showOnly = value; Refilter(); } }
    private ShowOnly showOnly = ShowOnly.All;

    /// <summary>The search box's text, trimmed and in small letters.</summary>
    public string Search { get => search; set { search = value.Trim().ToLowerInvariant(); Refilter(); } }
    private string search = "";

    /// <summary>The widths of the shown columns, worked out by <see cref="ColumnFit"/>.</summary>
    public ColumnWidths Columns { get; } = new();

    /// <summary>Every row, shown or not, in the order they were added.</summary>
    public IReadOnlyList<FileEntry> Entries => entries;

    /// <summary>Whether the list has no rows at all (then it says how to add files).</summary>
    public bool IsEmpty => entries.Count == 0;

    /// <summary>How many rows are selected.</summary>
    public int SelectedCount => matching.Count(r => r.IsSelected);

    /// <summary>
    /// The note above the list (the HTML app's <c>#jobNote</c>): what the last action did, and how to select several rows
    /// or how many are selected.
    /// </summary>
    public string Note
    {
        get
        {
            if (caution != "") return caution;
            string selection = entries.Count == 0 ? "" : SelectedCount == 0 ? "Ctrl-click or Shift-click selects several." : SelectedCount + " selected.";
            return string.Join(" ", new[] { flash, selection }.Where(t => t != ""));
        }
    }
    private string flash = "";

    /// <summary>A warning shown in place of the note, in the caution colour, until the list next changes.</summary>
    private string caution = "";

    /// <summary>Whether the note is a warning (drawn in the caution colour).</summary>
    public bool NoteIsCaution => caution != "";

    /// <summary>Shows a warning in place of the note, such as "Nothing to run.", until the list next changes.</summary>
    public void Warn(string message)
    {
        caution = message;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>Whether Preview or Run export is running: most buttons wait until it is done.</summary>
    public bool IsBusy
    {
        get => isBusy;
        set { isBusy = value; Changed(); }
    }
    private bool isBusy;

    /// <summary>Whether the buttons that change the list can be used now.</summary>
    public bool IsIdle => !isBusy;

    /// <summary>Whether Remove and Export folder... can be used: there is a selection, and no run is going.</summary>
    public bool CanChangeSelected => !isBusy && SelectedCount > 0;

    /// <summary>Whether Remove all can be used.</summary>
    public bool CanRemoveAll => !isBusy && entries.Count > 0;

    /// <summary>Whether Save as zip... has anything to save: files the last Run export wrote.</summary>
    public bool CanSaveZip => !isBusy && entries.Any(e => e.Done && e.ExportedPath is not null);

    /// <summary>Whether any row shown has a "Save anyway" button, which widens the buttons column.</summary>
    public bool AnyHeldOutput => Shown.Any(r => r.Entry.HeldOutput is not null);

    /// <summary>The row Details describes: the one clicked last, or the first one run.</summary>
    public FileEntry? DetailsRow { get; set; }

    /// <summary>Gives rows an export folder, and makes it the one their mods' new files start with.</summary>
    public void SetExportFolder(IEnumerable<FileEntry> rows, string folder)
    {
        foreach (FileEntry row in rows)
        {
            row.ExportFolder = folder;
            row.ClearMessage();
            if (row.Preset is not null && AppData.Current.ModOf(row.Preset) is Mod mod) AppData.Current.RememberExportFolder(mod, folder);
            RowChanged(row);
        }
    }

    /// <summary>The × after a row's export folder: the row has none again.</summary>
    public void ClearExportFolder(FileEntry row)
    {
        row.ExportFolder = null;
        row.ClearMessage();
        RowChanged(row);
    }

    /// <summary>The rows selected, in the order shown.</summary>
    public IReadOnlyList<FileEntry> SelectedEntries => matching.Where(r => r.IsSelected).Select(r => r.Entry).ToList();

    /// <summary>
    /// The count beside "Show only" while a search or filter hides rows (the HTML app's <c>#jobCount</c>), or "".
    /// </summary>
    public string ShownCount =>
        (ShowOnly != ShowOnly.All || Search != "") && entries.Count > 0
            ? "Showing " + FileStatus.Count(matching.Count) + " of " + FileStatus.Count(entries.Count) + ". Runs still use every file that is on."
            : "";

    /// <summary>Whether the note above the list has anything to say (it takes no room when it does not).</summary>
    public bool HasNote => Note != "";

    /// <summary>Whether the list has any rows (Remove all needs some).</summary>
    public bool HasFiles => entries.Count > 0;

    /// <summary>How many rows a search or filter is hiding.</summary>
    public int HiddenCount => entries.Count - matching.Count;

    /// <summary>Whether any row is selected (Remove and Export folder act on the selection).</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>Which button is the next step, drawn bold: Add files while the list is empty, then Run export.</summary>
    public bool AddFilesIsNextStep => entries.Count == 0;

    /// <inheritdoc cref="AddFilesIsNextStep"/>
    public bool RunIsNextStep => entries.Count > 0;

    /// <summary>
    /// Adds files from disk (the HTML app's <c>addSources</c>): each gets the open mod's preset its name points to. When a
    /// file of the same name and folder is already in the list, <paramref name="askAboutDuplicate"/> is asked whether to
    /// replace it, keep both or skip the new one, unless an earlier answer was for the whole batch. Returns the rows to
    /// read now: every new or replaced row when files were added on their own, and only those with a preset when they came
    /// from a folder, since a folder can hold hundreds of files.
    /// </summary>
    /// <param name="files">The files, from disk or from an archive.</param>
    /// <param name="fromFolder">Whether the files came from a folder or an archive.</param>
    /// <param name="askAboutDuplicate">Asks about a file already listed: the listed row, and the file just picked.</param>
    public async Task<IReadOnlyList<FileEntry>> AddFiles(IReadOnlyList<FileToAdd> files, bool fromFolder,
        Func<FileEntry, FileToAdd, Task<DuplicateAnswer>> askAboutDuplicate)
    {
        var fresh = new List<FileEntry>();
        int added = 0, replaced = 0, skipped = 0;
        DuplicateChoice? choiceForAll = null;
        flash = "";

        // The first row of each key: a new file is compared with that one.
        var listed = new Dictionary<string, FileEntry>();
        foreach (FileEntry entry in entries) listed.TryAdd(entry.Key, entry);

        foreach (FileToAdd file in files)
        {
            string key = FileReferences.Key(file.Folder, file.Name);
            DuplicateChoice choice = DuplicateChoice.KeepBoth;
            if (listed.TryGetValue(key, out FileEntry? existing))
            {
                // the "Files already in the list" setting: ask, or always replace, or always skip
                string duplicates = (string?)AppData.Current.Settings["dupes"] ?? "ask";
                if (duplicates == "replace") choice = DuplicateChoice.Replace;
                else if (duplicates == "skip") choice = DuplicateChoice.Skip;
                else if (choiceForAll is DuplicateChoice sameAsBefore) choice = sameAsBefore;
                else
                {
                    DuplicateAnswer answer = await askAboutDuplicate(existing, file);
                    choice = answer.Choice;
                    if (answer.ForAll) choiceForAll = choice;
                }
            }

            if (choice == DuplicateChoice.Skip) { skipped++; continue; }
            if (choice == DuplicateChoice.Replace)
            {
                Replace(existing!, file);
                fresh.Add(existing!);
                replaced++;
                continue;
            }
            FileEntry row = NewRow(file);
            entries.Add(row);
            fresh.Add(row);
            added++;
            listed.TryAdd(key, row);
        }

        int waiting = fromFolder ? fresh.Count(e => e.Preset is null) : 0;
        var said = new List<string>();
        if (fromFolder && added > 0) said.Add(FileStatus.Count(added) + " file(s) added from the folder");
        if (replaced > 0) said.Add(replaced + " listed file(s) replaced");
        if (skipped > 0) said.Add(skipped + " duplicate(s) skipped");
        if (said.Count > 0)
            flash = string.Join(", ", said) + "." + (waiting > 0 ? " The " + FileStatus.Count(waiting) + " without a preset are read when they are first needed." : "");
        Refilter();
        return fromFolder ? fresh.Where(e => e.Preset is not null).ToList() : fresh;
    }

    /// <summary>A new row for a file, with the open mod's preset its name points to.</summary>
    private FileEntry NewRow(FileToAdd file)
    {
        var row = new FileEntry
        {
            Id = nextId++,
            Name = file.Name,
            Folder = file.Folder,
            Source = file.Source,
            ArchivedContent = file.ArchivedContent,
            Size = file.Size,
            Modified = file.Modified,
            // the "Assign presets by file name" setting
            Preset = AppData.Current.Settings["autoAssign"]?.GetValue<bool>() == false ? null : PresetMatching.Match(file.Name, file.Folder, OpenModPresets),
        };
        // A file starts with the export folder its preset's mod last used.
        if (row.Preset is not null && AppData.Current.ModOf(row.Preset) is Mod mod)
            row.ExportFolder = AppData.Current.ExportFolderOf(mod);
        return row;
    }

    /// <summary>Puts a newly picked file in a listed row's place; the row keeps its preset and export folder.</summary>
    private static void Replace(FileEntry entry, FileToAdd file)
    {
        entry.Source = file.Source;
        entry.ArchivedContent = file.ArchivedContent;
        entry.Name = file.Name;
        entry.Size = file.Size;
        entry.Modified = file.Modified;
        entry.ClearMessage();
        entry.Edits = null;
        entry.Info = "";
        entry.Scanned = false;
    }

    /// <summary>Shows a message in the note above the list, such as "No .ndf files were found in that folder."</summary>
    public void Say(string message)
    {
        flash = message;
        Changed();
    }

    /// <summary>
    /// Reads a file to learn what it holds (the HTML app's <c>scanJob</c>), for its Status column and the Lines column.
    /// </summary>
    /// <remarks>It runs in the background; <see cref="RowChanged"/> shows the result.</remarks>
    /// <returns>What the file holds, for the property scan; null when it could not be read.</returns>
    public static LoadSummary? Read(FileEntry entry)
    {
        try
        {
            LoadSummary summary = new NdfEngine().Load(FileContent.Text(entry));
            entry.Info = FileStatus.Holds(summary.Descriptors, summary.Constants, summary.Unnamed);
            entry.Lines = summary.Lines;
            entry.Message = "";
            entry.IsError = false;
            entry.Scanned = true;
            entry.References = summary.References;
            return summary;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NdfParseException)
        {
            entry.Message = "Could not read or parse this file. " + error.Message;
            entry.IsError = true;
            return null;
        }
    }

    /// <summary>A row's file as the property scans name it: its folder and name, such as "Gameplay/Gfx/Ammunition.ndf".</summary>
    public static string ScanKey(FileEntry entry) => (entry.Folder != "" ? entry.Folder + "/" : "") + entry.Name;

    /// <summary>Marks a row as being read, as its Status column says until it is done.</summary>
    public void StartReading(FileEntry entry)
    {
        entry.Message = "Reading...";
        entry.IsError = false;
        RowChanged(entry);
    }

    /// <summary>Tells the screen that a row has changed (after it was read in the background, for example).</summary>
    public void RowChanged(FileEntry entry)
    {
        if (rowOf.TryGetValue(entry, out FileRow? row)) row.Refresh();
        // a row's news leaves a warning in the note as it is
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>
    /// A click on a row: selects only it; with Ctrl, adds it to the selection or takes it out; with Shift, selects every
    /// row from the one clicked last to this one.
    /// </summary>
    public void Click(FileRow row, bool control, bool shift)
    {
        if (shift && lastClicked is not null && Shown.Contains(lastClicked))
        {
            int from = Shown.IndexOf(lastClicked), to = Shown.IndexOf(row);
            var range = Shown.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1).ToHashSet();
            foreach (FileRow other in matching) other.IsSelected = range.Contains(other);
        }
        else if (control)
        {
            row.IsSelected = !row.IsSelected;
            lastClicked = row;
        }
        else
        {
            foreach (FileRow other in matching) other.IsSelected = other == row;
            lastClicked = row;
        }
        DetailsRow = row.Entry;
        Changed();
    }

    /// <summary>"Select all": every row shown, or none when every row shown already is.</summary>
    public void SelectAll()
    {
        bool allSelected = matching.Count > 0 && matching.All(r => r.IsSelected);
        foreach (FileRow row in matching) row.IsSelected = !allSelected;
        Changed();
    }

    /// <summary>Ctrl+A: selects every row the search and filter leave, in closed folders too.</summary>
    public void SelectAllShown()
    {
        foreach (FileRow row in matching) row.IsSelected = true;
        Changed();
    }

    /// <summary>"Remove": takes the selected rows out of the list. Files on disk are not touched.</summary>
    public void RemoveSelected()
    {
        var selected = SelectedEntries.ToHashSet();
        if (selected.Count == 0) return;
        entries.RemoveAll(selected.Contains);
        flash = Files(selected.Count) + " removed from the list.";
        Refilter();
    }

    /// <summary>"Remove all": empties the list. Files on disk are not touched.</summary>
    public void RemoveAll()
    {
        if (entries.Count == 0) return;
        flash = Files(entries.Count) + " removed from the list.";
        entries.Clear();
        lastClicked = null;
        Refilter();
    }

    /// <summary>
    /// Undo and Redo: the list gets back the rows it had at an earlier moment, in that order, each with the preset, On
    /// checkbox and export folder it had then. A row whose preset or folder changes loses its last message.
    /// </summary>
    public void PutBack(IReadOnlyList<SavedFileRow> rows)
    {
        entries.Clear();
        foreach (SavedFileRow saved in rows)
        {
            FileEntry entry = saved.Entry;
            if (!ReferenceEquals(entry.Preset, saved.Preset) || entry.ExportFolder != saved.ExportFolder) entry.ClearMessage();
            entry.Preset = saved.Preset;
            entry.MissingPreset = saved.MissingPreset;
            entry.On = saved.On;
            entry.ExportFolder = saved.ExportFolder;
            entries.Add(entry);
        }
        if (lastClicked is not null && !entries.Contains(lastClicked.Entry)) lastClicked = null;
        Refilter();
    }

    /// <summary>
    /// A row brought back from the last session (see <see cref="FileSession"/>): its preset by name, from the open mod
    /// (a name it lacks is kept as missing), its On box and its export folder.
    /// </summary>
    public void Restore(FileEntry entry, FileSession.KeptRow kept)
    {
        entry.On = kept.On;
        entry.ExportFolder = kept.ExportFolder;
        if (kept.Preset is string name)
        {
            entry.Preset = OpenModPresets.FirstOrDefault(p => p.Name == name);
            entry.MissingPreset = entry.Preset is null ? name : null;
        }
        else
        {
            entry.Preset = null;
            entry.MissingPreset = null;
        }
        RowChanged(entry);
    }

    /// <summary>"1 file", "3 files".</summary>
    public static string Files(int count) => FileStatus.Count(count) + (count == 1 ? " file" : " files");

    /// <summary>The × button on a row: takes that row out of the list.</summary>
    public void Remove(FileRow row)
    {
        entries.Remove(row.Entry);
        Refilter();
    }

    /// <summary>
    /// Fits the columns to what is in them (the HTML app's <c>applyJobCols</c>).
    /// </summary>
    /// <param name="listWidth">The list's width inside its edges.</param>
    /// <param name="width">Measures text.</param>
    /// <param name="saveAnywayWidth">How wide the "Save anyway" button is, with the space after it.</param>
    /// <param name="gap">The space between two buttons in a row.</param>
    public void FitColumns(double listWidth, ColumnFit.TextWidth width, double saveAnywayWidth, double gap)
    {
        double buttonsWidth = ColumnWidths.PlainButtonsWidth + (AnyHeldOutput ? saveAnywayWidth : 0);
        IReadOnlyList<FileColumn> shown = ShownColumns;
        Dictionary<FileColumn, double> fitted;
        bool autoFit = AppData.Current.Settings["autoFit"]?.GetValue<bool>() != false;
        if (manualWidths is not null || !autoFit)
        {
            // Widths set by hand, or (with "Auto-fit file list columns" off) each column's starting width, where the HTML
            // app leaves the table to the browser; the last column takes the rest, but keeps room to be read.
            fitted = shown.ToDictionary(column => column, ColumnWidthFor);
            fitted[shown[^1]] = shown[^1] == FileColumn.Status ? 180 : 60;
        }
        else
        {
            int available = (int)Math.Floor(listWidth - buttonsWidth) - 2;
            fitted = ColumnFit.Widths(shown, entries, AllPresetNames, groupedByFolder: groupByFolder, available, width)
                .ToDictionary(pair => pair.Key, pair => (double)pair.Value);
        }
        Columns.Set(shown, fitted, buttonsWidth, gap);
    }

    /// <summary>
    /// A click on a column heading: sorts the list by that column, or the other way round when it already is.
    /// </summary>
    public void SortBy(FileColumn column)
    {
        if (!FileListView.CanSort(column)) return;
        SortBy(column, ascending: column != SortColumn || !SortAscending);
    }

    /// <summary>"Sort ascending" or "Sort descending" in a heading's menu.</summary>
    public void SortBy(FileColumn column, bool ascending)
    {
        if (!FileListView.CanSort(column)) return;
        SortColumn = column;
        SortAscending = ascending;
        Refilter();
        ViewChanged?.Invoke();
    }

    /// <summary>"Show every column" in a heading's menu.</summary>
    public void ShowEveryColumn()
    {
        hiddenColumns.Clear();
        Refilter();
        ViewChanged?.Invoke();
    }

    /// <summary>"Select its files" in a folder's menu: opens the folder and selects only its files.</summary>
    public void SelectFolder(FolderRow folder)
    {
        openFolders.Add(folder.Path);
        foreach (FileRow row in matching) row.IsSelected = row.Entry.Folder == folder.Path;
        Refilter();
    }

    /// <summary>"Switch on" or "Switch off" in a row's menu: every selected file.</summary>
    public void SetSelectedOn(bool on)
    {
        foreach (FileRow row in matching.Where(r => r.IsSelected)) row.Entry.On = on;
        Refilter();
    }

    /// <summary>
    /// Works out again which rows are shown and in what order. Rows the search or filter hides are deselected, so a
    /// button that acts on the selection never acts on something unseen; rows in a closed folder stay selected.
    /// </summary>
    private void Refilter()
    {
        foreach (FileEntry gone in rowOf.Keys.Where(e => !entries.Contains(e)).ToList()) rowOf.Remove(gone);
        if (DetailsRow is not null && !entries.Contains(DetailsRow)) DetailsRow = null;
        Dictionary<string, int> sameFile = entries.GroupBy(e => e.Key).ToDictionary(group => group.Key, group => group.Count());

        matching = [];
        foreach (FileEntry entry in FileListView.Sorted(entries, SortColumn, SortAscending).Where(e => FileListView.Shows(e, ShowOnly, Search)))
        {
            if (!rowOf.TryGetValue(entry, out FileRow? row))
            {
                rowOf[entry] = row = new FileRow(entry);
                row.SwitchedOnOrOff += (_, _) => Changed();
                row.PresetChosen += (_, _) => Changed();
            }
            row.SameFileCount = sameFile[entry.Key];
            row.ListChoices = PresetChoices;
            matching.Add(row);
        }
        foreach (FileRow row in rowOf.Values.Except(matching)) row.IsSelected = false;

        Columns.ShowColumns(ShownColumns);
        Shown.Clear();
        ListItems.Clear();
        if (!groupByFolder)
            foreach (FileRow row in matching) ShowRow(row, inFolder: false);
        else
        {
            // one line per folder, in name order, with its files under it when it is open; while searching, folders with matches are open
            foreach (IGrouping<string, FileRow> folder in matching.GroupBy(r => r.Entry.Folder).OrderBy(g => g.Key, StringComparer.CurrentCulture))
            {
                bool isOpen = openFolders.Contains(folder.Key) || Search != "";
                ListItems.Add(new FolderRow(folder.Key, [.. folder], isOpen));
                if (isOpen)
                    foreach (FileRow row in folder) ShowRow(row, inFolder: true);
            }
        }
        if (ListItems.Count == 0) ListItems.Add(new EmptyListItem(EmptyText));
        // A long list is drawn as the HTML app draws one: a file's folder and its size and date share one line.
        bool isLong = ListItems.Count > LongListRows;
        foreach (FileRow row in Shown) row.IsInLongList = isLong;
        Changed();
    }

    /// <summary>Puts a file row on screen, numbered in the order shown.</summary>
    private void ShowRow(FileRow row, bool inFolder)
    {
        row.Number = Shown.Count + 1;
        row.IsInFolder = inFolder;
        row.Refresh();
        Shown.Add(row);
        ListItems.Add(row);
    }

    /// <summary>A click on a folder's line: opens it, or closes it.</summary>
    public void ToggleFolder(FolderRow folder)
    {
        if (!openFolders.Remove(folder.Path)) openFolders.Add(folder.Path);
        Refilter();
    }

    /// <summary>"Expand all": opens every folder.</summary>
    public void OpenAllFolders()
    {
        foreach (FileEntry entry in entries) openFolders.Add(entry.Folder);
        Refilter();
    }

    /// <summary>"Collapse all": closes every folder.</summary>
    public void CloseAllFolders()
    {
        openFolders.Clear();
        Refilter();
    }

    /// <summary>A folder line's checkbox: switches on or off every file of that folder the search and filter leave.</summary>
    public void SetFolderOn(FolderRow folder, bool on)
    {
        foreach (FileRow row in matching.Where(r => r.Entry.Folder == folder.Path)) row.Entry.On = on;
        Refilter();
    }

    /// <summary>
    /// Tells the screen that the list's own texts and button states may have changed. A warning in the note goes, as the
    /// list has moved on since.
    /// </summary>
    private void Changed()
    {
        caution = "";
        foreach (FolderRow folder in ListItems.OfType<FolderRow>()) folder.Refresh();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

/// <summary>A file row as it was at one moment, for Undo and Redo: the row itself, and what the user can change about it.</summary>
/// <param name="Entry">The row, kept whole with its file, so Undo can bring back a removed row as it was.</param>
/// <param name="Preset">The row's preset then.</param>
/// <param name="MissingPreset">The name of a preset the row asked for that did not exist, if any.</param>
/// <param name="On">The On checkbox then.</param>
/// <param name="ExportFolder">The export folder then.</param>
public sealed record SavedFileRow(FileEntry Entry, ModPreset? Preset, string? MissingPreset, bool On, string? ExportFolder);

/// <summary>What to do with a file already in the list.</summary>
public enum DuplicateChoice
{
    /// <summary>Put the new file in the listed row's place.</summary>
    Replace,
    /// <summary>Add the new file as a row of its own.</summary>
    KeepBoth,
    /// <summary>Leave the new file out.</summary>
    Skip,
}

/// <summary>The answer about a file already in the list, and whether it goes for the rest of the batch too.</summary>
public sealed record DuplicateAnswer(DuplicateChoice Choice, bool ForAll);

/// <summary>What the list shows in place of rows when it has none: "No files yet..." or "No files match...".</summary>
/// <param name="Text">What it says.</param>
public sealed record EmptyListItem(string Text);
