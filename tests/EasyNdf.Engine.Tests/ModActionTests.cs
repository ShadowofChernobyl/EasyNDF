using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The actions the desktop app adds to the HTML app's, which large mods such as Yokaiste's Sandbox Mod need: a
/// property removed, a path that starts at the descriptor's top, an entry inserted beside another, a list written
/// whole and then added to, a condition in brackets replaced, and values on several lines indented where they land.
/// </summary>
public class ModActionTests
{
    private static readonly string Panel = """
        Panel is TContainer
        (
            // the frame
            Frame = TFrame
            (
                Size = [1.0, 0.0]
                Offset = [5.0, 5.0] // moved a little
            )
            HasBackground = true
            Buttons =
            [
                TButton
                (
                    Frame = TFrame ( Size = [2.0, 2.0] )
                ),
            ]
        )

        """.ReplaceLineEndings("\n");

    private static readonly string Unit = """
        Descriptor_Unit_A is TEntityDescriptor
        (
            ModulesDescriptors =
            [
                TTagsModuleDescriptor
                (
                    TagSet = [ "AllUnits", "Unite" ]
                ),
                TBaseDamageModuleDescriptor ( MaxPhysicalDamages = 10 ),
                ~/FacingInfosModuleDescriptor,
            ]
        )

        """.ReplaceLineEndings("\n");

    private static RunResult Run(string text, params Rule[] rules)
    {
        var engine = new NdfEngine();
        engine.Load(text);
        return engine.Run([.. rules], new RunOptions());
    }

    private static Rule Edit(params RuleAction[] actions) => new()
    {
        Name = "test",
        Kind = "Edit",
        LogicGate = "All",
        Conditions = [],
        Actions = [.. actions],
    };

    private static RuleAction Action(string target, string op, string value = "") => new() { Target = target, Operator = op, Value = value };

    // ---- Remove Property ----

    [Fact]
    public void Remove_Property_takes_out_the_line_and_keeps_the_comment_above()
    {
        var result = Run(Panel, Edit(Action("> HasBackground", "Remove Property")));

        Assert.DoesNotContain("HasBackground", result.Text);
        Assert.Contains("        Offset = [5.0, 5.0] // moved a little\n    )\n    Buttons =", result.Text);
        Assert.Equal(1, result.Edits);
    }

    [Fact]
    public void Remove_Property_takes_out_a_line_with_its_end_comment()
    {
        var result = Run(Panel, Edit(Action("Frame > Offset", "Remove Property")));

        Assert.DoesNotContain("moved a little", result.Text);
        Assert.Contains("        Size = [1.0, 0.0]\n    )\n", result.Text);
    }

    [Fact]
    public void Remove_Property_on_one_line_takes_out_only_that_property()
    {
        var result = Run("X is TThing ( A = 1 B = 2 C = 3 )\n", Edit(Action("B", "Remove Property")));

        Assert.Equal("X is TThing ( A = 1 C = 3 )\n", result.Text);
    }

    [Fact]
    public void Remove_Property_of_a_property_that_is_not_there_skips()
    {
        var result = Run(Panel, Edit(Action("Missing", "Remove Property")));

        Assert.Equal(Panel, result.Text);
        Assert.Equal(1, result.Stats[0].Skipped);
    }

    [Fact]
    public void Add_Property_with_a_path_adds_inside_the_object_it_leads_to()
    {
        var result = Run(Panel, Edit(Action("Frame > Width", "Add Property", "4.0"), Action("Missing > Width", "Add Property", "4.0")));

        Assert.Contains("        Offset = [5.0, 5.0] // moved a little\n        Width = 4.0\n    )", result.Text);
        Assert.Equal(1, result.Edits);
        Assert.Equal(1, result.Stats[0].Skipped);
        Assert.Equal("", result.ParseError);
    }

    [Fact]
    public void Remove_Property_on_the_constants_takes_a_constant_out_of_the_file()
    {
        string text = "Subtitles is\n[\n    1,\n    2,\n]\n\nLogo is Video(File = 'intro.webm')\nSpeed is 3\n";

        var result = Run(text, new Rule
        {
            Name = "test",
            Kind = "Edit",
            LogicGate = "All",
            Conditions = [],
            Actions = [Action("> Subtitles", "Remove Property"), Action("Speed", "Set To [int, float, string, boolean]", "4")],
            Params = new() { ["Scope"] = "constants" },
        });

        Assert.DoesNotContain("Subtitles", result.Text);
        Assert.Contains("Logo is Video(File = 'intro.webm')\nSpeed is 4\n", result.Text);
        Assert.Equal("", result.ParseError);
    }

    // ---- paths that start at the top ----

    [Fact]
    public void An_anchored_path_leaves_the_same_names_deeper_down()
    {
        var result = Run(Panel, Edit(Action("> Frame > Size", "Set To [list, object]", "[3.0, 3.0]")));

        Assert.Contains("Size = [3.0, 3.0]", result.Text);
        Assert.Contains("Frame = TFrame ( Size = [2.0, 2.0] )", result.Text); // the button's frame is left alone
        Assert.Equal(1, result.Edits);
    }

    [Fact]
    public void A_path_without_the_anchor_still_finds_every_match()
    {
        var result = Run(Panel, Edit(Action("Frame > Size", "Set To [list, object]", "[3.0, 3.0]")));

        Assert.Equal(2, result.Edits);
    }

    // ---- inserting beside an entry ----

    [Fact]
    public void Insert_before_an_entry_goes_in_front_of_the_first_that_holds_the_text()
    {
        var result = Run(Unit, Edit(Action("TagSet", "Insert [array]", "Before \"Unite\": \"UNITE_A\"")));

        Assert.Contains("TagSet = [ \"AllUnits\", \"UNITE_A\", \"Unite\" ]", result.Text);
    }

    [Fact]
    public void Insert_before_a_type_name_and_after_a_reference()
    {
        var result = Run(Unit, Edit(
            Action("ModulesDescriptors", "Insert [array]", "Before TBaseDamageModuleDescriptor: $/GFX/Weapon/WeaponDescriptor_A"),
            Action("ModulesDescriptors", "Insert [array]", "After ~/FacingInfosModuleDescriptor: ~/Last")));

        int weapon = result.Text!.IndexOf("WeaponDescriptor_A", StringComparison.Ordinal);
        int damage = result.Text.IndexOf("TBaseDamageModuleDescriptor", StringComparison.Ordinal);
        int last = result.Text.IndexOf("~/Last", StringComparison.Ordinal);
        int facing = result.Text.IndexOf("~/FacingInfosModuleDescriptor", StringComparison.Ordinal);
        Assert.True(weapon < damage && facing < last);
    }

    [Fact]
    public void Insert_beside_an_entry_that_is_not_there_goes_at_the_end()
    {
        var result = Run(Unit, Edit(Action("TagSet", "Insert [array]", "Before \"Missing\": \"UNITE_A\"")));

        Assert.Contains("TagSet = [ \"AllUnits\", \"Unite\", \"UNITE_A\" ]", result.Text);
    }

    // ---- a list written whole, then added to ----

    [Fact]
    public void A_list_set_whole_can_be_added_to_by_a_later_rule()
    {
        var result = Run(Unit,
            Edit(Action("TagSet", "Set To [list, object]", "[]")),
            Edit(Action("TagSet", "Insert [array]", "LastIndex: \"A\""), Action("TagSet", "Insert [array]", "LastIndex: \"B\"")));

        string compact = result.Text!.Replace("\n", "").Replace(" ", "");
        Assert.Contains("TagSet=[\"A\",\"B\",]", compact);
        Assert.Equal("", result.ParseError);
    }

    // ---- conditions in brackets ----

    [Fact]
    public void A_condition_in_brackets_is_replaced_with_its_brackets()
    {
        string text = "template T\n[\n    IsOn,\n]\nis TThing\n(\n    Items = [ ~/A ] + (<IsOn> ? [ ~/B ] : [])\n)\n";

        var result = Run(text, Edit(Action("Items", "Set To [list, object]", "[ ~/C ]")));

        Assert.Contains("    Items = [ ~/C ]\n)", result.Text);
        Assert.Equal("", result.ParseError);
    }

    [Fact]
    public void A_condition_in_brackets_reads_back_unchanged()
    {
        string text = "template T\n[\n    IsOn,\n]\nis TThing\n(\n    Clip = (<IsOn> ? true : false)\n    Items = [ ~/A ] + (<IsOn> ? [ ~/B ] : [])\n)\n";

        var result = Run(text);

        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void A_list_and_object_Set_To_sets_flags_written_with_a_bar()
    {
        var result = Run("X is T\n(\n    M = TM\n    (\n        G = E/Nothing | E/Attack\n    )\n)\n", Edit(Action("G", "Set To [list, object]", "E/Nothing")));

        Assert.Contains("        G = E/Nothing\n", result.Text);
        Assert.Equal(1, result.Edits);
    }

    // ---- values on several lines ----

    [Fact]
    public void A_list_typed_on_several_lines_takes_the_indentation_of_its_property()
    {
        var result = Run(Panel, Edit(Action("> HasBackground", "Set To [list, object]", "[\n    ~/A,\n    ~/B,\n]")));

        Assert.Contains("    HasBackground = [\n        ~/A,\n        ~/B,\n    ]\n", result.Text);
    }
}
