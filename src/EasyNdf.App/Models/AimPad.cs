using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Models;

/// <summary>
/// The viewer's aiming pad for one turret, in place of a Turn and a Gun slider: left and right turn the turret, up and
/// down raise its gun, within its limits. A click or drag says where to aim (a ring); a dot shows where it aims now, as
/// it follows at its own speed. Double-click or Home centres; the arrows move 1 degree, 10 with Shift. A turret that
/// only raises its gun gets an upright strip, one that only turns a flat one.
/// </summary>
internal sealed class AimPad : FrameworkElement
{
    /// <summary>The space between the pad's edge and the ends of its range, so the ring at an end shows whole.</summary>
    private const double Inset = 8;

    private readonly PixelSnapping snapping;
    private readonly ViewTurret turret;
    private readonly bool turns, raises;
    private readonly Action aimed;

    /// <summary>Where the pointer is over the pad, for its guide lines; null when it is not over it.</summary>
    private Point? hover;

    /// <summary>Makes the pad for a turret.</summary>
    /// <param name="turret">The turret: its limits, where it is told to aim and where it aims now.</param>
    /// <param name="turns">Whether it turns on the model (left and right on the pad).</param>
    /// <param name="raises">Whether its gun rises on the model (up and down on the pad).</param>
    /// <param name="aimed">What follows a new aim: the turret is set moving.</param>
    public AimPad(ViewTurret turret, bool turns, bool raises, Action aimed)
    {
        this.turret = turret;
        this.turns = turns;
        this.raises = raises;
        this.aimed = aimed;
        snapping = new PixelSnapping(this);
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Cross;
        if (!turns) { Width = 64; Height = 150; HorizontalAlignment = HorizontalAlignment.Center; }
        else Height = raises ? 128 : 36;
        System.Windows.Automation.AutomationProperties.SetName(this, Texts.Get("Units.Model.Pad"));
        ToolTip = Texts.Get("Units.Model.Pad.Tip");
    }

    /// <summary>Draws it again with where the turret aims now, while it moves.</summary>
    public void ShowNow() => InvalidateVisual();

    // ---- where a value sits on the pad, and back ----

    private double X(double turn) => turns ? Inset + (turn + turret.TurnLimit) / (2 * turret.TurnLimit) * (ActualWidth - 2 * Inset) : ActualWidth / 2;

    private double Y(double raise) => raises ? Inset + (turret.HighestRaise - raise) / (turret.HighestRaise - turret.LowestRaise) * (ActualHeight - 2 * Inset) : ActualHeight / 2;

    private double TurnAt(double x) => turns ? Math.Clamp((x - Inset) / (ActualWidth - 2 * Inset) * 2 * turret.TurnLimit - turret.TurnLimit, -turret.TurnLimit, turret.TurnLimit) : 0;

    private double RaiseAt(double y) => raises ? Math.Clamp(turret.HighestRaise - (y - Inset) / (ActualHeight - 2 * Inset) * (turret.HighestRaise - turret.LowestRaise), turret.LowestRaise, turret.HighestRaise) : 0;

    /// <summary>Aims the turret where the pad was pressed, to the whole degree.</summary>
    private void AimAt(Point at)
    {
        if (turns) turret.TurnTo = Math.Round(TurnAt(at.X));
        if (raises) turret.RaiseTo = Math.Round(RaiseAt(at.Y));
        Aimed();
    }

    private void Aimed()
    {
        InvalidateVisual();
        aimed();
    }

    // ---- the mouse and keys ----

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        Focus();
        if (e.ClickCount == 2)
        {
            Centre();
            return;
        }
        CaptureMouse();
        AimAt(e.GetPosition(this));
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        hover = e.GetPosition(this);
        if (IsMouseCaptured) AimAt(hover.Value);
        else InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        hover = null;
        InvalidateVisual();
    }

    /// <summary>The arrows move the aim 1 degree, 10 with Shift; Home centres it. They no longer turn the camera while the pad has the keyboard.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        if (e.Key == Key.Left && turns) turret.TurnTo = Math.Max(-turret.TurnLimit, turret.TurnTo - step);
        else if (e.Key == Key.Right && turns) turret.TurnTo = Math.Min(turret.TurnLimit, turret.TurnTo + step);
        else if (e.Key == Key.Up && raises) turret.RaiseTo = Math.Min(turret.HighestRaise, turret.RaiseTo + step);
        else if (e.Key == Key.Down && raises) turret.RaiseTo = Math.Max(turret.LowestRaise, turret.RaiseTo - step);
        else if (e.Key == Key.Home) { Centre(); e.Handled = true; return; }
        else return;
        e.Handled = true;
        Aimed();
    }

    /// <summary>Aims it straight ahead, the gun level, or as near as its limits let it.</summary>
    public void Centre()
    {
        turret.TurnTo = 0;
        turret.RaiseTo = Math.Clamp(0, turret.LowestRaise, turret.HighestRaise);
        Aimed();
    }

    /// <inheritdoc/>
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();

    /// <inheritdoc/>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();

    // ---- drawing ----

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawing)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 2 * Inset || height <= 2 * Inset) return;
        // sunken, as the app's wells: dark on its top and left, light on its bottom and right; the accent round it
        // while it has the keyboard
        Fill(drawing, "well", 0, 0, width, height);
        string top = IsKeyboardFocused ? "accent" : "lo", bottom = IsKeyboardFocused ? "accent" : "hi";
        Fill(drawing, top, 0, 0, width, 1);
        Fill(drawing, top, 0, 0, 1, height);
        Fill(drawing, bottom, 0, height - 1, width, 1);
        Fill(drawing, bottom, width - 1, 0, 1, height);

        // the grid: every 45 degrees of turn (15 or 5 on a short turn), every 5 of gun (2 on a short one); 0 brighter
        if (turns)
        {
            double step = turret.TurnLimit >= 90 ? 45 : turret.TurnLimit >= 30 ? 15 : 5;
            for (double turn = -Math.Floor(turret.TurnLimit / step) * step; turn <= turret.TurnLimit; turn += step)
                Fill(drawing, "line", X(turn), 1, 1, height - 2, turn == 0 ? 0.9 : 0.45);
        }
        if (raises)
        {
            double step = turret.HighestRaise - turret.LowestRaise > 20 ? 5 : 2;
            for (double raise = Math.Ceiling(turret.LowestRaise / step) * step; raise <= turret.HighestRaise; raise += step)
                Fill(drawing, "line", 1, Y(raise), width - 2, 1, raise == 0 ? 0.9 : 0.45);
            Label(drawing, Signed(turret.HighestRaise), width - 4, Y(turret.HighestRaise) + 1, atRight: true);
            Label(drawing, Signed(turret.LowestRaise), width - 4, Y(turret.LowestRaise) - 12, atRight: true);
        }
        if (turns) Label(drawing, "0°", X(0) + 3, height - 13, atRight: false);

        // the pointer's place, while it is over the pad
        if (hover is Point over && !IsMouseCaptured && over.X >= 0 && over.Y >= 0 && over.X <= width && over.Y <= height && TryFindResource("muted") is Brush guide)
        {
            var dashed = new Pen(guide, 1) { DashStyle = new DashStyle([2, 3], 0) };
            dashed.Freeze();
            drawing.PushOpacity(0.6);
            if (turns) drawing.DrawLine(dashed, new Point(Math.Round(over.X) + 0.5, 1), new Point(Math.Round(over.X) + 0.5, height - 1));
            if (raises) drawing.DrawLine(dashed, new Point(1, Math.Round(over.Y) + 0.5), new Point(width - 1, Math.Round(over.Y) + 0.5));
            drawing.Pop();
        }

        // where it is told to aim (the ring) and where it aims now (the dot), joined while it moves
        Point told = new(X(turret.TurnTo), Y(turret.RaiseTo)), now = new(X(turret.Turn), Y(turret.Raise));
        if (TryFindResource("accent") is Brush accent)
        {
            if ((told - now).Length > 1)
            {
                var path = new Pen(accent, 1.5);
                path.Freeze();
                drawing.PushOpacity(0.55);
                drawing.DrawLine(path, now, told);
                drawing.Pop();
            }
            var ring = new Pen(accent, 1.5);
            ring.Freeze();
            drawing.DrawEllipse(null, ring, told, 7, 7);
        }
        if (TryFindResource("ink") is Brush ink) drawing.DrawEllipse(ink, null, now, 3.6, 3.6);
    }

    /// <summary>A whole number of degrees with its sign: "+25°", "−10°", "0°".</summary>
    private static string Signed(double degrees)
    {
        long whole = (long)Math.Round(degrees);
        return (whole > 0 ? "+" : whole < 0 ? "−" : "") + Math.Abs(whole) + "°";
    }

    private void Fill(DrawingContext drawing, string colour, double left, double top, double width, double height, double opacity = 1)
    {
        if (TryFindResource(colour) is not Brush brush) return;
        if (opacity < 1) drawing.PushOpacity(opacity);
        drawing.DrawRectangle(brush, null, snapping.Snap(left, top, width, height));
        if (opacity < 1) drawing.Pop();
    }

    /// <summary>A small muted label, its left or right end at a place.</summary>
    private void Label(DrawingContext drawing, string text, double x, double y, bool atRight)
    {
        if (TryFindResource("muted") is not Brush muted) return;
        var font = (FontFamily)(TryFindResource("ui-font") ?? new FontFamily("Segoe UI"));
        var shown = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            10, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.PushOpacity(0.8);
        drawing.DrawText(shown, new Point(atRight ? x - shown.Width : x, y));
        drawing.Pop();
    }
}
