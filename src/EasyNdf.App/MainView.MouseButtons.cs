using System.Windows.Controls;
using System.Windows.Input;

namespace EasyNdf.App;

/// <summary>
/// The mouse's Back and Forward buttons (the fourth and fifth), as in a browser: on Help they go through the pages
/// shown, in the File Editor through the places left by jumps (as Alt+Left and Alt+Right), and where neither has
/// anywhere to go, through the tabs shown.
/// </summary>
public partial class MainView
{
    private readonly List<TabItem> tabsBack = [], tabsForward = [];

    /// <summary>Set while Back or Forward shows a tab, which is then not filed as a tab left.</summary>
    private bool goingThroughTabs;

    private void WatchMouseButtons()
    {
        PreviewMouseDown += BackOrForwardPressed;
        Tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, Tabs) || goingThroughTabs) return;
            if (e.RemovedItems.Count == 0 || e.RemovedItems[0] is not TabItem left) return;
            tabsBack.Add(left);
            if (tabsBack.Count > 50) tabsBack.RemoveAt(0);
            tabsForward.Clear();
        };
    }

    /// <summary>A press of the mouse's Back or Forward button, in the main window or a tab's own.</summary>
    internal void BackOrForwardPressed(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2)) return;
        // not under one of the app's windows or questions, nor while the tutorial leads
        if (AnyOverlayOpen || IsRuleOpenInWindowInUse || Tour.IsRunning) return;
        bool back = e.ChangedButton == MouseButton.XButton1;
        TabItem? on = TabInUse();
        if (on == HelpTabItem && HelpTab.GoBackOrForward(back)) { e.Handled = true; return; }
        if (on == EditorTabItem && EditorTab.GoBackOrForward(back)) { e.Handled = true; return; }
        // the tabs shown before, in the main window
        if (sender != this) return;
        List<TabItem> from = back ? tabsBack : tabsForward, to = back ? tabsForward : tabsBack;
        while (from.Count > 0)
        {
            TabItem tab = from[^1];
            from.RemoveAt(from.Count - 1);
            if (IsPoppedOut(tab) || tab == Tabs.SelectedItem) continue;
            if (Tabs.SelectedItem is TabItem shown) to.Add(shown);
            goingThroughTabs = true;
            try { Tabs.SelectedItem = tab; }
            finally { goingThroughTabs = false; }
            e.Handled = true;
            return;
        }
    }
}
