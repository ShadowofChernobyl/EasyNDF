using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Files;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Units tab: reads the units of a game or mod (the Source list), filters them (Find and Nations), and shows them
/// as cards or a table with the Unit pane, or side by side on Compare. This file reads the units and runs the filters;
/// UnitsPage.Views.cs fills the views, and UnitsPage.Menus.cs holds what the buttons and menus do.
/// </summary>
public partial class UnitsPage : UserControl
{
    /// <summary>The ways the units can be shown, as the buttons at the top right choose.</summary>
    private enum UnitView { Cards, Table, Compare }

    /// <summary>The units shown, with where they came from; null until a source has been read.</summary>
    private LoadedUnits? loaded;

    /// <summary>Counts the reads started, so a read that a newer one has overtaken is ignored when it ends.</summary>
    private int readsStarted;

    /// <summary>While a list (the roles, the divisions) is being filled, its choice is not a user's.</summary>
    private bool fillingLists;

    private readonly UnitFilter filter = new();
    private UnitView view = UnitView.Cards;

    /// <summary>Builds the tab. Nothing is read until it is first shown.</summary>
    public UnitsPage()
    {
        InitializeComponent();
        Panes.FoldedPlace = PaneSlot;
        Panes.SecondTitle = Texts.Get("Units.Pane.Title");
        selection.Changed += ShowUnitPane;
        // a right-click in the Unit pane, but not on a weapon, opens the unit's menu
        UnitPaneContent.MouseRightButtonUp += (_, e) =>
        {
            if (selection.Selected is not Unit unit) return;
            e.Handled = true;
            OpenUnitMenu(unit, UnitPaneContent);
        };
        IsVisibleChanged += (_, _) => TabShown();
        PreviewKeyDown += FoldKeys;
        Texts.LanguageChanged += LanguageChanged;
        CardsList.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(UnitListDoubleClicked), handledEventsToo: true);
        TableList.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(UnitListDoubleClicked), handledEventsToo: true);
        PrepareTableHeadings();
        // Cards or Table, whichever was used last; Compare, kept by an earlier version, counts as Cards
        view = (string?)AppData.Current.State["unitView"] == nameof(UnitView.Table) ? UnitView.Table : UnitView.Cards;
        ShowViewButtons();
    }

    /// <summary>The game the units shown are from; WARNO before any is read.</summary>
    public GameTitle Game => loaded?.Source.Game ?? GameTitle.Warno;

    /// <summary>
    /// The tab is shown: the source chosen on the Files tab is read if it is not shown yet, and the Unit pane is drawn
    /// again, as files added on the Files tab meanwhile change what its "Show in File Editor" can open.
    /// </summary>
    private void TabShown()
    {
        if (!IsVisible) return;
        ShowSource();
        ReadChosenSource();
        ShowUnitPane();
    }

    /// <summary>Another language: the names of sides, roles and nations are shown in it, the filters kept.</summary>
    private void LanguageChanged()
    {
        Panes.SecondTitle = Texts.Get("Units.Pane.Title");
        ShowSource();
        if (loaded is null) return;
        FillSideChoices();
        FillRoleChoice();
        FillDivisionChoice();
        ShowAll();
    }

    // ---- the source ----

    /// <summary>
    /// The game or mod chosen on the Files tab's Source bar. The units are read when it changes while this tab is
    /// shown, or when the tab is next shown, so choosing a source on the Files tab costs nothing until its units are wanted.
    /// </summary>
    public SourceSelection? Sources
    {
        get => sources;
        set
        {
            if (sources is not null) sources.Changed -= SourceChanged;
            sources = value;
            if (sources is not null) sources.Changed += SourceChanged;
        }
    }
    private SourceSelection? sources;

    private void SourceChanged()
    {
        ShowSource();
        if (IsVisible) ReadChosenSource();
    }

    /// <summary>
    /// Which source the units are from, in the heading: "Units in WTO x WiF", the game dimmed after a mod's name, with
    /// the folder as its tooltip; "Units" alone while none is chosen.
    /// </summary>
    private void ShowSource()
    {
        TitleText.Inlines.Clear();
        UnitSource? chosen = sources?.Chosen;
        TitleText.ToolTip = chosen?.Folder;
        if (chosen is null)
        {
            TitleText.Inlines.Add(new System.Windows.Documents.Run(Texts.Get("Main.Units")));
            return;
        }
        TitleText.Inlines.Add(new System.Windows.Documents.Run(Texts.Get("Units.Title", ("name", chosen.Name))));
        if (chosen.Mod is null) return;
        var game = new System.Windows.Documents.Run("  " + chosen.Game.Name) { FontWeight = FontWeights.Normal };
        game.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "muted");
        TitleText.Inlines.Add(game);
    }

    /// <summary>
    /// Reads the source chosen, unless its units are shown already. With none chosen, says to choose one on the Files
    /// tab: reading a game's units and pictures takes a while, so it only starts when the user asks.
    /// </summary>
    private void ReadChosenSource()
    {
        UnitSource? chosen = sources?.Chosen;
        if (chosen is null)
        {
            bool noGame = sources?.HasLooked == true && sources.Sources.Count == 0;
            ShowMessage(Texts.Get(noGame ? "Units.NoGame" : "Units.ChooseSourceFirst"), offerChooser: true);
            return;
        }
        if (loaded?.Source.Key == chosen.Key || readingKey == chosen.Key) return;
        _ = Read(chosen);
    }

    /// <summary>The source being read now, so it is not read twice at once.</summary>
    private string? readingKey;

    /// <summary>"Change…": the Files tab's Source list, opened.</summary>
    private void ChangeSourceClicked(object sender, RoutedEventArgs e) => MainView.Of(this).ShowSourceList();

    /// <summary>
    /// Reads a source's units in the background (a few seconds), then shows them with the filters cleared. The units
    /// compared and the unit selected belong to the source, so they are cleared too.
    /// </summary>
    private async Task Read(UnitSource source)
    {
        int mine = ++readsStarted;
        readingKey = source.Key;
        ShowMessage(Texts.Get("Units.Reading", ("name", source.Name)), offerChooser: false);
        ProblemsText.Visibility = Visibility.Collapsed;
        string cacheRoot = AppData.Current.Folders?.Cache ?? Path.Combine(Path.GetTempPath(), "EasyNDF", "Cache");
        LoadedUnits read;
        try
        {
            read = await Task.Run(() => UnitLibrary.Load(source, cacheRoot));
        }
        catch (Exception problem) when (problem is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (mine != readsStarted) return;
            readingKey = null;
            ShowMessage(Texts.Get("Units.CouldNotRead", ("name", source.Name), ("problem", problem.Message)), offerChooser: true);
            return;
        }
        if (mine != readsStarted) return;
        readingKey = null;

        loaded = read;
        // the source before is let go of, and what reading this one left behind (the game's archive lists, its
        // dictionaries, its unit files) is given back
        UnitPictureLibrary.Current = new UnitPictureLibrary(read.Pictures, read.Catalog.FlagSources);
        Memory.GiveBack();
        ProblemsText.Visibility = read.Problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProblemsText.ToolTip = string.Join("\n", read.Problems.Take(20));
        ResetFilters();
        compared.Clear();
        pinned = null;
        selection.Selected = null;
        MessageArea.Visibility = Visibility.Collapsed;
        ShowAll();
    }

    /// <summary>Shows a line where the units go: while reading, or when there is nothing to read; with a button to the Files tab's Source list when it helps.</summary>
    private void ShowMessage(string text, bool offerChooser)
    {
        MessageText.Text = text;
        ChooseFolderButton.Visibility = offerChooser ? Visibility.Visible : Visibility.Collapsed;
        MessageArea.Visibility = Visibility.Visible;
        CountText.Text = "";
    }

    // ---- the filters ----

    /// <summary>The filters as at first, for a source just read: every side, nation, role and division, and only units in a division.</summary>
    private void ResetFilters()
    {
        filter.Text = "";
        filter.Side = "";
        filter.Nations.Clear();
        filter.Role = "";
        filter.Division = "";
        filter.OnlyInDivision = true;
        filter.Factory = loaded is not null && loaded.Catalog.Units.Any(unit => unit.Factory == "Tanks") ? "Tanks" : "";
        foldedRoles.Clear();
        SearchBox.Text = "";
        OnlyInDivision.IsChecked = true;
        FillSideChoices();
        FillRoleChoice();
        FillDivisionChoice();
    }

    /// <summary>Both, and each side by its name, as buttons that stay pressed.</summary>
    private void FillSideChoices()
    {
        SideChoices.Children.Clear();
        foreach (string side in new[] { "" }.Concat(UnitNames.SidesOf(Game)))
        {
            var button = new ToggleButton
            {
                Content = side == "" ? Texts.Get("Units.BothSides") : UnitNames.Side(side), IsChecked = filter.Side == side,
                Style = (Style)FindResource("PressedToggle"), Padding = new Thickness(9, 1, 9, 1), Margin = new Thickness(SideChoices.Children.Count == 0 ? 0 : -1, 0, 0, 0),
                ToolTip = side == "" ? Texts.Get("Units.BothSides.Tip") : Texts.Get("Units.Side.Tip", ("side", UnitNames.Side(side))),
            };
            button.Click += (_, _) =>
            {
                filter.Side = side;
                // a division of the other side would leave nothing to show
                filter.Division = "";
                FillSideChoices();
                FillDivisionChoice();
                ShowAll();
            };
            SideChoices.Children.Add(button);
        }
    }

    /// <summary>All roles, then each role the units have, A to Z by name.</summary>
    private void FillRoleChoice()
    {
        fillingLists = true;
        RoleChoice.Items.Clear();
        RoleChoice.Items.Add(new ComboBoxItem { Content = Texts.Get("Units.AllRoles"), Tag = "" });
        IEnumerable<(string Code, string Name)> roles = (loaded?.Catalog.Units ?? [])
            .Where(unit => unit.Role != "")
            .GroupBy(unit => unit.Role)
            .Select(group => (group.Key, UnitNames.RoleOf(group.First())))
            .OrderBy(role => role.Item2, StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
        foreach ((string code, string name) in roles) RoleChoice.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        // the role chosen stays chosen when the list is filled again in another language
        RoleChoice.SelectedItem = RoleChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == filter.Role) ?? RoleChoice.Items[0];
        fillingLists = false;
    }

    /// <summary>All divisions, then each multiplayer division of the side chosen, A to Z.</summary>
    private void FillDivisionChoice()
    {
        fillingLists = true;
        DivisionChoice.Items.Clear();
        DivisionChoice.Items.Add(new ComboBoxItem { Content = Texts.Get("Units.AllDivisions"), Tag = "" });
        IEnumerable<string> divisions = (loaded?.Catalog.Units ?? [])
            .Where(unit => filter.Side == "" || unit.Side == filter.Side)
            .SelectMany(unit => unit.Divisions)
            .Distinct()
            .Order(StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
        foreach (string division in divisions) DivisionChoice.Items.Add(new ComboBoxItem { Content = division, Tag = division });
        DivisionChoice.SelectedItem = DivisionChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == filter.Division) ?? DivisionChoice.Items[0];
        fillingLists = false;
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        filter.Text = SearchBox.Text.Trim();
        if (loaded is not null) ShowAll();
    }

    private void RoleChosen(object sender, SelectionChangedEventArgs e)
    {
        if (fillingLists || RoleChoice.SelectedItem is not ComboBoxItem { Tag: string role }) return;
        filter.Role = role;
        ShowAll();
    }

    private void DivisionChosen(object sender, SelectionChangedEventArgs e)
    {
        if (fillingLists || DivisionChoice.SelectedItem is not ComboBoxItem { Tag: string division }) return;
        filter.Division = division;
        ShowAll();
    }

    private void OnlyInDivisionClicked(object sender, RoutedEventArgs e)
    {
        filter.OnlyInDivision = OnlyInDivision.IsChecked == true;
        if (loaded is not null) ShowAll();
    }

    /// <summary>"All": no nation ticked, which shows every nation.</summary>
    private void AllNationsClicked(object sender, RoutedEventArgs e)
    {
        filter.Nations.Clear();
        if (loaded is not null) ShowAll();
    }

    /// <summary>
    /// The nations, under their side's name: each with a tick and how many units it has with the other filters. A nation
    /// can fight on both sides (Romania in Steel Division 2), so each side lists its own. Ticking nations shows only them.
    /// </summary>
    private void FillNationList()
    {
        NationList.Children.Clear();
        if (loaded is null) return;
        IReadOnlyList<string> sides = UnitNames.SidesOf(Game);
        foreach (string side in sides.Where(side => filter.Side == "" || filter.Side == side))
        {
            NationList.Children.Add(SideHeading(side, side == sides[0]));
            Dictionary<string, int> counts = filter.NationCounts(loaded.Catalog.Units, side);
            IEnumerable<string> nations = loaded.Catalog.Units.Where(unit => unit.Side == side).Select(unit => unit.Nation).Distinct()
                .OrderBy(UnitNames.Nation, StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
            foreach (string nation in nations) NationList.Children.Add(NationRow(nation, counts.GetValueOrDefault(nation)));
        }
    }

    /// <summary>A side's heading in the nation list: bold, on the panel colour, with a stripe of the side's colour.</summary>
    private static Bevel SideHeading(string side, bool firstSide)
    {
        var stripe = new System.Windows.Shapes.Rectangle { Width = 3, Margin = new Thickness(0, 0, 6, 0) };
        stripe.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, firstSide ? "side-a" : "side-b");
        var heading = new DockPanel { Children = { stripe, new TextBlock { Text = UnitNames.Side(side), FontWeight = FontWeights.Bold } } };
        var row = new Bevel { Child = heading, Padding = new Thickness(8, 3, 8, 3), Edges = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Bevel.BottomRightProperty, "line");
        row.SetResourceReference(Bevel.BackgroundProperty, "panel");
        return row;
    }

    /// <summary>A nation's row: its tick, name and count; a click ticks or unticks it.</summary>
    private Bevel NationRow(string nation, int count)
    {
        var tick = new CheckBox { Style = (Style)FindResource("Tick"), IsChecked = filter.Nations.Contains(nation), IsHitTestVisible = false, Margin = new Thickness(0, 0, 6, 0) };
        var countText = new TextBlock { Text = Texts.Number(count), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        countText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(countText, Dock.Right);
        var name = new TextBlock { Text = UnitNames.Nation(nation), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var row = new Bevel { Child = new DockPanel { Children = { tick, countText, name } }, Padding = new Thickness(8, 3, 8, 3), Edges = new Thickness(0, 0, 0, 1), Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
        DockPanel.SetDock(tick, Dock.Left);
        row.SetResourceReference(Bevel.BottomRightProperty, "line");
        row.MouseEnter += (_, _) => row.SetResourceReference(Bevel.BackgroundProperty, "sel");
        row.MouseLeave += (_, _) => row.Background = System.Windows.Media.Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (!filter.Nations.Remove(nation)) filter.Nations.Add(nation);
            ShowAll();
        };
        return row;
    }

    /// <summary>
    /// The factory tabs: ALL, then each factory of the game, each with how many units it would show with the other
    /// filters. Compare has none.
    /// </summary>
    private void FillFactoryTabs()
    {
        FactoryTabs.Children.Clear();
        if (loaded is null) return;
        Dictionary<string, int> counts = filter.FactoryCounts(loaded.Catalog.Units);
        FactoryTabs.Children.Add(FactoryTab("", Texts.Get("Units.Factory.All.Short"), Texts.Get("Units.Factory.All"), counts.Values.Sum()));
        foreach (string factory in UnitNames.FactoriesOf(Game))
            FactoryTabs.Children.Add(FactoryTab(factory, UnitNames.FactoryShort(factory), UnitNames.Factory(factory), counts.GetValueOrDefault(factory)));
    }

    private ToggleButton FactoryTab(string factory, string shortName, string name, int count)
    {
        var countText = new System.Windows.Documents.Run(" " + Texts.Number(count)) { FontWeight = FontWeights.Normal, FontSize = 11 };
        countText.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "muted");
        var label = new TextBlock { Inlines = { new System.Windows.Documents.Run(shortName), countText } };
        var tab = new ToggleButton { Content = label, Style = (Style)FindResource("FactoryTab"), IsChecked = filter.Factory == factory, ToolTip = name };
        tab.Click += (_, _) =>
        {
            filter.Factory = factory;
            ShowAll();
        };
        return tab;
    }

    // ---- the view ----

    private void ViewClicked(object sender, RoutedEventArgs e)
    {
        view = sender == TableButton ? UnitView.Table : sender == CompareButton ? UnitView.Compare : UnitView.Cards;
        // only Cards or Table is kept for next time: Compare starts empty, so the app never opens on it
        if (view != UnitView.Compare)
        {
            AppData.Current.State["unitView"] = view.ToString();
            AppData.Current.SaveState();
        }
        ShowViewButtons();
        if (loaded is not null) ShowAll();
    }

    /// <summary>The view chosen stays pressed; + Add Units… is only on Compare.</summary>
    private void ShowViewButtons()
    {
        CardsButton.IsChecked = view == UnitView.Cards;
        TableButton.IsChecked = view == UnitView.Table;
        CompareButton.IsChecked = view == UnitView.Compare;
        AddUnitsButton.Visibility = view == UnitView.Compare ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Shows everything again after a filter, the view or the source changed: the nations, the factory tabs and the view.</summary>
    private void ShowAll()
    {
        if (loaded is null) return;
        FillNationList();
        FillFactoryTabs();
        bool compare = view == UnitView.Compare;
        Panes.Visibility = compare ? Visibility.Collapsed : Visibility.Visible;
        CompareBox.Visibility = compare ? Visibility.Visible : Visibility.Collapsed;
        CardsList.Visibility = view == UnitView.Cards ? Visibility.Visible : Visibility.Collapsed;
        TableList.Visibility = view == UnitView.Table ? Visibility.Visible : Visibility.Collapsed;
        shown = filter.Apply(loaded.Catalog.Units);
        if (view == UnitView.Cards) ShowCards();
        else if (view == UnitView.Table) ShowTable();
        else ShowCompare();
        ShowUnitPane();
        Footnote.Text = compare
            ? Texts.Get("Units.Footnote.Compare") + " " + Texts.Get(Game.RangeUnit == "GRU" ? "Units.Footnote.RangeGru" : "Units.Footnote.RangeMetres")
            : Texts.Get("Units.Footnote.List");
        CountText.Text = compare ? Texts.Count("Units.Comparing", compared.Count) : Texts.CountWide("Units.Shown", shown.Count);
    }
}
