using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.App.Text;

namespace EasyNdf.App.Help;

/// <summary>A page of the Help wiki: its id (which links name), its group in the list, its title and its HTML.</summary>
public sealed record WikiPage(string Id, string Group, string Title, string Html)
{
    /// <summary>The page's words, lower case, as the search looks through them: its title, then its text.</summary>
    public string SearchText { get; } = (Title + " " + WikiHtml.SearchText(Html)).ToLowerInvariant();
}

/// <summary>
/// The Help wiki's pages, in their order, and the short texts of the ? buttons that have no page of their own. Both are
/// the HTML app's (its <c>WIKI</c> and <c>Q_HELP</c>), copied into Help/wiki.json.
/// </summary>
/// <remarks>
/// The pages are in English in wiki.json; each title, group and page, and the About tab's page (Help/about.html), are
/// also texts of the catalog, so a language pack translates them like any other text: "Help.Page.start.Title",
/// "Help.Page.start.Html", "Help.Group.GettingStarted" and "About.Page.Html". The translation tool
/// (tools/Translate) names them the same way.
/// </remarks>
public static class WikiPages
{
    /// <summary>Each page as wiki.json has it, in English, in the order of the list.</summary>
    private static readonly IReadOnlyList<WikiPage> EnglishPages;

    /// <summary>The pages in the language they were last made in, and that language's code.</summary>
    private static IReadOnlyList<WikiPage> pages = [];
    private static string pagesLanguage = "";

    /// <summary>Every page, in the order of the list, in the language in use.</summary>
    public static IReadOnlyList<WikiPage> All
    {
        get
        {
            // made again only when the language changed, since each page's search text takes a moment to make
            if (pagesLanguage != Texts.Catalog.LanguageCode)
            {
                pages = [.. EnglishPages.Select(page => new WikiPage(page.Id,
                    Texts.Get(GroupName(page.Group)), Texts.Get(TitleName(page.Id)), Texts.Get(HtmlName(page.Id))))];
                pagesLanguage = Texts.Catalog.LanguageCode;
            }
            return pages;
        }
    }

    /// <summary>The short texts of the ? buttons, by name ("rule", "cond", "act", "dfilt", "wm"): a title and the HTML.</summary>
    public static IReadOnlyDictionary<string, (string Title, string Html)> QuickHelp { get; }

    /// <summary>The About tab's page, in the language in use, with "{version}" where the app's version goes.</summary>
    public static string AboutHtml => Texts.Get(AboutName);

    static WikiPages()
    {
        using Stream stream = typeof(WikiPages).Assembly.GetManifestResourceStream("wiki.json")!;
        var json = (JsonObject)JsonNode.Parse(stream)!;
        EnglishPages = [.. json["pages"]!.AsArray().Select(page => new WikiPage(
            (string)page!["id"]!, (string)page["group"]!, (string)page["title"]!, (string)page["html"]!))];
        QuickHelp = json["quickHelp"]!.AsArray().ToDictionary(
            help => (string)help!["id"]!, help => ((string)help!["title"]!, (string)help["html"]!));
    }

    /// <summary>A page by its id, in the language in use; null when there is none.</summary>
    public static WikiPage? ById(string id) => All.FirstOrDefault(page => page.Id == id);

    /// <summary>
    /// The English of the pages and of the About tab's page, by text name, for the catalog: each page's title and HTML,
    /// and each group's name once.
    /// </summary>
    public static JsonObject EnglishTexts()
    {
        var texts = new JsonObject();
        foreach (WikiPage page in EnglishPages)
        {
            if (!texts.ContainsKey(GroupName(page.Group))) texts[GroupName(page.Group)] = page.Group;
            texts[TitleName(page.Id)] = page.Title;
            texts[HtmlName(page.Id)] = page.Html;
        }
        using Stream stream = typeof(WikiPages).Assembly.GetManifestResourceStream("about.html")!;
        using var reader = new StreamReader(stream);
        texts[AboutName] = reader.ReadToEnd();
        return texts;
    }

    private const string AboutName = "About.Page.Html";

    private static string TitleName(string id) => "Help.Page." + id + ".Title";

    private static string HtmlName(string id) => "Help.Page." + id + ".Html";

    /// <summary>A group's text name: its English name without spaces or signs, such as "Help.Group.GettingStarted".</summary>
    private static string GroupName(string group) => "Help.Group." + string.Concat(group.Where(char.IsAsciiLetterOrDigit));
}
