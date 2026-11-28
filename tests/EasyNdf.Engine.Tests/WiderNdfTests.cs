using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The parts of the NDF language that the usual gameplay files do not use, but other WARNO and Steel Division 2 files
/// do (effects, interface, templates): each file must come back unchanged with no rules, and rules must still edit
/// the plain values around them. The texts are made up, in the games' style.
/// </summary>
public class WiderNdfTests
{
    private static RunResult Run(string text, params Rule[] rules)
    {
        var engine = new NdfEngine();
        engine.Load(text);
        return engine.Run([.. rules], new RunOptions());
    }

    private static Rule Edit(string title, string target, string op, string value) => new()
    {
        Name = "test",
        Kind = "Edit",
        LogicGate = "All",
        Conditions = [new Condition { Operand = "Descriptor Title", Operator = "Starts With", Value = title }],
        Actions = [new RuleAction { Target = target, Operator = op, Value = value }],
    };

    /// <summary>Each text uses one part of the language that the engine once could not read.</summary>
    public static TheoryData<string, string> Texts => new()
    {
        { "{ } comment", "export Module is TModule\n(\n    Effects = [\n        TEffect(Roll=(1,10) TimeOut=10),\n        {TEffect(Roll=(11,20) TimeOut=30),}\n    ]\n)\n" },
        { "DIV in capitals", "export Constants is TConstants\n(\n    Distance = 8000 * Metre\n    Bonus = 1.0 DIV Metre DIV 100.0\n)\n" },
        { "condition", "export Button is TButton\n(\n    Sound = HasSound ? ButtonSound : nil\n    Delay = <Delay> != nil & <Range> < 6500.0\n)\n" },
        { "not", "export Anim is TAnim\n(\n    Loops = !<Loop> & !<Final>\n)\n" },
        { "private entry in a list", "export Bank is TBank\n(\n    Params =\n    [\n        private parDirection is TActionAlias( ExpectedType = 'float3' DefaultValue = float3[0,0,0] ),\n    ]\n)\n" },
        { "export member in an object", "export PathTypes is TBaseClass\n(\n    export Flying is 0\n    export Amphibious is 1\n)\n" },
        { "member and index", "private Width is ~/Panel.ViewDescriptor.WidthHeight[0]\nprivate Margin is Grid.InterElementMargin.Magnifiable[1]\n" },
        { "Name: type", "export Power is TPinnableValue(ExpectedType: float InitialValue = 5)\n" },
        { "doubled comma", "export Tags is TTags\n(\n    Priority = [\n        \"Tank\",\n        \"Canon\",,\n    ]\n)\n" },
        { "#ifdef", "#ifdef EUG_PC\nexport Selection is TSelection ( Mouse = True )\n#endif\n" },
        { "Template in capitals", "public Template TemplateBehaviour\n[\n    Speed : float = 1.0,\n]\nis TBehaviour\n(\n    Speed = <Speed>\n)\n" },
    };

    [Theory]
    [MemberData(nameof(Texts))]
    public void With_no_rules_the_file_comes_back_unchanged(string part, string text)
    {
        var result = Run(text);
        Assert.True(result.ParseError == "", part + ": " + result.ParseError);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void A_rule_edits_values_beside_a_brace_comment_and_leaves_the_comment_alone()
    {
        string text = "export Module is TModule\n(\n    Effects = [\n        TEffect(Roll=(1,10) TimeOut=10),\n        {TEffect(Roll=(11,20) TimeOut=30),}\n    ]\n)\n";
        var result = Run(text, Edit("Module", "TimeOut", "Add", "5"));
        Assert.Contains("TEffect(Roll=(1,10) TimeOut=15)", result.Text);
        Assert.Contains("{TEffect(Roll=(11,20) TimeOut=30),}", result.Text);
        Assert.Equal(1, result.Edits);
    }

    [Fact]
    public void A_rule_edits_a_value_inside_a_private_entry_of_a_list()
    {
        string text = "export Bank is TBank\n(\n    Params =\n    [\n        private parOpacity is Template_Param_Float ( DefaultValue = 0.3 ),\n    ]\n)\n";
        var result = Run(text, Edit("Bank", "DefaultValue", "Multiply", "2"));
        Assert.Contains("private parOpacity is Template_Param_Float ( DefaultValue = 0.6 ),", result.Text);
    }

    [Fact]
    public void A_condition_is_kept_as_written_while_its_neighbours_are_edited()
    {
        string text = "export Button is TButton\n(\n    Size = 10\n    Sound = HasSound ? ButtonSound : nil\n)\n";
        var result = Run(text, Edit("Button", "Size", "Multiply", "2"));
        Assert.Equal(text.Replace("Size = 10", "Size = 20"), result.Text);
    }
}
