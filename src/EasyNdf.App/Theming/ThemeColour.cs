using System.Windows;
using System.Windows.Documents;

namespace EasyNdf.App.Theming;

/// <summary>
/// Gives an element's text colour by a theme colour's name, for example "accent", in a template that only knows the
/// name from its data: <c>theming:ThemeColour.Foreground="{Binding SymbolColour}"</c>. The colour follows a change
/// of theme straight away, as <c>{DynamicResource accent}</c> would; a colour looked up once would stay the old one
/// until the element was drawn again.
/// </summary>
public static class ThemeColour
{
    /// <summary>The name of the theme colour the element's text (or outline) is drawn in.</summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.RegisterAttached(
        "Foreground", typeof(string), typeof(ThemeColour), new PropertyMetadata(null, ForegroundChanged));

    /// <summary>Sets the name of the theme colour an element's text is drawn in.</summary>
    public static void SetForeground(DependencyObject element, string? value) => element.SetValue(ForegroundProperty, value);

    /// <summary>The name of the theme colour an element's text is drawn in.</summary>
    public static string? GetForeground(DependencyObject element) => (string?)element.GetValue(ForegroundProperty);

    private static void ForegroundChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        if (element is not FrameworkElement shown) return;
        // TextBlock's and FolderGlyph's Foreground are both this one property, so one reference serves both
        if (change.NewValue is string name && name != "") shown.SetResourceReference(TextElement.ForegroundProperty, name);
        else shown.ClearValue(TextElement.ForegroundProperty);
    }
}
