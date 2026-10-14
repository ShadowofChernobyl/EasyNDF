using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// "Wrap text in lists" (the HTML app's <c>data-wrap</c>; off at first): the texts of the file list, Details and the
/// preset, rule, condition and action lists stay on one line and end in "..." when they do not fit, or, switched on,
/// wrap onto more lines. Two app resources hold the choice, "list-trimming" and "list-wrapping", which those texts follow.
/// </summary>
public static class ListText
{
    /// <summary>Sets the two resources from the setting; at start-up and whenever the setting changes.</summary>
    public static void Apply(bool wrap)
    {
        Application.Current.Resources["list-trimming"] = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        Application.Current.Resources["list-wrapping"] = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    /// <summary>Applies the saved setting.</summary>
    public static void ApplySaved() => Apply(AppData.Current.Settings["wrapRows"]?.GetValue<bool>() == true);

    /// <summary>Makes a table cell's text follow the setting.</summary>
    public static CellText Follow(CellText cell)
    {
        cell.SetResourceReference(CellText.TextTrimmingProperty, "list-trimming");
        cell.SetResourceReference(CellText.TextWrappingProperty, "list-wrapping");
        return cell;
    }

    /// <summary>Makes a list's text follow the setting.</summary>
    public static T Follow<T>(T text) where T : TextBlock
    {
        text.SetResourceReference(TextBlock.TextTrimmingProperty, "list-trimming");
        text.SetResourceReference(TextBlock.TextWrappingProperty, "list-wrapping");
        return text;
    }
}
