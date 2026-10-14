using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Files;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Pages;

/// <summary>
/// The file list's right-click menus (the HTML app's <c>#jobBox</c> contextmenu): on a column heading, on a folder's
/// line, on a file's row, and on the list's empty part.
/// </summary>
public partial class FilesPage
{
    /// <summary>A right-click in the list: the menu for what is under the mouse.</summary>
    private void ListRightClicked(object sender, MouseButtonEventArgs e)
    {
        object? under = null;
        for (DependencyObject? at = e.OriginalSource as DependencyObject; at is not null && under is null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is FrameworkElement { Tag: FileColumn column } && IsInHeadings(at)) under = column;
            else if (at is FrameworkElement { DataContext: FolderRow folder }) under = folder;
            else if (at is FileRowView { DataContext: FileRow row }) under = row;
        }
        e.Handled = true;
        var owner = (FrameworkElement)sender;
        switch (under)
        {
            case FileColumn column: Menus.Show(owner, HeadingMenu(column)); break;
            case FolderRow folder: Menus.Show(owner, FolderMenu(folder)); break;
            case FileRow row: Menus.Show(owner, RowMenu(row)); break;
            default: Menus.Show(owner, ListMenu()); break;
        }
    }

    private bool IsInHeadings(DependencyObject element)
    {
        for (DependencyObject? at = element; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (ReferenceEquals(at, ListHeader)) return true;
        return false;
    }

    /// <summary>A heading: sorting, hiding and widths.</summary>
    private IEnumerable<MenuChoice?> HeadingMenu(FileColumn column)
    {
        bool sortable = FileListView.CanSort(column);
        return
        [
            sortable ? new MenuChoice("Sort ascending", () => Model.SortBy(column, ascending: true)) : null,
            sortable ? new MenuChoice("Sort descending", () => Model.SortBy(column, ascending: false)) : null,
            new MenuChoice("Hide this column", () => Model.SetColumnShown(column, false), IsEnabled: column != FileColumn.File),
            new MenuChoice("Show every column", Model.ShowEveryColumn, IsEnabled: Model.ColumnOrder.Any(c => !Model.IsShown(c))),
            Menus.Line,
            new MenuChoice("Reset column widths", Model.FitColumnsAgain),
        ];
    }

    /// <summary>A folder's line, with "Group by folder".</summary>
    private IEnumerable<MenuChoice?> FolderMenu(FolderRow folder) =>
    [
        new MenuChoice(folder.IsOpen ? "Collapse this folder" : "Expand this folder", () => Model.ToggleFolder(folder)),
        new MenuChoice("Select its files", () => Model.SelectFolder(folder)),
        new MenuChoice("Switch on its files", () => Model.SetFolderOn(folder, true), IsEnabled: !Model.IsBusy),
        new MenuChoice("Switch off its files", () => Model.SetFolderOn(folder, false), IsEnabled: !Model.IsBusy),
        Menus.Line,
        new MenuChoice("Expand all", Model.OpenAllFolders, Key: "Ctrl+Shift+]"),
        new MenuChoice("Collapse all", Model.CloseAllFolders, Key: "Ctrl+Shift+["),
        Menus.Line,
        new MenuChoice("Add files...", () => AddFilesClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
        new MenuChoice("Add folder...", () => AddFolderClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
    ];

    /// <summary>A file's row. A row that is not selected becomes the selection first.</summary>
    internal IEnumerable<MenuChoice?> RowMenu(FileRow row)
    {
        if (!row.IsSelected) Model.Click(row, control: false, shift: false);
        int count = Model.SelectedCount;
        string many = count > 1 ? " " + count + " files" : "";
        return
        [
            new MenuChoice((row.IsOn ? "Switch off" : "Switch on") + many, () => Model.SetSelectedOn(!row.IsOn), IsEnabled: !Model.IsBusy),
            row.Entry.HeldOutput is not null && count == 1 ? new MenuChoice("Save anyway", () => SaveAnyway(row)) : null,
            new MenuChoice("Open in editor", () => OpenInEditor(row.Entry), IsEnabled: row.Entry.Source is not null),
            new MenuChoice("Choose export folder...", () => ExportFolderClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
            new MenuChoice("Remove" + many, () => RemoveClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
            Menus.Line,
            .. ListMenu(),
        ];
    }

    /// <summary>The list's empty part, and the end of a row's menu.</summary>
    private IEnumerable<MenuChoice?> ListMenu() =>
    [
        new MenuChoice("Select all", Model.SelectAll, IsEnabled: Model.HasFiles),
        new MenuChoice("Add files...", () => AddFilesClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
        new MenuChoice("Add folder...", () => AddFolderClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
    ];
}
