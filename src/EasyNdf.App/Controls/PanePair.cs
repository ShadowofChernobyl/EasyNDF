using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// A tab's main pane and its second pane (Details on the Files tab, Descriptors on the Editor tab), beside it or below
/// it as <see cref="PaneArrangement"/> decides, with a bar between them to drag. The second pane has a heading that
/// folds it away and holds ▥ and ▤, which put it beside or below. Its size beside and its size below are kept apart,
/// and with whether it is folded, between sessions.
/// </summary>
public sealed class PanePair : Grid
{
    /// <summary>The smallest the second pane can be made: beside, and below.</summary>
    private const double SmallestBeside = 260, SmallestBelow = 120;

    /// <summary>The room the main pane always keeps: beside, and below.</summary>
    private const double MainKeepsBeside = 320, MainKeepsBelow = 160;

    private readonly SplitBar bar = new();

    /// <summary>The bar between the panes.</summary>
    internal FrameworkElement Bar => bar;
    private readonly DockPanel second = new();
    private readonly Bevel heading;
    private readonly TextBlock arrow = new() { Width = 10, TextAlignment = TextAlignment.Center, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { Margin = new Thickness(6, 0, 0, 0) };
    private readonly ToggleButton besideButton, belowButton;
    private readonly ContentControl body = new() { Focusable = false };
    private UIElement? main;
    private bool isFolded;

    /// <summary>Builds the pair; the tab gives it its panes.</summary>
    public PanePair()
    {
        besideButton = PlaceButton("beside");
        belowButton = PlaceButton("below");
        var fold = new Button { Style = (Style)Application.Current.FindResource("FoldHeading"), ToolTip = "Collapse or expand", Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { arrow, title } } };
        fold.Click += (_, _) => Fold(!isFolded);
        // 3 pixels kept under the buttons, so they do not sit on the heading's line
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3), Children = { besideButton, belowButton } };
        DockPanel.SetDock(tools, Dock.Right);
        heading = new Bevel { Style = (Style)Application.Current.FindResource("Heading"), Child = new DockPanel { LastChildFill = false, Children = { tools, fold } } };
        DockPanel.SetDock(heading, Dock.Top);
        second.Children.Add(heading);
        second.Children.Add(body);
        bar.Moved += BarMoved;
        bar.Reset += () => { SizeNow = DefaultSize; ShareNow = null; Arrange(); };
        SizeChanged += (_, _) => TakeShare();
        bar.Settled += KeepState;
        // the tabs live as long as the app, so the pair listens for as long
        PaneArrangement.Changed += Arrange;
    }

    /// <summary>The name its size and folding are kept under, such as "files".</summary>
    public string Key { get; set; } = "";

    /// <summary>Whether the second pane starts folded the first time the app runs, so a new user sees less at once.</summary>
    public bool FoldedAtFirst { get; set; }

    /// <summary>The second pane's size at first: its width beside.</summary>
    public double DefaultBeside { get; set; } = 480;

    /// <summary>The second pane's size at first: its height below.</summary>
    public double DefaultBelow { get; set; } = 280;

    /// <summary>The main pane: it takes the room the second pane leaves.</summary>
    public UIElement? Main
    {
        get => main;
        set { main = value; Arrange(); }
    }

    /// <summary>What the second pane shows under its heading.</summary>
    public UIElement? Second
    {
        get => body.Content as UIElement;
        set => body.Content = value;
    }

    /// <summary>The second pane's heading.</summary>
    public string SecondTitle
    {
        get => title.Text;
        set => title.Text = value;
    }

    /// <summary>Whether the second pane is folded away, with only its heading showing.</summary>
    public bool IsFolded => isFolded;

    private bool Beside => PaneArrangement.IsBeside;

    private double DefaultSize => Beside ? DefaultBeside : DefaultBelow;

    /// <summary>The second pane's size beside or below, as last chosen.</summary>
    private double SizeNow
    {
        get => (double?)AppData.Current.State["sizes"]?[Key + (Beside ? "Beside" : "Below")] ?? DefaultSize;
        set
        {
            if (AppData.Current.State["sizes"] is not JsonObject sizes) AppData.Current.State["sizes"] = sizes = [];
            sizes[Key + (Beside ? "Beside" : "Below")] = Math.Round(value);
        }
    }

    /// <summary>
    /// The second pane's share of the room beside or below (without the bar), from 0 to 1, so it grows and shrinks with
    /// the window; null until the pane has been laid out once at its size in pixels (<see cref="SizeNow"/>).
    /// </summary>
    private double? ShareNow
    {
        get => (double?)AppData.Current.State["sizes"]?[Key + (Beside ? "BesideShare" : "BelowShare")];
        set
        {
            if (AppData.Current.State["sizes"] is not JsonObject sizes) AppData.Current.State["sizes"] = sizes = [];
            string key = Key + (Beside ? "BesideShare" : "BelowShare");
            if (value is double share) sizes[key] = Math.Round(share, 4);
            else sizes.Remove(key);
        }
    }

    /// <summary>
    /// The room the two panes share beside or below: the whole width or height, less the bar's 9 pixels (a constant, as
    /// the bar's own size is still the other way round just after the panes change from below to beside).
    /// </summary>
    private double Room => (Beside ? ActualWidth : ActualHeight) - 9;

    /// <summary>
    /// Turns the size in pixels (the default, or one kept by an older version) into a share of the room, once the panes
    /// have room and the second one is open, so from then on they keep their proportions as the window is resized.
    /// </summary>
    private void TakeShare()
    {
        if (!GrowsWithWindow || isFolded || ShareNow is not null || main is null || Room <= 0) return;
        double second = Math.Max(Beside ? SmallestBeside : SmallestBelow, SizeNow);
        ShareNow = Math.Clamp(second / Room, 0.05, 0.95);
        ShowShare();
    }

    /// <summary>
    /// The "Panes grow with the window" setting ("panesScale", on by default): on, the second pane keeps its share of the
    /// room as the window is resized; off, it keeps the size it was dragged to, and only the main pane grows or shrinks.
    /// </summary>
    private static bool GrowsWithWindow => AppData.Current.Settings["panesScale"]?.GetValue<bool>() != false;

    /// <summary>Sizes the two panes by their shares, or the second one by its size in pixels; each keeps its least size.</summary>
    private void ShowShare()
    {
        if (!GrowsWithWindow)
        {
            var fixedSize = new GridLength(Math.Max(Beside ? SmallestBeside : SmallestBelow, SizeNow));
            if (Beside && ColumnDefinitions.Count > 2) { ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star); ColumnDefinitions[2].Width = fixedSize; }
            else if (!Beside && RowDefinitions.Count > 2) { RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star); RowDefinitions[2].Height = fixedSize; }
            return;
        }
        if (ShareNow is not double share) return;
        var mainSize = new GridLength(1 - share, GridUnitType.Star);
        var secondSize = new GridLength(share, GridUnitType.Star);
        if (Beside && ColumnDefinitions.Count > 2) { ColumnDefinitions[0].Width = mainSize; ColumnDefinitions[2].Width = secondSize; }
        else if (!Beside && RowDefinitions.Count > 2) { RowDefinitions[0].Height = mainSize; RowDefinitions[2].Height = secondSize; }
    }

    /// <summary>Folds the second pane away or back, and keeps that for next time.</summary>
    public void Fold(bool folded)
    {
        isFolded = folded;
        if (AppData.Current.State["folds"] is not JsonObject folds) AppData.Current.State["folds"] = folds = [];
        // kept either way, so a pane that starts folded stays open once opened
        folds[Key] = folded;
        AppData.Current.SaveState();
        Arrange();
    }

    /// <summary>Lays the panes out beside or below, folded or not.</summary>
    private void Arrange()
    {
        isFolded = AppData.Current.State["folds"]?[Key]?.GetValue<bool>() ?? FoldedAtFirst;
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        if (main is null) return;
        bool beside = Beside;
        // the main pane, the bar, the second pane: across, or down
        GridLength secondSize = isFolded ? GridLength.Auto : new GridLength(Math.Max(beside ? SmallestBeside : SmallestBelow, SizeNow));
        foreach (GridLength size in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, secondSize })
        {
            if (beside) ColumnDefinitions.Add(new ColumnDefinition { Width = size });
            else RowDefinitions.Add(new RowDefinition { Height = size });
        }
        if (beside) ColumnDefinitions[0].MinWidth = MainKeepsBeside;
        else RowDefinitions[0].MinHeight = MainKeepsBelow;
        if (!isFolded)
        {
            if (beside) ColumnDefinitions[2].MinWidth = SmallestBeside;
            else RowDefinitions[2].MinHeight = SmallestBelow;
            TakeShare();
            ShowShare();
        }
        Put(main, 0, beside);
        bar.LiesFlat = !beside;
        bar.ToolTip = "Drag to resize. Double-click to reset.";
        bar.Visibility = isFolded ? Visibility.Collapsed : Visibility.Visible;
        Put(bar, 1, beside);
        Put(second, 2, beside);
        body.Visibility = isFolded ? Visibility.Collapsed : Visibility.Visible;
        // folded beside, the heading stands on its side down the window's edge
        heading.LayoutTransform = isFolded && beside ? new RotateTransform(90) : Transform.Identity;
        arrow.Text = isFolded ? (beside ? "◂" : "▸") : "▾";
        // pressed only while chosen by hand; while the window's size decides, neither is
        bool byHand = !PaneArrangement.IsAutomatic;
        besideButton.IsChecked = byHand && beside;
        belowButton.IsChecked = byHand && !beside;
    }

    private void Put(UIElement element, int place, bool beside)
    {
        SetRow(element, beside ? 0 : place);
        SetColumn(element, beside ? place : 0);
        Children.Add(element);
    }

    /// <summary>The bar is dragged: the second pane takes the room past the mouse, within its limits.</summary>
    private void BarMoved(double at)
    {
        bool beside = Beside;
        double total = beside ? ActualWidth : ActualHeight;
        double wanted = total - at - (beside ? bar.ActualWidth : bar.ActualHeight) / 2;
        double largest = total - (beside ? MainKeepsBeside : MainKeepsBelow) - 9;
        SizeNow = Math.Clamp(wanted, beside ? SmallestBeside : SmallestBelow, Math.Max(beside ? SmallestBeside : SmallestBelow, largest));
        if (Room > 0) ShareNow = Math.Clamp(SizeNow / Room, 0.05, 0.95);
        ShowShare();
    }

    private static void KeepState() => AppData.Current.SaveState();

    /// <summary>
    /// ▥ or ▤ (drawn by <see cref="PaneIcon"/>): puts the second panes beside or below by hand. Clicking the one already pressed lets the window's size
    /// decide again.
    /// </summary>
    private static ToggleButton PlaceButton(string place)
    {
        var button = new ToggleButton
        {
            Style = (Style)Application.Current.FindResource("PressedToggle"),
            Content = new PaneIcon(place == "beside"),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = place == "beside" ? "Put this pane beside. Click again to arrange automatically" : "Put this pane below. Click again to arrange automatically",
        };
        button.Click += (_, _) =>
        {
            bool wasChosen = !PaneArrangement.IsAutomatic && PaneArrangement.IsBeside == (place == "beside");
            if (wasChosen) PaneArrangement.ChooseAutomatic();
            else PaneArrangement.Choose(place == "beside");
        };
        return button;
    }
}
