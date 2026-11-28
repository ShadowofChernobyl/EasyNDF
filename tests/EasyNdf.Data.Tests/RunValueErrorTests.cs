using EasyNdf.Data.FileList;
using EasyNdf.Engine;
using EasyNdf.Engine.Editing;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

/// <summary>
/// The values an export would write that stop mod generation, put on the rules and edits that wrote them, and how
/// Details lists them: the Errors filter, the rule it opens on, and the errors with no line of their own.
/// </summary>
public class RunValueErrorTests
{
    private static ExportValueError Error(string descriptor, string name, string value, int line = 10) =>
        new(descriptor, line, 0, new ValueProblem(0, value.Length, ValueProblemKind.OutsideLimit, name, value));

    private static EditLogEntry Edit(string descriptor, string target, string now) => new(descriptor, target, "1", now);

    private static RuleStats Rule(string name, int edits = 1) => new() { Name = name, Kind = "Edit", Enabled = true, Matched = 1, Edits = edits };

    private static RunDetails Details(List<List<EditLogEntry>> logs, List<RunValueError> errors) => new(new RunRecord
    {
        Result = new RunResult { Stats = [.. logs.Select((_, index) => Rule("Rule " + index, logs[index].Count))], Logs = logs, Warnings = [] },
        Info = new LoadSummary(0, 0, 0, 0, 0, [], [], [], []),
        Milliseconds = 1,
        RuleSets = "",
        ValueErrors = errors,
    });

    [Fact]
    public void A_value_is_put_on_the_last_rule_that_wrote_it_as_a_later_mods_edit_is_what_stays()
    {
        List<List<EditLogEntry>> logs = [[Edit("Ammo_A", "AngleDispersion", "0.9")], [Edit("Ammo_A", "Name", "'X'")], [Edit("Ammo_A", "AngleDispersion", "0.9")]];

        RunValueError placed = Assert.Single(RunValueError.Place(logs, [Error("Ammo_A", "AngleDispersion", "0.9")]));

        Assert.Equal((2, 0), (placed.Rule, placed.Edit));
    }

    [Fact]
    public void Of_edits_of_the_name_the_one_that_wrote_the_value_is_chosen()
    {
        List<List<EditLogEntry>> logs = [[Edit("Ammo_A", "AngleDispersion", "0.9")], [Edit("Ammo_A", "AngleDispersion", "0.5")]];

        RunValueError placed = Assert.Single(RunValueError.Place(logs, [Error("Ammo_A", "AngleDispersion", "0.9")]));

        Assert.Equal((0, 0), (placed.Rule, placed.Edit));
    }

    [Fact]
    public void Values_of_one_name_written_in_several_places_take_a_rules_edits_in_order()
    {
        // one rule setting the name in each of a descriptor's turrets, logged under the objects that hold them
        List<List<EditLogEntry>> logs = [[Edit("Unit_A > TTurret", "> Turrets > YulBoneOrdinal", "-2"), Edit("Unit_A > TTurret", "> Turrets > YulBoneOrdinal", "-2")]];

        List<RunValueError> placed = RunValueError.Place(logs, [Error("Unit_A", "YulBoneOrdinal", "-2", 20), Error("Unit_A", "YulBoneOrdinal", "-2", 30)]);

        Assert.Equal([0, 1], placed.Select(error => error.Edit));
    }

    [Fact]
    public void A_value_no_edit_names_goes_on_the_last_rule_that_edited_its_descriptor_and_one_no_rule_touched_on_none()
    {
        // a clone keeps its source's values: only the Clone rule's note names the new descriptor
        List<List<EditLogEntry>> logs = [[new EditLogEntry("Ammo_B", "Clone", "", "Cloned from Ammo_A.", IsNote: true)], [Edit("Ammo_B", "Name", "'B'")]];

        List<RunValueError> placed = RunValueError.Place(logs, [Error("Ammo_B", "AngleDispersion", "0.9"), Error("Ammo_C", "AngleDispersion", "0.9")]);

        Assert.Equal((1, -1), (placed[0].Rule, placed[0].Edit));
        Assert.Equal((-1, -1), (placed[1].Rule, placed[1].Edit));
    }

    [Fact]
    public void A_long_value_the_log_kept_only_part_of_is_not_compared()
    {
        List<List<EditLogEntry>> logs = [[Edit("Ammo_A", "Hits", "…0.9…")], [Edit("Ammo_A", "Hits", "[0.5]")]];

        RunValueError placed = Assert.Single(RunValueError.Place(logs, [Error("Ammo_A", "Hits", "…0.9…")]));

        Assert.Equal(1, placed.Rule);
    }

    [Fact]
    public void Details_lists_the_rules_with_errors_opens_on_the_first_and_marks_their_edits()
    {
        List<List<EditLogEntry>> logs = [[Edit("Ammo_A", "Name", "'A'")], [Edit("Ammo_A", "AngleDispersion", "0.9")], [Edit("Ammo_B", "Name", "'B'")]];
        List<RunValueError> errors = RunValueError.Place(logs, [Error("Ammo_A", "AngleDispersion", "0.9"), Error("Ammo_B", "Count", "0"), Error("Ammo_Z", "Count", "0")]);

        RunDetails details = Details(logs, errors);

        Assert.Equal(2, details.Count(RuleFilter.Errors));
        Assert.Equal(1, details.ChosenRule);
        Assert.Same(errors[0], details.ErrorAt(logs[1][0]));
        Assert.Null(details.ErrorAt(logs[0][0]));
        Assert.Empty(details.UnlistedErrorsOf(1));
        Assert.Same(errors[1], Assert.Single(details.UnlistedErrorsOf(2)));
        Assert.Same(errors[2], Assert.Single(details.OtherErrors));
    }

    [Fact]
    public void An_error_past_the_edits_the_log_kept_has_no_line_but_stays_on_its_rule()
    {
        List<List<EditLogEntry>> logs = [[Edit("Ammo_A", "Count", "0")]];
        var error = new RunValueError(Error("Ammo_A", "Count", "0"), 0, 5);

        RunDetails details = Details(logs, [error]);

        Assert.Equal(1, details.Count(RuleFilter.Errors));
        Assert.Same(error, Assert.Single(details.UnlistedErrorsOf(0)));
    }

    [Fact]
    public void Lines_below_a_header_count_it()
    {
        RunValueError error = new(new ExportValueError("A", 10, 4, new ValueProblem(0, 1, ValueProblemKind.WrittenTwice, "Count", "1")), 0, 0);

        Assert.Equal((13, 7), (error.Below(3).Error.Line, error.Below(3).Error.FirstLine));
    }
}
