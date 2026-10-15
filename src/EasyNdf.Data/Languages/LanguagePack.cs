namespace EasyNdf.Data.Languages;

/// <summary>A language: the app's texts translated, each by its name.</summary>
public sealed class LanguagePack
{
    /// <summary>The code of English, the app's own language, which needs no pack.</summary>
    public const string EnglishCode = "en";

    /// <summary>A short language code such as "fr" or "pt-br". Never "en".</summary>
    public string Code { get; set; } = "";

    /// <summary>The language's name in that language, such as "Français", shown in the Language dropdown.</summary>
    public string Name { get; set; } = "";

    /// <summary>The app version the pack was last made for.</summary>
    public string MadeFor { get; set; } = "";

    /// <summary>Each translated text, by its name.</summary>
    public Dictionary<string, PackText> Texts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>How much of the app's English this pack covers.</summary>
    public PackProgress ProgressAgainst(IReadOnlyDictionary<string, string> english)
    {
        int translated = 0, needsReview = 0;
        foreach ((string name, string englishText) in english)
        {
            if (!Texts.TryGetValue(name, out PackText? text) || text.Text == "") continue;
            translated++;
            // translated from English that has since been reworded, or by a machine and not yet checked
            if (text.English != englishText || text.Machine) needsReview++;
        }
        return new PackProgress(translated, needsReview, english.Count);
    }
}

/// <summary>One translated text.</summary>
/// <param name="Text">The translation.</param>
/// <param name="English">The English it was translated from; when the app's English differs, the translation needs a look.</param>
/// <param name="Machine">Translated by a machine and not yet checked by a person.</param>
public sealed record PackText(string Text, string English, bool Machine = false);

/// <summary>How much of the app a language pack covers.</summary>
/// <param name="Translated">Texts the pack translates.</param>
/// <param name="NeedsReview">Of those, the ones whose English has changed since, or that a machine translated.</param>
/// <param name="Total">Texts the app has.</param>
public sealed record PackProgress(int Translated, int NeedsReview, int Total)
{
    /// <summary>The share translated, as a whole percentage, rounded down so 100% means every text.</summary>
    public int Percent => Total == 0 ? 100 : (int)(Translated * 100L / Total);
}
