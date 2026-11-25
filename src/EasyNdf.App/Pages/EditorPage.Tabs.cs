using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Editor;
using EasyNdf.App.Text;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace EasyNdf.App.Pages;

/// <summary>
/// The open files (the "File Editor Tabs" mock, 2026-11-09). Each file is a tab over the text, with × at its right to
/// close it, and has its own text box (<see cref="FileView"/>), which keeps its text, undo steps, place, folds and checks.
/// A click on a tab shows that file alone; Ctrl+click puts up to three side by side, or takes one away; Shift+click shows
/// the tabs from the one in use to the one clicked. The file in use, the one last clicked or typed in, is the one the
/// buttons, Find, the Descriptor Tree and Properties work on. The rest of the page reaches it through the properties
/// below, which keep the names the fields had when the editor had one text box.
/// </summary>
public partial class EditorPage
{
    /// <summary>The most files shown side by side.</summary>
    private const int MostSideBySide = 3;

    /// <summary>The narrowest a file shown beside others is made.</summary>
    private const double NarrowestSideBySide = 160;

    /// <summary>A file open in a tab: its text box, with everything that goes with one text.</summary>
    private sealed class FileView(TextEditor editor)
    {
        public readonly TextEditor Editor = editor;

        /// <summary>What it shows: the file, its export or its changes only; null while there is none, or while it is let go of.</summary>
        public ShownText? Shown;

        /// <summary>The file put away while another tab of the app is in front (EditorPage.LetGo.cs); null while none is.</summary>
        public LetGoFile? LetGo;

        /// <summary>How many texts were put in it, so a slow one does not replace one put in after it.</summary>
        public int Loads;

        public readonly LineHeight LineHeight = new();
        public readonly LineNumbers LineNumbers = new();
        public readonly MarkColumn MarkColumn = new();
        public readonly Border TextGap = new() { Width = 4 };
        public readonly LineShading Shading = new();
        public SyntaxProblems SyntaxProblems = null!;
        public ProblemList ProblemList = null!;
        public EditorSuggestions Suggestions = null!;
        public StatusStrip Strip = null!;
        public Button? FoldAllButton, UnfoldAllButton;

        /// <summary>The guide of the values for its text; null for none (not WARNO's NDF, or not read yet).</summary>
        public ValueGuide? Guide;

        /// <summary>Whether its text wants the guide, so a guide read late is still given to it.</summary>
        public bool WantsGuide;

        // folding (EditorPage.Folding.cs)
        public FoldingManager? Folding;
        public ShownText? FoldingFor;
        public readonly FoldedBlocks FoldedBlocks = new();
        public readonly Dictionary<int, int> BlockEnds = [], BlockEndsBefore = [];
        public readonly HashSet<int> EndsBeingFound = [];
        public int TextChanges;

        /// <summary>The text changed: the block ends worked out are kept as guesses until found again.</summary>
        public void ForgetBlockEnds(object? sender, DocumentChangeEventArgs e)
        {
            TextChanges++;
            foreach ((int line, int end) in BlockEnds) BlockEndsBefore[line] = end;
            BlockEnds.Clear();
        }

        // its tab and its box
        public readonly ToggleButton Tab = new();
        public readonly TextBlock TabName = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock TabDot = new() { Text = "• ", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        public Button TabClose = null!;
        public readonly Bevel Box = new();
        public readonly Bevel Caption = new() { Edges = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 2, 8, 3) };
        public readonly TextBlock CaptionName = new() { FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        public readonly TextBlock CaptionNote = new() { Margin = new Thickness(8, 0, 0, 0), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };

        /// <summary>Its share of the width beside the others.</summary>
        public double Share = 1;

        /// <summary>The file it shows, as its tab names it: the file's own name also while its export is shown.</summary>
        public string Name => Shown?.File.Name ?? LetGo?.Entry.Name ?? "";

        /// <summary>The row of the Files tab its file is, or null for a file opened on its own.</summary>
        public FileEntry? Entry => Shown?.File.Entry ?? LetGo?.Entry;

        /// <summary>Whether its file has edits not yet used in the file list or saved.</summary>
        public bool IsEdited => Shown?.File.IsEdited == true;
    }

    /// <summary>The tabs, in their order.</summary>
    private readonly List<FileView> openViews = [];

    /// <summary>The files shown side by side, in the tabs' order: one, or up to <see cref="MostSideBySide"/>.</summary>
    private readonly List<FileView> shownViews = [];

    /// <summary>The file in use; with no file open, a text box no tab shows yet, which the next file opened takes.</summary>
    private FileView view = null!;

    /// <summary>Whether a file is shown, for the tutorial.</summary>
    internal bool HasFile => view?.Shown is not null;

    /// <summary>The name of the file shown, for the tutorial; "" with none.</summary>
    internal string ShownName => view?.Shown?.Name ?? "";

    /// <summary>Whether the file shown is its export (Show Export), for the tutorial.</summary>
    internal bool ShowsExport => view?.Shown?.IsExport == true;

    // ---- what the rest of the page knows as the editor: the file in use ----

    /// <summary>The text box of the file in use.</summary>
    internal TextEditor Text => view.Editor;

    /// <summary>What the file in use shows: the file, its export, or the export's changed lines; null while there is no file.</summary>
    private ShownText? shown { get => view.Shown; set => view.Shown = value; }

    private StatusStrip strip => view.Strip;
    private LineHeight lineHeight => view.LineHeight;
    private LineNumbers lineNumbers => view.LineNumbers;
    private MarkColumn markColumn => view.MarkColumn;
    private Border textGap => view.TextGap;
    private LineShading shading => view.Shading;

    /// <summary>The syntax check of the file in use, and its problems in its status strip.</summary>
    private SyntaxProblems syntaxProblems => view.SyntaxProblems;
    private ProblemList problemList => view.ProblemList;
    private EditorSuggestions suggestions => view.Suggestions;

    /// <summary>The status strip of the file in use, for the checks.</summary>
    internal StatusStrip Strip => view.Strip;

    /// <summary>For the checks: the names of the tabs, of the files shown side by side, and of the file in use.</summary>
    internal (List<string> Tabs, List<string> Shown, string InUse) TabNames => ([.. openViews.Select(v => v.Name)], [.. shownViews.Select(v => v.Name)], openViews.Contains(view) ? view.Name : "");

    /// <summary>The text box of the file in use and the others open, and the waiting one when none is.</summary>
    private IEnumerable<FileView> AllViews => openViews.Contains(view) ? openViews : openViews.Append(view);

    // ---- a text box for a file ----

    /// <summary>
    /// A text box with everything that goes with one text: its margins, colours behind the text, folding, checks, tips,
    /// suggestions and status strip, and its tab. Pressing anywhere in its box, or giving its text the keyboard, makes
    /// its file the one in use.
    /// </summary>
    private FileView MakeView()
    {
        var editor = new TextEditor
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        editor.SetResourceReference(Control.FontFamilyProperty, "mono");
        editor.SetResourceReference(Control.BackgroundProperty, "well");
        editor.SetResourceReference(Control.ForegroundProperty, "ink");
        var v = new FileView(editor);
        editor.Options.ConvertTabsToSpaces = true; // Tab types four spaces, as the Sublime settings file asks
        editor.Options.IndentationSize = 4;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.AllowScrollBelowDocument = false;
        editor.TextArea.SetResourceReference(ICSharpCode.AvalonEdit.Editing.TextArea.SelectionBrushProperty, "sel");
        editor.TextArea.SelectionForeground = null;
        editor.TextArea.SelectionBorder = null;
        editor.TextArea.SelectionCornerRadius = 0;
        editor.TextArea.SetResourceReference(ICSharpCode.AvalonEdit.Editing.TextArea.ForegroundProperty, "ink");
        editor.TextArea.Caret.CaretBrush = (System.Windows.Media.Brush)FindResource("ink");
        // the line numbers, then the column for marks (and the folding arrows), then 4 pixels before the text
        editor.TextArea.LeftMargins.Insert(0, v.LineNumbers);
        editor.TextArea.LeftMargins.Insert(1, v.MarkColumn);
        editor.TextArea.LeftMargins.Insert(2, v.TextGap);
        editor.TextArea.TextView.ElementGenerators.Add(v.LineHeight);
        v.LineHeight.KeepGuessesRight(editor.TextArea.TextView);
        editor.TextArea.TextView.BackgroundRenderers.Add(v.Shading);
        editor.TextArea.Caret.PositionChanged += (_, _) => ClearPointedLineWhenLeft(v);
        editor.TextArea.TextView.BackgroundRenderers.Add(new IndentGuides());
        _ = new AutoPairs(editor.TextArea);
        StartFolding(v);
        editor.PreviewMouseWheel += WheelTurned;
        editor.PreviewKeyDown += KeyPressed;
        editor.TextChanged += (_, _) => TextEdited(v);
        // the descriptors are those of the file in use, which is the one typed in
        editor.TextChanged += (sender, e) => { if (v == view) DescriptorTextChanged(sender, e); };
        v.Strip = new StatusStrip(editor);
        v.Strip.AddTools(FoldButtons(v));
        v.SyntaxProblems = new SyntaxProblems(editor);
        v.ProblemList = new ProblemList(v.SyntaxProblems);
        v.Suggestions = new EditorSuggestions(editor);
        StartValueHelp(v);
        v.Strip.AddAfterCaretPlace(v.ProblemList.StripText);

        // its box: the name over the text while files are side by side, the strip under it, the problems' list over its bottom
        v.Caption.SetResourceReference(Bevel.BottomRightProperty, "line");
        v.Caption.SetResourceReference(Bevel.BackgroundProperty, "panel");
        v.CaptionNote.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        v.Caption.Child = new DockPanel { Children = { v.CaptionName, v.CaptionNote } };
        v.Caption.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(v.Caption, Dock.Top);
        DockPanel.SetDock(v.Strip, Dock.Bottom);
        var text = new Grid { Children = { editor, v.ProblemList.Panel } };
        v.Box.SetResourceReference(Bevel.TopLeftProperty, "lo");
        v.Box.SetResourceReference(Bevel.BottomRightProperty, "hi");
        v.Box.SetResourceReference(Bevel.BackgroundProperty, "well");
        v.Box.Child = new DockPanel { Children = { v.Caption, v.Strip, text } };
        v.Box.PreviewMouseDown += (_, _) => Use(v);
        editor.TextArea.GotKeyboardFocus += (_, _) => Use(v);

        // its tab: the dot of unsaved edits, the name, and × to close it. The × reaches into the tab's padding, so a tab is
        // as tall as a line of text and its padding: the tabs then end where the "descriptors shown" note beside them does
        // with the panes beside, and the text boxes start level with the Descriptor Tree
        var close = new Button { Style = (Style)FindResource("TabCloseButton"), Margin = new Thickness(6, -3, 0, -3), VerticalAlignment = VerticalAlignment.Center };
        close.Click += (_, e) =>
        {
            e.Handled = true;
            _ = Close(v);
        };
        v.TabClose = close;
        DockPanel.SetDock(close, Dock.Right);
        v.TabDot.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        v.Tab.Style = (Style)FindResource("FileTab");
        v.Tab.Content = new DockPanel { Children = { close, v.TabDot, v.TabName } };
        v.Tab.Click += (_, _) => TabClicked(v, Keyboard.Modifiers.HasFlag(ModifierKeys.Control), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

        ApplyViewSettings(v);
        SetTextSize(v);
        return v;
    }

    /// <summary>A file closed: its text box lets go of its text and its checks.</summary>
    private void Empty(FileView v)
    {
        v.Shown = null;
        v.LetGo = null;
        v.Loads++;
        ShowText(v);
        puttingIn = true;
        try { v.Editor.Document = new TextDocument(); }
        finally { puttingIn = false; }
    }

    // ---- the tabs ----

    /// <summary>
    /// A click on a tab: that file alone; with Ctrl, it is added beside the others (up to three) or taken away; with
    /// Shift, the tabs from the file in use to this one are shown. The text then has the keyboard.
    /// </summary>
    private void TabClicked(FileView v, bool control, bool shift)
    {
        if (control)
        {
            if (shownViews.Contains(v))
            {
                if (shownViews.Count > 1)
                {
                    shownViews.Remove(v);
                    if (view == v) view = shownViews[^1];
                }
            }
            else if (shownViews.Count >= MostSideBySide) Say(Texts.Get("Editor.Tabs.AtMost", ("count", MostSideBySide)), NoticeKind.Caution);
            else
            {
                shownViews.Add(v);
                view = v;
            }
        }
        else if (shift && openViews.Contains(view))
        {
            int from = openViews.IndexOf(view), to = openViews.IndexOf(v);
            List<FileView> run = openViews.GetRange(Math.Min(from, to), Math.Abs(to - from) + 1);
            if (run.Count > MostSideBySide) Say(Texts.Get("Editor.Tabs.AtMost", ("count", MostSideBySide)), NoticeKind.Caution);
            else
            {
                shownViews.Clear();
                shownViews.AddRange(run);
                view = v;
            }
        }
        else
        {
            shownViews.Clear();
            shownViews.Add(v);
            view = v;
        }
        ShowSideBySide();
        Dispatcher.BeginInvoke(() => { if (Text.IsVisible) Text.TextArea.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>For the checks: a click on a file's tab, with Ctrl or Shift held or not.</summary>
    internal void ClickTabForChecks(string name, bool control = false, bool shift = false) => TabClicked(openViews.First(v => v.Name == name), control, shift);

    /// <summary>For the checks: × on a file's tab.</summary>
    internal Task CloseTabForChecks(string name) => Close(openViews.First(v => v.Name == name));

    /// <summary>The tabs: which files are shown (pressed), which is in use (bold), which has unsaved edits (•).</summary>
    private void ShowTabs()
    {
        if (!FileTabs.Children.Cast<UIElement>().SequenceEqual(openViews.Select(v => v.Tab)))
        {
            FileTabs.Children.Clear();
            foreach (FileView v in openViews) FileTabs.Children.Add(v.Tab);
        }
        foreach (FileView v in openViews)
        {
            v.Tab.IsChecked = shownViews.Contains(v);
            v.Tab.Tag = v == view ? "InUse" : null;
            v.TabName.Text = v.Name;
            v.TabDot.Visibility = v.IsEdited ? Visibility.Visible : Visibility.Collapsed;
            string where = v.Entry?.Source ?? v.Name;
            v.Tab.ToolTip = Texts.Get("Editor.Tab.Tip", ("file", v.Name), ("path", where), ("count", MostSideBySide));
            v.TabClose.ToolTip = Texts.Get("Editor.Tab.Close.Tip");
        }
        TabStrip.Visibility = openViews.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // the tabs share the width again for their names now
        FileTabs.InvalidateMeasure();
    }

    /// <summary>While files are side by side, each is named over its text, the one in use in the accent colour.</summary>
    private void ShowCaptions()
    {
        bool many = shownViews.Count > 1;
        foreach (FileView v in shownViews)
        {
            v.Caption.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
            if (!many) continue;
            v.CaptionName.Text = v.Name;
            v.CaptionName.SetResourceReference(TextBlock.ForegroundProperty, v == view ? "accent" : "ink");
            v.CaptionNote.Text = v.Shown is ShownText text ? Texts.CountWide("Editor.Note.Lines", text.Document.LineCount) : "";
        }
    }

    /// <summary>
    /// Lays out the files shown, side by side in the tabs' order, with a bar between two to drag; a file let go of is read
    /// again once shown. Then what follows the file in use.
    /// </summary>
    private void ShowSideBySide()
    {
        shownViews.Sort((a, b) => openViews.IndexOf(a).CompareTo(openViews.IndexOf(b)));
        SideBySide.Children.Clear();
        SideBySide.ColumnDefinitions.Clear();
        for (int i = 0; i < shownViews.Count; i++)
        {
            FileView v = shownViews[i];
            if (i > 0)
            {
                SideBySide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
                SplitBar bar = BarBetween(shownViews[i - 1], v);
                Grid.SetColumn(bar, SideBySide.ColumnDefinitions.Count - 1);
                SideBySide.Children.Add(bar);
            }
            SideBySide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(v.Share, GridUnitType.Star), MinWidth = NarrowestSideBySide });
            Grid.SetColumn(v.Box, SideBySide.ColumnDefinitions.Count - 1);
            SideBySide.Children.Add(v.Box);
            if (v.LetGo is not null && v.Shown is null) _ = BringBack(v);
        }
        ShowInUse();
    }

    /// <summary>The widths of the files shown, from their shares.</summary>
    private void ShowShares()
    {
        for (int i = 0; i < shownViews.Count && 2 * i < SideBySide.ColumnDefinitions.Count; i++)
            SideBySide.ColumnDefinitions[2 * i].Width = new GridLength(shownViews[i].Share, GridUnitType.Star);
    }

    /// <summary>The bar between two files shown: dragged, what one gains in width the other loses; double-clicked, all share it evenly.</summary>
    private SplitBar BarBetween(FileView left, FileView right)
    {
        var bar = new SplitBar { ToolTip = Texts.Get("Editor.DescriptorSplit.Tip") };
        bar.Moved += x =>
        {
            double start = left.Box.TranslatePoint(new Point(0, 0), SideBySide).X;
            double end = right.Box.TranslatePoint(new Point(right.Box.ActualWidth, 0), SideBySide).X;
            double room = end - start - 9;
            if (room < 2 * NarrowestSideBySide) return;
            double leftWidth = Math.Clamp(x - start - 4, NarrowestSideBySide, room - NarrowestSideBySide);
            double both = left.Share + right.Share;
            left.Share = both * leftWidth / room;
            right.Share = both - left.Share;
            ShowShares();
        };
        bar.Reset += () =>
        {
            foreach (FileView v in shownViews) v.Share = 1;
            ShowShares();
        };
        return bar;
    }

    /// <summary>Makes a file shown the one in use: the buttons, the note, Find, the Descriptor Tree and Properties follow it.</summary>
    private void Use(FileView v)
    {
        if (v == view || !shownViews.Contains(v)) return;
        view = v;
        ShowInUse();
    }

    /// <summary>What follows the file in use: the tabs and names, the buttons and the note, and the descriptors.</summary>
    private void ShowInUse()
    {
        Show();
        KickDescriptors(0);
    }

    /// <summary>
    /// Shows an open file: when it is not shown yet, it takes the place of the file in use; it is then the one in use.
    /// </summary>
    private void Bring(FileView v)
    {
        if (!shownViews.Contains(v))
        {
            int place = shownViews.IndexOf(view);
            if (place >= 0)
            {
                v.Share = view.Share;
                shownViews[place] = v;
            }
            else
            {
                shownViews.Clear();
                shownViews.Add(v);
            }
        }
        view = v;
        ShowSideBySide();
    }

    /// <summary>
    /// A new tab, after the one in use, shown in its place and in use, for a file about to be put in; the text box waiting
    /// while none is open takes the first.
    /// </summary>
    private FileView NewTab()
    {
        FileView v = openViews.Contains(view) ? MakeView() : view;
        int after = openViews.IndexOf(view);
        openViews.Insert(after < 0 ? openViews.Count : after + 1, v);
        int place = shownViews.IndexOf(view);
        if (place >= 0 && v != view)
        {
            v.Share = view.Share;
            shownViews[place] = v;
        }
        else
        {
            shownViews.Clear();
            shownViews.Add(v);
        }
        view = v;
        ShowSideBySide();
        return v;
    }

    /// <summary>The tab of a file of the Files tab, when it is open.</summary>
    private FileView? TabOf(FileEntry entry) => openViews.FirstOrDefault(v => v.Entry == entry);

    // ---- closing ----

    /// <summary>
    /// × on a tab: the file closes. One with edits not saved asks first: Use in File List keeps them where the rules run
    /// on them, Discard Changes lets them go, Cancel keeps the file open.
    /// </summary>
    private async Task Close(FileView v)
    {
        if (v.Shown?.File is { IsEdited: true } edited)
        {
            DialogAnswer reply = await Messages.Ask(Texts.Get("Editor.Tab.CloseEdited", ("file", edited.Name)), new DialogOptions
            {
                Ok = Texts.Get("Editor.UseInFileList"),
                Alternative = Texts.Get("Editor.DiscardChanges"),
                Icon = DialogIcon.Warning,
            });
            if (reply.Choice == DialogChoice.Cancel || !openViews.Contains(v)) return;
            if (reply.Choice == DialogChoice.Ok) await UseInFileList(v);
        }
        CloseNow(v);
    }

    /// <summary>
    /// Closes a tab without asking: when it was the only file shown, the tab beside it is shown instead; when it was the
    /// last, its text box waits for the next file.
    /// </summary>
    private void CloseNow(FileView v)
    {
        int at = openViews.IndexOf(v);
        if (at < 0) return;
        // kept for Ctrl+Shift+T
        if (v.Shown?.File.Entry is FileEntry closed) closedFiles.Add(closed);
        openViews.RemoveAt(at);
        shownViews.Remove(v);
        if (shownViews.Count == 0 && openViews.Count > 0) shownViews.Add(openViews[Math.Min(at, openViews.Count - 1)]);
        if (view == v) view = shownViews.Count > 0 ? shownViews[^1] : v;
        Empty(v);
        v.Share = 1;
        if (view != v) v.Editor.TextArea.TextView.Redraw();
        ShowSideBySide();
    }

    // ---- what is kept between sessions ----

    /// <summary>A file's place, for "Reopen the last file": its row of the Files tab and its top line; no row for a file opened on its own.</summary>
    private (FileEntry? Entry, int Line) PlaceOf(FileView v) =>
        v.LetGo is LetGoFile kept && v.Shown is null ? (kept.Entry, kept.TopLine) : (v.Entry, v.Editor.TextArea.TextView.GetDocumentLineByVisualTop(v.Editor.VerticalOffset)?.LineNumber ?? 1);

    /// <summary>
    /// Keeps the files of the Files tab open, in their tabs' order, each with its top line, which are shown side by side
    /// and which is in use, for "Reopen the last file".
    /// </summary>
    public void KeepOpenFile()
    {
        var files = new JsonArray();
        foreach (FileView v in openViews)
        {
            if (PlaceOf(v) is not (FileEntry entry, int line)) continue;
            files.Add(new JsonObject
            {
                ["name"] = entry.Name,
                ["folder"] = entry.Folder,
                ["line"] = line,
                ["shown"] = shownViews.Contains(v),
                ["inUse"] = v == view,
            });
        }
        AppData.Current.State.Remove("editorFile");
        if (files.Count == 0) AppData.Current.State.Remove("editorFiles");
        else AppData.Current.State["editorFiles"] = files;
    }

    /// <summary>
    /// "Reopen the last file": as the app starts, the files last open open again in their tabs, at the line each was left
    /// at, side by side as they were, when they are still on the Files tab. The Files tab stays in front.
    /// </summary>
    public async Task ReopenLastFile()
    {
        if (AppData.Current.Settings["reopenFile"]?.GetValue<bool>() == false || Files is null) return;
        // the files kept, or the one file an older version kept
        List<JsonObject> kept = AppData.Current.State["editorFiles"] is JsonArray files ? [.. files.OfType<JsonObject>()]
            : AppData.Current.State["editorFile"] is JsonObject one ? [one] : [];
        var reopened = new List<(FileView View, int Line, bool Shown, bool InUse)>();
        foreach (JsonObject file in kept)
        {
            string name = (string?)file["name"] ?? "", folder = (string?)file["folder"] ?? "";
            if (Files.Entries.FirstOrDefault(e => e.Name == name && e.Folder == folder) is not FileEntry entry || TabOf(entry) is not null) continue;
            await Open(entry, showTab: false);
            if (TabOf(entry) is FileView v)
                reopened.Add((v, (int?)file["line"] ?? 1, (bool?)file["shown"] ?? true, (bool?)file["inUse"] ?? true));
        }
        if (reopened.Count == 0) return;
        List<FileView> sideBySide = [.. reopened.Where(r => r.Shown).Select(r => r.View).Take(MostSideBySide)];
        shownViews.Clear();
        shownViews.AddRange(sideBySide.Count > 0 ? sideBySide : [reopened[^1].View]);
        view = reopened.FirstOrDefault(r => r.InUse && shownViews.Contains(r.View)).View ?? shownViews[^1];
        ShowSideBySide();
        foreach ((FileView v, int line, _, _) in reopened)
            if (line > 1 && v.Shown is not null) v.Editor.ScrollToLine(line);
    }
}
