using System.Text;
using System.Text.RegularExpressions;
using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Editing;

/// <summary>A coloured part of a line: where it starts and ends, and its colour's name ("comment", "keyword"...).</summary>
public sealed record ColouredPart(int Start, int End, string Colour);

/// <summary>
/// The File Editor's colouring of NDF text, as the HTML app colours it (its <c>SYN</c>, <c>tokLine</c>, <c>edLineEnd</c> and
/// <c>edStates</c>). The rules are those of the NDF.tmLanguage file made for Sublime Text, in the same order. Each line is
/// coloured on its own, given the block it starts inside: none, <c>{ }</c>, <c>/* */</c>, <c>'...'</c> or <c>"..."</c>, so
/// any line can be coloured without colouring the ones above it. The colour names are the theme's "s-" colours.
/// </summary>
public static class NdfColouring
{
    /// <summary>The block a line starts inside: outside any, in braces, in a /* */ comment, in '...' or in "...".</summary>
    public const int Outside = 0, InBraces = 1, InComment = 2, InSingleQuotes = 3, InDoubleQuotes = 4;

    /// <summary>
    /// One colouring rule: a pattern that colours what it matches (<see cref="Colour"/>), or only some of its groups
    /// (<see cref="GroupColours"/>), or that starts a block running to <see cref="End"/>, perhaps over several lines.
    /// </summary>
    private sealed record Rule(Regex Pattern, string? Colour, Regex? End = null, int Block = Outside, (int Group, string Colour)[]? GroupColours = null);

    /// <summary>The rules, in the HTML app's order, with its patterns as they are written there (see <see cref="FromJavaScript"/>).</summary>
    private static readonly Rule[] Rules =
    [
        Match(@"(//).*$", "comment"),
        BlockRule(@"\{", @"\}", "comment", InBraces),
        BlockRule(@"/\*", @"\*/", "comment", InComment),
        Match(@"\b(string|int|bool|float|float2|float3|float4|boolean|MAP|VECTOR)\b", "keyword"),
        Match(@"\b(is|not|or|and|div|mod|[$])\b", "keyword"),
        Match(@"\bmeta\b", "type"),
        Match(@"\bT[A-Z][a-zA-Z0-9]*_[a-zA-Z0-9]+\b", "const"),
        Match(@"\bT[A-Z][a-zA-Z0-9_]*\b", "type"),
        Groups(@"\b(template|Template)\s+([a-zA-Z0-9_]+)\b", (1, "keyword"), (2, "func")),
        Groups(@"\b([a-zA-Z0-9_]+)\s*(\()", (1, "func")),
        Match(@"\b(template|Template|export|Export|public|Public|private|Private|package|Package|unnamed|Unnamed|required|Required)\b", "keyword"),
        Match(@"\b(nil|true|True|false|False)\b", "const"),
        Match(@"\b(?<![\d.])\s0x[a-fA-F\d]+|\b\d+(\.\d+)?([eE]-?\d+)?|\.\d+([eE]-?\d+)?\b", "number"),
        Groups(@"([a-zA-Z0-9_]+)\s+(is)\b", (1, "name"), (2, "keyword")),
        Groups(@"([a-zA-Z0-9_]+)\s*=", (1, "field")),
        Match(@"[~.$]/[a-zA-Z][a-zA-Z0-9_/.]*", "ref"),
        Match(@"\B<[a-zA-Z0-9_]+>", "param"),
        BlockRule("'", "'", "string", InSingleQuotes),
        BlockRule("\"", "\"", "string", InDoubleQuotes),
        Match(@"\b(template|Template)[a-zA-Z0-9_]+\b", "func"),
    ];

    private static Rule Match(string pattern, string colour) => new(FromJavaScript(pattern), colour);

    private static Rule BlockRule(string start, string end, string colour, int block) => new(FromJavaScript(start), colour, FromJavaScript(end), block);

    private static Rule Groups(string pattern, params (int Group, string Colour)[] colours) => new(FromJavaScript(pattern), null, GroupColours: colours);

    /// <summary>The rule that ends each kind of block, by the block's number.</summary>
    private static Rule BlockEnding(int block) => Rules.First(r => r.Block == block);

    /// <summary>
    /// Colours one line (the HTML app's <c>tokLine</c>), given the block it starts inside. Returns its coloured parts, in
    /// order, and the block the line ends inside.
    /// </summary>
    public static (List<ColouredPart> Parts, int EndsInside) ColourLine(string line, int startsInside)
    {
        var parts = new List<ColouredPart>();
        int at = 0;
        void Colour(int start, int end, string colour)
        {
            // an empty part is not coloured, as the HTML app's span() leaves it out
            if (end > start) parts.Add(new ColouredPart(start, end, colour));
        }

        if (startsInside != Outside)
        {
            Rule block = BlockEnding(startsInside);
            System.Text.RegularExpressions.Match end = block.End!.Match(line, 0);
            if (!end.Success)
            {
                Colour(0, line.Length, block.Colour!);
                return (parts, startsInside);
            }
            at = end.Index + end.Length;
            Colour(0, at, block.Colour!);
        }

        // Each rule's next match from where the line has got to; a rule that matches nothing more is never asked again.
        var next = new System.Text.RegularExpressions.Match?[Rules.Length];
        var searched = new bool[Rules.Length];
        while (at < line.Length)
        {
            System.Text.RegularExpressions.Match? best = null;
            int bestRule = -1;
            for (int i = 0; i < Rules.Length; i++)
            {
                if (!searched[i] || (next[i] is { } stale && stale.Index < at))
                {
                    System.Text.RegularExpressions.Match found = Rules[i].Pattern.Match(line, at);
                    next[i] = found.Success ? found : null;
                    searched[i] = true;
                }
                // the earliest match wins; of two that start at the same place, the earlier rule
                if (next[i] is { } match && (best is null || match.Index < best.Index))
                {
                    best = match;
                    bestRule = i;
                    if (match.Index == at) break;
                }
            }
            if (best is null) break;

            Rule rule = Rules[bestRule];
            int start = best.Index, stop = start + best.Length;
            if (rule.End is not null)
            {
                System.Text.RegularExpressions.Match end = rule.End.Match(line, stop);
                if (!end.Success)
                {
                    // the block runs on to the next line
                    Colour(start, line.Length, rule.Colour!);
                    return (parts, rule.Block);
                }
                int after = end.Index + end.Length;
                Colour(start, after, rule.Colour!);
                at = after;
            }
            else if (rule.Colour is not null)
            {
                Colour(start, stop, rule.Colour);
                at = stop;
            }
            else
            {
                // only some groups are coloured; one that does not start after the last coloured part is left as it is
                int coloured = start;
                foreach ((int group, string colour) in rule.GroupColours!)
                {
                    Group part = best.Groups[group];
                    if (!part.Success || part.Index < coloured) continue;
                    Colour(part.Index, part.Index + part.Length, colour);
                    coloured = part.Index + part.Length;
                }
                at = stop;
            }
            // a match of nothing moves on by one character, so the line is always finished
            if (stop == start) at = start + 1;
        }
        return (parts, Outside);
    }

    /// <summary>The ends of the blocks a line can start inside, by the block's number.</summary>
    internal static readonly string?[] BlockEnds = [null, "}", "*/", "'", "\""];

    /// <summary>What starts a block, or a // comment that ends the line.</summary>
    private static readonly Regex BlockStart = new(@"//|/\*|[{'""]", RegexOptions.CultureInvariant);

    /// <summary>
    /// The block a line ends inside, given the one it starts inside (the HTML app's <c>edLineEnd</c>): a quick reading
    /// that only looks for the starts and ends of blocks.
    /// </summary>
    public static int LineEndsInside(string line, int startsInside)
    {
        int at = 0, inside = startsInside;
        while (true)
        {
            if (inside != Outside)
            {
                int end = JsText.IndexOf(line, BlockEnds[inside]!, at);
                if (end < 0) return inside;
                at = end + BlockEnds[inside]!.Length;
                inside = Outside;
            }
            System.Text.RegularExpressions.Match found = BlockStart.Match(line, at);
            if (!found.Success) return Outside;
            string start = found.Value;
            if (start == "//") return Outside;
            inside = start switch { "{" => InBraces, "/*" => InComment, "'" => InSingleQuotes, _ => InDoubleQuotes };
            at = found.Index + start.Length;
        }
    }

    /// <summary>
    /// The block every line starts inside (the HTML app's <c>edStates</c>), with one more entry for after the last line.
    /// </summary>
    public static int[] BlocksOfLines(IReadOnlyList<string> lines)
    {
        var inside = new int[lines.Count + 1];
        int block = Outside;
        for (int i = 0; i < lines.Count; i++)
        {
            inside[i] = block;
            block = LineEndsInside(lines[i], block);
        }
        inside[lines.Count] = block;
        return inside;
    }

    /// <summary>A letter, digit or underscore: what JavaScript's \b and \w count as a word.</summary>
    private const string WordCharacter = "[A-Za-z0-9_]";

    /// <summary>
    /// A JavaScript pattern for .NET, as the engine's patterns follow JavaScript. JavaScript's <c>\b</c>, <c>\B</c>,
    /// <c>\d</c> and <c>\w</c> know only ASCII letters and digits, and its <c>\s</c> has its own list of white space; .NET's
    /// know all of Unicode. Each is written out as JavaScript means it; everything else is read the same by both.
    /// </summary>
    public static Regex FromJavaScript(string pattern)
    {
        var result = new StringBuilder();
        bool inClass = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                char escaped = pattern[++i];
                result.Append((escaped, inClass) switch
                {
                    ('d', true) => "0-9",
                    ('d', false) => "[0-9]",
                    ('w', true) => "A-Za-z0-9_",
                    ('w', false) => WordCharacter,
                    // JavaScript's white space: .NET's, less U+0085, with U+FEFF (see JsText.IsWhiteSpace)
                    ('s', _) => inClass ? @"\s﻿" : @"[\s﻿-[\u0085]]",
                    ('b', false) => $"(?:(?<={WordCharacter})(?!{WordCharacter})|(?<!{WordCharacter})(?={WordCharacter}))",
                    ('B', false) => $"(?:(?<={WordCharacter})(?={WordCharacter})|(?<!{WordCharacter})(?!{WordCharacter}))",
                    _ => "\\" + escaped,
                });
                continue;
            }
            if (c == '[' && !inClass) inClass = true;
            else if (c == ']' && inClass) inClass = false;
            // JavaScript's $ without the m flag is the very end; a line has no line break in it
            if (c == '$' && !inClass && (i + 1 == pattern.Length || pattern[i + 1] == '|' || pattern[i + 1] == ')'))
            {
                result.Append(@"\z");
                continue;
            }
            result.Append(c);
        }
        return new Regex(result.ToString(), RegexOptions.CultureInvariant);
    }
}
