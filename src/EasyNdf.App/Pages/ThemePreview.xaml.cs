using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using EasyNdf.App.Controls;
using EasyNdf.App.Editor;
using EasyNdf.Engine.Editing;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Theme Designer's preview (the HTML app's <c>#thPrev</c>): the samples are the HTML app's own (<c>PV_TEXT</c>,
/// <c>PV_ROWS</c> and the lines of <c>pvDiffPaint</c>).
/// </summary>
public partial class ThemePreview : UserControl
{
    /// <summary>The sample NDF text. Every editor colour appears in the lines seen without scrolling.</summary>
    private static readonly string SampleText = string.Join("\n",
        "// A made-up descriptor. Every editor color appears in the lines you can see without scrolling.",
        "template Example_Template [ Range : float = <DefaultRange> ]",
        "export Descriptor_Unit_Example is TEntityDescriptor",
        "(",
        "    DescriptorId       = GUID:{00000000-0000-0000-0000-000000000000}",
        "    ClassNameForDebug  = 'Unit_Example'",
        "    Coalition          = TCoalition_NATO",
        "    MaxSpeedInKmph     = 65.5",
        "    IsTransport        = True",
        "    Weapon             = $/GFX/Weapon/WeaponDescriptor_Example",
        "    ModulesDescriptors = [ ~/TagsModuleDescriptor, ]",
        "    VisionRange        = MAP[ (EVisionRange/Standard, 3500.0) ]",
        "    /* a block comment",
        "       over two lines */",
        ")",
        "",
        "// Nothing new from here down: more of the same, so there is something to scroll.",
        "export Descriptor_Unit_Example_CMD is TEntityDescriptor",
        "(",
        "    DescriptorId       = GUID:{11111111-1111-1111-1111-111111111111}",
        "    ClassNameForDebug  = 'Unit_Example_CMD'",
        "    Coalition          = TCoalition_NATO",
        "    MaxSpeedInKmph     = 70.0",
        "    IsTransport        = False",
        "    IsCommandUnit      = True",
        "    Weapon             = $/GFX/Weapon/WeaponDescriptor_Example_CMD",
        "    ModulesDescriptors = [ ~/TagsModuleDescriptor, ~/CommandModuleDescriptor, ]",
        "    VisionRange        = MAP[ (EVisionRange/Standard, 4200.0) ]",
        "    FuelCapacity       = 520",
        "    Autonomy           = 480.5",
        "    // a line comment in the middle",
        "    UpgradeFrom        = ~/Descriptor_Unit_Example",
        ")");

    /// <summary>The sample of "Changes only": each line's number, its mark and its text.</summary>
    private static readonly (string Number, LineMark Mark, string Text)[] SampleChangeLines =
    [
        ("11", LineMark.Same, "    IsCommandUnit      = True"),
        ("12", LineMark.Removed, "    FuelCapacity       = 520"),
        ("12", LineMark.Added, "    FuelCapacity       = 650"),
        ("13", LineMark.Removed, "    Autonomy           = 480.5"),
        ("13", LineMark.Added, "    Autonomy           = 600.6"),
        ("14", LineMark.Same, "    // a line comment in the middle"),
        ("", LineMark.Gap, "⋯"),
        ("31", LineMark.Same, "    MaxSpeedInKmph     = 70.0"),
        ("32", LineMark.Added, "    UpgradeFrom        = ~/Descriptor_Unit_Example"),
    ];

    /// <summary>A row of the sample list: its look ("" plain, "sel" selected, "off" switched off), its cells, and its status colour.</summary>
    private sealed record SampleRow(string Look, string File, string Size, string Preset, string StatusColour, string Status);

    // the rows and headings are made when they are drawn, so they are in the language in use
    private static SampleRow[] SampleRows =>
    [
        new("", "Ammunition.ndf", "3.1 MB", "WTO (Ammunition.ndf)", "ok", Texts.Get("Settings.Preview.Exported", ("edits", Texts.Number(3290)))),
        new("sel", "UniteDescriptor.ndf", "27.2 MB", "WTO (UniteDescriptor.ndf)", "muted", Texts.Get("Settings.Preview.Selected")),
        new("", "MissileDescriptors.ndf", "637 KB", "WTO (MissileDescriptors.ndf)", "caution", Texts.Get("Settings.Preview.ChangedNothing", ("edits", 408))),
        new("", "WeaponDescriptor.ndf", "14.6 MB", "WTO (WeaponDescriptor.ndf)", "warn", Texts.Get("Settings.Preview.NotValid")),
        new("off", "Terrains.ndf", "11 KB", Texts.Get("Settings.Preview.NoPreset"), "muted", Texts.Get("Settings.Preview.Off")),
    ];

    /// <summary>The sample list's columns, in order; the first is always shown.</summary>
    private static string[] ColumnHeadings =>
        [Texts.Get("Files.Column.File"), Texts.Get("Files.Column.Size"), Texts.Get("Files.Column.Preset"), Texts.Get("Files.Column.Status")];

    /// <summary>The columns the user has hidden in the sample list, by their place in <see cref="ColumnHeadings"/>.</summary>
    private readonly HashSet<int> hiddenColumns = [];

    /// <summary>What the sample editor's text belongs to, so it keeps its undo steps while the page is drawn again.</summary>
    private readonly JsonObject sampleOwner = [];

    /// <summary>Builds the preview with its samples.</summary>
    public ThemePreview()
    {
        InitializeComponent();
        ShowSampleList();
        ShowSampleEditor(SampleText);
        SampleChanges.Content = new ChangesSample(SampleChangeLines);
    }

    /// <summary>"a link": opens in the browser, as the HTML app's opens in a new tab.</summary>
    private void LinkClicked(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    // ---- the sample list ----

    /// <summary>Draws the sample list with the columns that are shown (the HTML app's <c>renderPvTable</c>).</summary>
    private void ShowSampleList()
    {
        string[] headings = ColumnHeadings;
        List<int> shown = [.. Enumerable.Range(0, headings.Length).Where(column => !hiddenColumns.Contains(column))];
        var table = new WebTable([.. shown.Select(column => headings[column])], [.. shown.Select(column => column == 0)]);
        foreach (SampleRow sample in SampleRows)
        {
            string[] cells = [sample.File, sample.Size, sample.Preset, sample.Status];
            WebTableRow row = table.AddRow([.. shown.Select(column => cells[column])]);
            row.IsSelected = sample.Look == "sel";
            for (int place = 0; place < shown.Count; place++)
            {
                bool isStatus = shown[place] == 3;
                if (sample.Look == "off") row.SetColour(place, "muted");
                else if (isStatus) row.SetColour(place, sample.StatusColour);
            }
        }
        SampleList.Child = table;
    }

    /// <summary>"Columns": a checkbox for each column of the sample list; the file's is always on.</summary>
    private void ColumnsClicked(object sender, RoutedEventArgs e)
    {
        if (ColumnsPopup.IsOpen) { ColumnsPopup.IsOpen = false; return; }
        ColumnChoices.Children.Clear();
        string[] headings = ColumnHeadings;
        for (int place = 0; place < headings.Length; place++)
        {
            int column = place;
            var choice = new CheckBox
            {
                Style = (Style)FindResource("PopupChoice"),
                Content = headings[column],
                IsChecked = !hiddenColumns.Contains(column),
                IsEnabled = column != 0,
                ToolTip = Texts.Get("Files.Column.ShowOrHide.Tip"),
                Margin = new Thickness(0, ColumnChoices.Children.Count == 0 ? 0 : 5, 0, 0),
            };
            choice.Click += (_, _) =>
            {
                if (choice.IsChecked == true) hiddenColumns.Remove(column);
                else hiddenColumns.Add(column);
                ShowSampleList();
            };
            ColumnChoices.Children.Add(choice);
        }
        ColumnsPopup.IsOpen = true;
    }

    // ---- the sample editor ----

    /// <summary>The sample editor; the wheel scrolls the page over it until it is clicked into.</summary>
    private void ShowSampleEditor(string text)
    {
        var editor = new NdfTextBox(text, sampleOwner, isSample: true);
        Controls.SmoothScrolling.SetScrollsOnceClicked(editor, true);
        SampleEditor.Content = editor;
    }

    /// <summary>"Reset text": the sample text as it was.</summary>
    private void ResetTextClicked(object sender, RoutedEventArgs e)
    {
        if (SampleEditor.Content is NdfTextBox box) box.Replace(SampleText);
    }
}
