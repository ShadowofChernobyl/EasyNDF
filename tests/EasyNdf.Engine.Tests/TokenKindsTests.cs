using System.Text;
using System.Text.RegularExpressions;
using EasyNdf.Engine.Ndf;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The tokenizer picks the kinds to try at a position by the character there, rather than trying every kind in turn.
/// The order of the kinds still decides between those that can start alike, so it must split text exactly as the
/// pattern below does, which tries every kind in turn: the HTML app's pattern with the punctuation only the desktop
/// app reads (? ! &amp; .). Unlike <see cref="TokenizerTests"/>, text neither can read is compared too: both must refuse it
/// at the same place. ({ } comments, which only the desktop app reads, are left out of the texts.)
/// </summary>
public partial class TokenKindsTests
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
          | (?<punct> [()\[\],=|:*+/%<>\-?!&.] )
        )
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex EveryKindInTurn();

    private static readonly string[] Kinds = ["guid", "rel", "abs", "path", "id", "num", "str", "punct"];

    private static readonly Dictionary<TokenKind, string> KindNames = new()
    {
        [TokenKind.Guid] = "guid", [TokenKind.RelativeReference] = "rel", [TokenKind.AbsoluteReference] = "abs",
        [TokenKind.Path] = "path", [TokenKind.Identifier] = "id", [TokenKind.Number] = "num",
        [TokenKind.String] = "str", [TokenKind.Punctuation] = "punct",
    };

    /// <summary>The tokens and comments the pattern finds, or where it can read no further.</summary>
    private static string WithPattern(string text)
    {
        var output = new StringBuilder();
        int at = 0;
        while (at < text.Length)
        {
            var m = EveryKindInTurn().Match(text, at);
            if (!m.Success) return "ERROR at " + at;
            at += m.Length;
            if (m.Groups["space"].Success) continue;
            if (m.Groups["comment"].Success) { output.Append("comment:").Append(m.Value).Append('\u0001'); continue; }
            output.Append(Kinds.First(k => m.Groups[k].Success)).Append(':').Append(m.Index).Append(':').Append(m.Value).Append('\u0001');
        }
        return output.ToString();
    }

    /// <summary>The same from the tokenizer; where it refuses the text, the place its error gives.</summary>
    private static string WithTokenizer(string text)
    {
        List<Token> tokens;
        SortedDictionary<int, List<SourceComment>> comments;
        try { (tokens, comments) = Tokenizer.Read(text); }
        catch (NdfParseException e) { return "ERROR at " + e.Position; }
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

    [Theory]
    [InlineData("GUID:{0a-F} GUIDE GUID:{-} GUID:x G")]
    [InlineData("GUID:{fg25aaea-9679} GUIDS GUID:{Zz-9}")]
    [InlineData("Path/To/Name Path/to Path/ Name _Under/_Score <Name> <name <")]
    [InlineData("-5 - 5 -.5 .5 . -0x1F 0x 1. ~/a ~/ ~ $/A/b $/a $/A/B $ $/")]
    [InlineData("'one' \"two\" 'open")]
    [InlineData("a ? b ! c & d . e == f != g")]
    public void Text_that_starts_tokens_alike_is_split_as_trying_every_kind_in_turn_splits_it(string text) =>
        Assert.Equal(WithPattern(text), WithTokenizer(text));

    [Fact]
    public void Random_text_is_split_or_refused_as_trying_every_kind_in_turn_does()
    {
        // characters that start or end tokens, so the random text often has tokens that start alike
        const string alphabet = "aZ_9x0X.-/*~$<>'\" \t\r\n()[],=|:+%GUID?!&fF";
        var random = new Random(4321);
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
            Assert.True(WithPattern(s) == WithTokenizer(s), "text: " + System.Text.Json.JsonSerializer.Serialize(s));
        }
    }
}
