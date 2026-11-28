using System.Text;
using System.Text.RegularExpressions;
using EasyNdf.Engine.Ndf;
using EngineCheck;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The hand-written tokenizer must split text exactly as the HTML app's tokenizer pattern does. This test keeps that
/// pattern, alternative for alternative, and compares the two on random text and on every test case's file.
/// </summary>
public partial class TokenizerTests
{
    [GeneratedRegex("""
        \G(?:
            (?<space> [\x20\t\r\n]+ )
          | (?<comment> //[^\r\n]* | /\*[\s\S]*?\*/ )
          | (?<guid> GUID:\{[0-9a-zA-Z-]+\} )
          | (?<rel> ~(?:/[A-Za-z_][A-Za-z0-9_]*)+ )
          | (?<abs> \$(?:/[A-Z_][A-Za-z0-9_]*)*/[A-Za-z_][A-Za-z0-9_]* )
          | (?<path> [A-Z_][A-Za-z0-9_]*(?:/[A-Z_][A-Za-z0-9_]*)+ | <[A-Za-z_][A-Za-z0-9_]*> )
          | (?<id> [A-Za-z_][A-Za-z0-9_]* )
          | (?<num> -?(?:0[xX][0-9a-fA-F]+|[0-9]+(?:\.[0-9]*)?|\.[0-9]+) )
          | (?<str> '[^'\r\n]*' | "[^"\r\n]*" )
          | (?<punct> [()\[\],=|:*+/%<>-] )
        )
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlAppPattern();

    private static readonly string[] Kinds = ["guid", "rel", "abs", "path", "id", "num", "str", "punct"];

    /// <summary>The tokens and comments the pattern finds, as one string, or the error.</summary>
    private static string WithPattern(string text)
    {
        var output = new StringBuilder();
        int at = 0;
        while (at < text.Length)
        {
            var m = HtmlAppPattern().Match(text, at);
            if (!m.Success) return output.Append("ERROR at " + at).ToString();
            at += m.Length;
            if (m.Groups["space"].Success) continue;
            if (m.Groups["comment"].Success) { output.Append("comment:").Append(m.Value).Append('\u0001'); continue; }
            string kind = Kinds.First(k => m.Groups[k].Success);
            output.Append(kind).Append(':').Append(m.Index).Append(':').Append(m.Value).Append('\u0001');
        }
        return output.ToString();
    }

    private static readonly Dictionary<TokenKind, string> KindNames = new()
    {
        [TokenKind.Guid] = "guid", [TokenKind.RelativeReference] = "rel", [TokenKind.AbsoluteReference] = "abs",
        [TokenKind.Path] = "path", [TokenKind.Identifier] = "id", [TokenKind.Number] = "num",
        [TokenKind.String] = "str", [TokenKind.Punctuation] = "punct",
    };

    /// <summary>The same, from the tokenizer.</summary>
    private static string WithTokenizer(string text)
    {
        List<Token> tokens;
        SortedDictionary<int, List<SourceComment>> comments;
        try { (tokens, comments) = Tokenizer.Read(text); }
        catch (NdfParseException) { return "ERROR"; }
        var output = new StringBuilder();
        for (int i = 0; i <= tokens.Count; i++)
        {
            if (comments.TryGetValue(i, out var group))
                foreach (var c in group) output.Append("comment:").Append(c.Text).Append('\u0001');
            if (i < tokens.Count)
                output.Append(KindNames[tokens[i].Kind]).Append(':').Append(tokens[i].Position).Append(':').Append(tokens[i].Text).Append('\u0001');
        }
        return output.ToString();
    }

    [Fact]
    public void Random_text_is_split_the_same_way()
    {
        // Characters that start or end tokens, so the random text hits the edge cases often.
        const string alphabet = "aZ_9x0X.-/*~$<>'\" \t\r\n()[],=|:+%GUID{}fF";
        var random = new Random(12345);
        for (int n = 0; n < 20000; n++)
        {
            var text = new StringBuilder();
            int length = random.Next(1, 30);
            for (int i = 0; i < length; i++)
            {
                text.Append(random.Next(10) switch
                {
                    0 => "GUID:{",
                    1 => "$/",
                    2 => "/*",
                    3 => "*/",
                    _ => alphabet[random.Next(alphabet.Length)].ToString(),
                });
            }
            string s = text.ToString();
            string expected = WithPattern(s);
            string actual = WithTokenizer(s);
            // Text the HTML app's pattern cannot read may still be read here: the desktop app reads more of the NDF
            // language ({ } comments, ?, !, &, ., ==...), which game files outside the usual ones use. Text both read
            // must be split the same way.
            if (expected.Contains("ERROR")) continue;
            Assert.True(expected == actual, "text: " + System.Text.Json.JsonSerializer.Serialize(s) + "\nexpected " + expected + "\nactual   " + actual);
        }
    }

    [Fact]
    public void Every_test_case_file_is_split_the_same_way()
    {
        var cases = EngineCases.ReadCases(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", "engine-cases.json")));
        foreach (var c in cases)
        {
            string expected = WithPattern(c.Text);
            if (expected.Contains("ERROR")) continue;
            Assert.Equal(expected, WithTokenizer(c.Text));
        }
    }
}
