using System.Text;
using System.Text.RegularExpressions;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Rules;
using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Writing;

/// <summary>
/// Writes the exported file. It copies the source text and changes only what rules changed, so the file keeps its
/// comments, indentation, line endings and the mod builder's marker comments.
/// </summary>
/// <remarks>
/// <para>
/// The output is collected as a list of pieces rather than one string, because the rules' safety check later reads back
/// only the pieces of the declarations rules changed (<see cref="Result.ChangedSegments"/>).
/// </para>
/// <para>
/// A // comment written by a rule cannot go where the value goes when more text follows on that line: it would comment
/// that text out. Such a comment waits in <see cref="waitingLineComments"/> and is written at the end of the line.
/// </para>
/// </remarks>
public sealed partial class ExportWriter
{
    /// <summary>The finished text, its pieces, and where each changed or added declaration starts and ends among the pieces.</summary>
    public sealed record Result(string Text, List<string> Pieces, List<(int Start, int End)> ChangedSegments);

    private readonly List<string> pieces = [];
    private readonly List<string> waitingLineComments = [];
    private readonly List<(int Start, int End)> segments = [];
    private readonly string source;
    private readonly string lineBreak;

    private ExportWriter(string source, string lineBreak)
    {
        this.source = source;
        this.lineBreak = lineBreak;
    }

    /// <summary>Writes the declarations in the order the rules left them.</summary>
    /// <param name="work">The declarations as the rules left them.</param>
    /// <param name="source">The file's text.</param>
    /// <param name="lineBreak">The file's line break: "\n" or "\r\n".</param>
    public static Result Write(List<WorkItem> work, string source, string lineBreak) =>
        new ExportWriter(source, lineBreak).WriteAll(work);

    // ---- putting text out ----

    /// <summary>Text that does not come from the file gets the file's line endings.</summary>
    private string WithFileLineBreaks(string text)
    {
        if (!text.Contains('\n')) return text;
        return lineBreak == "\n" ? text.Replace("\r\n", "\n") : AnyLineBreak().Replace(text, "\r\n");
    }

    /// <summary>Adds text. Waiting // comments go in just before the first line break.</summary>
    private void Put(string text)
    {
        if (text == "") return;
        if (waitingLineComments.Count > 0)
        {
            int breakAt = CommentText.FirstLineBreakOutsideComments(text);
            if (breakAt >= 0)
            {
                string head = JsText.TrimSpacesAndTabsEnd(text[..breakAt]);
                if (head != "") pieces.Add(head);
                pieces.Add(" ");
                pieces.Add(string.Join(" ", waitingLineComments));
                pieces.Add(text[breakAt..]);
                waitingLineComments.Clear();
                return;
            }
        }
        pieces.Add(text);
    }

    /// <summary>Adds text that may end with a // comment; the comment waits for the end of the line.</summary>
    private void PutWithLateComment(string text)
    {
        int commentAt = CommentText.LineCommentOnLastLine(text);
        if (commentAt < 0) { Put(text); return; }
        string head = JsText.TrimSpacesAndTabsEnd(text[..commentAt]);
        if (head != "") Put(head);
        waitingLineComments.Add(text[commentAt..]);
    }

    /// <summary>Copies the text between two positions of a source text.</summary>
    private void Gap(string text, int? from, int? to)
    {
        if (from is null || to is null || to <= from) return;
        string part = text[from.Value..to.Value];
        Put(ReferenceEquals(text, source) || text == source ? part : WithFileLineBreaks(part));
    }

    // ---- values ----

    private void WriteValue(NdfValue value, string text)
    {
        if (value.Start is null)
        {
            // written by a rule; a value typed on several lines takes the indentation of the line it lands on
            var scalar = (ScalarValue)value;
            string written = scalar.FitsIndentation ? Reindented(scalar.Text) : scalar.Text;
            if (value is ScalarValue { Kind: ScalarKind.Raw }) PutWithLateComment(WithFileLineBreaks(written));
            else Put(WithFileLineBreaks(written));
        }
        else
        {
            // Everything between the parts of a value (names, "=", brackets, commas, spaces, line breaks, comments)
            // is copied from the text.
            switch (value)
            {
                case ObjectValue obj:
                    Put(obj.TypeName);
                    if (obj.TypeNameComment is not null) PutWithLateComment(WithFileLineBreaks(obj.TypeNameComment));
                    int? at = obj.Start + obj.SourceTypeNameLength;
                    foreach (var property in obj.Properties.Where(property => !property.IsAddedByRule))
                    {
                        at = SkipRemovedProperties(obj, text, at, property.Value.StartOrReplaced);
                        Gap(text, at, property.Value.StartOrReplaced);
                        WriteValue(property.Value, text);
                        at = property.Value.EndOrReplaced;
                    }
                    at = SkipRemovedProperties(obj, text, at, obj.End);
                    WriteClosingWithAddedProperties(obj, text, at);
                    break;

                case ListValue list:
                    WriteList(list, text);
                    break;

                case PairValue pair:
                    WriteTwo(pair.First, pair.Second, pair, text);
                    break;

                case BinaryValue binary:
                    WriteTwo(binary.Left, binary.Right, binary, text);
                    break;

                case WrappedValue wrapped:
                    Gap(text, wrapped.Start, wrapped.Inner.StartOrReplaced);
                    WriteValue(wrapped.Inner, text);
                    Gap(text, wrapped.Inner.EndOrReplaced, wrapped.End);
                    break;

                case AssignValue assign:
                    Gap(text, assign.Start, assign.Value.StartOrReplaced);
                    WriteValue(assign.Value, text);
                    Gap(text, assign.Value.EndOrReplaced, assign.End);
                    break;

                default:
                    Put(text[value.Start.Value..value.End!.Value]);
                    break;
            }
        }
        if (value.AddedComment is not null && value.AddedComment != "")
            PutWithLateComment(WithFileLineBreaks(value.AddedComment));
    }

    /// <summary>
    /// A value a rule wrote on several lines, indented to fit where it lands: its lines after the first lose the
    /// indentation they all share and take that of the line the value starts on, so "[" and "]" line up with the
    /// property's name whatever indentation was typed. A value on one line is left as it is.
    /// </summary>
    private string Reindented(string written)
    {
        if (!written.Contains('\n')) return written;
        string[] lines = written.Replace("\r\n", "\n").Split('\n');
        int shared = int.MaxValue;
        for (int k = 1; k < lines.Length; k++)
        {
            if (lines[k].Trim().Length == 0) continue;
            int indent = lines[k].Length - lines[k].TrimStart(' ', '\t').Length;
            shared = Math.Min(shared, indent);
        }
        if (shared == int.MaxValue) return written;
        string baseIndent = IndentOfCurrentLine();
        for (int k = 1; k < lines.Length; k++)
            lines[k] = lines[k].Trim().Length == 0 ? "" : baseIndent + lines[k][shared..];
        return string.Join("\n", lines);
    }

    /// <summary>The spaces and tabs at the start of the line being written.</summary>
    private string IndentOfCurrentLine()
    {
        var line = new StringBuilder();
        for (int k = pieces.Count - 1; k >= 0; k--)
        {
            string piece = pieces[k];
            int breakAt = piece.LastIndexOf('\n');
            if (breakAt >= 0)
            {
                line.Insert(0, piece[(breakAt + 1)..]);
                break;
            }
            line.Insert(0, piece);
        }
        string text = line.ToString();
        return text[..(text.Length - text.TrimStart(' ', '\t').Length)];
    }

    /// <summary>
    /// Writes the text from <paramref name="at"/> up to <paramref name="until"/>, leaving out the properties a rule
    /// removed in between (Remove Property), and returns where the writing got to. A property on a line of its own goes
    /// with its line, the comment at its end included; the comments above it stay. One sharing its line with others
    /// goes with the spaces after it.
    /// </summary>
    private int? SkipRemovedProperties(ObjectValue obj, string text, int? at, int? until)
    {
        if (obj.RemovedProperties is null || at is null || until is null) return at;
        foreach (var removed in obj.RemovedProperties.OrderBy(property => property.Value.StartOrReplaced))
        {
            int? valueStart = removed.Value.StartOrReplaced, valueEnd = removed.Value.EndOrReplaced;
            if (valueStart is null || valueEnd is null || valueStart < at || valueStart >= until) continue;
            var (start, end) = RemovedPropertySpan(text, valueStart.Value, valueEnd.Value);
            if (start < at) continue;
            Gap(text, at, start);
            at = end;
        }
        return at;
    }

    /// <summary>Where a removed property's text starts and ends, its name and "=" included.</summary>
    private static (int Start, int End) RemovedPropertySpan(string text, int valueStart, int valueEnd)
    {
        // back over "=" (or "is") and the name
        int nameStart = valueStart;
        while (nameStart > 0 && JsText.IsWhiteSpace(text[nameStart - 1])) nameStart--;
        if (nameStart > 0 && text[nameStart - 1] == '=') nameStart--;
        else if (nameStart >= 2 && text[(nameStart - 2)..nameStart] == "is") nameStart -= 2;
        while (nameStart > 0 && JsText.IsWhiteSpace(text[nameStart - 1])) nameStart--;
        while (nameStart > 0 && (char.IsAsciiLetterOrDigit(text[nameStart - 1]) || text[nameStart - 1] == '_')) nameStart--;

        int lineStart = nameStart;
        while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t') lineStart--;
        bool startsItsLine = lineStart == 0 || text[lineStart - 1] == '\n';

        int end = valueEnd;
        while (end < text.Length && text[end] is ' ' or '\t') end++;
        if (end + 1 < text.Length && text[end] == '/' && text[end + 1] == '/')
            while (end < text.Length && text[end] != '\n' && text[end] != '\r') end++;
        bool endsItsLine = end >= text.Length || text[end] is '\n' or '\r';
        if (startsItsLine && endsItsLine)
        {
            if (end < text.Length && text[end] == '\r') end++;
            if (end < text.Length && text[end] == '\n') end++;
            return (lineStart, end);
        }
        return (nameStart, end);
    }

    /// <summary>
    /// Writes the end of an object, from its last property to its ")": properties a rule added go in on lines of
    /// their own just before the line the ")" is on, indented as the object's first property, so a comment at the end
    /// of the last property's line stays on that line. An object written on one line gets them before its ")".
    /// </summary>
    private void WriteClosingWithAddedProperties(ObjectValue obj, string text, int? at)
    {
        var added = obj.Properties.Where(property => property.IsAddedByRule).ToList();
        if (added.Count == 0 || at is null || obj.End is null)
        {
            Gap(text, at, obj.End);
            return;
        }
        string closing = text[at.Value..obj.End.Value];
        int closingLineAt = closing.LastIndexOf('\n');
        if (closingLineAt < 0)
        {
            // one line: "TFoo ( A = 1 )" becomes "TFoo ( A = 1 B = 2 )"
            // they go right after the last property, so the spaces before ")" stay before it
            int insertAt = JsText.TrimSpacesAndTabsEnd(closing[..closing.LastIndexOf(')')]).Length;
            Gap(text, at, at + insertAt);
            foreach (var property in added)
            {
                Put(" " + property.Name + " = ");
                WriteValue(property.Value, text);
            }
            Gap(text, at + insertAt, obj.End);
            return;
        }
        // up to the line break before the ")" line (a "\r\n" break keeps its "\r" with the line it ends)
        int lineEnd = closingLineAt > 0 && closing[closingLineAt - 1] == '\r' ? closingLineAt - 1 : closingLineAt;
        Gap(text, at, at + lineEnd);
        string indent = AddedPropertyIndent(obj, text);
        foreach (var property in added)
        {
            Put(lineBreak + indent + property.Name + " = ");
            WriteValue(property.Value, text);
        }
        Gap(text, at + lineEnd, obj.End);
    }

    /// <summary>
    /// The indent for a property a rule adds: that of the object's first property from the source, or one step more
    /// than the object's own line when it has none.
    /// </summary>
    private static string AddedPropertyIndent(ObjectValue obj, string text)
    {
        var first = obj.Properties.FirstOrDefault(property => !property.IsAddedByRule && property.Value.StartOrReplaced is not null);
        if (first is not null) return IndentOfLineAt(text, first.Value.StartOrReplaced!.Value);
        string indent = IndentOfLineAt(text, obj.Start!.Value);
        return indent + (indent.Contains('\t') ? "\t" : "    ");
    }

    private void WriteTwo(NdfValue first, NdfValue second, NdfValue whole, string text)
    {
        Gap(text, whole.Start, first.StartOrReplaced);
        WriteValue(first, text);
        Gap(text, first.EndOrReplaced, second.StartOrReplaced);
        WriteValue(second, text);
        Gap(text, second.EndOrReplaced, whole.End);
    }

    // ---- lists ----

    /// <summary>
    /// Writes a list. The items that came from the file keep the spacing and comments around them; items a rule added
    /// are spaced like their neighbours; comments above an item a rule took out stay, above whatever follows.
    /// </summary>
    private void WriteList(ListValue list, string text)
    {
        List<NdfValue> original = list.SourceItems ?? list.Items;
        List<NdfValue> current = list.Items;
        int n = original.Count, m = current.Count;
        int closeAt = list.End!.Value - 1;
        string FromText(string part) => ReferenceEquals(text, source) || text == source ? part : WithFileLineBreaks(part);

        Gap(text, list.Start, list.OpenBracketAt + 1); // the type, if it has one, and "["

        if (n == 0)
        {
            WriteItemsIntoEmptyList(list, current, text, closeAt, FromText);
            return;
        }

        // The text around each original item: lead[i] comes before item i (lead[n] before "]"),
        // and before/comma/after the comma come after it.
        var lead = new List<string> { FromText(text[(list.OpenBracketAt + 1)..original[0].Start!.Value]) };
        var beforeComma = new List<string>();
        var hasComma = new List<bool>();
        var afterComma = new List<string>();
        for (int i = 0; i < n; i++)
        {
            int gapEnd = i < n - 1 ? original[i + 1].Start!.Value : closeAt;
            var gap = CommentText.SplitListGap(FromText(JsText.Slice(text, original[i].End!.Value, gapEnd)));
            beforeComma.Add(gap.BeforeComma);
            hasComma.Add(gap.HasComma);
            afterComma.Add(gap.AfterComma);
            lead.Add(gap.Rest);
        }

        string firstNew = CommentText.TrailingLineBreakAndIndent(lead[0]);
        string middleNew = n > 1
            ? CommentText.TrailingLineBreakAndIndent(lead[1])
            : (lead[0].Contains('\n') ? CommentText.TrailingLineBreakAndIndent(lead[0]) : " ");
        bool lastHasComma = hasComma[n - 1];

        int j = 0;              // the next original item
        string carried = "";    // comments that stood above an item that was taken out
        bool previousEndedWithLineComment = false;

        string FreshLine()
        {
            foreach (string l in lead)
                if (l.Contains('\n')) return CommentText.TrailingLineBreakAndIndent(l);
            string indent = IndentOfLineAt(text, list.Start!.Value);
            return lineBreak + indent + (indent.Contains('\t') ? "\t" : "    ");
        }

        void Before(string leading)
        {
            if (carried != "")
            {
                string trimmed = JsText.TrimEnd(carried);
                Put(trimmed);
                if (!leading.Contains('\n')) leading = carried[trimmed.Length..];
                carried = "";
            }
            if (previousEndedWithLineComment && !leading.Contains('\n')) leading = FreshLine();
            previousEndedWithLineComment = false;
            Put(leading);
        }

        void SkipTo(int? position)
        {
            while (j < n && original[j].Start != position)
            {
                if (JsText.HasNonWhiteSpace(lead[j])) carried += lead[j];
                j++;
            }
        }

        for (int k = 0; k < m; k++)
        {
            NdfValue item = current[k];
            int? at = item.StartOrReplaced;
            bool commaAfter = k < m - 1 || lastHasComma;

            if (at is null)
            {
                // an item a rule added
                Before(k == 0 ? firstNew : middleNew);
                WriteValue(item, text);
                if (commaAfter) Put(",");
            }
            else
            {
                SkipTo(at);
                if (j >= n) throw new InvalidOperationException("The writer lost track of a list item.");
                string leading = j == 0
                    ? (k == 0 || lead[0].Contains('\n') ? lead[0] : middleNew)
                    : (k == 0 && !JsText.HasNonWhiteSpace(lead[j]) ? firstNew : lead[j]);
                Before(leading);
                WriteValue(item, text);
                Put(beforeComma[j]);
                if (commaAfter) Put(",");
                Put(afterComma[j]);
                previousEndedWithLineComment = afterComma[j].Contains("//", StringComparison.Ordinal);
                j++;
            }
            if (item.TrailingLineComment is not null) waitingLineComments.Add(item.TrailingLineComment);
        }
        SkipTo(-1); // the rest of the original items were taken out
        Before(lead[n]);
        Put("]");
    }

    /// <summary>
    /// An empty list that gets items: one item a line, a level deeper than the line the list starts on,
    /// as the game writes its own lists.
    /// </summary>
    private void WriteItemsIntoEmptyList(ListValue list, List<NdfValue> items, string text, int closeAt, Func<string, string> fromText)
    {
        string inside = text[(list.OpenBracketAt + 1)..closeAt];
        if (items.Count == 0)
        {
            Put(fromText(inside));
            Put("]");
            return;
        }
        string indent = IndentOfLineAt(text, list.Start!.Value);
        string step = indent.Contains('\t') ? "\t" : "    ";
        if (JsText.HasNonWhiteSpace(inside)) Put(fromText(JsText.TrimEnd(inside)));
        foreach (var item in items)
        {
            Put(lineBreak + indent + step);
            WriteValue(item, text);
            Put(",");
            if (item.TrailingLineComment is not null) waitingLineComments.Add(item.TrailingLineComment);
        }
        Put(lineBreak + indent);
        Put("]");
    }

    /// <summary>The spaces and tabs at the start of the line a position is on.</summary>
    private static string IndentOfLineAt(string text, int position)
    {
        int lineStart = JsText.LastIndexOf(text, '\n', position) + 1;
        return JsText.LeadingSpacesAndTabs(JsText.Slice(text, lineStart, position));
    }

    // ---- declarations ----

    private void WriteDeclaration(Declaration declaration)
    {
        string text = declaration.Source;
        if (declaration.NameStart is null)
        {
            Gap(text, declaration.Start, declaration.Value.StartOrReplaced);
        }
        else
        {
            Gap(text, declaration.Start, declaration.NameStart);
            Put(declaration.Name);
            if (declaration.NameComment is not null) PutWithLateComment(WithFileLineBreaks(declaration.NameComment));
            Gap(text, declaration.NameStart + declaration.NameLength, declaration.Value.StartOrReplaced);
        }
        WriteValue(declaration.Value, text);
        Gap(text, declaration.Value.EndOrReplaced, declaration.End);
    }

    /// <summary>
    /// What is left of a line after a declaration ends, when that is only spaces and comments: it belongs to that
    /// declaration. Returns where the line's text ends, or <paramref name="position"/> when other text follows.
    /// </summary>
    private int EndOfLineRest(int position)
    {
        int i = position;
        while (true)
        {
            char ch = JsText.At(source, i);
            if (ch is ' ' or '\t') { i++; continue; }
            if (ch == '/' && JsText.At(source, i + 1) == '/')
            {
                int end = source.IndexOf('\n', i);
                return end < 0 ? source.Length : (source[end - 1] == '\r' ? end - 1 : end);
            }
            if (ch == '/' && JsText.At(source, i + 1) == '*')
            {
                int end = JsText.IndexOf(source, "*/", i + 2);
                if (end < 0 || source[i..end].Contains('\n')) return position;
                i = end + 2;
                continue;
            }
            return i >= source.Length || ch is '\n' or '\r' ? i : position;
        }
    }

    /// <summary>
    /// A copy made by a Clone rule leaves out the marker comments of other tools (YMB, YSM): their ids would be in the
    /// file twice. A YMB-ORIGINAL marker goes with the commented-out original lines under it; the line in force stays.
    /// </summary>
    private static string WithoutToolMarkers(string text)
    {
        var lines = new List<string>();
        bool inOriginal = false;
        foreach (string line in text.Split('\n'))
        {
            if (MarkerLine().IsMatch(line))
            {
                inOriginal = OriginalMarkerLine().IsMatch(line);
                continue;
            }
            if (inOriginal && CommentLine().IsMatch(line)) continue;
            inOriginal = false;
            lines.Add(MarkerAtLineEnd().Replace(line, "", 1));
        }
        return string.Join("\n", lines);
    }

    /// <summary>The text between declarations when it is only blank lines, or "" otherwise.</summary>
    private static string BlankLinesOnly(string text) => BlankLines().IsMatch(text) ? text : "";

    /// <summary>
    /// Comment lines right under a declaration, followed by a blank line, belong to it (such as a marker comment that
    /// closes it). Returns where those comment lines end, or <paramref name="position"/> when there are none or no blank
    /// line follows them: then it is unclear which declaration they belong to.
    /// </summary>
    private int EndOfCommentsBelow(int position)
    {
        Match comments = CommentLinesThenBlankLine().Match(source, position);
        return comments.Success ? position + comments.Groups[1].Length : position;
    }

    /// <summary>Whether the next item written after item <paramref name="k"/> is a declaration a rule added or cloned.</summary>
    private static bool AddedDeclarationFollows(List<WorkItem> work, int k)
    {
        int next = k + 1;
        while (next < work.Count && work[next].IsRemoved && !work[next].IsFromFile) next++;
        return next < work.Count && !work[next].IsFromFile;
    }

    private Result WriteAll(List<WorkItem> work)
    {
        int copiedUpTo = 0;      // how far into the file the text between declarations has been written
        string lastLead = "";

        for (int k = 0; k < work.Count; k++)
        {
            WorkItem item = work[k];

            if (!item.IsFromFile)
            {
                if (item.IsRemoved) continue;

                // A declaration a rule added is set apart from the one before it the way the file sets its own apart.
                string separator = "";
                for (int q = k + 1; q < work.Count; q++)
                {
                    if (work[q].IsFromFile)
                    {
                        separator = BlankLinesOnly(JsText.Slice(source, copiedUpTo, work[q].Declaration!.Start));
                        break;
                    }
                }
                Put(separator != "" ? separator : (BlankLinesOnly(lastLead) is { Length: > 0 } b ? b : lineBreak));

                int segmentStart = pieces.Count;
                if (item.IsAdded)
                {
                    Put(WithFileLineBreaks(item.AddedText!));
                }
                else
                {
                    // a copy of an indented declaration starts indented too
                    Declaration copy = item.Declaration!;
                    string text = copy.Source;
                    int lineStart = JsText.LastIndexOf(text, '\n', copy.Start - 1) + 1;
                    string indent = JsText.Slice(text, lineStart, copy.Start);
                    if (indent != "" && JsText.IsSpacesAndTabs(indent)) Put(indent);
                    WriteDeclaration(copy);
                    if (item.IsClone)
                    {
                        string written = string.Concat(pieces.Skip(segmentStart));
                        pieces.RemoveRange(segmentStart, pieces.Count - segmentStart);
                        Put(WithoutToolMarkers(written));
                    }
                }
                segments.Add((segmentStart, pieces.Count));
                continue;
            }

            Declaration declaration = item.Declaration!;
            int lineEnd = EndOfLineRest(declaration.End);
            // A copy or an added declaration goes below the comments that belong to this one, not between the two.
            int ownedEnd = !item.IsRemoved && AddedDeclarationFollows(work, k) ? EndOfCommentsBelow(lineEnd) : lineEnd;
            string lead = JsText.Slice(source, copiedUpTo, declaration.Start);
            lastLead = lead;

            if (item.IsRemoved)
            {
                // a removed declaration leaves the comments that sat above it
                if (JsText.HasNonWhiteSpace(lead)) Put(JsText.TrimEnd(lead));
                copiedUpTo = ownedEnd;
                continue;
            }

            // Nothing written yet because the file's first declarations were removed: no blank lines at the top.
            if (pieces.Count == 0 && copiedUpTo > 0 && !JsText.HasNonWhiteSpace(lead)) lead = "";

            if (!item.IsOwnCopy)
            {
                Put(lead + JsText.Slice(source, declaration.Start, ownedEnd));
            }
            else
            {
                Put(lead);
                int segmentStart = pieces.Count;
                WriteDeclaration(declaration);
                Put(JsText.Slice(source, declaration.End, lineEnd));
                segments.Add((segmentStart, pieces.Count));
                Put(JsText.Slice(source, lineEnd, ownedEnd));
            }
            copiedUpTo = ownedEnd;
        }

        Put(JsText.Slice(source, copiedUpTo));
        if (waitingLineComments.Count > 0)
        {
            pieces.Add(" ");
            pieces.Add(string.Join(" ", waitingLineComments));
        }
        return new Result(string.Concat(pieces), pieces, segments);
    }

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex AnyLineBreak();

    [GeneratedRegex(@"\A[ \t]*//[ \t]*(?:YMB|YSM)-")]
    private static partial Regex MarkerLine();

    [GeneratedRegex(@"\A[ \t]*//[ \t]*YMB-ORIGINAL(?![A-Za-z0-9_])")]
    private static partial Regex OriginalMarkerLine();

    [GeneratedRegex(@"\A[ \t]*//")]
    private static partial Regex CommentLine();

    [GeneratedRegex(@"[ \t]*//[ \t]*(?:YMB|YSM)-[^\r]*")]
    private static partial Regex MarkerAtLineEnd();

    [GeneratedRegex(@"\G((?:\r?\n[ \t]*(?://[^\r\n]*|/\*(?:(?!\*/)[^\r\n])*\*/[ \t]*))+)\r?\n[ \t]*\r?\n")]
    private static partial Regex CommentLinesThenBlankLine();

    [GeneratedRegex(@"\A[ \t\r\n]*\n[ \t\r\n]*\z")]
    private static partial Regex BlankLines();
}
