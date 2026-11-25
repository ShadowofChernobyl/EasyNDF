using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Windowing;

namespace EasyNdf.App;

/// <summary>
/// The rule window and its condition or action window are a layer of their own (<c>RuleLayer</c>), over the window the
/// Mods tab is in. Unlike the app's other windows and questions, they hold back only that window: a tab in a window of
/// its own goes on working while a rule is open, so that a file or a unit can be looked at meanwhile.
/// </summary>
public partial class MainView
{
    /// <summary>Whether a rule is open in its window, or one of its conditions or actions.</summary>
    public bool IsRuleOpen => RuleWindow.IsOpen || RowWindow.IsOpen;

    /// <summary>Whether the open rule is over the window in use, where it takes the keys and stops the page under it.</summary>
    private bool IsRuleOpenInWindowInUse => IsRuleOpen && RuleLayer.IsVisible && Window.GetWindow(RuleLayer)?.IsActive == true;

    private void WatchRuleLayer() => Tabs.SelectionChanged += (_, e) =>
    {
        if (ReferenceEquals(e.OriginalSource, Tabs)) ShowRuleLayerWithItsTab();
    };

    /// <summary>The rule windows are shown over the window the Mods tab is in: the main window, or the tab's own.</summary>
    internal void PlaceRuleLayer()
    {
        Panel host = poppedOut.TryGetValue(RulesTabItem, out PopOutWindow? own) ? own.OverlayHost : Page;
        if (!ReferenceEquals(RuleLayer.Parent, host))
        {
            (RuleLayer.Parent as Panel)?.Children.Remove(RuleLayer);
            // under the app's windows and questions, when they are shown in the same window
            int overlays = host.Children.IndexOf(Overlays);
            if (overlays >= 0) host.Children.Insert(overlays, RuleLayer);
            else host.Children.Add(RuleLayer);
        }
        ShowRuleLayerWithItsTab();
    }

    /// <summary>
    /// In the main window, the open rule is shown only with the Mods tab: when another window shows another tab there
    /// (Show in File Editor from the Armory's own window), the rule waits on its tab.
    /// </summary>
    private void ShowRuleLayerWithItsTab() =>
        RuleLayer.Visibility = ReferenceEquals(RuleLayer.Parent, Page) && Tabs.SelectedItem != RulesTabItem ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Whether something that would change the rules under an open rule is refused: says so, and with
    /// <paramref name="show"/> brings the rule to the front.
    /// </summary>
    public bool RefusedForOpenRule(bool show = true)
    {
        if (!IsRuleOpen) return false;
        if (show) ShowTab(RulesTabItem);
        Notices.Say(Texts.Get("Main.RuleOpen"), NoticeKind.Caution);
        return true;
    }
}
