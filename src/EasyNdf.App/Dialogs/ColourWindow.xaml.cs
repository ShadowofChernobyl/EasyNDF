using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyNdf.App.Controls;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The Theme Designer's colour window: Windows' colour window, drawn in the theme. While a colour is being picked, each
/// change is shown at once (the theme follows the mouse); OK, Enter or a click outside keeps it, and Cancel or Escape
/// puts the old colour back. The custom colours are kept between sessions.
/// </summary>
public partial class ColourWindow : UserControl
{
    /// <summary>Windows' 48 basic colours, row by row, as its colour window shows them.</summary>
    private static readonly string[] BasicColourList =
    [
        "#ff8080", "#ffff80", "#80ff80", "#00ff80", "#80ffff", "#0080ff", "#ff80c0", "#ff80ff",
        "#ff0000", "#ffff00", "#80ff00", "#00ff40", "#00ffff", "#0080c0", "#8080c0", "#ff00ff",
        "#804040", "#ff8040", "#00ff00", "#008080", "#004080", "#8080ff", "#800040", "#ff0080",
        "#800000", "#ff8000", "#008000", "#008040", "#0000ff", "#0000a0", "#800080", "#8000ff",
        "#400000", "#804000", "#004000", "#004040", "#000080", "#000040", "#400040", "#400080",
        "#000000", "#808000", "#808040", "#808080", "#408080", "#c0c0c0", "#400040", "#ffffff",
    ];

    /// <summary>How many custom colours there are, as in Windows.</summary>
    private const int CustomCount = 16;

    /// <summary>Windows shows hue, saturation and lightness from 0 to 240.</summary>
    private const double WindowsScale = 240;

    private double hue, saturation, lightness;
    private Color colour;
    private string before = "";
    private Action<string>? showColour;
    private TaskCompletionSource<string?>? answer;
    private bool isDragging, isFillingBoxes, isPreviewDue;
    private int nextCustom;

    /// <summary>Builds the window, closed.</summary>
    public ColourWindow()
    {
        InitializeComponent();
        foreach (string basic in BasicColourList) BasicColours.Children.Add(SwatchOf(basic));
        ShowCustomColours();
        foreach (TextBox box in new[] { HueBox, SatBox, LumBox })
            WatchBox(box, TakeHslBoxes);
        foreach (TextBox box in new[] { RedBox, GreenBox, BlueBox })
            WatchBox(box, TakeRgbBoxes);
        WatchBox(HexBox, TakeHexBox);
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>
    /// Opens the window on a colour. <paramref name="show"/> is called with each colour as it changes, so the theme can
    /// follow; it is called with the old colour again if the user cancels. Gives back the colour kept, or null.
    /// </summary>
    /// <param name="besideAt">
    /// Where the window's right edge goes, at the height of its middle, in this window's own coordinates (the Theme
    /// Designer passes the preview's left edge, so the preview stays in view); null to open in the middle.
    /// </param>
    public Task<string?> Pick(string title, string start, Action<string> show, Point? besideAt = null)
    {
        Title.Text = title;
        before = start;
        showColour = show;
        BeforeSwatch.Background = BrushOf(start);
        SetFromRgb(ColourOf(start), tell: false);
        Visibility = Visibility.Visible;
        PlaceBox(besideAt);
        answer = new TaskCompletionSource<string?>();
        Dispatcher.BeginInvoke(() => HexBox.Focus());
        return answer.Task;
    }

    // ---- where the window is ----

    /// <summary>Puts the window with its right edge at a point, or in the middle; always whole inside the app's window.</summary>
    private void PlaceBox(Point? besideAt)
    {
        Box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = Box.DesiredSize;
        Size room = Room;
        double left = besideAt is Point at ? at.X - size.Width : (room.Width - size.Width) / 2;
        double top = besideAt is Point middle ? middle.Y - size.Height / 2 : (room.Height - size.Height) / 2;
        MoveBox(left, top);
    }

    /// <summary>The room the window has: the main view's size, as its own is not known until it has been shown.</summary>
    private Size Room => VisualTreeHelper.GetParent(this) is FrameworkElement holder ? new Size(holder.ActualWidth, holder.ActualHeight) : new Size(ActualWidth, ActualHeight);

    /// <summary>Moves the window, keeping it 8 pixels inside the app's window.</summary>
    private void MoveBox(double left, double top)
    {
        Size size = Box.DesiredSize, room = Room;
        left = Math.Max(8, Math.Min(left, room.Width - size.Width - 8));
        top = Math.Max(8, Math.Min(top, room.Height - size.Height - 8));
        Box.Margin = new Thickness(left, top, 0, 0);
    }

    private Point dragStart;

    private void TitlePressed(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindButton(source)) return;
        dragStart = e.GetPosition(Box);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Whether a click was on a button (the ×), which is not a drag.</summary>
    private static bool FindButton(DependencyObject source)
    {
        for (DependencyObject? at = source; at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    private void TitleDragged(object sender, MouseEventArgs e)
    {
        if (!((UIElement)sender).IsMouseCaptured) return;
        Point at = e.GetPosition(this);
        MoveBox(at.X - dragStart.X, at.Y - dragStart.Y);
    }

    private void TitleReleased(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    // ---- the colour, kept as hue, saturation and lightness ----

    private void SetFromHsl(double newHue, double newSaturation, double newLightness, bool tell = true)
    {
        hue = (newHue % 360 + 360) % 360;
        saturation = Math.Clamp(newSaturation, 0, 1);
        lightness = Math.Clamp(newLightness, 0, 1);
        colour = RgbOf(hue, saturation, lightness);
        ShowAll(tell);
    }

    private void SetFromRgb(Color newColour, bool tell = true)
    {
        colour = newColour;
        (double newHue, double newSaturation, double newLightness) = HslOf(newColour);
        // a grey has no hue of its own: the hue chosen before stays, so the spectrum's mark does not jump to red
        if (newSaturation > 0) hue = newHue;
        saturation = newSaturation;
        lightness = newLightness;
        ShowAll(tell);
    }

    /// <summary>Shows the colour everywhere in the window and, when asked, in the theme.</summary>
    private void ShowAll(bool tell)
    {
        NowSwatch.Background = new SolidColorBrush(colour);
        double width = Spectrum.Width, height = Spectrum.Height;
        Place(SpectrumMark, hue / 360 * width, (1 - saturation) * height);
        Place(SpectrumMarkEdge, hue / 360 * width, (1 - saturation) * height);
        Canvas.SetTop(LightnessMark, (1 - lightness) * LightnessStrip.Height - 6);
        LightnessStrip.Fill = new LinearGradientBrush(
            new GradientStopCollection { new(Colors.White, 0), new(RgbOf(hue, saturation, 0.5), 0.5), new(Colors.Black, 1) }, new Point(0, 0), new Point(0, 1));
        FillBoxes();
        if (tell) PreviewSoon();
    }

    private static void Place(FrameworkElement mark, double x, double y)
    {
        Canvas.SetLeft(mark, x - mark.Width / 2);
        Canvas.SetTop(mark, y - mark.Height / 2);
    }

    /// <summary>
    /// The theme follows the colour, but at most once per frame: the mouse moves faster than the whole window can be
    /// drawn again, so the latest colour waits for the next one.
    /// </summary>
    private void PreviewSoon()
    {
        if (isPreviewDue) return;
        isPreviewDue = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            isPreviewDue = false;
            if (IsOpen) showColour?.Invoke(HexOf(colour));
        });
    }

    // ---- the spectrum and the lightness strip ----

    private void SpectrumPressed(object sender, MouseButtonEventArgs e)
    {
        isDragging = Spectrum.CaptureMouse();
        TakeSpectrum(e.GetPosition(Spectrum));
    }

    private void SpectrumDragged(object sender, MouseEventArgs e)
    {
        if (isDragging) TakeSpectrum(e.GetPosition(Spectrum));
    }

    /// <summary>A point on the spectrum: hue across, saturation down; a lightness of 0 or 1 would hide both, so it goes to the middle.</summary>
    private void TakeSpectrum(Point at)
    {
        double newLightness = lightness is <= 0 or >= 1 ? 0.5 : lightness;
        SetFromHsl(Math.Clamp(at.X / Spectrum.Width, 0, 0.9999) * 360, 1 - Math.Clamp(at.Y / Spectrum.Height, 0, 1), newLightness);
    }

    private void LightnessPressed(object sender, MouseButtonEventArgs e)
    {
        isDragging = LightnessStrip.CaptureMouse();
        TakeLightness(e.GetPosition(LightnessStrip));
    }

    private void LightnessDragged(object sender, MouseEventArgs e)
    {
        if (isDragging) TakeLightness(e.GetPosition(LightnessStrip));
    }

    private void TakeLightness(Point at) => SetFromHsl(hue, saturation, 1 - Math.Clamp(at.Y / LightnessStrip.Height, 0, 1));

    private void DragEnded(object sender, MouseButtonEventArgs e)
    {
        isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    // ---- the number boxes ----

    private void FillBoxes()
    {
        isFillingBoxes = true;
        Fill(HueBox, Whole(hue / 360 * WindowsScale));
        Fill(SatBox, Whole(saturation * WindowsScale));
        Fill(LumBox, Whole(lightness * WindowsScale));
        Fill(RedBox, colour.R.ToString(CultureInfo.InvariantCulture));
        Fill(GreenBox, colour.G.ToString(CultureInfo.InvariantCulture));
        Fill(BlueBox, colour.B.ToString(CultureInfo.InvariantCulture));
        Fill(HexBox, HexOf(colour));
        isFillingBoxes = false;
    }

    /// <summary>A box shows a number, unless it is the one being typed in: that would move the cursor under the typing.</summary>
    private static void Fill(TextBox box, string text)
    {
        if (!box.IsKeyboardFocused) box.Text = text;
    }

    private static string Whole(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);

    /// <summary>A box's number is taken as it is typed; a box left with something that is not a number shows the colour's again.</summary>
    private void WatchBox(TextBox box, Action take)
    {
        box.TextChanged += (_, _) => { if (!isFillingBoxes) take(); };
        box.LostKeyboardFocus += (_, _) => FillBoxes();
    }

    private void TakeHslBoxes()
    {
        if (Number(HueBox, WindowsScale) is not double h || Number(SatBox, WindowsScale) is not double s || Number(LumBox, WindowsScale) is not double l) return;
        SetFromHsl(h / WindowsScale * 360, s / WindowsScale, l / WindowsScale);
    }

    private void TakeRgbBoxes()
    {
        if (Number(RedBox, 255) is not double r || Number(GreenBox, 255) is not double g || Number(BlueBox, 255) is not double b) return;
        SetFromRgb(Color.FromRgb((byte)r, (byte)g, (byte)b));
    }

    private void TakeHexBox()
    {
        string valid = EasyNdf.Data.Themes.ThemeColors.ValidColor(HexBox.Text.Trim());
        if (valid != "" && HexBox.Text.Trim().Length == 7) SetFromRgb(ColourOf(valid));
    }

    /// <summary>A box's whole number from 0 to <paramref name="largest"/>; null while it is anything else.</summary>
    private static double? Number(TextBox box, double largest) =>
        int.TryParse(box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value <= largest ? value : null;

    // ---- the basic and custom colours ----

    /// <summary>A colour box in the grids on the left; a click takes its colour.</summary>
    private Bevel SwatchOf(string hex)
    {
        var swatch = new Bevel
        {
            Width = 22, Height = 18, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(2), Cursor = Cursors.Hand, ToolTip = hex,
            Child = new Border { Background = BrushOf(hex) },
        };
        swatch.SetResourceReference(Bevel.TopLeftProperty, "lo");
        swatch.SetResourceReference(Bevel.BottomRightProperty, "hi");
        swatch.SetResourceReference(Bevel.BackgroundProperty, "well");
        swatch.MouseLeftButtonUp += (_, _) => SetFromRgb(ColourOf(hex));
        return swatch;
    }

    /// <summary>The custom colours as kept (state "customColours"); black where none was added yet, as in Windows.</summary>
    private static List<string> KeptCustomColours()
    {
        var kept = (AppData.Current.State["customColours"] as JsonArray)?.Select(c => (string?)c ?? "#000000").ToList() ?? [];
        while (kept.Count < CustomCount) kept.Add("#000000");
        return kept.Take(CustomCount).ToList();
    }

    private void ShowCustomColours()
    {
        CustomColours.Children.Clear();
        foreach (string custom in KeptCustomColours()) CustomColours.Children.Add(SwatchOf(custom));
    }

    /// <summary>"Add to Custom Colors": the colour goes into the next custom box, round from the first again after the last.</summary>
    private void AddCustomClicked(object sender, RoutedEventArgs e)
    {
        List<string> kept = KeptCustomColours();
        kept[nextCustom] = HexOf(colour);
        nextCustom = (nextCustom + 1) % CustomCount;
        AppData.Current.State["customColours"] = new JsonArray([.. kept.Select(c => (JsonNode)c)]);
        AppData.Current.SaveState();
        ShowCustomColours();
    }

    // ---- closing ----

    private void OkClicked(object sender, RoutedEventArgs e) => Close(keep: true);

    private void CancelClicked(object sender, RoutedEventArgs e) => Close(keep: false);

    private void Close(bool keep)
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        if (!keep) showColour?.Invoke(before);
        answer?.TrySetResult(keep ? HexOf(colour) : null);
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(keep: false); }
        else if (e.Key == Key.Enter) { e.Handled = true; Close(keep: true); }
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close(keep: true);
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // ---- colour arithmetic ----

    private static Color ColourOf(string hex)
    {
        int[] rgb = EasyNdf.Data.Themes.ThemeColors.Channels(hex);
        return Color.FromRgb((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]);
    }

    private static SolidColorBrush BrushOf(string hex) => new(ColourOf(hex));

    private static string HexOf(Color c) => "#" + c.R.ToString("x2", CultureInfo.InvariantCulture) + c.G.ToString("x2", CultureInfo.InvariantCulture) + c.B.ToString("x2", CultureInfo.InvariantCulture);

    /// <summary>A colour from its hue (0 to 360), saturation and lightness (0 to 1).</summary>
    private static Color RgbOf(double h, double s, double l)
    {
        double chroma = (1 - Math.Abs(2 * l - 1)) * s;
        double part = chroma * (1 - Math.Abs(h / 60 % 2 - 1));
        double lowest = l - chroma / 2;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (chroma, part, 0.0),
            1 => (part, chroma, 0.0),
            2 => (0.0, chroma, part),
            3 => (0.0, part, chroma),
            4 => (part, 0.0, chroma),
            _ => (chroma, 0.0, part),
        };
        return Color.FromRgb(ToByte(r + lowest), ToByte(g + lowest), ToByte(b + lowest));
    }

    private static byte ToByte(double share) => (byte)Math.Round(Math.Clamp(share, 0, 1) * 255);

    /// <summary>A colour's hue (0 to 360), saturation and lightness (0 to 1).</summary>
    private static (double Hue, double Saturation, double Lightness) HslOf(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double highest = Math.Max(r, Math.Max(g, b)), lowest = Math.Min(r, Math.Min(g, b)), spread = highest - lowest;
        double l = (highest + lowest) / 2;
        if (spread == 0) return (0, 0, l);
        double s = spread / (1 - Math.Abs(2 * l - 1));
        double h = highest == r ? (g - b) / spread % 6 : highest == g ? (b - r) / spread + 2 : (r - g) / spread + 4;
        return ((h * 60 + 360) % 360, s, l);
    }
}
