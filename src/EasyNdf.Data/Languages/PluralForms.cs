namespace EasyNdf.Data.Languages;

/// <summary>
/// Which plural form a language uses for a number. A text that depends on a number lists its forms separated by "|",
/// in the order given here for its language; a language not listed uses English's two forms.
/// </summary>
/// <remarks>The rules are the usual ones for each language (the Unicode CLDR's), for whole numbers.</remarks>
public static class PluralForms
{
    /// <summary>
    /// The form for <paramref name="count"/>, counting from 0:
    /// <list type="bullet">
    /// <item>English, German, Spanish, Italian and most others: 0 for 1, 1 for the rest ("1 edit", "5 edits").</item>
    /// <item>French and Portuguese: 0 for 0 and 1, 1 for the rest.</item>
    /// <item>Russian and Ukrainian: 0 for 1, 21, 31…; 1 for 2–4, 22–24…; 2 for the rest.</item>
    /// <item>Polish: 0 for 1; 1 for 2–4, 22–24…; 2 for the rest.</item>
    /// <item>Czech and Slovak: 0 for 1; 1 for 2–4; 2 for the rest.</item>
    /// <item>Chinese, Japanese and Korean: always 0, as they have one form.</item>
    /// </list>
    /// </summary>
    public static int FormOf(string languageCode, long count)
    {
        long number = Math.Abs(count);
        long lastDigit = number % 10, lastTwoDigits = number % 100;
        bool endsInTwoToFour = lastDigit is >= 2 and <= 4 && lastTwoDigits is < 12 or > 14;
        switch (BaseLanguage(languageCode))
        {
            case "zh":
            case "ja":
            case "ko":
                return 0;
            case "fr":
            case "pt":
                return number <= 1 ? 0 : 1;
            case "ru":
            case "uk":
                if (lastDigit == 1 && lastTwoDigits != 11) return 0;
                return endsInTwoToFour ? 1 : 2;
            case "pl":
                if (number == 1) return 0;
                return endsInTwoToFour ? 1 : 2;
            case "cs":
            case "sk":
                if (number == 1) return 0;
                return number is >= 2 and <= 4 ? 1 : 2;
            default:
                return number == 1 ? 0 : 1;
        }
    }

    /// <summary>The language part of a code: "pt" for "pt-br".</summary>
    private static string BaseLanguage(string code)
    {
        int dash = code.IndexOf('-');
        return dash < 0 ? code : code[..dash];
    }
}
