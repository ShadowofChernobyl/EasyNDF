using System.Text;
using EasyNdf.Engine.Editing;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// What a run does with the game's own version of a file: a Clone whose source an earlier mod removed is copied from
/// it; and Export's check of classes the game's files don't use.
/// </summary>
public class GameOriginalTests
{
    private const string Game = "export Division_A is TDivision\n(\n    Name = 'A'\n    Points = 50\n)\nexport Division_B is TDivision\n(\n    Name = 'B'\n)\n";

    // an earlier mod removed Division_A
    private const string AfterEarlierMod = "export Division_B is TDivision\n(\n    Name = 'B'\n)\n";

    private static Rule CloneA() => new()
    {
        Name = "copy A",
        Kind = "Clone",
        LogicGate = "All",
        Conditions = [],
        Actions = [new RuleAction { Target = "Points", Operator = "Set To [int, float, string, boolean]", Value = "720" }],
        Params = new() { ["Source"] = "Division_A", ["NewName"] = "Division_New", ["NewIds"] = true },
    };

    private static RunResult Run(string text, Func<string?>? game)
    {
        var engine = new NdfEngine();
        engine.Load(text);
        return engine.Run([CloneA()], new RunOptions { GameOriginal = game });
    }

    [Fact]
    public void A_clone_whose_source_an_earlier_mod_removed_is_copied_from_the_games_file()
    {
        RunResult result = Run(AfterEarlierMod, () => Game);

        Assert.Contains("export Division_New is TDivision", result.Text);
        Assert.Contains("Points = 720", result.Text);
        Assert.Contains("Name = 'A'", result.Text);
        // the removed source is not brought back
        Assert.DoesNotContain("Division_A", result.Text);
    }

    [Fact]
    public void Without_the_games_file_the_clone_is_not_made()
    {
        RunResult result = Run(AfterEarlierMod, null);

        Assert.DoesNotContain("Division_New", result.Text);
    }

    [Fact]
    public void A_renamed_form_of_a_game_class_is_found_once_outside_quotes_and_comments()
    {
        const string text = "T_A is TUIResourceTexture_Common(FileName = 'TFake(')\nT_B is TUIResourceTexture_Common(FileName = \"x\") // TOther(\nT_C is TUIResourceTexture(FileName = \"y\")\n"
            // a class the files never write, which the game knows: not reported
            + "T_D is TLabel(Opacity = TEugBMutableFloat(Value = 1))\n";
        var classes = new HashSet<string> { "TUIResourceTexture", "TLabel" };

        ValueProblem problem = Assert.Single(ExportValueCheck.UnknownClasses(text, 0, text.Length, classes));

        Assert.Equal(("TUIResourceTexture_Common", "TUIResourceTexture", ValueProblemKind.UnknownClass), (problem.Name, problem.Value, problem.Kind));
        Assert.Equal(text.IndexOf("TUIResourceTexture_Common", StringComparison.Ordinal), problem.Position);
    }
}
