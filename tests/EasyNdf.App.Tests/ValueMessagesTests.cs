using System.IO;
using System.Text;
using EasyNdf.App.Editor;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Tests;

/// <summary>
/// What the File Editor says about a marked value, as the mock chose ("AngleDispersion must be from 0 to 0.785."), and
/// what its tips say a value on a line means ("On", "3 seconds.").
/// </summary>
public sealed class ValueMessagesTests
{
    private static readonly ValueGuide Guide = ValueGuide.Read(new MemoryStream(Encoding.UTF8.GetBytes("""
        {"types": {"TShot": {
          "Count": {"valueType": "int", "typeCertain": true},
          "Spread": {"valueType": "float", "typeCertain": true, "description": "Half-angle of the cone.", "rules": ["In radians. Must be from 0 to 0.785."], "limits": [{"side": "max", "value": 0.785, "open": false, "confirmed": true}]},
          "Kind": {"valueType": "enum", "typeCertain": true, "choices": ["nil", "EKind/Shell", "EKind/Bomb"], "choicesComplete": true},
          "Delay": {"valueType": "float", "typeCertain": true, "description": "Seconds before it fires; -1 (never) to wait."},
          "RangeGRU": {"valueType": "float", "typeCertain": true}
        }}}
        """)));

    private static List<string> MessagesOf(string body)
    {
        string text = "Shot_A is TShot\n(\n" + body + ")\n";
        var document = new TextDocument(text);
        return [.. ValueChecker.Check(text, Guide).Select(problem => ValueMessages.MessageOf(problem, document))];
    }

    [Fact]
    public void Each_kind_of_problem_is_said_in_plain_words()
    {
        List<string> messages = MessagesOf("    Count = 1.5\n    Spread = 1.2\n    Kind = EKind/Nope\n    Count = 2\n");

        Assert.Equal(
            [
                "Count expects an int (a whole number), but 1.5 is a float.",
                "Spread must be from 0 to 0.785.",
                "EKind has no Nope. Its choices: Shell, Bomb.",
                "Count is already set on line 3.",
            ],
            messages);
    }

    [Fact]
    public void Under_a_value_in_Details_each_problem_is_said_without_the_name_or_the_list_of_choices()
    {
        List<ValueProblem> problems = ValueChecker.Check("Shot_A is TShot\n(\n    Count = 1.5\n    Spread = 1.2\n    Kind = EKind/Nope\n    Count = 2\n)\n", Guide);

        Assert.Equal(
            [
                "expects an int (a whole number), but 1.5 is a float.",
                "must be from 0 to 0.785.",
                "EKind has no Nope.",
                "is already set on line 3.",
            ],
            problems.Select(problem => ValueMessages.ShortOf(problem, firstLine: 3)));
    }

    [Fact]
    public void An_error_fails_mod_generation_and_a_warning_says_what_the_value_becomes()
    {
        var error = new ValueProblem(0, 1, ValueProblemKind.WrongKind, "Count", "1.5");
        var warning = new ValueProblem(0, 1, ValueProblemKind.NegativeInZeroOrMore, "Lines", "-1");

        var range = new ValueProblem(0, 4, ValueProblemKind.BrokenComparison, "MinimumRangeGRU", "3000")
        {
            Comparison = new ValueComparison("<", "MaximumRangeGRU"),
            OtherValue = "1925",
        };
        Assert.Equal("MinimumRangeGRU must be less than MaximumRangeGRU (1925).", ValueMessages.MessageOf(range, new TextDocument("x")));
        Assert.Equal("Mod generation will fail.", ValueMessages.WhyOf(error));
        Assert.Equal("Mod generation works, but -1 turns into 4294967295.", ValueMessages.WhyOf(warning));
        Assert.Equal("Lines expects an int of 0 or more.", ValueMessages.MessageOf(warning, new TextDocument("x")));
    }

    [Fact]
    public void An_impact_the_game_does_not_have_crashes_the_game_rather_than_mod_generation()
    {
        var impact = new ValueProblem(0, 21, ValueProblemKind.NoSuchImpact, "ImpactHappening", "'MIssileAASIDEWINDER'");

        Assert.Equal("MIssileAASIDEWINDER is not an impact the game has.", ValueMessages.MessageOf(impact, new TextDocument("x")));
        Assert.Equal("The game crashes when it is fired.", ValueMessages.WhyOf(impact));
    }

    [Fact]
    public void A_value_on_a_line_is_read_in_plain_words()
    {
        ValueInfo spread = Guide.Find("TShot", "Spread")!, delay = Guide.Find("TShot", "Delay")!, kind = Guide.Find("TShot", "Kind")!, range = Guide.Find("TShot", "RangeGRU")!;

        Assert.Equal("True", ValueTips.Reading("Spread", "True", spread));
        Assert.Equal("False", ValueTips.Reading("Spread", "false", spread));
        Assert.Equal("None", ValueTips.Reading("Kind", "nil", kind));
        Assert.Equal("Refers to Ammo_X.", ValueTips.Reading("Kind", "~/Ammo_X", kind));
        Assert.Equal("Shell, one of 3 choices in the files.", ValueTips.Reading("Kind", "EKind/Shell", kind));
        Assert.Equal("0.5 rad = 28.6°.", ValueTips.Reading("Spread", "0.5", spread));
        Assert.Equal("1,925 GRU.", ValueTips.Reading("RangeGRU", "1925", range));
        Assert.Equal("3 seconds.", ValueTips.Reading("Delay", "3.0", delay));
        Assert.Equal("-1: never.", ValueTips.Reading("Delay", "-1", delay));
        Assert.Equal("A TDamage, set in the block below.", ValueTips.Reading("Spread", "TDamage", spread));
        Assert.Equal("", ValueTips.Reading("Spread", "5 * 60", spread));
    }

    [Fact]
    public void A_place_is_found_in_the_declaration_it_is_in_only()
    {
        const string text = "A is TShot\n(\n    Count = 1\n)\nB is TOther\n(\n    Kind = EKind/Shell\n)\n";
        var document = new TextDocument(text);

        NdfPlace place = EditorPlaces.At(document, text.IndexOf("Shell", StringComparison.Ordinal))!;

        Assert.Equal("TOther", place.ObjectType);
        Assert.Equal("Kind", place.Name);
        Assert.Equal(text.IndexOf("EKind", StringComparison.Ordinal), place.WordStart);
        Assert.Equal(["Kind"], place.NamesSet.Keys);
    }
}
