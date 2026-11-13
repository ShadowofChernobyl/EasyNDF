using System.Text.RegularExpressions;
using EasyNdf.Engine.Ndf;

namespace EasyNdf.Engine.Editing;

/// <summary>A value an export would write that stops mod generation, as the File Editor marks it.</summary>
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
    public static List<ExportValueError> Find(string source, string exported, ValueGuide guide)
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
            found[index] = (name, ValueChecker.Check(document, guide, start, mountsNamed).FindAll(problem => !problem.IsWarning));
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
