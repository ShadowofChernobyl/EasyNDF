using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>A condition marked Must be true has to pass even when the rule needs only any one of its conditions.</summary>
public class MustBeTrueTests
{
    private const string File = "export Unit_A is TUnit\n(\n    Speed = 10\n    Side = 1\n)\nexport Unit_B is TUnit\n(\n    Speed = 20\n    Side = 2\n)\nexport Tank_C is TUnit\n(\n    Speed = 30\n    Side = 1\n)\n";

    private static Condition On(string operand, string op, string value, bool must = false) => new() { Operand = operand, Operator = op, Value = value, MustBeTrue = must };

    private static string Run(string gate, params Condition[] conditions)
    {
        var engine = new NdfEngine();
        engine.Load(File);
        return engine.Run([new Rule
        {
            Name = "r", Kind = "Edit", LogicGate = gate, Conditions = [.. conditions],
            Actions = [new RuleAction { Target = "Speed", Operator = "Set To [int, float, string, boolean]", Value = "99" }],
        }], new RunOptions()).Text;
    }

    private static int Changed(string text) => text.Split("Speed = 99").Length - 1;

    [Fact]
    public void With_Any_a_condition_that_must_be_true_is_needed_beside_one_of_the_others()
    {
        // Side 1 always; then either title: Unit_A (Side 1) and Tank_C (Side 1) pass, Unit_B (Side 2) does not
        string text = Run("Any", On("Side", "Is Equal To", "1", must: true), On("Descriptor Title", "Contains", "Unit_"), On("Descriptor Title", "Contains", "Tank_"));
        Assert.Equal(2, Changed(text));
        Assert.Contains("Speed = 20", text);
    }

    [Fact]
    public void Without_the_mark_Any_takes_one_condition_alone()
    {
        Assert.Equal(3, Changed(Run("Any", On("Side", "Is Equal To", "1"), On("Descriptor Title", "Contains", "Unit_"))));
    }

    [Fact]
    public void A_title_condition_that_must_be_true_rules_the_others_out()
    {
        string text = Run("Any", On("Descriptor Title", "Contains", "Unit_", must: true), On("Side", "Is Equal To", "1"), On("Speed", "Is Equal To", "30"));
        Assert.Equal(1, Changed(text));
        Assert.Contains("Speed = 30", text);
    }

    [Fact]
    public void Only_conditions_that_must_be_true_are_all_needed()
    {
        Assert.Equal(1, Changed(Run("Any", On("Side", "Is Equal To", "1", must: true), On("Descriptor Title", "Contains", "Unit_", must: true))));
    }

    [Fact]
    public void With_All_the_mark_changes_nothing()
    {
        Assert.Equal(1, Changed(Run("All", On("Side", "Is Equal To", "1", must: true), On("Descriptor Title", "Contains", "Unit_"))));
    }
}
