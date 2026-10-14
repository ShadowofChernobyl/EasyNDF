using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace EasyNdf.App.Editor;

/// <summary>
/// The File Editor's editor for one text, in a sunken box (the HTML app's <c>rcmMount</c>): NDF colours, line numbers,
/// folding, Tab typing four spaces, undo, and the File Editor's text size, which Ctrl and the wheel change. The Add
/// Descriptor rule's text is written in it. The rule window is drawn again after many changes; the same text gets
/// back its undo steps, its folds and its selection.
/// </summary>
public sealed class NdfTextBox : ContentControl
{
    private readonly TextEditor editor = new();
    private readonly LineHeight lineHeight = new();
    private readonly MarkColumn markColumn = new();
    private readonly FoldedBlocks foldedBlocks = new();
    private readonly Dictionary<int, int> blockEnds = [];
    private readonly NdfColourizer colourizer;
    private readonly FoldingManager folding;
    private readonly JsonObject owner;

    /// <summary>The status strip under the text; none for the Theme Designer's sample.</summary>
    private StatusStrip? strip;

    /// <summary>Whether this is the Theme Designer's sample (the HTML app's <c>#pvTa</c>): 12 pixels always, and no folding.</summary>
    private readonly bool isSample;

    /// <summary>What a text kept between two drawings of the window: its document (with its undo steps), folds and selection.</summary>
    private sealed record Kept(JsonObject Owner, TextDocument Document, List<(int Start, int End)> Folds, int SelectionStart, int SelectionLength, bool HadFocus);

    /// <summary>The text last shown, kept for when the window is drawn again for the same rule.</summary>
    private static Kept? kept;

    /// <summary>Raised when the text is changed.</summary>
    public event Action<string>? TextChanged;

    /// <summary>The text.</summary>
    public string Text => editor.Text;

    /// <summary>The editor itself.</summary>
    internal TextEditor InnerEditor => editor;

    /// <summary>The status strip under the text; none for the Theme Designer's sample.</summary>
    internal StatusStrip? Strip => strip;

    /// <summary>Shows a text, owned by <paramref name="owner"/> (the rule's settings): the same owner gets back the same document.</summary>
    /// <param name="text">The text.</param>
    /// <param name="owner">What the text belongs to.</param>
    /// <param name="isSample">True for the Theme Designer's sample, which keeps 12 pixels and does not fold.</param>
    public NdfTextBox(string text, JsonObject owner, bool isSample = false)
    {
        this.owner = owner;
        this.isSample = isSample;
        Kept? before = kept is { } k && ReferenceEquals(k.Owner, owner) && k.Document.Text == text ? k : null;
        TextDocument document = before?.Document ?? new TextDocument(text);
        Setup();
        editor.Document = document;
        colourizer = new NdfColourizer(document);
        editor.TextArea.TextView.LineTransformers.Add(colourizer);
        folding = FoldingManager.Install(editor.TextArea);
        // AvalonEdit's own fold margin is taken off, and its marker is drawn by FoldedBlocks, as in the File Editor
        foreach (FoldingMargin margin in editor.TextArea.LeftMargins.OfType<FoldingMargin>().ToList()) editor.TextArea.LeftMargins.Remove(margin);
        editor.TextArea.TextView.ElementGenerators.Insert(0, foldedBlocks);
        foldedBlocks.Folding = folding;
        document.Changed += (_, _) => blockEnds.Clear();
        if (before is not null)
        {
            foreach ((int start, int end) in before.Folds)
                if (end <= document.TextLength) Fold(start, end);
            editor.Select(Math.Min(before.SelectionStart, document.TextLength), Math.Min(before.SelectionLength, document.TextLength - Math.Min(before.SelectionStart, document.TextLength)));
            if (before.HadFocus) Loaded += (_, _) => editor.TextArea.Focus();
        }
        editor.TextChanged += (_, _) => TextChanged?.Invoke(editor.Text);
        Loaded += (_, _) => Theming.ThemeManager.Applied += RedrawInTheme;
        Loaded += (_, _) => { if (!isSample) TextZoom.Changed += ShowSize; };
        Unloaded += (_, _) =>
        {
            Theming.ThemeManager.Applied -= RedrawInTheme;
            TextZoom.Changed -= ShowSize;
            if (!isSample) Keep();
        };
        // the status strip under the text, as in the File Editor; the Theme Designer's sample has none
        var inside = new DockPanel();
        if (!isSample)
        {
            strip = new StatusStrip(editor);
            DockPanel.SetDock(strip, Dock.Bottom);
            inside.Children.Add(strip);
        }
        inside.Children.Add(editor);
        var box = new Bevel { Child = inside };
        box.SetResourceReference(Bevel.TopLeftProperty, "lo");
        box.SetResourceReference(Bevel.BottomRightProperty, "hi");
        box.SetResourceReference(Bevel.BackgroundProperty, "well");
        Content = box;
    }

    /// <summary>The editor as the File Editor has it.</summary>
    private void Setup()
    {
        editor.SetResourceReference(Control.FontFamilyProperty, "mono");
        editor.SetResourceReference(Control.BackgroundProperty, "well");
        editor.SetResourceReference(Control.ForegroundProperty, "ink");
        editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
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
        editor.TextArea.Caret.CaretBrush = (Brush)Application.Current.Resources["ink"];
        // the line numbers, the folding arrows, then 4 pixels before the text
        editor.TextArea.LeftMargins.Insert(0, new LineNumbers());
        editor.TextArea.LeftMargins.Insert(1, markColumn);
        editor.TextArea.LeftMargins.Insert(2, new Border { Width = 4 });
        editor.TextArea.TextView.ElementGenerators.Add(lineHeight);
        lineHeight.KeepGuessesRight(editor.TextArea.TextView);
        editor.TextArea.TextView.BackgroundRenderers.Add(new IndentGuides());
        _ = new AutoPairs(editor.TextArea);
        editor.PreviewKeyDown += KeyPressed;
        if (isSample)
        {
            SetTextSize(12, remember: false);
            return;
        }
        markColumn.Arrows = ArrowOf;
        markColumn.ArrowClicked += ToggleBlock;
        editor.PreviewMouseWheel += WheelTurned;
        SetTextSize(TextZoom.Saved, remember: false);
    }

    /// <summary>A new theme: the text's colours, the line numbers and the arrows are drawn again in it.</summary>
    private void RedrawInTheme()
    {
        editor.TextArea.Caret.CaretBrush = (Brush)Application.Current.Resources["ink"];
        editor.TextArea.TextView.Redraw();
        foreach (UIElement margin in editor.TextArea.LeftMargins) margin.InvalidateVisual();
    }

    /// <summary>Puts another text in the box, as one step that Undo takes back.</summary>
    public void Replace(string text) => editor.Document.Text = text;

    /// <summary>Keeps the text, its folds and its selection for when the window is drawn again.</summary>
    private void Keep() =>
        kept = new Kept(owner, editor.Document, [.. folding.AllFoldings.Where(f => f.IsFolded).Select(f => (f.StartOffset, f.EndOffset))],
            editor.SelectionStart, editor.SelectionLength, editor.TextArea.IsKeyboardFocusWithin);

    // ---- folding, as in the File Editor ----

    /// <summary>▸ beside a line whose block is folded, ▾ beside one that starts a block that closes.</summary>
    private FoldArrow ArrowOf(int index)
    {
        TextDocument document = editor.Document;
        if (index >= document.LineCount) return FoldArrow.None;
        DocumentLine line = document.GetLineByNumber(index + 1);
        if (folding.GetFoldingsAt(line.EndOffset).Any(f => f.IsFolded)) return FoldArrow.Shut;
        return BlockEndOf(index) > index ? FoldArrow.Open : FoldArrow.None;
    }

    private int BlockEndOf(int index)
    {
        if (blockEnds.TryGetValue(index, out int end)) return end;
        end = NdfFolding.BlockEnd(new DocumentLines(editor.Document), colourizer.Blocks, index);
        blockEnds[index] = end;
        return end;
    }

    private void ToggleBlock(int index)
    {
        DocumentLine line = editor.Document.GetLineByNumber(index + 1);
        List<FoldingSection> here = [.. folding.GetFoldingsAt(line.EndOffset)];
        bool wasFolded = here.Any(f => f.IsFolded);
        foreach (FoldingSection fold in here) folding.RemoveFolding(fold);
        if (wasFolded) return;
        int end = BlockEndOf(index);
        if (end > index) Fold(line.EndOffset, editor.Document.GetLineByNumber(end + 1).EndOffset);
    }

    private void Fold(int start, int end)
    {
        FoldingSection fold = folding.CreateFolding(start, end);
        fold.Title = "⋯";
        fold.IsFolded = true;
    }

    // ---- keys and text size ----

    /// <summary>Backspace just after a folded block, or Delete just before it, unfolds it; Ctrl+plus, minus and 0 change the text size.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Back or Key.Delete && editor.SelectionLength == 0)
        {
            int caret = editor.CaretOffset;
            FoldingSection? fold = folding.AllFoldings.FirstOrDefault(f => f.IsFolded && (e.Key == Key.Back ? f.EndOffset == caret : f.StartOffset == caret));
            if (fold is not null)
            {
                folding.RemoveFolding(fold);
                e.Handled = true;
                return;
            }
        }
        if (isSample || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        if (e.Key is Key.OemPlus or Key.Add) { e.Handled = true; SetTextSize(editor.FontSize + 1, remember: true); }
        else if (e.Key is Key.OemMinus or Key.Subtract) { e.Handled = true; SetTextSize(editor.FontSize - 1, remember: true); }
        else if (e.Key is Key.D0 or Key.NumPad0) { e.Handled = true; SetTextSize(TextZoom.UsualSize, remember: true); }
    }

    private void WheelTurned(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        SetTextSize(editor.FontSize + (e.Delta > 0 ? 1 : -1), remember: true);
    }

    /// <summary>
    /// The text size, from 7 to 36 pixels, on lines of round(size × 17 / 12). It is the File Editor's: a size to
    /// remember is saved and given to the File Editor and the status strip too.
    /// </summary>
    private void SetTextSize(double size, bool remember)
    {
        if (remember) TextZoom.Set(size); // comes back to ShowSize through TextZoom.Changed
        else ShowSize(size);
    }

    /// <summary>Shows the text at a size.</summary>
    private void ShowSize(double size)
    {
        double textSize = TextZoom.Fit(size);
        editor.FontSize = textSize;
        lineHeight.Height = markColumn.LineHeight = Math.Round(textSize * 17 / 12, MidpointRounding.AwayFromZero);
        editor.TextArea.TextView.Redraw();
        strip?.ShowSize(textSize);
    }
}
