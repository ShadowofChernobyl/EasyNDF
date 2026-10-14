namespace EasyNdf.Data.FileList;

/// <summary>The choices of the "Show only" dropdown above the file list.</summary>
public enum ShowOnly
{
    /// <summary>All files.</summary>
    All,
    /// <summary>Has a preset.</summary>
    HasPreset,
    /// <summary>No preset.</summary>
    NoPreset,
    /// <summary>On.</summary>
    On,
    /// <summary>Off.</summary>
    Off,
    /// <summary>Changed: the last run made edits.</summary>
    Changed,
    /// <summary>Errors or warnings.</summary>
    ErrorsOrWarnings,
    /// <summary>A rule changed nothing.</summary>
    RuleChangedNothing,
    /// <summary>Not read.</summary>
    NotRead,
}

/// <summary>The file list's columns, in their starting order (the HTML app's <c>COLS</c>).</summary>
public enum FileColumn
{
    /// <summary>On: the checkbox.</summary>
    On,
    /// <summary>#: the row's number.</summary>
    Number,
    /// <summary>Source file.</summary>
    File,
    /// <summary>Size.</summary>
    Size,
    /// <summary>Lines.</summary>
    Lines,
    /// <summary>Date modified.</summary>
    Modified,
    /// <summary>Preset.</summary>
    Preset,
    /// <summary>Export folder.</summary>
    ExportFolder,
    /// <summary>Status.</summary>
    Status,
}

/// <summary>The columns' names in the settings, as the HTML app stores them (its <c>COLS</c> keys).</summary>
public static class FileColumnIds
{
    private static readonly Dictionary<FileColumn, string> IdOf = new()
    {
        [FileColumn.On] = "on", [FileColumn.Number] = "num", [FileColumn.File] = "file", [FileColumn.Size] = "size",
        [FileColumn.Lines] = "lines", [FileColumn.Modified] = "modified", [FileColumn.Preset] = "preset",
        [FileColumn.ExportFolder] = "dir", [FileColumn.Status] = "status",
    };

    /// <summary>A column's name in the settings, such as "dir" for Export folder.</summary>
    public static string Id(FileColumn column) => IdOf[column];

    /// <summary>The column a name in the settings stands for, or null for a name no column has.</summary>
    public static FileColumn? FromId(string? id) => IdOf.FirstOrDefault(pair => pair.Value == id) is { Value: not null } found ? found.Key : null;
}

/// <summary>Which rows the list shows, and in what order: the search box, "Show only", and the column it is sorted by.</summary>
public static class FileListView
{
    /// <summary>
    /// Whether a row passes "Show only" and the search box. The search is matched, in small letters, against the row's
    /// folder and name (see <see cref="FileEntry.Key"/>).
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="showOnly">The "Show only" choice.</param>
    /// <param name="search">The search box's text, trimmed and in small letters; "" for none.</param>
    public static bool Shows(FileEntry row, ShowOnly showOnly, string search) =>
        PassesShowOnly(row, showOnly) && (search == "" || row.Key.Contains(search, StringComparison.Ordinal));

    /// <summary>Whether a row passes "Show only".</summary>
    public static bool PassesShowOnly(FileEntry row, ShowOnly showOnly) => showOnly switch
    {
        ShowOnly.HasPreset => row.HasPresetChosen,
        ShowOnly.NoPreset => !row.HasPresetChosen,
        ShowOnly.On => row.On,
        ShowOnly.Off => !row.On,
        ShowOnly.Changed => row.Edits > 0,
        ShowOnly.ErrorsOrWarnings => row.IsError || row.WarningCount != 0,
        ShowOnly.RuleChangedNothing => row.IdleRuleCount != 0,
        ShowOnly.NotRead => !row.Scanned,
        _ => true,
    };

    /// <summary>Whether a column can sort the list. The row number cannot: it is the place in the sorted list.</summary>
    public static bool CanSort(FileColumn column) => column != FileColumn.Number;

    /// <summary>
    /// The rows in the order the list shows them, sorted by a column (the HTML app's <c>sortJobs</c>). Rows without a value
    /// in that column (a file not loaded has no size, for example) come last whichever way it is sorted; rows with the same
    /// value keep the order they were added in.
    /// </summary>
    /// <param name="rows">The rows, in the order they were added.</param>
    /// <param name="column">The column the list is sorted by.</param>
    /// <param name="ascending">False to reverse the order.</param>
    public static List<FileEntry> Sorted(IReadOnlyList<FileEntry> rows, FileColumn column, bool ascending)
    {
        var withKeys = rows.Select((row, index) => (row, index, key: SortKey(row, column))).ToList();
        withKeys.Sort((a, b) =>
        {
            if ((a.key is null) != (b.key is null)) return a.key is null ? 1 : -1;
            int order = a.key is null ? 0 : a.key.CompareTo(b.key);
            if (order != 0) return ascending ? order : -order;
            return a.index.CompareTo(b.index);
        });
        return withKeys.Select(x => x.row).ToList();
    }

    /// <summary>The value a row is sorted by in a column, or null when the row has none.</summary>
    private static SortValue? SortKey(FileEntry row, FileColumn column) => column switch
    {
        FileColumn.On => new SortValue(row.On ? 0 : 1),
        FileColumn.File => new SortValue(row.Name.ToLowerInvariant()),
        FileColumn.Size => row.Source is not null && row.Size is long size ? new SortValue(size) : null,
        FileColumn.Lines => row.Lines is int lines ? new SortValue(lines) : null,
        FileColumn.Modified => row.Source is not null && row.Modified is DateTime when ? new SortValue(when.Ticks) : null,
        FileColumn.Preset => row.HasPresetChosen ? new SortValue((row.Preset?.Name ?? row.MissingPreset ?? "").ToLowerInvariant()) : null,
        FileColumn.ExportFolder => row.ExportFolder is not null ? new SortValue(FolderName(row.ExportFolder).ToLowerInvariant()) : null,
        FileColumn.Status => new SortValue(StatusRank(row) + "|" + FileStatus.Of(row).Text.ToLowerInvariant()),
        _ => new SortValue(0),
    };

    /// <summary>
    /// The order of the Status column: what needs attention first. Errors, warnings, rules that changed nothing, no
    /// preset, ready, done, off (the HTML app's <c>jobRank</c>).
    /// </summary>
    private static int StatusRank(FileEntry row)
    {
        if (FileStatus.Of(row).Kind == StatusKind.Error) return 0;
        if (!row.On) return 6;
        if (row.Message != "")
        {
            if (row.WarningCount != 0) return 1;
            if (row.IdleRuleCount != 0) return 2;
            return row.Done ? 5 : 4;
        }
        return row.HasPresetChosen ? 4 : 3;
    }

    /// <summary>The last part of a folder's path: what the Export folder column shows.</summary>
    public static string FolderName(string folder) =>
        Path.GetFileName(folder.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : folder;

    /// <summary>A value to sort by: a number or a text. Texts compare by character codes, as the HTML app's do.</summary>
    private sealed record SortValue(IComparable Value) : IComparable<SortValue>
    {
        public SortValue(long number) : this((IComparable)number) { }

        public SortValue(string text) : this((IComparable)text) { }

        public int CompareTo(SortValue? other) => Value is string a && other?.Value is string b
            ? string.CompareOrdinal(a, b)
            : Value.CompareTo(other?.Value);
    }
}
