using System.Text.Json.Nodes;
using EasyNdf.Engine.Editing;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The File Editor's colouring, against the HTML app's own functions. Cases/editor-colours.json holds made-up NDF text
/// with the block each line starts inside and the coloured parts of each line, as the HTML app gives them; regenerate it
/// with <c>node tools/engine-check/editor-colours.js "path/to/EasyNDF vX.Y.Z.html" tests/EasyNdf.Engine.Tests/Cases/editor-colours.json</c>.
/// </summary>
public class EditorColouringMatchesHtmlAppTests
{
    private static readonly Lazy<JsonArray> Cases = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", "editor-colours.json")))!.AsArray());

    [Fact]
    public void Each_line_starts_inside_the_same_block()
    {
        foreach (JsonNode? text in Cases.Value)
        {
            List<string> lines = [.. text!["lines"]!.AsArray().Select(l => (string)l!)];
            int[] expected = [.. text["states"]!.AsArray().Select(s => (int)s!)];
            Assert.Equal(expected, NdfColouring.BlocksOfLines(lines));
        }
    }

    [Fact]
    public void Lines_are_coloured_the_same_way()
    {
        foreach (JsonNode? text in Cases.Value)
        {
            JsonArray lines = text!["lines"]!.AsArray(), states = text["states"]!.AsArray(), parts = text["parts"]!.AsArray();
            for (int i = 0; i < lines.Count; i++)
            {
                string expected = string.Join(" ", parts[i]!.AsArray().Select(p => p![0] + "-" + p[1] + ":" + p[2]));
                var (coloured, _) = NdfColouring.ColourLine((string)lines[i]!, (int)states[i]!);
                Assert.True(expected == string.Join(" ", coloured.Select(p => p.Start + "-" + p.End + ":" + p.Colour)),
                    "Line \"" + lines[i] + "\": expected " + expected + ", got " + string.Join(" ", coloured.Select(p => p.Start + "-" + p.End + ":" + p.Colour)));
            }
        }
    }
}
