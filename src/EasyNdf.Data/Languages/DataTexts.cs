using System.Reflection;

namespace EasyNdf.Data.Languages;

/// <summary>
/// The one catalog of the app's texts, shared by this library and the app: the library's own English texts (in its
/// Languages\en folder) are read here, and the app adds its own to the same catalog when it starts. Code in this library
/// asks for a text by name with <see cref="Get(string)"/>, as the app's code does with its <c>Texts</c>.
/// </summary>
public static class DataTexts
{
    /// <summary>The start of this library's English files' resource names.</summary>
    private const string EnglishResourcePrefix = "EasyNdf.Data.Languages.en.";

    /// <summary>Every text, by name, in English and in the language in use.</summary>
    public static TextCatalog Catalog { get; } = LoadEnglish();

    /// <summary>The text with this name, in the language in use.</summary>
    public static string Get(string name) => Catalog.Get(name);

    /// <summary>The text with this name, each {blank} in it filled in.</summary>
    public static string Get(string name, params (string Name, object? Value)[] blanks) => Catalog.Get(name, blanks);

    /// <summary>A text that depends on a number, in the plural form the language uses for it.</summary>
    public static string Count(string name, long count, params (string Name, object? Value)[] blanks) => Catalog.Count(name, count, blanks);

    private static TextCatalog LoadEnglish()
    {
        var catalog = new TextCatalog();
        Assembly library = typeof(DataTexts).Assembly;
        foreach (string resource in library.GetManifestResourceNames().Where(r => r.StartsWith(EnglishResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var reader = new StreamReader(library.GetManifestResourceStream(resource)!);
            catalog.AddEnglish(reader.ReadToEnd(), resource[EnglishResourcePrefix.Length..]);
        }
        return catalog;
    }
}
