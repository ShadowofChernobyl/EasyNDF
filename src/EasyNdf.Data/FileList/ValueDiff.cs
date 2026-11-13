using System.Text;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.FileList;

/// <summary>What a piece of a value is.</summary>
public enum ValuePieceKind
{
    /// <summary>Text the edit left as it was.</summary>
    Same,
    /// <summary>Text the edit changed.</summary>
    Changed,
    /// <summary>A mark for unchanged text left out.</summary>
    LeftOut,
}

/// <summary>A piece of a value, as Details shows it.</summary>
/// <param name="Text">The text.</param>
/// <param name="Kind">What it is.</param>
public readonly record struct ValuePiece(string Text, ValuePieceKind Kind);

/// <summary>What an edit did to a value.</summary>
public enum ValueChangeShape
{
    /// <summary>Text was replaced: there is a before and an after.</summary>
    Changed,
    /// <summary>Text was only added, such as an entry in a list.</summary>
    Added,
    /// <summary>Text was only removed.</summary>
    Removed,
    /// <summary>The words are the same; only the spaces and line breaks between them differ.</summary>
    SpacingOnly,
}

/// <summary>An edit's value before and after, in pieces.</summary>
/// <param name="Shape">What the edit did.</param>
/// <param name="Before">The value before, in pieces.</param>
/// <param name="After">The value after, in pieces.</param>
public sealed record ValueChange(ValueChangeShape Shape, IReadOnlyList<ValuePiece> Before, IReadOnlyList<ValuePiece> After);

/// <summary>
/// Finds what an edit changed in a value, word by word, so Details can show that part in full and leave out long
/// stretches that stayed the same. Spacing never counts: a list written on many lines and the same list on one are the
/// same words.
/// </summary>
public static class ValueDiff
{
    /// <summary>A value this short, or shorter, shows whole.</summary>
    private const int ShownWholeUpTo = 110;

    /// <summary>How much of what stayed the same shows on each side of what changed in a longer value.</summary>
    private const int ContextShown = 40;

    /// <summary>How much shows of a longer value in which nothing changed but its spacing.</summary>
    private const int UnchangedShown = 120;

    /// <summary>How many words past the context shown a comma is looked for, to start or stop on.</summary>
    private const int ReachForAComma = 4;

    /// <summary>
    /// Tuned for speed: the most cells of the table that finds the longest common words; past it, all of what lies
    /// between the common start and end counts as changed. Keep it that way when tidying.
    /// </summary>
    private const long MostCells = 250_000;

    private readonly record struct Word(string Text, bool SpaceBefore)
    {
        public int Length => Text.Length + (SpaceBefore ? 1 : 0);
    }

    /// <summary>What an edit did to a value.</summary>
    public static ValueChange Of(string before, string after)
    {
        List<Word> old = WordsOf(before), now = WordsOf(after);
        if (old.Count == 0) return new ValueChange(ValueChangeShape.Added, [], PiecesOf(now, AllChanged(now)));
        if (old.Select(word => word.Text).SequenceEqual(now.Select(word => word.Text)))
            return new ValueChange(ValueChangeShape.SpacingOnly, PiecesOf(old, new bool[old.Count]), PiecesOf(now, new bool[now.Count]));
        (bool[] oldChanged, bool[] nowChanged) = ChangedWords(old, now);
        if (!oldChanged.Contains(true))
        {
            Slide(now, nowChanged);
            return new ValueChange(ValueChangeShape.Added, [], PiecesOf(now, nowChanged));
        }
        if (!nowChanged.Contains(true))
        {
            Slide(old, oldChanged);
            return new ValueChange(ValueChangeShape.Removed, PiecesOf(old, oldChanged), []);
        }
        return new ValueChange(ValueChangeShape.Changed, PiecesOf(old, oldChanged), PiecesOf(now, nowChanged));
    }

    private static bool[] AllChanged(List<Word> words)
    {
        var changed = new bool[words.Count];
        Array.Fill(changed, true);
        return changed;
    }

    /// <summary>A value's words: a name, a number or a path is one word, and any other mark is a word of its own.</summary>
    private static List<Word> WordsOf(string text)
    {
        var words = new List<Word>();
        bool space = false;
        for (int at = 0; at < text.Length;)
        {
            char c = text[at];
            if (char.IsWhiteSpace(c)) { space = true; at++; continue; }
            int end = at + 1;
            if (InAWord(c))
                while (end < text.Length && InAWord(text[end])) end++;
            words.Add(new Word(text[at..end], space));
            space = false;
            at = end;
        }
        return words;
    }

    private static bool InAWord(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '~' or '.' or '/' or ':' or '#' or '-';

    /// <summary>Which words of each side changed: the common start and end first, then the longest common words of what is left.</summary>
    private static (bool[] Old, bool[] Now) ChangedWords(List<Word> old, List<Word> now)
    {
        int oldCount = old.Count, nowCount = now.Count, start = 0, end = 0;
        while (start < oldCount && start < nowCount && old[start].Text == now[start].Text) start++;
        while (end < oldCount - start && end < nowCount - start && old[oldCount - 1 - end].Text == now[nowCount - 1 - end].Text) end++;
        var oldChanged = new bool[oldCount];
        var nowChanged = new bool[nowCount];
        int width = oldCount - start - end, height = nowCount - start - end;
        if (width == 0 || height == 0 || (long)width * height > MostCells)
        {
            Array.Fill(oldChanged, true, start, width);
            Array.Fill(nowChanged, true, start, height);
            return (oldChanged, nowChanged);
        }
        // what fits in the table has at most 500 words on one side, so a count fits in 16 bits
        int stride = height + 1;
        var longest = new ushort[(width + 1) * stride];
        for (int i = width - 1; i >= 0; i--)
            for (int j = height - 1; j >= 0; j--)
                longest[i * stride + j] = old[start + i].Text == now[start + j].Text
                    ? (ushort)(longest[(i + 1) * stride + j + 1] + 1)
                    : Math.Max(longest[(i + 1) * stride + j], longest[i * stride + j + 1]);
        int x = 0, y = 0;
        while (x < width && y < height)
        {
            if (old[start + x].Text == now[start + y].Text) { x++; y++; }
            else if (longest[(x + 1) * stride + y] >= longest[x * stride + y + 1]) oldChanged[start + x++] = true;
            else nowChanged[start + y++] = true;
        }
        while (x < width) oldChanged[start + x++] = true;
        while (y < height) nowChanged[start + y++] = true;
        return (oldChanged, nowChanged);
    }

    /// <summary>
    /// An entry added to or taken from a list reads from its first word to its comma, not from the middle of two
    /// neighbours: where the changed words could sit in more than one place, they go where they read as a whole.
    /// </summary>
    private static void Slide(List<Word> words, bool[] changed)
    {
        int start = Array.IndexOf(changed, true), end = Array.LastIndexOf(changed, true) + 1;
        if (start < 0 || Array.IndexOf(changed, false, start, end - start) >= 0) return;
        int length = end - start, first = start, last = start;
        while (first > 0 && words[first - 1].Text == words[first + length - 1].Text) first--;
        while (last + length < words.Count && words[last].Text == words[last + length].Text) last++;
        int best = start, bestScore = Score(words, start, length);
        for (int place = first; place <= last; place++)
        {
            int score = Score(words, place, length);
            if (score > bestScore) (best, bestScore) = (place, score);
        }
        Array.Clear(changed);
        Array.Fill(changed, true, best, length);
    }

    private static int Score(List<Word> words, int place, int length)
    {
        string before = place > 0 ? words[place - 1].Text : ",";
        string firstWord = words[place].Text, lastWord = words[place + length - 1].Text;
        return (before is "," or "(" or "[" or "{" ? 2 : 0) + (lastWord == "," ? 2 : 0) + (firstWord is "," or ")" or "]" ? 0 : 1);
    }

    /// <summary>A side's pieces: what changed in full, and of a long value only a little of what stayed the same around it.</summary>
    private static List<ValuePiece> PiecesOf(List<Word> words, bool[] changed)
    {
        var pieces = new List<ValuePiece>();
        bool showsWhole = words.Sum(word => word.Length) <= ShownWholeUpTo;
        for (int at = 0, end; at < words.Count; at = end)
        {
            bool isChanged = changed[at];
            end = at;
            while (end < words.Count && changed[end] == isChanged) end++;
            if (isChanged) Add(pieces, words, at, end, ValuePieceKind.Changed);
            else if (showsWhole) Add(pieces, words, at, end, ValuePieceKind.Same);
            else AddLongSame(pieces, words, at, end);
        }
        return pieces;
    }

    /// <summary>A stretch that stayed the same in a long value: what is nearest to what changed, and a mark for the rest.</summary>
    private static void AddLongSame(List<ValuePiece> pieces, List<Word> words, int from, int to)
    {
        bool changeBefore = from > 0, changeAfter = to < words.Count;
        if (!changeBefore && !changeAfter)
        {
            int headEnd = HeadEnd(words, from, to, UnchangedShown);
            Add(pieces, words, from, headEnd, ValuePieceKind.Same);
            if (headEnd < to) AddMark(pieces);
        }
        else if (!changeBefore)
        {
            int tailStart = TailStart(words, from, to);
            if (tailStart > from) AddMark(pieces);
            Add(pieces, words, tailStart, to, ValuePieceKind.Same);
        }
        else if (!changeAfter)
        {
            int headEnd = HeadEnd(words, from, to, ContextShown);
            Add(pieces, words, from, headEnd, ValuePieceKind.Same);
            if (headEnd < to) AddMark(pieces);
        }
        else
        {
            int headEnd = HeadEnd(words, from, to, ContextShown), tailStart = TailStart(words, from, to);
            if (headEnd >= tailStart) Add(pieces, words, from, to, ValuePieceKind.Same);
            else
            {
                Add(pieces, words, from, headEnd, ValuePieceKind.Same);
                AddMark(pieces);
                Add(pieces, words, tailStart, to, ValuePieceKind.Same);
            }
        }
    }

    /// <summary>
    /// Where the words from a place on reach as much as a size, with at least one word; a comma just after that ends them,
    /// so they stop where the value's own items do.
    /// </summary>
    private static int HeadEnd(List<Word> words, int from, int to, int size)
    {
        int at = from, used = 0;
        while (at < to)
        {
            used += words[at].Length;
            if (used > size && at > from) break;
            at++;
        }
        for (int near = at; near < to && near < at + ReachForAComma; near++)
            if (words[near].Text == ",") return near + 1;
        return at;
    }

    /// <summary>
    /// Where the words up to a place start, to reach as much as the context shown, with at least one word; a comma just
    /// after that start moves it past the comma, so they start where the value's own items do.
    /// </summary>
    private static int TailStart(List<Word> words, int from, int to)
    {
        int at = to, used = 0;
        while (at > from)
        {
            used += words[at - 1].Length;
            if (used > ContextShown && at < to) break;
            at--;
        }
        if (at == from) return at;
        for (int near = at; near < to - 1 && near < at + ReachForAComma; near++)
            if (words[near].Text == ",") return near + 1;
        return at;
    }

    /// <summary>Adds words as a piece; the marks the edit log left in a long value become marks here too.</summary>
    private static void Add(List<ValuePiece> pieces, List<Word> words, int from, int to, ValuePieceKind kind)
    {
        var text = new StringBuilder();
        for (int at = from; at < to; at++)
        {
            Word word = words[at];
            if (word.Text == EditLogEntry.LeftOutMark)
            {
                Flush();
                AddMark(pieces);
                continue;
            }
            if (word.SpaceBefore)
            {
                if (text.Length > 0) text.Append(' ');
                else if (pieces.Count > 0 && pieces[^1].Kind != ValuePieceKind.LeftOut) pieces[^1] = pieces[^1] with { Text = pieces[^1].Text + " " };
            }
            text.Append(word.Text);
        }
        Flush();

        void Flush()
        {
            if (text.Length > 0) pieces.Add(new ValuePiece(text.ToString(), kind));
            text.Clear();
        }
    }

    private static void AddMark(List<ValuePiece> pieces)
    {
        if (pieces.Count == 0 || pieces[^1].Kind != ValuePieceKind.LeftOut) pieces.Add(new ValuePiece(EditLogEntry.LeftOutMark, ValuePieceKind.LeftOut));
    }
}
