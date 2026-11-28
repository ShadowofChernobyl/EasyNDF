using System.Text.Json.Nodes;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The actions a division change needs, on made-up text laid out as the game's division files: a path to one cost
/// matrix's key, Set To on a whole list, a property added where it is missing, and a unit's line taken out of a
/// division's list.
/// </summary>
public class DivisionActionTests
{
    private static readonly string DivisionRules = """
        export Descriptor_Deck_Division_Alpha_Rule is TDeckDivisionRule
        (
            UnitRuleList =
            [
                // tanks
                TDeckUniteRule
                (
                    UnitDescriptor = $/GFX/Unit/Descriptor_Unit_Tank
                    AvailableWithoutTransport = True
                    MaxPackNumber = 2 // two cards
                ),
                TDeckUniteRule
                (
                    UnitDescriptor = $/GFX/Unit/Descriptor_Unit_Squad
                    AvailableWithoutTransport = False
                    AvailableTransportList = [ $/GFX/Unit/Descriptor_Unit_Truck ]
                    MaxPackNumber = 3
                ),
            ]
        )

        """.ReplaceLineEndings("\n");

    private static readonly string CostMatrix = """
        MatrixCostName_Alpha is MAP [
            (EProductionFactory/Tanks, [1, 1, 2]),
            (EProductionFactory/Logistic, [1, 2]),
        ]
        MatrixCostName_Beta is MAP [
            (EProductionFactory/Tanks, [1, 1, 1]),
        ]

        """.ReplaceLineEndings("\n");

    private static RunResult Run(string text, Rule rule)
    {
        var engine = new NdfEngine();
        engine.Load(text);
        return engine.Run([rule], new RunOptions());
    }

    private static Rule Rule(string kind, Condition? condition, RuleAction? action, Dictionary<string, JsonNode?>? parameters = null) => new()
    {
        Name = "test",
        Kind = kind,
        LogicGate = "All",
        Conditions = condition is null ? [] : [condition],
        Actions = action is null ? [] : [action],
        Params = parameters,
    };

    private static Dictionary<string, JsonNode?> EachObjectOf(string type) => new()
    {
        ["Scope"] = "type",
        ["ScopeType"] = type,
    };

    private static Condition UnitIs(string unit) =>
        new() { Operand = "UnitDescriptor", Operator = "Is Equal To", Value = "$/GFX/Unit/" + unit };

    [Fact]
    public void A_path_sets_one_matrix_key_and_leaves_the_same_key_in_other_matrices()
    {
        var action = new RuleAction { Target = "MatrixCostName_Alpha > EProductionFactory/Tanks", Operator = "Set To [list, object]", Value = "[1, 2, 3, 4]" };
        var rule = Rule("Edit", null, action, new() { ["Scope"] = "constants" });

        var result = Run(CostMatrix, rule);

        Assert.Contains("(EProductionFactory/Tanks, [1, 2, 3, 4]),\n    (EProductionFactory/Logistic, [1, 2]),", result.Text);
        Assert.Contains("(EProductionFactory/Tanks, [1, 1, 1]),", result.Text); // Beta is left alone
        Assert.Equal(1, result.Edits);
    }

    [Fact]
    public void Set_To_list_writes_a_whole_list()
    {
        var action = new RuleAction { Target = "AvailableTransportList", Operator = "Set To [list, object]", Value = "[ $/GFX/Unit/Descriptor_Unit_Jeep ]" };
        var rule = Rule("Edit", UnitIs("Descriptor_Unit_Squad"), action, EachObjectOf("TDeckUniteRule"));

        var result = Run(DivisionRules, rule);

        Assert.Contains("AvailableTransportList = [ $/GFX/Unit/Descriptor_Unit_Jeep ]\n", result.Text);
    }

    [Fact]
    public void Plain_Set_To_leaves_a_list_alone_as_the_HTML_app_does()
    {
        var action = new RuleAction { Target = "AvailableTransportList", Operator = "Set To [int, float, string, boolean]", Value = "[]" };
        var rule = Rule("Edit", UnitIs("Descriptor_Unit_Squad"), action, EachObjectOf("TDeckUniteRule"));

        var result = Run(DivisionRules, rule);

        Assert.Equal(DivisionRules, result.Text);
    }

    [Fact]
    public void Add_Property_adds_a_line_under_the_last_property_and_keeps_its_comment()
    {
        var action = new RuleAction { Target = "AvailableTransportList", Operator = "Add Property", Value = "[ $/GFX/Unit/Descriptor_Unit_Truck ]" };
        var rule = Rule("Edit", UnitIs("Descriptor_Unit_Tank"), action, EachObjectOf("TDeckUniteRule"));

        var result = Run(DivisionRules, rule);

        Assert.Contains(
            "            MaxPackNumber = 2 // two cards\n" +
            "            AvailableTransportList = [ $/GFX/Unit/Descriptor_Unit_Truck ]\n" +
            "        ),\n", result.Text);
        Assert.Equal(1, result.Edits);
    }

    [Fact]
    public void Add_Property_leaves_a_property_that_is_there_already()
    {
        var action = new RuleAction { Target = "AvailableTransportList", Operator = "Add Property", Value = "[]" };
        var rule = Rule("Edit", UnitIs("Descriptor_Unit_Squad"), action, EachObjectOf("TDeckUniteRule"));

        var result = Run(DivisionRules, rule);

        Assert.Equal(DivisionRules, result.Text);
        Assert.Equal(0, result.Edits);
    }

    [Fact]
    public void Add_Property_works_on_a_one_line_object()
    {
        var action = new RuleAction { Target = "B", Operator = "Add Property", Value = "2" };
        var result = Run("X is TThing ( A = 1 )\n", Rule("Edit", null, action));

        Assert.Equal("X is TThing ( A = 1 B = 2 )\n", result.Text);
    }

    [Fact]
    public void Add_Property_keeps_Windows_line_breaks()
    {
        string text = "X is TThing\r\n(\r\n    A = 1\r\n)\r\n";
        var action = new RuleAction { Target = "B", Operator = "Add Property", Value = "2" };

        var result = Run(text, Rule("Edit", null, action));

        Assert.Equal("X is TThing\r\n(\r\n    A = 1\r\n    B = 2\r\n)\r\n", result.Text);
    }

    [Fact]
    public void Remove_on_a_type_takes_a_unit_out_of_its_division_with_its_comment_kept()
    {
        var rule = Rule("Remove", UnitIs("Descriptor_Unit_Tank"), null, EachObjectOf("TDeckUniteRule"));

        var result = Run(DivisionRules, rule);

        Assert.DoesNotContain("Descriptor_Unit_Tank", result.Text);
        Assert.Contains("Descriptor_Unit_Squad", result.Text);
        Assert.Contains("// tanks", result.Text);
        Assert.Equal(1, result.Edits);
    }

    [Fact]
    public void Remove_on_a_type_leaves_an_object_that_is_not_in_a_list()
    {
        string text = "X is TThing\n(\n    Inner = TPart ( A = 1 )\n)\n";
        var rule = Rule("Remove", new Condition { Operand = "A", Operator = "Is Equal To", Value = "1" }, null, EachObjectOf("TPart"));

        var result = Run(text, rule);

        Assert.Equal(text, result.Text);
        Assert.Equal(0, result.Edits);
    }
}
