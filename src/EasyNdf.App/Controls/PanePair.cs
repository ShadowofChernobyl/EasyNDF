using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EasyNdf.App.Text;

namespace EasyNdf.App.Controls;

/// <summary>
/// A tab's main pane and its second pane (Details on the Files tab, Descriptors on the Editor tab), beside it or below
/// it as <see cref="PaneArrangement"/> decides, with a bar between them to drag. The second pane has a heading that
/// folds it away and holds ▥ and ▤, which put it beside or below. Folded below, its heading stays as a line along the
/// bottom; folded beside, it leaves the main pane the whole width and a button with its name, with ▥ and ▤, waits at
/// the right of the main pane's heading (<see cref="FoldedPlace"/>). Its size beside and its size below are kept
/// apart, and with whether it is folded, between sessions.
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
    private readonly DockPanel headingRow;

    /// <summary>▥ and ▤: in the second pane's heading, or with the button in the main pane's heading when folded beside.</summary>
    private readonly StackPanel tools;

    /// <summary>Folded beside: the button in the main pane's heading that opens the pane again, with its icon and name.</summary>
    private readonly Button openButton = new();
    private readonly System.Windows.Shapes.Path openIcon = new() { Width = 14, Height = 14, StrokeThickness = 1.3, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock openName = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// Folded beside: the open button and the ▥ ▤ buttons together in the main pane's heading. Kept, not made anew,
    /// because a button can only be in one panel at a time.
    /// </summary>
    private readonly StackPanel foldedRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// Beside, in the main pane's heading: an empty element as tall as the second pane's heading row, so the main
    /// heading is at least that tall and the two headings' lines meet.
    /// </summary>
    private readonly FrameworkElement headingSpacer = new() { Width = 0 };
    /// <summary>The pane's own heading: a button with the pane's icon, name and an arrow, open or folded.</summary>
    private readonly System.Windows.Shapes.Path foldIcon = new() { Width = 14, Height = 14, StrokeThickness = 1.3, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock arrow = new() { Margin = new Thickness(5, 0, 0, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton besideButton, belowButton;
    private readonly ContentControl body = new() { Focusable = false };
    private UIElement? main;
    private bool isFolded;

    /// <summary>Builds the pair; the tab gives it its panes.</summary>
    public PanePair()
    {
        besideButton = PlaceButton("beside");
        belowButton = PlaceButton("below");
        // the same button look as the one that opens the pane when folded beside, so the heading never changes style
        var fold = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 3), ToolTip = Texts.Get("Common.Pane.Fold.Tip"), Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { foldIcon, title, arrow } } };
        fold.Click += (_, _) => Fold(!isFolded);
        // 3 pixels kept under the buttons, so they do not sit on the heading's line
        tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3), Children = { besideButton, belowButton } };
        DockPanel.SetDock(tools, Dock.Right);
        // at the bottom, so the row keeps its own height when the heading is made as tall as the main one
        headingRow = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Bottom, Children = { tools, fold } };
        headingSpacer.SetBinding(HeightProperty, new System.Windows.Data.Binding(nameof(ActualHeight)) { Source = headingRow });
        heading = new Bevel { Style = (Style)Application.Current.FindResource("Heading"), Child = headingRow };
        DockPanel.SetDock(heading, Dock.Top);
        // the name and its icon in the accent colour, as the pane's own heading is
        foreach (TextBlock name in new[] { openName, title })
        {
            name.FontWeight = FontWeights.Bold;
            name.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        }
        foreach (System.Windows.Shapes.Path icon in new[] { openIcon, foldIcon })
            icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "accent");
        openButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { openIcon, openName, new TextBlock { Text = " ◂", VerticalAlignment = VerticalAlignment.Center } } };
        openButton.Click += (_, _) => Fold(false);
        foldedRow.Children.Add(openButton);
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

    /// <summary>
    /// The place at the right of the main pane's heading where, folded beside, the button that opens the second pane
    /// and ▥ and ▤ go (the tab puts an empty ContentControl there and gives it here).
    /// </summary>
    public ContentControl? FoldedPlace
    {
        get => foldedPlace;
        set { foldedPlace = value; Arrange(); }
    }
    private ContentControl? foldedPlace;

    /// <summary>The second pane's icon, on the button that opens it when it is folded beside.</summary>
    public Geometry? FoldedIcon
    {
        get => openIcon.Data;
        set
        {
            openIcon.Data = value;
            foldIcon.Data = value;
            foldIcon.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

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
        get => secondTitle;
        set { secondTitle = value; ShowTitle(); }
    }
    private string secondTitle = "";

    /// <summary>
    /// The heading while the second pane is folded, when it should be shorter than <see cref="SecondTitle"/>: Details
    /// is just "Details" then, not the file and preset it shows. Null keeps the same heading folded or not.
    /// </summary>
    public string? FoldedTitle { get; set; }

    /// <summary>Shows the heading for the pane as it is now, folded or not.</summary>
    private void ShowTitle()
    {
        title.Text = isFolded && FoldedTitle is not null ? FoldedTitle : secondTitle;
        openName.Text = FoldedTitle ?? secondTitle;
        openButton.ToolTip = Texts.Get("Common.Pane.Open.Tip", ("name", openName.Text));
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

    /// <summary>Raised after the second pane is folded away or brought back.</summary>
    public event Action? FoldChanged;

    /// <summary>Folds the second pane away or back, and keeps that for next time.</summary>
    public void Fold(bool folded)
    {
        isFolded = folded;
        if (AppData.Current.State["folds"] is not JsonObject folds) AppData.Current.State["folds"] = folds = [];
        // kept either way, so a pane that starts folded stays open once opened
        folds[Key] = folded;
        AppData.Current.SaveState();
        Arrange();
        FoldChanged?.Invoke();
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
        // folded beside, the main pane has the whole width; the button that opens the pane, and ▥ and ▤, are in its heading
        bool inMainHeading = isFolded && beside && FoldedPlace is not null;
        PutTools(inMainHeading);
        MatchHeadings(beside && !inMainHeading);
        ShowTitle();
        ShowPlaceButtons(beside);
        if (inMainHeading) return;
        bar.LiesFlat = !beside;
        bar.ToolTip = Texts.Get("Common.ResizeGrip.Tip");
        // folded, the bar cannot be dragged but keeps its room, so the heading keeps the same gap from the main pane
        // as when the pane is open, rather than touching it
        bar.Visibility = isFolded ? Visibility.Hidden : Visibility.Visible;
        Put(bar, 1, beside);
        Put(second, 2, beside);
        body.Visibility = isFolded ? Visibility.Collapsed : Visibility.Visible;
        arrow.Text = isFolded ? "▸" : "▾";
    }

    /// <summary>
    /// Puts ▥ and ▤ in the main pane's heading, after the button that opens the pane, or back in the second pane's
    /// own heading.
    /// </summary>
    private void PutTools(bool inMainHeading)
    {
        (tools.Parent as Panel)?.Children.Remove(tools);
        if (FoldedPlace is not null) FoldedPlace.Content = null;
        if (inMainHeading)
        {
            tools.Margin = new Thickness(0);
            foldedRow.Children.Add(tools);
            FoldedPlace!.Content = foldedRow;
        }
        else
        {
            // 3 pixels kept under the buttons, so they do not sit on the heading's line
            tools.Margin = new Thickness(0, 0, 0, 3);
            headingRow.Children.Insert(0, tools);
        }
    }

    /// <summary>
    /// Beside, the two panes' headings are made the same height, whichever has the taller content, so their lines meet:
    /// the main heading holds <see cref="headingSpacer"/>, and the second heading is at least as tall as the main one.
    /// Below, each heading has its own height.
    /// </summary>
    private void MatchHeadings(bool beside)
    {
        Bevel? mainHeading = MainHeading();
        if (beside && mainHeading is not null)
        {
            FoldedPlace!.Content = headingSpacer;
            heading.SetBinding(MinHeightProperty, new System.Windows.Data.Binding(nameof(ActualHeight)) { Source = mainHeading });
        }
        else heading.ClearValue(MinHeightProperty);
    }

    /// <summary>The main pane's heading: the one <see cref="FoldedPlace"/> sits in.</summary>
    private Bevel? MainHeading()
    {
        for (DependencyObject? at = FoldedPlace; at is not null; at = LogicalTreeHelper.GetParent(at))
            if (at is Bevel bevel) return bevel;
        return null;
    }

    /// <summary>▥ and ▤ pressed only while chosen by hand; while the window's size decides, neither is.</summary>
    private void ShowPlaceButtons(bool beside)
    {
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
            ToolTip = place == "beside" ? Texts.Get("Common.Pane.Beside.Tip") : Texts.Get("Common.Pane.Below.Tip"),
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
