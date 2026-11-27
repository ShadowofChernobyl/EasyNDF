using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EasyNdf.App.Text;

namespace EasyNdf.App.Windowing;

/// <summary>A tab in a window of its own, holding the tab's page until it closes (see MainView.PopOut.cs).</summary>
public partial class PopOutWindow : Window
{
    /// <summary>Opens nothing yet: the caller shows the window.</summary>
    /// <param name="view">The main view the tab belongs to.</param>
    /// <param name="tab">The tab.</param>
    /// <param name="name">The tab's name, for the title.</param>
    /// <param name="page">The tab's page.</param>
    /// <param name="nativeFrame">Whether the main window has Windows' own title bar; this one then has it too.</param>
    public PopOutWindow(MainView view, TabItem tab, string name, object? page, bool nativeFrame)
    {
        InitializeComponent();
        View = view;
        placeKey = "window" + tab.Name;
        ShowName(name);
        // another language: the tab's name follows (its heading in the tab row gets it first, so it is read a moment later)
        void NewLanguage() => Dispatcher.BeginInvoke(() => ShowName(tab.Header as string ?? name));
        Texts.LanguageChanged += NewLanguage;
        Closed += (_, _) => Texts.LanguageChanged -= NewLanguage;
        PageHost.Content = page;
        // every key not taken by the control that has the focus, as in the main window
        KeyDown += (_, e) => view.HandleShortcut(e, tab);
        StateChanged += (_, _) => ShowWindowState();
        // F11: full screen, or back, as in the main window
        FullScreen.Watch(this, view, ShowWindowState);
        Activated += (_, _) => view.WindowCameIntoUse(Frame);
        view.WatchWindow(Root);
        // while one of the app's windows or questions is open in another window, this one takes no clicks or keys
        void Hold(object sender, RoutedEventArgs e)
        {
            if (!view.IsHeldBack(Frame)) return;
            e.Handled = true;
            GetWindow(view.Overlays)?.Activate();
        }
        Root.PreviewMouseDown += Hold;
        Root.PreviewMouseUp += Hold;
        Root.PreviewKeyDown += Hold;
        // files dropped on this window are added as on the main one
        Root.AllowDrop = true;
        Root.DragEnter += view.FilesDraggedOver;
        Root.DragOver += view.FilesDraggedOver;
        Root.DragLeave += view.FilesDragLeft;
        Root.Drop += view.FilesDropped;
        Closing += WhenClosing;
        // beside the main window's top left the first time, then where it was left
        if (GetWindow(view) is Window main && main.WindowState == WindowState.Normal)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = main.Left + 60;
            Top = main.Top + 60;
        }
        WindowPlace.Restore(this, placeKey);
        // opened maximized, as it was left: no change of state is raised, so the button is set here
        ShowWindowState();
        if (nativeFrame) CaptionButtons.Visibility = Visibility.Collapsed;
        else _ = new CustomTitleBar(this, IsTitleBarAt);
    }

    /// <summary>The main view the tab belongs to: what the page and the windows over it find as theirs.</summary>
    public MainView View { get; }

    /// <summary>Where the app's windows and questions are shown while this window is in use.</summary>
    public Panel OverlayHost => Frame;

    /// <summary>Where the app's state keeps this window's place.</summary>
    private readonly string placeKey;

    /// <summary>Whether the window closes whatever is open in it: the app is closing, or the tutorial starts.</summary>
    private bool closeNow;

    /// <summary>Closes the window at once.</summary>
    public void CloseNow()
    {
        closeNow = true;
        Close();
    }

    /// <summary>The window stays while one of the app's windows or questions is open in it: that is answered first.</summary>
    private void WhenClosing(object? sender, CancelEventArgs e)
    {
        bool ruleOpenHere = ReferenceEquals(View.RuleLayer.Parent, Frame) && View.IsRuleOpen;
        if (!closeNow && (ruleOpenHere || (ReferenceEquals(View.Overlays.Parent, Frame) && View.AnyOverlayOpen)))
        {
            e.Cancel = true;
            // asked from elsewhere (Return to Main Window), the window comes to the front, showing what waits
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            return;
        }
        WindowPlace.Keep(this, placeKey);
        AppData.Current.SaveState();
        // a window opened from this one (the Theme Designer, from Settings) stays open, now with the main window
        if (GetWindow(View) is Window main)
            foreach (Window owned in OwnedWindows.OfType<Window>().ToList()) owned.Owner = main;
    }

    /// <summary>The tab's name in the title row, the window's title and the close button's tip.</summary>
    private void ShowName(string name)
    {
        Title = "EasyNDF – " + name;
        TabName.Text = name;
        CloseButton.ToolTip = Texts.Get("Main.PopOut.Close.Tip", ("tab", name));
    }

    /// <summary>Gives the page back, for its tab.</summary>
    public void TakePage() => PageHost.Content = null;

    /// <summary>"Interface size": the same as the main window's.</summary>
    public void SetScale(Transform scale) => Frame.LayoutTransform = scale;

    private void ShowWindowState()
    {
        bool maximized = WindowState == WindowState.Maximized || FullScreen.IsOn(this);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = Texts.Get(maximized ? "Main.Restore.Tip" : "Main.Maximize.Tip");
    }

    /// <summary>Whether a point of the window is title bar that drags it: on the title row, but not on one of its buttons.</summary>
    private bool IsTitleBarAt(Point point)
    {
        double height = TitleRow.TransformToAncestor(Root).TransformBounds(new Rect(TitleRow.RenderSize)).Bottom;
        if (point.Y < 0 || point.Y >= height) return false;
        for (DependencyObject? hit = Root.InputHitTest(point) as DependencyObject; hit is not null; hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit))
            if (hit is ButtonBase) return false;
        return true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (FullScreen.IsOn(this)) { FullScreen.Leave(this); ShowWindowState(); return; }
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
