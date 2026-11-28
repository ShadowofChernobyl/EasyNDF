using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// A condition's property can be a path, as an action's target can: "Outer &gt; Inner" tests only the Inner found
/// inside Outer, "&gt; A &gt; B" starts at the descriptor's own properties, and "[Key]" picks one entry of a list.
/// A plain name is still found at any depth.
/// </summary>
public class ConditionPathTests
{
    private const string Text =
        "export Unit_A is TUnit\n(\n    Damage = TDamage( Index = 3 )\n    Squad = TSquad( Index = 5 )\n    Speed = 10\n)\n" +
        "export Unit_B is TUnit\n(\n    Damage = TDamage( Index = 5 )\n    Squad = TSquad( Index = 3 Inner = TInner( Index = 7 ) )\n    Speed = 10\n)\n" +
        "export Costs is TCosts\n(\n    Matrix = MAP [\n        ( EFactory/Tanks, TCost( Points = 40 ) ),\n        ( EFactory/Infantry, TCost( Points = 10 ) ),\n    ]\n    Speed = 10\n)\n";

    /// <summary>The descriptors a condition picks: each gets Speed + 1, and the titles of those that changed come back.</summary>
    private static List<string> Picked(string operand, string op, string value)
    {
        var engine = new NdfEngine();
        engine.Load(Text);
        var rule = new Rule
        {
            Name = "test",
            Kind = "Edit",
            LogicGate = "All",
            Conditions = [new Condition { Operand = operand, Operator = op, Value = value }],
            Actions = [new RuleAction { Target = "> Speed", Operator = "Add", Value = "1" }],
        };
        RunResult result = engine.Run([rule], new RunOptions { WantText = true });
        var picked = new List<string>();
        foreach (string title in new[] { "Unit_A", "Unit_B", "Costs" })
        {
            int at = result.Text!.IndexOf("export " + title + " ", StringComparison.Ordinal);
            int next = result.Text.IndexOf("export ", at + 1, StringComparison.Ordinal);
            string block = next < 0 ? result.Text[at..] : result.Text[at..next];
            if (block.Contains("Speed = 11")) picked.Add(title);
        }
        return picked;
    }

    [Fact]
    public void A_plain_name_still_matches_at_any_depth() =>
        Assert.Equal(["Unit_A", "Unit_B"], Picked("Index", "Is Equal To", "3"));

    [Fact]
    public void A_path_tests_only_the_property_inside_the_one_named() =>
        Assert.Equal(["Unit_A"], Picked("Damage > Index", "Is Equal To", "3"));

    [Fact]
    public void A_path_finds_its_last_part_at_any_depth_below_the_one_named() =>
        Assert.Equal(["Unit_B"], Picked("Squad > Index", "Is Equal To", "7"));

    [Fact]
    public void An_anchored_path_goes_no_deeper_than_its_last_part() =>
        Assert.Empty(Picked("> Squad > Index", "Is Equal To", "7"));

    [Fact]
    public void An_anchored_path_finds_the_descriptors_own_property() =>
        Assert.Equal(["Unit_B"], Picked("> Squad > Index", "Is Equal To", "3"));

    [Fact]
    public void A_key_in_square_brackets_picks_one_entry_of_a_list()
    {
        Assert.Equal(["Costs"], Picked("[EFactory/Tanks] > Points", "Is Greater Than", "30"));
        Assert.Empty(Picked("[EFactory/Infantry] > Points", "Is Greater Than", "30"));
    }

    [Fact]
    public void An_item_of_a_list_can_end_a_path()
    {
        var engine = new NdfEngine();
        engine.Load("export U is TUnit\n(\n    Turret = TTurret( Salves = [4, 8] )\n    Other = TOther( Salves = [8, 4] )\n    Speed = 10\n)\n");
        var rule = new Rule
        {
            Name = "test", Kind = "Edit", LogicGate = "All",
            Conditions = [new Condition { Operand = "Turret > Salves[0]", Operator = "Is Equal To", Value = "8" }],
            Actions = [new RuleAction { Target = "> Speed", Operator = "Add", Value = "1" }],
        };
        Assert.Equal(0, engine.Run([rule], new RunOptions()).Edits);
    }
}
