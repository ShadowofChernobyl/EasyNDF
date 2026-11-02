using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Files;

namespace EasyNdf.App.Pages;

/// <summary>
/// The file list by keyboard: once it has the keyboard (a click on a row, or Tab), Up and Down move the selection
/// (Shift extends it), Home, End, Page Up and Page Down go further, Space switches the selected files on or off, and
/// Enter opens Details on the row. Delete and Ctrl+A are the tab's shortcuts (<see cref="HandleShortcut"/>). The row
/// the keys are on is outlined while the list has the keyboard.
/// </summary>
public partial class FilesPage
{
    /// <summary>The row the arrow keys are on; null until they are used or a row is clicked.</summary>
    private FileRow? keyRow;

    /// <summary>The row outlined now, so only it is cleared when the outline moves.</summary>
    private FileRow? outlinedRow;

    /// <summary>How many rows Page Up and Page Down move by.</summary>
    private const int PageRows = 10;

    /// <summary>The file rows as listed, in order, folder lines left out.</summary>
    private List<FileRow> ListedRows() => [.. Model.ListItems.OfType<FileRow>()];

    /// <summary>The row the keys are on: the one moved to last, or else the one Details shows, a selected one, or the first.</summary>
    private FileRow? KeyRowNow(List<FileRow> rows)
    {
        if (keyRow is not null && rows.Contains(keyRow)) return keyRow;
        return rows.FirstOrDefault(row => row.Entry == Model.DetailsRow) ?? rows.FirstOrDefault(row => row.IsSelected) ?? rows.FirstOrDefault();
    }

    /// <summary>Whether the keyboard is in use on the list: the outline shows only then, not after a click with the mouse.</summary>
    private bool keysInUse;

    /// <summary>Outlines the row the keys are on while the list itself has the keyboard and it is in use; otherwise none.</summary>
    private void ShowKeyRow()
    {
        FileRow? now = Rows.IsKeyboardFocused && keysInUse ? KeyRowNow(ListedRows()) : null;
        if (outlinedRow is not null && outlinedRow != now) outlinedRow.IsKeyRow = false;
        if (now is not null) now.IsKeyRow = true;
        outlinedRow = now;
    }

    /// <summary>The list got or lost the keyboard: reached with Tab, the outline shows where the keys are; clicked, it waits for a key.</summary>
    private void RowsFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Rows.IsKeyboardFocused && e.NewFocus == Rows) keysInUse = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        ShowKeyRow();
    }

    /// <summary>A key while the list itself has the keyboard; keys meant for a row's checkbox or buttons are theirs.</summary>
    private void RowsKeyDown(object sender, KeyEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Rows) || Model.IsBusy) return;
        List<FileRow> rows = ListedRows();
        if (KeyRowNow(rows) is not FileRow current) return;
        int at = rows.IndexOf(current);
        // with nothing selected and no row moved to yet, the first arrow selects the row the keys start on, not the next
        if (e.Key is Key.Up or Key.Down && !rows.Contains(keyRow!) && !current.IsSelected) at += e.Key == Key.Up ? 1 : -1;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool plain = (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None;
        int? to = e.Key switch
        {
            Key.Up => at - 1,
            Key.Down => at + 1,
            Key.PageUp => at - PageRows,
            Key.PageDown => at + PageRows,
            Key.Home => 0,
            Key.End => rows.Count - 1,
            _ => null,
        };
        if (to is int target && plain)
        {
            e.Handled = true;
            MoveKeyRow(rows[Math.Clamp(target, 0, rows.Count - 1)], shift);
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            // the selected files, or the row the keys are on when none is selected: all on, unless all are on already
            if (!current.IsSelected && !Model.HasSelection) Model.Click(current, control: false, shift: false);
            bool allOn = Model.Shown.Where(row => row.IsSelected).All(row => row.IsOn);
            Model.SetSelectedOn(!allOn);
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Model.DetailsRow = current.Entry;
            ShowDetails();
            if (Panes.IsFolded) Panes.Fold(false);
        }
        if (!e.Handled) return;
        keysInUse = true;
        ShowKeyRow();
    }

    /// <summary>Moves the keys to a row: it alone is selected, or with Shift the rows from the one clicked last to it; it is brought into sight.</summary>
    private void MoveKeyRow(FileRow row, bool extend)
    {
        Model.Click(row, control: false, shift: extend);
        keyRow = row;
        keysInUse = true;
        ShowKeyRow();
        BringIntoSight(row);
    }

    /// <summary>Scrolls the list so a row is in sight, making it first if it is out of sight and not made yet.</summary>
    private void BringIntoSight(FileRow row)
    {
        int index = Model.ListItems.IndexOf(row);
        if (index < 0) return;
        if (Rows.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement made)
        {
            made.BringIntoView();
            return;
        }
        if (FirstChild<VirtualizingStackPanel>(Rows) is VirtualizingStackPanel panel) panel.BringIndexIntoViewPublic(index);
    }

    /// <summary>The first element of a type inside another, looking down its children.</summary>
    private static T? FirstChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) return found;
            if (FirstChild<T>(child) is T deeper) return deeper;
        }
        return null;
    }
}
