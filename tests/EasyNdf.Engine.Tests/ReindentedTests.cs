using System.Reflection;
using EasyNdf.Engine.Writing;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The export writer's indentation of a value a rule wrote on several lines (ExportWriter.Reindented): it goes through
/// the lines where they lie in the text, and must give exactly what splitting the text into lines and joining them
/// again gave, for every kind of line (blank, white space only, tabs, a lone "\r", other white space).
/// </summary>
public sealed class ReindentedTests
{
    /// <summary>What the writer did before: the text split into lines, each line after the first re-indented, and joined again.</summary>
    private static string SplitAndJoined(string written, string baseIndent)
    {
        if (!written.Contains('\n')) return written;
        string[] lines = written.Replace("\r\n", "\n").Split('\n');
        int shared = int.MaxValue;
        for (int k = 1; k < lines.Length; k++)
        {
            if (lines[k].Trim().Length == 0) continue;
            int indent = lines[k].Length - lines[k].TrimStart(' ', '\t').Length;
            shared = Math.Min(shared, indent);
        }
        if (shared == int.MaxValue) return written;
        for (int k = 1; k < lines.Length; k++)
            lines[k] = lines[k].Trim().Length == 0 ? "" : baseIndent + lines[k][shared..];
        return string.Join("\n", lines);
    }

    /// <summary>The writer's own, with the line being written so far (which gives the indentation).</summary>
    private static string Reindented(string written, string lineSoFar)
    {
        var writer = (ExportWriter)Activator.CreateInstance(typeof(ExportWriter), BindingFlags.NonPublic | BindingFlags.Instance, null, ["", "\n"], null)!;
        var pieces = (List<string>)typeof(ExportWriter).GetField("pieces", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(writer)!;
        pieces.Add("Before\n");
        pieces.Add(lineSoFar);
        return (string)typeof(ExportWriter).GetMethod("Reindented", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(writer, [written])!;
    }

    [Theory]
    [InlineData("[\n        A,\n        B,\n    ]", "    Units = ")]
    [InlineData("[\r\n\t\tA,\r\n\r\n\t\tB\r\n\t]", "\tUnits = ")]
    [InlineData("One line", "  ")]
    [InlineData("[\n\n   \n]\n", "    ")]
    [InlineData("A\n   B\n \v\n\rC\n", "\t ")]
    [InlineData("\n", "")]
    [InlineData("A\n    \n", "    X = ")]
    public void Gives_what_splitting_into_lines_gave(string written, string lineSoFar)
    {
        string indent = lineSoFar[..(lineSoFar.Length - lineSoFar.TrimStart(' ', '\t').Length)];
        Assert.Equal(SplitAndJoined(written, indent), Reindented(written, lineSoFar));
    }

    [Fact]
    public void Gives_what_splitting_into_lines_gave_for_random_texts()
    {
        var random = new Random(20261031);
        string[] parts = [" ", "  ", "\t", "\n", "\r\n", "\r", " ", "\v", "A", "Bc", ",", "[", "]", "// x"];
        for (int test = 0; test < 5000; test++)
        {
            string written = string.Concat(Enumerable.Range(0, random.Next(0, 30)).Select(_ => parts[random.Next(parts.Length)]));
            string lineSoFar = string.Concat(Enumerable.Range(0, random.Next(0, 4)).Select(_ => random.Next(3) switch { 0 => " ", 1 => "\t", _ => "X" }));
            string indent = lineSoFar[..(lineSoFar.Length - lineSoFar.TrimStart(' ', '\t').Length)];
            Assert.Equal(SplitAndJoined(written, indent), Reindented(written, lineSoFar));
        }
    }
}
