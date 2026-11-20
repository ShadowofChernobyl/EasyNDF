using System.Text.RegularExpressions;
using EasyNdf.Engine.Ndf;

namespace EasyNdf.Engine.Editing;

/// <summary>A value an export would write that stops mod generation (or crashes the game), as the File Editor marks it.</summary>
/// <param name="Descriptor">The name of the declaration it is in; empty for an unnamed one.</param>
/// <param name="Line">The value's line in the exported file, counted from 1.</param>
/// <param name="FirstLine">For a name set twice, the line of its first value; 0 otherwise.</param>
/// <param name="Problem">What is wrong, as the File Editor's check finds it.</param>
public sealed record ExportValueError(string Descriptor, int Line, int FirstLine, ValueProblem Problem);

/// <summary>
/// Export's value check: the File Editor's check (<see cref="ValueChecker"/>) on what the rules wrote. Only the
/// declarations that differ from the source file are read, so a large file with a few changes is checked quickly; the
/// check runs on the file the last mod gave, so a value one mod writes and a later mod changes is judged as exported.
/// Warnings are left out: mod generation works with them.
/// </summary>
public static partial class ExportValueCheck
{
    /// <summary>The most errors kept for a file; a rule that writes a wrong value into every descriptor would make thousands.</summary>
    public const int MostErrors = 200;

    /// <summary>
    /// The errors in an export, in the order of the file. Tuned for speed: the declarations that changed are read and
    /// checked in parallel, as the read-back check reads them, since a rule can change every one of a 3 MB file's. Keep
    /// it that way when tidying.
    /// </summary>
    /// <param name="source">The file the rules ran on.</param>
    /// <param name="exported">What they made of it.</param>
    /// <param name="guide">The values' types and limits.</param>
    /// <param name="impacts">The impact names the MimeticImpactMapping.ndf the game will load maps; null checks no ImpactHappening.</param>
    /// <param name="classes">The classes the game's files use; null checks no class.</param>
    public static List<ExportValueError> Find(string source, string exported, ValueGuide guide, IReadOnlySet<string>? impacts = null, IReadOnlySet<string>? classes = null)
    {
        var before = new Dictionary<string, (int Start, int End)>(StringComparer.Ordinal);
        int unnamed = 0;
        foreach ((int start, int end) in NdfChecker.DeclarationStretches(source))
            before.TryAdd(KeyOf(source, start, ref unnamed), (start, end));

        var changed = new List<(int Start, int End)>();
        unnamed = 0;
        foreach ((int start, int end) in NdfChecker.DeclarationStretches(exported))
        {
            string key = KeyOf(exported, start, ref unnamed);
            if (before.TryGetValue(key, out (int Start, int End) was)
                && source.AsSpan(was.Start, was.End - was.Start).SequenceEqual(exported.AsSpan(start, end - start))) continue;
            changed.Add((start, end));
        }

        var found = new (string Name, List<ValueProblem> Problems)[changed.Count];
        // whether the file gives its mounted weapons their names (WARNO v210942 on), seen in the whole file: a changed
        // declaration alone can't tell an older file's mounted weapons from ones a rule wrote the older way
        bool mountsNamed = changed.Count > 0 && (ValueChecker.NamesMountedWeapons(source) || ValueChecker.NamesMountedWeapons(exported));
        Parallel.For(0, changed.Count, index =>
        {
            (int start, int end) = changed[index];
            NdfDocument document;
            // what does not read is the read-back check's to report (RunResult.ParseError)
            try { document = Parser.Parse(exported[start..end]); }
            catch (NdfParseException) { found[index] = ("", []); return; }
            string name = document.Declarations.Count > 0 && !document.Declarations[0].IsUnnamed ? document.Declarations[0].Name : "";
            List<ValueProblem> problems = ValueChecker.Check(document, guide, start, mountsNamed, impacts).FindAll(problem => !problem.IsWarning);
            if (classes is not null) problems.AddRange(UnknownClasses(exported, start, end, classes));
            found[index] = (name, problems);
        });

        var errors = new List<ExportValueError>();
        LineCounter? lines = null;
        foreach ((string name, List<ValueProblem> problems) in found)
            foreach (ValueProblem problem in problems)
            {
                lines ??= new LineCounter(exported);
                int firstLine = problem.Kind == ValueProblemKind.WrittenTwice ? lines.At(problem.FirstPosition) : 0;
                errors.Add(new ExportValueError(name, lines.At(problem.Position), firstLine, problem));
                if (errors.Count >= MostErrors) return errors;
            }
        return errors;
    }

    /// <summary>
    /// The objects of a stretch of text whose class (a T name before its opening bracket) is an older or other form of a
    /// class the game's files use, as TUIResourceTexture_Common of TUIResourceTexture: each once, where it is first
    /// written, with the game's class as its value. A class the files don't use at all is not reported: the game knows
    /// classes its files never write (TEugBMutableFloat), so only a renamed one is surely wrong. Text in quotes and
    /// comments is passed over.
    /// </summary>
    public static IEnumerable<ValueProblem> UnknownClasses(string text, int start, int end, IReadOnlySet<string> classes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string stretch = Blanked(text.Substring(start, end - start));
        foreach (Match use in ClassUse().Matches(stretch))
        {
            string type = use.Groups[1].Value;
            if (classes.Contains(type) || !seen.Add(type)) continue;
            string? game = classes.Where(known => type.StartsWith(known + "_", StringComparison.Ordinal) || known.StartsWith(type + "_", StringComparison.Ordinal))
                .OrderByDescending(known => known.Length).FirstOrDefault();
            if (game is null) continue;
            yield return new ValueProblem(start + use.Groups[1].Index, type.Length, ValueProblemKind.UnknownClass, type, game);
        }
    }

    /// <summary>A text with its quoted texts and comments turned to spaces, each place kept where it was.</summary>
    private static string Blanked(string text)
    {
        char[] chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c is '"' or '\'')
            {
                int close = text.IndexOf(c, i + 1);
                if (close < 0) close = chars.Length - 1;
                for (int j = i; j <= close; j++) if (chars[j] != '\n') chars[j] = ' ';
                i = close;
            }
            else if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
                for (; i < chars.Length && chars[i] != '\n'; i++) chars[i] = ' ';
            else if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                close = close < 0 ? chars.Length - 1 : close + 1;
                for (int j = i; j <= close; j++) if (chars[j] != '\n') chars[j] = ' ';
                i = close;
            }
        }
        return new string(chars);
    }

    /// <summary>A class name before the bracket of its object: TUIResourceTexture( or TUIResourceTexture\n(.</summary>
    [GeneratedRegex(@"\b(T[A-Z][A-Za-z0-9_]*)\s*\(")]
    private static partial Regex ClassUse();

    /// <summary>The classes in game files' texts: every T name before an object's bracket.</summary>
    public static void AddClassesOf(string text, ISet<string> classes)
    {
        foreach (Match use in ClassUse().Matches(text)) classes.Add(use.Groups[1].Value);
    }

    /// <summary>
    /// What a declaration is known by, to find it again in the other text: its name, or for an unnamed one its place
    /// among the unnamed ones.
    /// </summary>
    private static string KeyOf(string text, int start, ref int unnamed)
    {
        Match name = DeclarationName().Match(text, start, Math.Min(400, text.Length - start));
        if (!name.Success || name.Groups[1].Value == "unnamed") return "\u0001unnamed " + unnamed++;
        return name.Groups[1].Value;
    }

    /// <summary>The first name of a declaration, after "export", "private" and the like.</summary>
    [GeneratedRegex(@"\G\s*(?:(?:export|private|public|template)\s+)*([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex DeclarationName();

    /// <summary>The line of a place in a text: where each line starts is found once, then each place by halving.</summary>
    private sealed class LineCounter
    {
        private readonly List<int> starts = [0];

        public LineCounter(string text)
        {
            for (int at = text.IndexOf('\n'); at >= 0; at = text.IndexOf('\n', at + 1)) starts.Add(at + 1);
        }

        public int At(int position)
        {
            int found = starts.BinarySearch(position);
            return (found >= 0 ? found : ~found - 1) + 1;
        }
    }
}
