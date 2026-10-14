using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Editing;

/// <summary>
/// A place where two texts differ: lines <see cref="FromA"/> up to (not including) <see cref="ToA"/> of the first text
/// stand where lines <see cref="FromB"/> up to <see cref="ToB"/> of the second do. Either range may be empty.
/// </summary>
public sealed record LineDifference(int FromA, int ToA, int FromB, int ToB);

/// <summary>How a line of <see cref="ChangedLines"/> is marked.</summary>
public enum LineMark
{
    /// <summary>A line both texts have, shown around a change.</summary>
    Same,

    /// <summary>A line of the first text that the second does not have.</summary>
    Removed,

    /// <summary>A line of the second text that the first does not have.</summary>
    Added,

    /// <summary>Where lines that did not change are left out.</summary>
    Gap,
}

/// <summary>
/// The lines "Changes only" shows: each changed line, and two lines around each place, with the line number it has in
/// its own text, the block it starts inside (for its colours), and its mark. A gap's text is "⋯" and it has no number.
/// </summary>
public sealed record ChangedLines(List<string> Lines, List<int> Blocks, List<string> Numbers, List<LineMark> Marks, int ChangedLineCount, int PlaceCount);

/// <summary>
/// The File Editor's comparison of a file with its export, line by line, as the HTML app makes it (its <c>lineDiff</c>
/// and <c>edMapLine</c>). Where the two texts part, the nearest place where they run together again is looked for, close
/// by first. The rules change a file in small places, so that is found at once; it is not a general comparison of any
/// two files.
/// </summary>
public static class LineComparison
{
    /// <summary>How far apart, in lines of both texts together, a place where they run together again is looked for.</summary>
    private const int SearchLimit = 4000;

    /// <summary>Every place where the two texts differ, from the top.</summary>
    public static List<LineDifference> Differences(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var differences = new List<LineDifference>();
        int i = 0, j = 0;
        while (i < a.Count || j < b.Count)
        {
            if (i < a.Count && j < b.Count && a[i] == b[j])
            {
                i++;
                j++;
                continue;
            }
            (int nextI, int nextJ) = WhereTheyMeetAgain(a, b, i, j);
            differences.Add(new LineDifference(i, nextI, j, nextJ));
            i = nextI;
            j = nextJ;
        }
        return differences;
    }

    /// <summary>How many lines that did not change are shown before and after each change.</summary>
    private const int LinesAround = 2;

    /// <summary>
    /// The lines "Changes only" shows, from the export set against the source (the HTML app's <c>edDiff</c>): at each
    /// place where they differ, the source's lines marked removed and the export's marked added, with the export's lines
    /// around them; a gap where lines are left out. Null when the texts are the same.
    /// </summary>
    /// <param name="a">The source's lines.</param>
    /// <param name="blocksA">The block each of the source's lines starts inside.</param>
    /// <param name="b">The export's lines.</param>
    /// <param name="blocksB">The block each of the export's lines starts inside.</param>
    public static ChangedLines? ChangesOnly(IReadOnlyList<string> a, IReadOnlyList<int> blocksA, IReadOnlyList<string> b, IReadOnlyList<int> blocksB)
    {
        List<LineDifference> differences = Differences(a, b);
        if (differences.Count == 0) return null;
        var shown = new ChangedLines([], [], [], [], 0, differences.Count);
        void Add(string text, int block, string number, LineMark mark)
        {
            shown.Lines.Add(text);
            shown.Blocks.Add(block);
            shown.Numbers.Add(number);
            shown.Marks.Add(mark);
        }
        void AddFromB(int k) => Add(b[k], blocksB[k], Number(k), LineMark.Same);

        int shownUpTo = 0, linesAfter = 0, changed = 0;
        foreach (LineDifference place in differences)
        {
            // the lines after the change before, when they run up to this one
            int upTo = Math.Min(linesAfter, place.FromB);
            for (int k = shownUpTo; k < upTo; k++) AddFromB(k);
            if (upTo > shownUpTo) shownUpTo = upTo;

            int from = Math.Max(shownUpTo, place.FromB - LinesAround);
            if (shown.Lines.Count > 0 && from > shownUpTo) Add("⋯", NdfColouring.Outside, "", LineMark.Gap);
            for (int k = from; k < place.FromB; k++) AddFromB(k);
            for (int k = place.FromA; k < place.ToA; k++) Add(a[k], blocksA[k], Number(k), LineMark.Removed);
            for (int k = place.FromB; k < place.ToB; k++) Add(b[k], blocksB[k], Number(k), LineMark.Added);

            changed += Math.Max(place.ToA - place.FromA, place.ToB - place.FromB);
            shownUpTo = place.ToB;
            linesAfter = Math.Min(b.Count, place.ToB + LinesAround);
        }
        for (int k = shownUpTo; k < linesAfter; k++) AddFromB(k);
        return shown with { ChangedLineCount = changed };
    }

    /// <summary>A line's number as shown, counting from 1.</summary>
    private static string Number(int index) => (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Which line of the second text is line <paramref name="line"/> of the first (the HTML app's <c>edMapLine</c>): the
    /// two texts are walked together as <see cref="Differences"/> walks them, stopping at that line. A line taken out maps
    /// to where it was; lines put in above it push the place down by as many. Lines count from 0.
    /// </summary>
    public static int MatchingLine(IReadOnlyList<string> a, IReadOnlyList<string> b, int line)
    {
        if (line <= 0) return 0;
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            if (a[i] == b[j])
            {
                if (i == line) return j;
                i++;
                j++;
                continue;
            }
            (int nextI, int nextJ) = WhereTheyMeetAgain(a, b, i, j);
            if (line < nextI) return Math.Max(j, Math.Min(j + (line - i), nextJ - 1));
            i = nextI;
            j = nextJ;
        }
        return Math.Max(0, Math.Min(b.Count - 1, j + (line - i)));
    }

    /// <summary>
    /// The nearest place after lines <paramref name="i"/> and <paramref name="j"/> where the texts run together again:
    /// two lines alike, followed by a second pair alike, and a third when the lines are too short to tell much (a lone
    /// brace, a blank line). The ends of both texts when there is none close by.
    /// </summary>
    private static (int I, int J) WhereTheyMeetAgain(IReadOnlyList<string> a, IReadOnlyList<string> b, int i, int j)
    {
        int n = a.Count, m = b.Count;
        for (int distance = 1; distance <= SearchLimit; distance++)
        {
            for (int stepInA = 0; stepInA <= distance; stepInA++)
            {
                int x = i + stepInA, y = j + distance - stepInA;
                if (x > n || y > m) continue;
                if (x == n && y == m) return (x, y);
                if (x < n && y < m && a[x] == b[y]
                    && (x + 1 >= n || y + 1 >= m || a[x + 1] == b[y + 1])
                    && (JsText.Trim(a[x]).Length > 2 || x + 2 >= n || y + 2 >= m || a[x + 2] == b[y + 2]))
                    return (x, y);
            }
        }
        return (n, m);
    }
}
