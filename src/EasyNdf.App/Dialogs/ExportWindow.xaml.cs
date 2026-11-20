using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Files;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using Microsoft.Win32;

namespace EasyNdf.App.Dialogs;

/// <summary>What the Export window was left with: the folder for the files that need one, and the files left out.</summary>
/// <param name="Folder">The folder chosen; null when every file keeps its own.</param>
/// <param name="LeftOut">Files on the list not to export this time (those not selected, with "Selected").</param>
/// <param name="OverwriteSource">"Overwrite my source files" was ticked.</param>
/// <param name="Zip">With "Pack into one .zip", the .zip to write in the folder chosen; else null.</param>
public sealed record ExportChoice(string? Folder, IReadOnlySet<FileEntry> LeftOut, bool OverwriteSource = false, string? Zip = null);

/// <summary>One line of the Export window's list.</summary>
public sealed record ExportLine(string Name, string Folder, string Size, string Status, Brush StatusBrush, string Path, string Kind);

/// <summary>
/// The Export window: where the files go, typed or picked with Browse…, or one of the folders used last; a warning,
/// with a box to tick, when that would write over the files they were added from; the export's options (the Settings
/// tab's Export settings, kept when Export is clicked); and every file it will write, new or replacing one.
/// </summary>
public partial class ExportWindow : UserControl
{
    /// <summary>How many folders "Recent" offers.</summary>
    private const int RecentCount = 5;

    private TaskCompletionSource<ExportChoice?>? answer;
    private IReadOnlyList<FileEntry> entries = [];
    private IReadOnlyList<FileEntry> selected = [];
    private List<ExportLine> lines = [];
    private int overSource;
    private bool filling;

    /// <summary>For this opening: each file's carried files, and the stored files' sizes; neither depends on the folder.</summary>
    private readonly Dictionary<FileEntry, List<EasyNdf.Data.Divisions.FileAndTextRules.CarriedFile>> carried = [];
    private readonly Dictionary<string, long?> storedSizes = new(StringComparer.Ordinal);

    /// <summary>For one look at the list: the files of each folder looked in, so thousands of files cost one listing per folder.</summary>
    private readonly Dictionary<string, HashSet<string>?> folderFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Waits for typing in the folder box to pause before working out the list again: thousands of File rule files are checked.</summary>
    private readonly System.Windows.Threading.DispatcherTimer typed;

    /// <summary>Builds the window, closed.</summary>
    public ExportWindow()
    {
        InitializeComponent();
        typed = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        typed.Tick += (_, _) =>
        {
            typed.Stop();
            _ = Refresh();
        };
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>
    /// While the tutorial runs: the window only shows what an export would write. Its Export button stays off, and the
    /// summary says so; nothing is exported during the tour (Paul's choice).
    /// </summary>
    public bool TourOnly { get; set; }

    /// <summary>Opens the window; gives back what to export, or null when cancelled.</summary>
    /// <param name="all">The file list's rows.</param>
    /// <param name="selectedRows">The rows selected, for "Selected".</param>
    /// <param name="startFolder">The folder to start with: the one used last.</param>
    public Task<ExportChoice?> Open(IReadOnlyList<FileEntry> all, IReadOnlyList<FileEntry> selectedRows, string? startFolder)
    {
        entries = all;
        selected = selectedRows;
        carried.Clear();
        storedSizes.Clear();
        JsonObject settings = AppData.Current.Settings;
        filling = true;
        bool everyFile = (string?)settings["exportScope"] == "all";
        ScopeChanged.IsChecked = !everyFile;
        ScopeAll.IsChecked = everyFile;
        ScopeSelected.IsChecked = false;
        ScopeSelected.IsEnabled = selected.Count > 0;
        CommentsTick.IsChecked = settings["keepComments"]?.GetValue<bool>() != false;
        BackupTick.IsChecked = settings["backupOverwritten"]?.GetValue<bool>() == true;
        StampTick.IsChecked = settings["stamp"]?.GetValue<bool>() != false;
        OpenTick.IsChecked = settings["openAfterExport"]?.GetValue<bool>() == true;
        AskTick.IsChecked = settings["askExportFolder"]?.GetValue<bool>() != false;
        StructureTick.IsChecked = settings["keepFolderStructure"]?.GetValue<bool>() != false;
        ZipTick.IsChecked = settings["exportZip"]?.GetValue<bool>() == true;
        HoldBackTick.IsChecked = settings["holdBackFailing"]?.GetValue<bool>() != false;
        WatermarkTick.IsChecked = settings["updateWatermarkFirst"]?.GetValue<bool>() == true;
        RememberTick.IsChecked = settings["rememberExportFolder"]?.GetValue<bool>() != false;
        string eol = (string?)settings["eol"] ?? "lf";
        LineEndChoice.SelectedItem = LineEndChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == eol) ?? LineEndChoice.Items[0];
        FolderBox.Text = startFolder ?? "";
        FilterBox.Text = "";
        ShowAll.IsChecked = true;
        SourceOk.IsChecked = false;
        filling = false;
        ShowRecent();
        _ = Refresh();
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => { FolderBox.Focus(); FolderBox.CaretIndex = FolderBox.Text.Length; }, System.Windows.Threading.DispatcherPriority.Input);
        answer = new TaskCompletionSource<ExportChoice?>();
        return answer.Task;
    }

    /// <summary>The files this export writes, as the run picks them, with the scope chosen here.</summary>
    private List<FileEntry> FilesNow()
    {
        if (ScopeSelected.IsChecked == true) return FileRunner.FilesThatRun(selected, everyFile: true);
        return FileRunner.FilesThatRun(entries, everyFile: ScopeAll.IsChecked == true);
    }

    /// <summary>Whether every file goes into the folder chosen, as the box under Options says (a .zip takes them all); off, files keep the folder kept for them.</summary>
    private bool AsksEachTime => AskTick.IsChecked == true || Zips;

    /// <summary>"Pack into one .zip".</summary>
    private bool Zips => ZipTick.IsChecked == true;

    /// <summary>The .zip's name, in the folder chosen: dated, so an export never replaces an earlier one.</summary>
    private static string ZipNameNow() => "EasyNDF export " + DateTime.Now.ToString("yyyy-MM-dd HH-mm", System.Globalization.CultureInfo.InvariantCulture) + ".zip";

    private string? ChosenFolder => FolderBox.Text.Trim() is { Length: > 0 } typed ? typed : null;

    /// <summary>A line worked out in the background: what is decided there, the texts and colours added on screen.</summary>
    private sealed record Planned(string Name, string Where, long? Size, string Path, string Kind, bool Carried);

    /// <summary>Which working-out is the latest: an older one finishing late is dropped.</summary>
    private int version;

    /// <summary>The working-out under way, which Export waits for.</summary>
    private Task working = Task.CompletedTask;

    /// <summary>
    /// Works out where each file goes, which already exist, and which are the files they were added from. Tuned for
    /// speed: the looking (thousands of File rule files) runs in the background, the window staying usable; keep it so.
    /// </summary>
    private Task Refresh()
    {
        if (filling) return Task.CompletedTask;
        return working = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        int mine = ++version;
        List<FileEntry> files = FilesNow();
        bool asks = AsksEachTime, keep = StructureTick.IsChecked == true, zips = Zips;
        // a .zip is never written over anything, and its files are never kept in the folder they were gathered in
        AskTick.IsEnabled = BackupTick.IsEnabled = RememberTick.IsEnabled = !zips;
        List<FileEntry> needFolder = asks ? files : [.. files.Where(entry => entry.ExportFolder is null)];
        int keepOwn = files.Count - needFolder.Count;
        OwnFolders.Visibility = keepOwn > 0 ? Visibility.Visible : Visibility.Collapsed;
        OwnFolders.Text = Texts.Get("Files.Export.OwnFolders", ("files", FileListModel.Files(keepOwn)));
        // the folder is only used by files without one of their own, unless every file goes there
        bool folderUsed = needFolder.Count > 0;
        FolderBox.IsEnabled = BrowseButton.IsEnabled = RecentFolders.IsEnabled = folderUsed;
        string? folder = ChosenFolder;
        ExportButton.IsEnabled = false;
        (List<Planned> planned, int sources) = await Task.Run(() => { lock (carried) return Plan(files, folder, asks, keep, zips); });
        if (mine != version || !IsOpen) return;
        overSource = sources;
        Brush ok = Brush("ok"), caution = Brush("caution"), warn = Brush("warn"), muted = Brush("muted");
        string fileRule = " · " + Texts.Get("Files.Export.Status.FileRule");
        var said = new Dictionary<string, string>
        {
            ["new"] = Texts.Get("Files.Export.Status.New"), ["replaces"] = Texts.Get("Files.Export.Status.Replaces"),
            ["source"] = Texts.Get("Files.Export.Status.Source"), ["none"] = Texts.Get("Files.Export.Status.NoFolder"),
            ["clash"] = Texts.Get("Files.Export.Status.Clash"), ["zip"] = Texts.Get("Files.Export.Status.InZip"),
        };
        lines = [.. planned.Select(line => new ExportLine(line.Name, line.Where, line.Size is long size ? FileStatus.Size(size) : "",
            said[line.Kind] + (line.Carried ? fileRule : ""), line.Kind switch { "new" or "zip" => ok, "replaces" => caution, "source" or "clash" => warn, _ => muted },
            line.Path, line.Kind switch { "source" => "replaces", "zip" => "new", _ => line.Kind }))];
        SourceWarning.Visibility = overSource > 0 ? Visibility.Visible : Visibility.Collapsed;
        SourceWarningText.Text = Texts.Count("Files.Export.Source.Warning", overSource, ("files", FileListModel.Files(overSource)));
        int replacing = lines.Count(line => line.Kind == "replaces");
        Summary.Text = lines.Count == 0 ? Texts.Get("Files.Export.Summary.None")
            : zips ? Texts.Get("Files.Export.Summary.Zip", ("files", FileListModel.Files(lines.Count)), ("zip", ZipNameNow()))
            : Texts.Get("Files.Export.Summary", ("files", FileListModel.Files(lines.Count)), ("new", Texts.Number(lines.Count(line => line.Kind == "new"))), ("replaces", Texts.Number(replacing)));
        ShowLines();
        ExportButton.IsEnabled = !TourOnly && lines.Count > 0 && (needFolder.Count == 0 || folder is not null) && (overSource == 0 || SourceOk.IsChecked == true);
        ExportButton.ToolTip = TourOnly ? Texts.Get("Files.Export.TourOnly")
            : overSource > 0 && SourceOk.IsChecked != true ? Texts.Get("Files.Export.Source.TickFirst")
            : needFolder.Count > 0 && folder is null ? Texts.Get("Files.Export.NeedsFolder") : null;
        if (TourOnly) Summary.Text = Texts.Get("Files.Export.TourOnly");
    }

    /// <summary>The lines of the list, and how many would overwrite the files they were added from; run in the background.</summary>
    private (List<Planned> Lines, int OverSource) Plan(List<FileEntry> files, string? folder, bool asks, bool keep, bool zips)
    {
        var planned = new List<Planned>();
        int sources = 0;
        folderFiles.Clear();
        List<(FileEntry Entry, string? Path)> paths = FileRunner.PlannedPaths(files, folder, asks, keep);
        if (zips)
        {
            // where each file sits in the .zip: nothing on disk is replaced
            string? root = folder is null ? null : FileRunner.ZipRootFor(SafeFull(folder));
            foreach ((FileEntry entry, string? path) in paths)
                planned.Add(path is null ? new Planned(entry.Name, "", entry.Size, "", "none", false)
                    : new Planned(entry.Name, Relative(Path.GetDirectoryName(SafeFull(path)) ?? "", root), entry.Size, path, "zip", false));
            foreach ((string path, string stored) in FileRunner.PlannedModFiles(paths, carried))
                planned.Add(new Planned(Path.GetFileName(path), Relative(Path.GetDirectoryName(SafeFull(path)) ?? "", root), StoredSize(stored), path, "zip", true));
            return (planned, 0);
        }
        // straight into the folder, two files of the same name would be one
        HashSet<string> clashing = keep ? [] : [.. paths.Where(each => each.Path is not null).GroupBy(each => each.Path!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key)];
        foreach ((FileEntry entry, string? path) in paths)
        {
            long? size = entry.Size;
            if (path is null)
            {
                planned.Add(new Planned(entry.Name, "", size, "", "none", false));
                continue;
            }
            // the folder under the export folder (the chosen one, or the file's own), the whole path in the tip
            string where = Path.GetDirectoryName(path) ?? "";
            string? root = folder is not null && IsUnder(where, folder) ? folder : entry.ExportFolder is string own && IsUnder(where, own) ? own : null;
            bool isSource = entry.Source is string source && entry.ArchivedContent is null
                && string.Equals(SafeFull(source), SafeFull(path), StringComparison.OrdinalIgnoreCase);
            if (isSource) sources++;
            bool exists = !isSource && InFolder(where, Path.GetFileName(path));
            planned.Add(new Planned(entry.Name, Relative(where, root), size, path, isSource ? "source" : clashing.Contains(path) ? "clash" : exists ? "replaces" : "new", false));
        }
        // the files the File rules carry into the mod: models, pictures, sounds and the like
        foreach ((string path, string stored) in FileRunner.PlannedModFiles(paths, carried))
        {
            string where = Path.GetDirectoryName(path) ?? "";
            bool exists = InFolder(where, Path.GetFileName(path));
            planned.Add(new Planned(Path.GetFileName(path), Relative(where, folder is not null && IsUnder(where, folder) ? folder : null), StoredSize(stored), path, exists ? "replaces" : "new", true));
        }
        return (planned, sources);
    }

    /// <summary>The size of a file the File rules carry, as it is stored; null when the store no longer has it.</summary>
    private long? StoredSize(string stored)
    {
        if (!storedSizes.TryGetValue(stored, out long? size))
            storedSizes[stored] = size = AppData.Current.ModFileStore.PathOf(stored) is string kept && SafeExists(kept) ? new FileInfo(kept).Length : null;
        return size;
    }

    private static string Relative(string where, string? root) =>
        root is null ? where : Path.GetRelativePath(root, where) is var inside && inside != "." ? inside : "";

    /// <summary>The list, filtered by the box and All / New / Replaces.</summary>
    private void ShowLines()
    {
        string filter = FilterBox.Text.Trim();
        string? kind = ShowNew.IsChecked == true ? "new" : ShowReplaces.IsChecked == true ? "replaces" : null;
        FileList.ItemsSource = lines.Where(line => (kind is null || line.Kind == kind)
            && (filter == "" || line.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || line.Folder.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>The folders exported to last, newest first, that are still there.</summary>
    private void ShowRecent()
    {
        RecentFolders.Children.Clear();
        foreach (string folder in Recent().Where(Directory.Exists).Take(RecentCount))
        {
            var button = new Button { Content = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } name ? name : folder, ToolTip = folder, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 1, 8, 1) };
            button.Click += (_, _) => FolderBox.Text = folder;
            RecentFolders.Children.Add(button);
        }
        Visibility shown = RecentFolders.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentFolders.Visibility = shown;
        RecentLabel.Visibility = shown;
    }

    private static List<string> Recent() =>
        AppData.Current.Settings["recentExportFolders"] is JsonArray kept ? [.. kept.Select(JsonValues.AsText).Where(folder => folder.Length > 0)] : [];

    /// <summary>Keeps a folder first among the recent ones.</summary>
    private static void Remember(string folder)
    {
        List<string> recent = [folder, .. Recent().Where(other => !string.Equals(other, folder, StringComparison.OrdinalIgnoreCase))];
        AppData.Current.Settings["recentExportFolders"] = new JsonArray([.. recent.Take(10).Select(each => (JsonNode)each)]);
    }

    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        // the list as the folder typed last says, before anything is decided on it
        if (typed.IsEnabled)
        {
            typed.Stop();
            _ = Refresh();
        }
        await working;
        if (!IsOpen || !ExportButton.IsEnabled) return;
        JsonObject settings = AppData.Current.Settings;
        // the options are the Settings tab's: kept as they are left here
        if (ScopeSelected.IsChecked != true) settings["exportScope"] = ScopeAll.IsChecked == true ? "all" : "changed";
        settings["keepComments"] = CommentsTick.IsChecked == true;
        settings["backupOverwritten"] = BackupTick.IsChecked == true;
        settings["stamp"] = StampTick.IsChecked == true;
        settings["openAfterExport"] = OpenTick.IsChecked == true;
        settings["askExportFolder"] = AskTick.IsChecked == true;
        settings["keepFolderStructure"] = StructureTick.IsChecked == true;
        settings["exportZip"] = ZipTick.IsChecked == true;
        settings["holdBackFailing"] = HoldBackTick.IsChecked == true;
        settings["updateWatermarkFirst"] = WatermarkTick.IsChecked == true;
        settings["rememberExportFolder"] = RememberTick.IsChecked == true;
        settings["eol"] = (string?)(LineEndChoice.SelectedItem as ComboBoxItem)?.Tag ?? "lf";
        string? folder = ChosenFolder is string given ? Path.GetFullPath(given) : null;
        if (folder is not null) Remember(folder);
        AppData.Current.SaveSettings();
        IReadOnlySet<FileEntry> leftOut = ScopeSelected.IsChecked == true ? entries.Except(selected).ToHashSet() : new HashSet<FileEntry>();
        string? zip = Zips && folder is not null ? FreeName(Path.Combine(folder, ZipNameNow())) : null;
        Close(new ExportChoice(folder, leftOut, overSource > 0 && SourceOk.IsChecked == true, zip));
    }

    private void BrowseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Texts.Get("Files.ExportFolder.Title") };
        if (ChosenFolder is string now && Directory.Exists(now)) dialog.InitialDirectory = now;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) FolderBox.Text = dialog.FolderName;
    }

    private void FolderChanged(object sender, TextChangedEventArgs e)
    {
        SourceOk.IsChecked = false;
        typed.Stop();
        typed.Start();
    }

    private void ScopeClicked(object sender, RoutedEventArgs e) => _ = Refresh();

    /// <summary>A tick that changes where the files go: the folder box's, "Keep folder structure", "Pack into one .zip".</summary>
    private void OptionClicked(object sender, RoutedEventArgs e) => _ = Refresh();

    /// <summary>A path no file has yet: "(2)", "(3)"... before the extension when taken.</summary>
    private static string FreeName(string path)
    {
        string found = path;
        for (int number = 2; SafeExists(found); number++) found = Path.ChangeExtension(path, null) + " (" + number + ")" + Path.GetExtension(path);
        return found;
    }

    private void SourceOkClicked(object sender, RoutedEventArgs e) => _ = Refresh();

    private void ShowClicked(object sender, RoutedEventArgs e) => ShowLines();

    private void FilterChanged(object sender, TextChangedEventArgs e) => ShowLines();

    private void CancelClicked(object sender, RoutedEventArgs e) => Close(null);

    private void Close(ExportChoice? choice)
    {
        Visibility = Visibility.Collapsed;
        FileList.ItemsSource = null;
        lines = [];
        TaskCompletionSource<ExportChoice?>? waiting = answer;
        answer = null;
        waiting?.TrySetResult(choice);
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
        }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button)
        {
            e.Handled = true;
            ExportClicked(this, new RoutedEventArgs());
        }
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close(null);
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>860 pixels wide, or 96% of a narrower window; never higher than 96% of the window.</summary>
    private void BackdropResized(object sender, SizeChangedEventArgs e)
    {
        Box.Width = Math.Min(860, (e.NewSize.Width + 20) * 0.96);
        Box.MaxHeight = e.NewSize.Height * 0.96;
    }

    private Brush Brush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    /// <summary>Whether a folder holds a file, from one listing of the folder (none when it isn't there).</summary>
    private bool InFolder(string folder, string name)
    {
        if (!folderFiles.TryGetValue(folder, out HashSet<string>? names))
        {
            try { names = Directory.Exists(folder) ? new HashSet<string>(Directory.EnumerateFiles(folder).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase) : null; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { names = null; }
            folderFiles[folder] = names;
        }
        return names?.Contains(name) == true;
    }

    private static bool IsUnder(string path, string folder)
    {
        string full = SafeFull(path), root = Path.TrimEndingDirectorySeparator(SafeFull(folder));
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFull(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    private static bool SafeExists(string path)
    {
        try { return File.Exists(path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return false; }
    }
}
