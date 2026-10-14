using System.Windows;
using System.Windows.Media;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// Colours NDF text in an AvalonEdit editor with the HTML app's rules (see <see cref="NdfColouring"/>), in the theme's
/// "s-" colours. It keeps, for every line, the block the line starts inside, and works it out again after each change
/// only as far as it changes, so a keystroke in a long file costs little. Only the lines on screen are coloured.
/// </summary>
public sealed class NdfColourizer : DocumentColorizingTransformer
{
    private readonly TextDocument document;

    /// <summary>The block each line starts inside, by line number from 0, and one more for after the last line.</summary>
    private readonly List<int> blocks = [];

    /// <summary>The editor's view of the text, which is told to draw again the lines whose colours a change moves.</summary>
    private TextView? view;

    /// <summary>Lines coloured before, by their block and text, so scrolling back over them costs nothing.</summary>
    private readonly Dictionary<(int Block, string Text), List<ColouredPart>> coloured = [];

    /// <summary>Starts colouring a document, and follows its changes.</summary>
    public NdfColourizer(TextDocument document)
    {
        this.document = document;
        Recount();
        document.Changed += (_, e) => Follow(e);
    }

    /// <summary>
    /// Colours a read-only text whose lines come from more than one text ("Changes only"), so each line's block is given
    /// rather than worked out from the lines above it.
    /// </summary>
    public NdfColourizer(TextDocument document, IEnumerable<int> givenBlocks)
    {
        this.document = document;
        blocks.AddRange(givenBlocks);
    }

    /// <summary>The block each line starts inside (the folding reads them too).</summary>
    public IReadOnlyList<int> Blocks => blocks;

    /// <summary>The lines' texts, from 0.</summary>
    public string LineText(int index) => document.GetText(document.GetLineByNumber(index + 1));

    /// <summary>Works out every line's block from the start (the HTML app's <c>edStates</c>).</summary>
    private void Recount()
    {
        blocks.Clear();
        blocks.AddRange(NdfColouring.BlocksOfLines([.. document.Lines.Select(line => document.GetText(line))]));
    }

    /// <summary>
    /// After a change, the lines from the first one changed onwards get their blocks again, until a line starts inside
    /// the same block as before and nothing further down can change (the HTML app's <c>cmSyncLines</c>). Every line
    /// from the first changed to the last whose block changed is then drawn again: the editor itself would only draw
    /// again the lines typed in, and a quote or brace typed or taken away recolours the lines below it too.
    /// </summary>
    private void Follow(DocumentChangeEventArgs change)
    {
        int lineCount = document.LineCount;
        int first = document.GetLineByOffset(Math.Min(change.Offset, document.TextLength)).LineNumber - 1;
        int lastOfChange = document.GetLineByOffset(Math.Min(change.Offset + change.InsertionLength, document.TextLength)).LineNumber - 1;
        // the list keeps one block per line: lines taken out or put in by the change are taken out or put in here
        int difference = lineCount + 1 - blocks.Count;
        if (difference > 0) blocks.InsertRange(first + 1, Enumerable.Repeat(-1, difference));
        else if (difference < 0) blocks.RemoveRange(first + 1, -difference);

        int block = blocks[first], last = first;
        for (int i = first; i < lineCount; i++)
        {
            last = i;
            block = NdfColouring.LineEndsInside(LineText(i), block);
            if (i >= lastOfChange && blocks[i + 1] == block) break;
            blocks[i + 1] = block;
        }
        if (view is null) return;
        int start = document.GetLineByNumber(first + 1).Offset;
        int end = document.GetLineByNumber(Math.Min(last + 2, lineCount)).EndOffset;
        view.Redraw(start, end - start, System.Windows.Threading.DispatcherPriority.Normal);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        // only the text this colours, never another the editor may have shown before
        if (CurrentContext.Document != document) return;
        view = CurrentContext.TextView;
        int index = line.LineNumber - 1;
        if (index >= blocks.Count) return;
        string text = document.GetText(line);
        var key = (blocks[index], text);
        if (!coloured.TryGetValue(key, out List<ColouredPart>? parts))
        {
            if (coloured.Count > 20000) coloured.Clear();
            parts = NdfColouring.ColourLine(text, blocks[index]).Parts;
            coloured[key] = parts;
        }
        foreach (ColouredPart part in parts)
        {
            if (Application.Current.TryFindResource("s-" + part.Colour) is not Brush brush) continue;
            ChangeLinePart(line.Offset + part.Start, line.Offset + part.End, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
