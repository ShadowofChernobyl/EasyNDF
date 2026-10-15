using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace EasyNdf.App.Text;

/// <summary>
/// A text with simple formatting, for a sentence that is partly bold or partly code, kept as one text so a translator
/// can move the formatted words: <c>**bold**</c> and <c>`code`</c>. Used as
/// <c>&lt;TextBlock t:RichText.Markup="{t:Text Welcome.Intro}" /&gt;</c>.
/// </summary>
public static partial class RichText
{
    /// <summary>The formatted text the TextBlock shows.</summary>
    public static readonly DependencyProperty MarkupProperty = DependencyProperty.RegisterAttached(
        "Markup", typeof(string), typeof(RichText), new PropertyMetadata("", MarkupChanged));

    /// <summary>Reads the formatted text of a TextBlock.</summary>
    public static string GetMarkup(DependencyObject element) => (string)element.GetValue(MarkupProperty);

    /// <summary>Gives a TextBlock a formatted text.</summary>
    public static void SetMarkup(DependencyObject element, string value) => element.SetValue(MarkupProperty, value);

    /// <summary>Shows the new text: plain parts as they are, **bold** parts in bold and `code` parts in the code font.</summary>
    private static void MarkupChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block) return;
        block.Inlines.Clear();
        block.Inlines.AddRange(Pieces((string?)e.NewValue ?? "", bold: text => new Bold(new Run(text)), code: CodeRun, plain: text => new Run(text)));
    }

    /// <summary>
    /// A formatted text as pieces, each made by the caller's own function for its kind, for a place that shows
    /// **bold** some other way, such as in a warning colour.
    /// </summary>
    public static Inline[] Pieces(string markup, Func<string, Inline> bold, Func<string, Inline> code, Func<string, Inline> plain)
    {
        var pieces = new List<Inline>();
        foreach (Match part in Parts().Matches(markup))
        {
            if (part.Groups["bold"].Success) pieces.Add(bold(part.Groups["bold"].Value));
            else if (part.Groups["code"].Success) pieces.Add(code(part.Groups["code"].Value));
            else pieces.Add(plain(part.Value));
        }
        return [.. pieces];
    }

    /// <summary>A piece of code in the text, in the code font at its usual size.</summary>
    private static Run CodeRun(string text)
    {
        var run = new Run(text) { FontSize = 12 };
        run.SetResourceReference(TextElement.FontFamilyProperty, "mono");
        return run;
    }

    /// <summary>The parts of a formatted text: **bold**, `code`, or plain text up to the next of those.</summary>
    [GeneratedRegex(@"\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|[^*`]+|[*`]")]
    private static partial Regex Parts();
}
