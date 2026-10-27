using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Armory tab: reads the units of a game or mod (the Source list), filters them (Find and Nations), and shows them
/// as cards or a table with the Unit pane, or side by side on Compare, under three sub-tabs (UnitsPage.ArmoryParts.cs).
/// This file reads the units and runs the filters; UnitsPage.Views.cs fills the views, and UnitsPage.Menus.cs holds
/// what the buttons and menus do.
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
        PreparePaneButtons();
        selection.Changed += ShowUnitPane;
        IsVisibleChanged += (_, _) => ShowReadingCover();
        // a right-click in the Unit pane, but not on a weapon, opens the unit's menu
        UnitPaneContent.MouseRightButtonUp += (_, e) =>
        {
            if (selection.Selected is not Unit unit) return;
            e.Handled = true;
            OpenUnitMenu(unit, UnitPaneContent);
        };
        IsVisibleChanged += (_, _) => TabShown();
        ShowSource();
        PreviewKeyDown += FoldKeys;
        PreviewKeyDown += ZoomKeys;
        PreviewKeyDown += SelectAllKey;
        PreviewKeyDown += DeleteKey;
        // the page takes the keys after a click on a unit, for Ctrl+A to reach it (the lists themselves take no focus)
        Focusable = true;
        IsTabStop = false;
        FocusVisualStyle = null;
        Texts.LanguageChanged += LanguageChanged;
        CardsList.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(UnitListDoubleClicked), handledEventsToo: true);
        TableList.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(UnitListDoubleClicked), handledEventsToo: true);
        PrepareZoom();
        PrepareDivisions();
        PrepareParts();
        PrepareModValues();
        // Cards or Table, whichever was used last; Compare, kept by an earlier version, counts as Cards
        view = (string?)AppData.Current.State["unitView"] == nameof(UnitView.Table) ? UnitView.Table : UnitView.Cards;
        ShowViewButtons();
    }

    /// <summary>The game the units shown are from; WARNO before any is read.</summary>
    public GameTitle Game => loaded?.Source.Game ?? GameTitle.Warno;

    /// <summary>
    /// The tab is shown: the first time, the games are looked for; each time, the Unit pane is drawn again, as files
    /// added on the Files tab meanwhile change what its "Show in File Editor" can open.
    /// </summary>
    private void TabShown()
    {
        if (!IsVisible) return;
        if (!started) _ = LookForGames();
        if (redrawWhenShown && loaded is not null)
        {
            redrawWhenShown = false;
            ShowAll();
        }
        // what the other tabs changed meanwhile (files added, say) is not all told to this one: the views kept for the
        // sub-tabs are drawn again when next shown
        ShownThingsChanged();
        ShowUnitPane();
        ModValuesMayHaveChanged();
    }

    /// <summary>Another language: the names of sides, roles and nations are shown in it, the filters kept.</summary>
    private void LanguageChanged()
    {
        ShowPaneButtons();
        ShowSource();
        if (loaded is null) return;
        FillSideChoices();
        FillRoleChoice();
        ShowAll();
    }

    // ---- the source ----

    /// <summary>The games and mods found, and the one chosen in a Source list: the main window's, shared with the Files tab.</summary>
    private SourceSelection sources = new();

    /// <summary>Shows and reads the main window's source, which the Files tab's list chooses too.</summary>
    public void UseSources(SourceSelection shared)
    {
        sources.Changed -= SourceChanged;
        sources = shared;
        sources.Changed += SourceChanged;
        SourceChoice.Selection = shared;
        WeaponsView.UseSources(shared);
        ShowSource();
    }

    /// <summary>Whether the games have been looked for: the first time the tab is shown, not when the app starts.</summary>
    private bool started;

    /// <summary>The read started last, so the checks can wait for it.</summary>
    private Task lastRead = Task.CompletedTask;

    /// <summary>The source being read now, so it is not read twice at once.</summary>
    private string? readingKey;

    /// <summary>
    /// The first time the tab is shown: finds the games and their mods, unless another tab has already. Nothing is read
    /// until a source is chosen; one chosen on the Files or Divisions tab is being read already.
    /// </summary>
    private async Task LookForGames()
    {
        started = true;
        if (sources.Chosen is not null) return;
        if (!sources.HasLooked) ShowMessage(Texts.Get("Units.LookingForGames"), offerChooser: false);
        await sources.LookOnceAsync();
        // reading a game's units and pictures takes a while, and the user should know what started it
        if (sources.Chosen is null)
            ShowMessage(Texts.Get(sources.Sources.Count == 0 ? "Units.NoGame" : "Units.ChooseSourceFirst"), offerChooser: sources.Sources.Count == 0);
    }

    /// <summary>A source was chosen in the list: it is shown in the heading and read, unless it is shown already.</summary>
    private void SourceChanged()
    {
        ShowSource();
        if (sources.Chosen is not UnitSource chosen || loaded?.Source.Key == chosen.Key || readingKey == chosen.Key) return;
        lastRead = Read(chosen);
    }

    /// <summary>
    /// Which source the units are from: in the heading, "Units in WTO x WiF", the game dimmed after a mod's name, and
    /// "Units" alone while none is chosen; and its folder under the Source list.
    /// </summary>
    private void ShowSource()
    {
        TitleText.Inlines.Clear();
        UnitSource? chosen = sources.Chosen;
        WhereText.Text = chosen?.Folder ?? "";
        WeaponsView.ShowWhere(chosen?.Folder ?? "");
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

    private void ChooseFolderClicked(object sender, RoutedEventArgs e) => SourceChoice.ChooseFolder();

    /// <summary>
    /// Reads a source's units in the background (a few seconds), then shows them with the filters cleared. The units
    /// compared and the unit selected belong to the source, so they are cleared too.
    /// </summary>
    private async Task Read(UnitSource source)
    {
        int mine = ++readsStarted;
        readingKey = source.Key;
        StartReading(source);
        // created here, so each step is told on the screen's thread
        var progress = new Progress<UnitReadStep>(step => { if (mine == readsStarted) ShowReadingStep(step); });
        ProblemsText.Visibility = Visibility.Collapsed;
        string cacheRoot = AppData.Current.Folders?.Cache ?? Path.Combine(Path.GetTempPath(), "EasyNDF", "Cache");
        LoadedUnits read;
        try
        {
            read = await Task.Run(() => UnitLibrary.Load(source, cacheRoot, progress: progress));
        }
        // any failure, not only a file problem: one not caught left the reading card up for good, and the same source
        // could not be chosen again
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            if (mine != readsStarted) return;
            readingKey = null;
            ShowMessage(Texts.Get("Units.CouldNotRead", ("name", source.Name), ("problem", problem.Message)), offerChooser: true);
            return;
        }
        if (mine != readsStarted) return;
        readingKey = null;

        loaded = read;
        gameUnits = read;
        filter.VanillaUnits = read.VanillaUnits;
        // the open mod's values are worked out for this source once it shows
        ModMarks.Changes = UnitChanges.None;
        modValuesFrom = "";
        unitsByRules = null;
        addedUnitsShown = "";
        NotShownText.Visibility = Visibility.Collapsed;
        // the source before is let go of, and what reading this one left behind (the game's archive lists, its
        // dictionaries, its unit files) is given back
        UnitPictureLibrary.Current = new UnitPictureLibrary(read.Pictures, read.Catalog.FlagSources);
        UnitCountFont.Use(read.UnitCountFontFile);
        Memory.GiveBack();
        ProblemsText.Visibility = read.Problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProblemsText.ToolTip = string.Join("\n", read.Problems.Take(20));
        ReadDivisions();
        ResetFilters();
        // a division chosen last time shows all its categories
        if (chosenDivision is not null) filter.Factory = "";
        compared.Clear();
        pinned = null;
        selection.Selected = null;
        StopReading();
        MessageArea.Visibility = Visibility.Collapsed;
        ShowAll();
        ModValuesMayHaveChanged();
    }

    /// <summary>Shows a line where the units go: while reading, or when there is nothing to read; with "Choose a Game Folder…" when it helps.</summary>
    private void ShowMessage(string text, bool offerChooser)
    {
        StopReading();
        MessageText.Text = text;
        ChooseFolderButton.Visibility = offerChooser ? Visibility.Visible : Visibility.Collapsed;
        MessageArea.Visibility = Visibility.Visible;
        CountText.Text = "";
        WeaponsView.ShowMessage(text);
    }

    // ---- the filters ----

    /// <summary>
    /// The filters as at first, for a source just read: every side, nation, role and division, and every unit, those in
    /// no division too (a unit only some division's transport is in none, such as the M2A2 BRAT).
    /// </summary>
    private void ResetFilters()
    {
        filter.Text = "";
        filter.Side = "";
        filter.Nations.Clear();
        filter.Role = "";
        filter.OnlyInDivision = false;
        filter.OnlyModded = false;
        filter.Factory = loaded is not null && loaded.Catalog.Units.Any(unit => unit.Factory == "Tanks") ? "Tanks" : "";
        foldedRoles.Clear();
        SearchBox.Text = "";
        OnlyInDivision.IsChecked = false;
        OnlyModded.IsChecked = false;
        FillSideChoices();
        FillRoleChoice();
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
                FillSideChoices();
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

    /// <summary>
    /// The search is applied once typing pauses, as on the Files tab: drawing the whole tab again for each letter made
    /// typing lag with many units.
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer searchPause = new() { Interval = TimeSpan.FromMilliseconds(160) };

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (searchPause.Tag is null)
        {
            searchPause.Tag = true;
            searchPause.Tick += (_, _) =>
            {
                searchPause.Stop();
                filter.Text = SearchBox.Text.Trim();
                if (loaded is not null) ShowAll();
            };
        }
        searchPause.Stop();
        searchPause.Start();
    }

    private void RoleChosen(object sender, SelectionChangedEventArgs e)
    {
        if (fillingLists || RoleChoice.SelectedItem is not ComboBoxItem { Tag: string role }) return;
        filter.Role = role;
        ShowAll();
    }

    private void OnlyInDivisionClicked(object sender, RoutedEventArgs e)
    {
        filter.OnlyInDivision = OnlyInDivision.IsChecked == true;
        if (loaded is not null) ShowAll();
    }

    /// <summary>"Only modded units": the units vanilla WARNO does not have, those the open mod adds among them.</summary>
    private void OnlyModdedClicked(object sender, RoutedEventArgs e)
    {
        filter.OnlyModded = OnlyModded.IsChecked == true;
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
    /// <param name="mayKeep">Whether the list may be left as it was drawn, when its counts are the same (a sub-tab switch).</param>
    private void FillNationList(bool mayKeep = false)
    {
        DrawnFor now = CountsDrawnForNow();
        if (IsDrawnFor(nationListDrawnFor, now, mayKeep)) return;
        nationListDrawnFor = now;
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

    /// <summary>A side's heading in the nation list (see <see cref="GroupHeading"/>).</summary>
    private static Bevel SideHeading(string side, bool firstSide) => GroupHeading(UnitNames.Side(side), firstSide ? "side-a" : "side-b");

    /// <summary>
    /// A group's heading in the nation or division list, the same in both: bold, on the panel colour, with a stripe of
    /// a theme colour (a side's, or the accent for the mod's own divisions) and a line under it.
    /// </summary>
    private static Bevel GroupHeading(string text, string colour)
    {
        var stripe = new System.Windows.Shapes.Rectangle { Width = 3, Margin = new Thickness(0, 0, 6, 0) };
        stripe.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, colour);
        var name = new TextBlock { Text = text, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        var row = new Bevel { Child = new DockPanel { Children = { stripe, name } }, Padding = new Thickness(8, 3, 8, 3), Edges = new Thickness(0, 0, 0, 1) };
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
    /// <param name="mayKeep">Whether the tabs may be left as they were drawn, when their counts are the same (a sub-tab switch).</param>
    private void FillFactoryTabs(bool mayKeep = false)
    {
        DrawnFor now = CountsDrawnForNow();
        if (IsDrawnFor(factoryTabsDrawnFor, now, mayKeep)) return;
        factoryTabsDrawnFor = now;
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

    /// <summary>
    /// The view chosen stays pressed; + Add Units… is on Compare. A battlegroup has no views or factory tabs: its cards
    /// are in rows, one a type.
    /// </summary>
    private void ShowViewButtons()
    {
        ViewButtons.Visibility = ShowsBattlegroup ? Visibility.Collapsed : Visibility.Visible;
        FactoryTabs.Visibility = ShowsBattlegroup ? Visibility.Collapsed : Visibility.Visible;
        CardsButton.IsChecked = view == UnitView.Cards;
        TableButton.IsChecked = view == UnitView.Table;
        CompareButton.IsChecked = view == UnitView.Compare;
        // a division's units are added from the Division pane's heading
        AddUnitsButton.Visibility = view == UnitView.Compare && !ShowsBattlegroup ? Visibility.Visible : Visibility.Collapsed;
        // a new unit is made among every unit, where it shows once made
        NewUnitButton.Visibility = part == Data.ArmoryPart.Units && view != UnitView.Compare ? Visibility.Visible : Visibility.Collapsed;
        ShowZoom();
    }

    /// <summary>Shows everything again after a filter, the view or the source changed: the nations, the factory tabs and the view.</summary>
    private void ShowAll() => ShowAll(onlyPartChanged: false);

    /// <summary>
    /// Shows everything again; after only a sub-tab switch (<paramref name="onlyPartChanged"/>), the views and panes
    /// drawn since the last change are shown as they were (see UnitsPage.KeptViews.cs).
    /// </summary>
    private void ShowAll(bool onlyPartChanged)
    {
        if (loaded is null) return;
        if (!onlyPartChanged) ShownThingsChanged();
        KeepADivisionChosen();
        UseDivisionMarks();
        // on the Divisions sub-tab, only the division's units: the filters narrow them further
        filter.InDivision = ShowsDivisionUnits ? ChosenDivisionUnits() : null;
        FillNationList(mayKeep: onlyPartChanged);
        FillFactoryTabs(mayKeep: onlyPartChanged);
        FillDivisionList(mayKeep: onlyPartChanged);
        ShowChosenDivision();
        ShowTitle();
        ShowViewButtons();
        // a battlegroup is shown whatever the view: it has no table, and Compare is the units'
        bool battlegroup = ShowsBattlegroup;
        bool compare = view == UnitView.Compare && !battlegroup;
        bool divisionCards = view == UnitView.Cards && ShowsDivisionUnits;
        FrameworkElement cardsView = battlegroup ? BattlegroupScroller : divisionCards ? DivisionCardsScroller : CardsList;
        bool cardsViewWasHidden = cardsView.Visibility != Visibility.Visible;
        Panes.Visibility = compare ? Visibility.Collapsed : Visibility.Visible;
        CompareBox.Visibility = compare ? Visibility.Visible : Visibility.Collapsed;
        CardsList.Visibility = view == UnitView.Cards && !divisionCards && !battlegroup ? Visibility.Visible : Visibility.Collapsed;
        DivisionCardsScroller.Visibility = divisionCards ? Visibility.Visible : Visibility.Collapsed;
        TableList.Visibility = view == UnitView.Table && !battlegroup ? Visibility.Visible : Visibility.Collapsed;
        BattlegroupScroller.Visibility = battlegroup ? Visibility.Visible : Visibility.Collapsed;
        shown = filter.Apply(loaded.Catalog.Units);
        // the battlegroup's cards are not filtered, so the unit selected there stays selected
        if (!battlegroup) selection.KeepOnly(shown);
        if (battlegroup || view == UnitView.Cards) ShowCardsView(cardsView, cardsViewWasHidden, mayKeep: onlyPartChanged);
        else if (view == UnitView.Table) ShowTable();
        else ShowCompare();
        if (onlyPartChanged) ShowUnitPaneUnlessDrawn();
        else ShowUnitPane();
        ShowDivisionPane(mayKeep: onlyPartChanged);
        ShowFootnote();
        CountText.Text = battlegroup ? BattlegroupCountText()
            : compare ? Texts.Count("Units.Comparing", compared.Count) : Texts.CountWide("Units.Shown", shown.Count);
        if (part == Data.ArmoryPart.Weapons) ShowWeapons();
    }

    /// <summary>The note at the foot of the tab: how Compare reads its numbers, or how to use the list.</summary>
    private void ShowFootnote()
    {
        if (messageTimer?.IsEnabled == true) return;
        if (ShowsBattlegroup)
        {
            Footnote.Text = Texts.Get("Divisions.Battlegroup.Footnote");
            return;
        }
        Footnote.Text = view == UnitView.Compare
            ? Texts.Get("Units.Footnote.Compare") + " " + Texts.Get(Game.RangeUnit == "GRU" ? "Units.Footnote.RangeGru" : "Units.Footnote.RangeMetres")
            : Texts.Get("Units.Footnote.List");
    }

    /// <summary>How long a message stays at the foot of the tab before its note comes back.</summary>
    private static readonly TimeSpan MessageTime = TimeSpan.FromSeconds(4);

    private System.Windows.Threading.DispatcherTimer? messageTimer;

    /// <summary>
    /// Shows a message, such as what Undo did, at the foot of the tab in place of its note for a few seconds: the tab
    /// has no message line of its own, as the Files and Rules tabs have.
    /// </summary>
    public void SayForAMoment(string message)
    {
        if (messageTimer is null)
        {
            messageTimer = new System.Windows.Threading.DispatcherTimer { Interval = MessageTime };
            messageTimer.Tick += (_, _) =>
            {
                messageTimer.Stop();
                ShowFootnote();
            };
        }
        messageTimer.Stop();
        Footnote.Text = message;
        messageTimer.Start();
    }
}
