using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// A path part in square brackets reaches one entry of a list: a MAP entry by its key, or an object by a value it holds.
/// Steel Division 2 keeps every division's units in one MAP of DivisionRules.ndf, keyed by the division, so a change to
/// one division's units must reach that entry only. Made-up text laid out as the game's file.
/// </summary>
public class MapEntryPathTests
{
    private static readonly string DivisionRules = """
        private DivisionRules is TDeckDivisionRules
        (
            DivisionRules = MAP
            [
                (
                    ~/Descriptor_Deck_Division_Alpha_multi,
                    TDeckDivisionRule
                    (
                        UnitRuleList =
                        [
                            TDeckUniteRule
                            (
                                UnitDescriptor = ~/Descriptor_Unit_Tank
                                MaxPackNumber = 2
                                NumberOfUnitInPackByPhase = [4, 8, 0]
                            ),
                            TDeckUniteRule
                            (
                                UnitDescriptor = ~/Descriptor_Unit_Squad
                                MaxPackNumber = 3
                                NumberOfUnitInPackByPhase = [6, 12, 0]
                            ),
                        ]
                    )
                ),
                (
                    ~/Descriptor_Deck_Division_Beta_multi,
                    TDeckDivisionRule
                    (
                        UnitRuleList =
                        [
                            TDeckUniteRule
                            (
                                UnitDescriptor = ~/Descriptor_Unit_Tank
                                MaxPackNumber = 2
                                NumberOfUnitInPackByPhase = [4, 8, 0]
                            ),
                        ]
                    )
                ),
            ]
        )

        """.ReplaceLineEndings("\n");

    private static Rule Edit(params RuleAction[] actions) => new()
    {
        Name = "test",
        Kind = "Edit",
        LogicGate = "All",
        Conditions = [new Condition { Operand = Conditions.DescriptorTitle, Operator = "Is Equal To", Value = "DivisionRules" }],
        Actions = [.. actions],
    };

    private static RunResult Run(string text, params Rule[] rules)
    {
        var engine = new NdfEngine();
        engine.Load(text);
        RunResult result = engine.Run([.. rules], new RunOptions());
        Assert.Equal("", result.ParseError);
        return result;
    }

    private const string Alpha = "> DivisionRules > [~/Descriptor_Deck_Division_Alpha_multi]";

    /// <summary>The text of one division's entry, from its key to the next division's.</summary>
    private static string EntryOf(string text, string division)
    {
        int start = text.IndexOf("~/Descriptor_Deck_Division_" + division, StringComparison.Ordinal);
        int next = text.IndexOf("~/Descriptor_Deck_Division_", start + 1, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }

    [Fact]
    public void A_unit_is_added_to_one_division_of_the_map_only()
    {
        var add = new RuleAction { Target = Alpha + " > UnitRuleList", Operator = "Insert [array]", Value = "LastIndex: TDeckUniteRule ( UnitDescriptor = ~/Descriptor_Unit_Jeep MaxPackNumber = 1 )" };

        RunResult result = Run(DivisionRules, Edit(add));

        Assert.Contains("Descriptor_Unit_Jeep", EntryOf(result.Text!, "Alpha"), StringComparison.Ordinal);
        Assert.DoesNotContain("Descriptor_Unit_Jeep", EntryOf(result.Text!, "Beta"), StringComparison.Ordinal);
        Assert.Equal(1, result.Edits);
    }

    [Fact]
    public void A_unit_of_one_division_is_changed_and_taken_out_and_the_same_unit_elsewhere_is_left_alone()
    {
        var cards = new RuleAction { Target = Alpha + " > UnitRuleList > [~/Descriptor_Unit_Tank] > MaxPackNumber", Operator = "Set To [int, float, string, boolean]", Value = "5" };
        var phases = new RuleAction { Target = Alpha + " > UnitRuleList > [~/Descriptor_Unit_Tank] > NumberOfUnitInPackByPhase", Operator = "Set To [list, object]", Value = "[2, 4, 6]" };
        var remove = new RuleAction { Target = Alpha + " > UnitRuleList > [~/Descriptor_Unit_Squad]", Operator = ActionApplier.RemovePropertyOperator, Value = "" };

        RunResult result = Run(DivisionRules, Edit(cards, phases, remove));

        string alpha = EntryOf(result.Text!, "Alpha"), beta = EntryOf(result.Text!, "Beta");
        Assert.Contains("MaxPackNumber = 5", alpha, StringComparison.Ordinal);
        Assert.Contains("NumberOfUnitInPackByPhase = [2, 4, 6]", alpha, StringComparison.Ordinal);
        Assert.DoesNotContain("Descriptor_Unit_Squad", alpha, StringComparison.Ordinal);
        Assert.Contains("MaxPackNumber = 2", beta, StringComparison.Ordinal);
        Assert.Contains("NumberOfUnitInPackByPhase = [4, 8, 0]", beta, StringComparison.Ordinal);
        Assert.Equal(3, result.Edits);
        Assert.All(result.Stats, rule => Assert.Equal(0, rule.Skipped));
        // still NDF the engine reads
        Assert.Single(Ndf.Parser.Parse(result.Text!).Declarations);
    }

    [Fact]
    public void Two_mods_changing_different_units_of_the_same_division_both_take()
    {
        var first = new RuleAction { Target = Alpha + " > UnitRuleList > [~/Descriptor_Unit_Tank] > MaxPackNumber", Operator = "Set To [int, float, string, boolean]", Value = "7" };
        var second = new RuleAction { Target = Alpha + " > UnitRuleList > [~/Descriptor_Unit_Squad]", Operator = ActionApplier.RemovePropertyOperator, Value = "" };

        // as the export runs mods: the second on what the first gave
        string afterFirst = Run(DivisionRules, Edit(first)).Text!;
        string afterSecond = Run(afterFirst, Edit(second)).Text!;

        string alpha = EntryOf(afterSecond, "Alpha");
        Assert.Contains("MaxPackNumber = 7", alpha, StringComparison.Ordinal);
        Assert.DoesNotContain("Descriptor_Unit_Squad", alpha, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_matches_a_reference_by_its_last_part_and_a_missing_key_changes_nothing()
    {
        var byLastPart = new RuleAction { Target = "> DivisionRules > [Descriptor_Deck_Division_Beta_multi] > UnitRuleList > [$/GFX/Everything/Descriptor_Unit_Tank] > MaxPackNumber", Operator = "Set To [int, float, string, boolean]", Value = "9" };
        var missing = new RuleAction { Target = "> DivisionRules > [~/Descriptor_Deck_Division_Gamma_multi] > UnitRuleList > [~/Descriptor_Unit_Tank]", Operator = ActionApplier.RemovePropertyOperator, Value = "" };

        RunResult result = Run(DivisionRules, Edit(byLastPart, missing));

        Assert.Contains("MaxPackNumber = 9", EntryOf(result.Text!, "Beta"), StringComparison.Ordinal);
        Assert.Contains("MaxPackNumber = 2", EntryOf(result.Text!, "Alpha"), StringComparison.Ordinal);
        Assert.Equal(1, result.Edits);
        // the key no entry has is said, as any action that finds nothing
        Assert.Equal(1, result.Stats.Single().Skipped);
    }

    [Fact]
    public void A_map_entry_is_taken_out_by_its_key()
    {
        var packs = """
            export Descriptor_Deck_Division_Alpha_multi is TDeckDivisionDescriptor
            (
                PackList = MAP [
                    (~/Descriptor_Deck_Pack_TOE_Alpha_multi_Tank, 2),
                    (~/Descriptor_Deck_Pack_TOE_Alpha_multi_Squad, 3),
                ]
            )

            """.ReplaceLineEndings("\n");
        var remove = new RuleAction { Target = "PackList > [~/Descriptor_Deck_Pack_TOE_Alpha_multi_Squad]", Operator = ActionApplier.RemovePropertyOperator, Value = "" };
        var rule = new Rule
        {
            Name = "test", Kind = "Edit", LogicGate = "All", Actions = [remove],
            Conditions = [new Condition { Operand = Conditions.DescriptorTitle, Operator = "Is Equal To", Value = "Descriptor_Deck_Division_Alpha_multi" }],
        };

        RunResult result = Run(packs, rule);

        Assert.DoesNotContain("Squad", result.Text!, StringComparison.Ordinal);
        Assert.Contains("(~/Descriptor_Deck_Pack_TOE_Alpha_multi_Tank, 2),", result.Text!, StringComparison.Ordinal);
    }
}
