using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Files;
using EasyNdf.App.Text;

namespace EasyNdf.App.Controls;

/// <summary>
/// The table in the question about a file already in the list: a heading row, then Size, Modified and Lines for both
/// files. Lines that differ say so and their label is in the warning colour; lines that are the same are dim.
/// </summary>
public sealed class ComparisonTable : Grid
{
    private static string[] Headings => ["", Texts.Get("Files.Compare.AlreadyListed"), Texts.Get("Files.Compare.Picked")];

    /// <summary>Builds the table for a comparison.</summary>
    public ComparisonTable(FileComparison comparison)
    {
        Margin = new Thickness(0, 0, 0, 10);
        var labels = comparison.Lines.Select(line => line.IsSame ? line.Label : Texts.Get("Files.Compare.Differs", ("label", line.Label))).ToList();

        // A web page shares out a table's spare width in proportion to how wide each column's longest text is.
        double[] widest =
        [
            Math.Max(TextWidth(Headings[0], false), labels.Max(label => TextWidth(label, false))),
            Math.Max(TextWidth(Headings[1], false), comparison.Lines.Max(line => TextWidth(line.Listed, true))),
            Math.Max(TextWidth(Headings[2], false), comparison.Lines.Max(line => TextWidth(line.Picked, true))),
        ];
        foreach (double width in widest)
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width + 14, GridUnitType.Star) });

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int column = 0; column < 3; column++) AddHeading(column, Headings[column]);

        for (int row = 0; row < comparison.Lines.Count; row++)
        {
            FileComparison.Line line = comparison.Lines[row];
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddLabel(row + 1, labels[row], line.IsSame, isFirst: row == 0);
            AddValue(row + 1, 1, line.Listed, line.IsSame);
            AddValue(row + 1, 2, line.Picked, line.IsSame);
        }
    }

    /// <summary>thead th: the panel colour, a dark line under it and a faint one at its right.</summary>
    private void AddHeading(int column, string text)
    {
        var heading = new Bevel { Style = (Style)Application.Current.FindResource("ColumnHeading"), Child = new TextBlock { Text = text } };
        SetColumn(heading, column);
        Children.Add(heading);
        var darkLine = new Bevel { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Edges = new Thickness(0) };
        darkLine.SetResourceReference(Bevel.BackgroundProperty, "lo");
        SetColumn(darkLine, column);
        Children.Add(darkLine);
    }

    /// <summary>A line's label: dim, or in the warning colour when the two files differ there. It has no line under it.</summary>
    /// <remarks>
    /// A web table with merged borders counts half of each line under a row in the row below. The first line is taller
    /// than its values need, so the half that would sit in it is added below it instead, which puts every line where the
    /// HTML app draws it.
    /// </remarks>
    private void AddLabel(int row, string text, bool isSame, bool isFirst)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(7, 3, 7, isFirst ? 3.5 : 3), VerticalAlignment = VerticalAlignment.Top };
        label.SetResourceReference(TextBlock.ForegroundProperty, isSame ? "muted" : "warn");
        SetRow(label, row);
        Children.Add(label);
    }

    /// <summary>A value: in the code font, dim when both files have it, with a faint line under it.</summary>
    private void AddValue(int row, int column, string text, bool isSame)
    {
        var value = new TextBlock { Text = text, FontSize = 12, LineHeight = 17.4, VerticalAlignment = VerticalAlignment.Center };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        value.SetResourceReference(TextBlock.ForegroundProperty, isSame ? "muted" : "ink");
        var cell = new Bevel { Edges = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 3, 7, 3), Child = value };
        cell.SetResourceReference(Bevel.BottomRightProperty, "line");
        SetRow(cell, row);
        SetColumn(cell, column);
        Children.Add(cell);
    }

    /// <summary>How wide a text is in the interface font (12.5 pixels) or the code font (12 pixels).</summary>
    private static double TextWidth(string text, bool code)
    {
        var family = (FontFamily)Application.Current.FindResource(code ? "mono" : "ui-font");
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), code ? 12 : 12.5, Brushes.Black, 1);
        return formatted.WidthIncludingTrailingWhitespace;
    }
}
