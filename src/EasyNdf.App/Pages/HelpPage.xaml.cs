using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Editor;
using EasyNdf.App.Help;
using EasyNdf.Engine.Editing;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Help tab (the HTML app's wiki: <c>wkList</c>, <c>wkGo</c> and the handlers of its buttons). The page shown is
/// remembered for the next start; Back and Forward go through the pages seen.
/// </summary>
public partial class HelpPage : UserControl
{
    /// <summary>The pages gone back from, latest last, and the pages gone forward from, each with how far down it was.</summary>
    private readonly Stack<(string Id, double Offset)> backPages = new(), forwardPages = new();

    private readonly WikiRenderer renderer = new(PageLook.Wiki);

    /// <summary>The id of the page shown.</summary>
    private string shownId = "";

    /// <summary>The variable the page was opened at ("ref:TType#Name"), until the page is scrolled by hand; null when none.</summary>
    private string? shownVariable;

    /// <summary>Raised when a Welcome window button is clicked.</summary>
    public event Action? WelcomeRequested;

    /// <summary>Where each heading of the page shown is, down the page, worked out when first needed after it is drawn.</summary>
    private List<double>? headingPlaces;

    /// <summary>The section marked in the right column, so it is scrolled into sight only when it changes; -1 for none yet.</summary>
    private int railMarked = -1;

    /// <summary>Builds the tab on the page it showed last (Start Here the first time).</summary>
    public HelpPage()
    {
        InitializeComponent();
        PageScroller.ScrollChanged += (_, _) => MarkSectionInView();
        PageScroller.SizeChanged += (_, _) => headingPlaces = null;
        // Back, Forward and Welcome Window end where the page does, the right column under them left clear
        Rail.IsVisibleChanged += (_, _) => TopBar.Margin = new Thickness(0, 0, Rail.IsVisible ? Rail.Width + Rail.Margin.Left : 0, 6);
        // scrolled by hand: the page keeps its place when its examples come in
        PageScroller.PreviewMouseWheel += (_, _) => shownVariable = null;
        PageScroller.PreviewMouseLeftButtonDown += (_, _) => shownVariable = null;
        PageScroller.PreviewKeyDown += (_, _) => shownVariable = null;
        renderer.PageLinkClicked += id => Go(id);
        renderer.WelcomeClicked += () => WelcomeRequested?.Invoke();
        string last = (string?)AppData.Current.State["helpPage"] ?? "start";
        Go(last, remember: false);
        // the first page shown drew, at the first look, another line's text over one of its own (the SteamCMD page's
        // warning under step 3's "Paste this into it"); drawn once more as the tab first shows, it is right, as it is
        // whenever a page is opened again
        void FirstShown(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible) return;
            IsVisibleChanged -= FirstShown;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
            {
                if (shownVariable is null) Go(shownId, remember: false, offset: PageScroller.VerticalOffset);
            });
        }
        IsVisibleChanged += FirstShown;
        // the page and the list are drawn again in the new language
        Texts.LanguageChanged += () => { if (shownId != "") Go(shownId, remember: false); };
    }

    /// <summary>Shows a page (the HTML app's <c>wkGo</c>); an unknown id shows the first page.</summary>
    /// <param name="id">The page's id.</param>
    /// <param name="remember">Whether the page shown before is kept for Back; not when going Back or Forward.</param>
    /// <param name="offset">How far down to show the page (Back and Forward); its top, or the variable asked for, when null.</param>
    public void Go(string id, bool remember = true, double? offset = null)
    {
        // a Reference page needs the File Editor's guide, read once in the background
        if (ReferencePages.IsReference(id) && EditorGuide.IfRead is null)
        {
            ShowLoading();
            _ = GoOnceRead(id, remember, offset);
            return;
        }
        WikiPage page = PageById(id);
        // "ref:TType#Name" opens the type's page at the variable's heading
        string? variable = id.Contains('#', StringComparison.Ordinal) ? id[(id.IndexOf('#', StringComparison.Ordinal) + 1)..] : null;
        if (remember && shownId != "" && shownId != page.Id)
        {
            backPages.Push((shownId, PageScroller.VerticalOffset));
            forwardPages.Clear();
        }
        shownId = page.Id;
        shownVariable = offset is null ? variable : null;
        CrumbText.Text = page.Group;
        ShowIndexCrumb(page.Id);
        PageTitle.Text = page.Title;
        // the related pages are listed in the right column
        (FrameworkElement body, double top, double bottom) = renderer.Render(page.Html);
        headingPlaces = null;
        // the marker is placed again once the new page has been laid out, when its headings have their places
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => { headingPlaces = null; railMarked = -1; MarkSectionInView(); });
        TextColumn.MaxWidth = 880;
        // room for the right column as well as the page
        Wiki.MaxWidth = 1440;
        // the larger of the title's 6 pixels and the first block's margin, and of the last block's and the pager's 22
        body.Margin = new Thickness(0, Math.Max(6, top), 0, 0);
        Body.Content = body;
        StayPut(body);
        Pager.Margin = new Thickness(0, Math.Max(22, bottom), 0, 0);
        ShowPager(page);
        BackButton.IsEnabled = backPages.Count > 0;
        ForwardButton.IsEnabled = forwardPages.Count > 0;
        ShowList();
        ReferenceFind.Visibility = page.Id == ReferencePages.IndexId ? Visibility.Visible : Visibility.Collapsed;
        if (page.Id == ReferencePages.IndexId && ReferenceFind.Text.Trim() != "") ShowReferenceIndex();
        ShowRail();
        if (page.Id.StartsWith("ref:", StringComparison.Ordinal) && ReferenceExamples.IfRead is null && EditorGuide.IfRead is ValueGuide forExamples)
            _ = ShowExamplesOnceRead(forExamples);
        AppData.Current.State["helpPage"] = page.Id;
        AppData.Current.SaveState();
        // a new page starts at its top, or at the variable asked for; Back and Forward return to where the page was
        PageScroller.ScrollToTop();
        if (offset is double down)
        {
            PageScroller.UpdateLayout();
            PageScroller.ScrollToVerticalOffset(down);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => PageScroller.ScrollToVerticalOffset(down));
        }
        else if (variable is not null && renderer.Headings.FirstOrDefault(heading => heading.Text == variable) is { Block: not null } wanted)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => ScrollTo(wanted.Block));
    }

    /// <summary>A page by its id: a Help page, or a Reference page once the guide is read; the first page when none has it.</summary>
    private static WikiPage PageById(string id)
    {
        if (ReferencePages.IsReference(id) && EditorGuide.IfRead is ValueGuide guide)
            return ReferencePages.Page(id, guide) ?? ReferencePages.Page(ReferencePages.IndexId, guide)!;
        return WikiPages.ById(id) ?? WikiPages.All[0];
    }

    /// <summary>
    /// A click on a link in the page gives the page's text box the keyboard, and the page would then scroll to show the
    /// whole box from its top; the page stays where it is instead.
    /// </summary>
    private static void StayPut(FrameworkElement body) =>
        // any part of the page asking to be shown (the text box, its caret, a link taking the keyboard) is refused: the
        // page scrolls only by the wheel, the scroll bar and Help's own jumps
        body.RequestBringIntoView += (_, e) => e.Handled = true;

    /// <summary>Reads the examples from the user's WARNO, then shows the type's page again with them, if it is still shown.</summary>
    private async Task ShowExamplesOnceRead(ValueGuide guide)
    {
        string waitingOn = shownId;
        if (await ReferenceExamples.Read(guide) is null || shownId != waitingOn) return;
        // the examples move the headings down: a page opened at a variable ("Open in Help" in the editor's tip) goes back to it
        if (shownVariable is string variable)
        {
            Go(shownId + "#" + variable, remember: false);
            return;
        }
        double offset = PageScroller.VerticalOffset;
        Go(shownId, remember: false);
        PageScroller.ScrollToVerticalOffset(offset);
    }

    /// <summary>"Reading the reference…" while the guide is read.</summary>
    private void ShowLoading()
    {
        PageTitle.Text = Texts.Get("Help.Reference.Title");
        CrumbText.Text = Texts.Get("Help.Group.Reference");
        Body.Content = new TextBlock { Text = Texts.Get("Help.Loading"), Margin = new Thickness(0, 6, 0, 0) };
        Rail.Visibility = Visibility.Collapsed;
        ReferenceFind.Visibility = Visibility.Collapsed;
        Pager.Visibility = Visibility.Collapsed;
    }

    private async Task GoOnceRead(string id, bool remember, double? offset)
    {
        try { await EditorGuide.Get(); }
        catch (InvalidOperationException) { }
        if (EditorGuide.IfRead is null) Go("start", remember);
        else Go(id, remember, offset);
    }

    /// <summary>A related page's name: a tab's Overview with the tab's name, as "Mods &amp; Rules Overview", so the Overviews are told apart.</summary>
    private static string RelatedTitle(WikiPage page) =>
        page.Id.StartsWith("tab-", StringComparison.Ordinal) ? page.Group + " " + page.Title : page.Title;

    /// <summary>The Help pages that link to this one and that its text does not link to already, at most six; none for the Reference.</summary>
    private static List<WikiPage> RelatedPages(WikiPage page)
    {
        if (ReferencePages.IsReference(page.Id)) return [];
        var linked = new List<WikiPage>();
        void Add(WikiPage? other)
        {
            if (other is not null && other.Id != page.Id && !linked.Contains(other)) linked.Add(other);
        }
        // pages the text already links to are not repeated
        var linkedInText = new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(page.Html, "data-p=\"([^\"]+)\"").Select(link => link.Groups[1].Value));
        foreach (WikiPage other in WikiPages.All)
            if (!linkedInText.Contains(other.Id) && other.Html.Contains("data-p=\"" + page.Id + "\"", StringComparison.Ordinal)) Add(other);
        // a tab's Overview links to all its own pages: the other tabs' Overviews are related instead
        if (linked.Count == 0 && page.Id.StartsWith("tab-", StringComparison.Ordinal))
            foreach (WikiPage other in WikiPages.All.Where(other => other.Id.StartsWith("tab-", StringComparison.Ordinal))) Add(other);
        return [.. linked.Take(6)];
    }

    /// <summary>
    /// The right column: the page's headings, each a link to its place (the one in view marked), then the related
    /// pages. Hidden when the page has neither.
    /// </summary>
    private void ShowRail()
    {
        RailContents.Children.Clear();
        RailRelated.Children.Clear();
        railMarked = -1;
        RailScroller.ScrollToTop();
        WikiPage? page = shownId == "" ? null : PageById(shownId);
        List<WikiPage> related = page is not null ? RelatedPages(page) : [];
        bool hasContents = renderer.Headings.Count >= 1;
        Rail.Visibility = hasContents || related.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RailContentsHeading.Text = Texts.Get("Help.Rail.OnThisPage").ToUpperInvariant();
        RailRelatedHeading.Text = Texts.Get("Help.Rail.Related").ToUpperInvariant();
        RailContentsHeading.Visibility = RailContents.Visibility = hasContents ? Visibility.Visible : Visibility.Collapsed;
        RailRelatedHeading.Visibility = RailRelated.Visibility = related.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RailRelatedHeading.Margin = new Thickness(0, hasContents ? 12 : 0, 0, 3);
        if (hasContents)
            foreach (var heading in renderer.Headings)
            {
                System.Windows.Documents.Paragraph? block = heading.Block;
                ToggleButton link = RailLink(heading.Text);
                link.Click += (_, _) =>
                {
                    if (block is not null) ScrollTo(block);
                    MarkSectionInView();
                };
                RailContents.Children.Add(link);
            }
        foreach (WikiPage other in related)
        {
            ToggleButton link = RailLink(RelatedTitle(other));
            link.Click += (_, _) => Go(other.Id);
            RailRelated.Children.Add(link);
        }
        MarkSectionInView();
    }

    private ToggleButton RailLink(string text) => new()
    {
        Style = (Style)FindResource("WikiRailLink"),
        Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text },
        Cursor = Cursors.Hand,
    };

    /// <summary>Marks, in the right column, the heading of the section at the top of the page as it scrolls.</summary>
    private void MarkSectionInView()
    {
        if (RailContents.Children.Count == 0 || Body.Content is not RichTextBox box || PageScroller.Content is not UIElement content) return;
        if (headingPlaces is null)
        {
            // Tuned for speed: the places are worked out once per page and size, not at every step of the scroll
            var places = new List<double>();
            foreach (var heading in renderer.Headings)
            {
                Rect at = heading.Block?.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward) ?? Rect.Empty;
                if (at.IsEmpty) return;
                places.Add(box.TranslatePoint(new Point(0, at.Top), content).Y);
            }
            // a page not laid out yet has every heading at 0: tried again once it is
            if (places.Count > 1 && places.Max() - places.Min() < 1) return;
            headingPlaces = places;
        }
        // the last heading at or above a line just under the top of the view; the first when none is yet
        double line = PageScroller.VerticalOffset + 60;
        int current = 0;
        for (int i = 0; i < headingPlaces.Count; i++)
            if (headingPlaces[i] <= line) current = i;
        // at the very end the last section is the one being read, however short
        if (PageScroller.ScrollableHeight > 0 && PageScroller.VerticalOffset >= PageScroller.ScrollableHeight - 1) current = headingPlaces.Count - 1;
        for (int i = 0; i < RailContents.Children.Count; i++)
            if (RailContents.Children[i] is ToggleButton link) link.IsChecked = i == current;
        // a list longer than the column scrolls the marked section into sight, only when it changes
        if (current != railMarked && RailContents.Children[current] is FrameworkElement { IsVisible: true } marked && marked.IsDescendantOf(RailScroller))
        {
            railMarked = current;
            if (current == 0)
            {
                RailScroller.ScrollToTop();
                return;
            }
            RailScroller.UpdateLayout();
            Rect at = marked.TransformToAncestor(RailScroller).TransformBounds(new Rect(marked.RenderSize));
            if (at.Top < 0) RailScroller.ScrollToVerticalOffset(RailScroller.VerticalOffset + at.Top - 24);
            else if (at.Bottom > RailScroller.ViewportHeight) RailScroller.ScrollToVerticalOffset(RailScroller.VerticalOffset + at.Bottom - RailScroller.ViewportHeight + 24);
        }
    }

    /// <summary>Scrolls the page so a heading is at its top, a few pixels under it.</summary>
    private void ScrollTo(System.Windows.Documents.Paragraph heading, int tries = 0)
    {
        if (Body.Content is not RichTextBox box || PageScroller.Content is not UIElement content) return;
        PageScroller.UpdateLayout();
        Rect at = heading.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
        // a page just drawn may not know where its text is yet: tried again once the app has caught up
        if (at.IsEmpty)
        {
            if (tries < 20) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => ScrollTo(heading, tries + 1));
            return;
        }
        double y = box.TranslatePoint(new Point(0, at.Top), content).Y;
        PageScroller.ScrollToVerticalOffset(Math.Max(0, y - 8));
    }

    /// <summary>"‹ Previous: X" and "Next: X ›": the pages before and after this one in the list.</summary>
    private void ShowPager(WikiPage page)
    {
        Pager.Visibility = ReferencePages.IsReference(page.Id) ? Visibility.Collapsed : Visibility.Visible;
        if (ReferencePages.IsReference(page.Id)) return;
        int index = WikiPages.All.ToList().IndexOf(page);
        WikiPage? previous = index > 0 ? WikiPages.All[index - 1] : null;
        WikiPage? next = index < WikiPages.All.Count - 1 ? WikiPages.All[index + 1] : null;
        ShowPagerButton(PreviousButton, previous, isNext: false);
        ShowPagerButton(NextButton, next, isNext: true);
        // alone, either button may take the whole width; together, each has its half
        Grid.SetColumnSpan(PreviousButton, next is null ? 3 : 1);
        Grid.SetColumn(NextButton, previous is null ? 0 : 2);
        Grid.SetColumnSpan(NextButton, previous is null ? 3 : 1);
    }

    private static void ShowPagerButton(Button button, WikiPage? target, bool isNext)
    {
        button.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        if (target is null) return;
        button.Tag = target.Id;
        // a long title is cut with "…", and the whole of it shows on hover (Controls/TrimmedTip.cs)
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        var labelRun = new System.Windows.Documents.Run(isNext ? Texts.Get("Help.Next") : Texts.Get("Help.Previous"));
        // the label is dim, except under the mouse (Styles/Controls.xaml, PagerLabel)
        labelRun.Style = (Style)Application.Current.FindResource("PagerLabel");
        if (!isNext) text.Inlines.Add("‹ ");
        text.Inlines.Add(labelRun);
        text.Inlines.Add(" " + target.Title);
        if (isNext) text.Inlines.Add(" ›");
        button.Content = text;
    }

    // ---- the list ----

    /// <summary>
    /// The list of pages under their groups, only those whose title or text has the search's words (the HTML app's
    /// <c>wkList</c>); "No page has that." when none has.
    /// </summary>
    private void ShowList()
    {
        string search = Find.Text.Trim().ToLowerInvariant();
        // a page opened from the list with the keys keeps the keyboard on its line, though the lines are made again
        bool listHadKeyboard = PageList.IsKeyboardFocusWithin;
        PageList.Children.Clear();
        string group = "";
        bool documentationShown = false;
        List<WikiPage> found = [.. WikiPages.All.Where(page => search == "" || page.SearchText.Contains(search, StringComparison.Ordinal))];
        string shownGroup = WikiPages.ById(shownId)?.Group ?? "";
        foreach (WikiPage page in found)
        {
            if (page.Group != group)
            {
                // Documentation comes right after the first group, Start
                if (group != "" && !documentationShown)
                {
                    ShowReferenceInList(search, listHadKeyboard);
                    documentationShown = true;
                }
                group = page.Group;
                PageList.Children.Add(GroupHeading(group, found.Count(other => other.Group == page.Group), group == shownGroup));
            }
            var item = new ToggleButton
            {
                Style = (Style)FindResource("WikiNavItemTidy"),
                Content = new TextBlock { Text = page.Title, TextWrapping = TextWrapping.Wrap },
                IsChecked = page.Id == shownId,
                Tag = page.Id,
                // the WikiNavItem style gives no hand, though the lines open pages as links do
                Cursor = Cursors.Hand,
            };
            item.Click += (_, _) => Go((string)item.Tag);
            PageList.Children.Add(item);
            if (listHadKeyboard && page.Id == shownId) Dispatcher.BeginInvoke(() => item.Focus());
        }
        if (!documentationShown) ShowReferenceInList(search, listHadKeyboard);
        if (PageList.Children.Count == 0)
        {
            var none = new TextBlock { Text = Texts.Get("Help.NoPageHasThat"), Margin = new Thickness(8, 4, 8, 4) };
            none.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            PageList.Children.Add(none);
        }
    }

    /// <summary>
    /// The Documentation group after Start, with its one page, "Types and Variables" (the types and variables
    /// are found with that page's own search box); when the list is searched, only if the search names it.
    /// </summary>
    private void ShowReferenceInList(string search, bool listHadKeyboard)
    {
        string title = Texts.Get("Help.Reference.Title"), group = Texts.Get("Help.Group.Reference");
        if (search != "" && !(title + " " + group).Contains(search, StringComparison.OrdinalIgnoreCase)) return;
        PageList.Children.Add(GroupHeading(group, 1, ReferencePages.IsReference(shownId)));
        AddListItem(title, ReferencePages.IndexId, listHadKeyboard);
    }

    /// <summary>
    /// A group's heading in the list: in small capitals, with how many pages it has, a line above it, and the accent
    /// colour only for the group of the page shown.
    /// </summary>
    private FrameworkElement GroupHeading(string group, int count, bool isShown)
    {
        bool first = PageList.Children.Count == 0;
        var name = new TextBlock { Text = group.ToUpperInvariant(), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
        var number = new TextBlock { Text = Texts.Number(count), FontSize = 10.5 };
        name.SetResourceReference(TextBlock.ForegroundProperty, isShown ? "accent" : "muted");
        number.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(number, Dock.Right);
        var row = new DockPanel { Margin = new Thickness(2, first ? 2 : 6, 4, 2) };
        row.Children.Add(number);
        row.Children.Add(name);
        if (first) return row;
        var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0), Margin = new Thickness(0, 4, 0, 0), Child = row };
        line.SetResourceReference(Border.BorderBrushProperty, "line");
        return line;
    }

    // ---- the Variables and Entries page's search ----

    /// <summary>Waits a moment after a key before searching, so typing stays quick.</summary>
    private System.Windows.Threading.DispatcherTimer? referenceSearchWait;

    private void ReferenceFindChanged(object sender, TextChangedEventArgs e)
    {
        referenceSearchWait ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250), System.Windows.Threading.DispatcherPriority.Input, (_, _) =>
        {
            referenceSearchWait!.Stop();
            if (shownId == ReferencePages.IndexId) ShowReferenceIndex();
        }, Dispatcher);
        referenceSearchWait.Stop();
        referenceSearchWait.Start();
    }

    /// <summary>Enter searches at once; Escape empties the box.</summary>
    private void ReferenceFindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ReferenceFind.Text != "")
        {
            e.Handled = true;
            ReferenceFind.Text = "";
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            referenceSearchWait?.Stop();
            ShowReferenceIndex();
        }
    }

    /// <summary>The Variables and Entries page drawn again for its search: every type, or what the search finds.</summary>
    private void ShowReferenceIndex()
    {
        if (EditorGuide.IfRead is not ValueGuide guide) return;
        WikiPage page = ReferencePages.Index(guide, ReferenceFind.Text.Trim());
        (FrameworkElement body, double top, _) = renderer.Render(page.Html);
        body.Margin = new Thickness(0, Math.Max(6, top), 0, 0);
        Body.Content = body;
        StayPut(body);
        ShowRail();
    }

    /// <summary>A line of the list that opens a page; marked when it is the page shown.</summary>
    private void AddListItem(string text, string id, bool listHadKeyboard)
    {
        var item = new ToggleButton
        {
            Style = (Style)FindResource("WikiNavItemTidy"),
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            // Types and Variables is marked on every Documentation page, as they are reached from it
            IsChecked = id == shownId || id == ReferencePages.IndexId && ReferencePages.IsReference(shownId),
            Tag = id,
            Cursor = Cursors.Hand,
        };
        item.Click += (_, _) => Go((string)item.Tag);
        PageList.Children.Add(item);
        if (listHadKeyboard && id == shownId) Dispatcher.BeginInvoke(() => item.Focus());
    }

    private void FindChanged(object sender, TextChangedEventArgs e) => ShowList();

    /// <summary>Enter in the search box opens the first page found; Escape empties it, as on the Settings tab.</summary>
    private void FindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Find.Text != "")
        {
            e.Handled = true;
            Find.Text = "";
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (PageList.Children.OfType<ToggleButton>().FirstOrDefault() is { Tag: string id }) Go(id);
    }

    /// <summary>
    /// The keys in the list of pages: Up and Down go to the page above or below, Home and End to the first or last, and
    /// Enter opens the page (Space does, as on any button).
    /// </summary>
    private void PageListKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        List<ToggleButton> items = [.. PageList.Children.OfType<ToggleButton>()];
        int at = items.FindIndex(item => item.IsKeyboardFocusWithin);
        if (items.Count == 0 || at < 0) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Go((string)items[at].Tag);
            return;
        }
        int to = e.Key switch
        {
            Key.Up => Math.Max(0, at - 1),
            Key.Down => Math.Min(items.Count - 1, at + 1),
            Key.Home => 0,
            Key.End => items.Count - 1,
            _ => -1,
        };
        if (to < 0) return;
        e.Handled = true;
        items[to].Focus();
        items[to].BringIntoView();
    }

    // ---- the buttons ----

    private void BackClicked(object sender, RoutedEventArgs e)
    {
        if (backPages.Count == 0) return;
        forwardPages.Push((shownId, PageScroller.VerticalOffset));
        (string id, double offset) = backPages.Pop();
        Go(id, remember: false, offset);
    }

    private void ForwardClicked(object sender, RoutedEventArgs e)
    {
        if (forwardPages.Count == 0) return;
        backPages.Push((shownId, PageScroller.VerticalOffset));
        (string id, double offset) = forwardPages.Pop();
        Go(id, remember: false, offset);
    }

    /// <summary>
    /// A Documentation page other than Types and Variables (a topic or a type) is reached from it: the trail names it, as
    /// "Help › Documentation › Types and Variables", and the list marks it.
    /// </summary>
    private void ShowIndexCrumb(string id)
    {
        bool under = ReferencePages.IsReference(id) && id != ReferencePages.IndexId;
        IndexCrumbText.Text = under ? Texts.Get("Help.Reference.Title") : "";
        IndexArrow.Text = under ? " › " : "";
    }

    private void IndexCrumbClicked(object sender, RoutedEventArgs e) => Go(ReferencePages.IndexId);

    /// <summary>"Help" in the trail above the title opens Help's first page.</summary>
    private void HelpCrumbClicked(object sender, RoutedEventArgs e) => Go(WikiPages.All[0].Id);

    /// <summary>The group above the title opens the group's first page.</summary>
    private void CrumbClicked(object sender, RoutedEventArgs e)
    {
        if (CrumbText.Text == Texts.Get("Help.Group.Reference"))
        {
            Go(ReferencePages.IndexId);
            return;
        }
        WikiPage? first = WikiPages.All.FirstOrDefault(page => page.Group == CrumbText.Text);
        if (first is not null) Go(first.Id);
    }

    private void PagerClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) Go(id);
    }

    private void WelcomeClicked(object sender, RoutedEventArgs e) => WelcomeRequested?.Invoke();

    /// <summary>
    /// Tutorials: a list of the tutorials under the button, in the order they run, with Start and Cancel under it. A
    /// double-click or Enter starts the one picked; Escape or a click elsewhere closes it.
    /// </summary>
    private void TutorialsClicked(object sender, RoutedEventArgs e)
    {
        // the list as the app's others: sunken, one line each (what it covers in its tooltip), the line picked in the
        // selection colour, as the Mods tab's lines are
        var list = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), FocusVisualStyle = null };
        var line = new FrameworkElementFactory(typeof(Border), "Row");
        line.SetValue(Border.PaddingProperty, new Thickness(6, 3, 6, 3));
        line.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        line.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = line };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("hover"), "Row"));
        var picked = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        picked.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("sel"), "Row"));
        template.Triggers.Add(hover);
        template.Triggers.Add(picked);
        var item = new Style(typeof(ListBoxItem));
        item.Setters.Add(new Setter(TemplateProperty, template));
        item.Setters.Add(new Setter(FocusVisualStyleProperty, null));
        list.ItemContainerStyle = item;
        foreach (Dialogs.Chapter chapter in Enum.GetValues<Dialogs.Chapter>())
        {
            var name = new TextBlock { Text = (int)chapter + 1 + ". " + Dialogs.TourOverlay.NameOf(chapter) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "ink");
            list.Items.Add(new ListBoxItem { Content = name, Tag = chapter, ToolTip = Dialogs.TourOverlay.AboutOf(chapter) });
        }
        list.SelectedIndex = 0;
        var well = new Controls.Bevel { MinWidth = 260, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(2), Child = list };
        well.SetResourceReference(Controls.Bevel.TopLeftProperty, "lo");
        well.SetResourceReference(Controls.Bevel.BottomRightProperty, "hi");
        well.SetResourceReference(Controls.Bevel.BackgroundProperty, "well");
        var start = new Button { Content = Texts.Get("Help.Tutorials.Start"), Margin = new Thickness(0, 0, 6, 0) };
        start.SetResourceReference(StyleProperty, "GoButton");
        var cancel = new Button { Content = Texts.Get("Common.Cancel") };
        var box = new Controls.Bevel { Padding = new Thickness(10), Child = new StackPanel { Children =
        {
            well,
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { start, cancel } },
        } } };
        box.SetResourceReference(Controls.Bevel.TopLeftProperty, "hi");
        box.SetResourceReference(Controls.Bevel.BottomRightProperty, "lo");
        box.SetResourceReference(Controls.Bevel.BackgroundProperty, "panel");
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            Child = box, PlacementTarget = TutorialsButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            VerticalOffset = 2, StaysOpen = false, AllowsTransparency = true,
        };
        void Go()
        {
            if (list.SelectedItem is not ListBoxItem { Tag: Dialogs.Chapter chosen }) return;
            popup.IsOpen = false;
            MainView.Of(this).Tour.Start(chosen);
        }
        start.Click += (_, _) => Go();
        cancel.Click += (_, _) => popup.IsOpen = false;
        list.MouseDoubleClick += (_, _) => Go();
        box.PreviewKeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) { key.Handled = true; Go(); }
            else if (key.Key == Key.Escape) { key.Handled = true; popup.IsOpen = false; }
        };
        popup.Opened += (_, _) => Dispatcher.BeginInvoke(() => (list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        popup.IsOpen = true;
    }
}
