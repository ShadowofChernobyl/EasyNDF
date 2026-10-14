using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Rules;

namespace EasyNdf.App.Controls;

/// <summary>
/// Colours the text of a rule's formula, path or value box in the theme's NDF colours (see <see cref="RuleTextColours"/>).
/// A WPF text box draws its text in one colour, so the box's own text is made invisible and this draws it again, piece by
/// piece, exactly where the box puts each character, on top of it; the caret and the selection are still the box's own.
/// Set it with <c>ColouredTextBox.SetKind(box, RuleTextKind.Formula)</c>.
/// </summary>
public sealed class ColouredTextBox : FrameworkElement
{
    private readonly TextBox box;
    private readonly ScrollViewer textHost;
    private readonly RuleTextKind kind;

    /// <summary>What a text box holds, which decides its colours; <see cref="RuleTextKind.None"/> leaves it in one colour.</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(RuleTextKind), typeof(ColouredTextBox), new PropertyMetadata(RuleTextKind.None, KindSet));

    /// <summary>What a text box holds.</summary>
    public static RuleTextKind GetKind(TextBox box) => (RuleTextKind)box.GetValue(KindProperty);

    /// <summary>Colours a text box's text as what it holds.</summary>
    public static void SetKind(TextBox box, RuleTextKind kind) => box.SetValue(KindProperty, kind);

    private static void KindSet(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox box || (RuleTextKind)e.NewValue == RuleTextKind.None) return;
        // the template's parts exist only once it is applied, which happens when the box is first laid out
        if (!AddTo(box)) box.Loaded += AddWhenLoaded;
    }

    private static void AddWhenLoaded(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        box.Loaded -= AddWhenLoaded;
        AddTo(box);
    }

    /// <summary>Puts the coloured text over the box's text, in the same place in its template; false if it has no template yet.</summary>
    private static bool AddTo(TextBox box)
    {
        box.ApplyTemplate();
        if (box.Template?.FindName("PART_ContentHost", box) is not ScrollViewer host || host.Parent is not Panel holder) return false;
        if (holder.Children.OfType<ColouredTextBox>().Any()) return true;
        holder.Children.Insert(holder.Children.IndexOf(host) + 1, new ColouredTextBox(box, host, GetKind(box)));
        box.Foreground = Brushes.Transparent;
        return true;
    }

    private ColouredTextBox(TextBox box, ScrollViewer textHost, RuleTextKind kind)
    {
        this.box = box;
        this.textHost = textHost;
        this.kind = kind;
        IsHitTestVisible = false;
        box.TextChanged += (_, _) => InvalidateVisual();
        box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => InvalidateVisual()));
        box.SizeChanged += (_, _) => InvalidateVisual();
        Loaded += (_, _) => Theming.ThemeManager.Applied += InvalidateVisual;
        Unloaded += (_, _) => Theming.ThemeManager.Applied -= InvalidateVisual;
    }

    /// <summary>Draws each piece where the box puts its first character, inside the box.</summary>
    protected override void OnRender(DrawingContext drawing)
    {
        string text = box.Text;
        if (text == "") return;
        // left and right, where the box shows its text (it scrolls sideways); up and down, the whole box, as the box's
        // own text area is shorter than the letters that hang below the line
        double left = textHost.TranslatePoint(new Point(0, 0), this).X;
        var visible = new Rect(left, 0, textHost.ViewportWidth, ActualHeight);
        drawing.PushClip(new RectangleGeometry(visible));
        var typeface = new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (ColouredPiece piece in RuleTextColours.Pieces(text, kind))
        {
            Rect place = box.GetRectFromCharacterIndex(piece.Start);
            if (place.IsEmpty) continue;
            var shown = new FormattedText(text.Substring(piece.Start, piece.Length), CultureInfo.CurrentUICulture, box.FlowDirection, typeface,
                box.FontSize, Brush(piece.Colour), pixelsPerDip);
            // at the top of the box's own glyphs for that character: the box puts its letters there whatever its line height
            drawing.DrawText(shown, box.TranslatePoint(place.TopLeft, this));
        }
        drawing.Pop();
    }

    private Brush Brush(string colour) => TryFindResource(colour) as Brush ?? Brushes.Gray;
}
