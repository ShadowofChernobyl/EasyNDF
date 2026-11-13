using System.Text.RegularExpressions;
using EasyNdf.Engine.Editing;
using EasyNdf.Engine.Viewer;

namespace EasyNdf.App.Editor;

/// <summary>A value a template declares: "MagnifiableHeight : float = 24.0".</summary>
/// <param name="Name">The value's name.</param>
/// <param name="Type">Its declared type; "" when none is written.</param>
/// <param name="Default">Its default as written; null when it has none.</param>
public sealed record TemplateValue(string Name, string Type, string? Default);

/// <summary>A template's first lines: its name, the values in its [ ], and what it is built on.</summary>
/// <param name="Name">The template's name.</param>
/// <param name="BuiltOn">What follows "is" after the [ ]; "" when it can't be read.</param>
/// <param name="Values">The values it declares, in order.</param>
/// <param name="Open">Where its "[" is in the text.</param>
/// <param name="Close">Where its "]" is in the text.</param>
public sealed record TemplateSignature(string Name, string BuiltOn, IReadOnlyList<TemplateValue> Values, int Open, int Close);

/// <summary>An object's first settings that have a value, for a name's tip.</summary>
/// <param name="Shown">Up to <see cref="SyntaxWords.MostKeyValues"/> settings and their values (the first line of each).</param>
/// <param name="Total">How many settings the object has.</param>
/// <param name="ConstantValue">For a constant (not an object), its value; null for an object.</param>
public sealed record KeyValues(IReadOnlyList<(string Key, string Value)> Shown, int Total, string? ConstantValue);

/// <summary>
/// The File Editor's words that have a tip of their own (Paul, 2026-11-11: "I want all colored syntax in the editor to
/// have a tooltip of its own"): which coloured word a place is on, and what the text says about it: a keyword's note,
/// a template's values, a value's declared type and default, an object's first settings.
/// </summary>
public static class SyntaxWords
{
    /// <summary>The most settings a name's tip lists; the rest are counted.</summary>
    public const int MostKeyValues = 4;

    /// <summary>The longest value a name's tip shows in full; a longer one is cut.</summary>
    private const int LongestValue = 70;

    /// <summary>The coloured word of a line at a column (from the line's start), as the editor colours it; null where nothing is.</summary>
    public static (ColouredPart Part, string Word)? PartAt(string line, int startsInside, int column)
    {
        foreach (ColouredPart part in NdfColouring.ColourLine(line, startsInside).Parts)
            if (column >= part.Start && column < part.End) return (part, line[part.Start..part.End]);
        return null;
    }

    /// <summary>
    /// What a comment's tip is headed with (Paul, 2026-11-12: "program all {} comments to display as {}"): {} for a { }
    /// comment, on its first line or on one inside it; otherwise its first two characters (// or /*).
    /// </summary>
    public static string CommentMark(string word, bool insideBraces) =>
        insideBraces || word.StartsWith('{') ? "{}" : word.Length > 2 ? word[..2] : word;

    /// <summary>The text name of a keyword's note ("Editor.Syntax.Keyword.export"); null for a word with no note.</summary>
    public static string? KeywordNote(string word) => word.ToLowerInvariant() switch
    {
        "is" => "Editor.Syntax.Keyword.is",
        "export" => "Editor.Syntax.Keyword.export",
        "private" => "Editor.Syntax.Keyword.private",
        "public" => "Editor.Syntax.Keyword.public",
        "package" => "Editor.Syntax.Keyword.package",
        "unnamed" => "Editor.Syntax.Keyword.unnamed",
        "template" => "Editor.Syntax.Keyword.template",
        "required" => "Editor.Syntax.Keyword.required",
        "string" => "Editor.Syntax.Keyword.string",
        "int" => "Editor.Syntax.Keyword.int",
        "bool" or "boolean" => "Editor.Syntax.Keyword.boolean",
        "float" => "Editor.Syntax.Keyword.float",
        "float2" => "Editor.Syntax.Keyword.float2",
        "float3" => "Editor.Syntax.Keyword.float3",
        "float4" => "Editor.Syntax.Keyword.float4",
        "map" => "Editor.Syntax.Keyword.map",
        "vector" => "Editor.Syntax.Keyword.vector",
        "not" or "and" or "or" => "Editor.Syntax.Keyword.logic",
        "div" => "Editor.Syntax.Keyword.div",
        "mod" => "Editor.Syntax.Keyword.mod",
        "$" => "Editor.Syntax.Keyword.root",
        _ => null,
    };

    /// <summary>The name a reference points at: its last part ("$/GFX/Weapon/Ammo_X" and "~/Ammo_X" give "Ammo_X").</summary>
    public static string ReferredName(string reference)
    {
        int slash = reference.LastIndexOf('/');
        return slash < 0 ? reference : reference[(slash + 1)..];
    }

    /// <summary>Whether a word is a type's name, as the colours read one: T and a capital letter.</summary>
    public static bool IsTypeName(string word) => word.Length > 1 && word[0] == 'T' && char.IsAsciiLetterUpper(word[1]);

    private static readonly Regex TemplateStart = new(@"(?m)^[ \t]*template[ \t]+(\w+)\s*\[", RegexOptions.CultureInvariant);
    private static readonly Regex LastTemplateStart = new(@"(?m)^[ \t]*template[ \t]+(\w+)\s*\[", RegexOptions.CultureInvariant | RegexOptions.RightToLeft);
    private static readonly Regex BuiltOnAfter = new(@"\G\s*is\s+([A-Za-z_]\w*)", RegexOptions.CultureInvariant);
    private static readonly Regex OneValue = new(@"^\s*([A-Za-z_]\w*)\s*(?::\s*([^=]*?))?\s*(?:=\s*(.*?))?\s*$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>The template of this name that a text declares; null when it declares none.</summary>
    public static TemplateSignature? TemplateIn(string text, string name)
    {
        foreach (Match start in TemplateStart.Matches(text))
            if (start.Groups[1].Value == name) return SignatureAt(text, start);
        return null;
    }

    /// <summary>The template whose [ ] or body a place is in: the last one that starts before it; null for none.</summary>
    public static TemplateSignature? TemplateAround(string text, int offset)
    {
        Match start = LastTemplateStart.Match(text, 0, Math.Min(offset, text.Length));
        return start.Success ? SignatureAt(text, start) : null;
    }

    private static TemplateSignature? SignatureAt(string text, Match start)
    {
        int open = start.Index + start.Length - 1;
        int close = Closing(text, open);
        if (close < 0) return null;
        Match builtOn = BuiltOnAfter.Match(text, close + 1);
        var values = new List<TemplateValue>();
        foreach (string part in SplitAtCommas(WithoutComments(text[(open + 1)..close])))
        {
            Match value = OneValue.Match(part);
            if (!value.Success) continue;
            values.Add(new TemplateValue(value.Groups[1].Value, value.Groups[2].Success ? value.Groups[2].Value.Trim() : "",
                value.Groups[3].Success && value.Groups[3].Value.Trim() != "" ? value.Groups[3].Value.Trim() : null));
        }
        return new TemplateSignature(start.Groups[1].Value, builtOn.Success ? builtOn.Groups[1].Value : "", values, open, close);
    }

    /// <summary>Where the bracket that closes the one at <paramref name="open"/> is; -1 when it isn't closed. Quotes and comments are skipped.</summary>
    public static int Closing(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\'' or '"')
            {
                int end = text.IndexOf(c, i + 1);
                if (end < 0) return -1;
                i = end;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int end = text.IndexOf('\n', i);
                if (end < 0) return -1;
                i = end;
            }
            else if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static string WithoutComments(string text) => Regex.Replace(text, @"//[^\n]*", "", RegexOptions.CultureInvariant);

    /// <summary>A list's parts at its own level: commas inside brackets or quotes don't split it.</summary>
    private static List<string> SplitAtCommas(string text)
    {
        var parts = new List<string>();
        int depth = 0, from = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '\'' or '"') quote = c;
            else if (c is '(' or '[' or '{' or '<') depth++;
            else if (c is ')' or ']' or '}' or '>') depth--;
            else if (c == ',' && depth == 0) { parts.Add(text[from..i]); from = i + 1; }
        }
        parts.Add(text[from..]);
        return parts;
    }

    private static readonly Regex DeclarationLine = new(@"^\s*(?:(?:export|private|public)\s+)?([A-Za-z_]\w*)\s+is\b", RegexOptions.CultureInvariant);
    private static readonly Regex ValueLine = new(@"^\s*([A-Za-z_]\w*)\s*:", RegexOptions.CultureInvariant);

    /// <summary>The name a line declares ("private XpLeftOffset is 10.0" gives "XpLeftOffset"); null for another line.</summary>
    public static string? DeclaredOn(string line) => DeclarationLine.Match(line) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>The type or template a line declares its name as ("export Ammo_X is TAmmunitionDescriptor"); null when it declares a value instead.</summary>
    public static string? TypeDeclaredOn(string line, string name) =>
        Regex.Match(line, @"^\s*(?:(?:export|private|public)\s+)?" + Regex.Escape(name) + @"\s+is\s+([A-Za-z_]\w*)\s*(?:\(|\[|//|$)", RegexOptions.CultureInvariant) is { Success: true } m
            ? m.Groups[1].Value : null;

    /// <summary>The template value a line of a template's [ ] declares ("MagnifiableHeight : float = 24.0"); null for another line.</summary>
    public static string? ValueDeclaredOn(string line) => ValueLine.Match(line) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>
    /// Where a declaration of this name starts in a text and where the next one does (or the text ends); null when the
    /// text doesn't declare it.
    /// </summary>
    public static (int Start, int End)? DeclarationIn(string text, string name)
    {
        Match start = Regex.Match(text, @"(?m)^[ \t]*(?:(?:export|private|public)[ \t]+)?" + Regex.Escape(name) + @"[ \t]+is\b", RegexOptions.CultureInvariant);
        if (!start.Success) return null;
        Match next = Regex.Match(text[(start.Index + start.Length)..], @"(?m)^[ \t]*(?:(?:export|private|public|unnamed)[ \t]+\w+|\w+[ \t]+is\b|template[ \t]+\w+|unnamed\b)", RegexOptions.CultureInvariant);
        return (start.Index, next.Success ? start.Index + start.Length + next.Index : text.Length);
    }

    /// <summary>A declaration's first settings that have a value (its id, empty texts and 'None' are left out); null when the text can't be read.</summary>
    public static KeyValues? KeyValuesOf(string declaration)
    {
        PropertyList list;
        try { list = DescriptorViewer.Properties(declaration); }
        catch (Exception e) when (e is ArgumentException or Engine.Ndf.NdfParseException) { return null; }
        List<PropertyRow> own = [.. list.Rows.Where(r => r.Depth == 0)];
        if (list.Type == "")
            return own.FirstOrDefault() is { } only ? new KeyValues([], 0, FirstLine(declaration, only.Start, only is GroupRow g ? g.Title : null)) : null;
        var shown = new List<(string, string)>();
        foreach (PropertyRow row in own)
        {
            if (shown.Count == MostKeyValues) break;
            string value = row is ValueRow plain ? FirstLine(plain.Value, 0, null) : FirstLine(declaration, row.Start, ((GroupRow)row).Title);
            if (row.Key == "DescriptorId" || value.StartsWith("GUID:", StringComparison.Ordinal) || IsEmpty(value)) continue;
            shown.Add((row.Key, value));
        }
        return new KeyValues(shown, own.Count, null);
    }

    /// <summary>A value that says nothing: an empty text or list, 'None', or a choice named None (Weapon_Cursor_None, EKind/None).</summary>
    private static bool IsEmpty(string value) =>
        value is "''" or "\"\"" or "'None'" or "\"None\"" or "[ ]" or "[]" || NoneChoice.IsMatch(value);

    private static readonly Regex NoneChoice = new(@"^[A-Za-z_]\w*[_/]None$", RegexOptions.CultureInvariant);

    /// <summary>A value's first line, cut when long; a value that opens on a bracket alone shows its title ("[ 3 ]").</summary>
    private static string FirstLine(string text, int start, string? title)
    {
        int end = text.IndexOf('\n', start);
        string line = (end < 0 ? text[start..] : text[start..end]).Trim();
        if (title is not null && (line.Length <= 1 || line is "(" or "[")) line = title;
        return line.Length > LongestValue ? line[..LongestValue] + "…" : line;
    }

    /// <summary>How many times a word stands on its own in a text (not inside a longer name or a path).</summary>
    public static int CountOf(string text, string word) =>
        Regex.Matches(text, @"(?<![\w$/~.])" + Regex.Escape(word) + @"(?!\w)", RegexOptions.CultureInvariant).Count;

    /// <summary>The line a place of a text is on, from 1.</summary>
    public static int LineOf(string text, int offset)
    {
        int line = 1;
        for (int i = text.IndexOf('\n'); i >= 0 && i < offset; i = text.IndexOf('\n', i + 1)) line++;
        return line;
    }
}
