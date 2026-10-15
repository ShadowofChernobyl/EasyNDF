using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using EasyNdf.App.Text;

namespace EasyNdf.App.Controls;

/// <summary>
/// The larger box for long text (the HTML app's <c>vxPlace</c>, <c>valOpen</c> and <c>valClose</c>). A one-line text box
/// whose text is wider than the box shows a small ▾ in its bottom right corner, while the mouse is over it or it has the
/// keys; a click on it opens a larger box over the text box, with the whole text wrapped in it. Enter keeps what was
/// typed and goes back to the text box, Escape leaves the text as it was, and moving the mouse away or clicking elsewhere
/// keeps it. Line breaks typed in the larger box become spaces, as the one-line box cannot hold them.
/// </summary>
public static class LongText
{
    /// <summary>The text box the ▾ is on, and the ▾ itself.</summary>
    private static TextBox? current;
    private static OpenButton? button;

    /// <summary>The larger box while it is open, the text box it belongs to, and its text when it opened.</summary>
    private static Popup? popup;
    private static TextBox? opened;
    private static string textWhenOpened = "";
    private static readonly DispatcherTimer closing = new() { Interval = TimeSpan.FromMilliseconds(280) };

    /// <summary>Starts watching every text box of the app; called once at start-up.</summary>
    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.MouseEnterEvent, new MouseEventHandler((sender, _) => Watch((TextBox)sender)));
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((sender, _) => Watch((TextBox)sender)));
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.MouseLeaveEvent, new MouseEventHandler((sender, _) => Leave((TextBox)sender)));
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((sender, _) => Leave((TextBox)sender)));
        EventManager.RegisterClassHandler(typeof(TextBox), TextBoxBase.TextChangedEvent, new TextChangedEventHandler((sender, _) => { if (ReferenceEquals(sender, current)) Show(); }));
        EventManager.RegisterClassHandler(typeof(TextBox), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler((sender, _) => { if (ReferenceEquals(sender, current)) Show(); }));
        closing.Tick += (_, _) => Close(keep: true, back: false);
    }

    /// <summary>The mouse came over a text box, or it got the keys: the ▾ goes on it if its text does not fit.</summary>
    internal static void Watch(TextBox box)
    {
        if (box.AcceptsReturn || ReferenceEquals(box, popup?.Child)) return;
        if (!ReferenceEquals(box, current)) Hide();
        current = box;
        Show();
    }

    /// <summary>The mouse left, or the keys went elsewhere: the ▾ goes, unless the box still has the mouse or the keys.</summary>
    private static void Leave(TextBox box)
    {
        if (!ReferenceEquals(box, current)) return;
        box.Dispatcher.BeginInvoke(() =>
        {
            if (box.IsMouseOver || box.IsKeyboardFocusWithin || button?.IsMouseOver == true) return;
            Hide();
            current = null;
        });
    }

    /// <summary>Puts the ▾ on the text box when its text is wider than the box, and takes it off when it is not.</summary>
    private static void Show()
    {
        if (current is null || popup is not null) { Hide(); return; }
        bool shown = button is not null && ReferenceEquals(button.Box, current);
        // the text's width against the room it has; while the ▾ is on, the room is 17 pixels narrower, as it takes them
        double room = current.ViewportWidth, width = current.ExtentWidth;
        bool needed = room > 0 && width > room + (shown ? 0 : 1);
        if (!needed) { Hide(); return; }
        if (shown) return;
        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(current);
        if (layer is null) return;
        button = new OpenButton(current);
        layer.Add(button);
        // input.vxon: the text keeps clear of the ▾, 17 pixels from the right edge
        button.PaddingBefore = current.Padding;
        current.Padding = new Thickness(current.Padding.Left, current.Padding.Top, 17, current.Padding.Bottom);
    }

    /// <summary>Whether the ▾ is on a text box.</summary>
    internal static bool HasButton(TextBox box) => button is not null && ReferenceEquals(button.Box, box);

    private static void Hide()
    {
        if (button is null) return;
        AdornerLayer.GetAdornerLayer(button.Box)?.Remove(button);
        button.Box.Padding = button.PaddingBefore;
        button = null;
    }

    /// <summary>Opens the larger box over a text box, with the caret at the end of its text.</summary>
    private static void Open(TextBox box)
    {
        Close(keep: true, back: false);
        Hide();
        opened = box;
        textWhenOpened = box.Text;
        // as wide as the text box, or wider, up to 620 pixels within the window; tall enough for the text, from 90 to 360 pixels
        double windowWidth = Window.GetWindow(box)?.ActualWidth ?? 1280;
        Point at = box.TranslatePoint(new Point(), (UIElement?)Window.GetWindow(box) ?? box);
        double width = Math.Max(box.ActualWidth, Math.Min(620, windowWidth - 24));
        double left = Math.Max(8, Math.Min(at.X, windowWidth - width - 12));
        double height = Math.Max(90, Math.Min(360, 22 + 18 * (Math.Ceiling(box.Text.Length * 7 / width) + 2)));
        var editor = new TextBox
        {
            Text = box.Text,
            Width = width,
            Height = height,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = box.IsReadOnly,
            FontFamily = box.FontFamily,
            FontSize = box.FontSize,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.45, Color = Colors.Black },
        };
        editor.SetResourceReference(FrameworkElement.StyleProperty, "LongTextBox");
        editor.MouseLeave += (_, e) => { if (e.LeftButton == MouseButtonState.Released) closing.Start(); };
        editor.MouseEnter += (_, _) => closing.Stop();
        editor.LostKeyboardFocus += (_, _) => Close(keep: true, back: false);
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(keep: false, back: true); }
            else if (e.Key == Key.Enter) { e.Handled = true; Close(keep: true, back: true); }
        };
        popup = new Popup
        {
            Child = editor,
            PlacementTarget = box,
            Placement = PlacementMode.Relative,
            HorizontalOffset = left - at.X,
            AllowsTransparency = true,
            StaysOpen = true,
            IsOpen = true,
        };
        editor.Focus();
        editor.CaretIndex = editor.Text.Length;
    }

    /// <summary>Closes the larger box: its text goes into the text box (line breaks as spaces) unless it is left as it was.</summary>
    private static void Close(bool keep, bool back)
    {
        closing.Stop();
        if (popup?.Child is not TextBox editor || opened is null) return;
        TextBox box = opened;
        popup.IsOpen = false;
        popup = null;
        opened = null;
        string text = Regex.Replace(editor.Text, @"\s*[\r\n]+\s*", " ");
        if (keep && !editor.IsReadOnly && text != textWhenOpened) box.Text = text;
        if (back) box.Focus();
    }

    /// <summary>
    /// The ▾ in a text box's bottom right corner (the HTML app's <c>.vx</c>): 15 by 12 pixels, 1 pixel in from the edge,
    /// in the box colour with a dim arrow that lights up under the mouse. A press on it opens the larger box.
    /// </summary>
    private sealed class OpenButton : Adorner
    {
        private readonly TextBlock arrow = new() { Text = "▾", FontSize = 10, LineHeight = 12, TextAlignment = TextAlignment.Center, Width = 15, Height = 12 };
        private readonly Border face;

        public OpenButton(TextBox box) : base(box)
        {
            Box = box;
            face = new Border { Width = 15, Height = 12, Child = arrow, Cursor = Cursors.Arrow, ToolTip = Texts.Get("Common.LongText.Tip") };
            face.SetResourceReference(Border.BackgroundProperty, "well");
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            face.MouseEnter += (_, _) => arrow.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            face.MouseLeave += (_, _) => arrow.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            face.PreviewMouseLeftButtonDown += (_, e) => { e.Handled = true; Open(Box); };
            AddVisualChild(face);
        }

        /// <summary>The text box it is on.</summary>
        public TextBox Box { get; }

        /// <summary>The text box's padding before the ▾ made room for itself.</summary>
        public Thickness PaddingBefore { get; set; }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => face;

        protected override Size MeasureOverride(Size constraint)
        {
            face.Measure(new Size(15, 12));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Size size = AdornedElement.RenderSize;
            face.Arrange(new Rect(size.Width - 16, size.Height - 13, 15, 12));
            return finalSize;
        }
    }
}
