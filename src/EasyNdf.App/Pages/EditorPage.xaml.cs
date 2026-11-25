using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Editor;
using EasyNdf.App.Files;
using EasyNdf.App.Rules;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Win32;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Editor tab (the HTML app's <c>edLoad</c>, <c>edShow</c>, <c>edNote</c> and the buttons around them): one file at a
/// time, from the Files tab or opened on its own, in an editor with NDF colours. Its edits can be saved as a file, or
/// handed to the file list so the rules run on them. EditorPage.Export.cs shows what the rules make of it,
/// EditorPage.Find.cs finds and replaces in it, and EditorPage.SearchAll.cs searches every file on the Files tab.
/// </summary>
public partial class EditorPage : UserControl
{
    /// <summary>Where the height of Search all files' results is kept, as their grip leaves it.</summary>
    private const string ResultsHeightKey = "editorResultsHeight";

    /// <summary>The text size of every open file; the line height goes with it, 17 pixels for 12, as in the HTML app.</summary>
    private double textSize = TextZoom.UsualSize;

    private bool showingPick;

    /// <summary>The syntax check of the file in use, for the checks.</summary>
    internal SyntaxProblems SyntaxCheck => syntaxProblems;

    /// <summary>The problems in the status strip, for the checks.</summary>
    internal ProblemList Problems => problemList;

    /// <summary>The files on the Files tab, which the dropdown offers (set by the main view).</summary>
    public FileListModel? Files { get; set; }

    /// <summary>Reads a file on the Files tab again after its text changed (set by the main view).</summary>
    public Func<FileEntry, Task> ReadFile { get; set; } = _ => Task.CompletedTask;

    /// <summary>Adds a file to the Files tab and reads it (set by the main view).</summary>
    public Func<FileToAdd, Task<FileEntry?>> AddFile { get; set; } = _ => Task.FromResult<FileEntry?>(null);

    /// <summary>
    /// A text the editor shows (the HTML app's <c>ed</c>): a file, which can be edited; or, read-only, what the rules
    /// export from it ("Show export"), or only the lines they change ("Changes only"). Each keeps its own text, colours,
    /// undo steps, place and the line pointed at, so going back to one finds it as it was.
    /// </summary>
    private sealed class ShownText
    {
        /// <summary>The file's name.</summary>
        public string Name = "";

        /// <summary>The row on the Files tab it came from; null for a file opened on its own.</summary>
        public FileEntry? Entry;

        /// <summary>Where a file opened on its own is, for what its folder says (its mod's own files); null otherwise.</summary>
        public string? Path;

        /// <summary>Whether the file had Windows line ends, which saving puts back.</summary>
        public bool HasWindowsLineEnds;

        /// <summary>
        /// The text as opened (or as last handed to the file list), which "Discard changes" goes back to. It is kept
        /// packed, as a large file's text takes tens of MB, so each read unpacks it.
        /// </summary>
        public string Original
        {
            get => original.UnpackText();
            set
            {
                original = PackedBytes.PackText(value);
                OriginalLength = value.Length;
            }
        }
        private PackedBytes original = PackedBytes.PackText("");

        /// <summary>How long <see cref="Original"/> is, so a text of another length is known to differ without unpacking it.</summary>
        public int OriginalLength { get; private set; }

        /// <summary>Whether the file was edited since.</summary>
        public bool IsEdited;

        /// <summary>The text, with its undo steps.</summary>
        public TextDocument Document = new();

        /// <summary>Its colours.</summary>
        public NdfColourizer? Colourizer;

        /// <summary>
        /// The text's descriptors and what could not be read of them, read while its lines and colours were worked out,
        /// for the first listing of them (<see cref="RefreshDescriptors"/>); null once used, and for a text not read so.
        /// </summary>
        /// <remarks>Tuned for speed: read after the text showed, a 26 MB file's descriptors came two thirds of a second later.</remarks>
        public Task<(List<EasyNdf.Engine.Viewer.OutlineEntry> List, string Error)>? FirstOutline;

        /// <summary>The document as <see cref="FirstOutline"/> read it: a text changed since is read again.</summary>
        public ITextSourceVersion? OutlineVersion;

        /// <summary>Whether it is an export (or its changed lines), read-only.</summary>
        public bool IsExport;

        /// <summary>The file an export was made from.</summary>
        public ShownText? Source;

        /// <summary>The export whose changed lines "Changes only" shows; null otherwise.</summary>
        public ShownText? Parent;

        /// <summary>The lines "Changes only" shows; null otherwise.</summary>
        public ChangedLines? Changes;

        /// <summary>How an export was made: "as "WTO (Ammunition.ndf)" would export it now, 12 edits".</summary>
        public string How = "";

        /// <summary>A warning about an export, or "".</summary>
        public string Warning = "";

        /// <summary>How many edits the rules made, for "Changes only"'s note.</summary>
        public int? RuleEdits;

        /// <summary>How many places differ between the file as first loaded and the text the rules ran on (edits by hand).</summary>
        public int? HandEdits;

        /// <summary>The file as first loaded, when it was edited by hand since; "Changes only" sets the export against it.</summary>
        public string? FirstText;

        /// <summary>Where it was scrolled to when another text was shown instead.</summary>
        public double Top;

        /// <summary>The blocks folded when another text was shown instead: where each starts and ends in the text.</summary>
        public List<(int Start, int End)> Folds = [];

        /// <summary>The start of the line Find, Go to line or a search result points at; null for none.</summary>
        public TextAnchor? PointedAt;

        /// <summary>Whether it is the file itself, which can be edited.</summary>
        public bool IsFile => !IsExport;

        /// <summary>Whether it is "Changes only".</summary>
        public bool IsChangesOnly => Changes is not null;

        /// <summary>The file it shows or was made from.</summary>
        public ShownText File => Source ?? this;
    }

    /// <summary>Builds the tab, empty.</summary>
    public EditorPage()
    {
        InitializeComponent();
        Panes.FoldedPlace = PaneSlot;
        Panes.SecondTitle = Texts.Get("Editor.Descriptors");
        Texts.LanguageChanged += ShowInNewLanguage;
        // the Descriptor Tree refreshes the first time it shows: opened, or already open when the tab first shows
        Panes.FoldChanged += RefreshTreeOnFirstOpen;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) RefreshTreeOnFirstOpen();
        };
        Theming.ThemeManager.Applied += RedrawInTheme;
        textSize = TextZoom.Fit(TextZoom.Saved);
        // the text box the first file opened takes (EditorPage.Tabs.cs)
        view = MakeView();
        StartDescriptors();
        PreviewKeyDown += FindBarKeys;
        PreviewKeyDown += ShortcutKeys;
        PreviewKeyDown += (_, e) => { if (!e.Handled && SearchOptionKey(e)) e.Handled = true; };
        PreviewKeyDown += (_, e) => { if (!e.Handled && e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None && OpenHelpAtCaret()) e.Handled = true; };
        Editor.SearchOptions.Changed += ShowSearchOptions;
        Loaded += (_, _) => ShowSearchOptions();
        foreach (TextBox box in new[] { FindBox, GoBox, SearchAllBox }) box.TextChanged += FindBoxChanged;
        // the results of Search all files take at most 40% of the tab, so the panes under them keep their room
        SizeChanged += (_, _) => ResultsBox.MaxHeight = Math.Max(ResultsBox.MinHeight, 0.4 * ActualHeight);
        // the grip under the results: dragged, it gives them a height within their limits; double-clicked, their first
        // height again. The height is kept for the next time the app runs.
        double firstResultsHeight = ResultsBox.Height;
        ResultsGrip.Target = ResultsBox;
        ResultsGrip.HeightChosen += height => ResultsBox.Height = Math.Max(ResultsBox.MinHeight, Math.Min(height ?? firstResultsHeight, ResultsBox.MaxHeight));
        ResultsGrip.HeightSettled += () =>
        {
            AppData.Current.State[ResultsHeightKey] = ResultsBox.Height;
            AppData.Current.SaveState();
        };
        if (AppData.Current.State[ResultsHeightKey] is System.Text.Json.Nodes.JsonValue kept && kept.TryGetValue(out double keptHeight) && keptHeight >= ResultsBox.MinHeight)
            ResultsBox.Height = keptHeight;
        SetTextSize(TextZoom.Saved, remember: false);
        TextZoom.Changed += size => SetTextSize(size, remember: false);
        ApplyViewSettings();
        Show();
    }

    /// <summary>The "Text size" setting: the text takes a size, which is kept for next time.</summary>
    public void ChangeTextSize(double size) => SetTextSize(size, remember: true);

    /// <summary>The "Line numbers" and "Word wrap" settings, applied to every open file.</summary>
    public void ApplyViewSettings()
    {
        foreach (FileView v in AllViews) ApplyViewSettings(v);
    }

    private static void ApplyViewSettings(FileView v)
    {
        bool numbers = AppData.Current.Settings["lineNumbers"]?.GetValue<bool>() != false;
        v.LineNumbers.Visibility = numbers ? Visibility.Visible : Visibility.Collapsed;
        v.Editor.WordWrap = AppData.Current.Settings["wordWrap"]?.GetValue<bool>() == true;
    }

    /// <summary>The "Scope lines" setting: the lines behind the text are drawn again in every open file.</summary>
    public void RedrawIndentGuides()
    {
        foreach (FileView v in AllViews) v.Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    /// <summary>The names of the files open here with edits not yet saved, which closing the app would lose; otherwise null.</summary>
    public string? EditedFileName => openViews.Where(v => v.IsEdited).Select(v => v.Name).ToList() is { Count: > 0 } names ? string.Join(", ", names) : null;

    /// <summary>The line height for the text size: 17 pixels for 12, rounded.</summary>
    private double LineHeightForSize => Math.Round(textSize * 17 / 12, MidpointRounding.AwayFromZero);

    private MessageOverlay Messages => MainView.Of(this).Messages;

    // ---- opening a file ----

    /// <summary>
    /// "Open in editor" and a double-click on the Files tab (the HTML app's <c>edOpenJob</c>): the row's file opens in a
    /// tab of its own, or its tab is shown when it is open already.
    /// </summary>
    public async Task Open(FileEntry entry) => await Open(entry, showTab: true);

    /// <summary>
    /// Opens a file of the Files tab, in a new tab after the one in use and in its place, or shows its tab when it is open
    /// already (see <see cref="Bring"/>); with <paramref name="showTab"/>, the Editor tab comes to the front.
    /// </summary>
    /// <summary>
    /// A listed row now holds another file (Replace, when a file of the same name was added): a tab showing it reads the
    /// new file, so the editor and the Files tab stay in step.
    /// </summary>
    public async Task ReadAgain(FileEntry entry)
    {
        if (TabOf(entry) is not FileView open || entry.Source is null) return;
        try
        {
            string text = await Task.Run(() => FileContent.Text(entry));
            if (TabOf(entry) == open) await LoadAside(open, entry.Name, text, entry);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await Messages.Tell(Texts.Get("Editor.CouldNotRead", ("file", entry.Name), ("problem", error.Message)), DialogIcon.Error);
        }
    }

    private async Task Open(FileEntry entry, bool showTab)
    {
        if (entry.Source is null) return;
        if (showTab) MainView.Of(this).ShowTab(MainView.Of(this).EditorTabItem);
        if (TabOf(entry) is FileView open)
        {
            Bring(open);
            return;
        }
        using Work.Job job = Work.Start(Texts.Get("Main.Usage.Job.Opening", ("file", entry.Name)));
        FileView into = NewTab();
        Note.Text = Texts.Get("Editor.Viewer.ReadingFile", ("file", entry.Name));
        // opened to show a descriptor: the card stays, on a step of its own, until that descriptor is shown
        bool finds = wanted is { Quiet: false };
        string[] steps = [Texts.Get("Editor.Loading.Reading"), Texts.Get("Editor.Loading.Colouring"), Texts.Get("Editor.Loading.Showing")];
        int loading = StartLoading(Texts.Get("Editor.Loading.Opening", ("file", entry.Name)), entry.Folder,
            finds ? [.. steps, Texts.Get("Editor.Loading.FindingDescriptor")] : steps);
        try
        {
            string text = await Task.Run(() => FileContent.Text(entry));
            ShowLoadingStep(loading, 1);
            await LoadAside(into, entry.Name, text, entry, loading: loading);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            EndLoading(loading);
            CloseNow(into);
            await Messages.Tell(Texts.Get("Editor.CouldNotRead", ("file", entry.Name), ("problem", error.Message)), DialogIcon.Error);
        }
        finally
        {
            if (finds && wanted is not null && ReferenceEquals(view, into) && into.Shown?.Entry == entry) HoldUntilFound(loading, steps.Length);
            else EndLoading(loading);
        }
    }

    /// <summary>
    /// Puts a file's text into the file in use, or a new tab when none is open (the HTML app's <c>edLoad</c>). Line ends
    /// are kept to be put back on saving.
    /// </summary>
    public void Load(string name, string text, FileEntry? entry)
    {
        if (!openViews.Contains(view)) NewTab();
        view.Loads++;
        view.LetGo = null;
        shown = Prepared(name, text, entry, Thread.CurrentThread);
        ShowText();
    }

    /// <summary>
    /// <see cref="Load"/> into a file's text box, with the text's lines and colours worked out off the screen's thread:
    /// for Steel Division 2's 117 MB UniteDescriptor.ndf they take two seconds. A text put in that box meanwhile is the one
    /// shown; nothing is put in a box closed meanwhile.
    /// </summary>
    /// <param name="folds">The blocks to fold, where a file let go of had them (see <see cref="BringBack"/>); null for none.</param>
    /// <param name="loading">The load the loading card shows (see <see cref="StartLoading"/>), or 0 for none.</param>
    /// <param name="path">Where a file opened on its own is (<see cref="ShownText.Path"/>); null for any other.</param>
    private async Task LoadAside(FileView into, string name, string text, FileEntry? entry, List<(int Start, int End)>? folds = null, int loading = 0, string? path = null)
    {
        int load = ++into.Loads;
        // a file opened now takes the place of one let go of
        into.LetGo = null;
        Thread screen = Thread.CurrentThread;
        bool isNdf = !Data.Divisions.FileAndTextRules.IsDictionaryFile(name);
        ShownText prepared = await Task.Run(() => Prepared(name, text, entry, screen, readsDescriptors: isNdf));
        if (load != into.Loads) return;
        if (loading != 0)
        {
            await ShowStepThatHolds(loading, 2, text.Length);
            if (load != into.Loads) return;
        }
        if (folds is not null) prepared.Folds = folds;
        prepared.Path = path;
        into.Shown = prepared;
        ShowText(into);
    }

    /// <summary>A text to show, its line ends made "\n", its document handed to <paramref name="owner"/> once made.</summary>
    /// <param name="readsDescriptors">Whether its descriptors are read alongside, for their first listing (<see cref="ShownText.FirstOutline"/>).</param>
    private static ShownText Prepared(string name, string text, FileEntry? entry, Thread owner, bool readsDescriptors = false)
    {
        bool windowsLineEnds = text.Contains("\r\n", StringComparison.Ordinal);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var (document, colourizer, outline, version) = PreparedDocument(text, owner, readsDescriptors);
        return new ShownText
        {
            Name = name, Entry = entry, HasWindowsLineEnds = windowsLineEnds, Original = text,
            Document = document, Colourizer = colourizer, FirstOutline = outline, OutlineVersion = version,
        };
    }

    /// <summary>
    /// A text's document and colours, handed to <paramref name="owner"/>, and with <paramref name="readsDescriptors"/>
    /// its descriptors, read on another thread meanwhile. Called away from the screen's thread for a large text.
    /// </summary>
    /// <remarks>
    /// Tuned for speed: the descriptors of WARNO's 26 MB units file take two thirds of a second to read, about as long
    /// as its lines and colours. Read side by side, the descriptor asked for shows that much sooner.
    /// </remarks>
    private static (TextDocument Document, NdfColourizer Colourizer, Task<(List<EasyNdf.Engine.Viewer.OutlineEntry> List, string Error)>? Outline, ITextSourceVersion? Version)
        PreparedDocument(string text, Thread owner, bool readsDescriptors)
    {
        // it never fails: what could not be read is kept as the message the Descriptor Tree shows
        Task<(List<EasyNdf.Engine.Viewer.OutlineEntry> List, string Error)>? outline = !readsDescriptors ? null : Task.Run(() =>
        {
            try { return (EasyNdf.Engine.Viewer.DescriptorViewer.Outline(text), ""); }
            catch (Exception problem) when (problem is EasyNdf.Engine.Ndf.NdfParseException or ArgumentException) { return (new List<EasyNdf.Engine.Viewer.OutlineEntry>(), problem.Message); }
        });
        var document = new TextDocument(text);
        var colourizer = new NdfColourizer(document);
        ITextSourceVersion? version = outline is null ? null : document.Version;
        document.SetOwnerThread(owner);
        return (document, colourizer, outline, version);
    }

    private async void FilePicked(object sender, SelectionChangedEventArgs e)
    {
        if (showingPick || FilePick.SelectedItem is not ChoiceOption { Value: { Length: > 0 } id }) return;
        FileEntry? entry = Files?.Entries.FirstOrDefault(f => f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == id);
        if (entry is not null) await Open(entry);
        else ShowPick();
    }

    private async void OpenFileClicked(object sender, RoutedEventArgs e) => await OpenFileOnItsOwn();

    /// <summary>
    /// "Open file...": the file is added to the Files tab as well (a file of the same name there asks Replace, Keep both
    /// or Skip, as Add Files does), and opens here from its row, so the editor and the list stay in step.
    /// </summary>
    private async Task OpenFileOnItsOwn()
    {
        var dialog = new OpenFileDialog { Title = Texts.Get("Editor.OpenFile.Title"), Filter = Texts.Get("Files.NdfFiles") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (await MainView.Of(this).FilesTab.AddForEditor(dialog.FileName) is FileEntry row) await Open(row);
    }

    /// <summary>The dropdown's right-click menu.</summary>
    private void FilePickRightClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Controls.Menus.Show(FilePick,
        [
            new Controls.MenuChoice(Texts.Get("Editor.OpenFile"), () => _ = OpenFileOnItsOwn()),
            new Controls.MenuChoice(Texts.Get("Editor.SaveAs"), () => SaveClicked(this, e), IsEnabled: shown is not null),
        ]);
    }

    // ---- what can be done with it ----

    /// <summary>"Discard changes": the text goes back to how it was opened.</summary>
    private async void DiscardClicked(object sender, RoutedEventArgs e)
    {
        if (shown is not { IsFile: true, IsEdited: true } open) return;
        FileView of = view;
        if (!await Messages.Confirm(Texts.Get("Editor.DiscardAll", ("file", open.Name)), Texts.Get("Editor.Discard"), DialogIcon.Warning)) return;
        if (of.Shown != open) return;
        await LoadAside(of, open.Name, open.Original, open.Entry, path: open.Path);
        // another file put in that box while it was read keeps its own line ends
        if (of.Shown is not ShownText again || again.Name != open.Name || again.Entry != open.Entry) return;
        again.HasWindowsLineEnds = open.HasWindowsLineEnds;
    }

    /// <summary>
    /// The text to save, with the file's own line endings put back. With "Changes only" shown, it is the whole export.
    /// </summary>
    private string TextToSave() => TextToSave(shown!);

    private static string TextToSave(ShownText text)
    {
        ShownText whole = text.Parent ?? text;
        return whole.HasWindowsLineEnds ? whole.Document.Text.Replace("\n", "\r\n", StringComparison.Ordinal) : whole.Document.Text;
    }

    /// <summary>"Save as...": the text, saved as a file (the HTML app's "Download").</summary>
    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (shown is null) return;
        var dialog = new SaveFileDialog { Title = Texts.Get("Editor.SaveAs.Title"), FileName = shown.Name, Filter = Texts.Get("Files.NdfFiles") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, TextToSave(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _ = Messages.Tell(Texts.Get("Editor.CouldNotSave", ("problem", error.Message)), DialogIcon.Error);
        }
    }

    /// <summary>
    /// "Use in file list": the file list uses the edited text from now on, so Preview and Run export apply the rules to it.
    /// A file opened on its own is added to the list.
    /// </summary>
    private async void UseClicked(object sender, RoutedEventArgs e) => await UseInFileList(view);

    /// <summary>"Use in file list" for a file's text box, also when its tab is closed with edits.</summary>
    private async Task UseInFileList(FileView of)
    {
        if (of.Shown is not { IsFile: true } open || Files is null) return;
        byte[] edited = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(TextToSave(open));
        open.Original = open.Document.Text;
        open.IsEdited = false;
        if (open.Entry is FileEntry entry && Files.Entries.Contains(entry))
        {
            // the file as it was first loaded is kept, for "Changes only" to tell the hand edits from the rules' edits
            entry.FirstContent ??= entry.ArchivedContent ?? PackedBytes.Pack(FileContent.Bytes(entry));
            entry.ArchivedContent = PackedBytes.Pack(edited);
            entry.ClearMessage();
            entry.Info = "";
            entry.Lines = null;
            entry.Scanned = false;
            await ReadFile(entry);
        }
        else open.Entry = await AddFile(new FileToAdd(open.Name, "", open.Name, PackedBytes.Pack(edited), DateTime.Now));
        Show();
        if (of == view) Note.Text = Texts.Get("Editor.Note.UsedInFileList", ("file", open.Name));
    }

    /// <summary>Whether a text is being put in the editor, which is no edit: comparing a large one with its original takes a quarter second.</summary>
    private bool puttingIn;

    /// <summary>The first edit of a file marks it as edited: Discard changes and Use in file list can be used, and its tab has a dot.</summary>
    private void TextEdited(FileView of)
    {
        if (puttingIn || of.Shown is not { IsFile: true, IsEdited: false } open) return;
        // a text of another length differs: only one as long as the original is compared with it, character for character
        if (open.Document.TextLength == open.OriginalLength && open.Document.Text == open.Original) return;
        open.IsEdited = true;
        Show();
    }

    // ---- showing it ----

    /// <summary>
    /// Puts the shown text into the editor (the HTML app's <c>edShow</c>): its text and colours, read-only for an export,
    /// with its own line numbers and marks for "Changes only". Then the buttons and the note.
    /// </summary>
    private void ShowText() => ShowText(view);

    /// <summary><see cref="ShowText()"/> for a file's text box, which need not be the one in use.</summary>
    private void ShowText(FileView of)
    {
        ShownText? text = of.Shown;
        TextView lines = of.Editor.TextArea.TextView;
        PutFoldingAway(of);
        foreach (IVisualLineTransformer transformer in lines.LineTransformers.Where(t => t is NdfColourizer or MutedLines).ToList())
            lines.LineTransformers.Remove(transformer);
        // only the file itself is checked: an export cannot be edited; a dictionary file such as UNITS.csv is not NDF
        bool isNdf = text is not null && !Data.Divisions.FileAndTextRules.IsDictionaryFile(text.Name);
        UseGuideFor(of, text, isNdf);
        of.SyntaxProblems.Watch(text is { IsFile: true } && isNdf ? text.Document : null, text is { IsFile: true } ? of.Guide : null);
        _ = UseImpactsFor(of, text);
        if (text is null)
        {
            ShowWhatFollowsText(of);
            return;
        }
        puttingIn = true;
        try { of.Editor.Document = text.Document; }
        finally { puttingIn = false; }
        of.Editor.IsReadOnly = text.IsExport;
        lines.LineTransformers.Add(text.Colourizer!);
        ChangedLines? changes = text.Changes;
        if (changes is not null) lines.LineTransformers.Add(new MutedLines(changes.Marks));

        // a read-only view's numbers are as wide as the whole export's, and its text follows the marks with no gap
        of.LineNumbers.NumberOf = changes is null ? null : number => changes.Numbers[number - 1];
        int wholeLength = (text.Parent ?? text).Document.LineCount;
        of.LineNumbers.Digits = text.IsExport ? wholeLength.ToString(System.Globalization.CultureInfo.InvariantCulture).Length : null;
        of.LineNumbers.InvalidateMeasure();
        of.MarkColumn.Marks = changes?.Marks;
        of.TextGap.Width = text.IsExport ? 0 : 4;
        of.Shading.Marks = changes?.Marks;
        of.Shading.PointedAt = text.PointedAt;
        BringFoldingBack(of);
        ShowWhatFollowsText(of);
    }

    /// <summary>After a text is put in a box: the buttons, the note and the descriptors when it is the file in use, else only the tabs and names.</summary>
    private void ShowWhatFollowsText(FileView of)
    {
        if (of != view)
        {
            ShowTabs();
            ShowCaptions();
            return;
        }
        Show();
        if (of.Shown is not null) KickDescriptors(0);
    }

    /// <summary>Shows the buttons that can be used, and the note (the HTML app's <c>edBar</c>).</summary>
    private void Show()
    {
        ClearSearchScope();
        bool has = shown is not null, isFile = shown is { IsFile: true }, anyOpen = openViews.Count > 0;
        EmptyBox.Visibility = anyOpen ? Visibility.Collapsed : Visibility.Visible;
        SideBySide.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;
        ShowTabs();
        ShowCaptions();
        SaveButton.IsEnabled = FindBox.IsEnabled = PreviousButton.IsEnabled = NextButton.IsEnabled = GoBox.IsEnabled = has;
        // replacing needs the text to be editable
        ReplaceBox.IsEnabled = ReplaceButton.IsEnabled = ReplaceAllButton.IsEnabled = isFile;
        bool fromList = shown?.Entry is FileEntry entry && Files is not null && Files.Entries.Contains(entry);
        ExportButton.IsEnabled = ChangesButton.IsEnabled = fromList;
        ShowExportText.Visibility = shown is { IsExport: true } ? Visibility.Hidden : Visibility.Visible;
        ShowSourceText.Visibility = shown is { IsExport: true } ? Visibility.Visible : Visibility.Hidden;
        ChangesButton.IsChecked = shown is { IsChangesOnly: true };
        // Discard changes and Use in file list only show once there is something to discard or use
        bool edited = shown is { IsFile: true, IsEdited: true };
        DiscardButton.Visibility = UseButton.Visibility = edited ? Visibility.Visible : Visibility.Collapsed;
        DiscardButton.IsEnabled = UseButton.IsEnabled = edited;
        ShowPick();
        ShowNote();
    }

    /// <summary>Shows the buttons and the note again, after the Files tab may have changed (its rows, their presets).</summary>
    public void Refresh()
    {
        Show();
        _ = RebuildTree();
        _ = BringBackLetGo();
    }

    /// <summary>
    /// Draws again what the code wrote in the old language: the pane's heading, the note and buttons, the Refresh button
    /// and the Descriptor Tree with its notes.
    /// </summary>
    private void ShowInNewLanguage()
    {
        Panes.SecondTitle = Texts.Get("Editor.Descriptors");
        halfButtons.Show();
        ShowFoldTips();
        if (!scanning) ScanButton.Content = Texts.Get("Editor.ScanAll");
        DescriptorFind.Tag = showsAllFiles ? Texts.Get("Editor.DescriptorFind.AllFilesHint") : Texts.Get("Editor.DescriptorFind.Hint");
        Show();
        _ = RebuildTree();
        ShowProperties();
    }

    /// <summary>
    /// The dropdown of the Files tab's files, by file name, with a folder only where two files share a name; only those
    /// the file filter lets through (the file open stays listed either way).
    /// </summary>
    public void ShowPick()
    {
        ShowFileFilterButton();
        List<FileEntry> listed = [.. ListedFiles().Where(f => FileFilterLetsThrough(f) || f == shown?.Entry)];
        var names = new PropertyScans(new Dictionary<string, Data.ScanInfo>(), [.. listed.Select(FileListModel.ScanKey)]);
        var options = new List<ChoiceOption> { new("", Files is { Entries.Count: > 0 } ? Texts.Get("Editor.ChooseFile") : Texts.Get("Editor.NoFiles")) };
        foreach (FileEntry entry in listed.OrderBy(FileListModel.ScanKey, Comparer<string>.Create(PropertyScans.CompareByFileName)))
        {
            string key = FileListModel.ScanKey(entry), folder = names.Folder(key);
            options.Add(new ChoiceOption(entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.Name, folder != "" ? PropertyScans.FolderTag(folder) : ""));
        }
        showingPick = true;
        FilePick.ItemsSource = options;
        string current = shown?.Entry?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        FilePick.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options[0];
        showingPick = false;
        // a long name is cut short in the dropdown, so its tooltip names the file in full, and where it is
        FilePick.ToolTip = shown?.Entry is FileEntry open
            ? Texts.Get("Editor.FilePick.File.Tip", ("file", open.Name), ("path", open.Source ?? open.Name))
            : Texts.Get("Editor.FilePick.Tip");
    }

    /// <summary>
    /// The note beside the heading (the HTML app's <c>edNote</c>): the file's name and length, and whether it was edited;
    /// for an export, how it was made; for "Changes only", how much changed.
    /// </summary>
    private void ShowNote()
    {
        Note.Inlines.Clear();
        if (shown is null) return;
        int lines = shown.Document.LineCount;
        if (shown.Changes is ChangedLines changes)
        {
            string changed = Texts.Get("Editor.Note.Changes",
                ("lines", Texts.CountWide("Editor.Note.ChangedLines", changes.ChangedLineCount)),
                ("places", Texts.CountWide("Editor.Note.Places", changes.PlaceCount)));
            if (shown.HandEdits is > 0 and int hand)
            {
                string handEdits = Texts.CountWide("Editor.Note.HandEdits", hand);
                changed = shown.RuleEdits is int rule
                    ? Texts.Get("Editor.Note.ChangesByRulesAndHand", ("changes", changed), ("rules", Texts.CountWide("Editor.Note.RuleEdits", rule)), ("hand", handEdits))
                    : Texts.Get("Editor.Note.ChangesByHand", ("changes", changed), ("hand", handEdits));
            }
            Note.Inlines.Add(new Run(Texts.Get("Editor.Note.ChangesOnly", ("file", shown.Name), ("changes", changed))));
            return;
        }
        string length = Texts.CountWide("Editor.Note.Lines", lines);
        if (shown.IsExport)
        {
            Note.Inlines.Add(new Run(Texts.Get("Editor.Note.Export", ("file", shown.Name), ("how", shown.How), ("length", length))));
            if (shown.Warning != "") Note.Inlines.Add(Coloured(" " + shown.Warning, "warn"));
            return;
        }
        Note.Inlines.Add(new Run(Texts.Get("Editor.Note.File", ("file", shown.Name), ("length", length))));
        if (!shown.IsEdited) { Note.Inlines.Add(new Run(Texts.Get("Editor.Note.Editable"))); return; }
        Note.Inlines.Add(Coloured(Texts.Get("Editor.Note.Edited"), "caution"));
        Note.Inlines.Add(new Run(" " + Texts.Get("Editor.Note.EditedHow")));
    }

    /// <summary>Text in one of the theme's colours, such as "caution" or "warn".</summary>
    private static Run Coloured(string text, string colour)
    {
        var run = new Run(text);
        run.SetResourceReference(TextElement.ForegroundProperty, colour);
        return run;
    }

    /// <summary>The note's text.</summary>
    internal string NoteText => string.Concat(Note.Inlines.OfType<Run>().Select(run => run.Text));

    /// <summary>The line marked by Find, Go to line or a search result, counting from 1; 0 for none.</summary>
    internal int PointedLine => shown?.PointedAt is { IsDeleted: false } anchor ? anchor.Line : 0;

    /// <summary>Whether the file itself is shown, or its export, or the changes only.</summary>
    internal string ShownKind => shown is null ? "" : shown.IsChangesOnly ? "changes" : shown.IsExport ? "export" : "file";

    /// <summary>The line at the top of the editor, counting from 1.</summary>
    internal int TopLine => Text.TextArea.TextView.GetDocumentLineByVisualTop(Text.VerticalOffset)?.LineNumber ?? 0;

    /// <summary>Gives a notification of what the last action did.</summary>
    private static void Say(string text, NoticeKind kind = NoticeKind.Done) => Notices.Say(text, kind);

    /// <summary>Ctrl and the wheel change the text size, only of the editor (the HTML app's <c>edZoom</c>).</summary>
    private void WheelTurned(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        SetTextSize(textSize + (e.Delta > 0 ? 1 : -1), remember: true);
    }

    /// <summary>Ctrl+plus, Ctrl+minus and Ctrl+0 in the text change its size too. Backspace or Delete next to a folded block unfolds it.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (UnfoldInsteadOfDeleting(e.Key)) { e.Handled = true; return; }
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        if (e.Key is Key.OemPlus or Key.Add) { e.Handled = true; SetTextSize(textSize + 1, remember: true); }
        else if (e.Key is Key.OemMinus or Key.Subtract) { e.Handled = true; SetTextSize(textSize - 1, remember: true); }
        else if (e.Key is Key.D0 or Key.NumPad0) { e.Handled = true; SetTextSize(TextZoom.UsualSize, remember: true); }
    }

    // ---- the find bar ----

    private void FindToggleClicked(object sender, RoutedEventArgs e) => ShowFindBar(FindToggle.IsChecked == true, FindBox);

    /// <summary>Shows the find bar with the keyboard in one of its boxes, or hides it and gives the keyboard back to the text.</summary>
    internal void ShowFindBar(bool show, TextBox? box)
    {
        FindBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        FindToggle.IsChecked = show;
        // the search-all results go and come back with the bar, as they were; the line found is unmarked once done
        if (!show)
        {
            if (SearchResults.Visibility == Visibility.Visible)
            {
                resultsHiddenWithBar = true;
                SearchResults.Visibility = Visibility.Collapsed;
            }
            ClearPointedLine();
        }
        else if (resultsHiddenWithBar)
        {
            SearchResults.Visibility = Visibility.Visible;
            resultsHiddenWithBar = false;
        }
        if (show && box is not null)
        {
            // after the bar is laid out, or the box cannot take the keyboard yet
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
        else if (!show && Text.IsVisible) Text.TextArea.Focus();
    }

    /// <summary>
    /// What is selected in the text becomes the text to find, when the text has the keyboard and the selection is on one
    /// line, as Ctrl+F does in Sublime Text; a selection over several lines is not something to find.
    /// </summary>
    private void TakeSelectionToFind()
    {
        if (shown is null || !Text.TextArea.IsKeyboardFocusWithin || Text.SelectionLength == 0) return;
        string selected = Text.SelectedText;
        if (selected.IndexOfAny(LineBreaks) >= 0) return;
        FindBox.Text = selected;
    }

    /// <summary>The characters that end a line, which a text to find cannot hold.</summary>
    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>Whether the search-all results were showing when the find bar was hidden, to show them again with it.</summary>
    private bool resultsHiddenWithBar;

    /// <summary>A find box emptied: the search is done, so the line it found is no longer marked.</summary>
    private void FindBoxChanged(object sender, TextChangedEventArgs e)
    {
        if (((TextBox)sender).Text == "") ClearPointedLine();
        if (ReferenceEquals(sender, FindBox)) FindCountText.Text = "";
    }

    /// <summary>Unmarks the line that Find, Go to line, a search result or a descriptor marked.</summary>
    private void ClearPointedLine() => ClearPointedLine(view);

    private static void ClearPointedLine(FileView of)
    {
        if (of.Shown is null || of.Shown.PointedAt is null) return;
        of.Shown.PointedAt = null;
        of.Shading.PointedAt = null;
        of.Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    /// <summary>
    /// A file's caret moved: when it leaves the marked line (a click elsewhere in the text, the arrow keys), the line's
    /// tint goes, as a problem's does. Not while a text is put in, which moves the caret by itself.
    /// </summary>
    private void ClearPointedLineWhenLeft(FileView of)
    {
        if (puttingIn || of.Shown?.PointedAt is not { IsDeleted: false } anchor || of.Editor.TextArea.Caret.Line == anchor.Line) return;
        ClearPointedLine(of);
    }

    /// <summary>
    /// The tab's keys, anywhere on it, as Sublime Text has them: Ctrl+F finds (what is selected in the text, when it is
    /// on one line), Ctrl+H replaces, Ctrl+G goes to a line, Ctrl+Shift+F searches all files, Ctrl+W closes the file in use; F3 and Shift+F3 find the
    /// next and previous match; Ctrl+Shift+[ folds every descriptor and Ctrl+Shift+] unfolds them; Escape closes the file
    /// filter, or hides the find bar and gives the text the keyboard.
    /// </summary>
    private void FindBarKeys(object sender, KeyEventArgs e)
    {
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        TextBox? box = !control ? null : e.Key switch
        {
            Key.F when shift => SearchAllBox,
            Key.F => FindBox,
            Key.H => ReplaceBox,
            Key.G => GoBox,
            _ => null,
        };
        if (e.Key == Key.Escape && FileFilterPopup.IsOpen)
        {
            // the popup keeps the keyboard in its name box, so Escape comes here first
            e.Handled = true;
            FileFilterPopup.IsOpen = false;
            FileFilterButton.Focus();
        }
        else if (box is not null)
        {
            e.Handled = true;
            if (box == FindBox || box == ReplaceBox) TakeSelectionToFind();
            ShowFindBar(true, box);
        }
        // Ctrl+W closes the file in use, as its tab's × does: one with edits not saved asks first
        else if (control && !shift && e.Key == Key.W && openViews.Contains(view))
        {
            e.Handled = true;
            _ = Close(view);
        }
        else if (e.Key == Key.F3 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && shown is not null)
        {
            e.Handled = true;
            // with nothing to find yet, F3 opens the bar to type it, as Sublime Text does
            if (FindBox.Text == "")
            {
                TakeSelectionToFind();
                ShowFindBar(true, FindBox);
            }
            if (FindBox.Text != "") FindNext(shift ? -1 : 1);
        }
        // the keys that fold and unfold the Rules tab's groups fold and unfold the file's descriptors here
        else if (RulesPage.IsCollapseAllKey(e))
        {
            e.Handled = true;
            FoldAll();
        }
        else if (RulesPage.IsExpandAllKey(e))
        {
            e.Handled = true;
            UnfoldAll();
        }
        else if (e.Key == Key.Escape && FindBar.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            ShowFindBar(false, null);
        }
        else if (e.Key == Key.Escape && problemList.IsOpen)
        {
            e.Handled = true;
            problemList.Close();
        }
    }

    /// <summary>A new theme: the text's colours, the line numbers and the marks are drawn again in it.</summary>
    private void RedrawInTheme()
    {
        foreach (FileView v in AllViews)
        {
            v.Editor.TextArea.Caret.CaretBrush = (System.Windows.Media.Brush)FindResource("ink");
            v.Editor.TextArea.TextView.Redraw();
            v.LineNumbers.InvalidateVisual();
            v.MarkColumn.InvalidateVisual();
        }
        // the descriptor's state is in two theme colours mixed, which cannot follow the theme by itself
        ShowState();
    }

    /// <summary>
    /// Sets the text size of every open file, from 7 to 36 pixels; the line that was at the top of each stays at the top.
    /// A size to remember is saved and given to the Add Descriptor rule's text box and the status strips too.
    /// </summary>
    private void SetTextSize(double size, bool remember)
    {
        if (remember)
        {
            TextZoom.Set(size); // comes back here through TextZoom.Changed
            return;
        }
        textSize = TextZoom.Fit(size);
        foreach (FileView v in AllViews) SetTextSize(v);
    }

    /// <summary>Gives a file's text box the text size.</summary>
    private void SetTextSize(FileView v)
    {
        TextView lines = v.Editor.TextArea.TextView;
        // the line at the top stays at the top: where it starts is asked for again once the lines have their new height,
        // and the view put there. (ScrollToLine puts a line in the middle of the view, which scrolled the text up half a
        // page at every step of Ctrl and the wheel.)
        int topLine = lines.GetDocumentLineByVisualTop(lines.ScrollOffset.Y)?.LineNumber ?? 1;
        v.Editor.FontSize = textSize;
        v.LineHeight.Height = v.MarkColumn.LineHeight = LineHeightForSize;
        lines.Redraw();
        if (v.Shown is not null && topLine <= v.Editor.Document.LineCount)
        {
            v.Editor.UpdateLayout();
            v.Editor.ScrollToVerticalOffset(lines.GetVisualTopByDocumentLine(topLine));
        }
        v.Strip.ShowSize(textSize);
    }
}
