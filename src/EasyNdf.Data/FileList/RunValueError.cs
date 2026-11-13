using EasyNdf.Engine.Editing;
using EasyNdf.Engine.Rules;
using EasyNdf.Engine.Text;

namespace EasyNdf.Data.FileList;

/// <summary>A value a run wrote that would stop mod generation, with the edit that wrote it, for Details.</summary>
/// <param name="Error">The value: where it is and what is wrong with it.</param>
/// <param name="Rule">The rule that wrote it, by its place among the rules of the run; -1 when no rule's edits name its descriptor.</param>
/// <param name="Edit">The edit in that rule's log; -1 when the rule's edits name the descriptor but not the value.</param>
public sealed record RunValueError(ExportValueError Error, int Rule, int Edit)
{
    /// <summary>The same value, its lines counted with as many lines more before it, such as the export's header.</summary>
    public RunValueError Below(int lines) =>
        this with { Error = Error with { Line = Error.Line + lines, FirstLine = Error.FirstLine > 0 ? Error.FirstLine + lines : 0 } };

    /// <summary>
    /// Finds the edit that wrote each value: of the edits of the value's name in its descriptor, the last rule's that
    /// wrote that very value (a later mod's edit is the one left in the file), and in that rule the first not taken by
    /// an error before, as both are in the order of the file. Without such an edit, the last rule that edited the
    /// descriptor, such as the Clone rule that made it.
    /// </summary>
    /// <param name="logs">Each rule's edits, in the order the rules ran.</param>
    /// <param name="errors">The values, in the order of the file.</param>
    public static List<RunValueError> Place(IReadOnlyList<List<EditLogEntry>> logs, IReadOnlyList<ExportValueError> errors)
    {
        if (errors.Count == 0) return [];
        var named = errors.Select(error => error.Descriptor).ToHashSet(StringComparer.Ordinal);
        var editsOf = new Dictionary<(string Descriptor, string Name), List<(int Rule, int Edit)>>();
        var lastRuleOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int rule = 0; rule < logs.Count; rule++)
            for (int edit = 0; edit < logs[rule].Count; edit++)
            {
                EditLogEntry entry = logs[rule][edit];
                string descriptor = DescriptorOf(entry.Descriptor);
                if (!named.Contains(descriptor)) continue;
                lastRuleOf[descriptor] = rule;
                if (entry.IsNote) continue;
                (string, string) key = (descriptor, NameOf(entry.Target));
                if (!editsOf.TryGetValue(key, out List<(int Rule, int Edit)>? edits)) editsOf[key] = edits = [];
                edits.Add((rule, edit));
            }

        var taken = new HashSet<(int Rule, int Edit)>();
        var placed = new List<RunValueError>(errors.Count);
        foreach (ExportValueError error in errors)
        {
            if (editsOf.TryGetValue((error.Descriptor, error.Problem.Name), out List<(int Rule, int Edit)>? edits))
            {
                (int Rule, int Edit) at = Pick(edits, logs, error.Problem.Value, taken);
                taken.Add(at);
                placed.Add(new RunValueError(error, at.Rule, at.Edit));
            }
            else placed.Add(new RunValueError(error, lastRuleOf.GetValueOrDefault(error.Descriptor, -1), -1));
        }
        return placed;
    }

    private static (int Rule, int Edit) Pick(List<(int Rule, int Edit)> edits, IReadOnlyList<List<EditLogEntry>> logs, string value, HashSet<(int, int)> taken)
    {
        List<(int Rule, int Edit)> writing = [.. edits.Where(at => Writes(logs[at.Rule][at.Edit], value))];
        if (writing.Count == 0) writing = edits;
        int last = writing[^1].Rule;
        foreach ((int Rule, int Edit) at in writing)
            if (at.Rule == last && !taken.Contains(at)) return at;
        return writing[^1];
    }

    /// <summary>Whether an edit left a value as it is in the file; a long value the log kept only part of is not compared.</summary>
    private static bool Writes(EditLogEntry entry, string value) =>
        entry.NewLeftOut == 0 && !entry.New.Contains(EditLogEntry.LeftOutMark, StringComparison.Ordinal) && JsText.Trim(entry.New) == JsText.Trim(value);

    /// <summary>The descriptor an edit was in: the log names an object inside one after it, "Unit_X > TModuleDescriptor".</summary>
    private static string DescriptorOf(string logged)
    {
        int inside = logged.IndexOf(ActionApplier.PathSeparator, StringComparison.Ordinal);
        return inside < 0 ? logged : logged[..inside];
    }

    /// <summary>The name an edit changed: the last part of its target, "Turrets > YulBoneOrdinal" or "> AngleDispersion".</summary>
    private static string NameOf(string target)
    {
        int last = target.LastIndexOf('>');
        return JsText.Trim(last < 0 ? target : target[(last + 1)..]);
    }
}
