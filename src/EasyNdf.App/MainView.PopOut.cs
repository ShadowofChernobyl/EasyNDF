using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Windowing;

namespace EasyNdf.App;

/// <summary>
/// A tab in a window of its own: right-click a tab, Open in New Window. The tab's page moves into the window and comes
/// back when the window closes; meanwhile the tab stays in its row, dimmed, and a click on it shows its window.
/// </summary>
/// <remarks>
/// The app's windows, questions and notifications (the layer <c>Overlays</c>) are shown in whichever of the app's
/// windows is in use, so a question asked from a page appears over that page. While one of them is open, the app's
/// other windows take no clicks or keys, as the pages under it take none; a viewer (the 3D viewer, the License and
/// change log) holds back nothing but the window it is in.
/// </remarks>
public partial class MainView
{
    /// <summary>The tabs open in windows of their own, each with its window.</summary>
    private readonly Dictionary<TabItem, PopOutWindow> poppedOut = [];

    /// <summary>The page of each such tab, to put back.</summary>
    private readonly Dictionary<TabItem, object?> pagesOut = [];

    /// <summary>Whether a tab is open in a window of its own.</summary>
    public bool IsPoppedOut(TabItem tab) => poppedOut.ContainsKey(tab);

    /// <summary>The tab row's right-click menu, and the click on a dimmed tab.</summary>
    private void WatchTabsForPopOut()
    {
        Tabs.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (TabAt(e.OriginalSource) is not TabItem tab) return;
            e.Handled = true;
            OpenPopOutMenu(tab);
        };
        Tabs.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (TabAt(e.OriginalSource) is not TabItem tab || !IsPoppedOut(tab)) return;
            e.Handled = true;
            ShowTab(tab);
        };
    }

    /// <summary>The tab of the main row a click was on, or null: not a tab of a page's own row of tabs.</summary>
    private TabItem? TabAt(object clicked)
    {
        for (DependencyObject? at = clicked as DependencyObject; at is not null and not TabControl; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is TabItem tab) return Tabs.Items.Contains(tab) ? tab : null;
        return null;
    }

    private void OpenPopOutMenu(TabItem tab)
    {
        bool tour = Tour.IsRunning;
        // a tab's own text can be bold: its menu's is not
        Menus.Show(tab, IsPoppedOut(tab)
            ?
            [
                new MenuChoice(Texts.Get("Main.PopOut.Show"), () => ShowTab(tab)),
                new MenuChoice(Texts.Get("Main.PopOut.Return"), () => poppedOut[tab].Close()),
            ]
            :
            [
                new MenuChoice(Texts.Get("Main.PopOut.Open"), () => PopOut(tab), IsEnabled: !tour, Tip: tour ? Texts.Get("Main.PopOut.Tour") : ""),
            ]).FontWeight = FontWeights.Normal;
    }

    /// <summary>
    /// Brings a tab to the front: selects it in the main window, or shows its own window. Everything that switches to
    /// a tab goes through here.
    /// </summary>
    /// <param name="tab">The tab.</param>
    /// <param name="activate">False to leave the window in use as it is, such as while files are dragged over the app.</param>
    public void ShowTab(TabItem tab, bool activate = true)
    {
        if (poppedOut.TryGetValue(tab, out PopOutWindow? window))
        {
            if (!activate) return;
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            return;
        }
        Tabs.SelectedItem = tab;
        if (activate && poppedOut.Count > 0 && Window.GetWindow(this) is Window main && !main.IsActive)
        {
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Activate();
        }
    }

    /// <summary>Opens a tab in a window of its own.</summary>
    public void PopOut(TabItem tab)
    {
        if (IsPoppedOut(tab) || Tour.IsRunning || AnyOverlayOpen || IsRuleOpen) return;
        object? page = tab.Content;
        string name = tab.Header as string ?? "";
        tab.Content = PlaceholderFor(tab, name);
        pagesOut[tab] = page;
        var window = new PopOutWindow(this, tab, name, page, nativeFrame: CaptionButtons.Visibility != Visibility.Visible);
        poppedOut[tab] = window;
        // the row's tab is dimmed; its page, shown elsewhere, is not a part of it
        tab.Opacity = 0.45;
        window.SetScale(Page.LayoutTransform);
        if (Tabs.SelectedItem == tab && NeighbourOf(tab) is TabItem other) Tabs.SelectedItem = other;
        window.Closed += (_, _) => Return(tab);
        window.Show();
        SessionLog.Write("Tab", name + " opened in its own window");
        if (tab == EditorTabItem) EditorTab.Refresh();
    }

    /// <summary>The nearest tab still in the main window: the next one, else the one before.</summary>
    private TabItem? NeighbourOf(TabItem tab)
    {
        List<TabItem> all = [.. Tabs.Items.OfType<TabItem>()];
        int at = all.IndexOf(tab);
        return all.Skip(at + 1).FirstOrDefault(each => !IsPoppedOut(each)) ?? all.Take(at).LastOrDefault(each => !IsPoppedOut(each));
    }

    /// <summary>What the main window shows on a tab whose page is in a window of its own.</summary>
    private FrameworkElement PlaceholderFor(TabItem tab, string name)
    {
        var show = new Button { Content = Texts.Get("Main.PopOut.Show"), Margin = new Thickness(0, 0, 8, 0) };
        show.Click += (_, _) => ShowTab(tab);
        var back = new Button { Content = Texts.Get("Main.PopOut.Return") };
        back.Click += (_, _) => { if (poppedOut.TryGetValue(tab, out PopOutWindow? window)) window.Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Children = { show, back } };
        var text = new TextBlock { Text = Texts.Get("Main.PopOut.Placeholder", ("tab", name)), HorizontalAlignment = HorizontalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { text, buttons } };
    }

    /// <summary>A tab's window closed: its page is back in the main window, shown.</summary>
    private void Return(TabItem tab)
    {
        if (!poppedOut.Remove(tab, out PopOutWindow? window)) return;
        if (ReferenceEquals(Overlays.Parent, window.OverlayHost)) HostOverlaysIn(Page);
        if (ReferenceEquals(ReadingCover.Parent, window.OverlayHost))
        {
            window.OverlayHost.Children.Remove(ReadingCover);
            Page.Children.Insert(Page.Children.IndexOf(RuleLayer) is >= 0 and int rules ? rules : Page.Children.IndexOf(Overlays), ReadingCover);
        }
        window.TakePage();
        pagesOut.Remove(tab, out object? page);
        tab.Content = page;
        tab.Opacity = 1;
        // the open rule comes back with the Mods tab
        if (tab == RulesTabItem) PlaceRuleLayer();
        if (closing) return;
        bool wasShown = Tabs.SelectedItem == tab;
        Tabs.SelectedItem = tab;
        // the main window's focus goes to the tab: left on another tab's heading, it chose that tab again when the
        // window next came into use
        tab.Focus();
        // selecting the Editor tab shows its files again; already selected, nothing told it
        if (wasShown && tab == EditorTabItem) EditorTab.Refresh();
        SessionLog.Write("Tab", (tab.Header as string ?? "") + " returned to the main window");
    }

    /// <summary>Whether the app is closing: the tabs come back without being shown.</summary>
    private bool closing;

    /// <summary>Every tab comes back into the main window: before the tutorial, which points at them there.</summary>
    public void ReturnAllTabs()
    {
        foreach (PopOutWindow window in poppedOut.Values.ToList()) window.CloseNow();
    }

    /// <summary>The app closes: the tabs' windows close with it, each keeping its place for next time.</summary>
    public void CloseTabWindows()
    {
        closing = true;
        ReturnAllTabs();
    }

    // ---- the windows, questions and notifications follow the window in use ----

    /// <summary>
    /// Whether one of the app's windows or questions is open over the pages. The notifications, the reading card and
    /// the ? popup do not count: they stop nothing.
    /// </summary>
    public bool AnyOverlayOpen => Overlays.Children.OfType<FrameworkElement>().Any(each =>
        each.Visibility == Visibility.Visible && !ReferenceEquals(each, Notifications) && !ReferenceEquals(each, ReadingCover) && !ReferenceEquals(each, HelpPopup));

    /// <summary>
    /// Whether an open overlay is one only to look at (the 3D viewer, the License and change log): the app's other
    /// windows stay in use beside it, so a tab's own window can be worked in while the main window shows a model.
    /// </summary>
    private bool IsViewer(FrameworkElement each) => ReferenceEquals(each, ModelViewer) || ReferenceEquals(each, TextWindow);

    /// <summary>Whether a window or question is open that the app's other windows wait for: any but a viewer (<see cref="IsViewer"/>).</summary>
    private bool AnyHoldingOverlayOpen => Overlays.Children.OfType<FrameworkElement>().Any(each =>
        each.Visibility == Visibility.Visible && !ReferenceEquals(each, Notifications) && !ReferenceEquals(each, ReadingCover)
        && !ReferenceEquals(each, HelpPopup) && !IsViewer(each));

    /// <summary>The window the overlays are shown in.</summary>
    private Window? OverlayWindow => Window.GetWindow(Overlays);

    /// <summary>
    /// One of the app's windows came into use. The overlays move into it, unless one of them is open in another window:
    /// then that window comes back to the front, as the owner of a question does. A viewer open in another window
    /// stays there, and this window is used beside it.
    /// </summary>
    /// <param name="host">Where that window shows the overlays: <c>Page</c> for the main window.</param>
    internal void WindowCameIntoUse(Panel host)
    {
        if (ReferenceEquals(Overlays.Parent, host)) return;
        if (AnyHoldingOverlayOpen)
        {
            Dispatcher.BeginInvoke(() => OverlayWindow?.Activate());
            return;
        }
        if (AnyOverlayOpen) return;
        HostOverlaysIn(host);
    }

    /// <summary>The main window came into use.</summary>
    public void MainWindowCameIntoUse() => WindowCameIntoUse(Page);

    /// <summary>
    /// A page's reading card is shown over the window the page is in: the main window, or the page's own. Unlike the
    /// app's windows and questions, it belongs to its page, not to the window in use.
    /// </summary>
    /// <param name="page">The page that reads.</param>
    internal void PlaceReadingCoverFor(FrameworkElement page)
    {
        Panel host = Window.GetWindow(page) is PopOutWindow own ? own.OverlayHost : Page;
        if (ReferenceEquals(ReadingCover.Parent, host)) return;
        (ReadingCover.Parent as Panel)?.Children.Remove(ReadingCover);
        // under the rule window and the app's windows and questions, when they are shown in the same window
        int overlays = host.Children.IndexOf(RuleLayer);
        if (overlays < 0) overlays = host.Children.IndexOf(Overlays);
        if (overlays >= 0) host.Children.Insert(overlays, ReadingCover);
        else host.Children.Add(ReadingCover);
    }

    private void HostOverlaysIn(Panel host)
    {
        if (ReferenceEquals(Overlays.Parent, host)) return;
        HelpPopup.Close();
        Notifications.CloseRecent();
        (Overlays.Parent as Panel)?.Children.Remove(Overlays);
        host.Children.Add(Overlays);
    }

    /// <summary>
    /// Whether a window of the app takes no clicks or keys just now: one of the app's windows or questions is open in
    /// another of them.
    /// </summary>
    internal bool IsHeldBack(Panel host) => !ReferenceEquals(Overlays.Parent, host) && AnyHoldingOverlayOpen;

    /// <summary>Clicks and keys in the main window do nothing while a question is open in a tab's window.</summary>
    private void HoldBackWhileAskedElsewhere()
    {
        void Hold(object sender, RoutedEventArgs e)
        {
            if (poppedOut.Count == 0 || !IsHeldBack(Page)) return;
            e.Handled = true;
            OverlayWindow?.Activate();
        }
        PreviewMouseDown += Hold;
        PreviewMouseUp += Hold;
        PreviewKeyDown += Hold;
    }

    /// <summary>The tab of the window in use: a tab's own window, else the tab shown in the main window.</summary>
    private TabItem? TabInUse()
    {
        foreach ((TabItem tab, PopOutWindow window) in poppedOut)
            if (window.IsActive) return tab;
        return Tabs.SelectedItem is TabItem shown && !IsPoppedOut(shown) ? shown : null;
    }

    /// <summary>"Interface size" reaches the tabs' windows too.</summary>
    private void ScaleTabWindows()
    {
        foreach (PopOutWindow window in poppedOut.Values) window.SetScale(Page.LayoutTransform);
    }

    // the handlers of the main view that a tab's window needs as well
    internal void WatchWindow(UIElement root)
    {
        root.PreviewMouseDown += BackOrForwardPressed;
        root.PreviewMouseDown += CloseNoticesOnClickElsewhere;
        root.PreviewMouseDown += CloseHelpPopupOnClickElsewhere;
        root.PreviewKeyDown += CloseHelpPopupOnEscape;
    }
}
