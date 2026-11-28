using EasyNdf.Data.FileList;
using EasyNdf.Engine;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

public class RunDetailsTests
{
    private static RuleStats Rule(string name, string kind = "Edit", int matched = 0, int edits = 0, int skipped = 0, bool enabled = true) =>
        new() { Name = name, Kind = kind, Enabled = enabled, Matched = matched, Edits = edits, Skipped = skipped };

    private static readonly LoadSummary Nothing = new(0, 0, 0, 0, 0, [], [], [], []);

    private static EditLogEntry Edit(string descriptor, string target = "Speed", string old = "1", string now = "2") => new(descriptor, target, old, now);

    private static RunDetails Run(params (RuleStats Rule, EditLogEntry[] Log)[] rules) => new(Record(rules));

    private static RunRecord Record(params (RuleStats Rule, EditLogEntry[] Log)[] rules) => new()
    {
        Result = new RunResult { Stats = [.. rules.Select(rule => rule.Rule)], Logs = [.. rules.Select(rule => rule.Log.ToList())], Warnings = [] },
        Info = Nothing,
        Milliseconds = 1,
        RuleSets = "",
    };

    [Fact]
    public void Rules_fall_in_the_group_above_them_and_a_new_mod_starts_outside_any_group()
    {
        RunRecord record = Record((Rule("G", "Group"), []), (Rule("A"), []), (Rule("B"), []), (Rule("C"), []));
        RunRecord withMods = new()
        {
            Result = record.Result, Info = record.Info, Milliseconds = 1, RuleSets = "",
            RuleSources = [new RuleSource("First", "Set", 0, 3), new RuleSource("Second", "Set", 3, 1)],
        };

        Assert.Equal([1, 2, 3], new RunDetails(record).RulesIn(0));
        var details = new RunDetails(withMods);
        Assert.Equal([1, 2], details.RulesIn(0));
        Assert.Equal(-1, details.GroupOf(3));
        Assert.Equal(3, details.RuleCount);
    }

    [Fact]
    public void It_opens_on_the_first_rule_with_a_change_to_read_rather_than_one_with_only_notes()
    {
        RunDetails details = Run(
            (Rule("G", "Group"), []),
            (Rule("Removes", edits: 1), [new EditLogEntry("D", "Remove", "", "Removed from the file.", IsNote: true)]),
            (Rule("Changes", edits: 1), [Edit("D")]));

        Assert.Equal(2, details.ChosenRule);
    }

    [Fact]
    public void A_long_run_opens_with_its_groups_folded_but_for_the_chosen_rules()
    {
        var rules = new List<(RuleStats, EditLogEntry[])> { (Rule("G1", "Group"), []) };
        rules.AddRange(Enumerable.Range(0, 20).Select(i => (Rule("Idle " + i), Array.Empty<EditLogEntry>())));
        rules.Add((Rule("G2", "Group"), []));
        rules.Add((Rule("Changes", edits: 1), [Edit("D")]));
        rules.AddRange(Enumerable.Range(0, 20).Select(i => (Rule("Other " + i), Array.Empty<EditLogEntry>())));

        RunDetails details = Run([.. rules]);

        Assert.Equal([0], details.Folds);
        List<RuleLine> lines = details.Lines(RuleFilter.All, "");
        Assert.Equal(new RuleLine(0, IsGroup: true), lines[0]);
        Assert.Equal(new RuleLine(21, IsGroup: true), lines[1]);
        Assert.Equal(22, lines[2].Index);
    }

    [Fact]
    public void A_short_run_opens_with_nothing_folded()
    {
        RunDetails details = Run((Rule("G", "Group"), []), (Rule("A"), []));

        Assert.Empty(details.Folds);
    }

    [Fact]
    public void A_filter_lists_matching_rules_under_their_groups_whatever_is_folded()
    {
        RunDetails details = Run(
            (Rule("G1", "Group"), []), (Rule("Idle"), []), (Rule("Edits", edits: 1), [Edit("D")]),
            (Rule("G2", "Group"), []), (Rule("Also edits", edits: 2), [Edit("D")]));
        details.Folds.Add(0);

        Assert.Equal([new RuleLine(0, true), new RuleLine(1, false)], details.Lines(RuleFilter.ChangedNothing, ""));
        Assert.Equal(2, details.Lines(RuleFilter.All, "").Count(line => line.IsGroup));
        Assert.Equal(1, details.Count(RuleFilter.ChangedNothing));
        Assert.Equal(2, details.Count(RuleFilter.WithEdits));
        Assert.Equal(3, details.Count(RuleFilter.All));
    }

    [Fact]
    public void Group_variable_check_text_and_file_rules_are_not_counted_as_changing_nothing()
    {
        RunDetails details = Run((Rule("V", "Variable"), []), (Rule("C", "Check"), []), (Rule("T", "Text"), []), (Rule("F", "File"), []), (Rule("Off", enabled: false), []));

        Assert.Equal(0, details.Count(RuleFilter.ChangedNothing));
    }

    [Fact]
    public void Find_looks_in_rule_names_descriptors_targets_and_values_in_any_case()
    {
        RunDetails details = Run((Rule("Armour"), [Edit("Unit_Tank", "Speed", "100", "250")]), (Rule("Other"), [Edit("Unit_Plane", "Range", "7", "8")]));

        Assert.True(details.Shows(0, RuleFilter.All, "armour"));
        Assert.True(details.Shows(0, RuleFilter.All, "UNIT_TANK"));
        Assert.True(details.Shows(0, RuleFilter.All, "speed"));
        Assert.True(details.Shows(0, RuleFilter.All, "250"));
        Assert.False(details.Shows(1, RuleFilter.All, "250"));
    }

    [Fact]
    public void Find_narrows_the_edits_unless_the_rule_is_what_it_found()
    {
        RunDetails details = Run((Rule("Speed up"), [Edit("Unit_A"), Edit("Unit_B")]));

        Assert.Equal(2, details.EditsOf(0, "speed").Groups.Count);
        Assert.Equal("Unit_B", Assert.Single(details.EditsOf(0, "unit_b").Groups).Title);
    }

    [Fact]
    public void A_descriptor_inside_another_is_written_as_a_path_and_looked_for_by_its_own_name()
    {
        RunDetails details = Run((Rule("R"), [Edit("Unit_A > TModule", "> Inner > Value")]));

        EditGroup group = Assert.Single(details.EditsOf(0, "").Groups);
        Assert.Equal(("Unit_A › TModule", "Unit_A"), (group.Title, group.Name));
        Assert.Equal("Inner › Value", Assert.Single(group.Rows).Target);
    }

    [Fact]
    public void A_label_for_no_one_thing_in_the_file_has_no_name_to_go_to()
    {
        RunDetails details = Run((Rule("R"), [Edit("(constants)")]));

        Assert.Equal("", Assert.Single(details.EditsOf(0, "").Groups).Name);
    }

    [Fact]
    public void One_edit_of_one_target_on_many_descriptors_lists_a_line_each()
    {
        RunDetails details = Run((Rule("R"), [Edit("A"), Edit("B"), Edit("C")]));

        RuleEdits edits = details.EditsOf(0, "");
        Assert.Equal(EditListShape.OneTarget, edits.Shape);
        Assert.True(edits.HasChange);
    }

    [Fact]
    public void Several_edits_on_a_descriptor_list_under_its_heading()
    {
        RunDetails details = Run((Rule("R"), [Edit("A", "One"), Edit("A", "Two"), Edit("B", "One")]));

        RuleEdits edits = details.EditsOf(0, "");
        Assert.Equal(EditListShape.ByDescriptor, edits.Shape);
        Assert.Equal(2, edits.Groups.Count);
        Assert.Equal(2, edits.Groups[0].Rows.Count);
    }

    [Fact]
    public void One_note_for_each_descriptor_lists_a_line_each()
    {
        RunDetails details = Run((Rule("R"),
            [new EditLogEntry("A", "Remove", "", "Removed from the file.", IsNote: true), new EditLogEntry("B", "Remove", "", "Removed from the file.", IsNote: true)]));

        RuleEdits edits = details.EditsOf(0, "");
        Assert.Equal(EditListShape.OneNoteEach, edits.Shape);
        Assert.False(edits.HasChange);
    }

    [Fact]
    public void Warnings_show_on_their_rule_and_those_of_no_rule_are_kept_apart()
    {
        var record = new RunRecord
        {
            Result = new RunResult
            {
                Stats = [Rule("A"), Rule("B")],
                Logs = [[], []],
                Warnings = [new RunWarning("B", "Careful."), new RunWarning("Unit_X", "Elsewhere.")],
            },
            Info = Nothing,
            Milliseconds = 1,
            RuleSets = "",
        };

        var details = new RunDetails(record);

        Assert.Empty(details.WarningsOf(0));
        Assert.Equal("Careful.", Assert.Single(details.WarningsOf(1)).Message);
        Assert.Equal("Elsewhere.", Assert.Single(details.OtherWarnings).Message);
        Assert.Equal(1, details.Count(RuleFilter.Warnings));
    }

    [Fact]
    public void The_edit_log_keeps_what_changed_in_a_long_value_and_marks_what_it_left_out()
    {
        string before = string.Concat(Enumerable.Range(0, 300).Select(i => "Part" + i + ","));

        EditLogEntry entry = EditLogEntry.OfChange("D", "T", before + "Old" + before, before + "New" + before);

        Assert.StartsWith(EditLogEntry.LeftOutMark, entry.New);
        Assert.EndsWith(EditLogEntry.LeftOutMark, entry.New);
        Assert.Contains("New", entry.New);
        Assert.DoesNotContain("New", entry.Old);
        Assert.True(entry.New.Length < 240, entry.New);
    }

    [Fact]
    public void The_edit_log_keeps_short_values_whole()
    {
        EditLogEntry entry = EditLogEntry.OfChange("D", "T", "1.5", "2.5");

        Assert.Equal(("1.5", "2.5"), (entry.Old, entry.New));
        Assert.Equal(0, entry.NewLeftOut);
    }

    [Fact]
    public void The_edit_log_counts_what_it_left_out_of_a_very_long_change()
    {
        EditLogEntry entry = EditLogEntry.OfChange("D", "T", "a", new string('b', 5000));

        Assert.Equal(3000, entry.NewLeftOut);
        Assert.Equal(2000 + EditLogEntry.LeftOutMark.Length, entry.New.Length);
    }

    [Fact]
    public void Two_mods_leaving_different_long_values_conflict_even_when_what_they_logged_reads_alike()
    {
        string start = new('x', 400);
        (string, int, EditLogEntry) First() => ("R", 0, EditLogEntry.OfChange("D", "T", start + "old" + start, start + "one" + start));
        (string, int, EditLogEntry) Second() => ("R", 0, EditLogEntry.OfChange("D", "T", start + "one" + start, start + "two" + start));
        (string, int, EditLogEntry) Same() => ("R", 0, EditLogEntry.OfChange("D", "T", start + "old" + start, start + "one" + start));

        Assert.Single(ModConflictFinder.Find("F.ndf", [("A", [First()]), ("B", [Second()])]));
        Assert.Empty(ModConflictFinder.Find("F.ndf", [("A", [First()]), ("B", [Same()])]));
    }
}
