using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Files;

/// <summary>
/// The Source bar at the top of the Files tab: a raised strip with the game or mod the app works with, its folder,
/// how many of its files the list holds, and Add Its Files. The list shows "Choose a Source…" until one is chosen,
/// then the games, the mods under a heading of their own, and "Choose a Game Folder…" for a game Steam did not find.
/// </summary>
public sealed class SourceBar : Bevel
{
    // a set width: a dropdown otherwise grows to its widest item, and a mod's long name stretched it across the bar
    private readonly ComboBox list = new() { Width = 260, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    private readonly TextBlock path = new() { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock listedCount = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    private readonly Button addButton = new();
    private readonly TextBlock label = new() { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    private SourceSelection? selection;

    /// <summary>While the list is being filled, its choice is not a user's.</summary>
    private bool filling;

    /// <summary>Builds the bar; <see cref="Selection"/> gives it the sources.</summary>
    public SourceBar()
    {
        SetResourceReference(TopLeftProperty, "hi");
        SetResourceReference(BottomRightProperty, "lo");
        SetResourceReference(BackgroundProperty, "panel");
        Padding = new Thickness(8, 5, 8, 5);
        Margin = new Thickness(0, 0, 0, 8);

        label.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        list.SelectionChanged += Chosen;
        list.DropDownOpened += (_, _) => selection?.LookAgain();
        path.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        listedCount.SetResourceReference(TextBlock.ForegroundProperty, "ok");
        addButton.IsEnabled = false;
        addButton.Click += (_, _) => AddFilesRequested?.Invoke();

        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { listedCount, addButton } };
        DockPanel.SetDock(right, Dock.Right);
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(list, Dock.Left);
        Child = new DockPanel { Children = { right, label, list, path } };
        ShowTexts();
        Texts.LanguageChanged += () => { ShowTexts(); Fill(); };
    }

    /// <summary>The bar's own words, in the language in use.</summary>
    private void ShowTexts()
    {
        label.Text = Texts.Get("Files.Source");
        list.ToolTip = Texts.Get("Files.Source.Tip");
        addButton.Content = Texts.Get("Files.Source.AddItsFiles");
        addButton.ToolTip = Texts.Get("Files.Source.AddItsFiles.Tip");
    }

    /// <summary>Raised by Add Its Files.</summary>
    public event Action? AddFilesRequested;

    /// <summary>The sources the bar shows and chooses from.</summary>
    public SourceSelection? Selection
    {
        get => selection;
        set
        {
            if (selection is not null) selection.Changed -= Fill;
            selection = value;
            if (selection is not null) selection.Changed += Fill;
            Fill();
        }
    }

    /// <summary>"✓ 17 of its files listed": how many files of the source the list holds; nothing for none.</summary>
    public void ShowListedCount(int count) => listedCount.Text = count > 0 ? Texts.CountWide("Files.Source.Listed", count) : "";

    /// <summary>Opens the list, as the Units tab's "Change…" does.</summary>
    public void OpenList()
    {
        list.Focus();
        list.IsDropDownOpen = true;
    }

    /// <summary>
    /// Fills the list: "Choose a Source…" until one is chosen, the games, then their mods under a heading, each mod with
    /// its game dimmed after it; last, "Choose a Game Folder…".
    /// </summary>
    private void Fill()
    {
        filling = true;
        list.Items.Clear();
        UnitSource? chosen = selection?.Chosen;
        IReadOnlyList<UnitSource> sources = selection?.Sources ?? [];
        if (chosen is null)
        {
            string first = selection?.HasLooked == false ? Texts.Get("Files.Source.Looking") : Texts.Get("Files.Source.Choose");
            list.Items.Add(new ComboBoxItem { Content = first, Tag = "none", IsEnabled = false });
        }
        foreach (UnitSource game in sources.Where(source => source.Mod is null))
            list.Items.Add(new ComboBoxItem { Content = game.Name, Tag = game, ToolTip = game.Folder });
        List<UnitSource> mods = [.. sources.Where(source => source.Mod is not null)];
        if (mods.Count > 0)
        {
            list.Items.Add(new ComboBoxItem { Content = CategoryHeading(Texts.Get("Files.Source.Mods")), IsEnabled = false });
            foreach (UnitSource mod in mods)
            {
                var game = new Run("  " + mod.Game.Name);
                game.SetResourceReference(TextElement.ForegroundProperty, "muted");
                // never bold: the box shows a picture of the chosen item, which the list draws bold while it is chosen
                var name = new TextBlock { Inlines = { new Run(mod.Name), game }, FontWeight = FontWeights.Normal };
                list.Items.Add(new ComboBoxItem { Content = name, Tag = mod, ToolTip = mod.Folder });
            }
        }
        list.Items.Add(new ComboBoxItem { Content = Texts.Get("Files.Source.ChooseFolder"), Tag = "choose", ToolTip = Texts.Get("Files.Source.ChooseFolder.Tip") });
        list.SelectedItem = chosen is null ? list.Items[0] : ItemOf(chosen);
        path.Text = chosen?.Folder ?? "";
        path.ToolTip = chosen?.Folder;
        addButton.IsEnabled = chosen is not null;
        filling = false;
    }

    private ComboBoxItem? ItemOf(UnitSource source) =>
        list.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is UnitSource listed && listed.Key == source.Key);

    private void Chosen(object sender, SelectionChangedEventArgs e)
    {
        if (filling || selection is null || list.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is UnitSource source)
        {
            selection.Choose(source);
            return;
        }
        if (item.Tag is "choose")
        {
            // the list goes back to the source chosen, whether or not a folder is picked
            Fill();
            ChooseFolder();
        }
    }

    /// <summary>
    /// "Choose a Game Folder…": for a game Steam did not find. The folder may be the game's, or a mod's in its Mods
    /// folder; the game and its mods are then listed, kept for next time, and the folder picked is chosen.
    /// </summary>
    private async void ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Texts.Get("Files.Source.ChooseFolder.Title") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || selection is null) return;
        if (!await selection.AddFolder(dialog.FolderName))
            await MainView.Of(this).Messages.Tell(Texts.Get("Files.Source.NotAGameFolder", ("folder", dialog.FolderName)));
    }

    /// <summary>
    /// A category's heading in the list, such as "Mods": its name in the middle, bold and dim, with a dashed line on
    /// each side reaching the list's edges, so it reads as the start of a group rather than something to choose.
    /// </summary>
    private static Grid CategoryHeading(string name)
    {
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = name, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 8, 0) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        Grid.SetColumn(text, 1);
        heading.Children.Add(text);
        foreach (int column in new[] { 0, 2 })
        {
            var line = new System.Windows.Shapes.Rectangle { Height = 1, StrokeThickness = 1, StrokeDashArray = [3, 2], VerticalAlignment = VerticalAlignment.Center, MinWidth = 12 };
            line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "muted");
            Grid.SetColumn(line, column);
            heading.Children.Add(line);
        }
        return heading;
    }
}
