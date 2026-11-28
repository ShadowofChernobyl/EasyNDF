namespace EasyNdf.Engine.Tests;

/// <summary>
/// The Lines column's count (LoadSummary.Lines): one more than the line breaks, none for an empty file, whatever the
/// line endings, and the same for a full read and an export's read.
/// </summary>
public sealed class LineCountTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("A is 1", 1)]
    [InlineData("A is 1\n", 2)]
    [InlineData("A is 1\r\nB is 2\r\n", 3)]
    [InlineData("A is 1\rB is 2", 1)]
    [InlineData("// a comment\n\n\nA is T\n(\n    X = 1\n)\n", 8)]
    public void Counts_the_lines_of_a_file(string text, int lines)
    {
        Assert.Equal(lines, new NdfEngine().Load(text).Lines);
        Assert.Equal(lines, new NdfEngine().LoadForRun(text).Lines);
    }

    [Fact]
    public void Counts_every_line_of_a_large_file()
    {
        string text = string.Concat(Enumerable.Range(0, 5000).Select(k => "A" + k + " is " + k + (k % 3 == 0 ? "\r\n" : "\n")));
        Assert.Equal(5001, new NdfEngine().LoadForRun(text).Lines);
    }
}
