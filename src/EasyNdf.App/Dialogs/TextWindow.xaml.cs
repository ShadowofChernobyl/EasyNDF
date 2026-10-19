using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// A window with a long text to read, such as the License. It closes with ×, Escape or a click outside it.
/// </summary>
public partial class TextWindow : UserControl
{
    /// <summary>Builds the window, closed.</summary>
    public TextWindow()
    {
        InitializeComponent();
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Opens the window on a text, scrolled to its top, with the keyboard in it so the arrow keys scroll.</summary>
    public void Open(string title, UIElement text)
    {
        Title.Text = title;
        TextHolder.Content = text;
        Visibility = Visibility.Visible;
        Scroller.ScrollToTop();
        Dispatcher.BeginInvoke(() => Scroller.Focus());
    }

    /// <summary>
    /// The License text: white space kept, in the code font, dim (the HTML app's <c>.lic</c>). The text is as wide as its
    /// longest line and sits in the middle of the box, so the license's lines, written to fit 80 columns, keep their
    /// shape and its centred headings look centred; a narrow window wraps them.
    /// </summary>
    public static TextBlock PlainText(string text)
    {
        var block = new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, LineHeight = 16.675, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        block.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return block;
    }

    private void Close() => Visibility = Visibility.Collapsed;

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>46rem wide, or 94% of the window when it is narrower; 88% of its height, at most 60rem.</summary>
    private void BackdropResized(object sender, SizeChangedEventArgs e)
    {
        // the browser's window is the app's, with its 10-pixel border
        double windowWidth = e.NewSize.Width + 20, windowHeight = e.NewSize.Height + 20;
        Box.Width = Math.Min(736, windowWidth * 0.94);
        Box.Height = Math.Min(960, windowHeight * 0.88);
    }
}
