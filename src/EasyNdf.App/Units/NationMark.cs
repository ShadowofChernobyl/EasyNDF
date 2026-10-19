using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EasyNdf.App.Controls;

namespace EasyNdf.App.Units;

/// <summary>
/// A nation's mark: its flag in a small sunken frame, 16 pixels high. Where the game has no flag for it, its code
/// ("US", "SOV") in the same frame, bold, with a stripe of its side's colour at the left. The tooltip is the nation's name.
/// </summary>
public sealed class NationMark : Grid
{
    private readonly Bevel frame = new() { Height = 16, MinWidth = 24, Padding = new Thickness(1) };
    private readonly Image flag = new() { Width = 21, Height = 12, Stretch = Stretch.UniformToFill };
    private readonly TextBlock code = new() { FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 12, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly Rectangle sideStripe = new() { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1) };
    private string shownNation = "";

    /// <summary>Builds the mark for a nation, on a side (true for the game's first side: NATO, or the Allies).</summary>
    public NationMark(string nation, bool firstSide)
    {
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        frame.SetResourceReference(Bevel.TopLeftProperty, "lo");
        frame.SetResourceReference(Bevel.BottomRightProperty, "hi");
        frame.SetResourceReference(Bevel.BackgroundProperty, "well");
        code.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        frame.Child = new Grid { Children = { flag, code } };
        Children.Add(frame);
        Children.Add(sideStripe);
        Show(nation, firstSide);
    }

    /// <summary>Shows another nation, on a side: a card reused for another unit shows that unit's.</summary>
    public void Show(string nation, bool firstSide)
    {
        sideStripe.SetResourceReference(Shape.FillProperty, firstSide ? "side-a" : "side-b");
        ShowNation(nation);
    }

    /// <summary>Shows a nation: the code at once, the flag when it has been read.</summary>
    private async void ShowNation(string nation)
    {
        shownNation = nation;
        code.Text = nation;
        ToolTip = UnitNames.Nation(nation);
        ShowCode(true);
        if (UnitPictureLibrary.Current is not { } library || library.FlagSourceOf(nation) is not string source) return;
        ImageSource? picture = library.TryGetKept(source, flag.Width, out ImageSource? kept) ? kept : await library.GetAsync(source, flag.Width);
        if (picture is null || shownNation != nation) return;
        flag.Source = picture;
        ShowCode(false);
    }

    /// <summary>The code and its side stripe, or the flag.</summary>
    private void ShowCode(bool showCode)
    {
        code.Visibility = showCode ? Visibility.Visible : Visibility.Collapsed;
        sideStripe.Visibility = code.Visibility;
        flag.Visibility = showCode ? Visibility.Collapsed : Visibility.Visible;
        // the code leaves room for the stripe
        frame.Padding = showCode ? new Thickness(4, 1, 1, 1) : new Thickness(1);
    }
}
