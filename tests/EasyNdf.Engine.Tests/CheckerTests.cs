using EasyNdf.Engine.Editing;

namespace EasyNdf.Engine.Tests;

/// <summary>The File Editor's check: every broken declaration gives one problem, with its place; text that reads gives none.</summary>
public class CheckerTests
{
    [Fact]
    public void Text_that_reads_has_no_problems()
    {
        const string text = "// head\nexport Unit_A is TEntity\n(\n    Speed = 12\n    Items = [1, 2]\n)\nunnamed TThing()\n";

        Assert.Empty(NdfChecker.Check(text));
    }

    [Fact]
    public void A_bracket_left_open_is_found_where_the_next_declaration_starts_and_that_one_is_still_checked()
    {
        const string text = "Unit_A is TEntity\n(\n    Speed = 12\n\nUnit_B is TEntity\n(\n    Speed = = 1\n)\n";

        List<NdfProblem> problems = NdfChecker.Check(text);

        Assert.Equal(2, problems.Count);
        Assert.Equal(5, problems[0].Line);
        Assert.Equal(text.IndexOf("Unit_B", StringComparison.Ordinal), problems[0].Position);
        Assert.Equal("Unit_B".Length, problems[0].Length);
        Assert.StartsWith("Line 5: expected ", problems[0].Message);
        Assert.EndsWith(" before \"Unit_B\"", problems[0].Message);
        Assert.Equal(7, problems[1].Line);
        Assert.StartsWith("Line 7: ", problems[1].Message);
    }

    [Fact]
    public void Text_that_is_not_NDF_is_found_where_it_is_and_the_text_after_it_is_still_checked()
    {
        const string text = "Unit_A is TEntity\n(\n    Name = §\n)\nUnit_B is TEntity\n(\n    Speed = = 1\n)\nUnit_C is TEntity()\n";

        List<NdfProblem> problems = NdfChecker.Check(text);

        Assert.Equal([3, 7], problems.Select(p => p.Line));
        Assert.Equal(text.IndexOf('§'), problems[0].Position);
        Assert.Contains("unrecognized", problems[0].Message);
    }

    [Fact]
    public void A_bracket_open_at_the_end_is_found_at_the_end()
    {
        const string text = "Unit_A is TEntity\n(\n    Speed = 12\n";

        NdfProblem problem = Assert.Single(NdfChecker.Check(text));

        Assert.Equal(text.Length, problem.Position);
        Assert.Equal(0, problem.Length);
        Assert.Contains("(end of file)", problem.Message);
    }

    [Fact]
    public void The_engine_still_reports_text_that_is_not_NDF_further_on_first()
    {
        const string text = "Unit_A is TEntity\n(\n    Speed = = 12\n)\nUnit_B is TEntity\n(\n    Name = §\n)\n";

        var error = Assert.Throws<Ndf.NdfParseException>(() => Ndf.Parser.Parse(text));

        Assert.StartsWith("Line 7: unrecognized text", error.Message);
        Assert.Equal(text.IndexOf('§'), error.Position);
    }
}
