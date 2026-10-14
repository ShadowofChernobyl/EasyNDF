using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Ndf;

/// <summary>The kinds of tokens in NDF text.</summary>
public enum TokenKind
{
    /// <summary>GUID:{...}</summary>
    Guid,
    /// <summary>~/Name: a reference relative to the current file.</summary>
    RelativeReference,
    /// <summary>$/Path/Name: a reference to a declaration anywhere in the game.</summary>
    AbsoluteReference,
    /// <summary>Name/With/Slashes, or &lt;Name&gt;.</summary>
    Path,
    /// <summary>A plain word: a name, a keyword such as "is" or "export", True, nil...</summary>
    Identifier,
    /// <summary>A number, possibly with a minus sign: -12, 0.5, .5, 0xFF.</summary>
    Number,
    /// <summary>A quoted text on one line: 'Text' or "Text".</summary>
    String,
    /// <summary>One punctuation character: ( ) [ ] , = | : * + / % &lt; &gt; -</summary>
    Punctuation,
}

/// <summary>One token, with where it starts in the text.</summary>
public readonly record struct Token(TokenKind Kind, string Text, int Position);

/// <summary>
/// Splits NDF text into tokens and collects the comments between them.
/// </summary>
/// <remarks>
/// <para>
/// This does what the HTML app's tokenizer pattern does, written out by hand because a regular expression is about
/// twenty times slower here on the game's large files. At each position the first of these that matches wins:
/// </para>
/// <code>
///   [ \t\r\n]+                                   spaces and line breaks
///   //[^\r\n]*    /\*[\s\S]*?\*/                 comments
///   GUID:\{[0-9a-fA-F-]+\}                       Guid
///   ~(?:/[A-Za-z_]\w*)+                          RelativeReference
///   \$(?:/[A-Z_]\w*)*/[A-Za-z_]\w*               AbsoluteReference
///   [A-Z_]\w*(?:/[A-Z_]\w*)+   &lt;[A-Za-z_]\w*&gt;     Path
///   [A-Za-z_]\w*                                 Identifier
///   -?(?:0[xX][0-9a-fA-F]+|\d+(?:\.\d*)?|\.\d+)  Number
///   '[^'\r\n]*'   "[^"\r\n]*"                    String
///   [()\[\],=|:*+/%&lt;&gt;-]                          Punctuation
/// </code>
/// <para>\w and \d mean ASCII letters, digits and _ only. The test project checks this scanner against that pattern.</para>
/// </remarks>
public static class Tokenizer
{
    /// <summary>
    /// Reads the tokens of a text. Comments are not tokens: each group of comments is filed under the index of the
    /// token that follows it (or the number of tokens, for comments at the very end).
    /// </summary>
    public static (List<Token> Tokens, SortedDictionary<int, List<SourceComment>> CommentsBefore) Read(string text)
    {
        var tokens = new List<Token>();
        var commentsBefore = new SortedDictionary<int, List<SourceComment>>();
        List<SourceComment>? waiting = null; // comments waiting for the next token
        bool newLineSeen = true;             // a line break has been seen since the last token

        int at = 0;
        while (at < text.Length)
        {
            int spaces = SpacesAt(text, at);
            if (spaces > 0)
            {
                if (!newLineSeen && text.AsSpan(at, spaces).Contains('\n')) newLineSeen = true;
                at += spaces;
                continue;
            }

            int comment = CommentAt(text, at);
            if (comment > 0)
            {
                (waiting ??= []).Add(new SourceComment(text.Substring(at, comment), SameLine: !newLineSeen));
                at += comment;
                continue;
            }

            var (kind, length) = TokenAt(text, at);
            if (length == 0)
            {
                string snippet = JsText.Slice(text, at, at + 20);
                int nl = snippet.IndexOf('\n');
                if (nl >= 0) snippet = snippet[..(nl > 0 && snippet[nl - 1] == '\r' ? nl - 1 : nl)];
                throw new NdfParseException("Line " + JsText.LineNumberAt(text, at) + ": unrecognized text \"" + snippet + "\"");
            }

            if (waiting is not null)
            {
                commentsBefore[tokens.Count] = waiting;
                waiting = null;
            }
            tokens.Add(new Token(kind, text.Substring(at, length), at));
            newLineSeen = false;
            at += length;
        }
        if (waiting is not null) commentsBefore[tokens.Count] = waiting;
        return (tokens, commentsBefore);
    }

    // Each scanner below returns how many characters it matches at a position, or 0 when it does not match there.

    private static bool IsWordStart(char c) => char.IsAsciiLetter(c) || c == '_';
    private static bool IsUpperStart(char c) => char.IsAsciiLetterUpper(c) || c == '_';
    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    private static char At(string text, int i) => i < text.Length ? text[i] : '\0';

    /// <summary>[ \t\r\n]+</summary>
    private static int SpacesAt(string text, int at)
    {
        int i = at;
        while (i < text.Length && text[i] is ' ' or '\t' or '\r' or '\n') i++;
        return i - at;
    }

    /// <summary>//[^\r\n]* or /\*[\s\S]*?\*/ (a /* with no closing */ is not a comment).</summary>
    private static int CommentAt(string text, int at)
    {
        if (At(text, at) != '/') return 0;
        if (At(text, at + 1) == '/')
        {
            int i = at + 2;
            while (i < text.Length && text[i] is not '\r' and not '\n') i++;
            return i - at;
        }
        if (At(text, at + 1) == '*')
        {
            int close = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
            return close < 0 ? 0 : close + 2 - at;
        }
        return 0;
    }

    /// <summary>The token at a position: the first kind that matches, in the order of the pattern.</summary>
    private static (TokenKind Kind, int Length) TokenAt(string text, int at)
    {
        int n;
        if ((n = GuidAt(text, at)) > 0) return (TokenKind.Guid, n);
        if ((n = RelativeReferenceAt(text, at)) > 0) return (TokenKind.RelativeReference, n);
        if ((n = AbsoluteReferenceAt(text, at)) > 0) return (TokenKind.AbsoluteReference, n);
        if ((n = PathAt(text, at)) > 0) return (TokenKind.Path, n);
        if ((n = WordAt(text, at)) > 0) return (TokenKind.Identifier, n);
        if ((n = NumberAt(text, at)) > 0) return (TokenKind.Number, n);
        if ((n = StringAt(text, at)) > 0) return (TokenKind.String, n);
        if (At(text, at) is '(' or ')' or '[' or ']' or ',' or '=' or '|' or ':' or '*' or '+' or '/' or '%' or '<' or '>' or '-')
            return (TokenKind.Punctuation, 1);
        return (TokenKind.Punctuation, 0);
    }

    /// <summary>[A-Za-z_]\w*</summary>
    private static int WordAt(string text, int at)
    {
        if (!IsWordStart(At(text, at))) return 0;
        int i = at + 1;
        while (i < text.Length && IsWordChar(text[i])) i++;
        return i - at;
    }

    /// <summary>"/" followed by a word, where the word's first letter must pass <paramref name="firstLetter"/>.</summary>
    private static int SegmentAt(string text, int at, Func<char, bool> firstLetter)
    {
        if (At(text, at) != '/' || !firstLetter(At(text, at + 1))) return 0;
        return 1 + WordAt(text, at + 1);
    }

    /// <summary>GUID:\{[0-9a-fA-F-]+\}</summary>
    private static int GuidAt(string text, int at)
    {
        if (string.CompareOrdinal(text, at, "GUID:{", 0, 6) != 0) return 0;
        int i = at + 6;
        while (i < text.Length && (char.IsAsciiHexDigit(text[i]) || text[i] == '-')) i++;
        if (i == at + 6 || At(text, i) != '}') return 0;
        return i + 1 - at;
    }

    /// <summary>~(?:/[A-Za-z_]\w*)+</summary>
    private static int RelativeReferenceAt(string text, int at)
    {
        if (At(text, at) != '~') return 0;
        int i = at + 1, n;
        while ((n = SegmentAt(text, i, IsWordStart)) > 0) i += n;
        return i == at + 1 ? 0 : i - at;
    }

    /// <summary>
    /// \$(?:/[A-Z_]\w*)*/[A-Za-z_]\w* : "$", then segments starting with a capital (or _), then one last segment that may
    /// start with any letter.
    /// </summary>
    private static int AbsoluteReferenceAt(string text, int at)
    {
        if (At(text, at) != '$') return 0;
        int i = at + 1, n;
        int lastSegmentStart = -1;
        while ((n = SegmentAt(text, i, IsUpperStart)) > 0)
        {
            lastSegmentStart = i;
            i += n;
        }
        // One more segment that starts with a small letter ends the reference...
        if ((n = SegmentAt(text, i, IsWordStart)) > 0) return i + n - at;
        // ...otherwise the last capital segment is the final one, if there was any.
        return lastSegmentStart < 0 ? 0 : i - at;
    }

    /// <summary>[A-Z_]\w*(?:/[A-Z_]\w*)+ or &lt;[A-Za-z_]\w*&gt;</summary>
    private static int PathAt(string text, int at)
    {
        char c = At(text, at);
        if (c == '<')
        {
            int word = WordAt(text, at + 1);
            return word > 0 && At(text, at + 1 + word) == '>' ? word + 2 : 0;
        }
        if (!IsUpperStart(c)) return 0;
        int i = at + WordAt(text, at), n, segments = 0;
        while ((n = SegmentAt(text, i, IsUpperStart)) > 0) { i += n; segments++; }
        return segments == 0 ? 0 : i - at;
    }

    /// <summary>-?(?:0[xX][0-9a-fA-F]+|\d+(?:\.\d*)?|\.\d+)</summary>
    private static int NumberAt(string text, int at)
    {
        int i = at;
        if (At(text, i) == '-') i++;
        int start = i;

        if (At(text, i) == '0' && At(text, i + 1) is 'x' or 'X' && char.IsAsciiHexDigit(At(text, i + 2)))
        {
            i += 2;
            while (i < text.Length && char.IsAsciiHexDigit(text[i])) i++;
            return i - at;
        }
        if (char.IsAsciiDigit(At(text, i)))
        {
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            if (At(text, i) == '.')
            {
                i++;
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            }
            return i - at;
        }
        if (At(text, i) == '.' && char.IsAsciiDigit(At(text, i + 1)))
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            return i - at;
        }
        return 0;
    }

    /// <summary>'[^'\r\n]*' or "[^"\r\n]*"</summary>
    private static int StringAt(string text, int at)
    {
        char quote = At(text, at);
        if (quote is not '\'' and not '"') return 0;
        int i = at + 1;
        while (i < text.Length && text[i] != quote && text[i] is not '\r' and not '\n') i++;
        return At(text, i) == quote ? i + 1 - at : 0;
    }
}
