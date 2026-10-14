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

    private void ForgetBlockEnds(object? sender, DocumentChangeEventArgs e) => blockEnds.Clear();

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
        return BlockEndOf(index) > index ? FoldArrow.Open : FoldArrow.None;
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

    private void FoldAllClicked(object sender, RoutedEventArgs e) => FoldAll();

    private void UnfoldAllClicked(object sender, RoutedEventArgs e) => UnfoldAll();

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

    private readonly System.Windows.Controls.ToolTip foldTip = new() { Content = "Folded. Click to unfold", Placement = PlacementMode.Mouse };

    private void FoldedBlockHovered(object sender, MouseEventArgs e)
    {
        TextView view = Text.TextArea.TextView;
        Point point = e.GetPosition(view);
        VisualLine? line = view.GetVisualLineFromVisualTop(point.Y + view.VerticalOffset);
        if (line is null) return;
        int column = line.GetVisualColumnFloor(new Point(point.X + view.HorizontalOffset, point.Y + view.VerticalOffset - line.VisualTop));
        if (line.Elements.FirstOrDefault(el => el.VisualColumn <= column && column < el.VisualColumn + el.VisualLength) is not FoldedBlocks.FoldedElement) return;
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
                new MenuChoice("Undo", () => Text.Undo(), Text.CanUndo, "Ctrl+Z"),
                new MenuChoice("Redo", () => Text.Redo(), Text.CanRedo, "Ctrl+Y"),
                Menus.Line,
                new MenuChoice("Cut", () => ApplicationCommands.Cut.Execute(null, Text.TextArea), Text.SelectionLength > 0, "Ctrl+X"),
                new MenuChoice("Copy", () => ApplicationCommands.Copy.Execute(null, Text.TextArea), Text.SelectionLength > 0, "Ctrl+C"),
                new MenuChoice("Paste", () => ApplicationCommands.Paste.Execute(null, Text.TextArea), Clipboard.ContainsText(), "Ctrl+V"),
                Menus.Line,
                new MenuChoice("Select all", () => Text.SelectAll(), Key: "Ctrl+A"),
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
            if (Text.SelectionLength > 0) items.Add(new MenuChoice("Copy selected text", () => Copy(Text.SelectedText)));
            items.Add(new MenuChoice("Copy this line", () => Copy(lineText), line is not null && !isGap));
            items.Add(Menus.Line);
            FoldArrow arrow = index >= 0 ? ArrowOf(index) : FoldArrow.None;
            if (arrow != FoldArrow.None) items.Add(new MenuChoice(arrow == FoldArrow.Shut ? "Unfold this block" : "Fold this block", () => ToggleBlock(index)));
        }
        items.Add(new MenuChoice("Fold all", FoldAll, folding is not null));
        items.Add(new MenuChoice("Unfold all", UnfoldAll, folding is not null));
        items.Add(Menus.Line);
        items.Add(new MenuChoice(shown.IsExport ? "Show source" : "Show export", () => _ = ShowExportOrSource(), ExportButton.IsEnabled));
        items.Add(new MenuChoice("Changes only", () => _ = ChangesFromMenu(), ChangesButton.IsEnabled));
        items.Add(new MenuChoice("Save as...", () => SaveClicked(this, e)));
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
