using System.Net;
using System.Text;

namespace EasyNdf.App.Help;

/// <summary>A part of a wiki page's HTML: an element with its children, or a piece of text.</summary>
public abstract record WikiNode;

/// <summary>A piece of text, with its entities (such as <c>&amp;rsaquo;</c>) already turned into characters.</summary>
public sealed record WikiText(string Text) : WikiNode;

/// <summary>An element, such as <c>p</c> or <c>a</c>, with its attributes and children.</summary>
public sealed record WikiElement(string Name, IReadOnlyDictionary<string, string> Attributes, List<WikiNode> Children) : WikiNode
{
    /// <summary>An attribute's value; "" when the element does not have it.</summary>
    public string Attribute(string name) => Attributes.GetValueOrDefault(name, "");

    /// <summary>Whether the element has a class, such as "wklead".</summary>
    public bool HasClass(string name) => Attribute("class").Split(' ').Contains(name, StringComparer.Ordinal);
}

/// <summary>
/// Reads the HTML of the Help wiki's pages (copied from the HTML app). The pages are written
/// by the HTML app's own wiki tool, so they use a small, tidy part of HTML: every element is closed, attributes are in double
/// quotes, and there are no scripts; comments are left out. That is all this reader expects.
/// </summary>
public static class WikiHtml
{
    /// <summary>Elements that never have children or a closing tag.</summary>
    private static readonly HashSet<string> EmptyElements = new(StringComparer.Ordinal) { "br", "hr", "img", "input" };

    /// <summary>Reads a page's HTML into its top-level parts.</summary>
    public static List<WikiNode> Parse(string html)
    {
        var top = new WikiElement("body", new Dictionary<string, string>(), []);
        var open = new Stack<WikiElement>();
        open.Push(top);
        int at = 0;
        while (at < html.Length)
        {
            int tagStart = html.IndexOf('<', at);
            if (tagStart < 0) tagStart = html.Length;
            if (tagStart > at) open.Peek().Children.Add(new WikiText(WebUtility.HtmlDecode(html[at..tagStart])));
            if (tagStart >= html.Length) break;
            if (string.CompareOrdinal(html, tagStart, "<!--", 0, 4) == 0)
            {
                // a comment is left out
                int commentEnd = html.IndexOf("-->", tagStart, StringComparison.Ordinal);
                at = commentEnd < 0 ? html.Length : commentEnd + 3;
                continue;
            }
            int tagEnd = html.IndexOf('>', tagStart);
            if (tagEnd < 0) tagEnd = html.Length - 1;
            string tag = html[(tagStart + 1)..tagEnd];
            at = tagEnd + 1;
            if (tag.StartsWith('/'))
            {
                // a closing tag closes its element, and any left open inside it
                string closed = tag[1..].Trim();
                while (open.Count > 1 && open.Pop().Name != closed) { }
                continue;
            }
            WikiElement element = ReadTag(tag);
            open.Peek().Children.Add(element);
            if (!EmptyElements.Contains(element.Name) && !tag.EndsWith('/')) open.Push(element);
        }
        return top.Children;
    }

    /// <summary>An opening tag's name and attributes: <c>a href="#" data-p="rules"</c>. An attribute with no value, such as <c>data-wl</c>, has "".</summary>
    private static WikiElement ReadTag(string tag)
    {
        int nameEnd = 0;
        while (nameEnd < tag.Length && !char.IsWhiteSpace(tag[nameEnd]) && tag[nameEnd] != '/') nameEnd++;
        string name = tag[..nameEnd].ToLowerInvariant();
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        int at = nameEnd;
        while (at < tag.Length)
        {
            while (at < tag.Length && (char.IsWhiteSpace(tag[at]) || tag[at] == '/')) at++;
            if (at >= tag.Length) break;
            var attributeName = new StringBuilder();
            while (at < tag.Length && tag[at] != '=' && !char.IsWhiteSpace(tag[at])) attributeName.Append(tag[at++]);
            string value = "";
            if (at < tag.Length && tag[at] == '=')
            {
                at++;
                if (at < tag.Length && tag[at] == '"')
                {
                    int close = tag.IndexOf('"', at + 1);
                    if (close < 0) close = tag.Length;
                    value = WebUtility.HtmlDecode(tag[(at + 1)..close]);
                    at = close + 1;
                }
            }
            attributes[attributeName.ToString()] = value;
        }
        return new WikiElement(name, attributes, []);
    }

    /// <summary>A page's words with the tags left out, as the HTML app's search looks through them (tags become spaces).</summary>
    public static string SearchText(string html)
    {
        var text = new StringBuilder(html.Length);
        bool inTag = false;
        foreach (char c in html)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>' && inTag) { inTag = false; text.Append(' '); continue; }
            if (!inTag) text.Append(c);
        }
        return text.ToString();
    }
}
