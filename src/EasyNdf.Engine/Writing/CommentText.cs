using System.Text;
using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Writing;

/// <summary>
/// Finding, splitting and removing comments in NDF text without being fooled by quotes.
/// </summary>
/// <remarks>
/// Every helper here skips over quoted text ('...' or "..."), so a // inside a string is not taken for a comment.
/// </remarks>
public static class CommentText
{
    /// <summary>
    /// Where a // comment starts on the last line of a text, skipping quotes and /* */ blocks; -1 when there is none.
    /// </summary>
    public static int LineCommentOnLastLine(string text)
    {
        char quote = '\0';
        for (int i = text.LastIndexOf('\n') + 1; i < text.Length; i++)
        {
            char ch = text[i];
            if (quote != '\0') { if (ch == quote) quote = '\0'; continue; }
            if (ch is '"' or '\'') { quote = ch; continue; }
            if (ch == '/' && JsText.At(text, i + 1) == '*')
            {
                int end = JsText.IndexOf(text, "*/", i + 2);
                if (end < 0) return -1;
                i = end + 1;
                continue;
            }
            if (ch == '/' && JsText.At(text, i + 1) == '/') return i;
        }
        return -1;
    }

    /// <summary>
    /// Splits one line a rule is writing into the value and a comment after it: "70 // note" gives ("70", " // note").
    /// </summary>
    /// <remarks>
    /// The text comes back whole, with no comment, when it has several lines, when it is only a comment,
    /// or when value text follows the comment.
    /// </remarks>
    public static (string Value, string Comment) SplitTrailingComment(string text)
    {
        if (text.Contains('\n') || !text.Contains('/')) return (text, "");
        char quote = '\0';
        int commentStart = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quote != '\0') { if (ch == quote) quote = '\0'; continue; }
            if (ch == '/' && JsText.At(text, i + 1) == '/')
            {
                if (commentStart < 0) commentStart = i;
                break;
            }
            if (ch == '/' && JsText.At(text, i + 1) == '*')
            {
                int end = JsText.IndexOf(text, "*/", i + 2);
                if (end < 0) return (text, "");
                if (commentStart < 0) commentStart = i;
                i = end + 1;
                continue;
            }
            if (ch is ' ' or '\t') continue;
            if (commentStart >= 0) return (text, ""); // value text after a /* */ comment
            if (ch is '"' or '\'') quote = ch;
        }
        if (commentStart < 0) return (text, "");
        string value = JsText.TrimSpacesAndTabsEnd(text[..commentStart]);
        return value == "" ? (text, "") : (value, text[value.Length..]);
    }

    /// <summary>A text with its comments taken out, for the copy of a descriptor that conditions read.</summary>
    public static string StripForConditions(string text)
    {
        if (!text.Contains('/')) return text;
        var output = new StringBuilder();
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quote != '\0')
            {
                output.Append(ch);
                if (ch == quote || ch == '\n') quote = '\0';
                continue;
            }
            if (ch is '"' or '\'') { quote = ch; output.Append(ch); continue; }
            if (ch == '/' && JsText.At(text, i + 1) == '/')
            {
                int end = text.IndexOf('\n', i);
                if (end < 0) break;
                i = end - 1;
                continue;
            }
            if (ch == '/' && JsText.At(text, i + 1) == '*')
            {
                int end = JsText.IndexOf(text, "*/", i + 2);
                if (end < 0) break;
                i = end + 1;
                continue;
            }
            output.Append(ch);
        }
        return RemoveSpacesBeforeLineEnds(output.ToString());
    }

    /// <summary>text.replace(/[ \t]+(\n|$)/g, '$1'): spaces and tabs at the end of every line go.</summary>
    private static string RemoveSpacesBeforeLineEnds(string text)
    {
        var output = new StringBuilder(text.Length);
        int pendingStart = -1; // where a run of spaces and tabs started
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch is ' ' or '\t')
            {
                if (pendingStart < 0) pendingStart = i;
                continue;
            }
            if (pendingStart >= 0)
            {
                if (ch != '\n') output.Append(text, pendingStart, i - pendingStart);
                pendingStart = -1;
            }
            output.Append(ch);
        }
        return output.ToString(); // a run at the very end is dropped
    }

    /// <summary>
    /// Where a comment a rule writes goes relative to the comments already on a value: a // comment must come last on the line.
    /// </summary>
    public static string JoinComments(string extra, string rest) =>
        extra == "" ? rest : (LineCommentOnLastLine(extra) >= 0 ? rest + extra : extra + rest);

    /// <summary>The first line break in a text that is not inside a /* */ comment, or -1. A \r\n counts from its \r.</summary>
    public static int FirstLineBreakOutsideComments(string text)
    {
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\n') return i > 0 && text[i - 1] == '\r' ? i - 1 : i;
            if (quote != '\0') { if (ch == quote) quote = '\0'; continue; }
            if (ch is '"' or '\'') { quote = ch; continue; }
            if (ch == '/' && JsText.At(text, i + 1) == '/')
            {
                int end = text.IndexOf('\n', i);
                return end < 0 ? -1 : (JsText.At(text, end - 1) == '\r' ? end - 1 : end);
            }
            if (ch == '/' && JsText.At(text, i + 1) == '*')
            {
                int end = JsText.IndexOf(text, "*/", i + 2);
                if (end < 0) return -1;
                i = end + 1;
            }
        }
        return -1;
    }

    /// <summary>
    /// A text with its comments taken out, keeping everything else as it is: used when "Keep source comments" is off.
    /// </summary>
    /// <remarks>
    /// A line that held only a comment goes with its line break; a comment after other text goes with the spaces before it.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <param name="startsAtLineStart">True when the text starts at the start of a line.</param>
    public static string RemoveComments(string text, bool startsAtLineStart)
    {
        if (!text.Contains("//", StringComparison.Ordinal) && !text.Contains("/*", StringComparison.Ordinal)) return text;

        var output = new StringBuilder();
        var line = new StringBuilder();
        bool cut = false;                    // a comment was taken out of this line
        bool firstLine = !startsAtLineStart; // the first line may be the end of a line that started before the text
        char quote = '\0';

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\n' || (ch == '\r' && JsText.At(text, i + 1) == '\n'))
            {
                string lineBreak = ch == '\n' ? "\n" : "\r\n";
                if (lineBreak.Length == 2) i++;
                string lineText = line.ToString();
                bool dropLine = cut && !firstLine && JsText.IsSpacesAndTabs(lineText);
                if (!dropLine) output.Append(cut ? JsText.TrimSpacesAndTabsEnd(lineText) : lineText).Append(lineBreak);
                line.Clear();
                cut = false;
                firstLine = false;
                quote = '\0';
                continue;
            }
            if (quote != '\0')
            {
                line.Append(ch);
                if (ch == quote) quote = '\0';
                continue;
            }
            if (ch is '"' or '\'') { quote = ch; line.Append(ch); continue; }

            if (ch == '/' && JsText.At(text, i + 1) == '/')
            {
                int end = text.IndexOf('\n', i);
                if (end < 0) end = text.Length;
                else if (text[end - 1] == '\r') end--;
                i = end - 1;
                cut = true;
                continue;
            }
            if (ch == '/' && JsText.At(text, i + 1) == '*')
            {
                int end = JsText.IndexOf(text, "*/", i + 2);
                end = end < 0 ? text.Length : end + 2;
                cut = true;
                bool lineEndsInSpace = line.Length == 0 || line[^1] is ' ' or '\t';
                if (lineEndsInSpace)
                {
                    while (JsText.At(text, end) is ' ' or '\t') end++;
                    if (JsText.At(text, end) is ',' or ')' or ']')
                    {
                        string trimmed = JsText.TrimSpacesAndTabsEnd(line.ToString());
                        line.Clear().Append(trimmed);
                    }
                }
                else if (end < text.Length && IsWordChar(line[^1]) && IsWordChar(text[end]))
                {
                    line.Append(' '); // "a/* */b" must not become "ab"
                }
                i = end - 1;
                continue;
            }
            line.Append(ch);
        }
        return output.Append(line).ToString();
    }

    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    /// <summary>The text between two list items, split into its parts.</summary>
    /// <param name="BeforeComma">What comes before the comma (spaces, comments).</param>
    /// <param name="HasComma">Whether there is a comma.</param>
    /// <param name="AfterComma">A comment that follows the comma on the same line.</param>
    /// <param name="Rest">The rest, usually a line break and the indentation: it belongs to the next item.</param>
    public readonly record struct ListGap(string BeforeComma, bool HasComma, string AfterComma, string Rest);

    /// <summary>
    /// Splits the text between two list items. The part up to and including a same-line comment after the comma belongs
    /// to the item before; the rest belongs to the next item.
    /// </summary>
    public static ListGap SplitListGap(string gap)
    {
        int i = 0;
        while (i < gap.Length)
        {
            char ch = gap[i];
            if (ch is ' ' or '\t' or '\r' or '\n') { i++; continue; }
            if (ch == '/' && JsText.At(gap, i + 1) == '*')
            {
                int end = JsText.IndexOf(gap, "*/", i + 2);
                i = end < 0 ? gap.Length : end + 2;
                continue;
            }
            if (ch == '/' && JsText.At(gap, i + 1) == '/')
            {
                int end = gap.IndexOf('\n', i);
                i = end < 0 ? gap.Length : end;
                continue;
            }
            break;
        }

        string before = "";
        bool hasComma = false;
        int afterCommaStart = 0;
        if (JsText.At(gap, i) == ',')
        {
            before = gap[..i];
            hasComma = true;
            afterCommaStart = i + 1;
        }

        int j = afterCommaStart, end2 = afterCommaStart;
        while (JsText.At(gap, j) is ' ' or '\t') j++;
        if (JsText.At(gap, j) == '/' && JsText.At(gap, j + 1) == '/')
        {
            int lineEnd = gap.IndexOf('\n', j);
            end2 = lineEnd < 0 ? gap.Length : (gap[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd);
        }
        else if (JsText.At(gap, j) == '/' && JsText.At(gap, j + 1) == '*')
        {
            int close = JsText.IndexOf(gap, "*/", j + 2);
            if (close >= 0 && !gap[j..close].Contains('\n'))
            {
                int k = close + 2;
                while (JsText.At(gap, k) is ' ' or '\t') k++;
                if (k >= gap.Length || gap[k] is '\n' or '\r') end2 = close + 2;
            }
        }
        return new ListGap(before, hasComma, gap[afterCommaStart..end2], gap[end2..]);
    }

    /// <summary>
    /// The line break and indentation a text ends with, without any comment in it: what a new list item is given so it
    /// lines up with its neighbours. A text with no line break gives itself when it is only spaces, and " " otherwise.
    /// </summary>
    public static string TrailingLineBreakAndIndent(string text)
    {
        int lastBreak = text.LastIndexOf('\n');
        if (lastBreak < 0) return JsText.IsSpacesAndTabs(text) ? text : " ";
        string lineBreak = lastBreak > 0 && text[lastBreak - 1] == '\r' ? "\r\n" : "\n";
        return lineBreak + JsText.LeadingSpacesAndTabs(text[(lastBreak + 1)..]);
    }
}
