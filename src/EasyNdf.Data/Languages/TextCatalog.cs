using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyNdf.Data.Languages;

/// <summary>
/// Every text the app shows, each by a fixed name such as "Files.AddFolder": the English texts the app comes with, and
/// the translations of the language in use. A text the language lacks is shown in English, and a name with no English
/// at all is shown as the name itself, so a typo stands out instead of leaving a blank.
/// </summary>
/// <remarks>
/// Names never change when the English is reworded, so a translation is never lost; the pack remembers which English
/// it was made from, which is how a reworded text is found to need a new translation.
/// <para>
/// A text can have blanks, such as <c>Delete "{name}"?</c>, which <see cref="Get(string, (string Name, object? Value)[])"/>
/// fills in. A text that depends on a number has a form for each plural, separated by "|", such as
/// <c>{count} edit|{count} edits</c>; <see cref="Count"/> picks the form the language uses for that number.
/// </para>
/// </remarks>
public sealed class TextCatalog
{
    private readonly Dictionary<string, string> english = new(StringComparer.Ordinal);

    /// <summary>Every name with its English text.</summary>
    public IReadOnlyDictionary<string, string> English => english;

    /// <summary>The language in use, or null for English.</summary>
    public LanguagePack? Language { get; private set; }

    /// <summary>The code of the language in use, such as "fr"; "en" for English.</summary>
    public string LanguageCode => Language?.Code ?? LanguagePack.EnglishCode;

    /// <summary>Numbers are written the way the language in use writes them.</summary>
    public CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Adds one file of English texts: a JSON object of names and texts. A name that is already known is refused, so two
    /// parts of the app can never quietly share one name.
    /// </summary>
    /// <param name="json">The file's text.</param>
    /// <param name="fileName">The file's name, for the error message.</param>
    public void AddEnglish(string json, string fileName)
    {
        if (JsonFiles.Parse(json) is not JsonObject texts) throw new FormatException(fileName + " is not a list of names and texts.");
        foreach ((string name, JsonNode? text) in texts)
        {
            if (text is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
                throw new FormatException(fileName + ": the text of " + name + " is not a piece of text.");
            if (!english.TryAdd(name, value.GetValue<string>()))
                throw new FormatException(fileName + ": " + name + " is already used by another file.");
        }
    }

    /// <summary>Switches to a language; null switches back to English.</summary>
    public void Use(LanguagePack? language)
    {
        Language = language;
        Culture = language is null ? CultureInfo.InvariantCulture : CultureOf(language.Code);
    }

    /// <summary>The text with this name, in the language in use.</summary>
    public string Get(string name)
    {
        if (Language is not null && Language.Texts.TryGetValue(name, out PackText? translated) && translated.Text != "")
            return translated.Text;
        return english.TryGetValue(name, out string? text) ? text : name;
    }

    /// <summary>The text with this name, with its blanks filled in: each {name} in it is replaced by the value given for that name.</summary>
    public string Get(string name, params (string Name, object? Value)[] blanks) => Fill(Get(name), blanks);

    /// <summary>
    /// A text that depends on a number: the plural form the language uses for <paramref name="count"/>, with
    /// {count} and the other blanks filled in.
    /// </summary>
    public string Count(string name, long count, params (string Name, object? Value)[] blanks)
    {
        string[] forms = Get(name).Split('|');
        int form = Math.Min(PluralForms.FormOf(LanguageCode, count), forms.Length - 1);
        // a {count} given among the blanks (already written with thousands separators, say) wins over the plain number
        return Fill(forms[form], [.. blanks, ("count", count)]);
    }

    /// <summary>Replaces each {name} in a text by its value. Numbers are written the way the language in use writes them.</summary>
    public string Fill(string text, (string Name, object? Value)[] blanks)
    {
        if (blanks.Length == 0 || !text.Contains('{')) return text;
        var filled = new StringBuilder(text);
        foreach ((string blankName, object? value) in blanks)
            filled.Replace("{" + blankName + "}", value is IFormattable number ? number.ToString(null, Culture) : value?.ToString() ?? "");
        return filled.ToString();
    }

    /// <summary>The way a language writes numbers; plain digits for a language Windows does not know.</summary>
    private static CultureInfo CultureOf(string code)
    {
        try { return CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}
