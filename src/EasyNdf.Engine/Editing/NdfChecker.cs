using System.Text.RegularExpressions;
using EasyNdf.Engine.Ndf;

namespace EasyNdf.Engine.Editing;

/// <summary>A place where NDF text cannot be read, for the File Editor to mark.</summary>
/// <param name="Position">Where in the text the problem is.</param>
/// <param name="Length">How many characters the wrong part takes; 0 at the end of the text.</param>
/// <param name="Line">The line it is on, from 1.</param>
/// <param name="Message">What is wrong, starting "Line N:", as the engine words it.</param>
public sealed record NdfProblem(int Position, int Length, int Line, string Message);

/// <summary>
/// Finds the places where NDF text cannot be read, for the File Editor. A text the engine reads has none. Otherwise,
/// since the engine's reader stops at the first problem, each declaration is checked on its own: from a line that starts
/// one ("Name is", "export Name is", "unnamed") to the next. So every broken declaration gives one problem, and a bracket
/// left open is found where the next declaration starts, not at the end of the file.
/// </summary>
public static partial class NdfChecker
{
    /// <summary>The most problems listed: past these, the text is too broken for more to help.</summary>
    public const int MostProblems = 100;

    /// <summary>The engine's words for the end of the text, which for one declaration means the next one starts.</summary>
    private const string AtTheEnd = " (end of file)";

    /// <summary>The problems in a text, in order; none when the engine reads it.</summary>
    public static List<NdfProblem> Check(string text)
    {
        if (ProblemIn(text) is null) return [];
        var problems = new List<NdfProblem>();
        List<int> starts = DeclarationStarts(text);
        for (int k = 0; k < starts.Count && problems.Count < MostProblems; k++)
        {
            int start = starts[k], end = k + 1 < starts.Count ? starts[k + 1] : text.Length;
            NdfParseException? problem = ProblemIn(text[start..end]);
            if (problem is null || problem.Position < 0) continue;
            bool isLast = end == text.Length;
            problems.Add(isLast ? Placed(text, start + problem.Position, problem.Length, problem.Message) : InDeclaration(text, start, end, problem));
        }
        return problems;
    }

    /// <summary>The first problem reading a text; null when it reads.</summary>
    private static NdfParseException? ProblemIn(string text)
    {
        try
        {
            Parser.Check(text);
            return null;
        }
        catch (NdfParseException problem)
        {
            return problem;
        }
    }

    /// <summary>
    /// A problem found in one declaration of the text, placed in the whole text. One that ran out of text, such as a
    /// bracket left open, is marked on the first word of the next declaration.
    /// </summary>
    private static NdfProblem InDeclaration(string text, int start, int end, NdfParseException problem)
    {
        if (!problem.Message.EndsWith(AtTheEnd, StringComparison.Ordinal)) return Placed(text, start + problem.Position, problem.Length, problem.Message);
        int wordLength = 0;
        while (end + wordLength < text.Length && (char.IsAsciiLetterOrDigit(text[end + wordLength]) || text[end + wordLength] == '_')) wordLength++;
        string before = problem.Message[..^AtTheEnd.Length] + " before \"" + text.Substring(end, wordLength) + "\"";
        return Placed(text, end, wordLength, before);
    }

    /// <summary>A problem at a place of the whole text, its message starting with that place's line.</summary>
    private static NdfProblem Placed(string text, int position, int length, string message)
    {
        int line = LineAt(text, position);
        // the reader numbered the lines of the part it read: the message gets the line in the whole text
        string what = message.StartsWith("Line ", StringComparison.Ordinal) && message.IndexOf(": ", StringComparison.Ordinal) is int colon and > 0
            ? message[(colon + 2)..]
            : message;
        return new NdfProblem(position, length, line, "Line " + line + ": " + what);
    }

    /// <summary>Where each declaration starts: the start of the text, then each line that starts one.</summary>
    private static List<int> DeclarationStarts(string text)
    {
        List<int> starts = [0];
        foreach (Match match in DeclarationLine().Matches(text))
            if (match.Index > 0) starts.Add(match.Index);
        return starts;
    }

    /// <summary>A line that starts a declaration, as the game's files write them: at the very start of the line.</summary>
    [GeneratedRegex(@"^(?:(?:export|private|public)[ \t]+)?(?:unnamed\b|template\b|[A-Za-z_][A-Za-z0-9_]*[ \t]+is\b)", RegexOptions.Multiline)]
    private static partial Regex DeclarationLine();

    /// <summary>The line a place is on, from 1.</summary>
    private static int LineAt(string text, int position)
    {
        int line = 1;
        for (int at = text.IndexOf('\n'); at >= 0 && at < position; at = text.IndexOf('\n', at + 1)) line++;
        return line;
    }
}
