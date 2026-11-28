using System.Reflection;
using System.Text.RegularExpressions;
using EasyNdf.Engine.Writing;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// A Clone rule's copy leaves out other tools' marker comments (ExportWriter.WithoutToolMarkers). A copy with no marker
/// at all is given back whole; it must give exactly what going through it line by line gives, for copies with and
/// without markers.
/// </summary>
public sealed class ToolMarkersTests
{
    /// <summary>The writer's way, line by line, as it always went through every copy.</summary>
    private static string LineByLine(string text)
    {
        var lines = new List<string>();
        bool inOriginal = false;
        foreach (string line in text.Split('\n'))
        {
            if (Regex.IsMatch(line, @"\A[ \t]*//[ \t]*(?:YMB|YSM)-"))
            {
                inOriginal = Regex.IsMatch(line, @"\A[ \t]*//[ \t]*YMB-ORIGINAL(?![A-Za-z0-9_])");
                continue;
            }
            if (inOriginal && Regex.IsMatch(line, @"\A[ \t]*//")) continue;
            inOriginal = false;
            lines.Add(new Regex(@"[ \t]*//[ \t]*(?:YMB|YSM)-[^\r]*").Replace(line, "", 1));
        }
        return string.Join("\n", lines);
    }

    private static string WithoutToolMarkers(string text) =>
        (string)typeof(ExportWriter).GetMethod("WithoutToolMarkers", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [text])!;

    [Theory]
    [InlineData("export A is T\r\n(\r\n    X = 1 // a comment\r\n)\r\n")]
    [InlineData("A\n// YMB-ORIGINAL x\n// old line\nB // YSM-123\n")]
    [InlineData("A // YMB-1\r\n  // YSM-2\r\nB")]
    [InlineData("// ymb-lower case is not a marker\nYMB without a dash")]
    [InlineData("")]
    public void Gives_what_going_line_by_line_gave(string text) => Assert.Equal(LineByLine(text), WithoutToolMarkers(text));

    [Fact]
    public void Gives_what_going_line_by_line_gave_for_random_copies()
    {
        var random = new Random(20261031);
        string[] lines = ["export A is T", "(", ")", "    X = 1", "    // a comment", "// YMB-ORIGINAL", "// YMB-ORIGINALS", "  // YSM-12 marker",
            "    Y = 2 // YMB-3", "Z = 3 // YSM-", "\r", "", "    // old = 1", "YSM-", "// YMB"];
        for (int test = 0; test < 3000; test++)
        {
            string text = string.Join(random.Next(2) == 0 ? "\n" : "\r\n", Enumerable.Range(0, random.Next(0, 12)).Select(_ => lines[random.Next(lines.Length)]));
            Assert.Equal(LineByLine(text), WithoutToolMarkers(text));
        }
    }
}
