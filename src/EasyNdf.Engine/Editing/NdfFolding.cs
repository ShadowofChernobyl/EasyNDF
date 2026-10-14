using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Editing;

/// <summary>
/// Which lines of NDF text start a block that folds away, and where it ends, as the HTML app's File Editor finds them
/// (its <c>edDepth</c> and <c>edBlock</c>). A block starts on a line that leaves a bracket open, ( or [, or on a line such
/// as "Name is TType" when the bracket opens on one of the next lines; it ends on the line that closes it. Brackets in
/// comments, quotes and braces do not count.
/// </summary>
public static class NdfFolding
{
    /// <summary>What a line's first piece of code is: none, an opening bracket, or anything else.</summary>
    private enum FirstCode { None, OpeningBracket, Other }

    /// <summary>
    /// How many ( and [ a line opens, less those it closes, outside comments, quotes and braces; and what its first piece
    /// of code is (the HTML app's <c>edDepth</c>).
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="startsInside">The block the line starts inside (see <see cref="NdfColouring"/>).</param>
    private static (int Depth, FirstCode First) Depth(string line, int startsInside)
    {
        int depth = 0, at = 0, inside = startsInside;
        FirstCode first = FirstCode.None;
        while (at < line.Length)
        {
            if (inside != NdfColouring.Outside)
            {
                string end = NdfColouring.BlockEnds[inside]!;
                int found = JsText.IndexOf(line, end, at);
                if (found < 0) break;
                at = found + end.Length;
                inside = NdfColouring.Outside;
                continue;
            }
            char c = line[at];
            if (c == '/' && at + 1 < line.Length)
            {
                char next = line[at + 1];
                if (next == '/') break;
                if (next == '*')
                {
                    inside = NdfColouring.InComment;
                    at += 2;
                    continue;
                }
            }
            if (c is '(' or '[')
            {
                depth++;
                if (first == FirstCode.None) first = FirstCode.OpeningBracket;
            }
            else if (c is ')' or ']')
            {
                depth--;
                if (first == FirstCode.None) first = FirstCode.Other;
            }
            else if (c is '{' or '\'' or '"')
            {
                inside = c == '{' ? NdfColouring.InBraces : c == '\'' ? NdfColouring.InSingleQuotes : NdfColouring.InDoubleQuotes;
                if (first == FirstCode.None) first = FirstCode.Other;
            }
            else if (c > ' ' && c < '\uD800' && first == FirstCode.None) first = FirstCode.Other;
            at++;
        }
        return (depth, first);
    }

    /// <summary>
    /// The line that ends the block line <paramref name="index"/> starts (the HTML app's <c>edBlock</c>), or -1 when it
    /// starts none or the block never closes. Lines count from 0.
    /// </summary>
    /// <param name="lines">The text's lines.</param>
    /// <param name="blocks">The block each line starts inside (see <see cref="NdfColouring.BlocksOfLines"/>).</param>
    /// <param name="index">The line.</param>
    public static int BlockEnd(IReadOnlyList<string> lines, IReadOnlyList<int> blocks, int index) => Block(lines, blocks, index, startOnly: false);

    /// <summary>
    /// Whether line <paramref name="index"/> starts a block (the HTML app's <c>edCanFold</c>): only its start is looked at,
    /// not whether it closes, so a read-only view can tell quickly.
    /// </summary>
    public static bool StartsBlock(IReadOnlyList<string> lines, IReadOnlyList<int> blocks, int index) => Block(lines, blocks, index, startOnly: true) >= 0;

    /// <summary>
    /// Every block that starts at the top level, which is every descriptor, as "Fold all" folds them: each one's first
    /// and last line, from the top.
    /// </summary>
    public static List<(int First, int Last)> TopLevelBlocks(IReadOnlyList<string> lines, IReadOnlyList<int> blocks)
    {
        var found = new List<(int, int)>();
        for (int i = 0; i < lines.Count; i++)
        {
            int end = BlockEnd(lines, blocks, i);
            if (end <= i) continue;
            found.Add((i, end));
            i = end;
        }
        return found;
    }

    /// <summary>
    /// Where the block line <paramref name="index"/> starts would end. With <paramref name="startOnly"/>, the line after
    /// its opening bracket, without looking for its end.
    /// </summary>
    private static int Block(IReadOnlyList<string> lines, IReadOnlyList<int> blocks, int index, bool startOnly)
    {
        int count = lines.Count;
        (int depth, FirstCode first) = Depth(lines[index], blocks[index]);
        int next;
        if (depth > 0) next = index + 1;
        else if (depth == 0 && first == FirstCode.Other)
        {
            // "Name is TType", with the bracket on one of the next three lines, after any blank or comment lines
            int j = index + 1;
            while (j < count && j < index + 4 && Depth(lines[j], blocks[j]).First == FirstCode.None) j++;
            if (j >= count) return -1;
            (int openDepth, FirstCode openFirst) = Depth(lines[j], blocks[j]);
            if (openFirst != FirstCode.OpeningBracket || openDepth <= 0) return -1;
            next = j + 1;
            depth = openDepth;
        }
        else return -1;

        if (startOnly) return next < count ? next : -1;
        for (int k = next; k < count; k++)
        {
            depth += Depth(lines[k], blocks[k]).Depth;
            if (depth <= 0) return k;
        }
        return -1;
    }
}
