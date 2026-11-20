using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace EasyNdf.App.Controls;

/// <summary>Where an element sits among the elements around it.</summary>
public static class TreeWalk
{
    /// <summary>
    /// Whether <paramref name="element"/> is <paramref name="ancestor"/> or inside it. Unlike <see cref="Visual.IsAncestorOf"/>,
    /// it takes elements that are not drawn on their own, such as a link in a text (a Hyperlink), which can have the
    /// focus: IsAncestorOf throws on them.
    /// </summary>
    public static bool Holds(this DependencyObject ancestor, DependencyObject? element)
    {
        for (DependencyObject? at = element; at is not null; at = ParentOf(at))
            if (ReferenceEquals(at, ancestor)) return true;
        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject element) => element switch
    {
        Visual or Visual3D => VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element),
        ContentElement content => ContentOperations.GetParent(content) ?? LogicalTreeHelper.GetParent(element),
        _ => LogicalTreeHelper.GetParent(element),
    };
}
