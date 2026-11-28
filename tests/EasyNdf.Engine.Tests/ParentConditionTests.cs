using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// "Parent:" in a condition's operand: a rule that applies to each object of one type can test the descriptor the
/// object is in ("every armor module of an infantry unit"); with any other rule it tests the descriptor itself.
/// </summary>
public class ParentConditionTests
{
    private const string Text =
        "export Unit_Rifles is TEntityDescriptor\n(\n    Modules = [\n        TTags( TagSet = ['Infanterie', 'Ground'] ),\n        TDamage( Index = 1 ),\n    ]\n)\n" +
        "export Unit_Tank is TEntityDescriptor\n(\n    Modules = [\n        TTags( TagSet = ['Char', 'Ground'] ),\n        TDamage( Index = 1 ),\n    ]\n)\n";

    private static string Run(string operand, string op, string value, bool typeScope = true)
    {
        var engine = new NdfEngine();
        engine.Load(Text);
        var rule = new Rule
        {
            Name = "test",
            Kind = "Edit",
            LogicGate = "All",
            Conditions = [new Condition { Operand = operand, Operator = op, Value = value }],
            Actions = [new RuleAction { Target = "Index", Operator = "Set To", Value = "9" }],
            Params = typeScope ? new() { ["Scope"] = "type", ["ScopeType"] = "TDamage" } : null,
        };
        return engine.Run([rule], new RunOptions { WantText = true }).Text!;
    }

    private static bool Changed(string text, string unit)
    {
        int at = text.IndexOf("export " + unit, StringComparison.Ordinal);
        int next = text.IndexOf("export ", at + 1, StringComparison.Ordinal);
        return (next < 0 ? text[at..] : text[at..next]).Contains("Index = 9", StringComparison.Ordinal);
    }

    [Fact]
    public void An_object_of_a_type_is_picked_by_its_descriptors_property()
    {
        string text = Run("Parent: TagSet", "Contains", "Infanterie");
        Assert.True(Changed(text, "Unit_Rifles"));
        Assert.False(Changed(text, "Unit_Tank"));
    }

    [Fact]
    public void Without_Parent_the_object_alone_is_tested_as_before() =>
        Assert.False(Changed(Run("TagSet", "Contains", "Infanterie"), "Unit_Rifles"));

    [Fact]
    public void Parent_reads_the_descriptors_title_and_its_entire_text()
    {
        Assert.True(Changed(Run("Parent: Descriptor Title", "Is Equal To", "Unit_Tank"), "Unit_Tank"));
        Assert.True(Changed(Run("Parent: Entire Descriptor", "Contains", "'Char'"), "Unit_Tank"));
        Assert.False(Changed(Run("Parent: Entire Descriptor", "Contains", "'Char'"), "Unit_Rifles"));
    }

    [Fact]
    public void With_a_rule_on_whole_descriptors_Parent_is_the_descriptor_itself()
    {
        string text = Run("Parent: TagSet", "Contains", "Infanterie", typeScope: false);
        Assert.True(Changed(text, "Unit_Rifles"));
        Assert.False(Changed(text, "Unit_Tank"));
    }
}
