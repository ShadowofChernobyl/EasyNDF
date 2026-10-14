using System.IO;
using System.Text.Json.Nodes;

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
public static class WikiPages
{
    /// <summary>Every page, in the order of the list.</summary>
    public static IReadOnlyList<WikiPage> All { get; }

    /// <summary>The short texts of the ? buttons, by name ("rule", "cond", "act", "dfilt", "wm"): a title and the HTML.</summary>
    public static IReadOnlyDictionary<string, (string Title, string Html)> QuickHelp { get; }

    static WikiPages()
    {
        using Stream stream = typeof(WikiPages).Assembly.GetManifestResourceStream("wiki.json")!;
        var json = (JsonObject)JsonNode.Parse(stream)!;
        All = [.. json["pages"]!.AsArray().Select(page => new WikiPage(
            (string)page!["id"]!, (string)page["group"]!, (string)page["title"]!, (string)page["html"]!))];
        QuickHelp = json["quickHelp"]!.AsArray().ToDictionary(
            help => (string)help!["id"]!, help => ((string)help!["title"]!, (string)help["html"]!));
    }

    /// <summary>A page by its id; null when there is none.</summary>
    public static WikiPage? ById(string id) => All.FirstOrDefault(page => page.Id == id);
}
