using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Editor;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// Folding (the HTML app's <c>edBlock</c>, <c>edFoldAll</c>, <c>edUnfoldAll</c> and its fold arrows): a block, such as
/// a descriptor, an object inside it or a list, folds away to its first line. The text itself is never changed by it.
/// The file and its export fold; "Changes only" does not. The right-click menus are here too.
/// </summary>
public partial class EditorPage
{
    /// <summary>The folded blocks of the text shown; null for none (no file, or "Changes only").</summary>
    private FoldingManager? folding;

    /// <summary>The text <see cref="folding"/> belongs to, whose folds are kept when another text is shown.</summary>
    private ShownText? foldingFor;

    /// <summary>Draws the folded blocks.</summary>
    private readonly FoldedBlocks foldedBlocks = new();

    /// <summary>Where the block each line starts ends, as worked out since the text last changed, by line from 0.</summary>
    private readonly Dictionary<int, int> blockEnds = [];

    /// <summary>
    /// The ends worked out before the text last changed. A block too long to look through on a key typed keeps the arrow
    /// it had until its end is found again (see <see cref="ArrowEndOf"/>).
    /// </summary>
    private readonly Dictionary<int, int> blockEndsBefore = [];

    /// <summary>The lines whose block's end is being looked for away from the screen's thread.</summary>
    private readonly HashSet<int> endsBeingFound = [];

    /// <summary>Counts the changes to the text, so an end found in an older text is not kept.</summary>
    private int textChanges;

    /// <summary>How many lines below a line are looked through for its block's end while drawing its arrow.</summary>
    private const int LinesLookedAtOnce = 3000;

    /// <summary>Sets up folding when the page is built.</summary>
    private void StartFolding()
    {
        markColumn.Arrows = ArrowOf;
        markColumn.ArrowClicked += ToggleBlock;
        Text.TextArea.TextView.MouseHover += FoldedBlockHovered;
        Text.TextArea.TextView.MouseHoverStopped += (_, _) => foldTip.IsOpen = false;
        Text.PreviewMouseRightButtonUp += EditorRightClicked;
    }

    /// <summary>Before another text is shown: the folds of the one shown are kept with it, and its folding is taken off the editor.</summary>
    private void PutFoldingAway()
    {
        if (folding is null) return;
        if (foldingFor is not null)
        {
            foldingFor.Folds = [.. folding.AllFoldings.Where(f => f.IsFolded).Select(f => (f.StartOffset, f.EndOffset))];
            foldingFor.Document.Changed -= ForgetBlockEnds;
        }
        FoldingManager.Uninstall(folding);
        folding = null;
        foldingFor = null;
        foldedBlocks.Folding = null;
    }

    /// <summary>After a text is put in the editor: its folding, with the blocks it had folded.</summary>
    private void BringFoldingBack()
    {
        blockEnds.Clear();
        blockEndsBefore.Clear();
        textChanges++;
        if (shown is null || shown.IsChangesOnly) return;
        folding = FoldingManager.Install(Text.TextArea);
        // AvalonEdit's own margin is taken off: the mark column draws the arrows as the HTML app does. Its own marker stays,
        // as it is what lets the folding hide lines in this editor; FoldedBlocks goes before it, so the box drawn is ours.
        foreach (FoldingMargin margin in Text.TextArea.LeftMargins.OfType<FoldingMargin>().ToList()) Text.TextArea.LeftMargins.Remove(margin);
        Text.TextArea.TextView.ElementGenerators.Remove(foldedBlocks);
        Text.TextArea.TextView.ElementGenerators.Insert(0, foldedBlocks);
        foldedBlocks.Folding = folding;
        foldingFor = shown;
        shown.Document.Changed += ForgetBlockEnds;
        foreach ((int start, int end) in shown.Folds)
            if (end <= shown.Document.TextLength) Fold(start, end);
        markColumn.ArrowsLightUp = shown.IsExport;
    }

    private void ForgetBlockEnds(object? sender, DocumentChangeEventArgs e)
    {
        textChanges++;
        foreach ((int line, int end) in blockEnds) blockEndsBefore[line] = end;
        blockEnds.Clear();
    }

    /// <summary>The lines of the text shown, and the block each starts inside, as the folding reads them.</summary>
    private (DocumentLines Lines, IReadOnlyList<int> Blocks) LinesAndBlocks() => (new DocumentLines(shown!.Document), shown.Colourizer!.Blocks);

    /// <summary>
    /// A line's arrow: ▸ when its block is folded; ▾ when it starts one, which in the file means a block that closes
    /// (the HTML app's fold gutter), and in a read-only view any line that opens one (its <c>edCanFold</c>).
    /// </summary>
    private FoldArrow ArrowOf(int index)
    {
        if (folding is null || shown is null || index >= shown.Document.LineCount) return FoldArrow.None;
        DocumentLine line = shown.Document.GetLineByNumber(index + 1);
        if (folding.GetFoldingsAt(line.EndOffset).Any(f => f.IsFolded)) return FoldArrow.Shut;
        (DocumentLines lines, IReadOnlyList<int> blocks) = LinesAndBlocks();
        if (shown.IsExport) return NdfFolding.StartsBlock(lines, blocks, index) ? FoldArrow.Open : FoldArrow.None;
        return ArrowEndOf(index) > index ? FoldArrow.Open : FoldArrow.None;
    }

    /// <summary>
    /// Where the block a line starts ends, for its arrow, which is drawn again on every key typed. Only a few thousand
    /// lines are looked through at once: for a block that goes on further (one a quote or bracket left open runs to the
    /// file's end), the end known before is shown while the real one is found away from the screen's thread.
    /// </summary>
    private int ArrowEndOf(int index)
    {
        if (blockEnds.TryGetValue(index, out int end)) return end;
        (DocumentLines lines, IReadOnlyList<int> blocks) = LinesAndBlocks();
        end = NdfFolding.BlockEnd(lines, blocks, index, LinesLookedAtOnce);
        if (end != NdfFolding.FurtherThanLooked)
        {
            blockEnds[index] = end;
            return end;
        }
        FindFarEndLater(index, blocks[index]);
        return blockEndsBefore.GetValueOrDefault(index, -1);
    }

    /// <summary>Finds a long block's end in a snapshot of the text, then draws its arrow again, unless the text changed meanwhile.</summary>
    private async void FindFarEndLater(int index, int startsInside)
    {
        if (shown is null || !endsBeingFound.Add(index)) return;
        int changes = textChanges;
        TextDocument document = shown.Document;
        ITextSource text = document.CreateSnapshot();
        int lineStart = document.GetLineByNumber(index + 1).Offset;
        int linesOn = await Task.Run(() => FarBlockEnd.Find(text, lineStart, startsInside));
        endsBeingFound.Remove(index);
        if (changes != textChanges || shown?.Document != document) return;
        blockEnds[index] = linesOn < 0 ? -1 : index + linesOn;
        markColumn.InvalidateVisual();
    }

    /// <summary>Where the block a line starts ends, or -1; remembered until the text changes.</summary>
    private int BlockEndOf(int index)
    {
        if (blockEnds.TryGetValue(index, out int end)) return end;
        (DocumentLines lines, IReadOnlyList<int> blocks) = LinesAndBlocks();
        end = NdfFolding.BlockEnd(lines, blocks, index);
        blockEnds[index] = end;
        return end;
    }

    /// <summary>A click on an arrow folds its block, or unfolds it; the line clicked stays where it is.</summary>
    internal void ToggleBlock(int index)
    {
        if (folding is null || shown is null) return;
        DocumentLine line = shown.Document.GetLineByNumber(index + 1);
        List<FoldingSection> here = [.. folding.GetFoldingsAt(line.EndOffset)];
        bool wasFolded = here.Any(f => f.IsFolded);
        foreach (FoldingSection fold in here) folding.RemoveFolding(fold);
        if (wasFolded) return;
        int end = BlockEndOf(index);
        if (end > index) Fold(line.EndOffset, shown.Document.GetLineByNumber(end + 1).EndOffset);
    }

    /// <summary>Folds the text from the end of a block's first line to the end of its last, leaving the first line.</summary>
    private void Fold(int start, int end)
    {
        FoldingSection fold = folding!.CreateFolding(start, end);
        fold.Title = "⋯";
        fold.IsFolded = true;
    }

    // ---- Fold all and Unfold all, in the status strip ----

    private System.Windows.Controls.Button? foldAllButton, unfoldAllButton;

    /// <summary>
    /// Fold all and Unfold all, at the left of the status strip under the text. They used to sit at the right of the File
    /// Editor heading, where, with the panes switching between beside and below, they ended up next to the pane
    /// buttons and were mistaken for them. A right-click on them offers the same two.
    /// </summary>
    private FrameworkElement FoldButtons()
    {
        foldAllButton = FoldButton("FoldAllIcon", FoldAll, new Thickness(0, 0, 6, 0));
        unfoldAllButton = FoldButton("UnfoldAllIcon", UnfoldAll, new Thickness(0));
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { foldAllButton, unfoldAllButton },
        };
        buttons.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            Menus.Show(buttons, FoldChoices());
        };
        ShowFoldTips();
        return buttons;
    }

    /// <summary>One of the two buttons: its icon (from the page's resources), drawn in the button's text colour.</summary>
    private System.Windows.Controls.Button FoldButton(string icon, Action action, Thickness margin)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = (System.Windows.Media.Geometry)FindResource(icon),
            StrokeThickness = 1.3, Width = 14, Height = 14, Margin = new Thickness(0, 2, 0, 2),
        };
        path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(System.Windows.Controls.Control.Foreground))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.Button), 1),
        });
        var button = new System.Windows.Controls.Button { Style = (Style)FindResource("IconButton"), Margin = margin, Content = path };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>The buttons' tooltips, in the language in use.</summary>
    private void ShowFoldTips()
    {
        if (foldAllButton is null || unfoldAllButton is null) return;
        foldAllButton.ToolTip = Texts.Get("Editor.FoldAll.Tip");
        unfoldAllButton.ToolTip = Texts.Get("Editor.UnfoldAll.Tip");
    }

    /// <summary>Fold all and Unfold all as menu items, with their keys (the same ones that fold the Rules tab's groups).</summary>
    private List<MenuChoice?> FoldChoices() =>
    [
        new MenuChoice(Texts.Get("Editor.Menu.FoldAll"), FoldAll, folding is not null, "Ctrl+Shift+["),
        new MenuChoice(Texts.Get("Editor.Menu.UnfoldAll"), UnfoldAll, folding is not null, "Ctrl+Shift+]"),
    ];

    /// <summary>"Unfold all".</summary>
    internal void UnfoldAll() => folding?.Clear();

    /// <summary>"Fold all": every block that starts at the top level, which is every descriptor; then back to the top.</summary>
    internal void FoldAll()
    {
        if (folding is null || shown is null) return;
        folding.Clear();
        (DocumentLines lines, IReadOnlyList<int> blocks) = LinesAndBlocks();
        TextDocument document = shown.Document;
        foreach ((int first, int last) in NdfFolding.TopLevelBlocks(lines, blocks))
            Fold(document.GetLineByNumber(first + 1).EndOffset, document.GetLineByNumber(last + 1).EndOffset);
        Text.ScrollToVerticalOffset(0);
    }

    /// <summary>Unfolds whatever folded block hides any part of the text from <paramref name="start"/> to <paramref name="end"/> (the HTML app's <c>cmShow</c>).</summary>
    private void Unfold(int start, int end)
    {
        if (folding is null) return;
        foreach (FoldingSection fold in folding.AllFoldings.Where(f => f.IsFolded && f.StartOffset < Math.Max(end, start + 1) && f.EndOffset > start).ToList())
            folding.RemoveFolding(fold);
    }

    /// <summary>
    /// Backspace just after a folded block, or Delete just before it, unfolds it instead of deleting a character of it
    /// unseen. Returns whether it did.
    /// </summary>
    private bool UnfoldInsteadOfDeleting(Key key)
    {
        if (folding is null || Text.SelectionLength > 0 || key is not (Key.Back or Key.Delete)) return false;
        int caret = Text.CaretOffset;
        FoldingSection? fold = folding.AllFoldings.FirstOrDefault(f => f.IsFolded && (key == Key.Back ? f.EndOffset == caret : f.StartOffset == caret));
        if (fold is null) return false;
        folding.RemoveFolding(fold);
        return true;
    }

    // ---- the folded block's tooltip ----

    private readonly System.Windows.Controls.ToolTip foldTip = new() { Placement = PlacementMode.Mouse };

    private void FoldedBlockHovered(object sender, MouseEventArgs e)
    {
        TextView view = Text.TextArea.TextView;
        Point point = e.GetPosition(view);
        VisualLine? line = view.GetVisualLineFromVisualTop(point.Y + view.VerticalOffset);
        if (line is null) return;
        int column = line.GetVisualColumnFloor(new Point(point.X + view.HorizontalOffset, point.Y + view.VerticalOffset - line.VisualTop));
        if (line.Elements.FirstOrDefault(el => el.VisualColumn <= column && column < el.VisualColumn + el.VisualLength) is not FoldedBlocks.FoldedElement) return;
        // the text is set here, not when the tooltip is made, so it follows the language in use
        foldTip.Content = Texts.Get("Editor.Fold.Tip");
        foldTip.PlacementTarget = view;
        foldTip.IsOpen = true;
    }

    // ---- right-click menus ----

    /// <summary>
    /// The right-click menus: on the line numbers and arrows of the file, Fold all, Unfold all and what can be done with
    /// the file; anywhere in a read-only view, also copying and folding the line clicked. The file's text gets Cut, Copy
    /// and Paste, as a browser offers in a text box, with Undo and Redo (there are no buttons for them).
    /// </summary>
    private void EditorRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (shown is null) return;
        TextView view = Text.TextArea.TextView;
        Point point = e.GetPosition(view);
        bool onMargin = point.X < 0;
        if (shown.IsFile && !onMargin)
        {
            e.Handled = true;
            Menus.Show(Text,
            [
                new MenuChoice(Texts.Get("Common.Menu.Undo"), () => Text.Undo(), Text.CanUndo, "Ctrl+Z"),
                new MenuChoice(Texts.Get("Common.Menu.Redo"), () => Text.Redo(), Text.CanRedo, "Ctrl+Y"),
                Menus.Line,
                new MenuChoice(Texts.Get("Common.Menu.Cut"), () => ApplicationCommands.Cut.Execute(null, Text.TextArea), Text.SelectionLength > 0, "Ctrl+X"),
                new MenuChoice(Texts.Get("Common.Menu.Copy"), () => ApplicationCommands.Copy.Execute(null, Text.TextArea), Text.SelectionLength > 0, "Ctrl+C"),
                new MenuChoice(Texts.Get("Common.Menu.Paste"), () => ApplicationCommands.Paste.Execute(null, Text.TextArea), Clipboard.ContainsText(), "Ctrl+V"),
                Menus.Line,
                new MenuChoice(Texts.Get("Editor.Menu.SelectAll"), () => Text.SelectAll(), Key: "Ctrl+A"),
                Menus.Line,
                .. FoldChoices(),
            ]);
            return;
        }
        e.Handled = true;
        List<MenuChoice?> items = [];
        if (shown.IsExport)
        {
            DocumentLine? line = view.GetDocumentLineByVisualTop(Math.Max(0, point.Y + view.VerticalOffset));
            int index = line is null ? -1 : line.LineNumber - 1;
            bool isGap = index >= 0 && shown.Changes is ChangedLines changes && changes.Marks[index] == LineMark.Gap;
            string lineText = line is null ? "" : shown.Document.GetText(line);
            if (Text.SelectionLength > 0) items.Add(new MenuChoice(Texts.Get("Editor.Menu.CopySelected"), () => Copy(Text.SelectedText)));
            items.Add(new MenuChoice(Texts.Get("Editor.CopyThisLine"), () => Copy(lineText), line is not null && !isGap));
            items.Add(Menus.Line);
            FoldArrow arrow = index >= 0 ? ArrowOf(index) : FoldArrow.None;
            if (arrow != FoldArrow.None) items.Add(new MenuChoice(arrow == FoldArrow.Shut ? Texts.Get("Editor.Menu.UnfoldBlock") : Texts.Get("Editor.Menu.FoldBlock"), () => ToggleBlock(index)));
        }
        items.AddRange(FoldChoices());
        items.Add(Menus.Line);
        items.Add(new MenuChoice(shown.IsExport ? Texts.Get("Editor.ShowSource") : Texts.Get("Editor.ShowExport"), () => _ = ShowExportOrSource(), ExportButton.IsEnabled));
        items.Add(new MenuChoice(Texts.Get("Editor.ChangesOnly"), () => _ = ChangesFromMenu(), ChangesButton.IsEnabled));
        items.Add(new MenuChoice(Texts.Get("Editor.SaveAs"), () => SaveClicked(this, e)));
        Menus.Show(Text, items);
    }

    private async Task ChangesFromMenu()
    {
        await ShowChangesOnly();
        Show();
    }

    private static void Copy(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { } // the clipboard is busy with another program
    }
}
