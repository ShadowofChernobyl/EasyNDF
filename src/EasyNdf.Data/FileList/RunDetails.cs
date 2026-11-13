using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.FileList;

/// <summary>Which of a run's rules Details lists.</summary>
public enum RuleFilter
{
    /// <summary>Every rule.</summary>
    All,
    /// <summary>The rules that made an edit.</summary>
    WithEdits,
    /// <summary>The rules that are on and changed nothing (<see cref="RunRecord.IsIdle"/>).</summary>
    ChangedNothing,
    /// <summary>The rules that skipped something.</summary>
    Skipped,
    /// <summary>The rules with a warning.</summary>
    Warnings,
    /// <summary>The rules that wrote a value that would stop mod generation.</summary>
    Errors,
}

/// <summary>A line of Details' rule list.</summary>
/// <param name="Index">The rule's place among the rules of the run.</param>
/// <param name="IsGroup">Whether it is a group's heading.</param>
public readonly record struct RuleLine(int Index, bool IsGroup);

/// <summary>What a line of Details' edit list shows.</summary>
public enum EditRowKind
{
    /// <summary>A sentence, not a value: "Removed from the file."</summary>
    Note,
    /// <summary>A value with a before and an after.</summary>
    Change,
    /// <summary>Text only added, such as an entry of a list.</summary>
    Added,
    /// <summary>Text only taken out.</summary>
    Removed,
    /// <summary>A value whose words stayed the same.</summary>
    SpacingOnly,
}

/// <summary>An edit, as a line of Details' edit list.</summary>
/// <param name="Kind">What the line shows.</param>
/// <param name="Target">The property or setting changed, as Details writes it.</param>
/// <param name="Entry">The edit.</param>
/// <param name="Change">The value's pieces; null for a note.</param>
public sealed record EditRow(EditRowKind Kind, string Target, EditLogEntry Entry, ValueChange? Change);

/// <summary>A rule's edits to one descriptor.</summary>
/// <param name="Title">The descriptor, as Details writes it.</param>
/// <param name="Name">The name it goes by in the file; empty when it has none to look for.</param>
/// <param name="Rows">The edits.</param>
public sealed record EditGroup(string Title, string Name, IReadOnlyList<EditRow> Rows);

/// <summary>How Details lays out a rule's edits.</summary>
public enum EditListShape
{
    /// <summary>A heading for each descriptor, with its edits under it.</summary>
    ByDescriptor,
    /// <summary>One edit of one target on each of many descriptors: a line each.</summary>
    OneTarget,
    /// <summary>One note for each of many descriptors: a line each.</summary>
    OneNoteEach,
}

/// <summary>A rule's edits, as Details lists them.</summary>
/// <param name="Groups">The edits, by descriptor.</param>
/// <param name="Shape">How they are laid out.</param>
/// <param name="HasChange">Whether a line shows a before and an after, which is what the column headings are for.</param>
public sealed record RuleEdits(IReadOnlyList<EditGroup> Groups, EditListShape Shape, bool HasChange);

/// <summary>
/// A run's rules as Details shows them: the groups they fall in, the filters and Find, which rule it opens on, and the
/// edits of each rule by descriptor.
/// </summary>
public sealed class RunDetails
{
    /// <summary>Over this many rules, a run opens with its groups folded, but for the chosen rule's.</summary>
    private const int MostRulesUnfolded = 30;

    /// <summary>The rule a descriptor's edits are about, on a line of its own: what follows this mark in a target is a place inside it.</summary>
    private const string InsideMark = " > ";

    /// <summary>What Details writes in place of <see cref="InsideMark"/>.</summary>
    private const string ShownInsideMark = " › ";

    /// <summary>Targets of rules that make or remove a whole descriptor: with nothing before, what they log is a sentence.</summary>
    private static readonly HashSet<string> WholeDescriptorTargets = ["Remove", "Add", "Clone"];

    private readonly int[] groupOf;
    private readonly Dictionary<int, List<int>> rulesOf = [];
    private readonly Dictionary<int, List<RunWarning>> warnings = [];
    private readonly Dictionary<int, List<RunValueError>> errors = [];
    private readonly Dictionary<EditLogEntry, RunValueError> errorAt = new(ReferenceEqualityComparer.Instance);

    /// <summary>Works out the groups, warnings and errors of a run.</summary>
    public RunDetails(RunRecord record)
    {
        Record = record;
        List<RuleStats> rules = Stats;
        // a mod's rules run on their own, so a group of the mod before ends where they start
        var modStarts = new HashSet<int>();
        string? mod = null;
        foreach (RuleSource source in record.RuleSources)
        {
            if (source.Mod != mod) modStarts.Add(source.First);
            mod = source.Mod;
        }
        groupOf = new int[rules.Count];
        int group = -1;
        for (int index = 0; index < rules.Count; index++)
        {
            if (modStarts.Contains(index)) group = -1;
            if (rules[index].Kind == "Group")
            {
                group = index;
                groupOf[index] = -1;
                rulesOf[index] = [];
                continue;
            }
            groupOf[index] = group;
            RuleCount++;
            if (group >= 0) rulesOf[group].Add(index);
        }
        var firstByName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int index = 0; index < rules.Count; index++)
        {
            if (!firstByName.TryGetValue(rules[index].Name, out List<int>? named)) firstByName[rules[index].Name] = named = [];
            named.Add(index);
        }
        var others = new List<RunWarning>();
        foreach (RunWarning warning in record.Result.Warnings)
        {
            if (!firstByName.TryGetValue(warning.Rule, out List<int>? named)) { others.Add(warning); continue; }
            foreach (int index in named)
            {
                if (!warnings.TryGetValue(index, out List<RunWarning>? held)) warnings[index] = held = [];
                held.Add(warning);
            }
        }
        OtherWarnings = others;
        var otherErrors = new List<RunValueError>();
        foreach (RunValueError error in record.ValueErrors)
        {
            if (error.Rule < 0 || error.Rule >= rules.Count) { otherErrors.Add(error); continue; }
            if (!errors.TryGetValue(error.Rule, out List<RunValueError>? held)) errors[error.Rule] = held = [];
            held.Add(error);
            // an edit past what the log kept has no line to mark
            IReadOnlyList<EditLogEntry> log = LogOf(error.Rule);
            if (error.Edit >= 0 && error.Edit < log.Count) errorAt.TryAdd(log[error.Edit], error);
        }
        OtherErrors = otherErrors;
    }

    /// <summary>The run.</summary>
    public RunRecord Record { get; }

    /// <summary>The rules' counts, in the order they ran.</summary>
    public List<RuleStats> Stats => Record.Result.Stats;

    /// <summary>How many rules ran, not counting groups.</summary>
    public int RuleCount { get; }

    /// <summary>Whether any rule is in a group.</summary>
    public bool HasGroups => rulesOf.Count > 0;

    /// <summary>Warnings given under a name no rule has, so they cannot be shown on a rule.</summary>
    public IReadOnlyList<RunWarning> OtherWarnings { get; }

    /// <summary>Values that would stop mod generation that no rule's edits name, so they cannot be shown on a rule.</summary>
    public IReadOnlyList<RunValueError> OtherErrors { get; }

    /// <summary>A rule's values that would stop mod generation.</summary>
    public IReadOnlyList<RunValueError> ErrorsOf(int rule) => errors.TryGetValue(rule, out List<RunValueError>? held) ? held : [];

    /// <summary>The value an edit wrote that would stop mod generation; null for a value that is fine.</summary>
    public RunValueError? ErrorAt(EditLogEntry entry) => errorAt.GetValueOrDefault(entry);

    /// <summary>A rule's values that would stop mod generation with no line of its edits to show them on.</summary>
    public IEnumerable<RunValueError> UnlistedErrorsOf(int rule)
    {
        IReadOnlyList<EditLogEntry> log = LogOf(rule);
        return ErrorsOf(rule).Where(error => error.Edit < 0 || error.Edit >= log.Count || !ReferenceEquals(errorAt.GetValueOrDefault(log[error.Edit]), error));
    }

    /// <summary>Whether a rule's kind changes nothing in the file by design.</summary>
    public static bool MakesNoEdits(string kind) => kind is "Group" or "Variable" or "Check" or "Text" or "File";

    /// <summary>Whether a rule ran: not switched off, by itself or with its group.</summary>
    public static bool IsOn(RuleStats rule) => rule.Enabled && !rule.OffByGroup;

    /// <summary>The rules of a group.</summary>
    public IReadOnlyList<int> RulesIn(int group) => rulesOf.TryGetValue(group, out List<int>? rules) ? rules : [];

    /// <summary>The group a rule is in; -1 for none.</summary>
    public int GroupOf(int rule) => groupOf[rule];

    /// <summary>A rule's warnings.</summary>
    public IReadOnlyList<RunWarning> WarningsOf(int rule) => warnings.TryGetValue(rule, out List<RunWarning>? held) ? held : [];

    /// <summary>A rule's edits, as logged.</summary>
    public IReadOnlyList<EditLogEntry> LogOf(int rule) => rule < Record.Result.Logs.Count ? Record.Result.Logs[rule] : [];

    /// <summary>How many rules a filter lists.</summary>
    public int Count(RuleFilter filter)
    {
        int count = 0;
        for (int index = 0; index < Stats.Count; index++)
            if (Stats[index].Kind != "Group" && Passes(index, filter)) count++;
        return count;
    }

    private bool Passes(int index, RuleFilter filter)
    {
        RuleStats rule = Stats[index];
        return filter switch
        {
            RuleFilter.WithEdits => rule.Edits > 0,
            RuleFilter.ChangedNothing => RunRecord.IsIdle(rule),
            RuleFilter.Skipped => rule.Skipped > 0,
            RuleFilter.Warnings => warnings.ContainsKey(index),
            RuleFilter.Errors => errors.ContainsKey(index),
            _ => true,
        };
    }

    /// <summary>Whether a rule, its descriptors or its values hold a text. Tuned for speed: nothing is put together to look in, as a run can log tens of thousands of edits. Keep it that way when tidying.</summary>
    private bool Holds(int index, string find)
    {
        if (Holds(Stats[index].Name, find)) return true;
        foreach (EditLogEntry entry in LogOf(index))
            if (Holds(entry.Descriptor, find) || Holds(entry.Target, find) || Holds(entry.Old, find) || Holds(entry.New, find)) return true;
        return false;
    }

    private static bool Holds(string text, string find) => text.Contains(find, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a rule shows with a filter and a text to find.</summary>
    public bool Shows(int index, RuleFilter filter, string find) =>
        Stats[index].Kind != "Group" && Passes(index, filter) && (find == "" || Holds(index, find));

    /// <summary>
    /// The lines of the rule list. With a filter or a text to find, the rules that show are listed under their groups
    /// whatever is folded, and groups with none are left out.
    /// </summary>
    public List<RuleLine> Lines(RuleFilter filter, string find)
    {
        bool narrowed = filter != RuleFilter.All || find != "";
        HashSet<int> folded = Folds;
        var lines = new List<RuleLine>();
        int group = -1;
        bool groupListed = true;
        for (int index = 0; index < Stats.Count; index++)
        {
            if (Stats[index].Kind == "Group")
            {
                group = index;
                groupListed = !narrowed;
                if (groupListed) lines.Add(new RuleLine(index, IsGroup: true));
                continue;
            }
            if (!Shows(index, filter, find)) continue;
            if (!narrowed && groupOf[index] >= 0 && folded.Contains(groupOf[index])) continue;
            if (!groupListed && group >= 0 && groupOf[index] == group)
            {
                lines.Add(new RuleLine(group, IsGroup: true));
                groupListed = true;
            }
            lines.Add(new RuleLine(index, IsGroup: false));
        }
        return lines;
    }

    /// <summary>The groups folded: for a long run, all but the chosen rule's, until the user folds them otherwise.</summary>
    public HashSet<int> Folds
    {
        get
        {
            if (Record.FoldedGroups is HashSet<int> folded) return folded;
            folded = [];
            if (RuleCount > MostRulesUnfolded)
            {
                int chosen = ChosenRule;
                foreach (int group in rulesOf.Keys)
                    if (chosen < 0 || groupOf[chosen] != group) folded.Add(group);
            }
            return Record.FoldedGroups = folded;
        }
    }

    /// <summary>
    /// The rule whose edits are listed; -1 for none. Until one is chosen, the first that wrote a value that would stop
    /// mod generation; then the first with a change to read; then the first with anything listed, and then simply the first.
    /// </summary>
    public int ChosenRule
    {
        get
        {
            if (Record.ShownRule is int shown && shown >= 0 && shown < Stats.Count && Stats[shown].Kind != "Group") return shown;
            int first = FirstIndex(errors.ContainsKey);
            if (first < 0) first = FirstIndex(index => LogOf(index).Any(entry => !IsNote(entry)));
            if (first < 0) first = FirstIndex(index => Stats[index].Edits > 0);
            if (first < 0) first = FirstIndex(_ => true);
            Record.ShownRule = first < 0 ? null : first;
            return first;
        }
        set => Record.ShownRule = value;
    }

    private int FirstIndex(Func<int, bool> test)
    {
        for (int index = 0; index < Stats.Count; index++)
            if (Stats[index].Kind != "Group" && test(index)) return index;
        return -1;
    }

    /// <summary>Whether what an edit logged is a sentence rather than a value.</summary>
    private static bool IsNote(EditLogEntry entry) => entry.IsNote || (entry.Old == "" && WholeDescriptorTargets.Contains(entry.Target));

    /// <summary>A name or target as Details writes it: a place inside another with a mark that reads as a path.</summary>
    private static string Shown(string text) => text.Replace(InsideMark, ShownInsideMark, StringComparison.Ordinal);

    /// <summary>
    /// A rule's edits by descriptor, with what each changed worked out. With a text to find that the rule's own name
    /// does not hold, only the edits and descriptors holding it. Each line's change is worked out here, as the shape of
    /// the list rests on them, and is not kept: a rule is listed at most as many times as it is clicked.
    /// </summary>
    public RuleEdits EditsOf(int rule, string find)
    {
        bool everyEdit = find == "" || Holds(Stats[rule].Name, find);
        var groups = new List<EditGroup>();
        string? descriptor = null;
        List<EditRow>? rows = null;
        foreach (EditLogEntry entry in LogOf(rule))
        {
            if (!ReferenceEquals(entry.Descriptor, descriptor) && entry.Descriptor != descriptor)
            {
                descriptor = entry.Descriptor;
                rows = null;
            }
            if (!everyEdit && !Holds(entry.Descriptor, find) && !Holds(entry.Target, find) && !Holds(entry.Old, find) && !Holds(entry.New, find)) continue;
            if (rows is null)
            {
                rows = [];
                int inside = entry.Descriptor.IndexOf(InsideMark, StringComparison.Ordinal);
                // "(constants)" and "(no name)" stand for no one thing in the file to go to
                string name = entry.Descriptor.StartsWith('(') ? "" : inside < 0 ? entry.Descriptor : entry.Descriptor[..inside];
                groups.Add(new EditGroup(Shown(entry.Descriptor), name, rows));
            }
            rows.Add(RowOf(entry));
        }
        bool single = groups.Count > 1 && groups.All(group => group.Rows.Count == 1);
        EditListShape shape = single && groups.All(group => group.Rows[0].Kind == EditRowKind.Change && group.Rows[0].Target == groups[0].Rows[0].Target) ? EditListShape.OneTarget
            : single && groups.All(group => group.Rows[0].Kind == EditRowKind.Note) ? EditListShape.OneNoteEach
            : EditListShape.ByDescriptor;
        return new RuleEdits(groups, shape, groups.Any(group => group.Rows.Any(row => row.Kind == EditRowKind.Change)));
    }

    private static EditRow RowOf(EditLogEntry entry)
    {
        string target = Shown(entry.Target.StartsWith("> ", StringComparison.Ordinal) ? entry.Target[2..] : entry.Target);
        if (IsNote(entry)) return new EditRow(EditRowKind.Note, target, entry, null);
        ValueChange change = ValueDiff.Of(entry.Old, entry.New);
        EditRowKind kind = change.Shape switch
        {
            ValueChangeShape.Added => EditRowKind.Added,
            ValueChangeShape.Removed => EditRowKind.Removed,
            ValueChangeShape.SpacingOnly => EditRowKind.SpacingOnly,
            _ => EditRowKind.Change,
        };
        return new EditRow(kind, target, entry, change);
    }
}
