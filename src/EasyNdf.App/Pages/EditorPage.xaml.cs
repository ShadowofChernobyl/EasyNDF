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
    /// <summary>What the editor shows: the file, its export, or the export's changed lines; null while there is no file.</summary>
    private ShownText? shown;

    /// <summary>The text size; the line height goes with it, 17 pixels for 12, as in the HTML app.</summary>
    private double textSize = TextZoom.UsualSize;

    /// <summary>The status strip under the text.</summary>
    private StatusStrip? strip;
    private readonly LineHeight lineHeight = new();
    private readonly LineNumbers lineNumbers = new();
    private readonly MarkColumn markColumn = new();
    private readonly Border textGap = new() { Width = 4 };
    private readonly LineShading shading = new();
    private bool showingPick;

    /// <summary>The syntax check of the file shown, and its problems in the status strip.</summary>
    private readonly SyntaxProblems syntaxProblems;
    private readonly ProblemList problemList;

    /// <summary>The syntax check, for the checks.</summary>
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
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshTreeOnFirstOpen(); };
        Text.Options.ConvertTabsToSpaces = true; // Tab types four spaces, as the Sublime settings file asks
        Text.Options.IndentationSize = 4;
        Text.Options.EnableHyperlinks = false;
        Text.Options.EnableEmailHyperlinks = false;
        Text.Options.AllowScrollBelowDocument = false;
        Text.TextArea.SetResourceReference(ICSharpCode.AvalonEdit.Editing.TextArea.SelectionBrushProperty, "sel");
        Text.TextArea.SelectionForeground = null;
        Text.TextArea.SelectionBorder = null;
        Text.TextArea.SelectionCornerRadius = 0;
        Text.TextArea.SetResourceReference(ICSharpCode.AvalonEdit.Editing.TextArea.ForegroundProperty, "ink");
        Text.TextArea.Caret.CaretBrush = (System.Windows.Media.Brush)FindResource("ink");
        Theming.ThemeManager.Applied += RedrawInTheme;
        // the line numbers, then the column for marks (and later the folding arrows), then 4 pixels before the text
        Text.TextArea.LeftMargins.Insert(0, lineNumbers);
        Text.TextArea.LeftMargins.Insert(1, markColumn);
        Text.TextArea.LeftMargins.Insert(2, textGap);
        Text.TextArea.TextView.ElementGenerators.Add(lineHeight);
        lineHeight.KeepGuessesRight(Text.TextArea.TextView);
        Text.TextArea.TextView.BackgroundRenderers.Add(shading);
        Text.TextArea.TextView.BackgroundRenderers.Add(new IndentGuides());
        _ = new AutoPairs(Text.TextArea);
        StartFolding();
        StartDescriptors();
        Text.PreviewMouseWheel += WheelTurned;
        Text.PreviewKeyDown += KeyPressed;
        PreviewKeyDown += FindBarKeys;
        foreach (TextBox box in new[] { FindBox, GoBox, SearchAllBox }) box.TextChanged += FindBoxChanged;
        Text.TextChanged += TextEdited;
        // the results of Search all files take at most 40% of the tab, so the panes under them keep their room
        SizeChanged += (_, _) => ResultsBox.MaxHeight = Math.Max(ResultsBox.MinHeight, 0.4 * ActualHeight);
        StripHolder.Content = strip = new StatusStrip(Text);
        strip.AddTools(FoldButtons());
        syntaxProblems = new SyntaxProblems(Text);
        problemList = new ProblemList(syntaxProblems);
        strip.AddAfterCaretPlace(problemList.StripText);
        // the list of problems lies over the bottom of the text, above the strip
        ((Grid)Text.Parent).Children.Add(problemList.Panel);
        SetTextSize(TextZoom.Saved, remember: false);
        TextZoom.Changed += size => SetTextSize(size, remember: false);
        ApplyViewSettings();
        Show();
    }

    /// <summary>The "Text size" setting: the text takes a size, which is kept for next time.</summary>
    public void ChangeTextSize(double size) => SetTextSize(size, remember: true);

    /// <summary>The "Line numbers" and "Word wrap" settings, applied to the text.</summary>
    public void ApplyViewSettings()
    {
        bool numbers = AppData.Current.Settings["lineNumbers"]?.GetValue<bool>() != false;
        lineNumbers.Visibility = numbers ? Visibility.Visible : Visibility.Collapsed;
        Text.WordWrap = AppData.Current.Settings["wordWrap"]?.GetValue<bool>() == true;
    }

    /// <summary>The name of the file open here when it has edits not yet saved, which closing the app would lose; otherwise null.</summary>
    public string? EditedFileName => shown?.File is { IsEdited: true } edited ? edited.Name : null;

    /// <summary>Keeps which file of the Files tab is open, and its top line, for "Reopen the last file".</summary>
    public void KeepOpenFile()
    {
        if (shown?.Entry is not FileEntry entry) { AppData.Current.State.Remove("editorFile"); return; }
        AppData.Current.State["editorFile"] = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = entry.Name,
            ["folder"] = entry.Folder,
            ["line"] = TopLine,
        };
    }

    /// <summary>
    /// "Reopen the last file": as the app starts, the file last open opens again at the line it was left at, when it is
    /// still on the Files tab. The Files tab stays in front.
    /// </summary>
    public async Task ReopenLastFile()
    {
        if (AppData.Current.Settings["reopenFile"]?.GetValue<bool>() == false) return;
        if (AppData.Current.State["editorFile"] is not System.Text.Json.Nodes.JsonObject kept || Files is null) return;
        string name = (string?)kept["name"] ?? "", folder = (string?)kept["folder"] ?? "";
        FileEntry? entry = Files.Entries.FirstOrDefault(e => e.Name == name && e.Folder == folder);
        if (entry is null) return;
        await Open(entry, showTab: false);
        int line = (int?)kept["line"] ?? 1;
        if (shown?.Entry == entry && line > 1) Text.ScrollToLine(line);
    }

    /// <summary>The line height for the text size: 17 pixels for 12, rounded.</summary>
    private double LineHeightForSize => Math.Round(textSize * 17 / 12, MidpointRounding.AwayFromZero);

    private MessageOverlay Messages => MainView.Of(this).Messages;

    // ---- opening a file ----

    /// <summary>
    /// "Open in editor" and a double-click on the Files tab (the HTML app's <c>edOpenJob</c>): the row's file opens here.
    /// Edits not saved in the file open now are asked about first.
    /// </summary>
    public async Task Open(FileEntry entry) => await Open(entry, showTab: true);

    /// <summary>Opens a file of the Files tab; with <paramref name="showTab"/>, the Editor tab comes to the front.</summary>
    private async Task Open(FileEntry entry, bool showTab)
    {
        if (entry.Source is null || !await MayLeave()) return;
        if (showTab) MainView.Of(this).Tabs.SelectedItem = MainView.Of(this).EditorTabItem;
        Note.Text = Texts.Get("Editor.Viewer.ReadingFile", ("file", entry.Name));
        try
        {
            string text = await Task.Run(() => FileContent.Text(entry));
            Load(entry.Name, text, entry);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            shown = null;
            ShowText();
            await Messages.Tell(Texts.Get("Editor.CouldNotRead", ("file", entry.Name), ("problem", error.Message)));
        }
    }

    /// <summary>Puts a file's text into the editor (the HTML app's <c>edLoad</c>). Line ends are kept to be put back on saving.</summary>
    public void Load(string name, string text, FileEntry? entry)
    {
        bool windowsLineEnds = text.Contains("\r\n", StringComparison.Ordinal);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var document = new TextDocument(text);
        shown = new ShownText
        {
            Name = name, Entry = entry, HasWindowsLineEnds = windowsLineEnds, Original = text,
            Document = document, Colourizer = new NdfColourizer(document),
        };
        ShowText();
    }

    /// <summary>
    /// Whether the file open now may be put away (the HTML app's <c>edLeave</c>): yes when it has no edits, or when the
    /// user says to discard them.
    /// </summary>
    private async Task<bool> MayLeave() =>
        shown?.File is not { IsEdited: true } edited || await Messages.Confirm(Texts.Get("Editor.DiscardUnsaved", ("file", edited.Name)), Texts.Get("Editor.Discard"));

    private async void FilePicked(object sender, SelectionChangedEventArgs e)
    {
        if (showingPick || FilePick.SelectedItem is not ChoiceOption { Value: { Length: > 0 } id }) return;
        FileEntry? entry = Files?.Entries.FirstOrDefault(f => f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == id);
        if (entry is not null) await Open(entry);
        else ShowPick();
    }

    private async void OpenFileClicked(object sender, RoutedEventArgs e) => await OpenFileOnItsOwn();

    /// <summary>"Open file...": a file that is not on the Files tab.</summary>
    private async Task OpenFileOnItsOwn()
    {
        var dialog = new OpenFileDialog { Title = Texts.Get("Editor.OpenFile.Title"), Filter = Texts.Get("Files.NdfFiles") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || !await MayLeave()) return;
        try { Load(Path.GetFileName(dialog.FileName), await File.ReadAllTextAsync(dialog.FileName), null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await Messages.Tell(Texts.Get("Editor.CouldNotRead", ("file", Path.GetFileName(dialog.FileName)), ("problem", error.Message)));
        }
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
        if (!await Messages.Confirm(Texts.Get("Editor.DiscardAll", ("file", open.Name)), Texts.Get("Editor.Discard"))) return;
        Load(open.Name, open.Original, open.Entry);
        shown!.HasWindowsLineEnds = open.HasWindowsLineEnds;
    }

    /// <summary>
    /// The text to save, with the file's own line endings put back. With "Changes only" shown, it is the whole export.
    /// </summary>
    private string TextToSave()
    {
        ShownText whole = shown!.Parent ?? shown;
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
            _ = Messages.Tell(Texts.Get("Editor.CouldNotSave", ("problem", error.Message)));
        }
    }

    /// <summary>
    /// "Use in file list": the file list uses the edited text from now on, so Preview and Run export apply the rules to it.
    /// A file opened on its own is added to the list.
    /// </summary>
    private async void UseClicked(object sender, RoutedEventArgs e)
    {
        if (shown is not { IsFile: true } open || Files is null) return;
        byte[] edited = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(TextToSave());
        open.Original = Text.Text;
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
        Note.Text = Texts.Get("Editor.Note.UsedInFileList", ("file", open.Name));
    }

    /// <summary>The first edit of a file marks it as edited: Discard changes and Use in file list can be used.</summary>
    private void TextEdited(object? sender, EventArgs e)
    {
        if (shown is not { IsFile: true, IsEdited: false } open) return;
        // a text of another length differs: only one as long as the original is compared with it, character for character
        if (Text.Document.TextLength == open.OriginalLength && Text.Text == open.Original) return;
        open.IsEdited = true;
        Show();
    }

    // ---- showing it ----

    /// <summary>
    /// Puts the shown text into the editor (the HTML app's <c>edShow</c>): its text and colours, read-only for an export,
    /// with its own line numbers and marks for "Changes only". Then the buttons and the note.
    /// </summary>
    private void ShowText()
    {
        TextView view = Text.TextArea.TextView;
        PutFoldingAway();
        foreach (IVisualLineTransformer transformer in view.LineTransformers.Where(t => t is NdfColourizer or MutedLines).ToList())
            view.LineTransformers.Remove(transformer);
        // only the file itself is checked: an export cannot be edited; a dictionary file such as UNITS.csv is not NDF
        bool isNdf = shown is not null && !Data.Divisions.FileAndTextRules.IsDictionaryFile(shown.Name);
        syntaxProblems.Watch(shown is { IsFile: true } && isNdf ? shown.Document : null);
        if (shown is null)
        {
            Show();
            return;
        }
        Text.Document = shown.Document;
        Text.IsReadOnly = shown.IsExport;
        view.LineTransformers.Add(shown.Colourizer!);
        ChangedLines? changes = shown.Changes;
        if (changes is not null) view.LineTransformers.Add(new MutedLines(changes.Marks));

        // a read-only view's numbers are as wide as the whole export's, and its text follows the marks with no gap
        lineNumbers.NumberOf = changes is null ? null : number => changes.Numbers[number - 1];
        int wholeLength = (shown.Parent ?? shown).Document.LineCount;
        lineNumbers.Digits = shown.IsExport ? wholeLength.ToString(System.Globalization.CultureInfo.InvariantCulture).Length : null;
        lineNumbers.InvalidateMeasure();
        markColumn.Marks = changes?.Marks;
        textGap.Width = shown.IsExport ? 0 : 4;
        shading.Marks = changes?.Marks;
        shading.PointedAt = shown.PointedAt;
        BringFoldingBack();
        Show();
        KickDescriptors(0);
    }

    /// <summary>Shows the buttons that can be used, and the note (the HTML app's <c>edBar</c>).</summary>
    private void Show()
    {
        bool has = shown is not null, isFile = shown is { IsFile: true };
        EmptyText.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        Text.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>The dropdown of the Files tab's files, by file name, with a folder only where two files share a name.</summary>
    public void ShowPick()
    {
        List<FileEntry> listed = [.. (Files?.Entries ?? []).Where(f => f.Source is not null)];
        var names = new PropertyScans(new Dictionary<string, Data.ScanInfo>(), [.. listed.Select(FileListModel.ScanKey)]);
        var options = new List<ChoiceOption> { new("", Files is { Entries.Count: > 0 } ? Texts.Get("Editor.ChooseFile") : Texts.Get("Editor.NoFiles")) };
        foreach (FileEntry entry in listed.OrderBy(FileListModel.ScanKey, Comparer<string>.Create(PropertyScans.CompareByFileName)))
        {
            string key = FileListModel.ScanKey(entry), folder = names.Folder(key);
            options.Add(new ChoiceOption(entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.Name, folder != "" ? PropertyScans.FolderTag(folder) : ""));
        }
        showingPick = true;
        FilePick.SetResourceReference(ItemsControl.ItemTemplateProperty, "DimmedChoice");
        FilePick.ItemsSource = options;
        string current = shown?.Entry?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        FilePick.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options[0];
        showingPick = false;
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

    /// <summary>The note says something else, in the plain colour or in "caution" or "warn".</summary>
    private void Say(string text, string? colour = null)
    {
        Note.Inlines.Clear();
        Note.Inlines.Add(colour is null ? new Run(text) : Coloured(text, colour));
    }

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

    /// <summary>Whether the search-all results were showing when the find bar was hidden, to show them again with it.</summary>
    private bool resultsHiddenWithBar;

    /// <summary>A find box emptied: the search is done, so the line it found is no longer marked.</summary>
    private void FindBoxChanged(object sender, TextChangedEventArgs e)
    {
        if (((TextBox)sender).Text == "") ClearPointedLine();
    }

    /// <summary>Unmarks the line that Find, Go to line, a search result or a descriptor marked.</summary>
    private void ClearPointedLine()
    {
        if (shown is null || shown.PointedAt is null) return;
        shown.PointedAt = null;
        shading.PointedAt = null;
        Text.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
    }

    /// <summary>
    /// The tab's keys, anywhere on it: Ctrl+F finds, Ctrl+H replaces, Ctrl+G goes to a line, Ctrl+Shift+F searches all
    /// files; Ctrl+Shift+[ folds every descriptor and Ctrl+Shift+] unfolds them; Escape in the find bar hides it.
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
        if (box is not null)
        {
            e.Handled = true;
            ShowFindBar(true, box);
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
        Text.TextArea.Caret.CaretBrush = (System.Windows.Media.Brush)FindResource("ink");
        Text.TextArea.TextView.Redraw();
        lineNumbers.InvalidateVisual();
        markColumn.InvalidateVisual();
    }

    /// <summary>
    /// Sets the text size, from 7 to 36 pixels; the line that was at the top stays at the top. A size to remember is
    /// saved and given to the Add Descriptor rule's text box and the status strip too.
    /// </summary>
    private void SetTextSize(double size, bool remember)
    {
        if (remember)
        {
            TextZoom.Set(size); // comes back here through TextZoom.Changed
            return;
        }
        int topLine = Text.TextArea.TextView.GetDocumentLineByVisualTop(Text.TextArea.TextView.ScrollOffset.Y)?.LineNumber ?? 1;
        textSize = TextZoom.Fit(size);
        Text.FontSize = textSize;
        lineHeight.Height = markColumn.LineHeight = LineHeightForSize;
        Text.TextArea.TextView.Redraw();
        Text.ScrollToLine(topLine);
        strip?.ShowSize(textSize);
    }
}
