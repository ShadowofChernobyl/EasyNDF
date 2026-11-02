using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Pages;

namespace EasyNdf.App.Units;

/// <summary>
/// The bar between the Armory's left column (the source, the filters, the nations and divisions) and the units or
/// weapons: dragged, the column is made wider or narrower, so long mod and division names can be read; double-clicked,
/// it goes back to its first width. The Units and Weapons sub-tabs share the width, kept for next time.
/// </summary>
public static class LeftColumnSplit
{
    /// <summary>The column's first width.</summary>
    public const double FirstWidth = 300;

    /// <summary>The narrowest the column is made: its filters' buttons still fit side by side.</summary>
    private const double Least = 220;

    /// <summary>The room always left for the units or weapons beside it.</summary>
    private const double RoomLeft = 360;

    /// <summary>The name the width is kept under, among the app's other kept sizes.</summary>
    private const string SizeName = "armoryLeftColumn";

    /// <summary>
    /// Makes a bar resize a column: the column at the left of what holds the bar, which stands in the gap after it.
    /// </summary>
    public static void Prepare(ColumnDefinition column, SplitBar bar)
    {
        ShowKept(column);
        bar.Moved += x =>
        {
            // the bar stands in the middle of the gap after the column, and the mouse is on the bar's middle
            double gap = bar.Parent is Grid grid ? grid.ColumnDefinitions[Grid.GetColumn(bar)].ActualWidth : 14;
            double most = bar.Parent is FrameworkElement holder ? Math.Max(Least, holder.ActualWidth - gap - RoomLeft) : 600;
            column.Width = new GridLength(Math.Round(Math.Clamp(x - gap / 2, Least, most)));
        };
        bar.Reset += () => column.Width = new GridLength(FirstWidth);
        bar.Settled += () => FilesPage.RememberSize(SizeName, column.Width.Value == FirstWidth ? null : column.Width.Value);
        // the other sub-tab's bar may have moved meanwhile: they share the width
        bar.IsVisibleChanged += (_, _) => { if (bar.IsVisible) ShowKept(column); };
    }

    /// <summary>The column at the width kept, or its first width.</summary>
    private static void ShowKept(ColumnDefinition column) =>
        column.Width = new GridLength((double?)AppData.Current.State["sizes"]?[SizeName] ?? FirstWidth);
}
