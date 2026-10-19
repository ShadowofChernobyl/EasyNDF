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
        if (UnitPictureLibrary.Current is not { } library) return;
        if (library.FlagSourceOf(nation) is not string source)
        {
            ShowDrawnFlag(nation);
            return;
        }
        ImageSource? picture = library.TryGetKept(source, flag.Width, out ImageSource? kept) ? kept : await library.GetAsync(source, flag.Width);
        // the card may show another nation by now, as the list's rows are reused while it scrolls
        if (shownNation != nation) return;
        if (picture is null)
        {
            ShowDrawnFlag(nation);
            return;
        }
        flag.Source = picture;
        ShowCode(false);
    }

    /// <summary>
    /// The flag of a nation whose flag picture the game lacks or the app cannot read, drawn here: Poland in Steel
    /// Division 2, whose only flag is an animation the app cannot read. Other nations keep their code.
    /// </summary>
    private void ShowDrawnFlag(string nation)
    {
        if (!string.Equals(nation, "POL", StringComparison.OrdinalIgnoreCase)) return;
        flag.Source = PolishFlag;
        ShowCode(false);
    }

    /// <summary>Poland's flag: white over red, in the flag's official red.</summary>
    private static readonly ImageSource PolishFlag = DrawPolishFlag();

    private static DrawingImage DrawPolishFlag()
    {
        var white = new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 2.5)));
        var red = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xDC, 0x14, 0x3C)), null, new RectangleGeometry(new Rect(0, 2.5, 8, 2.5)));
        var picture = new DrawingImage(new DrawingGroup { Children = { white, red } });
        picture.Freeze();
        return picture;
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
