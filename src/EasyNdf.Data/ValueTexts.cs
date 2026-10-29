using System.Text.RegularExpressions;

namespace EasyNdf.Data;

/// <summary>
/// Finds a value's lines in a file's text, for the Mod Conflicts window's panels: the descriptor's line, then each part
/// of the edit log's target ("ModulesDescriptors > TProductionModuleDescriptor > ProductionRessourcesNeeded") in turn
/// inside the descriptor, and a few lines each side of the last. Made once for a text and asked for each value in it.
/// </summary>
public sealed partial class ValueTexts
{
    /// <summary>How many lines are shown each side of the value's line.</summary>
    public const int Around = 6;

    private readonly string[] lines;

    /// <summary>The line each declaration starts on, by name, and those lines in order, to know where a descriptor ends.</summary>
    private readonly Dictionary<string, int> declarations = new(StringComparer.Ordinal);
    private readonly List<int> starts = [];

    /// <summary>Reads the text's lines and where its declarations start (a name at the start of a line, then "is").</summary>
    public ValueTexts(string text)
    {
        lines = text.Split('\n');
        for (int at = 0; at < lines.Length; at++)
        {
            lines[at] = lines[at].TrimEnd('\r');
            if (lines[at].Length == 0 || char.IsWhiteSpace(lines[at][0])) continue;
            Match declared = Declaration().Match(lines[at]);
            if (!declared.Success) continue;
            starts.Add(at);
            declarations.TryAdd(declared.Groups[1].Value, at);
        }
    }

    /// <summary>
    /// The lines around a value: its property's line in the descriptor, found by each part of the target in turn; the
    /// descriptor's own line when the target is the descriptor (a name made by Clone or Add) or a part is not found.
    /// Null when the descriptor is not in the text.
    /// </summary>
    /// <param name="descriptor">The descriptor's name, as the edit log has it.</param>
    /// <param name="target">The target, as the edit log has it.</param>
    public ValueText? Find(string descriptor, string target)
    {
        if (!declarations.TryGetValue(descriptor, out int start)) return null;
        int index = starts.BinarySearch(start);
        int end = index + 1 < starts.Count ? starts[index + 1] : lines.Length;
        int at = start;
        if (target is not ("Clone" or "Add"))
            foreach (string part in target.Split(" > ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                int found = LineWith(Plain(part), at + (at == start ? 1 : 0), end);
                if (found < 0) break;
                at = found;
            }
        int from = Math.Max(start, at - Around), to = Math.Min(end, at + Around + 1);
        return new ValueText(lines[from..to], at - from);
    }

    /// <summary>A part of a target without what picks among several (an index "[2]", or a quoted text).</summary>
    private static string Plain(string part)
    {
        int bracket = part.IndexOf('[');
        return (bracket > 0 ? part[..bracket] : part).Trim().Trim('\'', '"');
    }

    /// <summary>The first line from <paramref name="from"/> up to <paramref name="end"/> that holds the name as a whole word; -1 when none does.</summary>
    private int LineWith(string name, int from, int end)
    {
        if (name == "") return -1;
        for (int at = from; at < end; at++)
        {
            int found = lines[at].IndexOf(name, StringComparison.Ordinal);
            while (found >= 0)
            {
                bool startsWord = found == 0 || !IsNamePart(lines[at][found - 1]);
                bool endsWord = found + name.Length == lines[at].Length || !IsNamePart(lines[at][found + name.Length]);
                if (startsWord && endsWord) return at;
                found = lines[at].IndexOf(name, found + 1, StringComparison.Ordinal);
            }
        }
        return -1;
    }

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>A declaration's first line: "export Name is ..." or "Name is ...".</summary>
    [GeneratedRegex(@"^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s+is\b")]
    private static partial Regex Declaration();
}
