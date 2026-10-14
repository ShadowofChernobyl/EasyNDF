using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Writing;

/// <summary>One place where two texts differ: lines [AStart, AEnd) of the first stand where [BStart, BEnd) of the second do.</summary>
public readonly record struct DiffHunk(int AStart, int AEnd, int BStart, int BEnd);

/// <summary>
/// The changed lines between a file and its export, for the Editor tab's "Changes only".
/// </summary>
/// <remarks>
/// Where the two texts part, the nearest place where they run together again is looked for, closest first. Rules change
/// a file in small places, so that is found at once. It is not a general comparison of any two files: past
/// <see cref="SearchLimit"/> lines it gives up and treats the rest as one change.
/// </remarks>
public static class LineDiff
{
    /// <summary>How far ahead, in lines, a place where the texts run together again is looked for.</summary>
    public const int SearchLimit = 4000;

    public static List<DiffHunk> Compare(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var hunks = new List<DiffHunk>();
        int n = a.Count, m = b.Count, i = 0, j = 0;
        while (i < n || j < m)
        {
            if (i < n && j < m && a[i] == b[j]) { i++; j++; continue; }
            var (nextI, nextJ) = WhereTheyMeetAgain(a, b, i, j);
            hunks.Add(new DiffHunk(i, nextI, j, nextJ));
            i = nextI;
            j = nextJ;
        }
        return hunks;
    }

    /// <summary>
    /// The nearest pair of positions after (i, j) where the texts agree again: two lines in a row the same (three when the
    /// line is very short, such as a lone bracket, so the match is not a coincidence), or both ends.
    /// </summary>
    private static (int, int) WhereTheyMeetAgain(IReadOnlyList<string> a, IReadOnlyList<string> b, int i, int j)
    {
        int n = a.Count, m = b.Count;
        for (int distance = 1; distance <= SearchLimit; distance++)
        {
            for (int skipA = 0; skipA <= distance; skipA++)
            {
                int x = i + skipA, y = j + distance - skipA;
                if (x > n || y > m) continue;
                if (x == n && y == m) return (x, y);
                bool same = x < n && y < m && a[x] == b[y]
                    && (x + 1 >= n || y + 1 >= m || a[x + 1] == b[y + 1])
                    && (JsText.Trim(a[x]).Length > 2 || x + 2 >= n || y + 2 >= m || a[x + 2] == b[y + 2]);
                if (same) return (x, y);
            }
        }
        return (n, m);
    }
}
