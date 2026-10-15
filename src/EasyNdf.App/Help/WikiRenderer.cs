using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Help;

/// <summary>
/// How a page's text is spaced. The Help wiki's pages (#wkBody) have taller lines than the About tab (.help); the ?
/// popup (#qText) is tighter still; the change log has larger version headings.
/// </summary>
public sealed record PageLook
{
    /// <summary>The height of a line of a paragraph or a list item.</summary>
    public required double TextLineHeight { get; init; }

    /// <summary>The space above and below a list item.</summary>
    public required double ItemMargin { get; init; }

    /// <summary>Whether h3 is the wiki's larger heading with a line under it; else the About tab's.</summary>
    public required bool HasWikiHeadings { get; init; }

    /// <summary>The space above and below a paragraph.</summary>
    public double ParagraphTop { get; init; } = 12.5;

    /// <inheritdoc cref="ParagraphTop"/>
    public double ParagraphBottom { get; init; } = 12.5;

    /// <summary>The opening paragraph's line height and the space above it.</summary>
    public double LeadLineHeight { get; init; } = 21.6;

    /// <inheritdoc cref="LeadLineHeight"/>
    public double LeadTop { get; init; } = 13.5;

    /// <summary>The space above a list; below it is always 6 pixels.</summary>
    public double ListTop { get; init; } = 6;

    /// <summary>How far in a list's items start, after their bullets or numbers.</summary>
    public double ListIndent { get; init; } = 22;

    /// <summary>The text size of an h2.</summary>
    public double SectionHeadingSize { get; init; } = 12.5;

    /// <summary>The space above an h2 that is not the first block.</summary>
    public double SectionHeadingMargin { get; init; } = 16;

    /// <summary>A Help page: lines of 1.6 times the text, 4 pixels between items.</summary>
    public static readonly PageLook Wiki = new() { TextLineHeight = 20, ItemMargin = 4, HasWikiHeadings = true };

    /// <summary>The About tab: the page's usual lines, 3 pixels between items.</summary>
    public static readonly PageLook About = new() { TextLineHeight = 18.125, ItemMargin = 3, HasWikiHeadings = false };

    /// <summary>The change log: as the About tab, with each version's heading larger and no space kept above it.</summary>
    public static readonly PageLook ChangeLog = About with { SectionHeadingSize = 15, SectionHeadingMargin = 0 };

    /// <summary>The ? popup: a Help page with the usual lines, paragraphs 6 pixels apart, and lists less far in.</summary>
    public static readonly PageLook Popup = new()
    {
        TextLineHeight = 18.125,
        ItemMargin = 3,
        HasWikiHeadings = true,
        ParagraphTop = 0,
        ParagraphBottom = 6,
        LeadLineHeight = 19.575,
        LeadTop = 0,
        ListTop = 0,
        ListIndent = 20,
    };
}

/// <summary>
/// Draws a Help page's HTML as the HTML app's page styles it (#wkBody): paragraphs, headings, lists, numbered steps, tips,
/// tables and the inline parts in them (bold, links, keys, code). The whole page is one document in a read-only text
/// box, so its text can be selected and copied as on a web page. Space between blocks follows the browser's rule that
/// the larger of two touching margins wins, which a document's blocks follow too. The About tab, the change log and the
/// ? popup are drawn with it as well.
/// </summary>
/// <param name="look">How the text is spaced.</param>
public sealed class WikiRenderer(PageLook look)
{
    /// <summary>Raised when a link to another page is clicked, with that page's id.</summary>
    public event Action<string>? PageLinkClicked;

    /// <summary>Raised when the "Open the welcome window" button in a page is clicked.</summary>
    public event Action? WelcomeClicked;

    /// <summary>A page's body: its blocks one under the other, in a text box that can be selected but not typed in.</summary>
    /// <param name="html">The page's HTML.</param>
    /// <returns>The body, and the margins of its first and last blocks, which the space around it takes into account.</returns>
    public (FrameworkElement Body, double Top, double Bottom) Render(string html)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), FontSize = 12.5 };
        document.SetResourceReference(FlowDocument.FontFamilyProperty, "ui-font");
        document.SetResourceReference(FlowDocument.ForegroundProperty, "ink");
        List<WikiNode> loose = [];
        foreach (WikiNode node in WikiHtml.Parse(html))
        {
            if (node is WikiElement element && IsBlock(element.Name))
            {
                FlushLoose(loose, document);
                document.Blocks.Add(BlockOf(element, isFirst: document.Blocks.Count == 0));
            }
            // white space between blocks is only the HTML's layout
            else if (node is WikiText { Text: var text } && loose.Count == 0 && string.IsNullOrWhiteSpace(text)) continue;
            else loose.Add(node);
        }
        FlushLoose(loose, document);
        double top = document.Blocks.FirstBlock?.Margin.Top ?? 0, bottom = document.Blocks.LastBlock?.Margin.Bottom ?? 0;
        // the space outside the first and last blocks is the caller's to give
        if (document.Blocks.FirstBlock is Block first) first.Margin = new Thickness(first.Margin.Left, 0, first.Margin.Right, first.Margin.Bottom);
        if (document.Blocks.LastBlock is Block last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        var box = new RichTextBox
        {
            Document = document,
            IsReadOnly = true,
            // links and the buttons in a page work in a read-only box only with this
            IsDocumentEnabled = true,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Arrow,
            Focusable = true,
            FocusVisualStyle = null,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        box.SetResourceReference(TextBoxBase.SelectionBrushProperty, "accent");
        // a text box's own look is a template with a border; the bare text is what the page wants
        box.Template = BareTemplate;
        return (box, top, bottom);
    }

    /// <summary>A text box drawn as its text alone: no border, no background, no scroll bars of its own.</summary>
    private static readonly ControlTemplate BareTemplate = MakeBareTemplate();

    private static ControlTemplate MakeBareTemplate()
    {
        var template = new ControlTemplate(typeof(TextBoxBase));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(UIElement.FocusableProperty, false);
        template.VisualTree = host;
        return template;
    }

    private static bool IsBlock(string name) => name is "p" or "h2" or "h3" or "ul" or "ol" or "div" or "table" or "pre";

    /// <summary>Text that is not inside a block becomes a paragraph of its own.</summary>
    private void FlushLoose(List<WikiNode> loose, FlowDocument document)
    {
        if (loose.Count == 0) return;
        document.Blocks.Add(Paragraph(loose, lineHeight: 18.125, top: 0, bottom: 0));
        loose.Clear();
    }

    private Block BlockOf(WikiElement element, bool isFirst) => element.Name switch
    {
        "p" when element.HasClass("wklead") => LeadParagraph(element),
        "p" => Paragraph(element.Children, look.TextLineHeight, look.ParagraphTop, look.ParagraphBottom),
        "h2" => SectionHeading(element, isFirst),
        "h3" when look.HasWikiHeadings => Heading(element, isFirst),
        "h3" => SmallHeading(element),
        "ol" when element.HasClass("wksteps") => Steps(element),
        "ul" or "ol" => List(element),
        "div" when element.HasClass("wkcall") => Callout(element),
        "div" when element.HasClass("helptbl") => Table(element),
        "pre" => Code(element),
        _ => Paragraph(element.Children, lineHeight: 18.125, top: 0, bottom: 0),
    };

    // ---- blocks ----

    /// <summary>A paragraph: its text wraps, in lines of the given height, with the margins given above and below.</summary>
    private Paragraph Paragraph(IEnumerable<WikiNode> children, double lineHeight, double top, double bottom)
    {
        var paragraph = new Paragraph
        {
            LineHeight = lineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(0, top, 0, bottom),
        };
        paragraph.Inlines.AddRange(Inlines(children));
        return paragraph;
    }

    /// <summary>p.wklead: the page's opening line, a little larger, in the dim colour.</summary>
    private Paragraph LeadParagraph(WikiElement element)
    {
        Paragraph lead = Paragraph(element.Children, look.LeadLineHeight, look.LeadTop, 12);
        lead.FontSize = 13.5;
        lead.SetResourceReference(TextElement.ForegroundProperty, "muted");
        return lead;
    }

    /// <summary>h3: a heading in the accent colour with a line under it; none above it at the top of the page.</summary>
    private Paragraph Heading(WikiElement element, bool isFirst)
    {
        Paragraph heading = Paragraph(element.Children, 19.575, isFirst ? 0 : 18, 6);
        heading.FontSize = 13.5;
        heading.FontWeight = FontWeights.Bold;
        heading.Padding = new Thickness(0, 0, 0, 3);
        heading.BorderThickness = new Thickness(0, 0, 0, 1);
        heading.SetResourceReference(TextElement.ForegroundProperty, "accent");
        heading.SetResourceReference(Block.BorderBrushProperty, "line");
        return heading;
    }

    /// <summary>h2 (the About tab's): a heading in the accent colour with a line under it; none above it at the top.</summary>
    private Paragraph SectionHeading(WikiElement element, bool isFirst)
    {
        // the change log's headings keep no space below them either
        double below = look.SectionHeadingMargin == 0 ? 0 : 6;
        Paragraph heading = Paragraph(element.Children, look.SectionHeadingSize * 1.45, isFirst ? 0 : look.SectionHeadingMargin, below);
        heading.FontSize = look.SectionHeadingSize;
        heading.FontWeight = FontWeights.Bold;
        heading.Padding = new Thickness(0, 0, 0, 3);
        heading.BorderThickness = new Thickness(0, 0, 0, 1);
        heading.SetResourceReference(TextElement.ForegroundProperty, "accent");
        heading.SetResourceReference(Block.BorderBrushProperty, "line");
        return heading;
    }

    /// <summary>h3 (the About tab's): a bold heading in the accent colour.</summary>
    private Paragraph SmallHeading(WikiElement element)
    {
        Paragraph heading = Paragraph(element.Children, 18.125, 12, 5);
        heading.FontWeight = FontWeights.Bold;
        heading.SetResourceReference(TextElement.ForegroundProperty, "accent");
        return heading;
    }

    /// <summary>ul and ol: each item after its bullet or number, a few pixels apart.</summary>
    private List List(WikiElement element)
    {
        var list = new List
        {
            MarkerStyle = element.Name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Padding = new Thickness(look.ListIndent, 0, 0, 0),
            MarkerOffset = 6,
            Margin = new Thickness(0, look.ListTop, 0, 6),
        };
        foreach (WikiElement item in element.Children.OfType<WikiElement>().Where(child => child.Name == "li"))
            list.ListItems.Add(new ListItem(Paragraph(item.Children, look.TextLineHeight, look.ItemMargin, look.ItemMargin)));
        return list;
    }

    /// <summary>
    /// ol.wksteps: each step beside its number in a square with an accent line around it; the text's lines after the
    /// first start under the first, 36 pixels in.
    /// </summary>
    private Section Steps(WikiElement element)
    {
        // The squares are 24 pixels tall in 20-pixel lines, so they stick out 2 pixels above and below. Padding (unlike
        // margin) keeps that inside the list, which clips what is outside it; the margin is 2 less to keep the spacing.
        var steps = new Section { Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(0, 2, 0, 2) };
        int number = 0;
        foreach (WikiElement item in element.Children.OfType<WikiElement>().Where(child => child.Name == "li"))
        {
            number++;
            var square = new Bevel
            {
                Width = 24,
                Height = 24,
                Child = new TextBlock { Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            square.SetResourceReference(Bevel.TopLeftProperty, "accent");
            square.SetResourceReference(Bevel.BottomRightProperty, "accent");
            ((TextBlock)square.Child).SetResourceReference(TextBlock.ForegroundProperty, "accent");
            Paragraph step = Paragraph(item.Children, 20, 6, 6);
            step.Margin = new Thickness(36, 6, 0, 6);
            step.TextIndent = -36;
            // the square and 12 pixels, which make the 36 the lines after the first start at
            step.Inlines.InsertBefore(step.Inlines.FirstInline, new InlineUIContainer(new Border { Width = 12 }));
            step.Inlines.InsertBefore(step.Inlines.FirstInline, new InlineUIContainer(square) { BaselineAlignment = BaselineAlignment.Center });
            steps.Blocks.Add(step);
        }
        return steps;
    }

    /// <summary>
    /// pre: NDF text as it looks in a file, in the code font in a sunken box, with its line breaks and spacing kept
    /// (unlike other text, whose spaces collapse).
    /// </summary>
    private Section Code(WikiElement element)
    {
        var text = new Paragraph { FontSize = 12, Margin = new Thickness(0) };
        string[] lines = PlainText(element).Trim('\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) text.Inlines.Add(new LineBreak());
            text.Inlines.Add(new Run(lines[i].TrimEnd('\r')));
        }
        text.SetResourceReference(TextElement.FontFamilyProperty, "mono");
        text.SetResourceReference(TextElement.ForegroundProperty, "ink");
        var box = new Section(text) { Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(1), Margin = new Thickness(0, 6, 0, 10) };
        box.SetResourceReference(Block.BorderBrushProperty, "line");
        box.SetResourceReference(TextElement.BackgroundProperty, "well");
        return box;
    }

    /// <summary>All the text inside an element, as written.</summary>
    private static string PlainText(WikiElement element)
    {
        var text = new System.Text.StringBuilder();
        foreach (WikiNode child in element.Children)
            text.Append(child is WikiText words ? words.Text : child is WikiElement inner ? PlainText(inner) : "");
        return text.ToString();
    }

    /// <summary>
    /// div.wkcall: a tip on the panel colour, with a line around it and a 3-pixel stripe on the left: accent, or the
    /// warning colour for div.wkwarn. Its first bold word is its label, before the rest.
    /// </summary>
    private Section Callout(WikiElement element)
    {
        string colour = element.HasClass("wkwarn") ? "caution" : "accent";
        WikiElement? label = element.Children.OfType<WikiElement>().FirstOrDefault(child => child.Name == "b");
        Paragraph text = Paragraph(element.Children.Where(child => child != label), 18.125, 0, 0);
        if (label is not null)
        {
            var labelText = new Bold();
            labelText.Inlines.AddRange(Inlines(label.Children));
            labelText.SetResourceReference(TextElement.ForegroundProperty, colour);
            if (text.Inlines.FirstInline is Inline firstInline) text.Inlines.InsertBefore(firstInline, labelText);
            else text.Inlines.Add(labelText);
            // the label's 10 pixels before the text
            text.Inlines.InsertAfter(labelText, new InlineUIContainer(new Border { Width = 10 }));
        }
        // the stripe is the outer box's left edge; the line around the rest is the inner box's
        var inner = new Section(text) { Padding = new Thickness(12, 7, 12, 7), BorderThickness = new Thickness(0, 1, 1, 1), Margin = new Thickness(0) };
        inner.SetResourceReference(Block.BorderBrushProperty, "line");
        inner.SetResourceReference(TextElement.BackgroundProperty, "panel");
        var outer = new Section(inner) { BorderThickness = new Thickness(3, 0, 0, 0), Margin = new Thickness(0, 10, 0, 10) };
        outer.SetResourceReference(Block.BorderBrushProperty, colour);
        return outer;
    }

    /// <summary>
    /// .helptbl: drawn as the app's lists are (a grid of cells), placed in the page. A document's own tables share
    /// their width badly, so these cells' text is the one part of a page that cannot be selected.
    /// </summary>
    private BlockUIContainer Table(WikiElement element) => new(GridTable(element)) { Margin = new Thickness(0, 6, 0, 10) };

    /// <summary>A cell's text, on lines of the list's height.</summary>
    private TextBlock TextOfLine(IEnumerable<WikiNode> children)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 18.125, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        text.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        text.Inlines.AddRange(Inlines(children));
        return text;
    }

    /// <summary>
    /// .helptbl: a table in a sunken box, drawn as the app's lists are: the headings on the panel colour, and cells that
    /// stay on one line ending in "..." unless "Wrap text in lists" is on. The first column is in the accent colour and
    /// never wraps. Each column's share of the width follows its longest text, as in <see cref="WebTable"/>.
    /// </summary>
    private Bevel GridTable(WikiElement element)
    {
        List<WikiElement> rows = [.. Descendants(element).Where(node => node.Name == "tr")];
        int columnCount = rows.Count == 0 ? 0 : rows.Max(row => row.Children.OfType<WikiElement>().Count());
        var grid = new Grid();
        double[] widest = new double[columnCount];
        double firstColumnNeeds = 0;
        for (int column = 0; column < columnCount; column++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            List<WikiElement> cells = [.. rows[rowIndex].Children.OfType<WikiElement>()];
            bool isHeading = cells.All(cell => cell.Name == "th");
            bool isLast = rowIndex == rows.Count - 1;
            for (int column = 0; column < cells.Count; column++)
            {
                FrameworkElement cell = isHeading ? GridHeadingCell(cells[column]) : GridCell(cells[column], column == 0, isLast);
                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
                double width = Math.Min(WebTable.TextWidth(TextOf(cells[column]), code: false), WebTable.WidestCellText(code: false));
                if (column == 0 && !isHeading)
                {
                    // the first column never wraps, and its keys (kbd) are boxes wider than their text, so it is measured
                    cell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    firstColumnNeeds = Math.Max(firstColumnNeeds, cell.DesiredSize.Width);
                    width = Math.Max(width, cell.DesiredSize.Width - 14);
                }
                widest[column] = Math.Max(widest[column], width);
            }
            if (isHeading)
            {
                var darkLine = new Bevel { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Edges = new Thickness(0) };
                darkLine.SetResourceReference(Bevel.BackgroundProperty, "lo");
                Grid.SetRow(darkLine, rowIndex);
                Grid.SetColumnSpan(darkLine, Math.Max(1, columnCount));
                grid.Children.Add(darkLine);
            }
        }
        // the text and its 7 pixels of padding each side
        for (int column = 0; column < columnCount; column++)
            grid.ColumnDefinitions[column].Width = new GridLength(widest[column] + 14, GridUnitType.Star);
        if (columnCount > 0) grid.ColumnDefinitions[0].MinWidth = firstColumnNeeds;
        var box = new Bevel { Child = grid };
        box.SetResourceReference(Bevel.TopLeftProperty, "lo");
        box.SetResourceReference(Bevel.BottomRightProperty, "hi");
        box.SetResourceReference(Bevel.BackgroundProperty, "well");
        return box;
    }

    private Bevel GridHeadingCell(WikiElement cell)
    {
        // thead th: never wraps
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        text.Inlines.AddRange(Inlines(cell.Children));
        return new Bevel { Style = (Style)Application.Current.FindResource("ColumnHeading"), Child = text };
    }

    /// <summary>A cell, with a faint line under it except in the last row.</summary>
    private Bevel GridCell(WikiElement cell, bool isFirstColumn, bool isLastRow)
    {
        TextBlock text = ListText.Follow(TextOfLine(cell.Children));
        if (isFirstColumn)
        {
            text.TextWrapping = TextWrapping.NoWrap;
            text.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        }
        var box = new Bevel { Edges = new Thickness(0, 0, 0, isLastRow ? 0 : 1), Padding = new Thickness(7, 3, 7, 3), Child = text, VerticalAlignment = VerticalAlignment.Stretch };
        box.SetResourceReference(Bevel.BottomRightProperty, "line");
        return box;
    }

    private static IEnumerable<WikiElement> Descendants(WikiElement element)
    {
        foreach (WikiElement child in element.Children.OfType<WikiElement>())
        {
            yield return child;
            foreach (WikiElement below in Descendants(child)) yield return below;
        }
    }

    // ---- inline parts ----

    private IEnumerable<Inline> Inlines(IEnumerable<WikiNode> nodes)
    {
        foreach (WikiNode node in nodes)
        {
            if (node is WikiText text)
            {
                foreach (Inline part in ColouredWords(CollapseWhiteSpace(text.Text))) yield return part;
            }
            else if (node is WikiElement element) yield return InlineOf(element);
        }
    }

    /// <summary>The status colours' names, and the theme colour each is shown in, as the Files tab shows its statuses.</summary>
    private static readonly Dictionary<string, string> StatusColours = new(StringComparer.OrdinalIgnoreCase)
    {
        ["green"] = "ok",
        ["yellow"] = "caution",
        ["red"] = "warn",
    };

    /// <summary>
    /// A text with the words "green", "yellow" and "red" shown in the theme's own colours for them, so the pages match
    /// what the app shows in any theme.
    /// </summary>
    private static IEnumerable<Inline> ColouredWords(string text)
    {
        int start = 0;
        foreach (Match word in Regex.Matches(text, "\\b(green|yellow|red)\\b", RegexOptions.IgnoreCase))
        {
            if (word.Index > start) yield return new Run(text[start..word.Index]);
            var coloured = new Run(word.Value);
            coloured.SetResourceReference(TextElement.ForegroundProperty, StatusColours[word.Value]);
            yield return coloured;
            start = word.Index + word.Length;
        }
        if (start < text.Length || start == 0) yield return new Run(text[start..]);
    }

    private Inline InlineOf(WikiElement element)
    {
        switch (element.Name)
        {
            case "b":
                return new Bold(Joined(element));
            case "i":
                return new Italic(Joined(element));
            case "a":
                return Link(element);
            case "kbd":
                return Key(element);
            case "button":
                return WelcomeButton(element);
            case "span" when element.HasClass("mono"):
                var code = new Span(Joined(element)) { FontSize = 12 };
                code.SetResourceReference(TextElement.FontFamilyProperty, "mono");
                return code;
            case "span" when element.HasClass("wksym"):
                return Symbol(element);
            case "span" when element.HasClass("note"):
                var note = new Span(Joined(element)) { FontWeight = FontWeights.Normal };
                note.SetResourceReference(TextElement.ForegroundProperty, "muted");
                return note;
            default:
                return new Span(Joined(element));
        }
    }

    /// <summary>As in a browser, any run of spaces and line breaks shows as one space.</summary>
    private static string CollapseWhiteSpace(string text)
    {
        var collapsed = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            bool isSpace = c is ' ' or '\n' or '\r' or '\t';
            if (isSpace && collapsed.Length > 0 && collapsed[^1] == ' ') continue;
            collapsed.Append(isSpace ? ' ' : c);
        }
        return collapsed.ToString();
    }

    /// <summary>An element's inline parts in one span.</summary>
    private Span Joined(WikiElement element)
    {
        var span = new Span();
        span.Inlines.AddRange(Inlines(element.Children));
        return span;
    }

    /// <summary>
    /// A link, underlined in the accent colour: to another page (<c>data-p</c>), or to a web address, which opens in the
    /// browser.
    /// </summary>
    private Hyperlink Link(WikiElement element)
    {
        var link = new Hyperlink(Joined(element)) { Cursor = Cursors.Hand };
        link.SetResourceReference(TextElement.ForegroundProperty, "accent");
        string page = element.Attribute("data-p");
        string address = element.Attribute("href");
        if (page != "") link.Click += (_, _) => PageLinkClicked?.Invoke(page);
        else if (address.StartsWith("https://", StringComparison.Ordinal))
        {
            link.ToolTip = address;
            link.Click += (_, _) => OpenInBrowser(address);
        }
        return link;
    }

    private static void OpenInBrowser(string address)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>
    /// kbd: a key, in the code font on a small raised key. kbd class="mouse" is a click, drawn as a mouse; with "double"
    /// too, a double-click.
    /// </summary>
    private static InlineUIContainer Key(WikiElement element)
    {
        FrameworkElement face;
        if (element.HasClass("mouse"))
        {
            face = new Controls.MouseGlyph { IsDouble = element.HasClass("double"), Margin = new Thickness(0, 2, 0, 2) };
            face.SetResourceReference(Controls.MouseGlyph.ForegroundProperty, "ink");
        }
        else
        {
            var text = new TextBlock { Text = TextOf(element), FontSize = 11.5 };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
            text.SetResourceReference(TextBlock.ForegroundProperty, "ink");
            face = text;
        }
        var key = new Bevel { Padding = new Thickness(5, 0, 5, 0), Child = face };
        key.SetResourceReference(Bevel.TopLeftProperty, "hi");
        key.SetResourceReference(Bevel.BottomRightProperty, "lo");
        key.SetResourceReference(Bevel.BackgroundProperty, "panel");
        return new InlineUIContainer(key) { BaselineAlignment = BaselineAlignment.Center };
    }

    private InlineUIContainer WelcomeButton(WikiElement element)
    {
        var button = new Button { Content = TextOf(element), ToolTip = Texts.Get("Help.OpenWelcome.Tip") };
        button.Click += (_, _) => WelcomeClicked?.Invoke();
        return new InlineUIContainer(button) { BaselineAlignment = BaselineAlignment.Center };
    }

    /// <summary>span.wksym: a rule type's symbol, 18 pixels wide, in the colour its style names (<c>color:var(--accent)</c>).</summary>
    private static InlineUIContainer Symbol(WikiElement element)
    {
        string style = element.Attribute("style");
        int start = style.IndexOf("var(--", StringComparison.Ordinal);
        string colour = start < 0 ? "ink" : style[(start + "var(--".Length)..].TrimEnd(')', ';', ' ');
        FrameworkElement symbol = Controls.FolderGlyph.For(TextOf(element), colour, 18);
        return new InlineUIContainer(symbol) { BaselineAlignment = BaselineAlignment.Center };
    }

    /// <summary>All the text inside an element.</summary>
    private static string TextOf(WikiElement element) =>
        string.Concat(element.Children.Select(child => child is WikiText text ? text.Text : child is WikiElement inner ? TextOf(inner) : ""));
}
