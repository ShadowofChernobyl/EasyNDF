using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Editor;

/// <summary>
/// Finds where a long block ends in a snapshot of a text, away from the screen's thread. While a quote or bracket is
/// left open, a block can run to the end of a file of 700,000 lines; looking for its end on every key typed held each
/// key up for half a second.
/// </summary>
public static class FarBlockEnd
{
    /// <summary>
    /// Where the block starting on the line at <paramref name="lineStart"/> ends, counted in lines from that line; -1 when
    /// it starts none or never closes.
    /// </summary>
    /// <param name="text">A snapshot of the text, which does not change while it is read.</param>
    /// <param name="lineStart">Where the line starts in the text.</param>
    /// <param name="startsInside">The block the line starts inside (see <see cref="NdfColouring.BlocksOfLines"/>).</param>
    public static int Find(ITextSource text, int lineStart, int startsInside)
    {
        var lines = new SnapshotLines(text, lineStart);
        return NdfFolding.BlockEnd(lines, new FollowedBlocks(lines, startsInside), 0);
    }

    /// <summary>The lines of a snapshot from one line to the end, each made as it is read.</summary>
    private sealed class SnapshotLines : IReadOnlyList<string>
    {
        private readonly ITextSource text;
        private readonly List<int> starts = [];

        public SnapshotLines(ITextSource text, int from)
        {
            this.text = text;
            starts.Add(from);
            for (int end = text.IndexOf('\n', from, text.TextLength - from); end >= 0; end = text.IndexOf('\n', end + 1, text.TextLength - end - 1))
                starts.Add(end + 1);
        }

        public string this[int index]
        {
            get
            {
                int start = starts[index];
                int end = index + 1 < starts.Count ? starts[index + 1] - 1 : text.TextLength;
                return text.GetText(start, end - start);
            }
        }

        public int Count => starts.Count;

        public IEnumerator<string> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>The block each line starts inside, worked out line after line from the first's as they are read.</summary>
    private sealed class FollowedBlocks(IReadOnlyList<string> lines, int first) : IReadOnlyList<int>
    {
        private readonly List<int> blocks = [first];

        public int this[int index]
        {
            get
            {
                while (blocks.Count <= index) blocks.Add(NdfColouring.LineEndsInside(lines[blocks.Count - 1], blocks[^1]));
                return blocks[index];
            }
        }

        public int Count => lines.Count + 1;

        public IEnumerator<int> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
