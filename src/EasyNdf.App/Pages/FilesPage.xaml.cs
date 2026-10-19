using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Files;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using EasyNdf.Engine;
using Microsoft.Win32;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Files tab: the files to run the rules on, and what each run did. The list itself is a <see cref="FileListModel"/>;
/// this class passes the clicks on to it and keeps the parts the model cannot reach (the dialogs, the fonts) in step.
/// </summary>
public partial class FilesPage : UserControl
{
    /// <summary>The headings over the list's columns (thead), built from the shown columns in FilesPage.Columns.cs.</summary>
    internal readonly Grid ListHeader = new();

    /// <summary>The list's scrolling frame, part of the rows' template in FilesPage.xaml.</summary>
    private ScrollViewer ListScroller => (ScrollViewer)Rows.Template.FindName("ListScroller", Rows);

    /// <summary>The file list shown.</summary>
    public FileListModel Model { get; }

    /// <summary>Builds the tab, with an empty file list for the open mod, "My mod".</summary>
    public FilesPage()
    {
        InitializeComponent();
        Panes.FoldedPlace = PaneSlot;
        ShowPaneTitles();
        Texts.LanguageChanged += () =>
        {
            ShowPaneTitles();
            // the column headings and the rows' sizes and statuses are written in code, so they are drawn again
            headingsBuiltFor = "";
            ShowHeadings();
            Model.ShowInNewLanguage();
            // Details is drawn again, in the new language
            detailsShown = null;
            ShowDetails();
        };
        searchPause.Tick += SearchPaused;
        Rows.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(RowButtonClicked));
        Rows.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(RowMouseUp));
        Rows.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(RowDoubleClicked), handledEventsToo: true);
        ListBox.MouseRightButtonUp += ListRightClicked;
        // the headings sit in the list's scrolling frame, above the rows
        Rows.ApplyTemplate();
        ListScroller.Tag = ListHeader;
        ListScroller.ApplyTemplate();
        if (ListScroller.Template.FindName("HeadingsClip", ListScroller) is Border headingsClip)
        {
            // only the sides are clipped: the line under the headings, put on a whole pixel, may reach a little lower
            headingsClip.SizeChanged += (_, _) =>
                headingsClip.Clip = new RectangleGeometry(new Rect(0, -1, headingsClip.ActualWidth, headingsClip.ActualHeight + 2));
        }
        ListScroller.ScrollChanged += ListScrolled;
        ListHeader.RenderTransform = headingsOffset;
        IReadOnlyList<string> allPresetNames = AppData.Current.Mods.SelectMany(m => m.Presets).Select(p => p.Name).ToList();
        Model = new FileListModel(AppData.Current.OpenMod.Presets, allPresetNames);
        // the columns, sort, grouping and sizes as the user left them
        Model.LoadView(AppData.Current.Settings, AppData.Current.State);
        Model.ViewChanged += SaveView;
        AppData.Current.ModsChanged += FollowMods;
        // once a burst: reading a thousand files tells of a thousand changes
        var showState = new RunOnceSoon(() => { ShowListState(); ShowSourceCount(); });
        Model.PropertyChanged += (_, _) => showState.Ask();
        SourceBar.AddFilesRequested += AddSourceFiles;
        DataContext = Model;
        ListBox.SizeChanged += (_, _) => FitColumns();
        Loaded += (_, _) =>
        {
            if (MainView.Find(this) is not MainView view || followsSettings) return;
            followsSettings = true;
            view.SettingChanged += key => { if (key is "autoFit" or "wrapRows" or "") FitColumns(); };
        };
        ShowListState();
    }

    /// <summary>Keeps the file list for next time, when "Reopen the last files" is on; called as the window closes.</summary>
    public void KeepSession()
    {
        if (AppData.Current.Settings["restoreSession"]?.GetValue<bool>() == false) AppData.Current.State.Remove("session");
        else AppData.Current.State["session"] = FileSession.Keep(Model.Entries);
        AppData.Current.SaveState();
    }

    /// <summary>
    /// Brings back the file list of the last session, when "Reopen the last files" is on: the files still where they
    /// were, each with its preset, On box and export folder, then reads them.
    /// </summary>
    public async Task ReopenSession()
    {
        if (AppData.Current.Settings["restoreSession"]?.GetValue<bool>() == false) return;
        List<FileSession.KeptRow> kept = FileSession.Rows(AppData.Current.State["session"]);
        if (kept.Count == 0) return;
        List<(FileToAdd File, FileSession.KeptRow Row)> found = await Task.Run(() => FileSession.StillThere(kept));
        if (found.Count == 0) return;
        IReadOnlyList<FileEntry> rows = await Model.AddFiles([.. found.Select(f => f.File)], fromFolder: false, (_, _) => Task.FromResult(new DuplicateAnswer(DuplicateChoice.KeepBoth, false)));
        for (int i = 0; i < rows.Count && i < found.Count; i++) Model.Restore(rows[i], found[i].Row);
        if (found.Count < kept.Count) Model.Say(Texts.Get("Files.Restore.Missing", ("files", FileListModel.Files(kept.Count - found.Count))));
        await ReadAll(rows);
    }

    /// <summary>
    /// Adds files and reads them, as the Add files... and Add folder... buttons do.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <param name="fromFolder">Whether the files came from a folder or an archive.</param>
    public async Task AddAndRead(IReadOnlyList<FileToAdd> files, bool fromFolder)
    {
        bool several = files.Count > 1;
        IReadOnlyList<FileEntry> toRead = await Model.AddFiles(files, fromFolder, (listed, picked) => AskAboutDuplicate(listed, picked, several));
        await ReadAll(toRead);
    }

    /// <summary>
    /// Reads rows one at a time, as the HTML app reads them, so the first ones are ready soonest. While thousands are
    /// read, the window stays smooth (<see cref="Memory.DuringLongWork"/>), and what reading left behind is given back
    /// at the end.
    /// </summary>
    private async Task ReadAll(IReadOnlyList<FileEntry> rows)
    {
        if (rows.Count == 0) return;
        using (Memory.DuringLongWork())
            foreach (FileEntry entry in rows) await ReadInBackground(entry);
    }

    /// <summary>
    /// Reads a row's file in the background, then shows what it holds; what it holds also fills the rule window's
    /// dropdowns (the property scan).
    /// </summary>
    public async Task ReadInBackground(FileEntry entry)
    {
        Model.StartReading(entry);
        LoadSummary? summary = await Task.Run(() => FileListModel.Read(entry));
        Model.RowChanged(entry);
        if (summary is not null) AppData.Current.SetScan(FileListModel.ScanKey(entry), summary);
    }

    /// <summary>
    /// Asks what to do with a file already in the list, showing the two side by side. With several files picked, the
    /// answer can go for the rest of them too.
    /// </summary>
    internal async Task<DuplicateAnswer> AskAboutDuplicate(FileEntry listed, FileToAdd picked, bool several)
    {
        var comparison = new FileComparison(listed, picked);
        string shown = (picked.Folder != "" ? picked.Folder + "/" : "") + picked.Name;
        DialogAnswer answer = await Messages.Ask(
            Texts.Get("Files.Duplicate.Ask", ("file", shown), ("verdict", comparison.Verdict)),
            new DialogOptions
            {
                Extra = new ComparisonTable(comparison),
                Ok = Texts.Get("Files.Duplicate.Replace"),
                Alternative = Texts.Get("Files.Duplicate.KeepBoth"),
                Cancel = Texts.Get("Files.Duplicate.Skip"),
                CheckText = several ? Texts.Get("Files.Duplicate.SameForOthers") : null,
            });
        DuplicateChoice choice = answer.Choice switch
        {
            DialogChoice.Ok => DuplicateChoice.Replace,
            DialogChoice.Alternative => DuplicateChoice.KeepBoth,
            _ => DuplicateChoice.Skip,
        };
        return new DuplicateAnswer(choice, answer.IsChecked);
    }

    /// <summary>Adds files and reads them straight away, without asking or waiting in the background.</summary>
    public void AddAndReadNow(IReadOnlyList<FileToAdd> files, bool fromFolder = false)
    {
        Task<IReadOnlyList<FileEntry>> adding = Model.AddFiles(files, fromFolder, (_, _) => throw new InvalidOperationException("The sample files are all different."));
        foreach (FileEntry entry in adding.Result)
        {
            LoadSummary? summary = FileListModel.Read(entry);
            Model.RowChanged(entry);
            if (summary is not null) AppData.Current.SetScan(FileListModel.ScanKey(entry), summary);
        }
    }

    /// <summary>The message window of the main view this page is in.</summary>
    internal MessageOverlay Messages => MainView.Of(this).Messages;

    private async void AddFilesClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Texts.Get("Files.AddFiles.Title"),
            Filter = Texts.Get("Files.AddFiles.Filter"),
            Multiselect = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await AddLooseFiles(dialog.FileNames);
    }

    // ---- the source ----

    /// <summary>
    /// "Add Its Files": the source's .ndf files are added as a folder's are, so a big one asks whether to take only the
    /// files the open mod has presets for. A mod's files are its own GameData folder; a game's are in its base.zip,
    /// which is read only for the files taken.
    /// </summary>
    private async void AddSourceFiles()
    {
        if (SourceBar.Selection?.Chosen is not Data.Units.UnitSource source) return;
        try
        {
            if (source.Mod is not null)
            {
                string gameData = Path.Combine(source.Mod.Folder, "GameData");
                await AddFolderFiles(NdfFilesIn(Directory.Exists(gameData) ? gameData : source.Mod.Folder));
                return;
            }
            string zip = Data.Game.GameInstall.BaseZipOf(source.GameFolder);
            Model.Say(Texts.Get("Files.Opening", ("file", Path.GetFileName(zip))));
            List<FileToAdd> listed = await Task.Run(() => ArchiveReader.NdfFilesListedIn(zip));
            Model.Say("");
            List<FileToAdd>? taken = await ChooseFolderFiles(listed);
            if (taken is null) return;
            Model.Say(Texts.Get("Files.Opening", ("file", Path.GetFileName(zip))));
            List<FileToAdd> read = await Task.Run(() => ArchiveReader.WithContent(zip, taken));
            Model.Say("");
            await AddAndRead(read, fromFolder: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Model.Say("");
            await Messages.Tell(Texts.Get("Files.Source.NotRead", ("name", source.Name), ("reason", error.Message)));
        }
    }

    /// <summary>The sources the Source bar shows, shared with the Units tab.</summary>
    public void UseSources(SourceSelection sources)
    {
        SourceBar.Selection = sources;
        sources.Changed += ShowSourceCount;
    }

    /// <summary>"✓ 17 of its files listed": how many rows hold a file from the source chosen.</summary>
    private void ShowSourceCount()
    {
        if (SourceBar.Selection?.Chosen is not Data.Units.UnitSource source)
        {
            SourceBar.ShowListedCount(0);
            return;
        }
        // a mod's files are on disk in its folder; a game's are inside its base.zip ("...\base.zip > GameData/...")
        string start = source.Mod is not null
            ? Path.GetFullPath(source.Mod.Folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar
            : Path.GetFullPath(Data.Game.GameInstall.BaseZipOf(source.GameFolder)) + " > ";
        SourceBar.ShowListedCount(Model.Entries.Count(entry => entry.Source?.StartsWith(start, StringComparison.OrdinalIgnoreCase) == true));
    }

    private async void AddFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Texts.Get("Files.AddFolder.Title") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await AddFolderFiles(NdfFilesIn(dialog.FolderName));
    }

    /// <summary>
    /// Adds the .ndf files found in a folder (the HTML app's <c>addFolderFiles</c>). For a big folder, such as the
    /// game's whole GameData, it offers to take only the files the open mod has presets for.
    /// </summary>
    private async Task AddFolderFiles(List<FileToAdd> found)
    {
        List<FileToAdd>? taken = await ChooseFolderFiles(found);
        if (taken is not null) await AddAndRead(taken, fromFolder: true);
    }

    /// <summary>
    /// The files to take of a folder's (the "Adding a folder" setting): every one, asking first for a big folder, or
    /// only those the open mod has presets for. Null when there are none to take, or the user cancels.
    /// </summary>
    private async Task<List<FileToAdd>?> ChooseFolderFiles(List<FileToAdd> found)
    {
        if (found.Count == 0) { Model.Say(Texts.Get("Files.AddFolder.NoneFound")); return null; }
        // the "Adding a folder" setting: every file (asking first for a big folder), or only those with a preset
        bool onlyWithPreset = (string?)AppData.Current.Settings["folderMode"] == "matched";
        if (!onlyWithPreset && found.Count > 150)
        {
            DialogAnswer answer = await Messages.Ask(
                Texts.Get("Files.AddFolder.BigAsk", ("count", FileStatus.Count(found.Count))),
                new DialogOptions { Ok = Texts.Get("Files.AddFolder.AddAll"), Alternative = Texts.Get("Files.AddFolder.OnlyWithPreset"), Cancel = Texts.Get("Common.Cancel") });
            if (answer.Choice == DialogChoice.Cancel) return null;
            onlyWithPreset = answer.Choice == DialogChoice.Alternative;
        }
        if (onlyWithPreset)
        {
            int all = found.Count;
            found = found.Where(file => PresetMatching.Match(file.Name, "", Model.OpenModPresets) is not null).ToList();
            if (found.Count == 0)
            {
                Model.Say(Texts.Get("Files.AddFolder.NoneWithPreset", ("count", FileStatus.Count(all))));
                return null;
            }
        }
        return found;
    }

    /// <summary>
    /// Files and folders dropped on the app: files are added as Add files... adds them, and folders as Add folder...
    /// does; the files in dropped archives join those of the folders. Mods (.json) dropped here come with the Rules tab;
    /// for now they are passed over.
    /// </summary>
    public async Task AddDropped(IReadOnlyList<string> paths)
    {
        var files = paths.Where(File.Exists).Where(path => !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
        var folders = paths.Where(Directory.Exists).ToList();
        if (folders.Count == 0)
        {
            await AddLooseFiles(files);
            return;
        }
        await AddNdfFiles(files.Where(path => !ArchiveReader.IsArchive(path)).ToList());
        List<FileToAdd> inside = await OpenArchives(files.Where(ArchiveReader.IsArchive).ToList());
        foreach (string folder in folders) inside.AddRange(NdfFilesIn(folder));
        await AddFolderFiles(inside);
    }

    /// <summary>
    /// Files picked with Add files..., or dropped: .ndf files are added; .zip and .rar files are opened, and the .ndf
    /// files inside are added as a folder's are.
    /// </summary>
    private async Task AddLooseFiles(IReadOnlyList<string> paths)
    {
        await AddNdfFiles(paths.Where(path => !ArchiveReader.IsArchive(path)).ToList());
        List<FileToAdd> inside = await OpenArchives(paths.Where(ArchiveReader.IsArchive).ToList());
        if (inside.Count > 0) await AddFolderFiles(inside);
    }

    /// <summary>Adds the .ndf files among files that are not archives; the others are named in a message and left out.</summary>
    private async Task AddNdfFiles(IReadOnlyList<string> paths)
    {
        var notNdf = paths.Where(path => !IsNdf(path)).Select(path => Path.GetFileName(path)).ToList();
        if (notNdf.Count > 0) await Messages.Tell(NotLoadedMessage(notNdf));
        var ndf = paths.Where(IsNdf).Select(path => FileToAdd.OnDisk(path)).ToList();
        if (ndf.Count > 0) await AddAndRead(ndf, fromFolder: false);
    }

    /// <summary>
    /// Opens each archive in turn (the HTML app's <c>openArchives</c>); whatever could not be opened is said once, at the end.
    /// </summary>
    private async Task<List<FileToAdd>> OpenArchives(IReadOnlyList<string> archives)
    {
        var found = new List<FileToAdd>();
        if (archives.Count == 0) return found;
        var problems = new List<string>();
        var empty = new List<string>();
        foreach (string archive in archives)
        {
            string name = Path.GetFileName(archive);
            Model.Say(Texts.Get("Files.Opening", ("file", name)));
            try
            {
                List<FileToAdd> files = await Task.Run(() => ArchiveReader.NdfFilesIn(archive));
                if (files.Count > 0) found.AddRange(files);
                else empty.Add(name);
            }
            catch (Exception error) when (error is ArchiveProblem or IOException or UnauthorizedAccessException)
            {
                problems.Add(Texts.Get("Files.Archive.NotOpened", ("file", name), ("reason", error.Message)));
            }
        }
        Model.Say("");
        if (empty.Count > 0)
            problems.Add(Texts.Count("Files.Archive.Empty", empty.Count, ("files", string.Join(", ", empty.Select(n => "\"" + n + "\"")))));
        if (problems.Count > 0) await Messages.Tell(string.Join(" ", problems));
        return found;
    }

    /// <summary>
    /// What is said about files left out for not being .ndf files: their names, five at most (the HTML app's <c>ndfOnly</c>).
    /// </summary>
    private static string NotLoadedMessage(IReadOnlyList<string> names)
    {
        string named = string.Join(", ", names.Take(5).Select(name => "\"" + name + "\""));
        if (names.Count == 1) return Texts.Get("Files.NotNdf.One", ("file", named));
        if (names.Count > 5) named = Texts.Get("Files.NotNdf.AndMore", ("files", named), ("more", FileStatus.Count(names.Count - 5)));
        return Texts.Get("Files.NotNdf.Several", ("count", FileStatus.Count(names.Count)), ("files", named));
    }

    private static bool IsNdf(string path) => path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Shows or hides the dashed outline that marks the list as the place dropped files land (the HTML app's
    /// <c>#dropHint</c>): 4 pixels inside the list box, and 3 below its headings, clear of a scroll bar.
    /// </summary>
    public void ShowDropHint(bool show)
    {
        DropHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        double top = ListHeader.TranslatePoint(new Point(0, ListHeader.ActualHeight), ListArea).Y + 3;
        double scrollBar = ListScroller.ComputedVerticalScrollBarVisibility == Visibility.Visible ? 14 : 0;
        DropHint.Margin = new Thickness(4, top, 4 + scrollBar, 4);
    }

    /// <summary>A key the focused control did not take (see <see cref="MainView.HandleShortcut"/>).</summary>
    public void HandleShortcut(KeyEventArgs e)
    {
        // Keys typed in a text box or a dropdown are theirs.
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (Model.HasFiles) Model.SelectAllShown();
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (Model.HasSelection) RemoveClicked(this, e);
        }
        // the same keys as the rule list's groups
        else if (Model.GroupByFolder && RulesPage.IsCollapseAllKey(e)) { e.Handled = true; Model.CloseAllFolders(); }
        else if (Model.GroupByFolder && RulesPage.IsExpandAllKey(e)) { e.Handled = true; Model.OpenAllFolders(); }
    }

    /// <summary>
    /// Every .ndf file in a folder and the folders under it, each with the folder it sat in, starting with the chosen
    /// folder's own name ("GameData/Gameplay/Unit"), as a browser names them.
    /// </summary>
    private static List<FileToAdd> NdfFilesIn(string folder)
    {
        string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder)) ?? folder;
        var found = new List<FileToAdd>();
        foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (!path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)) continue;
            string relativeFolder = Path.GetRelativePath(parent, Path.GetDirectoryName(path)!).Replace('\\', '/');
            found.Add(FileToAdd.OnDisk(path, relativeFolder));
        }
        return found;
    }

    /// <summary>Waits for a pause in typing before searching, as the HTML app does, so a long list does not redraw at each key.</summary>
    private readonly DispatcherTimer searchPause = new() { Interval = TimeSpan.FromMilliseconds(160) };

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        searchPause.Stop();
        searchPause.Start();
    }

    private void SearchPaused(object? sender, EventArgs e)
    {
        searchPause.Stop();
        Model.Search = SearchBox.Text;
    }

    private void ShowOnlyChanged(object sender, SelectionChangedEventArgs e)
    {
        // The first change comes while the page is being built, before there is a list.
        if (Model is not null) Model.ShowOnly = (ShowOnly)ShowOnlyChoice.SelectedIndex;
    }

    // ---- Preview, Run export and what they leave ----

    private FileRunner Runner => new(Model, RunSettings.From(AppData.Current.Settings));

    private async void PreviewClicked(object sender, RoutedEventArgs e) => await Preview();

    /// <summary>What the Preview button does.</summary>
    internal Task Preview() => Runner.Run(write: false, ChooseExportFolder);

    private async void RunClicked(object sender, RoutedEventArgs e)
    {
        if (Model.IsBusy || !await ExportIsConfirmed() || !await WatermarksAreUpToDate()) return;
        HashSet<FileEntry>? skipped = await AskAboutEarlierExports();
        if (skipped is null) return;
        await Runner.Run(write: true, ChooseExportFolder, skipped);
        if (AppData.Current.Settings["openAfterExport"]?.GetValue<bool>() == true) OpenExportFolder();
    }

    /// <summary>The files Run export writes: those that are on, with a preset unless "Files to export" is all files.</summary>
    private List<FileEntry> FilesToExport()
    {
        bool everyFile = (string?)AppData.Current.Settings["exportScope"] == "all";
        return [.. Model.Entries.Where(e => e.On && e.Source is not null && (everyFile || e.Preset is not null))];
    }

    /// <summary>"Confirm before Run export": says how many files will be written, and to how many folders, and asks.</summary>
    private async Task<bool> ExportIsConfirmed()
    {
        if (AppData.Current.Settings["confirmExport"]?.GetValue<bool>() != true) return true;
        List<FileEntry> files = FilesToExport();
        if (files.Count == 0) return true;
        int folders = files.Select(e => e.ExportFolder ?? "").Distinct().Count();
        bool someHaveNone = files.Any(e => e.ExportFolder is null);
        string question = Texts.Get("Files.ConfirmExport.Ask", ("files", FileListModel.Files(files.Count)), ("folders", Texts.Count("Files.ConfirmExport.Folders", folders)));
        if (someHaveNone) question += " " + Texts.Get("Files.ConfirmExport.SomeHaveNone");
        return await Messages.Confirm(question, Texts.Get("Files.ConfirmExport.Export"));
    }

    /// <summary>
    /// Files EasyNDF exported before (they start with its "Edited with EasyNDF" comment) would get the rules a second
    /// time. For each one, as Windows asks when replacing files: skip it, export it anyway, or cancel the export; the
    /// answer can go for the rest of them too. Gives back the files to skip, or null when the export is cancelled.
    /// </summary>
    private async Task<HashSet<FileEntry>?> AskAboutEarlierExports()
    {
        List<FileEntry> exportedBefore = [.. FilesToExport().Where(entry => entry.ExportStamp is not null)];
        var skipped = new HashSet<FileEntry>();
        DialogChoice? forTheRest = null;
        for (int at = 0; at < exportedBefore.Count; at++)
        {
            FileEntry entry = exportedBefore[at];
            DialogChoice choice;
            if (forTheRest is DialogChoice same) choice = same;
            else
            {
                int others = exportedBefore.Count - at - 1;
                DialogAnswer answer = await Messages.Ask(
                    Texts.Get("Files.Exported.Ask", ("file", entry.Name), ("stamp", entry.ExportStamp!.Replace("\n", " · "))),
                    new DialogOptions
                    {
                        Ok = Texts.Get("Files.Exported.Skip"),
                        Alternative = Texts.Get("Files.Exported.ExportAnyway"),
                        Cancel = Texts.Get("Files.Exported.CancelExport"),
                        CheckText = others > 0 ? Texts.Count("Files.Exported.SameForOthers", others) : null,
                    });
                choice = answer.Choice;
                if (answer.IsChecked && choice != DialogChoice.Cancel) forTheRest = choice;
            }
            if (choice == DialogChoice.Cancel) return null;
            if (choice == DialogChoice.Ok) skipped.Add(entry);
        }
        return skipped;
    }

    /// <summary>"Open the folder after export": the folder the most files were exported to opens in File Explorer.</summary>
    private void OpenExportFolder()
    {
        string? folder = Model.Entries.Where(e => e.Done && e.ExportFolder is not null).GroupBy(e => e.ExportFolder!)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        if (folder is null || !Directory.Exists(folder)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>
    /// The watermark question comes first (the HTML app's runJobs): for each mod whose files are exported, whose
    /// watermark is on and asks on export, the Watermark window offers to update it. Cancel stops the export.
    /// </summary>
    private async Task<bool> WatermarksAreUpToDate()
    {
        List<FileEntry> exported = FilesToExport();
        // with nothing to run, the run itself says so
        if (exported.Count == 0) return true;
        IEnumerable<Mod> asking = exported.Where(e => e.Preset is not null).Select(e => AppData.Current.ModOf(e.Preset!)).OfType<Mod>().Distinct()
            .Where(m => m.Watermark is { On: true, Ask: true });
        foreach (Mod mod in asking.ToList())
            if (!await MainView.Of(this).Watermark.AskBeforeExport(mod)) return false;
        return true;
    }

    /// <summary>Asks for the folder the files that have none are exported to; null when cancelled.</summary>
    private Task<string?> ChooseExportFolder()
    {
        var dialog = new OpenFolderDialog { Title = Texts.Get("Files.ExportFolder.Title") };
        return Task.FromResult(dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null);
    }

    /// <summary>"Export folder...": sets where the selected files are exported to.</summary>
    private async void ExportFolderClicked(object sender, RoutedEventArgs e)
    {
        string? folder = await ChooseExportFolder();
        if (folder is not null) Model.SetExportFolder(Model.SelectedEntries, folder);
    }

    /// <summary>"Save anyway": saves what the rules made for a file that failed the check, so it can be looked at.</summary>
    private void SaveAnyway(FileRow row)
    {
        if (row.Entry.HeldOutput is not string text) return;
        var dialog = new SaveFileDialog { Title = Texts.Get("Files.SaveAnyway"), FileName = row.Entry.Name, Filter = Texts.Get("Files.NdfFiles") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, text); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _ = Messages.Tell(Texts.Get("Files.SaveAnyway.Failed", ("reason", error.Message))); }
    }

    /// <summary>"Save as zip...": packs the files the last Run export wrote into one .zip, keeping their folders.</summary>
    private async void SaveZipClicked(object sender, RoutedEventArgs e)
    {
        List<FileEntry> written = Model.Entries.Where(entry => entry.Done && entry.ExportedPath is not null).ToList();
        if (written.Count == 0) return;
        var dialog = new SaveFileDialog { Title = Texts.Get("Files.SaveZip.Title"), FileName = "EasyNDF export.zip", Filter = Texts.Get("Files.ZipFiles") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            await Task.Run(() => ExportZip.Write(dialog.FileName, written));
            Model.Say(Texts.Get("Files.SaveZip.Done", ("files", FileListModel.Files(written.Count)), ("zip", Path.GetFileName(dialog.FileName))));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await Messages.Tell(Texts.Get("Files.SaveZip.Failed", ("reason", error.Message)));
        }
    }

    private void SelectAllClicked(object sender, RoutedEventArgs e) => Model.SelectAll();

    private async void RemoveClicked(object sender, RoutedEventArgs e)
    {
        if (await AskToRemoveSelected()) Model.RemoveSelected();
    }

    /// <summary>Asks before Remove takes the selected rows out of the list.</summary>
    internal Task<bool> AskToRemoveSelected()
    {
        int count = Model.SelectedCount;
        return Messages.Confirm(Texts.Count("Files.Remove.Ask", count, ("files", FileListModel.Files(count))), Texts.Get("Files.Remove"));
    }

    private async void RemoveAllClicked(object sender, RoutedEventArgs e)
    {
        if (!Model.HasFiles) return;
        int hidden = Model.HiddenCount;
        string message = hidden > 0
            ? Texts.Get("Files.RemoveAll.AskWithHidden", ("files", FileListModel.Files(Model.Entries.Count)), ("hidden", FileStatus.Count(hidden)))
            : Texts.Get("Files.RemoveAll.Ask", ("files", FileListModel.Files(Model.Entries.Count)));
        if (await Messages.Confirm(message, Texts.Get("Files.RemoveAll"))) Model.RemoveAll();
    }

    /// <summary>
    /// A click on one of a row's buttons, which say what they do in their Tag (see <see cref="FileRowView"/>), as the HTML
    /// app's <c>data-a</c> does.
    /// </summary>
    private async void RowButtonClicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: FileRow row, Tag: string action } || Model.IsBusy) return;
        e.Handled = true;
        switch (action)
        {
            case FileRowView.RemoveAction:
                Model.Remove(row);
                break;
            case FileRowView.SaveAnywayAction:
                SaveAnyway(row);
                break;
            case FileRowView.ChooseFolderAction:
                if (await ChooseExportFolder() is string folder) Model.SetExportFolder([row.Entry], folder);
                break;
            case FileRowView.ClearFolderAction:
                Model.ClearExportFolder(row.Entry);
                break;
        }
    }

    /// <summary>A click on a row, not on one of its controls (they take their own clicks): selects it.</summary>
    /// <summary>Opens a row's file in the Editor tab (set by the main view).</summary>
    public Action<FileEntry> OpenInEditor { get; set; } = _ => { };

    /// <summary>A double-click on a row opens its file in the Editor tab; one on its checkbox, dropdown or buttons does not.</summary>
    private void RowDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        FileRowView? view = null;
        // a click on text may come from a piece of it, which has no parent on screen: its parent is the text it is in
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null && view is null;
             at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is ButtonBase or ComboBox or TextBox) return;
            view = at as FileRowView;
        }
        // Opened once the button is released (see RowMouseUp): the release selects the row, and a tab whose page takes
        // the focus is shown again, which would take the user straight back from the Editor tab.
        if (view?.DataContext is FileRow { Entry.Source: not null } row) rowToOpen = row.Entry;
    }

    /// <summary>The file a double-click opens in the editor once its button is released; null for none.</summary>
    private FileEntry? rowToOpen;

    private void RowMouseUp(object sender, MouseButtonEventArgs e)
    {
        FileRowView? view = null;
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null && view is null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            view = at as FileRowView;
        FileEntry? toOpen = rowToOpen;
        rowToOpen = null;
        if (view?.DataContext is not FileRow row) return;
        ModifierKeys keys = Keyboard.Modifiers;
        Model.Click(row, control: keys.HasFlag(ModifierKeys.Control), shift: keys.HasFlag(ModifierKeys.Shift));
        // after everything the click does, so the Editor tab stays shown
        if (toOpen is not null) Dispatcher.BeginInvoke(() => OpenInEditor(toOpen), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    // ---- Group by folder ----

    /// <summary>A click on a folder's line opens or closes it; the second click of a double-click does nothing, so it does not close again.</summary>
    private void FolderClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderRow folder }) return;
        bool twice = lastFolderClick is { } last && last.Folder == folder.Path && DateTime.UtcNow - last.At < TimeSpan.FromMilliseconds(500);
        lastFolderClick = twice ? null : (folder.Path, DateTime.UtcNow);
        if (!twice) Model.ToggleFolder(folder);
    }

    /// <summary>The folder clicked last and when, to tell a double-click from two clicks.</summary>
    private (string Folder, DateTime At)? lastFolderClick;

    private void FolderToggleClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderRow folder }) Model.ToggleFolder(folder);
    }

    private void FolderOnClicked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: FolderRow folder } box) Model.SetFolderOn(folder, box.IsChecked == true);
    }

    private void ExpandAllClicked(object sender, RoutedEventArgs e) => Model.OpenAllFolders();

    private void CollapseAllClicked(object sender, RoutedEventArgs e) => Model.CloseAllFolders();

    /// <summary>
    /// Shows what the model does not bind: the empty row, the sort arrow, which button is the next step, and the columns'
    /// widths.
    /// </summary>
    private void ShowListState()
    {
        ShownCount.Visibility = Model.ShownCount == "" ? Visibility.Collapsed : Visibility.Visible;

        ShowHeadings();

        // The bold look marks the next step: Add files... while the list is empty, then Run export.
        var plain = (Style)FindResource(typeof(Button));
        var go = (Style)FindResource("GoButton");
        AddFilesButton.Style = Model.AddFilesIsNextStep ? go : plain;
        RunButton.Style = Model.RunIsNextStep ? go : plain;

        // What is in the columns may have changed, and with it their widths.
        FitColumns();
        ShowDetails();
    }

    /// <summary>The row and run Details last showed, so it is drawn again only when one of them changes.</summary>
    private (FileEntry? Row, RunRecord? Run)? detailsShown;

    /// <summary>The Details pane's heading before a row is chosen, and while it is folded.</summary>
    private void ShowPaneTitles()
    {
        Panes.FoldedTitle = Texts.Get("Files.Details");
        Panes.SecondTitle = Texts.Get("Files.Details");
    }

    /// <summary>Shows the chosen row's last run in Details, when the row or its run has changed since it was last shown.</summary>
    private void ShowDetails()
    {
        FileEntry? row = Model.DetailsRow;
        if (detailsShown is null && row is null) return; // nothing chosen yet: Details keeps its first words
        (FileEntry? Row, RunRecord? Run) now = (row, row?.LastRun);
        if (detailsShown == now) return;
        detailsShown = now;
        if (DetailsArea.Content is not DetailsPanel panel) DetailsArea.Content = panel = new DetailsPanel(title => Panes.SecondTitle = title);
        panel.Show(row);
    }

    /// <summary>Fits the columns to what is in them, measuring text in the list's fonts.</summary>
    private void FitColumns()
    {
        double inside = ListBox.ActualWidth - 2;
        if (inside <= 0) return;
        // "Save anyway": its text, 10 pixels of padding and an edge each side, then a space before the ×.
        double gap = TextWidthExact(" ");
        double saveAnyway = TextWidthExact(Texts.Get("Files.SaveAnyway")) + 22 + gap;
        Model.FitColumns(inside, TextWidth, saveAnyway, gap);

        // Columns sized by hand can be wider than the list: then it scrolls sideways, as the HTML app's table does.
        double scrollBar = ListScroller.ComputedVerticalScrollBarVisibility == Visibility.Visible ? 14 : 0;
        bool tooWide = Model.Columns.ListMinWidth > inside - scrollBar;
        ListScroller.HorizontalScrollBarVisibility = tooWide ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        Model.Columns.RowWidth = tooWide ? Model.Columns.ListMinWidth : double.NaN;
        ListHeader.Width = Model.Columns.RowWidth;
    }

    /// <summary>How wide a text is in one of the list's fonts, in whole pixels rounded up, as the HTML app measures it.</summary>
    private int TextWidth(string text, ColumnFit.ListFont font) => (int)Math.Ceiling(TextWidthExact(text, font));

    /// <summary>How wide a text is in one of the list's fonts (the interface font unless named), to a fraction of a pixel.</summary>
    private double TextWidthExact(string text, ColumnFit.ListFont font = ColumnFit.ListFont.Interface)
    {
        var family = (FontFamily)FindResource(font == ColumnFit.ListFont.Code ? "mono" : "ui-font");
        double size = font switch { ColumnFit.ListFont.Code => 12, ColumnFit.ListFont.Small => 11, _ => 12.5 };
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    /// <summary>The mods changed on the Rules tab: the list follows, and reads the files that now have a preset.</summary>
    private async void FollowMods(ModsChange change)
    {
        IReadOnlyList<FileEntry> toRead = Model.FollowMods(AppData.Current.Mods, AppData.Current.OpenMod, change);
        await ReadAll(toRead);
    }

    /// <summary>Saves the list's columns, sort and grouping, for next time.</summary>
    private void SaveView()
    {
        Model.SaveView(AppData.Current.Settings, AppData.Current.State);
        AppData.Current.SaveSettings();
        AppData.Current.SaveState();
    }

    /// <summary>Keeps a list's height for next time (the HTML app's <c>easyndf.sizes</c>), or forgets it when it is null.</summary>
    internal static void RememberSize(string list, double? height)
    {
        if (AppData.Current.State["sizes"] is not System.Text.Json.Nodes.JsonObject sizes) AppData.Current.State["sizes"] = sizes = [];
        if (height is double chosen) sizes[list] = chosen;
        else sizes.Remove(list);
        AppData.Current.SaveState();
    }

    /// <summary>Whether the page follows the Settings tab's changes yet (it is loaded again each time it is shown).</summary>
    private bool followsSettings;
}
