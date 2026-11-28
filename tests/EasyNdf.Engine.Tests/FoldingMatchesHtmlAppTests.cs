using System.Text.Json.Nodes;
using EasyNdf.Engine.Editing;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// Where the File Editor's blocks start and end, against the HTML app's own functions: Cases/editor-colours.json holds,
/// for each line of its made-up texts, where the block it starts ends (edBlock) and whether it starts one (edCanFold).
/// </summary>
public class FoldingMatchesHtmlAppTests
{
    private static readonly Lazy<JsonArray> Cases = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", "editor-colours.json")))!.AsArray());

    [Fact]
    public void Blocks_end_on_the_same_lines()
    {
        foreach (JsonNode? text in Cases.Value)
        {
            List<string> lines = [.. text!["lines"]!.AsArray().Select(l => (string)l!)];
            int[] blocks = NdfColouring.BlocksOfLines(lines);
            int[] expected = [.. text["blockEnds"]!.AsArray().Select(n => (int)n!)];
            Assert.Equal(expected, Enumerable.Range(0, lines.Count).Select(i => NdfFolding.BlockEnd(lines, blocks, i)));
            // looking a limited way gives the same end, or says the block goes on further
            for (int i = 0; i < lines.Count; i++)
            {
                int near = NdfFolding.BlockEnd(lines, blocks, i, mostLines: 3);
                Assert.True(near == expected[i] || (near == NdfFolding.FurtherThanLooked && expected[i] - i > 3 || expected[i] == -1 && near == NdfFolding.FurtherThanLooked),
                    "line " + i + ": " + near + " for " + expected[i]);
            }
            bool[] starts = [.. text["startsBlock"]!.AsArray().Select(n => (bool)n!)];
            Assert.Equal(starts, Enumerable.Range(0, lines.Count).Select(i => NdfFolding.StartsBlock(lines, blocks, i)));
        }
    }

    [Fact]
    public void Fold_all_folds_each_top_level_block_once()
    {
        List<string> lines = ["A is T", "(", "  L = [", "    1,", "  ]", ")", "B is T ( X = 1 )", "C is T", "(", ")"];
        Assert.Equal([(0, 5), (7, 9)], NdfFolding.TopLevelBlocks(lines, NdfColouring.BlocksOfLines(lines)));
    }
}
