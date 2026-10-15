using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Data;
using EasyNdf.App.Controls;
using EasyNdf.Data.FileList;
using EasyNdf.App.Text;

namespace EasyNdf.App.Files;

/// <summary>
/// One row of the file list on screen (the HTML app's <c>rowHtml</c>): a cell for each shown column, in their order,
/// then the row's buttons. Its look follows the styles in Pages/FilesPage.xaml (selected, off, status colours).
/// Buttons carry a <see cref="FrameworkElement.Tag"/> saying what they do; the page acts on their clicks. When the
/// shown columns change, the list makes its rows again, so a row is built once.
/// </summary>
public sealed class FileRowView : Decorator
{
    /// <summary>The list the row belongs to: its columns, widths and preset choices.</summary>
    public static readonly DependencyProperty ListProperty = DependencyProperty.Register(nameof(List), typeof(FileListModel), typeof(FileRowView),
        new PropertyMetadata(null, (view, _) => ((FileRowView)view).Build()));

    /// <summary>What the Remove button, Save anyway and the export folder buttons say they do, in their Tag.</summary>
    public const string RemoveAction = "remove", SaveAnywayAction = "save", ChooseFolderAction = "dir", ClearFolderAction = "nodir";

    /// <summary>Makes an empty row; it fills itself in once it has its list and its <see cref="FileRow"/>.</summary>
    public FileRowView() => DataContextChanged += (_, _) => Build();

    /// <inheritdoc cref="ListProperty"/>
    public FileListModel? List { get => (FileListModel?)GetValue(ListProperty); set => SetValue(ListProperty, value); }

    private void Build()
    {
        if (List is not FileListModel list || DataContext is not FileRow) return;
        var grid = new Grid();
        grid.SetResourceReference(TextElement.ForegroundProperty, "ink");
        IReadOnlyList<FileColumn> shown = list.Columns.Shown;
        for (int place = 0; place < shown.Count; place++)
        {
            var definition = new ColumnDefinition();
            definition.SetBinding(ColumnDefinition.WidthProperty, new Binding("[" + shown[place] + "]") { Source = list.Columns });
            grid.ColumnDefinitions.Add(definition);
            UIElement cell = CellFor(shown[place], list);
            Grid.SetColumn(cell, place);
            grid.Children.Add(cell);
        }
        var buttonsColumn = new ColumnDefinition();
        buttonsColumn.SetBinding(ColumnDefinition.WidthProperty, new Binding(nameof(ColumnWidths.Buttons)) { Source = list.Columns });
        grid.ColumnDefinitions.Add(buttonsColumn);
        UIElement buttons = ButtonsCell(list);
        Grid.SetColumn(buttons, shown.Count);
        grid.Children.Add(buttons);
        SetBinding(WidthProperty, new Binding(nameof(ColumnWidths.RowWidth)) { Source = list.Columns });
        // tbody tr: a line under it, in the selection colour when selected
        Child = new Bevel { Style = (Style)FindResource("FileListRow"), Child = grid };
    }

    private UIElement CellFor(FileColumn column, FileListModel list) => column switch
    {
        FileColumn.On => OnCell(),
        FileColumn.Number => TextCell(nameof(FileRow.Number)),
        FileColumn.File => FileCell(),
        FileColumn.Size => TextCell(nameof(FileRow.SizeText)),
        FileColumn.Lines => TextCell(nameof(FileRow.LinesText)),
        FileColumn.Modified => TextCell(nameof(FileRow.ModifiedText)),
        FileColumn.Preset => PresetCell(),
        FileColumn.ExportFolder => ExportFolderCell(),
        _ => StatusCell(),
    };

    /// <summary>The On checkbox, 5 pixels lower than the middle: a web page sets a checkbox 2 pixels under the text's baseline.</summary>
    private static UIElement OnCell()
    {
        var box = new CheckBox
        {
            Style = (Style)Application.Current.FindResource("Tick"),
            Margin = new Thickness(7, 8, 7, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Texts.Get("Files.Row.On.Tip"),
        };
        box.SetBinding(CheckBox.IsCheckedProperty, nameof(FileRow.IsOn));
        return box;
    }

    private static TextBlock TextCell(string property)
    {
        var text = Controls.ListText.Follow(new TextBlock { Style = (Style)Application.Current.FindResource("Cell") });
        text.SetBinding(TextBlock.TextProperty, property);
        return text;
    }

    /// <summary>The name in the code font, and under it its folder, and its size and date when another row has the same file.</summary>
    private UIElement FileCell()
    {
        var name = new TextBlock { Style = (Style)FindResource("FileName") };
        name.SetBinding(TextBlock.TextProperty, nameof(FileRow.NameText));
        var stack = new StackPanel { Margin = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(name);
        stack.Children.Add(SubLine(nameof(FileRow.FolderLine), nameof(FileRow.HasFolderLine)));
        stack.Children.Add(SubLine(nameof(FileRow.DuplicateLine), nameof(FileRow.HasDuplicateLine)));
        return stack;
    }

    private static TextBlock SubLine(string textProperty, string showProperty)
    {
        var line = new TextBlock { Style = (Style)Application.Current.FindResource("SubLine") };
        line.SetBinding(TextBlock.TextProperty, textProperty);
        line.SetBinding(VisibilityProperty, new Binding(showProperty) { Converter = (IValueConverter)Application.Current.FindResource("ShowWhenTrue") });
        return line;
    }

    private static UIElement PresetCell()
    {
        var choice = new ComboBox { Margin = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center, ToolTip = Texts.Get("Files.Row.Preset.Tip") };
        choice.SetBinding(ItemsControl.ItemsSourceProperty, nameof(FileRow.Choices));
        choice.SetBinding(Selector.SelectedItemProperty, nameof(FileRow.Preset));
        return choice;
    }

    /// <summary>td.dir: "Browse...", the folder's name 6 pixels either side (or "Not set"), and × to clear it.</summary>
    private static UIElement ExportFolderCell()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Button { Content = Texts.Get("Files.Row.Browse"), Tag = ChooseFolderAction, ToolTip = Texts.Get("Files.Row.Browse.Tip") });
        var name = new TextBlock { Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        name.SetBinding(TextBlock.TextProperty, nameof(FileRow.ExportFolderText));
        name.SetBinding(ToolTipProperty, nameof(FileRow.ExportFolderTip));
        row.Children.Add(name);
        var clear = new Button { Style = (Style)Application.Current.FindResource("IconButton"), Content = "×", Tag = ClearFolderAction, ToolTip = Texts.Get("Files.Row.ClearFolder.Tip") };
        clear.SetBinding(VisibilityProperty, new Binding(nameof(FileRow.HasExportFolder)) { Converter = (IValueConverter)Application.Current.FindResource("ShowWhenTrue") });
        row.Children.Add(clear);
        return row;
    }

    private UIElement StatusCell()
    {
        var status = new TextBlock { Style = (Style)FindResource("FileStatus") };
        status.SetBinding(TextBlock.TextProperty, nameof(FileRow.StatusText));
        return status;
    }

    /// <summary>td.acts: "Save anyway" when the result was held back, then the × that removes the row, on the well colour.</summary>
    private UIElement ButtonsCell(FileListModel list)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var saveAnyway = new Button { Content = Texts.Get("Files.SaveAnyway"), Tag = SaveAnywayAction, ToolTip = Texts.Get("Files.Row.SaveAnyway.Tip") };
        // the gap between the buttons is a space, as between two buttons in a line of text
        saveAnyway.SetBinding(MarginProperty, new Binding(nameof(ColumnWidths.ButtonGap)) { Source = list.Columns });
        saveAnyway.SetBinding(VisibilityProperty, new Binding(nameof(FileRow.HasHeldOutput)) { Converter = (IValueConverter)Application.Current.FindResource("ShowWhenTrue") });
        buttons.Children.Add(saveAnyway);
        buttons.Children.Add(new Button { Style = (Style)Application.Current.FindResource("IconButton"), Content = "×", Tag = RemoveAction, ToolTip = Texts.Get("Files.Row.Remove.Tip") });
        // it stays at the list's right edge while the list scrolls sideways (see ColumnWidths.ButtonsOffset)
        return new Border { Style = (Style)FindResource("FileButtonsCell"), Child = buttons, RenderTransform = list.Columns.ButtonsOffset };
    }
}
