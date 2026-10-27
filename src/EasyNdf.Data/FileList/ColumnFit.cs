using EasyNdf.Data.Languages;

namespace EasyNdf.Data.FileList;

/// <summary>
/// The file list's automatic column widths (the HTML app's <c>fitJobCols</c>): each column as wide as what is in it, aiming
/// for the shortest rows, so nothing wraps unless there is no other way to fit. When the columns are wider than the list,
/// what gives way first is whatever costs no row height. Used until the user drags a column line of their own.
/// </summary>
public static class ColumnFit
{
    /// <summary>How wide a text is, in pixels, rounded up, in one of the list's three fonts.</summary>
    public delegate int TextWidth(string text, ListFont font);

    /// <summary>The fonts the list's texts are measured in.</summary>
    public enum ListFont
    {
        /// <summary>The interface font at 12.5 px: most cells and the headings.</summary>
        Interface,
        /// <summary>The code font at 12 px: file names.</summary>
        Code,
        /// <summary>The interface font at 11 px: the dim second line under a name (its folder).</summary>
        Small,
    }

    /// <summary>The widest the Mods column grows by itself; longer lists of mods end in "2 more".</summary>
    public const int MostModsWidth = 320;

    /// <summary>A column's heading, as shown.</summary>
    public static string Heading(FileColumn column) => column switch
    {
        FileColumn.On => DataTexts.Get("Files.Column.On"), FileColumn.Number => "#", FileColumn.File => DataTexts.Get("Files.Column.File"),
        FileColumn.Size => DataTexts.Get("Files.Column.Size"), FileColumn.Lines => DataTexts.Get("Files.Column.Lines"),
        FileColumn.Modified => DataTexts.Get("Files.Column.Modified"), FileColumn.Mods => DataTexts.Get("Files.Column.Mods"),
        FileColumn.ExportFolder => DataTexts.Get("Files.Column.ExportFolder"), FileColumn.Armory => DataTexts.Get("Files.Column.Armory"),
        _ => DataTexts.Get("Files.Column.Status"),
    };

    /// <summary>
    /// Every shown column's width.
    /// </summary>
    /// <param name="shown">The shown columns, in their order.</param>
    /// <param name="rows">Every row of the list, shown or not.</param>
    /// <param name="groupedByFolder">Whether "Group by folder" is on: then rows have no second line for their folder.</param>
    /// <param name="available">The room there is for the columns, in pixels.</param>
    /// <param name="width">Measures text.</param>
    /// <param name="noExportFolderText">What a row without an export folder says (the tests give the HTML app's words).</param>
    public static Dictionary<FileColumn, int> Widths(IReadOnlyList<FileColumn> shown, IReadOnlyList<FileEntry> rows,
        bool groupedByFolder, int available, TextWidth width, string? noExportFolderText = null)
    {
        // The longest texts of each kind, by number of characters, as the HTML app picks them.
        string name = "", folder = "", status = "", exportFolder = noExportFolderText ?? NoExportFolderText, lines = "", mods = "";
        bool anyFolder = false, anyExportFolder = false;
        foreach (FileEntry row in rows)
        {
            name = Longest(name, row.Source is not null ? row.Name : DataTexts.Get("Files.Row.NotLoaded", ("file", row.Name)));
            if (row.Folder != "") { anyFolder = true; folder = Longest(folder, row.Folder); }
            status = Longest(status, FileStatus.Of(row).Text);
            if (row.ExportFolder is not null) { anyExportFolder = true; exportFolder = Longest(exportFolder, FileListView.FolderName(row.ExportFolder)); }
            if (row.Lines is int count) lines = Longest(lines, FileStatus.Count(count));
            mods = Longest(mods, FileListView.ModsText(row));
        }

        // Rows that already have a second line (their folder) can let Status wrap onto two lines at no cost.
        bool twoLines = anyFolder && !groupedByFolder;
        var natural = new Dictionary<FileColumn, int>
        {
            [FileColumn.On] = 34,
            [FileColumn.Number] = width(Math.Max(rows.Count, 1).ToString(), ListFont.Interface) + 20,
            [FileColumn.File] = Math.Max(Math.Max(width(name, ListFont.Code), twoLines ? width(folder, ListFont.Small) : 0), 150) + 18,
            [FileColumn.Size] = width("0,000 KB", ListFont.Interface) + 16,
            // the four letters' buttons, 18 wide and 2 apart
            [FileColumn.Armory] = 4 * 18 + 3 * 2 + 14,
            [FileColumn.Lines] = width(lines != "" ? lines : "00,000", ListFont.Interface) + 16,
            [FileColumn.Modified] = width("2026-00-00 00:00", ListFont.Interface) + 16,
            // as wide as the longest list of mods, up to a set width: past it, the cell ends in "2 more" (see the app's ModsCell)
            [FileColumn.Mods] = Math.Clamp(width(mods, ListFont.Interface) + 18, 130, MostModsWidth),
            [FileColumn.ExportFolder] = 76 + width(exportFolder, ListFont.Interface) + (anyExportFolder ? 30 : 0) + 18,
            [FileColumn.Status] = Math.Max(width(status, ListFont.Interface) + 18, 180),
        };

        var widths = new Dictionary<FileColumn, int>();
        foreach (FileColumn column in shown)
            widths[column] = Math.Max(natural[column], width(Heading(column), ListFont.Interface) + 30);

        int over = widths.Values.Sum() - available;
        void GiveWay(FileColumn column, int smallest)
        {
            if (over <= 0 || !widths.TryGetValue(column, out int current)) return;
            int cut = Math.Min(over, Math.Max(0, current - smallest));
            widths[column] = current - cut;
            over -= cut;
        }
        int StatusSmallest() => Math.Max(180, (int)Math.Ceiling(widths.GetValueOrDefault(FileColumn.Status) * 0.55));
        if (twoLines) GiveWay(FileColumn.Status, StatusSmallest());
        GiveWay(FileColumn.ExportFolder, 76 + 64);
        GiveWay(FileColumn.Mods, 130);
        GiveWay(FileColumn.File, Math.Max(170, width(name, ListFont.Code) + 18));
        if (!twoLines) GiveWay(FileColumn.Status, StatusSmallest());
        return widths;
    }

    /// <summary>
    /// What the Export folder column says for a row without one. The HTML app says "Browser download", as its exports
    /// download; the desktop app's Run export asks for a folder instead (chosen on purpose).
    /// </summary>
    public static string NoExportFolderText => DataTexts.Get("Files.Row.NotSet");

    /// <summary>The longer of two texts by number of characters; the first when they are as long.</summary>
    private static string Longest(string a, string b) => b.Length > a.Length ? b : a;
}
