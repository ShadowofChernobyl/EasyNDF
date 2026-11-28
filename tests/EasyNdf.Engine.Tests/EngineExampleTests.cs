using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// Examples of what the engine does, written out in full so they also serve as documentation.
/// Every behaviour here is also covered by the comparison with the HTML app (<see cref="MatchesHtmlAppTests"/>).
/// </summary>
public class EngineExampleTests
{
    // .ReplaceLineEndings: the test file may have Windows line endings after a checkout; the engine keeps whatever it is given.
    private static readonly string Ammunition = """
        // Weapons
        export Ammo_Canon_AP is TAmmunitionDescriptor
        (
            Name                              = 'CANON_AP'
            MaximumRangeGRU                   = 2100 // in metres x 1.5
            Salves                            = [4, 2]
        )
        export Ammo_Mortar is TAmmunitionDescriptor
        (
            MaximumRangeGRU                   = 3500
        )

        """.ReplaceLineEndings("\n");

    private static RunResult Run(string text, params Rule[] rules)
    {
        var engine = new NdfEngine();
        engine.Load(text);
        return engine.Run([.. rules], new RunOptions());
    }

    private static Rule Edit(string target, string op, string value, Condition? condition = null) => new()
    {
        Name = "test",
        Kind = "Edit",
        LogicGate = "All",
        Conditions = condition is null ? [] : [condition],
        Actions = [new RuleAction { Target = target, Operator = op, Value = value }],
    };

    [Fact]
    public void With_no_rules_the_file_comes_back_unchanged()
    {
        var result = Run(Ammunition);
        Assert.Equal(Ammunition, result.Text);
    }

    [Fact]
    public void Multiply_changes_only_the_value_and_keeps_the_comment_after_it()
    {
        var result = Run(Ammunition, Edit("MaximumRangeGRU", "Multiply [int, float]", "2",
            new Condition { Operand = "Descriptor Title", Operator = "Starts With", Value = "Ammo_Canon" }));

        Assert.Contains("MaximumRangeGRU                   = 4200 // in metres x 1.5", result.Text);
        Assert.Contains("MaximumRangeGRU                   = 3500\n", result.Text); // the mortar did not match
        Assert.Equal(1, result.Edits);
    }

    [Fact]
    public void An_action_comment_is_written_after_the_value()
    {
        var rule = Edit("MaximumRangeGRU", "Add", "100");
        rule.Actions![0]!.Comment = new ActionComment { Enabled = true, IncludeOriginalValue = true };

        var result = Run(Ammunition, rule);

        Assert.Contains("= 3600 /*Added 100 Original Value: 3500*/", result.Text);
    }

    [Fact]
    public void Insert_adds_a_list_item_spaced_like_the_others()
    {
        var result = Run(Ammunition, Edit("Salves", "Insert [array]", "LastIndex: 1"));
        Assert.Contains("Salves                            = [4, 2, 1]", result.Text);
    }

    [Fact]
    public void Text_that_is_not_valid_NDF_is_caught_before_saving()
    {
        var result = Run(Ammunition, Edit("Name", "Set To", "'unclosed"));
        Assert.Equal("Line 4: unrecognized text \"'unclosed\"", result.ParseError); // the line of the export, not of the descriptor
    }

    [Fact]
    public void Whole_numbers_too_long_for_a_double_are_calculated_exactly()
    {
        var result = Run("A is T\n(\n    Big = 123456789012345678\n)\n", Edit("Big", "Add", "1"));
        Assert.Contains("Big = 123456789012345679", result.Text);
    }

    [Fact]
    public void Cloned_descriptors_get_the_same_new_ids_on_every_export()
    {
        const string text = "export A is T\n(\n    DescriptorId = GUID:{11111111-2222-3333-4444-555555555555}\n)\n";
        var clone = new Rule
        {
            Name = "copy",
            Kind = "Clone",
            Params = new() { ["Source"] = "A", ["NewName"] = "B" },
        };

        string first = Run(text, clone).Text!;
        string second = Run(text, clone).Text!;

        Assert.Equal(first, second);
        Assert.Contains("export B is T", first);
        Assert.DoesNotContain("GUID:{11111111-2222-3333-4444-555555555555}", first[first.IndexOf("export B", StringComparison.Ordinal)..]);
    }
}
