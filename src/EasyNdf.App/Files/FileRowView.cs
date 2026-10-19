using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using EasyNdf.App.Controls;
using EasyNdf.Data.FileList;
using EasyNdf.App.Text;

namespace EasyNdf.App.Files;

/// <summary>
/// One row of the file list on screen (the HTML app's <c>rowHtml</c>): a cell for each shown column, in their order,
/// then the row's buttons. Its look follows the styles in Pages/FilesPage.xaml (selected, off, status colours).
/// Buttons carry a <see cref="FrameworkElement.Tag"/> saying what they do; the page acts on their clicks.
/// </summary>
/// <remarks>
/// The list reuses its rows while it scrolls, giving each another file. A row's cells are bound to its file, so they
/// follow it without being built again; they are built again only when the columns or the language change.
/// </remarks>
public sealed class FileRowView : Decorator
{
    /// <summary>The list the row belongs to: its columns, widths and preset choices.</summary>
    public static readonly DependencyProperty ListProperty = DependencyProperty.Register(nameof(List), typeof(FileListModel), typeof(FileRowView),
        new PropertyMetadata(null, (view, _) => ((FileRowView)view).Build()));

    /// <summary>What the Remove button, Save anyway and the export folder buttons say they do, in their Tag.</summary>
    public const string RemoveAction = "remove", SaveAnywayAction = "save", ChooseFolderAction = "dir", ClearFolderAction = "nodir";

    /// <summary>Makes an empty row; it fills itself in once it has its list and its <see cref="FileRow"/>.</summary>
    public FileRowView() => DataContextChanged += ShowOtherFile;

    /// <inheritdoc cref="ListProperty"/>
    public FileListModel? List { get => (FileListModel?)GetValue(ListProperty); set => SetValue(ListProperty, value); }

    /// <summary>The preset dropdown, when the Preset column is shown.</summary>
    private ComboBox? presetBox;

    /// <summary>While the preset dropdown is being filled in, its choice is not the user's.</summary>
    private bool showingPreset;

    /// <summary>The list reuses the row for another file: its cells follow, and the preset dropdown is filled in again.</summary>
    private void ShowOtherFile(object sender, DependencyPropertyChangedEventArgs e)
    {
        // a weak handler: the file row lives on after the list has done with this row
        if (e.OldValue is FileRow oldRow) PropertyChangedEventManager.RemoveHandler(oldRow, RowChanged, "");
        if (e.NewValue is FileRow newRow) PropertyChangedEventManager.AddHandler(newRow, RowChanged, "");
        Build();
        ShowPreset();
    }

    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(FileRow.Preset) or nameof(FileRow.Choices)) ShowPreset();
    }

    /// <summary>The list, columns and language the cells were built for.</summary>
    private (FileListModel List, string Columns, object? Language)? builtFor;

    private void Build()
    {
        if (List is not FileListModel list || DataContext is not FileRow) return;
        IReadOnlyList<FileColumn> shown = list.Columns.Shown;
        var layout = (list, string.Join(",", shown), (object?)Texts.Catalog.Language);
        if (builtFor == layout) return;
        builtFor = layout;
        presetBox = null;
        var grid = new Grid();
        grid.SetResourceReference(TextElement.ForegroundProperty, "ink");
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
        ShowPreset();
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
        stack.Children.Add(new DockPanel { Children = { ExportedTag(), name } });
        stack.Children.Add(SubLine(nameof(FileRow.FolderLine), nameof(FileRow.HasFolderLine)));
        stack.Children.Add(SubLine(nameof(FileRow.DuplicateLine), nameof(FileRow.HasDuplicateLine)));
        return stack;
    }

    /// <summary>"✎ edited", small, in the caution colour, after the name of a file EasyNDF exported before.</summary>
    private static Border ExportedTag()
    {
        var text = new TextBlock { Text = Texts.Get("Files.Exported"), FontSize = 10.5, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        text.SetResourceReference(TextBlock.ForegroundProperty, "caution");
        var tag = new Border { Child = text, BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        tag.SetResourceReference(Border.BorderBrushProperty, "caution");
        tag.SetBinding(ToolTipProperty, nameof(FileRow.ExportedTip));
        tag.SetBinding(VisibilityProperty, new Binding(nameof(FileRow.WasExported)) { Converter = (IValueConverter)Application.Current.FindResource("ShowWhenTrue") });
        DockPanel.SetDock(tag, Dock.Right);
        return tag;
    }

    private static TextBlock SubLine(string textProperty, string showProperty)
    {
        var line = new TextBlock { Style = (Style)Application.Current.FindResource("SubLine") };
        line.SetBinding(TextBlock.TextProperty, textProperty);
        line.SetBinding(VisibilityProperty, new Binding(showProperty) { Converter = (IValueConverter)Application.Current.FindResource("ShowWhenTrue") });
        return line;
    }

    /// <remarks>
    /// The dropdown is filled in by <see cref="ShowPreset"/> rather than bound: when a row is reused for another file,
    /// bound choices and a bound selection change one after the other, and in between the dropdown would clear the
    /// new file's preset.
    /// </remarks>
    private UIElement PresetCell()
    {
        presetBox = new ComboBox { Margin = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center, ToolTip = Texts.Get("Files.Row.Preset.Tip") };
        presetBox.SelectionChanged += PresetPicked;
        return presetBox;
    }

    /// <summary>Fills the preset dropdown with the file's choices and shows its preset.</summary>
    private void ShowPreset()
    {
        if (presetBox is null || DataContext is not FileRow row) return;
        showingPreset = true;
        presetBox.ItemsSource = row.Choices;
        presetBox.SelectedItem = row.Preset;
        showingPreset = false;
    }

    private void PresetPicked(object sender, SelectionChangedEventArgs e)
    {
        if (!showingPreset && DataContext is FileRow row && presetBox?.SelectedItem is PresetChoice choice) row.Preset = choice;
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
