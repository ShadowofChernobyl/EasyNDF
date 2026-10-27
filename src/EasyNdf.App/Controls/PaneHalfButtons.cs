using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Text;

namespace EasyNdf.App.Controls;

/// <summary>
/// One half of a <see cref="PanePair"/>'s second pane, such as the Armory's Division pane or the File Editor's
/// Properties: what its button shows, and when it can show at all.
/// </summary>
/// <param name="Icon">The icon on its button, drawn 14 pixels square.</param>
/// <param name="Name">Its name, as its button shows it: asked for each time, so a new language shows.</param>
/// <param name="ShowChoice">The ▾ menu's choice that brings it back while the panes are folded beside.</param>
/// <param name="FoldKey">The name its folding is kept under, such as "unitsDivisionPane".</param>
public sealed record PaneHalf(Geometry Icon, Func<string> Name, Func<string> ShowChoice, string FoldKey)
{
    /// <summary>
    /// Whether the half can show now: the Armory's Division pane only with a division chosen. While one half cannot,
    /// the other always shows, as it is the only one.
    /// </summary>
    public Func<bool> IsAvailable { get; init; } = () => true;

    /// <summary>
    /// A button of the half's own at the right of its part of the heading, such as the Division pane's "+ Add
    /// Units…"; shown while the half is. None by default.
    /// </summary>
    public FrameworkElement? HeadingButton { get; init; }
}

/// <summary>
/// A button for each half of a <see cref="PanePair"/>'s second pane, in its heading in place of the single one: each
/// folds its own half away or brings it back, and folding the last half still showing folds the whole pane away, as
/// the other tabs' single button does. With both halves showing, the right button sits over its own half.
/// </summary>
public sealed class PaneHalfButtons
{
    private readonly PanePair panes;
    private readonly PaneHalf left, right;
    private readonly Func<double> leftWidth;
    private readonly Action showHalves;
    private readonly Grid buttons = new() { Margin = new Thickness(0, 0, 0, 3) };
    private readonly Button leftButton, rightButton;

    /// <summary>Whether each half was folded away by its button, kept for next time.</summary>
    private bool isLeftFolded, isRightFolded;

    /// <summary>Makes the two buttons and gives them to the panes' heading.</summary>
    /// <param name="panes">The panes whose second pane has the two halves.</param>
    /// <param name="left">The left half.</param>
    /// <param name="right">The right half.</param>
    /// <param name="leftWidth">How wide the left half is with the bar after it, so the right button sits over its half.</param>
    /// <param name="showHalves">Shows or hides the halves' columns as <see cref="LeftShows"/> and <see cref="RightShows"/> now say.</param>
    public PaneHalfButtons(PanePair panes, PaneHalf left, PaneHalf right, Func<double> leftWidth, Action showHalves)
    {
        this.panes = panes;
        this.left = left;
        this.right = right;
        this.leftWidth = leftWidth;
        this.showHalves = showHalves;
        isLeftFolded = AppData.Current.State["folds"]?[left.FoldKey]?.GetValue<bool>() ?? false;
        isRightFolded = AppData.Current.State["folds"]?[right.FoldKey]?.GetValue<bool>() ?? false;
        leftButton = HalfButton(left.Icon, isLeft: true);
        rightButton = HalfButton(right.Icon, isLeft: false);
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.Children.Add(Cell(leftButton, left.HeadingButton, column: 0));
        buttons.Children.Add(Cell(rightButton, right.HeadingButton, column: 1));
        DockPanel.SetDock(buttons, Dock.Left);
        panes.FoldChanged += Show;
        // the tabs live as long as the app, so the buttons listen for as long
        PaneArrangement.Changed += Show;
        panes.SecondHeading = buttons;
        panes.FoldedChoices = FoldedChoices;
        Show();
    }

    /// <summary>
    /// A half's part of the heading: its button at the left and its own button (when it has one) at the right, a gap
    /// before the next half's button. Side by side, they cannot cover each other when the half is narrow.
    /// </summary>
    private static DockPanel Cell(Button halfButton, FrameworkElement? ownButton, int column)
    {
        var cell = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(halfButton, Dock.Left);
        cell.Children.Add(halfButton);
        if (ownButton is not null)
        {
            ownButton.Margin = new Thickness(12, 0, 8, 0);
            DockPanel.SetDock(ownButton, Dock.Right);
            cell.Children.Add(ownButton);
        }
        Grid.SetColumn(cell, column);
        return cell;
    }

    /// <summary>Whether the left half shows, folding of the whole pane aside: only while it can.</summary>
    public bool LeftShows => left.IsAvailable() && (!isLeftFolded || !right.IsAvailable());

    /// <summary>Whether the right half shows, folding of the whole pane aside: always while the left one cannot.</summary>
    public bool RightShows => right.IsAvailable() && (!isRightFolded || !left.IsAvailable());

    /// <summary>A button with a half's icon, its name in the accent colour and an arrow, as the other tabs' pane heading.</summary>
    private Button HalfButton(Geometry icon, bool isLeft)
    {
        var drawing = new System.Windows.Shapes.Path { Data = icon, Width = 14, Height = 14, StrokeThickness = 1.3, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        drawing.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "accent");
        var name = new TextBlock { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        var arrow = new TextBlock { Margin = new Thickness(5, 0, 0, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Left, Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { drawing, name, arrow } } };
        button.Click += (_, _) => Clicked(isLeft);
        return button;
    }

    /// <summary>
    /// A half's button is clicked: with everything folded, its half alone comes back; a half showing beside the other
    /// folds away; the last half showing folds the whole pane away.
    /// </summary>
    public void Clicked(bool isLeft)
    {
        if (panes.IsFolded)
        {
            // only the half asked for comes back; the other stays folded until its own button is clicked
            KeepFolded(isLeft, false);
            KeepFolded(!isLeft, true);
            showHalves();
            panes.Fold(false);
            return;
        }
        bool shows = isLeft ? LeftShows : RightShows;
        bool otherShows = isLeft ? RightShows : LeftShows;
        if (shows && !otherShows)
        {
            panes.Fold(true);
            return;
        }
        KeepFolded(isLeft, shows);
        showHalves();
        Show();
    }

    /// <summary>The top of the ▾ menu while the panes are folded beside: each half that can show.</summary>
    private IEnumerable<MenuChoice?> FoldedChoices() =>
    [
        left.IsAvailable() ? new MenuChoice(left.ShowChoice(), () => Clicked(isLeft: true)) : null,
        right.IsAvailable() ? new MenuChoice(right.ShowChoice(), () => Clicked(isLeft: false)) : null,
    ];

    /// <summary>Keeps whether a half is folded away by its button, for next time.</summary>
    private void KeepFolded(bool isLeft, bool folded)
    {
        if (isLeft) isLeftFolded = folded;
        else isRightFolded = folded;
        if (AppData.Current.State["folds"] is not JsonObject folds) AppData.Current.State["folds"] = folds = [];
        folds[isLeft ? left.FoldKey : right.FoldKey] = folded;
        AppData.Current.SaveState();
    }

    /// <summary>
    /// The buttons' names, arrows and tooltips as the halves are now: ▾ for a half showing, ▸ for one folded; folded
    /// beside, the buttons wait in the main pane's heading with no arrow. A half that cannot show has no button.
    /// </summary>
    public void Show()
    {
        leftButton.Visibility = left.IsAvailable() ? Visibility.Visible : Visibility.Collapsed;
        rightButton.Visibility = right.IsAvailable() ? Visibility.Visible : Visibility.Collapsed;
        ShowButton(leftButton, left.Name(), LeftShows);
        ShowButton(rightButton, right.Name(), RightShows);
        // a half's own button only while its half is open
        if (left.HeadingButton is not null) left.HeadingButton.Visibility = LeftShows && !panes.IsFolded ? Visibility.Visible : Visibility.Collapsed;
        if (right.HeadingButton is not null) right.HeadingButton.Visibility = RightShows && !panes.IsFolded ? Visibility.Visible : Visibility.Collapsed;
        LineUp();
    }

    private void ShowButton(Button button, string name, bool shows)
    {
        var parts = (StackPanel)button.Content;
        ((TextBlock)parts.Children[1]).Text = name;
        bool foldedBeside = panes.IsFolded && PaneArrangement.IsBeside;
        bool open = shows && !panes.IsFolded;
        // folded beside, the buttons sit in the main pane's heading as high as its other buttons, with no arrow
        parts.Children[2].Visibility = foldedBeside ? Visibility.Collapsed : Visibility.Visible;
        if (foldedBeside) button.Padding = new Thickness(7, 1, 7, 1);
        else button.ClearValue(Control.PaddingProperty);
        ((TextBlock)parts.Children[2]).Text = open ? "▾" : "▸";
        button.ToolTip = open ? Texts.Get("Common.Pane.Fold.Tip") : Texts.Get("Common.Pane.Open.Tip", ("name", name));
    }

    /// <summary>
    /// With both halves showing, the right button starts over the right half; folded beside, the two are joined, as
    /// the view buttons; otherwise they sit side by side, 6 pixels apart. Called again when the left half's width changes.
    /// </summary>
    public void LineUp()
    {
        bool overItsHalf = !panes.IsFolded && LeftShows && RightShows;
        buttons.ColumnDefinitions[0].MinWidth = overItsHalf ? leftWidth() : 0;
        bool afterLeftButton = left.IsAvailable();
        double gap = panes.IsFolded && PaneArrangement.IsBeside ? -1 : 6;
        rightButton.Margin = new Thickness(!overItsHalf && afterLeftButton ? gap : 0, 0, 0, 0);
    }
}
