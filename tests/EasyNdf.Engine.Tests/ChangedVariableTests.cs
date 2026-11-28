using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// Variables a rule's action changes, and variables shared between rule sets: an action aimed at "{Name}" changes the
/// variable for the rules after it, once for the rule or once for each match; each rule set of a run begins with its
/// own variables and the shared ones as they were given.
/// </summary>
public class ChangedVariableTests
{
    private const string File = "export Unit_A is TUnit\n(\n    Speed = 10\n)\nexport Unit_B is TUnit\n(\n    Speed = 20\n)\nexport Tank_C is TUnit\n(\n    Speed = 30\n)\n";

    private const string Set = "Set To [int, float, string, boolean]", Add = "Add [int, float]", Multiply = "Multiply [int, float]";

    private static Rule Variable(string name, string value, string scope = "RuleSet") =>
        new() { Name = name, Kind = "Variable", Params = new() { ["VarName"] = name, ["Value"] = value, ["VarScope"] = scope } };

    private static Rule Edit(string titleContains, params RuleAction[] actions) => new()
    {
        Name = "edit " + titleContains,
        Kind = "Edit",
        LogicGate = titleContains == "" ? "All" : "Any",
        Conditions = titleContains == "" ? [] : [new Condition { Operand = "Descriptor Title", Operator = "Contains", Value = titleContains }],
        Actions = [.. actions],
    };

    private static RuleAction Act(string target, string op, string value, bool perMatch = false) =>
        new() { Target = target, Operator = op, Value = value, PerMatch = perMatch };

    private static RunResult Run(List<Rule?> rules, RunOptions? options = null)
    {
        var engine = new NdfEngine();
        engine.Load(File);
        return engine.Run(rules, options ?? new RunOptions());
    }

    [Fact]
    public void An_action_aimed_at_a_variable_changes_it_for_the_rules_after()
    {
        RunResult result = Run([Variable("Bonus", "5"), Edit("Unit_A", Act("{Bonus}", Add, "2")), Edit("Unit_B", Act("Speed", Set, "{Bonus}"))]);

        Assert.Equal("7", result.Variables["Bonus"]);
        Assert.Contains("Speed = 7", result.Text);
        // the rule before the change is not touched by it, and nothing is written for the variable itself
        Assert.Contains("Speed = 10", result.Text);
        Assert.Equal(1, result.Edits);
    }

    [Fact]
    public void Once_for_the_rule_however_many_descriptors_match()
    {
        RunResult result = Run([Variable("Factor", "2"), Edit("Unit_", Act("{Factor}", Multiply, "1.5"))]);

        Assert.Equal("3", result.Variables["Factor"]);
    }

    [Fact]
    public void For_each_match_when_the_action_says_so()
    {
        RunResult result = Run([Variable("Count", "0"), Edit("Unit_", Act("{Count}", Add, "1", perMatch: true))]);

        Assert.Equal("2", result.Variables["Count"]);
    }

    [Fact]
    public void A_rule_that_matches_nothing_leaves_the_variable()
    {
        RunResult result = Run([Variable("Bonus", "5"), Edit("Nothing_Has_This", Act("{Bonus}", Set, "9"))]);

        Assert.Equal("5", result.Variables["Bonus"]);
    }

    [Fact]
    public void A_rule_with_no_conditions_changes_the_variable_once_whatever_its_gate()
    {
        Rule rule = Edit("", Act("{Bonus}", Set, "9"));
        rule.LogicGate = "Any";
        RunResult result = Run([Variable("Bonus", "5"), rule]);

        Assert.Equal("9", result.Variables["Bonus"]);
        Assert.Equal(0, result.Edits);
    }

    [Fact]
    public void Arithmetic_on_a_variable_that_is_not_a_number_is_skipped_with_a_note()
    {
        RunResult result = Run([Variable("Name", "'Tank'"), Edit("Unit_A", Act("{Name}", Add, "1"))]);

        Assert.Equal("'Tank'", result.Variables["Name"]);
        Assert.Contains(result.Logs[1], entry => entry.IsNote && entry.New.Contains("is not a number"));
    }

    [Fact]
    public void A_target_in_braces_that_is_no_variable_is_a_property_name_as_before()
    {
        RunResult result = Run([Edit("Unit_A", Act("{NotAVariable}", Set, "1"))]);

        Assert.Empty(result.Variables);
    }

    [Fact]
    public void Each_rule_set_begins_with_the_shared_values_given_and_its_own_variables()
    {
        // two rule sets in one run: the first has its own "Local"; the second must not see it, and sees Shared as given
        List<Rule?> rules =
        [
            Variable("Local", "111"),
            Edit("Unit_A", Act("Speed", Set, "{Local}")),
            Edit("Unit_B", Act("Speed", Set, "{Local}"), Act("{Shared}", Add, "1")),
            Edit("Tank_C", Act("Speed", Set, "{Shared}")),
        ];
        RunResult result = Run(rules, new RunOptions
        {
            Segments =
            [
                new RuleSegment(0, new Dictionary<string, string> { ["Shared"] = "40" }),
                new RuleSegment(2, new Dictionary<string, string> { ["Shared"] = "50" }),
            ],
        });

        Assert.Contains("Speed = 111", result.Text);
        // "{Local}" is left as written where no variable of that name is in reach
        Assert.Contains("Speed = {Local}", result.Text);
        Assert.Contains("Speed = 51", result.Text);
        Assert.Equal("51", result.Variables["Shared"]);
        Assert.False(result.Variables.ContainsKey("Local"));
    }

    [Fact]
    public void A_shared_variables_own_rule_does_not_reset_the_value_given()
    {
        List<Rule?> rules = [Variable("Shared", "1", scope: "Mod"), Edit("Unit_A", Act("Speed", Set, "{Shared}"))];
        RunResult result = Run(rules, new RunOptions { Segments = [new RuleSegment(0, new Dictionary<string, string> { ["Shared"] = "77" })] });

        Assert.Contains("Speed = 77", result.Text);
    }

    [Fact]
    public void A_group_switched_off_in_one_rule_set_does_not_reach_into_the_next()
    {
        List<Rule?> rules =
        [
            new Rule { Name = "off", Kind = "Group", Enabled = false },
            Edit("Unit_A", Act("Speed", Set, "1")),
            Edit("Unit_B", Act("Speed", Set, "2")),
        ];
        var none = new Dictionary<string, string>();
        RunResult result = Run(rules, new RunOptions { Segments = [new RuleSegment(0, none), new RuleSegment(2, none)] });

        Assert.Contains("Speed = 10", result.Text);
        Assert.Contains("Speed = 2", result.Text);
    }
}
