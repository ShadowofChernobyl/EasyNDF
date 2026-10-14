using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// A table drawn as the HTML app's tables are: headings on the panel colour with a dark line under them, then rows with
/// a faint line under each. As on a web page, the width is shared out between the columns in proportion to how wide
/// each column's longest text is. Each cell stays on one line (see <see cref="WidestCellText"/>). Rows can be picked with a click (see <see cref="WebTableRow"/>).
/// </summary>
public sealed class WebTable : Grid
{
    private readonly bool[] codeColumns;
    private readonly double[] widest;

    /// <summary>Starts a table with its headings.</summary>
    /// <param name="headings">The headings, one per column.</param>
    /// <param name="codeColumns">Which columns show their values in the code font (12 pixels); the others use the interface font.</param>
    public WebTable(IReadOnlyList<string> headings, IReadOnlyList<bool> codeColumns)
    {
        this.codeColumns = [.. codeColumns];
        widest = new double[headings.Count];
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int column = 0; column < headings.Count; column++)
        {
            ColumnDefinitions.Add(new ColumnDefinition());
            var heading = new Bevel { Style = (Style)Application.Current.FindResource("ColumnHeading"), Child = new TextBlock { Text = headings[column] } };
            SetColumn(heading, column);
            Children.Add(heading);
            widest[column] = TextWidth(headings[column], code: false);
        }
        var darkLine = new Bevel { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Edges = new Thickness(0) };
        darkLine.SetResourceReference(Bevel.BackgroundProperty, "lo");
        SetColumnSpan(darkLine, headings.Count);
        Children.Add(darkLine);
        ShareWidths();
    }

    /// <summary>Adds a row of texts, one per column.</summary>
    public WebTableRow AddRow(IReadOnlyList<string> texts)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        int row = RowDefinitions.Count - 1;
        var tableRow = new WebTableRow(this, row, texts, codeColumns);
        for (int column = 0; column < texts.Count; column++)
            widest[column] = Math.Max(widest[column], Math.Min(TextWidth(texts[column], codeColumns[column]), WidestCellText(codeColumns[column])));
        ShareWidths();
        return tableRow;
    }

    /// <summary>
    /// Shares the width out by other texts too: a <see cref="LongTable"/> uses a table with headings only as its heading
    /// row, sized by all of its rows.
    /// </summary>
    internal void WidenFor(IEnumerable<IReadOnlyList<string>> rows)
    {
        foreach (IReadOnlyList<string> texts in rows)
            for (int column = 0; column < texts.Count; column++)
                widest[column] = Math.Max(widest[column], Math.Min(TextWidth(texts[column], codeColumns[column]), WidestCellText(codeColumns[column])));
        ShareWidths();
    }

    /// <summary>Each column's share of the width, as the columns are sized now.</summary>
    internal IReadOnlyList<GridLength> ColumnWidths => [.. ColumnDefinitions.Select(c => c.Width)];

    /// <summary>Each column's share of the width: its longest text, with the 7 pixels of padding each side.</summary>
    private void ShareWidths()
    {
        for (int column = 0; column < widest.Length; column++)
            ColumnDefinitions[column].Width = new GridLength(widest[column] + 14, GridUnitType.Star);
    }

    /// <summary>
    /// With "Wrap rows" off (the default), the HTML app keeps each cell on one line, ending in "..." when its column is too
    /// narrow, and counts at most 72 characters (72 times the width of "0") of it when sharing out the width. The HTML app
    /// sizes boxes with their padding included, so those 72 characters hold the 7 pixels of padding each side too.
    /// </summary>
    internal static double WidestCellText(bool code) => 72 * TextWidth("0", code) - 14;

    /// <summary>How wide a text is in the interface font (12.5 pixels) or the code font (12 pixels).</summary>
    internal static double TextWidth(string text, bool code)
    {
        var family = (FontFamily)Application.Current.FindResource(code ? "mono" : "ui-font");
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), code ? 12 : 12.5, Brushes.Black, 1);
        return formatted.WidthIncludingTrailingWhitespace;
    }
}

/// <summary>
/// One row of a <see cref="WebTable"/>: its cells, each with a faint line under it, on a background that shows when the
/// row is selected or under the mouse.
/// </summary>
public sealed class WebTableRow
{
    private readonly Bevel background;
    private readonly List<TextBlock> cells = [];
    private readonly List<string> cellColours = [];
    private bool isSelected;

    internal WebTableRow(WebTable table, int row, IReadOnlyList<string> texts, IReadOnlyList<bool> codeColumns)
    {
        background = new Bevel { Background = Brushes.Transparent, Edges = new Thickness(0) };
        Grid.SetRow(background, row);
        Grid.SetColumnSpan(background, texts.Count);
        table.Children.Add(background);
        for (int column = 0; column < texts.Count; column++)
        {
            CellText cellText = ListText.Follow(new CellText { Text = texts[column] });
            TextBlock text = cellText.Shown;
            if (codeColumns[column])
            {
                text.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
                text.FontSize = 12;
                text.LineHeight = 17.4;
            }
            // a web table puts half of each line under a row in the row below; drawing the whole line at the bottom of
            // every row, and starting the text 3 pixels in, puts lines and texts where the HTML app has them
            var cell = new Bevel { Edges = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 3, 7, 3), Child = cellText, IsHitTestVisible = false };
            cell.SetResourceReference(Bevel.BottomRightProperty, "line");
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            table.Children.Add(cell);
            cells.Add(text);
            cellColours.Add("ink");
            text.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        }
        background.MouseEnter += (_, _) => ShowState();
        background.MouseLeave += (_, _) => ShowState();
        background.MouseLeftButtonUp += (_, e) => Clicked?.Invoke(this, e);
        background.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) DoubleClicked?.Invoke(this, e); };
    }

    /// <summary>Raised when the row is clicked.</summary>
    public event EventHandler<MouseButtonEventArgs>? Clicked;

    /// <summary>Raised when the row is double-clicked.</summary>
    public event EventHandler<MouseButtonEventArgs>? DoubleClicked;

    /// <summary>Under the mouse, the row's text turns the accent colour (tr.pickr:hover), as rows that can be picked do.</summary>
    public bool AccentUnderMouse { get; set; }

    /// <summary>Under the mouse, the row takes the selection colour (tr.detl:hover).</summary>
    public bool SelectionColourUnderMouse { get; set; }

    /// <summary>Whether the row is drawn in the selection colour.</summary>
    public bool IsSelected
    {
        get => isSelected;
        set { isSelected = value; ShowState(); }
    }

    /// <summary>A tooltip for the whole row.</summary>
    public object? ToolTip { get => background.ToolTip; set => background.ToolTip = value; }

    /// <summary>The hand cursor, for rows that do something when clicked.</summary>
    public Cursor? Cursor { get => background.Cursor; set => background.Cursor = value; }

    /// <summary>Gives a cell a colour from the theme, such as "muted" or "caution".</summary>
    public void SetColour(int column, string resourceKey)
    {
        cellColours[column] = resourceKey;
        ShowState();
    }

    /// <summary>Draws the row's background and text colours for its state.</summary>
    private void ShowState()
    {
        bool hovered = background.IsMouseOver;
        bool selectionColour = isSelected || (hovered && SelectionColourUnderMouse);
        if (selectionColour) background.SetResourceReference(Bevel.BackgroundProperty, "sel");
        else background.Background = Brushes.Transparent;
        for (int column = 0; column < cells.Count; column++)
            cells[column].SetResourceReference(TextBlock.ForegroundProperty, hovered && AccentUnderMouse ? "accent" : cellColours[column]);
    }
}
