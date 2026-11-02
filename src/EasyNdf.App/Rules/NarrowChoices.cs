using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace EasyNdf.App.Rules;

/// <summary>
/// Keeps the rule and row windows' dropdowns inside the app's window. A scanned file's property names can be very long,
/// and a dropdown's list was as wide as its longest name, running off the window; now the list is no wider than the
/// window, and a choice too long for the box or the list ends in "…", shown whole on hover (Controls/TrimmedTip.cs).
/// </summary>
internal static class NarrowChoices
{
    /// <summary>The space kept between the list and the window's edges.</summary>
    private const double EdgeRoom = 32;

    /// <summary>The widest a list is let be, even in a wide window, so it stays near its box.</summary>
    private const double Widest = 560;

    /// <summary>A choice's label and its dim part (a file's folder), as one line cut with "…" when it does not fit.</summary>
    private static readonly Lazy<DataTemplate> OptionTemplate = new(() => Template(
        """
        <TextBlock TextTrimming="CharacterEllipsis"><Run Text="{Binding Label, Mode=OneWay}" /><Run Text="{Binding DimText, Mode=OneWay}" Foreground="{DynamicResource muted}" /></TextBlock>
        """));

    /// <summary>A plain choice, a text, cut with "…" when it does not fit.</summary>
    private static readonly Lazy<DataTemplate> TextTemplate = new(() => Template(
        """<TextBlock Text="{Binding}" TextTrimming="CharacterEllipsis" />"""));

    /// <summary>A dropdown of <see cref="ChoiceOption"/>s: each cut with "…" when too long, its list no wider than the window.</summary>
    /// <param name="box">The dropdown.</param>
    /// <param name="tipShowsChoice">
    /// Whether its tooltip names the choice in full, after <paramref name="ownTip"/> when it has one: the box draws the
    /// choice where the mouse cannot reach it, so a choice cut short could not be read otherwise.
    /// </param>
    /// <param name="ownTip">What the dropdown is for, when its tooltip says.</param>
    public static ComboBox OfOptions(ComboBox box, bool tipShowsChoice = false, string? ownTip = null)
    {
        box.ItemTemplate = OptionTemplate.Value;
        if (tipShowsChoice)
        {
            void ShowChoice() => box.ToolTip = box.SelectedItem is ChoiceOption option
                ? ownTip is null ? option.Label + option.DimText : ownTip + "\n" + option.Label + option.DimText
                : ownTip;
            box.SelectionChanged += (_, _) => ShowChoice();
            ShowChoice();
        }
        return KeepInWindow(box);
    }

    /// <summary>A dropdown whose entries are <see cref="ComboBoxItem"/>s holding texts: each cut with "…" when too long, its list no wider than the window.</summary>
    public static ComboBox OfItems(ComboBox box)
    {
        foreach (ComboBoxItem item in box.Items.OfType<ComboBoxItem>().Where(item => item.Content is string)) item.ContentTemplate = TextTemplate.Value;
        return KeepInWindow(box);
    }

    /// <summary>Each time the list opens, it is made no wider than the window has room for.</summary>
    private static ComboBox KeepInWindow(ComboBox box)
    {
        box.DropDownOpened += (_, _) =>
        {
            if (PopupOf(box)?.Child is not FrameworkElement list) return;
            double window = Window.GetWindow(box)?.ActualWidth ?? Widest;
            list.MaxWidth = Math.Max(box.ActualWidth, Math.Min(Widest, window - EdgeRoom));
        };
        return box;
    }

    /// <summary>The pop-up that holds a dropdown's list, in its template.</summary>
    private static Popup? PopupOf(DependencyObject element)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, i);
            if (child is Popup popup) return popup;
            if (child is not ItemsPresenter && PopupOf(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>A data template from its XAML, in the app's own namespaces.</summary>
    private static DataTemplate Template(string content) =>
        (DataTemplate)XamlReader.Parse(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" + content.Trim() + "</DataTemplate>");
}
