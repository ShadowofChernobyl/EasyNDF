using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EasyNdf.App.Editor;
using EasyNdf.App.Text;
using EasyNdf.Engine.Editing;

namespace EasyNdf.App.Help;

/// <summary>
/// The Help tab's Reference: every type of the .ndf files with the variables it takes, as pages made from the File
/// Editor's guide (the same text as its tooltips). "ref" lists the topics, with a search; "ref-topic:units" lists a topic's
/// types by area (Assets/ReferenceTopics.json: where each type is used most in the game's files); "ref-all" lists every
/// type A to Z; "ref:TType" is a type's page, each variable under a heading of its own; "ref:TType#Name" opens it at a
/// variable.
/// </summary>
public static partial class ReferencePages
{
    /// <summary>The id of the Reference's first page, the list of every type.</summary>
    public const string IndexId = "ref";

    /// <summary>A type's page id: "ref:TMountedWeaponDescriptor".</summary>
    public static string IdOf(string type) => IndexId + ":" + type;

    /// <summary>The page of every type, A to Z.</summary>
    public const string AllId = "ref-all";

    /// <summary>A topic's page id: "ref-topic:units".</summary>
    private static string TopicId(string topic) => "ref-topic:" + topic;

    /// <summary>Whether an id is one of the Reference's pages.</summary>
    public static bool IsReference(string id) => id == IndexId || id == AllId || id.StartsWith("ref:", StringComparison.Ordinal) || id.StartsWith("ref-topic:", StringComparison.Ordinal);

    /// <summary>The topics, in the order the page lists them, by their key in ReferenceTopics.json.</summary>
    private static readonly string[] TopicOrder = ["units", "interface", "rules", "effects", "maps", "other"];

    /// <summary>Each type's topic and area, read once from the app's data.</summary>
    private static readonly Lazy<Dictionary<string, (string Topic, string Area)>> Topics = new(() =>
    {
        using Stream stream = typeof(ReferencePages).Assembly.GetManifestResourceStream("ReferenceTopics.json")!;
        var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream) ?? [];
        return json.ToDictionary(pair => pair.Key, pair => (pair.Value[0], pair.Value.Length > 1 ? pair.Value[1] : ""), StringComparer.Ordinal);
    });

    /// <summary>A type's topic and area; "other" and no area for a type the data does not place.</summary>
    private static (string Topic, string Area) PlaceOf(string type) => Topics.Value.TryGetValue(type, out var place) ? place : ("other", "");

    /// <summary>A topic's name, as the page shows it.</summary>
    private static string TopicName(string topic) => Texts.Get(topic switch
    {
        "units" => "Help.Reference.Topic.units",
        "interface" => "Help.Reference.Topic.interface",
        "rules" => "Help.Reference.Topic.rules",
        "effects" => "Help.Reference.Topic.effects",
        "maps" => "Help.Reference.Topic.maps",
        _ => "Help.Reference.Topic.other",
    });

    /// <summary>A topic's one-line description.</summary>
    private static string TopicAbout(string topic) => Texts.Get(topic switch
    {
        "units" => "Help.Reference.Topic.units.About",
        "interface" => "Help.Reference.Topic.interface.About",
        "rules" => "Help.Reference.Topic.rules.About",
        "effects" => "Help.Reference.Topic.effects.About",
        "maps" => "Help.Reference.Topic.maps.About",
        _ => "Help.Reference.Topic.other.About",
    });

    /// <summary>The types shown: every type the guide knows, but its own notes ("meta"), A to Z.</summary>
    public static IReadOnlyList<string> TypesOf(ValueGuide guide) =>
        [.. guide.Types.Where(type => type != "meta").Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Whether a type is a descriptor (its name ends with Descriptor) rather than another kind of entry.</summary>
    public static bool IsDescriptor(string type) => type.EndsWith("Descriptor", StringComparison.Ordinal);

    /// <summary>
    /// A Reference page by its id; null when it names no type. The variable after "#" is not part of the page: the Help
    /// tab scrolls to its heading.
    /// </summary>
    public static WikiPage? Page(string id, ValueGuide guide)
    {
        string group = Texts.Get("Help.Group.Reference");
        if (id == IndexId) return Index(guide, "");
        if (id == AllId) return new WikiPage(AllId, group, Texts.Get("Help.Reference.AllTitle"), IndexHtml(guide));
        if (id.StartsWith("ref-topic:", StringComparison.Ordinal))
        {
            string topic = id["ref-topic:".Length..];
            return TopicOrder.Contains(topic) ? new WikiPage(id, group, TopicName(topic), TopicHtml(topic, guide)) : null;
        }
        string type = id[(IndexId.Length + 1)..];
        int hash = type.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0) type = type[..hash];
        return guide.Knows(type) && type != "meta" ? new WikiPage(IdOf(type), group, type, TypeHtml(type, guide)) : null;
    }

    /// <summary>The Variables and Entries page: every type, or with a search the types and variables whose names have it.</summary>
    public static WikiPage Index(ValueGuide guide, string search) =>
        new(IndexId, Texts.Get("Help.Group.Reference"), Texts.Get("Help.Reference.Title"), search.Length == 0 ? TopicsHtml(guide) : FoundHtml(guide, search));

    /// <summary>The topics, each a link with its count of types, then a link to every type A to Z.</summary>
    private static string TopicsHtml(ValueGuide guide)
    {
        IReadOnlyList<string> types = TypesOf(guide);
        int variables = types.Sum(type => guide.NamesOf(type).Count());
        var html = new StringBuilder();
        html.Append("<p class=\"wklead\">").Append(Escape(Texts.Get("Help.Reference.Lead", ("types", Texts.Number(types.Count)), ("variables", Texts.Number(variables))))).Append("</p>");
        html.Append("<p>").Append(Escape(Texts.Get("Help.Reference.HowTo"))).Append("</p>");
        html.Append("<h3>").Append(Escape(Texts.Get("Help.Reference.Topics"))).Append("</h3><ul>");
        foreach (string topic in TopicOrder)
        {
            int count = types.Count(type => PlaceOf(type).Topic == topic);
            html.Append("<li><a href=\"#\" data-p=\"").Append(TopicId(topic)).Append("\">").Append(Escape(TopicName(topic))).Append("</a> <span class=\"note\">")
                .Append(Escape(Texts.Count("Help.Reference.TypeCount", count, ("count", Texts.Number(count))))).Append(" · ")
                .Append(Escape(TopicAbout(topic))).Append("</span></li>");
        }
        html.Append("</ul><p><a href=\"#\" data-p=\"").Append(AllId).Append("\">").Append(Escape(Texts.Get("Help.Reference.AllTitle"))).Append("</a></p>");
        return html.ToString();
    }

    /// <summary>A topic's types under their areas, those with the most variables first, each with its kind and number of variables.</summary>
    private static string TopicHtml(string topic, ValueGuide guide)
    {
        var html = new StringBuilder();
        List<IGrouping<string, string>> areas = [.. TypesOf(guide).Where(type => PlaceOf(type).Topic == topic)
            .GroupBy(type => PlaceOf(type).Area)
            // the areas whose largest type has the most to set first (ammunition, units), the unplaced ones last
            .OrderBy(area => area.Key == "" ? 1 : 0).ThenByDescending(area => area.Max(type => guide.NamesOf(type).Count())).ThenBy(area => area.Key, StringComparer.OrdinalIgnoreCase)];
        int count = areas.Sum(area => area.Count());
        html.Append("<p class=\"wklead\">").Append(Escape(TopicAbout(topic))).Append(' ')
            .Append(Escape(Texts.Count("Help.Reference.TypeCount", count, ("count", Texts.Number(count))))).Append(".</p>");
        foreach (IGrouping<string, string> area in areas)
        {
            html.Append("<h3>").Append(Escape(area.Key == "" ? Texts.Get("Help.Reference.ElsewhereArea") : area.Key)).Append("</h3><ul>");
            foreach (string type in area.OrderByDescending(type => guide.NamesOf(type).Count()).ThenBy(type => type, StringComparer.OrdinalIgnoreCase))
            {
                int variables = guide.NamesOf(type).Count();
                html.Append("<li><a href=\"#\" data-p=\"").Append(Escape(IdOf(type))).Append("\">").Append(Escape(type)).Append("</a> <span class=\"note\">")
                    .Append(Escape(Texts.Get(IsDescriptor(type) ? "Help.Reference.Kind.Descriptor" : "Help.Reference.Kind.Entry"))).Append(" · ")
                    .Append(Escape(Texts.Count("Help.Reference.VariableCount", variables, ("count", Texts.Number(variables))))).Append("</span></li>");
            }
            html.Append("</ul>");
        }
        return html.ToString();
    }


    /// <summary>What a search finds: the types whose name has it, then the variables whose name has it, each with its type.</summary>
    private static string FoundHtml(ValueGuide guide, string search)
    {
        IReadOnlyList<string> types = TypesOf(guide);
        List<string> typesFound = [.. types.Where(type => type.Contains(search, StringComparison.OrdinalIgnoreCase))];
        List<(string Type, string Name)> variablesFound = [.. types.SelectMany(type => guide.NamesOf(type)
            .Where(name => name.Contains(search, StringComparison.OrdinalIgnoreCase)).Select(name => (type, name)))
            .OrderBy(found => found.name, StringComparer.OrdinalIgnoreCase).ThenBy(found => found.type, StringComparer.OrdinalIgnoreCase)];
        var html = new StringBuilder();
        if (typesFound.Count == 0 && variablesFound.Count == 0)
            return "<p>" + Escape(Texts.Get("Help.Reference.NothingFound", ("text", search))) + "</p>";
        if (typesFound.Count > 0)
        {
            html.Append("<h3>").Append(Escape(Texts.Count("Help.Reference.TypesFound", typesFound.Count, ("count", Texts.Number(typesFound.Count))))).Append("</h3><p>");
            html.Append(string.Join(" · ", typesFound.Select(type => "<a href=\"#\" data-p=\"" + Escape(IdOf(type)) + "\">" + Escape(type) + "</a>")));
            html.Append("</p>");
        }
        if (variablesFound.Count > 0)
        {
            html.Append("<h3>").Append(Escape(Texts.Count("Help.Reference.VariablesFound", variablesFound.Count, ("count", Texts.Number(variablesFound.Count))))).Append("</h3><ul>");
            foreach ((string type, string name) in variablesFound)
                html.Append("<li><a href=\"#\" data-p=\"").Append(Escape(IdOf(type) + "#" + name)).Append("\">").Append(Escape(name))
                    .Append("</a> <span class=\"note\">").Append(Escape(Texts.Get("Help.Reference.InType", ("type", type)))).Append("</span></li>");
            html.Append("</ul>");
        }
        return html.ToString();
    }

    /// <summary>The list of every type, under its first letter, descriptors and other entries alike.</summary>
    private static string IndexHtml(ValueGuide guide)
    {
        IReadOnlyList<string> types = TypesOf(guide);
        var html = new StringBuilder();
        foreach (IGrouping<char, string> letter in types.GroupBy(type => char.ToUpperInvariant(type[0])))
        {
            html.Append("<h3>").Append(letter.Key).Append("</h3><p>");
            bool first = true;
            foreach (string type in letter)
            {
                if (!first) html.Append(" · ");
                first = false;
                html.Append("<a href=\"#\" data-p=\"").Append(Escape(IdOf(type))).Append("\">").Append(Escape(type)).Append("</a>");
            }
            html.Append("</p>");
        }
        return html.ToString();
    }

    /// <summary>A type's page: what kind it is and how many variables it takes, then each variable A to Z.</summary>
    private static string TypeHtml(string type, ValueGuide guide)
    {
        var known = new HashSet<string>(guide.Types, StringComparer.Ordinal);
        List<string> names = [.. guide.NamesOf(type).Order(StringComparer.OrdinalIgnoreCase)];
        var html = new StringBuilder();
        string kind = IsDescriptor(type) ? "Help.Reference.DescriptorLead" : "Help.Reference.EntryLead";
        // what the type is for comes first, when the guide says; then what kind it is and how many variables it takes
        if (guide.AboutType(type) is string about) html.Append("<p class=\"wklead\">").Append(Escape(about)).Append("</p>");
        html.Append("<p class=\"wklead\">").Append(Escape(Texts.Count(kind, names.Count, ("count", names.Count)))).Append("</p>");
        ReferenceExamples.TypeExamples? examples = ReferenceExamples.IfRead?.GetValueOrDefault(type);
        if (examples is { Files.Count: > 0 })
        {
            html.Append("<p><span class=\"note\">").Append(Escape(Texts.Get("Help.Reference.FoundIn"))).Append("</span> ");
            html.Append(string.Join(", ", examples.Files.Select(found =>
                "<span class=\"mono\">" + Escape(found.File) + "</span> <span class=\"note\">(" + Escape(Texts.Number(found.Count)) + ")</span>")));
            html.Append("</p>");
        }
        if (examples is { Block.Length: > 0 })
        {
            html.Append("<h3>").Append(Escape(Texts.Get("Help.Reference.Example"))).Append("</h3>");
            html.Append("<pre class=\"ndf\">").Append(Escape(examples.Block)).Append("</pre>");
            html.Append("<p><span class=\"note\">").Append(Escape(Texts.Get("Help.Reference.From", ("file", examples.BlockFile)))).Append("</span></p>");
        }
        foreach (string name in names)
        {
            if (guide.Find(type, name) is not ValueInfo info) continue;
            html.Append("<h3>").Append(Escape(name)).Append("</h3>");
            html.Append("<p><span class=\"note\">").Append(Escape(Texts.Get("Help.Reference.ValueType"))).Append("</span> <span class=\"mono\">")
                .Append(Escape(info.ValueType)).Append("</span>");
            if (info.IsRequired) html.Append(" <span class=\"note\">· ").Append(Escape(Texts.Get("Help.Reference.Required"))).Append("</span>");
            html.Append("</p>");
            if (info.Description != "") html.Append("<p>").Append(Linked(info.Description, type, known)).Append("</p>");
            if (examples?.Lines.GetValueOrDefault(name) is { Line.Length: > 0 } example)
                html.Append("<pre class=\"ndf\">").Append(Escape(example.Line)).Append("</pre>");
            if (info.Rules.Count > 0)
            {
                html.Append("<ul>");
                foreach (string rule in info.Rules) html.Append("<li>").Append(Linked(rule, type, known)).Append("</li>");
                html.Append("</ul>");
            }
            if (info.Choices.Count > 0)
            {
                html.Append("<p><span class=\"note\">").Append(Escape(Texts.Get("Editor.Tip.Choices"))).Append("</span><span class=\"mono\">");
                html.Append(Escape(string.Join(", ", info.Choices.Select(ValueMessages.LastPart))));
                html.Append("</span>");
                html.Append("</p>");
            }
            string? sure = info.Certainty switch
            {
                "approx" or "plain" => Texts.Get("Weapons.Certainty.Likely"),
                "unclear" => Texts.Get("Weapons.Certainty.Unknown"),
                _ => null,
            };
            if (sure is not null) html.Append("<p><span class=\"note\">").Append(Escape(sure)).Append("</span></p>");
        }
        return html.ToString();
    }



    /// <summary>A text with each other type it names turned into a link to that type's page.</summary>
    private static string Linked(string text, string self, HashSet<string> known)
    {
        var html = new StringBuilder();
        int at = 0;
        foreach (Match word in Word().Matches(text))
        {
            if (word.Value == self || !known.Contains(word.Value)) continue;
            html.Append(Escape(text[at..word.Index]));
            html.Append("<a href=\"#\" data-p=\"").Append(Escape(IdOf(word.Value))).Append("\">").Append(Escape(word.Value)).Append("</a>");
            at = word.Index + word.Length;
        }
        html.Append(Escape(text[at..]));
        return html.ToString();
    }

    private static string Escape(string text) => WebUtility.HtmlEncode(text);

    /// <summary>A name that may be a type's: letters, digits and underscores, starting with a capital.</summary>
    [GeneratedRegex(@"\b[A-Z][A-Za-z0-9_]{2,}\b")]
    private static partial Regex Word();
}
