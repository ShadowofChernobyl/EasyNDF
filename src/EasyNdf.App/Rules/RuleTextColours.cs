using System.Text.RegularExpressions;

namespace EasyNdf.App.Rules;

/// <summary>What a rule's text box holds, which decides how its text is coloured.</summary>
public enum RuleTextKind
{
    /// <summary>Plain text: not coloured.</summary>
    None,

    /// <summary>A Formula rule's formula: <c>round(value * 2, 25)</c>.</summary>
    Formula,

    /// <summary>A property name or path, which may pick one item of a list: <c>Weapons[LastIndex-1]</c>.</summary>
    Path,

    /// <summary>A condition's or action's value, which may start with a list position: <c>LastIndex: 'Text'</c>.</summary>
    Value,
}

/// <summary>A piece of a text box's text and the theme colour it is drawn in (one of the File Editor's colours, or "ink").</summary>
/// <param name="Start">Where the piece starts in the text.</param>
/// <param name="Length">How long it is; it never holds a line break.</param>
/// <param name="Colour">The theme colour's key, for example "s-keyword".</param>
public readonly record struct ColouredPiece(int Start, int Length, string Colour);

/// <summary>
/// Splits the text of a rule's formula, path or value box into pieces coloured as the File Editor colours NDF text, so
/// the parts the engine reads in a special way stand out:
/// <list type="bullet">
/// <item><c>value</c>, <c>FirstIndex</c> and <c>LastIndex</c>: keywords;</item>
/// <item>numbers;</item>
/// <item>the formula functions (<c>min</c>, <c>max</c>, <c>abs</c>, <c>floor</c>, <c>ceil</c>, <c>round</c>);</item>
/// <item>property names, and names with a slash in square brackets in a formula;</item>
/// <item><c>{Variable}</c> names;</item>
/// <item>in a value: 'text', "text" and references such as <c>$/GFX/...</c>.</item>
/// </list>
/// Everything else is in the usual text colour.
/// </summary>
public static partial class RuleTextColours
{
    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase) { "min", "max", "abs", "floor", "ceil", "round" };

    /// <summary>The pieces of a text, in order, covering all of it except its line breaks.</summary>
    public static List<ColouredPiece> Pieces(string text, RuleTextKind kind)
    {
        var pieces = new List<ColouredPiece>();
        if (kind == RuleTextKind.None) return pieces;
        int position = 0;
        if (kind == RuleTextKind.Value)
        {
            // "LastIndex - 1: value": the list position before the colon, for Insert and Replace
            Match listPosition = ListPosition().Match(text);
            if (listPosition.Success)
            {
                AddListPosition(pieces, text, listPosition.Length);
                position = listPosition.Length;
            }
        }

        Regex tokens = kind switch
        {
            RuleTextKind.Formula => FormulaToken(),
            RuleTextKind.Path => PathToken(),
            _ => ValueToken(),
        };
        foreach (Match token in tokens.Matches(text, position))
        {
            if (token.Index > position) AddPlain(pieces, text, position, token.Index - position);
            AddPlain(pieces, text, token.Index, token.Length, ColourOf(text, token, kind));
            position = token.Index + token.Length;
        }
        if (position < text.Length) AddPlain(pieces, text, position, text.Length - position);
        return pieces;
    }

    /// <summary>The colour of one token: what it is, and for a word, which word.</summary>
    private static string ColourOf(string text, Match token, RuleTextKind kind)
    {
        string word = token.Value;
        if (word.StartsWith('{')) return "s-const";
        if (word.StartsWith('[')) return "s-field";
        if (word.StartsWith('\'') || word.StartsWith('"')) return "s-string";
        if (word.StartsWith('$') || word.StartsWith('~')) return "s-ref";
        if (char.IsAsciiDigit(word[0]) || word[0] is '.' or '-') return "s-number";
        if (IsIndexWord(word)) return "s-keyword";
        if (kind == RuleTextKind.Formula)
        {
            if (word.Equals("value", StringComparison.OrdinalIgnoreCase)) return "s-keyword";
            if (Functions.Contains(word) && NextNonSpace(text, token.Index + token.Length) == '(') return "s-func";
            return "s-field";
        }
        // in a value, a bare word is NDF text the engine writes as it is
        return kind == RuleTextKind.Path ? "s-field" : "ink";
    }

    private static bool IsIndexWord(string word) =>
        word.Equals("FirstIndex", StringComparison.OrdinalIgnoreCase) || word.Equals("LastIndex", StringComparison.OrdinalIgnoreCase);

    private static string IndexWordOrNumber(string part) =>
        IsIndexWord(part) ? "s-keyword" : char.IsAsciiDigit(part[0]) ? "s-number" : "ink";

    private static char NextNonSpace(string text, int from)
    {
        for (int i = from; i < text.Length; i++) if (!char.IsWhiteSpace(text[i])) return text[i];
        return '\0';
    }

    /// <summary>Adds the list position at the start of a value: its word and numbers each in their colour, the rest plain.</summary>
    private static void AddListPosition(List<ColouredPiece> pieces, string text, int length)
    {
        int position = 0;
        foreach (Match word in WordOrNumber().Matches(text[..length]))
        {
            if (word.Index > position) AddPlain(pieces, text, position, word.Index - position);
            AddPlain(pieces, text, word.Index, word.Length, IndexWordOrNumber(word.Value));
            position = word.Index + word.Length;
        }
        if (position < length) AddPlain(pieces, text, position, length - position);
    }

    /// <summary>Adds a piece in one colour, split at its line breaks (which are left out).</summary>
    private static void AddPlain(List<ColouredPiece> pieces, string text, int start, int length, string colour = "ink")
    {
        int end = start + length;
        int lineStart = start;
        for (int i = start; i <= end; i++)
        {
            if (i < end && text[i] != '\n' && text[i] != '\r') continue;
            if (i > lineStart) pieces.Add(new ColouredPiece(lineStart, i - lineStart, colour));
            lineStart = i + 1;
        }
    }

    // a list position at the start of a value: FirstIndex, LastIndex or a number, maybe plus or minus a number, then ":"
    [GeneratedRegex(@"\A\s*(?:lastindex|firstindex|[0-9]+)\s*(?:[+-]\s*[0-9]+)?\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ListPosition();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*|[0-9]+")]
    private static partial Regex WordOrNumber();

    // a variable, a [name/with/slash], a number, or a word
    [GeneratedRegex(@"\{[A-Za-z0-9_]+\}|\[[^\]\r\n]*\]|[0-9]+(?:\.[0-9]+)?|\.[0-9]+|[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex FormulaToken();

    // a variable, a number, or a word (FirstIndex and LastIndex inside square brackets are words too)
    [GeneratedRegex(@"\{[A-Za-z0-9_]+\}|[0-9]+(?:\.[0-9]+)?|[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex PathToken();

    // a variable, 'text', "text", a reference, a number (with its sign), or a word
    [GeneratedRegex(@"\{[A-Za-z0-9_]+\}|'[^'\r\n]*'?|""[^""\r\n]*""?|[$~]/[A-Za-z0-9_/]*|-?[0-9]+(?:\.[0-9]+)?|[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex ValueToken();
}
