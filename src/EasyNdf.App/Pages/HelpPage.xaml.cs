using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Help;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Help tab (the HTML app's wiki: <c>wkList</c>, <c>wkGo</c> and the handlers of its buttons). The page shown is
/// remembered for the next start; Back and Forward go through the pages seen.
/// </summary>
public partial class HelpPage : UserControl
{
    /// <summary>The pages gone back from, latest last, and the pages gone forward from.</summary>
    private readonly Stack<string> backPages = new(), forwardPages = new();

    private readonly WikiRenderer renderer = new(PageLook.Wiki);

    /// <summary>The id of the page shown.</summary>
    private string shownId = "";

    /// <summary>Raised when a Welcome window button is clicked.</summary>
    public event Action? WelcomeRequested;

    /// <summary>Builds the tab on the page it showed last (Start Here the first time).</summary>
    public HelpPage()
    {
        InitializeComponent();
        renderer.PageLinkClicked += id => Go(id);
        renderer.WelcomeClicked += () => WelcomeRequested?.Invoke();
        string last = (string?)AppData.Current.State["helpPage"] ?? "start";
        Go(last, remember: false);
    }

    /// <summary>Shows a page (the HTML app's <c>wkGo</c>); an unknown id shows the first page.</summary>
    /// <param name="id">The page's id.</param>
    /// <param name="remember">Whether the page shown before is kept for Back; not when going Back or Forward.</param>
    public void Go(string id, bool remember = true)
    {
        WikiPage page = WikiPages.ById(id) ?? WikiPages.All[0];
        if (remember && shownId != "" && shownId != page.Id)
        {
            backPages.Push(shownId);
            forwardPages.Clear();
        }
        shownId = page.Id;
        CrumbText.Text = page.Group;
        PageTitle.Text = page.Title;
        (FrameworkElement body, double top, double bottom) = renderer.Render(page.Html);
        // the larger of the title's 6 pixels and the first block's margin, and of the last block's and the pager's 22
        body.Margin = new Thickness(0, Math.Max(6, top), 0, 0);
        Body.Content = body;
        Pager.Margin = new Thickness(0, Math.Max(22, bottom), 0, 0);
        ShowPager(page);
        BackButton.IsEnabled = backPages.Count > 0;
        ForwardButton.IsEnabled = forwardPages.Count > 0;
        ShowList();
        AppData.Current.State["helpPage"] = page.Id;
        AppData.Current.SaveState();
        // a new page starts at its top
        PageScroller.ScrollToTop();
    }

    /// <summary>"‹ Previous: X" and "Next: X ›": the pages before and after this one in the list.</summary>
    private void ShowPager(WikiPage page)
    {
        int index = WikiPages.All.ToList().IndexOf(page);
        WikiPage? previous = index > 0 ? WikiPages.All[index - 1] : null;
        WikiPage? next = index < WikiPages.All.Count - 1 ? WikiPages.All[index + 1] : null;
        ShowPagerButton(PreviousButton, previous, "Previous");
        ShowPagerButton(NextButton, next, "Next");
    }

    private static void ShowPagerButton(Button button, WikiPage? target, string label)
    {
        button.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        if (target is null) return;
        button.Tag = target.Id;
        var text = new TextBlock();
        var labelRun = new System.Windows.Documents.Run(label + ":");
        // the label is dim, except under the mouse (Styles/Controls.xaml, PagerLabel)
        labelRun.Style = (Style)Application.Current.FindResource("PagerLabel");
        if (label == "Previous") text.Inlines.Add("‹ ");
        text.Inlines.Add(labelRun);
        text.Inlines.Add(" " + target.Title);
        if (label == "Next") text.Inlines.Add(" ›");
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
        PageList.Children.Clear();
        string group = "";
        foreach (WikiPage page in WikiPages.All)
        {
            if (search != "" && !page.SearchText.Contains(search, StringComparison.Ordinal)) continue;
            if (page.Group != group)
            {
                group = page.Group;
                var heading = new TextBlock { Text = group, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, PageList.Children.Count == 0 ? 2 : 12, 6, 3) };
                heading.SetResourceReference(TextBlock.ForegroundProperty, "accent");
                PageList.Children.Add(heading);
            }
            var item = new ToggleButton
            {
                Style = (Style)FindResource("WikiNavItem"),
                Content = new TextBlock { Text = page.Title, TextWrapping = TextWrapping.Wrap },
                IsChecked = page.Id == shownId,
                Tag = page.Id,
            };
            item.Click += (_, _) => Go((string)item.Tag);
            PageList.Children.Add(item);
        }
        if (PageList.Children.Count == 0)
        {
            var none = new TextBlock { Text = "No page has that.", Margin = new Thickness(8, 4, 8, 4) };
            none.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            PageList.Children.Add(none);
        }
    }

    private void FindChanged(object sender, TextChangedEventArgs e) => ShowList();

    /// <summary>Enter in the search box opens the first page found.</summary>
    private void FindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (PageList.Children.OfType<ToggleButton>().FirstOrDefault() is { Tag: string id }) Go(id);
    }

    // ---- the buttons ----

    private void BackClicked(object sender, RoutedEventArgs e)
    {
        if (backPages.Count == 0) return;
        forwardPages.Push(shownId);
        Go(backPages.Pop(), remember: false);
    }

    private void ForwardClicked(object sender, RoutedEventArgs e)
    {
        if (forwardPages.Count == 0) return;
        backPages.Push(shownId);
        Go(forwardPages.Pop(), remember: false);
    }

    /// <summary>The group above the title opens the group's first page.</summary>
    private void CrumbClicked(object sender, RoutedEventArgs e)
    {
        WikiPage? first = WikiPages.All.FirstOrDefault(page => page.Group == CrumbText.Text);
        if (first is not null) Go(first.Id);
    }

    private void PagerClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) Go(id);
    }

    private void WelcomeClicked(object sender, RoutedEventArgs e) => WelcomeRequested?.Invoke();
}
