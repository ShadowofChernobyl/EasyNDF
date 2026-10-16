using System.ComponentModel;
using System.IO;
using System.Reflection;
using EasyNdf.Data.Languages;

namespace EasyNdf.App.Text;

/// <summary>
/// The app's texts, in the language chosen on the Settings tab. Code asks for a text by its name, such as
/// <c>Texts.Get("Files.AddFolder")</c>; screens use <see cref="TextExtension"/> (<c>{t:Text Files.AddFolder}</c>).
/// </summary>
/// <remarks>
/// The English texts are in the Languages\en folder of the project, one file per part of the app, built into the app.
/// To add a text: add its name and English to the right file, then use the name. Other languages are packs the user
/// imports on the Settings tab; whatever a pack lacks shows in English.
/// </remarks>
public static class Texts
{
    /// <summary>The start of the built-in English files' resource names (the project's Languages\en folder).</summary>
    private const string EnglishResourcePrefix = "EasyNdf.App.Languages.en.";

    /// <summary>The start of the built-in languages' resource names (the project's Languages folder, such as fr.json).</summary>
    private const string PackResourcePrefix = "EasyNdf.App.LanguagePacks.";

    /// <summary>Every text, by name, in English and in the language in use.</summary>
    public static TextCatalog Catalog { get; } = LoadEnglish();

    /// <summary>The languages built into the app, by code.</summary>
    public static IReadOnlyDictionary<string, LanguagePack> BuiltIn { get; } = LoadBuiltIn();

    /// <summary>What screens bind to, so they show the new language as soon as it is chosen.</summary>
    public static TextSource Source { get; } = new();

    /// <summary>Raised after the language changed, for texts that code has put on screen to be shown again.</summary>
    public static event Action? LanguageChanged;

    /// <summary>The text with this name, in the language in use.</summary>
    public static string Get(string name) => Catalog.Get(name);

    /// <summary>The text with this name, each {blank} in it filled in: <c>Texts.Get("Rules.DeleteAsk", ("name", rule.Name))</c>.</summary>
    public static string Get(string name, params (string Name, object? Value)[] blanks) => Catalog.Get(name, blanks);

    /// <summary>
    /// A text that depends on a number, in the plural form the language uses for it, with {count} filled in:
    /// <c>Texts.Count("Files.Edits", 5)</c> gives "5 edits".
    /// </summary>
    public static string Count(string name, long count, params (string Name, object? Value)[] blanks) => Catalog.Count(name, count, blanks);

    /// <summary>A label with its colon, as the language writes it: "Name:" in English, "Nom :" in French.</summary>
    public static string WithColon(string label) => Get("Common.WithColon", ("label", label));

    /// <summary>A number with the language's thousands separators, such as "1,234" in English or "1 234" in French.</summary>
    public static string Number(long number) => number.ToString("N0", Catalog.Culture);

    /// <summary>
    /// <see cref="Count"/> with the number written by <see cref="Number"/>: <c>Texts.CountWide("Editor.Lines", 1234)</c> gives "1,234 lines".
    /// </summary>
    public static string CountWide(string name, long count, params (string Name, object? Value)[] blanks) =>
        Catalog.Count(name, count, [.. blanks, ("count", Number(count))]);

    /// <summary>
    /// A language as the app shows it: the built-in pack with the user's imported pack for it laid over it, text by
    /// text. Null when there is neither.
    /// </summary>
    public static LanguagePack? LanguageFor(string code) =>
        LanguageStore.Layered(BuiltIn.GetValueOrDefault(code), AppData.Current.Languages.Packs.GetValueOrDefault(code));

    /// <summary>Every language the app can show besides English: the built-in ones and those the user imported.</summary>
    public static IEnumerable<LanguagePack> Languages() =>
        BuiltIn.Keys.Union(AppData.Current.Languages.Packs.Keys).Select(code => LanguageFor(code)!);

    /// <summary>
    /// Switches to a language by its code, such as "fr"; English for "en", or for a code with no pack. Every screen
    /// shows the new language at once.
    /// </summary>
    public static void Use(string code)
    {
        Catalog.Use(LanguageFor(code));
        Source.Changed();
        LanguageChanged?.Invoke();
    }

    /// <summary>Uses the language saved in the settings.</summary>
    public static void UseSaved() => Use((string?)AppData.Current.Settings["lang"] ?? LanguagePack.EnglishCode);

    /// <summary>Reads every built-in language. A broken one stops the app at once, so it is found before release.</summary>
    private static Dictionary<string, LanguagePack> LoadBuiltIn()
    {
        var packs = new Dictionary<string, LanguagePack>(StringComparer.Ordinal);
        Assembly app = typeof(Texts).Assembly;
        foreach (string resource in app.GetManifestResourceNames().Where(r => r.StartsWith(PackResourcePrefix, StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(app.GetManifestResourceStream(resource)!);
            LanguagePack pack = LanguageStore.ReadPack(reader.ReadToEnd());
            packs[pack.Code] = pack;
        }
        return packs;
    }

    /// <summary>Reads every built-in English file. A broken file stops the app at once, so it is found before release.</summary>
    private static TextCatalog LoadEnglish()
    {
        // the catalog the Data library already filled with its own English; the app's is added to it
        TextCatalog catalog = DataTexts.Catalog;
        Assembly app = typeof(Texts).Assembly;
        foreach (string resource in app.GetManifestResourceNames().Where(r => r.StartsWith(EnglishResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var reader = new StreamReader(app.GetManifestResourceStream(resource)!);
            catalog.AddEnglish(reader.ReadToEnd(), resource[EnglishResourcePrefix.Length..]);
        }
        // the Help wiki's pages and the About tab's page, which are kept as HTML files rather than with the texts
        catalog.AddEnglish(Help.WikiPages.EnglishTexts().ToJsonString(), "Help/wiki.json");
        return catalog;
    }
}

/// <summary>
/// The language in use, for screens to bind to: when it changes, every binding through <see cref="TextExtension"/>
/// fetches its text again.
/// </summary>
public sealed class TextSource : INotifyPropertyChanged
{
    /// <summary>The code of the language in use; bindings watch it.</summary>
    public string Language => Texts.Catalog.LanguageCode;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Tells every binding that the language changed.</summary>
    internal void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
}
