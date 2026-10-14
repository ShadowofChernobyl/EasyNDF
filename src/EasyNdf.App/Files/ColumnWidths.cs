using System.ComponentModel;
using System.Windows;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Files;

/// <summary>
/// The file list's shown columns and their widths, shared by the headings and every row. The last shown column takes
/// what is left; after it comes the column of each row's buttons, as wide as they are.
/// </summary>
public sealed class ColumnWidths : INotifyPropertyChanged
{
    private Dictionary<FileColumn, double> widths = [];

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The columns shown, in their order.</summary>
    public IReadOnlyList<FileColumn> Shown { get; private set; } = [];

    /// <summary>A column's width; the last shown column takes what is left.</summary>
    public GridLength this[FileColumn column] => widths.TryGetValue(column, out double width) ? new GridLength(width) : new GridLength(1, GridUnitType.Star);

    /// <summary>The first shown column's width: with "Group by folder", a folder's checkbox sits in it.</summary>
    public GridLength First => Shown.Count > 0 ? this[Shown[0]] : new GridLength(46);

    /// <summary>
    /// The width the list needs at least: every column but the last, the room the last one keeps, and the buttons. A
    /// narrower list scrolls sideways.
    /// </summary>
    public double ListMinWidth { get; private set; }

    /// <summary>
    /// The column of each row's buttons: the × that removes it, and "Save anyway" on a row whose result was held back.
    /// </summary>
    public GridLength Buttons { get; private set; } = new(PlainButtonsWidth);

    /// <summary>The buttons column with the × alone: the button, 2 pixels before it and 7 after.</summary>
    public const double PlainButtonsWidth = 33;

    /// <summary>
    /// How wide every row is: not set while the columns fit the list, or <see cref="ListMinWidth"/> when they are wider
    /// and the list scrolls sideways.
    /// </summary>
    public double RowWidth
    {
        get => rowWidth;
        set { rowWidth = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowWidth))); }
    }
    private double rowWidth = double.NaN;

    /// <summary>
    /// Moves the buttons column of every row and of the headings so it stays at the list's right edge while the list
    /// scrolls sideways, as the HTML app's <c>position: sticky</c> does.
    /// </summary>
    public System.Windows.Media.TranslateTransform ButtonsOffset { get; } = new();

    /// <summary>The space between "Save anyway" and the ×: a space in the interface font.</summary>
    public Thickness ButtonGap { get; private set; }

    /// <summary>
    /// Takes the shown columns before the rows are made, when they have changed; their widths follow with
    /// <see cref="Set"/>. Columns not fitted yet share what is left.
    /// </summary>
    public void ShowColumns(IReadOnlyList<FileColumn> shown)
    {
        if (Shown.SequenceEqual(shown)) return;
        Shown = shown;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>Takes the shown columns and their widths.</summary>
    /// <param name="shown">The shown columns, in order.</param>
    /// <param name="fitted">The width of every shown column; the last one's is the least it keeps.</param>
    /// <param name="buttonsWidth">The buttons column's width.</param>
    /// <param name="gap">The space between two buttons in a row.</param>
    public void Set(IReadOnlyList<FileColumn> shown, IReadOnlyDictionary<FileColumn, double> fitted, double buttonsWidth, double gap)
    {
        Shown = shown;
        widths = shown.Take(shown.Count - 1).ToDictionary(column => column, column => fitted[column]);
        ListMinWidth = widths.Values.Sum() + fitted[shown[^1]] + buttonsWidth;
        Buttons = new GridLength(buttonsWidth);
        ButtonGap = new Thickness(0, 0, gap, 0);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
