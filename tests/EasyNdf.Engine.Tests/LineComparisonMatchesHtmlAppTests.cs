using System.Text.Json.Nodes;
using EasyNdf.Engine.Editing;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The File Editor's line comparison, against the HTML app's own functions. Cases/line-diff.json holds pairs of made-up
/// texts with the places they differ and where each line maps to, as the HTML app gives them; regenerate it with
/// <c>node tools/engine-check/line-diff.js "path/to/EasyNDF vX.Y.Z.html" tests/EasyNdf.Engine.Tests/Cases/line-diff.json</c>.
/// </summary>
public class LineComparisonMatchesHtmlAppTests
{
    private static readonly Lazy<JsonArray> Cases = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", "line-diff.json")))!.AsArray());

    private static List<string> Lines(JsonNode? lines) => [.. lines!.AsArray().Select(l => (string)l!)];

    [Fact]
    public void The_texts_differ_in_the_same_places()
    {
        foreach (JsonNode? pair in Cases.Value)
        {
            string expected = string.Join(" ", pair!["differences"]!.AsArray().Select(d => string.Join(",", d!.AsArray().Select(n => (int)n!))));
            string actual = string.Join(" ", LineComparison.Differences(Lines(pair["a"]), Lines(pair["b"])).Select(d => d.FromA + "," + d.ToA + "," + d.FromB + "," + d.ToB));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Each_line_maps_to_the_same_line()
    {
        foreach (JsonNode? pair in Cases.Value)
        {
            List<string> a = Lines(pair!["a"]), b = Lines(pair["b"]);
            int[] expected = [.. pair["matching"]!.AsArray().Select(n => (int)n!)];
            Assert.Equal(expected, Enumerable.Range(0, a.Count).Select(t => LineComparison.MatchingLine(a, b, t)));
        }
    }

    [Fact]
    public void Changes_only_shows_the_changed_lines_with_two_around_each()
    {
        List<string> a = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"];
        List<string> b = ["1", "2", "3", "4", "5", "6", "7", "8", "9 changed", "10"];
        int[] blocks = new int[11];
        ChangedLines shown = LineComparison.ChangesOnly(a, blocks, b, blocks)!;
        Assert.Equal(["7", "8", "9", "9 changed", "10"], shown.Lines);
        Assert.Equal(["7", "8", "9", "9", "10"], shown.Numbers);
        Assert.Equal([LineMark.Same, LineMark.Same, LineMark.Removed, LineMark.Added, LineMark.Same], shown.Marks);
        Assert.Equal(1, shown.ChangedLineCount);
        Assert.Equal(1, shown.PlaceCount);
        Assert.Null(LineComparison.ChangesOnly(a, blocks, a, blocks));
    }

    [Fact]
    public void Changes_far_apart_have_a_gap_between_them()
    {
        List<string> a = [.. Enumerable.Range(1, 20).Select(n => "line " + n)];
        List<string> b = [.. a];
        b[1] = "changed 2";
        b[17] = "changed 18";
        int[] blocks = new int[21];
        ChangedLines shown = LineComparison.ChangesOnly(a, blocks, b, blocks)!;
        Assert.Equal(1, shown.Marks.Count(m => m == LineMark.Gap));
        Assert.Equal("⋯", shown.Lines[shown.Marks.IndexOf(LineMark.Gap)]);
        Assert.Equal(2, shown.PlaceCount);
    }
}
