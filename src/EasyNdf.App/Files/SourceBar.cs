using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;

namespace EasyNdf.App.Files;

/// <summary>
/// The Source bar at the top of the Files tab: a raised strip with the game or mod the app works with (the same Source
/// list as on the Units and Divisions tabs, so choosing here chooses there), its folder, how many of its files the list
/// holds, and Add Its Files.
/// </summary>
public sealed class SourceBar : Bevel
{
    // a set width: a dropdown otherwise grows to its widest item, and a mod's long name stretched it across the bar
    private readonly SourceChoice list = new() { Width = 260, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    private readonly TextBlock path = new() { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock listedCount = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    private readonly Button addButton = new() { IsEnabled = false };
    private readonly TextBlock label = new() { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Builds the bar; <see cref="Selection"/> gives it the sources.</summary>
    public SourceBar()
    {
        SetResourceReference(TopLeftProperty, "hi");
        SetResourceReference(BottomRightProperty, "lo");
        SetResourceReference(BackgroundProperty, "panel");
        Padding = new Thickness(8, 5, 8, 5);
        Margin = new Thickness(0, 0, 0, 8);

        label.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        path.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        listedCount.SetResourceReference(TextBlock.ForegroundProperty, "ok");
        addButton.Click += (_, _) => AddFilesRequested?.Invoke();

        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { listedCount, addButton } };
        DockPanel.SetDock(right, Dock.Right);
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(list, Dock.Left);
        Child = new DockPanel { Children = { right, label, list, path } };
        ShowTexts();
        Texts.LanguageChanged += ShowTexts;
    }

    /// <summary>The bar's own words, in the language in use.</summary>
    private void ShowTexts()
    {
        label.Text = Texts.Get("Files.Source");
        addButton.Content = Texts.Get("Files.Source.AddItsFiles");
        addButton.ToolTip = Texts.Get("Files.Source.AddItsFiles.Tip");
    }

    /// <summary>Raised by Add Its Files.</summary>
    public event Action? AddFilesRequested;

    /// <summary>The sources the bar shows and chooses from: the main window's, shared with the other tabs.</summary>
    public SourceSelection? Selection
    {
        get => list.Selection;
        set
        {
            if (list.Selection is not null) list.Selection.Changed -= ShowChosen;
            list.Selection = value;
            if (value is not null) value.Changed += ShowChosen;
            ShowChosen();
        }
    }

    /// <summary>The source's folder after the list; Add Its Files only once one is chosen.</summary>
    private void ShowChosen()
    {
        string? folder = list.Selection?.Chosen?.Folder;
        path.Text = folder ?? "";
        path.ToolTip = folder;
        addButton.IsEnabled = folder is not null;
    }

    /// <summary>"✓ 17 of its files listed": how many files of the source the list holds; nothing for none.</summary>
    public void ShowListedCount(int count) => listedCount.Text = count > 0 ? Texts.CountWide("Files.Source.Listed", count) : "";
}
