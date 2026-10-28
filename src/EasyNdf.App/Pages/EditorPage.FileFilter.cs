using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyNdf.App.Text;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Pages;

/// <summary>
/// The file filter: the funnel joined to the file dropdown, which opens a panel to find files by name and to choose
/// which files the dropdown lists (all, those with rules, those switched off, those the last run changed…). It is the
/// same filter as the Files tab's search box and Show only: changing one changes the other, and Show only is remembered.
/// </summary>
public partial class EditorPage
{
    /// <summary>The choices, in the panel's two columns, each under its heading.</summary>
    private static readonly (string Heading, ShowOnly[] Choices)[][] FilterColumns =
    [
        [("Editor.FileFilter.Rules", [ShowOnly.All, ShowOnly.HasPreset, ShowOnly.NoPreset]), ("Editor.FileFilter.Switch", [ShowOnly.On, ShowOnly.Off])],
        [("Editor.FileFilter.LastRun", [ShowOnly.Changed, ShowOnly.ErrorsOrWarnings, ShowOnly.RuleChangedNothing]), ("Editor.FileFilter.Reading", [ShowOnly.NotRead])],
    ];

    /// <summary>Waits for a pause in typing before filtering by name, as the Files tab's search box does.</summary>
    private DispatcherTimer? namePause;

    /// <summary>Whether the panel is filling its controls, when their changes are not the user's.</summary>
    private bool fillingFileFilter;

    /// <summary>The Files tab's files the dropdown can list: those read from a file.</summary>
    private IEnumerable<FileEntry> ListedFiles() => (Files?.Entries ?? []).Where(entry => entry.Source is not null);

    /// <summary>Whether the file filter lets a file into the dropdown: the Files tab's Show only and search.</summary>
    private bool FileFilterLetsThrough(FileEntry entry) =>
        Files is null || FileListView.Shows(entry, Files.ShowOnly, Files.Search);

    /// <summary>Whether a filter is on: a Show only choice other than all files, or a name to find.</summary>
    private bool FileFilterIsOn => Files is not null && (Files.ShowOnly != ShowOnly.All || Files.Search != "");

    /// <summary>The funnel, pressed in while a filter is on, with a tooltip saying which and how many files it lists.</summary>
    private void ShowFileFilterButton()
    {
        FileFilterButton.IsChecked = FileFilterIsOn;
        if (Files is null || !FileFilterIsOn)
        {
            FileFilterButton.ToolTip = Texts.Get("Editor.FileFilter.Tip");
            return;
        }
        int listed = ListedFiles().Count(FileFilterLetsThrough);
        string choice = Files.Search == "" ? ChoiceName(Files.ShowOnly)
            : Files.ShowOnly == ShowOnly.All ? Texts.Get("Editor.FileFilter.Named", ("name", Files.SearchText.Trim()))
            : Texts.Get("Editor.FileFilter.ChoiceAndName", ("choice", ChoiceName(Files.ShowOnly)), ("name", Files.SearchText.Trim()));
        FileFilterButton.ToolTip = Texts.Get("Editor.FileFilter.On.Tip", ("choice", choice), ("listed", listed), ("all", ListedFiles().Count()));
    }

    /// <summary>The funnel: opens the panel, or closes it.</summary>
    private void FileFilterClicked(object sender, RoutedEventArgs e)
    {
        if (FileFilterPopup.IsOpen)
        {
            FileFilterPopup.IsOpen = false;
            return;
        }
        fillingFileFilter = true;
        FileNameFilter.Text = Files?.SearchText ?? "";
        fillingFileFilter = false;
        FillFilterChoices();
        FileFilterPopup.IsOpen = true;
        FileNameFilter.Focus();
    }

    /// <summary>For the screenshots: the panel, open as a click on the funnel opens it.</summary>
    internal System.Windows.Controls.Primitives.Popup OpenFileFilterNow()
    {
        FileFilterClicked(this, new RoutedEventArgs());
        return FileFilterPopup;
    }

    /// <summary>The panel closed: the funnel shows again whether a filter is on (a click pressed or released it).</summary>
    private void FileFilterClosed(object? sender, EventArgs e) => ShowFileFilterButton();

    /// <summary>The choices in their columns, the chosen one marked, each with how many files it would list.</summary>
    private void FillFilterChoices()
    {
        StackPanel[] columns = [FilterColumnOne, FilterColumnTwo];
        for (int column = 0; column < columns.Length; column++)
        {
            columns[column].Children.Clear();
            foreach ((string heading, ShowOnly[] choices) in FilterColumns[column])
            {
                columns[column].Children.Add(FilterHeading(heading));
                foreach (ShowOnly choice in choices) columns[column].Children.Add(FilterChoice(choice));
            }
        }
    }

    /// <summary>A heading over a group of choices, in the accent colour.</summary>
    private static TextBlock FilterHeading(string textName)
    {
        var heading = new TextBlock { Text = Texts.Get(textName), FontSize = 12, Margin = new Thickness(10, 5, 10, 2) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        return heading;
    }

    /// <summary>A choice: chosen, it becomes the Files tab's Show only, and the dropdown lists those files.</summary>
    private RadioButton FilterChoice(ShowOnly choice)
    {
        string search = Files?.Search ?? "";
        var button = new RadioButton
        {
            Style = (Style)FindResource("PopupRadio"),
            GroupName = "FileFilter",
            Content = ChoiceName(choice),
            Tag = ListedFiles().Count(entry => FileListView.Shows(entry, choice, search)).ToString(Texts.Catalog.Culture),
            IsChecked = Files?.ShowOnly == choice,
        };
        button.Checked += (_, _) =>
        {
            if (fillingFileFilter || Files is null) return;
            Files.ShowOnly = choice;
            ShowPick();
        };
        return button;
    }

    /// <summary>A name was typed: after a pause, it becomes the Files tab's search, and the counts and the dropdown follow.</summary>
    private void FileNameFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (fillingFileFilter) return;
        if (namePause is null)
        {
            namePause = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            namePause.Tick += (_, _) =>
            {
                namePause.Stop();
                if (Files is null) return;
                Files.Search = FileNameFilter.Text;
                fillingFileFilter = true;
                FillFilterChoices();
                fillingFileFilter = false;
                ShowPick();
            };
        }
        namePause.Stop();
        namePause.Start();
    }

    /// <summary>A Show only choice's name, as the Files tab's dropdown has it.</summary>
    private static string ChoiceName(ShowOnly choice) => Texts.Get(choice switch
    {
        ShowOnly.HasPreset => "Files.HasAPreset",
        ShowOnly.NoPreset => "Files.NoPreset",
        ShowOnly.On => "Files.On",
        ShowOnly.Off => "Files.Off",
        ShowOnly.Changed => "Files.Changed",
        ShowOnly.ErrorsOrWarnings => "Files.ErrorsOrWarnings",
        ShowOnly.RuleChangedNothing => "Files.ARuleChangedNothing",
        ShowOnly.NotRead => "Files.NotRead",
        _ => "Files.AllFiles",
    });
}
