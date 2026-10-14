using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// A <see cref="WebTable"/> for many rows, such as the hundreds of edits Details lists: the same headings, cells and
/// column widths, but only the rows in sight are made, as the file list does. A table that makes every row is laid out
/// again whole whenever its width changes, which froze the app while a pane beside it was dragged. The headings stay
/// at the top while the rows scroll, as the HTML app's do.
/// </summary>
public sealed class LongTable : DockPanel
{
    private readonly IReadOnlyList<bool> codeColumns;
    private readonly IReadOnlyList<GridLength> widths;
    private readonly WebTable headings;
    private readonly ListBox lines;

    /// <summary>A table of rows of texts, one text per column.</summary>
    /// <param name="headings">The headings, one per column.</param>
    /// <param name="codeColumns">Which columns show their values in the code font.</param>
    /// <param name="rows">The rows.</param>
    public LongTable(IReadOnlyList<string> headings, IReadOnlyList<bool> codeColumns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        this.codeColumns = codeColumns;
        this.headings = new WebTable(headings, codeColumns);
        this.headings.WidenFor(rows);
        widths = this.headings.ColumnWidths;
        SetDock(this.headings, Dock.Top);
        Children.Add(this.headings);

        lines = new RowList(this) { ItemsSource = rows };
        lines.SetResourceReference(StyleProperty, "LongTableLines");
        Children.Add(lines);
        // the rows' columns are narrower by the scrollbar's width while it shows; the headings' are made so too
        lines.Loaded += (_, _) =>
        {
            if (lines.Template.FindName("Scroller", lines) is ScrollViewer scroller)
                scroller.ScrollChanged += (_, _) => this.headings.Margin = new Thickness(0, 0, scroller.ComputedVerticalScrollBarVisibility == Visibility.Visible ? ScrollBarWidth(scroller) : 0, 0);
        };
    }

    /// <summary>The headings' column widths, the first shown row's, and how wide that row and the list are.</summary>
    internal (double[] Headings, double[] Row, double RowWidth, double ListWidth)? Widths()
    {
        if (lines.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem { Content: Grid row }) return null;
        return ([.. headings.ColumnDefinitions.Select(c => c.ActualWidth)], [.. row.ColumnDefinitions.Select(c => c.ActualWidth)], row.ActualWidth, lines.ActualWidth);
    }

    private static double ScrollBarWidth(ScrollViewer scroller) =>
        scroller.Template.FindName("PART_VerticalScrollBar", scroller) is FrameworkElement bar ? bar.ActualWidth : SystemParameters.VerticalScrollBarWidth;

    /// <summary>A row's line: its cells, each with a faint line under it, in the columns' shares of the width.</summary>
    private FrameworkElement LineFor(IReadOnlyList<string> texts)
    {
        var line = new Grid();
        foreach (GridLength width in widths) line.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        for (int column = 0; column < texts.Count; column++)
        {
            CellText cellText = ListText.Follow(new CellText { Text = texts[column] });
            TextBlock text = cellText.Shown;
            text.SetResourceReference(TextBlock.ForegroundProperty, "ink");
            if (codeColumns[column])
            {
                text.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
                text.FontSize = 12;
                text.LineHeight = 17.4;
            }
            var cell = new Bevel { Edges = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 3, 7, 3), Child = cellText };
            cell.SetResourceReference(Bevel.BottomRightProperty, "line");
            Grid.SetColumn(cell, column);
            line.Children.Add(cell);
        }
        return line;
    }

    /// <summary>The list of rows, which makes each row's line as it comes into sight.</summary>
    private sealed class RowList(LongTable table) : ListBox
    {
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            if (element is ListBoxItem container && item is IReadOnlyList<string> texts) container.Content = table.LineFor(texts);
        }
    }
}
