using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// The Source list of the Files and Units tabs: "Choose a Source…" until one is chosen, then the games, the
/// mods under a heading of their own, and "Choose a Game Folder…" for a game Steam did not find. Every list shows the
/// main window's one <see cref="SourceSelection"/>, so choosing in one chooses in all.
/// </summary>
public sealed class SourceChoice : ComboBox
{
    private SourceSelection? selection;

    /// <summary>While the list is being filled, its choice is not a user's.</summary>
    private bool filling;

    /// <summary>Builds the list; <see cref="Selection"/> gives it the sources.</summary>
    public SourceChoice()
    {
        // the app's dropdown look: a style set for ComboBox does not reach a class made from it by itself
        SetResourceReference(StyleProperty, typeof(ComboBox));
        SelectionChanged += Chosen;
        DropDownOpened += (_, _) => selection?.LookAgain();
        // the games are looked for the first time any Source list is shown
        IsVisibleChanged += (_, _) => { if (IsVisible) _ = selection?.LookOnceAsync(); };
        ToolTip = Texts.Get("Units.SourceChoice.Tip");
        Texts.LanguageChanged += () => { ToolTip = Texts.Get("Units.SourceChoice.Tip"); Fill(); };
    }

    /// <summary>The sources the list shows and chooses from.</summary>
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

    /// <summary>
    /// Fills the list: "Choose a Source…" until one is chosen, the games, then their mods under a heading, each mod with
    /// its game dimmed after it; last, "Choose a Game Folder…".
    /// </summary>
    private void Fill()
    {
        filling = true;
        Items.Clear();
        UnitSource? chosen = selection?.Chosen;
        IReadOnlyList<UnitSource> sources = selection?.Sources ?? [];
        if (chosen is null) Items.Add(new ComboBoxItem { Content = Texts.Get("Units.ChooseSource"), Tag = "none", IsEnabled = false });
        foreach (UnitSource game in sources.Where(source => source.Mod is null))
            Items.Add(new ComboBoxItem { Content = game.Name, Tag = game, ToolTip = game.Folder });
        List<UnitSource> mods = [.. sources.Where(source => source.Mod is not null)];
        if (mods.Count > 0)
        {
            Items.Add(new ComboBoxItem { Content = CategoryHeading(Texts.Get("Units.Mods")), IsEnabled = false });
            foreach (UnitSource mod in mods)
            {
                var game = new Run("  " + mod.Game.Name);
                game.SetResourceReference(TextElement.ForegroundProperty, "muted");
                // never bold: the box shows a picture of the chosen item, which the list draws bold while it is chosen
                var name = new TextBlock { Inlines = { new Run(mod.Name), game }, FontWeight = FontWeights.Normal };
                Items.Add(new ComboBoxItem { Content = name, Tag = mod, ToolTip = mod.Folder });
            }
        }
        Items.Add(new ComboBoxItem { Content = Texts.Get("Units.ChooseGameFolder"), Tag = "choose", ToolTip = Texts.Get("Units.ChooseGameFolder.Tip") });
        SelectedItem = chosen is null ? Items[0] : ItemOf(chosen);
        filling = false;
    }

    private ComboBoxItem? ItemOf(UnitSource source) =>
        Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is UnitSource listed && listed.Key == source.Key);

    private void Chosen(object sender, SelectionChangedEventArgs e)
    {
        if (filling || selection is null || SelectedItem is not ComboBoxItem item) return;
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
    public async void ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Texts.Get("Units.ChooseGameFolder.Title") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || selection is null) return;
        if (!await selection.AddFolder(dialog.FolderName))
            await MainView.Of(this).Messages.Tell(Texts.Get("Units.NotAGameFolder", ("folder", dialog.FolderName)));
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
