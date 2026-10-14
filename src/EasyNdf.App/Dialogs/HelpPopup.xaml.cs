using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Help;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The popup of the ? buttons (the HTML app's <c>qOpen</c>, <c>qClose</c> and <c>qMore</c>). It opens under its button,
/// its right edge on the button's, kept 8 pixels inside the window; above the button when there is no room under it.
/// </summary>
public partial class HelpPopup : UserControl
{
    private readonly WikiRenderer renderer = new(PageLook.Popup);

    /// <summary>The button that opened the popup; null while it is closed.</summary>
    private Button? openedBy;

    /// <summary>The page shown.</summary>
    private string pageId = "";

    /// <summary>Raised to show a page on the Help tab: by See more, or a link in the popup.</summary>
    public event Action<string>? ShowPage;

    /// <summary>Builds the popup, closed.</summary>
    public HelpPopup()
    {
        InitializeComponent();
        renderer.PageLinkClicked += id => More(id);
    }

    /// <summary>Whether the popup is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Whether a point of the view is on the popup's box.</summary>
    public bool Contains(Point point) => IsOpen && new Rect(Canvas.GetLeft(Box), Canvas.GetTop(Box), Box.ActualWidth, Box.ActualHeight).Contains(point);

    /// <summary>
    /// Opens a page beside its ? button; the same button again closes it.
    /// </summary>
    /// <param name="button">The ? button.</param>
    /// <param name="id">The id of the Help page about its tab or window.</param>
    public void Open(Button button, string id)
    {
        if (IsOpen && ReferenceEquals(openedBy, button))
        {
            Close();
            return;
        }
        WikiPage page = WikiPages.ById(id) ?? WikiPages.All[0];
        pageId = page.Id;
        openedBy = button;
        Title.Text = page.Title;
        (FrameworkElement body, _, double bottom) = renderer.Render(page.Html);
        Body.Content = body;
        // See more is 8 pixels under the text, or more when the last block wants more
        MoreButton.Margin = new Thickness(0, Math.Max(8, bottom), 0, 0);
        Visibility = Visibility.Visible;
        Scroller.ScrollToTop();
        Place(button);
    }

    /// <summary>Under the button, or above it when there is no room; never closer than 8 pixels to the window's edges.</summary>
    private void Place(Button button)
    {
        // the browser's window is the app's, with its 10-pixel border
        // (the view's size: the popup itself has none until it has been laid out once)
        var view = (FrameworkElement)Parent;
        double windowWidth = view.ActualWidth + 20, windowHeight = view.ActualHeight + 20;
        Box.MaxHeight = Math.Min(windowHeight * 0.7, 544);
        Box.Measure(new Size(Box.Width, Box.MaxHeight));
        double width = Box.Width, height = Box.DesiredSize.Height;
        // the button is beside the popup in the view, not inside it
        Rect at = button.TransformToVisual(this).TransformBounds(new Rect(button.RenderSize));
        // window coordinates, then back to the view's, which starts 10 pixels in
        double right = at.Right + 10, top = at.Top + 10, bottom = at.Bottom + 10;
        double x = Math.Min(Math.Max(8, right - width), windowWidth - width - 8);
        double y = bottom + 4;
        if (y + height > windowHeight - 8) y = Math.Max(8, top - 4 - height);
        Canvas.SetLeft(Box, Math.Round(x) - 10);
        Canvas.SetTop(Box, Math.Round(y) - 10);
    }

    /// <summary>Closes the popup, and gives the keyboard back to its button.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        Button? button = openedBy;
        openedBy = null;
        if (button is { IsVisible: true }) button.Focus();
    }

    /// <summary>See more, or a link: closes the popup and shows the page on the Help tab.</summary>
    private void More(string id)
    {
        Close();
        ShowPage?.Invoke(id);
    }

    private void MoreClicked(object sender, RoutedEventArgs e) => More(pageId);

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
