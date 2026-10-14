using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// Gives a ? button its Help page (the HTML app's <c>data-w</c>): <c>controls:QuickHelp.Page="tab-files"</c>. A click
/// opens the page in the ? popup beside the button.
/// </summary>
public static class QuickHelp
{
    /// <summary>The id of the Help page a ? button shows.</summary>
    public static readonly DependencyProperty PageProperty = DependencyProperty.RegisterAttached(
        "Page", typeof(string), typeof(QuickHelp), new PropertyMetadata(null, PageChanged));

    /// <summary>Gets the Help page of a ? button.</summary>
    public static string? GetPage(DependencyObject button) => (string?)button.GetValue(PageProperty);

    /// <summary>Sets the Help page of a ? button.</summary>
    public static void SetPage(DependencyObject button, string? value) => button.SetValue(PageProperty, value);

    private static void PageChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Button button) return;
        button.Click -= Clicked;
        if (e.NewValue is string) button.Click += Clicked;
    }

    private static void Clicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && GetPage(button) is string page && MainView.Find(button) is MainView view)
            view.HelpPopup.Open(button, page);
    }
}
