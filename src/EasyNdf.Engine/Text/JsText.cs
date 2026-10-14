namespace EasyNdf.Engine.Text;

/// <summary>
/// Text helpers that behave exactly like their JavaScript counterparts.
/// </summary>
/// <remarks>
/// The engine must write byte-for-byte the same files as the HTML app, whose engine is JavaScript.
/// C# and JavaScript disagree on a few small things that matter here:
/// <list type="bullet">
/// <item>What counts as white space: JavaScript includes U+FEFF and leaves out U+0085; C# does the opposite.</item>
/// <item>string.IndexOf, StartsWith and EndsWith with a string argument compare by culture in C#.
/// The engine always compares character by character (ordinal), as JavaScript does.</item>
/// </list>
/// Use these helpers, or pass StringComparison.Ordinal, wherever the engine handles text.
/// </remarks>
public static class JsText
{
    /// <summary>JavaScript's definition of white space, used by trim() and by \s in regular expressions.</summary>
    public static bool IsWhiteSpace(char c) =>
        c == '﻿' || (c != '\u0085' && char.IsWhiteSpace(c));

    /// <summary>JavaScript's String.prototype.trim().</summary>
    public static string Trim(string text) => TrimEnd(TrimStart(text));

    /// <summary>JavaScript's String.prototype.trimStart().</summary>
    public static string TrimStart(string text)
    {
        int start = 0;
        while (start < text.Length && IsWhiteSpace(text[start])) start++;
        return start == 0 ? text : text[start..];
    }

    /// <summary>JavaScript's String.prototype.trimEnd(), the same as text.replace(/\s+$/, '').</summary>
    public static string TrimEnd(string text)
    {
        int end = text.Length;
        while (end > 0 && IsWhiteSpace(text[end - 1])) end--;
        return end == text.Length ? text : text[..end];
    }

    /// <summary>Removes spaces and tabs at the end: text.replace(/[ \t]+$/, '').</summary>
    public static string TrimSpacesAndTabsEnd(string text) => text.TrimEnd(' ', '\t');

    /// <summary>True when the text holds anything other than white space: /\S/.test(text).</summary>
    public static bool HasNonWhiteSpace(string text)
    {
        foreach (char c in text)
            if (!IsWhiteSpace(c)) return true;
        return false;
    }

    /// <summary>True when the text is only spaces and tabs, or empty: /^[ \t]*$/.test(text).</summary>
    public static bool IsSpacesAndTabs(string text)
    {
        foreach (char c in text)
            if (c != ' ' && c != '\t') return false;
        return true;
    }

    /// <summary>The spaces and tabs at the start of the text: /^[ \t]*/.exec(text)[0].</summary>
    public static string LeadingSpacesAndTabs(string text)
    {
        int n = 0;
        while (n < text.Length && (text[n] == ' ' || text[n] == '\t')) n++;
        return text[..n];
    }

    /// <summary>Ordinal IndexOf, like JavaScript's indexOf. Returns -1 when not found.</summary>
    public static int IndexOf(string text, string value, int startIndex = 0) =>
        startIndex > text.Length ? -1 : text.IndexOf(value, startIndex, StringComparison.Ordinal);

    /// <summary>Ordinal LastIndexOf, like JavaScript's lastIndexOf(value, fromIndex).</summary>
    /// <remarks>JavaScript finds a match that starts at or before fromIndex; a negative fromIndex counts as 0.</remarks>
    public static int LastIndexOf(string text, char value, int fromIndex)
    {
        if (text.Length == 0) return -1;
        return text.LastIndexOf(value, Math.Clamp(fromIndex, 0, text.Length - 1));
    }

    /// <summary>JavaScript's slice(start, end): clamps both ends instead of throwing.</summary>
    public static string Slice(string text, int start, int end)
    {
        if (start < 0) start = Math.Max(0, text.Length + start);
        if (end < 0) end = Math.Max(0, text.Length + end);
        start = Math.Min(start, text.Length);
        end = Math.Min(end, text.Length);
        return end > start ? text[start..end] : "";
    }

    /// <summary>JavaScript's slice(start): from start to the end, clamped.</summary>
    public static string Slice(string text, int start) => Slice(text, start, text.Length);

    /// <summary>The character at an index, or '\0' past the end (JavaScript gives undefined there).</summary>
    public static char At(string text, int index) =>
        index >= 0 && index < text.Length ? text[index] : '\0';

    /// <summary>Counts the line feeds before an offset, plus one: the line number the offset is on.</summary>
    public static int LineNumberAt(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }
}
