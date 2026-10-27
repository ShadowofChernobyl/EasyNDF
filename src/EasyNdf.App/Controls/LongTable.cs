using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Text;

namespace EasyNdf.App.Controls;

/// <summary>
/// A <see cref="WebTable"/> for many rows, such as the hundreds of edits Details lists: the same headings, cells and
/// column widths, but only the rows in sight are made, as the file list does. A table that makes every row is laid out
/// again whole whenever its width changes, which froze the app while a pane beside it was dragged. The headings stay
/// at the top while the rows scroll, as the HTML app's do. The line at a heading's right can be dragged to make its
/// column wider or narrower, as the Armory's table's can.
/// </summary>
public sealed class LongTable : DockPanel
{
    private readonly IReadOnlyList<bool> codeColumns;
    /// <summary>The columns' shares of the width now: their first shares, or as last dragged.</summary>
    private GridLength[] widths;

    /// <summary>The columns' shares of the width at first, which a double-click on a heading's line puts back.</summary>
    private readonly GridLength[] firstWidths;

    /// <summary>While a heading's line is dragged: which column, where the mouse started, and every column's width then.</summary>
    private (int Column, double StartX, double[] StartWidths)? resizing;

    /// <summary>A column is never dragged narrower than this.</summary>
    private const double NarrowestColumn = 30;
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
        widths = [.. this.headings.ColumnWidths];
        firstWidths = [.. widths];
        // every column but the last can be resized: the last takes what the others leave
        for (int column = 0; column < widths.Length - 1; column++)
        {
            Border line = ResizeLine(column);
            Grid.SetColumn(line, column);
            this.headings.Children.Add(line);
        }
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

    /// <summary>
    /// The strip over the line at a heading's right, 7 pixels wide: dragged, it moves the line between that column and
    /// the next; double-clicked, it puts every column back as at first. It shows in the accent colour under the mouse
    /// and while dragged.
    /// </summary>
    private Border ResizeLine(int column)
    {
        // the Armory's table's tooltip says the same, so it is shared
        var line = new Border { Width = 7, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, Cursor = Cursors.SizeWE, ToolTip = Texts.Get("Units.Table.Resize.Tip") };
        void ShowState()
        {
            if (line.IsMouseOver || line.IsMouseCaptured) line.SetResourceReference(Border.BackgroundProperty, "accent");
            else line.Background = Brushes.Transparent;
            line.Opacity = 0.55;
        }
        line.MouseEnter += (_, _) => ShowState();
        line.MouseLeave += (_, _) => ShowState();
        line.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                ShowWidths([.. firstWidths]);
                return;
            }
            resizing = (column, e.GetPosition(this).X, [.. headings.ColumnDefinitions.Select(c => c.ActualWidth)]);
            line.CaptureMouse();
        };
        line.MouseMove += (_, e) =>
        {
            if (resizing is var (dragged, startX, startWidths)) DragLine(dragged, e.GetPosition(this).X - startX, startWidths);
        };
        line.MouseLeftButtonUp += (_, e) => { e.Handled = true; line.ReleaseMouseCapture(); };
        line.LostMouseCapture += (_, _) =>
        {
            resizing = null;
            ShowState();
        };
        return line;
    }

    /// <summary>
    /// The line at a column's right moved this far from where the drag began: that column grows by it and the next one
    /// shrinks by as much, so the table keeps its width. Neither goes below <see cref="NarrowestColumn"/>.
    /// </summary>
    private void DragLine(int column, double moved, double[] startWidths)
    {
        double grown = Math.Clamp(moved, NarrowestColumn - startWidths[column], startWidths[column + 1] - NarrowestColumn);
        double[] pixels = [.. startWidths];
        pixels[column] += grown;
        pixels[column + 1] -= grown;
        // kept as shares of the width, so the table still fills a pane made wider or narrower afterwards
        ShowWidths([.. pixels.Select(width => new GridLength(width, GridUnitType.Star))]);
    }

    /// <summary>Gives the headings and every row in sight these widths; rows made later are made with them.</summary>
    private void ShowWidths(GridLength[] chosen)
    {
        widths = chosen;
        SetWidths(headings);
        for (int index = 0; index < lines.Items.Count; index++)
            if (lines.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem { Content: Grid row }) SetWidths(row);
    }

    private void SetWidths(Grid grid)
    {
        for (int column = 0; column < widths.Length; column++) grid.ColumnDefinitions[column].Width = widths[column];
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
