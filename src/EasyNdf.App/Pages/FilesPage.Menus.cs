using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Files;
using EasyNdf.Data.FileList;
using EasyNdf.App.Text;

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
            sortable ? new MenuChoice(Texts.Get("Files.Menu.SortAscending"), () => Model.SortBy(column, ascending: true)) : null,
            sortable ? new MenuChoice(Texts.Get("Files.Menu.SortDescending"), () => Model.SortBy(column, ascending: false)) : null,
            new MenuChoice(Texts.Get("Files.Menu.HideColumn"), () => Model.SetColumnShown(column, false), IsEnabled: column != FileColumn.File),
            new MenuChoice(Texts.Get("Files.Menu.ShowEveryColumn"), Model.ShowEveryColumn, IsEnabled: Model.ColumnOrder.Any(c => !Model.IsShown(c))),
            Menus.Line,
            new MenuChoice(Texts.Get("Files.Menu.ResetColumnWidths"), Model.FitColumnsAgain),
        ];
    }

    /// <summary>A folder's line, with "Group by folder".</summary>
    private IEnumerable<MenuChoice?> FolderMenu(FolderRow folder) =>
    [
        new MenuChoice(folder.IsOpen ? Texts.Get("Files.Menu.CollapseFolder") : Texts.Get("Files.Menu.ExpandFolder"), () => Model.ToggleFolder(folder)),
        new MenuChoice(Texts.Get("Files.Menu.SelectFolderFiles"), () => Model.SelectFolder(folder)),
        new MenuChoice(Texts.Get("Files.Menu.SwitchOnFolder"), () => Model.SetFolderOn(folder, true), IsEnabled: !Model.IsBusy),
        new MenuChoice(Texts.Get("Files.Menu.SwitchOffFolder"), () => Model.SetFolderOn(folder, false), IsEnabled: !Model.IsBusy),
        Menus.Line,
        new MenuChoice(Texts.Get("Files.ExpandAll"), Model.OpenAllFolders, Key: "Ctrl+Shift+]"),
        new MenuChoice(Texts.Get("Files.CollapseAll"), Model.CloseAllFolders, Key: "Ctrl+Shift+["),
        Menus.Line,
        new MenuChoice(Texts.Get("Files.AddFiles"), () => AddFilesClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
        new MenuChoice(Texts.Get("Files.AddFolder"), () => AddFolderClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
    ];

    /// <summary>A file's row. A row that is not selected becomes the selection first.</summary>
    internal IEnumerable<MenuChoice?> RowMenu(FileRow row)
    {
        if (!row.IsSelected) Model.Click(row, control: false, shift: false);
        int count = Model.SelectedCount;
        // one file: "Switch off"; several: "Switch off 3 files"
        string switchText = count > 1
            ? (row.IsOn ? Texts.Get("Files.Menu.SwitchOffSeveral", ("files", FileListModel.Files(count))) : Texts.Get("Files.Menu.SwitchOnSeveral", ("files", FileListModel.Files(count))))
            : (row.IsOn ? Texts.Get("Files.Menu.SwitchOff") : Texts.Get("Files.Menu.SwitchOn"));
        string removeText = count > 1 ? Texts.Get("Files.Menu.RemoveSeveral", ("files", FileListModel.Files(count))) : Texts.Get("Files.Remove");
        return
        [
            new MenuChoice(switchText, () => Model.SetSelectedOn(!row.IsOn), IsEnabled: !Model.IsBusy),
            row.Entry.HeldOutput is not null && count == 1 ? new MenuChoice(Texts.Get("Files.SaveAnyway"), () => SaveAnyway(row)) : null,
            new MenuChoice(Texts.Get("Files.Menu.OpenInEditor"), () => OpenInEditor(row.Entry), IsEnabled: row.Entry.Source is not null),
            new MenuChoice(Texts.Get("Files.Menu.ChooseExportFolder"), () => ExportFolderClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
            Data.Divisions.FileAndTextRules.IsDictionaryFile(row.Entry.Name) && FileRunner.GoesIntoLocalisation(row.Entry) && count == 1
                ? new MenuChoice(Texts.Get("Files.Menu.GameMod"), () => _ = ChangeGameMod(), IsEnabled: !Model.IsBusy,
                    Key: FileRunner.KeptGameMod(AppData.Current.OpenMod.Name) ?? "", Tip: Texts.Get("Files.Menu.GameMod.Tip"))
                : null,
            new MenuChoice(removeText, () => RemoveClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
            Menus.Line,
            .. ListMenu(),
        ];
    }

    /// <summary>The list's empty part, and the end of a row's menu.</summary>
    private IEnumerable<MenuChoice?> ListMenu() =>
    [
        new MenuChoice(Texts.Get("Files.SelectAll"), Model.SelectAll, IsEnabled: Model.HasFiles),
        new MenuChoice(Texts.Get("Files.AddFiles"), () => AddFilesClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
        new MenuChoice(Texts.Get("Files.AddFolder"), () => AddFolderClicked(this, new RoutedEventArgs()), IsEnabled: !Model.IsBusy),
    ];
}
