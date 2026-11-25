using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EasyNdf.App.Files;
using EasyNdf.App.Text;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Controls;

/// <summary>
/// The file filter's funnel for a dropdown of files outside the Editor tab: the same panel as the Editor tab's (find by
/// name; rules, switch, last run, reading), on the same filter, which is the Files tab's search and Show only. The
/// dropdown beside it lists the files <see cref="LetsThrough"/> passes; <see cref="Changed"/> says when to list again.
/// </summary>
public sealed class FileFilterFunnel : Grid
{
    private static readonly (string Heading, ShowOnly[] Choices)[][] Columns =
    [
        [("Editor.FileFilter.Rules", [ShowOnly.All, ShowOnly.HasPreset, ShowOnly.NoPreset]), ("Editor.FileFilter.Switch", [ShowOnly.On, ShowOnly.Off])],
        [("Editor.FileFilter.LastRun", [ShowOnly.Changed, ShowOnly.ErrorsOrWarnings, ShowOnly.RuleChangedNothing]), ("Editor.FileFilter.Reading", [ShowOnly.NotRead])],
    ];

    private readonly Func<FileListModel?> files;
    private readonly ToggleButton button;
    private readonly Popup popup;
    private readonly TextBox name = new() { Margin = new Thickness(8, 0, 8, 4) };
    private readonly StackPanel[] columns = [new() { MinWidth = 200, Margin = new Thickness(0, 0, 0, 6) }, new() { MinWidth = 200 }];
    private DispatcherTimer? namePause;
    private bool filling;

    /// <summary>The filter changed here: the dropdown beside the funnel lists its files again.</summary>
    public event Action? Changed;

    /// <summary>Builds the funnel on the Files tab's list.</summary>
    public FileFilterFunnel(Func<FileListModel?> files)
    {
        this.files = files;
        var funnel = new Path { Data = Geometry.Parse("M 1,2 H 11 L 7.2,6.6 V 10.5 L 4.8,9.3 V 6.6 Z"), StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round, Width = 12, Height = 12 };
        button = new ToggleButton { Padding = new Thickness(6, 0, 6, 0), Content = funnel };
        button.SetResourceReference(StyleProperty, "PressedToggle");
        funnel.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
        System.Windows.Automation.AutomationProperties.SetName(button, Texts.Get("Editor.FileFilter.Name"));
        button.Click += (_, _) => Toggle();

        name.Tag = Texts.Get("Editor.FileFilter.NameHint");
        name.ToolTip = Texts.Get("Editor.FileFilter.Name.Tip");
        name.TextChanged += (_, _) => NameTyped();
        var choices = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } } };
        var second = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Margin = new Thickness(0, 0, 0, 6), Child = columns[1] };
        second.SetResourceReference(Border.BorderBrushProperty, "line");
        SetColumn(second, 1);
        choices.Children.Add(columns[0]);
        choices.Children.Add(second);
        var shared = new TextBlock { Text = Texts.Get("Editor.FileFilter.Shared"), FontSize = 11, Margin = new Thickness(12, 5, 12, 5) };
        shared.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var foot = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = shared };
        foot.SetResourceReference(Border.BorderBrushProperty, "line");
        var panel = new Bevel { Padding = new Thickness(0, 8, 0, 0), Child = new StackPanel { Children = { name, choices, foot } } };
        panel.SetResourceReference(Bevel.TopLeftProperty, "hi");
        panel.SetResourceReference(Bevel.BottomRightProperty, "lo");
        panel.SetResourceReference(Bevel.BackgroundProperty, "panel");
        panel.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "ui-font");
        System.Windows.Documents.TextElement.SetFontSize(panel, 12.5);
        popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, VerticalOffset = 2, StaysOpen = false, AllowsTransparency = true, Child = panel };
        popup.Closed += (_, _) => ShowButton();
        // Escape closes the panel only, not the popup the funnel is in
        panel.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            popup.IsOpen = false;
            e.Handled = true;
            button.Focus();
        };

        Children.Add(button);
        Children.Add(popup);
        ShowButton();
    }

    /// <summary>Whether the filter lets a file into a dropdown: the Files tab's Show only and search.</summary>
    public static bool LetsThrough(FileListModel? files, FileEntry entry) => files is null || FileListView.Shows(entry, files.ShowOnly, files.Search);

    private IEnumerable<FileEntry> Listed() => (files()?.Entries ?? []).Where(entry => entry.Source is not null);

    /// <summary>The funnel, pressed in while a filter is on, with a tooltip saying which and how many files it lists.</summary>
    public void ShowButton()
    {
        FileListModel? list = files();
        bool on = list is not null && (list.ShowOnly != ShowOnly.All || list.Search != "");
        button.IsChecked = on;
        if (list is null || !on)
        {
            button.ToolTip = Texts.Get("Editor.FileFilter.Tip");
            return;
        }
        string choice = list.Search == "" ? ChoiceName(list.ShowOnly)
            : list.ShowOnly == ShowOnly.All ? Texts.Get("Editor.FileFilter.Named", ("name", list.SearchText.Trim()))
            : Texts.Get("Editor.FileFilter.ChoiceAndName", ("choice", ChoiceName(list.ShowOnly)), ("name", list.SearchText.Trim()));
        button.ToolTip = Texts.Get("Editor.FileFilter.On.Tip", ("choice", choice), ("listed", Listed().Count(entry => LetsThrough(list, entry))), ("all", Listed().Count()));
    }

    private void Toggle()
    {
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }
        filling = true;
        name.Text = files()?.SearchText ?? "";
        filling = false;
        FillChoices();
        popup.IsOpen = true;
        name.Focus();
    }

    private void FillChoices()
    {
        for (int column = 0; column < columns.Length; column++)
        {
            columns[column].Children.Clear();
            foreach ((string heading, ShowOnly[] choices) in Columns[column])
            {
                var text = new TextBlock { Text = Texts.Get(heading), FontSize = 12, Margin = new Thickness(10, 5, 10, 2) };
                text.SetResourceReference(TextBlock.ForegroundProperty, "accent");
                columns[column].Children.Add(text);
                foreach (ShowOnly choice in choices) columns[column].Children.Add(Choice(choice));
            }
        }
    }

    private RadioButton Choice(ShowOnly choice)
    {
        FileListModel? list = files();
        string search = list?.Search ?? "";
        var radio = new RadioButton
        {
            Style = (Style)FindResource("PopupRadio"),
            GroupName = "FileFilterFunnel" + GetHashCode(),
            Content = ChoiceName(choice),
            Tag = Listed().Count(entry => FileListView.Shows(entry, choice, search)).ToString(Texts.Catalog.Culture),
            IsChecked = list?.ShowOnly == choice,
        };
        radio.Checked += (_, _) =>
        {
            if (filling || files() is not FileListModel now) return;
            now.ShowOnly = choice;
            Changed?.Invoke();
        };
        return radio;
    }

    /// <summary>A name was typed: after a pause it becomes the Files tab's search, and the counts and the dropdown follow.</summary>
    private void NameTyped()
    {
        if (filling) return;
        if (namePause is null)
        {
            namePause = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            namePause.Tick += (_, _) =>
            {
                namePause.Stop();
                if (files() is not FileListModel list) return;
                list.Search = name.Text;
                filling = true;
                FillChoices();
                filling = false;
                Changed?.Invoke();
            };
        }
        namePause.Stop();
        namePause.Start();
    }

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
