using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// A Lookup rule can apply to each object of one type or to the constants, as an Edit rule can; a Table rule's own
/// conditions keep its rows from descriptors that do not meet them.
/// </summary>
public class LookupScopeAndTableConditionTests
{
    private const string Set = "Set To [int, float, string, boolean]";

    private const string Units =
        "export Unit_A is TUnit\n(\n    Side = 1\n    Modules = [\n        TWeaponModule( Weapon = $/GFX/Weapon/Weapon_X  Cost = 10 ),\n        TArmorModule( Cost = 10 ),\n    ]\n)\n" +
        "export Unit_B is TUnit\n(\n    Side = 2\n    Modules = [\n        TWeaponModule( Weapon = $/GFX/Weapon/Weapon_Y  Cost = 10 ),\n    ]\n)\n";

    private const string Weapons = "export Weapon_X is TWeapon\n(\n    Range = 5\n)\nexport Weapon_Z is TWeapon\n(\n    Range = 7\n)\n";

    private static string RunLookup(Dictionary<string, System.Text.Json.Nodes.JsonNode?> parameters)
    {
        var engine = new NdfEngine();
        engine.Load(Units);
        parameters["SourceFile"] = "Weapons.ndf";
        parameters["Collect"] = "Descriptor Title";
        return engine.Run([new Rule
        {
            Name = "lookup", Kind = "Lookup", Params = new(parameters),
            Actions = [new RuleAction { Target = "Cost", Operator = Set, Value = "99" }],
        }], new RunOptions { OtherFiles = new Dictionary<string, string> { ["weapons.ndf"] = Weapons } }).Text;
    }

    [Fact]
    public void Applied_to_each_descriptor_it_edits_the_descriptor_that_names_a_collected_value()
    {
        string text = RunLookup(new() { ["MatchOn"] = "Weapon" });
        // Unit_A names Weapon_X: every Cost in it is set (the target is found at any depth), Unit_B's is left
        Assert.Equal(2, text.Split("Cost = 99").Length - 1);
        Assert.Equal(1, text.Split("Cost = 10").Length - 1);
    }

    [Fact]
    public void Applied_to_each_object_of_one_type_it_edits_only_the_object_that_names_a_collected_value()
    {
        string text = RunLookup(new() { ["MatchOn"] = "Weapon", ["Scope"] = "type", ["ScopeType"] = "TWeaponModule" });
        // only Unit_A's weapon module: its armour module and Unit_B's weapon module keep 10
        Assert.Equal(1, text.Split("Cost = 99").Length - 1);
        Assert.Equal(2, text.Split("Cost = 10").Length - 1);
    }

    [Fact]
    public void A_table_rule_with_no_conditions_applies_every_row()
    {
        Assert.Equal(2, RunTable().Split("Side = 9").Length - 1);
    }

    [Fact]
    public void A_table_rules_conditions_keep_its_rows_from_descriptors_that_do_not_meet_them()
    {
        string text = RunTable(new Condition { Operand = "Side", Operator = "Is Equal To", Value = "1" });
        Assert.Equal(1, text.Split("Side = 9").Length - 1);
        Assert.Contains("Side = 2", text);
    }

    private static string RunTable(params Condition[] conditions)
    {
        var engine = new NdfEngine();
        engine.Load(Units);
        return engine.Run([new Rule
        {
            Name = "table", Kind = "Table", Conditions = [.. conditions],
            Params = new() { ["Op"] = "Set To", ["Text"] = "Name,Side\nUnit_A,9\nUnit_B,9" },
        }], new RunOptions()).Text;
    }
}
