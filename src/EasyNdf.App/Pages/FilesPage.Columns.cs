using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.Data.FileList;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The file list's column headings (the HTML app's <c>renderJobHead</c>): click one to sort, drag one onto another to
/// move its column, drag the line at its right to resize it (double-click that line to go back to automatic widths),
/// and the Columns popup to show or hide columns.
/// </summary>
public partial class FilesPage
{
    /// <summary>The shown columns and sort the headings were last built for, so they are built again only when those change.</summary>
    private string headingsBuiltFor = "";

    /// <summary>Builds the headings again when the shown columns or the sort have changed.</summary>
    private void ShowHeadings()
    {
        string now = string.Join(",", Model.ShownColumns) + "|" + Model.SortColumn + "|" + Model.SortAscending;
        if (now == headingsBuiltFor) return;
        headingsBuiltFor = now;

        ListHeader.Children.Clear();
        ListHeader.ColumnDefinitions.Clear();
        IReadOnlyList<FileColumn> shown = Model.ShownColumns;
        for (int place = 0; place < shown.Count; place++)
        {
            var definition = new ColumnDefinition();
            definition.SetBinding(ColumnDefinition.WidthProperty, new Binding("[" + shown[place] + "]") { Source = Model.Columns });
            ListHeader.ColumnDefinitions.Add(definition);
            UIElement heading = Heading(shown[place], isLast: place == shown.Count - 1);
            Grid.SetColumn(heading, place);
            ListHeader.Children.Add(heading);
        }
        // th.acts: above the row buttons, with no line at its right
        var buttonsColumn = new ColumnDefinition();
        buttonsColumn.SetBinding(ColumnDefinition.WidthProperty, new Binding(nameof(Files.ColumnWidths.Buttons)) { Source = Model.Columns });
        ListHeader.ColumnDefinitions.Add(buttonsColumn);
        var buttonsHeading = new Bevel { Style = (Style)FindResource("ColumnHeading"), Edges = new Thickness(0), RenderTransform = Model.Columns.ButtonsOffset };
        Grid.SetColumn(buttonsHeading, shown.Count);
        ListHeader.Children.Add(buttonsHeading);
        // the dark line under the headings, drawn over the lines between them
        var darkLine = new Bevel { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Edges = new Thickness(0) };
        darkLine.SetResourceReference(Bevel.BackgroundProperty, "lo");
        Grid.SetColumnSpan(darkLine, shown.Count + 1);
        ListHeader.Children.Add(darkLine);
    }

    /// <summary>One heading: its name, the accent arrow when the list is sorted by it, and the line that resizes it.</summary>
    private UIElement Heading(FileColumn column, bool isLast)
    {
        bool sortable = FileListView.CanSort(column);
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new TextBlock { Text = ColumnFit.Heading(column) });
        if (Model.SortColumn == column)
            label.Children.Add(new TextBlock { Text = Model.SortAscending ? "▴" : "▾", Style = (Style)FindResource("SortArrow") });
        var heading = new Bevel
        {
            Style = (Style)FindResource("ColumnHeading"),
            Child = label,
            Tag = column,
            AllowDrop = true,
            Cursor = sortable ? Cursors.Hand : null,
            ToolTip = sortable ? Texts.Get("Files.Column.Sortable.Tip") : Texts.Get("Files.Column.Number.Tip"),
        };
        heading.MouseLeftButtonDown += HeadingPressed;
        heading.MouseMove += HeadingDragged;
        heading.MouseLeftButtonUp += HeadingReleased;
        heading.Drop += HeadingDropped;

        // the last column ends where the buttons begin: that line cannot be dragged
        if (isLast) return heading;
        var cell = new Grid();
        cell.Children.Add(heading);
        cell.Children.Add(ResizeLine(column));
        return cell;
    }

    /// <summary>
    /// The list scrolled: the headings, which stay above the rows, follow them sideways, and the buttons column stays at
    /// the right edge, as the HTML app's sticky buttons do. Both are only moved where they are drawn.
    /// </summary>
    private void ListScrolled(object sender, ScrollChangedEventArgs e)
    {
        headingsOffset.X = -ListScroller.HorizontalOffset;
        Model.Columns.ButtonsOffset.X = ListScroller.HorizontalOffset - ListScroller.ScrollableWidth;
    }

    /// <summary>Moves the headings sideways with the rows.</summary>
    private readonly TranslateTransform headingsOffset = new();

    // ---- sorting and moving columns ----

    private Point? headingPressedAt;

    private void HeadingPressed(object sender, MouseButtonEventArgs e) => headingPressedAt = e.GetPosition(this);

    /// <summary>A heading dragged a few pixels: it can be dropped on another heading to move its column there.</summary>
    private void HeadingDragged(object sender, MouseEventArgs e)
    {
        if (headingPressedAt is not Point start || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { Tag: FileColumn column } heading) return;
        Vector moved = e.GetPosition(this) - start;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        headingPressedAt = null;
        DragDrop.DoDragDrop(heading, new DataObject(typeof(FileColumn), column), DragDropEffects.Move);
    }

    /// <summary>A click on a heading, without dragging it: sorts the list by its column.</summary>
    private void HeadingReleased(object sender, MouseButtonEventArgs e)
    {
        if (headingPressedAt is null || sender is not FrameworkElement { Tag: FileColumn column }) return;
        headingPressedAt = null;
        Model.SortBy(column);
    }

    /// <summary>A heading dropped on another: on its left half, the column goes before it; on its right half, after it.</summary>
    private void HeadingDropped(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FileColumn target } heading || e.Data.GetData(typeof(FileColumn)) is not FileColumn moved) return;
        e.Handled = true;
        Model.MoveColumn(moved, target, after: e.GetPosition(heading).X > heading.ActualWidth / 2);
    }

    // ---- resizing columns ----

    private (FileColumn Column, double StartX, double StartWidth)? resizing;

    /// <summary>.rz: the strip over the line at a heading's right, 7 pixels wide, accent-coloured under the mouse and while dragged.</summary>
    private Border ResizeLine(FileColumn column)
    {
        var line = new Border
        {
            Width = 7,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeWE,
            Tag = column,
            ToolTip = Texts.Get("Files.Column.Resize.Tip"),
        };
        void ShowState() => line.Background = line.IsMouseOver || line.IsMouseCaptured ? AccentAtHalf() : Brushes.Transparent;
        line.MouseEnter += (_, _) => ShowState();
        line.MouseLeave += (_, _) => ShowState();
        line.LostMouseCapture += (_, _) =>
        {
            if (resizing is not null) Model.ColumnWidthsSettled(); // kept for next time
            resizing = null;
            ShowState();
        };
        line.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2) { Model.FitColumnsAgain(); return; }
            // the first drag: every column starts from the width it has now
            Model.KeepColumnWidths(CurrentColumnWidths());
            resizing = (column, e.GetPosition(this).X, Model.ColumnWidthFor(column));
            line.CaptureMouse();
        };
        line.MouseMove += (_, e) =>
        {
            if (resizing is not var (dragged, startX, startWidth)) return;
            Model.SetColumnWidth(dragged, startWidth + e.GetPosition(this).X - startX);
        };
        line.MouseLeftButtonUp += (_, e) => { e.Handled = true; line.ReleaseMouseCapture(); };
        return line;
    }

    /// <summary>The accent colour at 55%, as the HTML app draws a column line under the mouse.</summary>
    private Brush AccentAtHalf()
    {
        var accent = ((SolidColorBrush)FindResource("accent")).Color;
        return new SolidColorBrush(accent) { Opacity = 0.55 };
    }

    /// <summary>How wide each shown column is drawn now.</summary>
    private Dictionary<FileColumn, double> CurrentColumnWidths()
    {
        IReadOnlyList<FileColumn> shown = Model.ShownColumns;
        return Enumerable.Range(0, shown.Count).ToDictionary(place => shown[place], place => ListHeader.ColumnDefinitions[place].ActualWidth);
    }

    // ---- the Columns popup ----

    /// <summary>"Columns": opens the popup with a checkbox for each column, in their order, and "Reset columns".</summary>
    private void ColumnsClicked(object sender, RoutedEventArgs e)
    {
        if (ColumnsPopup.IsOpen) { ColumnsPopup.IsOpen = false; return; }
        ColumnChoices.Children.Clear();
        foreach (FileColumn column in Model.ColumnOrder)
        {
            var choice = new CheckBox
            {
                Style = (Style)FindResource("PopupChoice"),
                ToolTip = Texts.Get("Files.Column.ShowOrHide.Tip"),
                Content = ColumnFit.Heading(column),
                IsChecked = Model.IsShown(column),
                IsEnabled = column != FileColumn.File, // the file's name is always shown
                Margin = new Thickness(0, ColumnChoices.Children.Count == 0 ? 0 : 5, 0, 0),
            };
            FileColumn chosen = column;
            choice.Click += (_, _) => Model.SetColumnShown(chosen, choice.IsChecked == true);
            ColumnChoices.Children.Add(choice);
        }
        var reset = new Button
        {
            Content = Texts.Get("Files.Column.Reset"),
            // a grid item stretches across the popup
            Margin = new Thickness(0, 9, 0, 0), // the 5 between items and its own 4
            ToolTip = Texts.Get("Files.Column.Reset.Tip"),
        };
        reset.Click += (_, _) =>
        {
            Model.ResetColumns();
            ColumnsPopup.IsOpen = false;
        };
        ColumnChoices.Children.Add(reset);
        ColumnsPopup.IsOpen = true;
    }
}
