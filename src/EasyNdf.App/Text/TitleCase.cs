using System.Text.RegularExpressions;

namespace EasyNdf.App.Text;

/// <summary>
/// Title case for headings and window titles (the HTML app's <c>TC</c>): every word starts with a capital, except short
/// joining words in the middle; words that already have a capital, or do not start with a letter, stay as they are.
/// </summary>
public static class TitleCase
{
    /// <summary>The joining words that stay small in the middle of a title.</summary>
    private static readonly HashSet<string> SmallWords =
        [.. "a an the and but or nor for so yet as at by in of on to up via with from into vs".Split(' ')];

    /// <summary>"Find and replace" becomes "Find and Replace".</summary>
    public static string Of(string text)
    {
        // title case is English's own habit; other languages keep their headings as their translators wrote them
        if (Texts.Catalog.LanguageCode != EasyNdf.Data.Languages.LanguagePack.EnglishCode) return text;
        // the words and the spaces between them, in order
        string[] parts = Regex.Split(text, @"(\s+)");
        int first = Array.FindIndex(parts, IsWord), last = Array.FindLastIndex(parts, IsWord);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (!IsWord(part) || part.Any(char.IsAsciiLetterUpper) || !char.IsAsciiLetterLower(part[0])) continue;
            if (i != first && i != last && SmallWords.Contains(part)) continue;
            parts[i] = string.Join("-", part.Split('-').Select(piece => piece == "" ? piece : char.ToUpperInvariant(piece[0]) + piece[1..]));
        }
        return string.Concat(parts);
    }

    private static bool IsWord(string part) => part.Any(c => !char.IsWhiteSpace(c));
}
