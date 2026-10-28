using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Divisions;
using EasyNdf.App.Mods;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Units tab's divisions: the list under the nations (All units, the open mod's new divisions, then the game's by
/// side and nation), the division chosen in it (its units in the centre, as cards by category with their slot costs or
/// as the table with its own columns), and the Division pane beside the Unit pane. What changes a division is in
/// UnitsPage.DivisionEditing.cs.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The source's units by descriptor, to find each division's units.</summary>
    private Dictionary<string, Unit> unitsByDescriptor = new(StringComparer.Ordinal);

    /// <summary>The divisions listed: the game's multiplayer ones with the open mod's changes, then the mod's new ones.</summary>
    private List<Division> divisions = [];

    /// <summary>The division chosen in the list, whose units the centre shows; null for All units.</summary>
    private Division? chosenDivision;

    /// <summary>The categories folded away in a division's Cards view, by code.</summary>
    private readonly HashSet<string> foldedCategories = [];

    /// <summary>How many tiles a row of a division's Cards view had when it was last filled.</summary>
    private int divisionCardColumns;

    /// <summary>The mod whose divisions are shown and changed: the one open on the Rules tab.</summary>
    private static Mod OpenMod => AppData.Current.OpenMod;

    /// <summary>
    /// Whether the Armory shows the open mod's own changes: "Show mods' changes" is on, and the mod is on on the Mods
    /// tab. The Armory shows only the mods that are on, as export runs only those.
    /// </summary>
    private static bool ShowsOpenMod => ArmoryShowsModChanges && ModStack.IsOn(OpenMod);

    /// <summary>"Show mods' changes", under the Source: on, the Armory shows the values as the mods that are on make them; off, as the source has them.</summary>
    internal static bool ArmoryShowsModChanges => AppData.Current.Settings["armoryModChanges"]?.GetValue<bool>() != false;

    /// <summary>The divisions as shown: with the open mod's changes when the Armory shows them.</summary>
    private List<Division> DivisionsShown() =>
        ShowsOpenMod ? DivisionChanges.Apply(DivisionsBeforeArmoryChanges, OpenMod, Rosters) : [.. DivisionsBeforeArmoryChanges];

    /// <summary>The generated divisions' units, made from the units shown (<see cref="DivisionRosters"/>), and what they were made from.</summary>
    private (DivisionRosters Rosters, object? Units, object? Game)? rosters;

    /// <summary>
    /// The units of generated divisions, made from the units the Armory shows (the source with the mods that are on),
    /// as an export makes them from the files on the Files tab; made again when those units are read again.
    /// </summary>
    private DivisionRosters Rosters
    {
        get
        {
            object? units = loaded?.Catalog.Units;
            object? game = gameUnits?.Catalog.Divisions;
            if (rosters is not { } made || !ReferenceEquals(made.Units, units) || !ReferenceEquals(made.Game, game))
                rosters = made = (new DivisionRosters(loaded?.Catalog.Units ?? [], GameDivisions), units, game);
            return made.Rosters;
        }
    }

    /// <summary>The open mod's change to a division, when the Armory shows the open mod's changes; null otherwise.</summary>
    private static ModDivision? EntryShown(string descriptor) => ShowsOpenMod ? DivisionChanges.EntryOf(OpenMod, descriptor) : null;

    /// <summary>
    /// Every division of the source, multiplayer or not, as the game has it: what the mod's division changes start from
    /// and are written as rules against. Not the divisions as the mod's other rules make them: the Armory presets made
    /// from those would change with whether the mod's values had been read yet.
    /// </summary>
    private IReadOnlyList<Division> GameDivisions => gameUnits?.Catalog.Divisions ?? [];

    /// <summary>
    /// Every division as the open mod's other rules make it (its own presets, and Divisions rules made editable), for the
    /// Armory to show the division changes on: the game's own until the mod's values are read.
    /// </summary>
    private IReadOnlyList<Division> DivisionsBeforeArmoryChanges => loaded?.Catalog.Divisions ?? [];

    /// <summary>Sets up the parts of the tab that show divisions, once, as the tab is built.</summary>
    private void PrepareDivisions()
    {
        // another mod opened, or the open one changed: its divisions are shown
        AppData.Current.ModsChanged += _ => ModChanged();
        DivisionCardsScroller.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(UnitListDoubleClicked), handledEventsToo: true);
        PrepareSplits();
    }

    /// <summary>
    /// A source has just been read: its units by descriptor, and its divisions with the open mod's; on Divisions and
    /// Battlegroups, the division chosen last time is chosen again when the source has it, else the first listed. On
    /// Units, a division chosen to narrow the units stays chosen, and none is chosen for it otherwise.
    /// </summary>
    private void ReadDivisions()
    {
        unitsByDescriptor = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in loaded?.Catalog.Units ?? []) unitsByDescriptor[unit.Descriptor] = unit;
        foldedCategories.Clear();
        foldedBattlegroupCategories.Clear();
        divisions = DivisionsShown();
        MakeArmoryPresetsIfMissing();
        string? remembered = part == ArmoryPart.Units ? chosenDivision?.Descriptor : (string?)AppData.Current.State["division"];
        chosenDivision = remembered is null ? null : divisions.FirstOrDefault(division => division.Descriptor == remembered);
        KeepADivisionChosen();
    }

    /// <summary>The open mod changed or another was opened: its divisions are shown, the one chosen kept when it is still there.</summary>
    private void ModChanged()
    {
        if (loaded is null) return;
        ShownThingsChanged();
        string? costsOf = costsChangedOf;
        costsChangedOf = null;
        if (costsOf is not null && chosenDivision is not null) ShowCostsChanged(costsOf, chosenDivision.Descriptor);
        else ApplyMod(chosenDivision?.Descriptor);
    }

    /// <summary>
    /// A type's costs were changed in the chosen division: the divisions are read again with the mod's changes, but only
    /// that type's heading is drawn again, with the mod's note in the pane, and the division list when the division
    /// has just become changed (for its ✎). Drawing the whole tab again would make it jump.
    /// </summary>
    private void ShowCostsChanged(string factory, string descriptor)
    {
        bool wasChanged = FirstChangeShown(descriptor);
        divisions = DivisionsShown();
        chosenDivision = divisions.FirstOrDefault(division => division.Descriptor == descriptor);
        UseDivisionMarks();
        if (chosenDivision is null)
        {
            ApplyMod(null);
            return;
        }
        if (!wasChanged) FillDivisionList();
        ShowModNote(EntryShown(descriptor));
        if (DivisionCardsContent.Content is not StackPanel view) return;
        Dictionary<string, Unit> shownUnits = ByDescriptor(shown);
        DivisionGroup? group = DivisionGroups.Of(chosenDivision, shownUnits, Game, factory, unitsByDescriptor, withEmpty: true).FirstOrDefault();
        if (group is not null) DivisionCards.RedrawHeading(view, group, foldedCategories.Contains(factory), CategoryFoldClicked, this);
    }

    /// <summary>Whether the division list already shows the division as changed by the mod (its ✎).</summary>
    private bool FirstChangeShown(string descriptor) =>
        DivisionList.Children.OfType<DivisionListRow>().Any(row => row.Division.Descriptor == descriptor && row.IsChanged);

    /// <summary>
    /// The divisions again, with the open mod's changes; <paramref name="descriptor"/>'s division chosen when there is
    /// one, else (on Divisions and Battlegroups) the first listed.
    /// </summary>
    private void ApplyMod(string? descriptor)
    {
        divisions = DivisionsShown();
        chosenDivision = descriptor is null ? null : divisions.FirstOrDefault(division => division.Descriptor == descriptor);
        KeepADivisionChosen();
        // every change on the Rules tab saves the mod: drawn while hidden, the tab was drawn again for each of them
        if (!IsDrawn)
        {
            redrawWhenShown = true;
            return;
        }
        ShowAll();
    }

    /// <summary>Whether the mod changed while the tab was hidden, so it is drawn again when shown.</summary>
    private bool redrawWhenShown;

    /// <summary>The descriptors of the chosen division's units, for the filter; null for All units.</summary>
    private HashSet<string>? ChosenDivisionUnits() =>
        chosenDivision is null ? null : new HashSet<string>(chosenDivision.Units.Select(rule => rule.Unit), StringComparer.Ordinal);

    /// <summary>
    /// The heading: "Units in WARNO" on the Units sub-tab; for a division, its name and size, or its battlegroup's name,
    /// and at the right where it comes from and which mod its changes are saved in.
    /// </summary>
    private void ShowTitle()
    {
        ShowSource();
        ShowGeneratedLine();
        SummaryText.Text = "";
        TitleText.ToolTip = null;
        RevertBattlegroupButton.Visibility = Visibility.Collapsed;
        GenerationSettingsButton.Visibility = Visibility.Collapsed;
        SavedInText.Text = "";
        SavedInText.ToolTip = null;
        if (chosenDivision is null) return;
        SavedInText.Text = Texts.Get("Divisions.SavedIn", ("source", loaded?.Source.Name), ("mod", OpenMod.Name));
        SavedInText.ToolTip = Texts.Get("Divisions.SavedIn.Tip", ("mod", OpenMod.Name));
        if (ShowsBattlegroup)
        {
            ShowBattlegroupTitle(chosenDivision);
            return;
        }
        TitleText.Inlines.Clear();
        TitleText.Inlines.Add(new System.Windows.Documents.Run(chosenDivision.Name));
        SummaryText.Text = Texts.Get("Divisions.Summary",
            ("units", Texts.Count("Divisions.UnitCount", chosenDivision.Units.Count)), ("points", UnitNumbers.Of(chosenDivision.ActivationPoints)));
    }

    // ---- the list ----

    /// <summary>
    /// The divisions of the side chosen: first the mod's new divisions, under the mod's name; then the
    /// game's, under a heading for each side (with its colour) and, within it, for each nation, A to Z, each nation's
    /// divisions in the game's order. A game division the mod changes is marked.
    /// </summary>
    /// <param name="mayKeep">Whether the list may be left as it was drawn, when nothing it shows changed since (a sub-tab switch).</param>
    private void FillDivisionList(bool mayKeep = false)
    {
        NewDivisionButton.IsEnabled = loaded is not null && divisions.Count > 0;
        GenerateDivisionsButton.IsEnabled = NewDivisionButton.IsEnabled;
        // drawn for every sub-tab alike: they share it
        var now = new DrawnFor(changesShown, chosenDivision, ArmoryPart.Divisions);
        if (IsDrawnFor(divisionListDrawnFor, now, mayKeep)) return;
        divisionListDrawnFor = now;
        DivisionList.Children.Clear();
        if (loaded is null) return;
        List<Division> shownDivisions = [.. divisions.Where(division => filter.Side == "" || division.Side == filter.Side)];
        List<Division> modsOwn = [.. shownDivisions.Where(IsNewInMod)];
        if (modsOwn.Count > 0)
        {
            DivisionList.Children.Add(GroupHeading(OpenMod.Name, "accent"));
            foreach (Division division in modsOwn) AddDivisionRow(division);
        }
        IReadOnlyList<string> sides = UnitNames.SidesOf(Game);
        IEnumerable<IGrouping<string, Division>> bySide = shownDivisions.Where(division => !IsNewInMod(division))
            .GroupBy(division => division.Side)
            .OrderBy(group => SideOrder(sides, group.Key));
        foreach (IGrouping<string, Division> sideGroup in bySide)
        {
            DivisionList.Children.Add(GroupHeading(UnitNames.Side(sideGroup.Key), sideGroup.Key == sides[0] ? "side-a" : "side-b"));
            var byNation = sideGroup.GroupBy(division => division.Country)
                .OrderBy(group => UnitNames.Nation(group.Key), StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
            foreach (IGrouping<string, Division> nationGroup in byNation)
            {
                // Steel Division 2 gives its divisions no nation: they are listed under the side only
                if (nationGroup.Key != "") DivisionList.Children.Add(NationHeading(nationGroup.Key));
                foreach (Division division in nationGroup.OrderBy(division => division.ListOrder ?? double.MaxValue)) AddDivisionRow(division);
            }
        }
    }

    /// <summary>
    /// The division list's heading: "Divisions", or the chosen division's emblem and name in its place, with x to show
    /// every unit again; the full name as a tooltip, for when it is cut short.
    /// </summary>
    private void ShowChosenDivision()
    {
        bool chosen = chosenDivision is not null;
        DivisionsTitle.Visibility = chosen ? Visibility.Collapsed : Visibility.Visible;
        ChosenDivisionMark.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
        ChosenDivisionName.Text = chosenDivision?.Name ?? "";
        ChosenDivisionMark.ToolTip = chosen ? Texts.Get("Divisions.Chosen.Find.Tip", ("name", chosenDivision!.Name)) : null;
        ChosenDivisionEmblem.Content = chosen ? new DivisionEmblem(chosenDivision!.EmblemSource, 16, keepsRoom: true, chosenDivision.EmblemFile) : null;
        // with a division chosen, its units are all in a division, so the toggle has nothing to do
        OnlyInDivision.IsEnabled = !chosen;
        // Divisions and Battlegroups always show a division; Units can go back to every unit
        ClearDivisionButton.Visibility = UnitsShowOneDivision ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The heading's ✕, on the Units sub-tab: every unit again, as before a division was chosen.</summary>
    private void ClearChosenDivisionClicked(object sender, RoutedEventArgs e) => ChooseDivision(null, keepsPart: true);

    /// <summary>The chosen division in the heading is clicked: the list scrolls to its row.</summary>
    private void ChosenDivisionMarkClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ScrollToChosenDivision();
    }

    /// <summary>
    /// Scrolls the division list so the chosen division's row is a third of the way down, where the eye finds it, or as
    /// near as the list allows. Nothing happens when its row is not listed (the list shows another side).
    /// </summary>
    private void ScrollToChosenDivision()
    {
        DivisionListRow? row = DivisionList.Children.OfType<DivisionListRow>().FirstOrDefault(each => ReferenceEquals(each.Division, chosenDivision));
        if (row is null) return;
        double rowTop = row.TranslatePoint(new Point(0, 0), DivisionList).Y;
        DivisionScroller.ScrollToVerticalOffset(Math.Max(0, rowTop - DivisionScroller.ViewportHeight / 3));
    }

    /// <summary>A division's row in the list, with its menu on a right-click.</summary>
    private void AddDivisionRow(Division division)
    {
        bool isChanged = !IsNewInMod(division) && EntryShown(division.Descriptor) is not null;
        bool isRemoved = ShowsOpenMod && DivisionChanges.IsRemoved(OpenMod, division.Descriptor);
        bool isGenerated = EntryShown(division.Descriptor) is { IsGenerated: true };
        var row = new DivisionListRow(division, ReferenceEquals(division, chosenDivision), isChanged, isRemoved, isGenerated);
        // a click keeps the sub-tab: on Units, it shows only the division's units
        row.Chosen += chosen => ChooseDivision(chosen, keepsPart: true);
        row.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenDivisionMenu(division, row);
        };
        DivisionList.Children.Add(row);
    }

    /// <summary>Whether a division is one of the mod's new divisions, rather than the game's.</summary>
    private static bool IsNewInMod(Division division) => DivisionChanges.EntryOf(OpenMod, division.Descriptor) is { IsNew: true };

    /// <summary>A side's place in the game's order; one the app does not know goes last.</summary>
    private static int SideOrder(IReadOnlyList<string> sides, string side)
    {
        int place = sides.ToList().IndexOf(side);
        return place < 0 ? sides.Count : place;
    }

    /// <summary>A nation's heading in the division list: its name, small and dim.</summary>
    private static TextBlock NationHeading(string nation)
    {
        var heading = new TextBlock { Text = UnitNames.Nation(nation), FontSize = 11, Margin = new Thickness(10, 3, 6, 1) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return heading;
    }

    /// <summary>
    /// A division clicked in the list, or none (null, the Units sub-tab): its units in the centre, on the ALL tab, or its
    /// battlegroup; remembered for next time. The unit selected stays selected when the division has it.
    /// </summary>
    /// <param name="division">The division; null for every unit.</param>
    /// <param name="keepsPart">
    /// Whether the sub-tab shown stays: a click in the division list, after which the Units sub-tab shows only the
    /// division's units. Otherwise (a link, a new division) the Divisions sub-tab shows it.
    /// </param>
    private void ChooseDivision(Division? division, bool keepsPart = false)
    {
        if (ReferenceEquals(division, chosenDivision)) return;
        // a box being typed in (the pane's, or the table's) saves into the division it belongs to first: left later,
        // it would save into the division chosen now. Saving reads the divisions again, so the one clicked is found anew.
        if (IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox)
        {
            Keyboard.ClearFocus();
            if (division is not null) division = divisions.FirstOrDefault(each => each.Descriptor == division.Descriptor) ?? division;
            if (ReferenceEquals(division, chosenDivision)) return;
        }
        chosenDivision = division;
        if (!keepsPart) ShowPartForDivision(division);
        filter.Factory = "";
        if (division is not null && selection.Selected is Unit unit && !division.Units.Any(rule => rule.Unit == unit.Descriptor)) selection.Selected = null;
        // the division is remembered for Divisions and Battlegroups even while Units shows
        if (division is not null) AppData.Current.State["division"] = division.Descriptor;
        AppData.Current.SaveState();
        ShowAll();
        DivisionCardsScroller.ScrollToTop();
        BattlegroupScroller.ScrollToTop();
        DivisionPaneScroller.ScrollToTop();
    }

    // ---- a division's Cards view ----

    /// <summary>
    /// A division's Cards view: its units the filters leave, by type, each type's heading with its counts and cost
    /// buttons. Every one of the game's types is there, even with no units, so its costs can be changed; with a factory
    /// tab chosen, only that type.
    /// </summary>
    private void ShowDivisionCards()
    {
        if (chosenDivision is null) return;
        Division division = chosenDivision;
        Dictionary<string, Unit> shownUnits = ByDescriptor(shown);
        List<DivisionGroup> groups = DivisionGroups.Of(division, shownUnits, Game, filter.Factory, unitsByDescriptor, withEmpty: true);
        Dictionary<string, int> inDivision = DivisionGroups.CountsByFactory(division, unitsByDescriptor);
        divisionCardColumns = CardRowView.ColumnsFor(DivisionCardsRoom());
        DivisionCardsContent.Content = groups.Count == 0
            ? new TextBlock { Text = Texts.Get("Units.NoneMatch"), Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap }
            : DivisionCards.Build(groups, division.Name, factory => inDivision.GetValueOrDefault(factory), Game, selection, divisionCardColumns,
                foldedCategories, CategoryFoldClicked, this, unitsByDescriptor);
    }

    /// <summary>The width a division's cards have across, beside the scrollbar, in their own units: less when zoomed in.</summary>
    private double DivisionCardsRoom() => RoomIn(DivisionCardsScroller);

    /// <summary>
    /// The view was resized: a division's cards are laid out again when a different number fits across, once the width
    /// stays put, as the units' cards are (see <see cref="ReflowCardsSoon"/>).
    /// </summary>
    private void DivisionCardsResized(object sender, SizeChangedEventArgs e)
    {
        if (!ShowsDivisionUnits || view != UnitView.Cards || drawIsWaiting) return;
        if (CardRowView.ColumnsFor(DivisionCardsRoom()) == divisionCardColumns) return;
        if (e.PreviousSize.Width == 0) ShowDivisionCards();
        else ReflowCardsSoon();
    }

    /// <summary>A category's heading clicked: its tiles fold away or come back.</summary>
    private void CategoryFoldClicked(string factory)
    {
        if (!foldedCategories.Remove(factory)) foldedCategories.Add(factory);
        ShowDivisionCards();
    }

    // ---- the Division pane ----

    /// <summary>The Division pane: what belongs to the whole division; on the Divisions sub-tab only.</summary>
    /// <param name="mayKeep">Whether the pane may be left as it was drawn, when nothing it shows changed since (a sub-tab switch).</param>
    private void ShowDivisionPane(bool mayKeep = false)
    {
        bool hasDivision = ShowsDivisionUnits && loaded is not null;
        ShowPanesSplit(hasDivision);
        ShowPaneButtons();
        if (!hasDivision) return;
        DrawnFor now = DrawnForNow();
        if (IsDrawnFor(divisionPaneDrawnFor, now, mayKeep)) return;
        divisionPaneDrawnFor = now;
        DivisionPaneContent.Content = DivisionPane.Build(chosenDivision!, unitsByDescriptor, Game, this);
        ShowModNote(EntryShown(chosenDivision!.Descriptor));
    }

    /// <summary>
    /// At the Division pane's foot, what the open mod does to the division (new in it, changed, or taken out of the
    /// game's list), its tooltip saying what that means; nothing for a division as the game has it.
    /// </summary>
    private void ShowModNote(ModDivision? inMod)
    {
        ModNote.Visibility = inMod is null ? Visibility.Collapsed : Visibility.Visible;
        if (inMod is null) return;
        string mod = OpenMod.Name;
        (ModNote.Text, ModNote.ToolTip) = inMod switch
        {
            { IsNew: true } => (Texts.Get("Divisions.Pane.NewInMod", ("mod", mod)), Texts.Get("Divisions.Pane.NewInMod.Tip", ("mod", mod), ("division", StartingName(inMod)))),
            { IsRemoved: true } => (Texts.Get("Divisions.Pane.RemovedInMod", ("mod", mod)), Texts.Get("Divisions.Pane.RemovedInMod.Tip", ("mod", mod))),
            _ => (Texts.Get("Divisions.Pane.ChangedInMod", ("mod", mod)), Texts.Get("Divisions.Pane.ChangedInMod.Tip", ("mod", mod))),
        };
    }

    /// <summary>The name of the game division a new division starts from.</summary>
    private string StartingName(ModDivision division) =>
        GameDivisions.FirstOrDefault(each => each.Descriptor == division.StartsFrom)?.Name ?? division.StartsFrom;

    // ---- the bars that share the room: the nations and the divisions, the Unit and Division panes ----

    /// <summary>The nations list's share of the left column's height; null for half.</summary>
    private double? listsShare;

    /// <summary>
    /// The Unit pane's width in pixels, as last dragged; null for half the panes' room. The Division pane at its left
    /// has the rest, so dragging the bar at the Division pane's left (the panes' own bar) changes the Division pane
    /// only, and the list gives or takes the room; the bar between the two panes shares the width between them.
    /// </summary>
    private double? unitPaneWidth;

    /// <summary>
    /// The Division pane's width in pixels beside the Unit pane, as last left; null for the Unit pane's width. When the
    /// Division pane comes back, the panes widen by this much, taking the room from the cards.
    /// </summary>
    private double? divisionPaneWidth;

    /// <summary>Whether both panes showed last time they were laid out, so the Division pane's coming back is seen.</summary>
    private bool showedBothPanes;

    /// <summary>The narrowest each pane is made while both show, so its boxes keep their room.</summary>
    private const double UnitPaneLeast = 200, DivisionPaneLeast = 240;

    /// <summary>The two bars: dragged, they share the room; double-clicked, evenly again; let go, the share is kept.</summary>
    private void PrepareSplits()
    {
        listsShare = (double?)AppData.Current.State["sizes"]?["unitsListsSplit"];
        unitPaneWidth = (double?)AppData.Current.State["sizes"]?["unitsUnitPaneWidth"];
        divisionPaneWidth = (double?)AppData.Current.State["sizes"]?["unitsDivisionPaneWidth"];
        ListsSplit.Moved += y =>
        {
            double height = ListsBox.ActualHeight - 9;
            if (height < 200) return;
            listsShare = Math.Clamp(Math.Round((y - 4) / height * 1000) / 1000, 0.15, 0.85);
            ShowListsSplit();
        };
        ListsSplit.Reset += () => { listsShare = null; ShowListsSplit(); };
        ListsSplit.Settled += () => FilesPage.RememberSize("unitsListsSplit", listsShare);
        PanesSplit.Moved += x =>
        {
            if (PanesBox.ActualWidth - 9 < UnitPaneLeast + DivisionPaneLeast) return;
            // the Division pane is at the left, so the Unit pane has the room right of the mouse
            unitPaneWidth = Math.Round(PanesBox.ActualWidth - x - 5);
            ShowPanesSplit(ShowsDivisionUnits);
        };
        PanesSplit.Reset += () => { unitPaneWidth = null; ShowPanesSplit(ShowsDivisionUnits); };
        PanesSplit.Settled += () =>
        {
            FilesPage.RememberSize("unitsUnitPaneWidth", unitPaneWidth);
            KeepDivisionPaneWidth();
        };
        Panes.Resized += KeepUnitPaneWidth;
        ShowListsSplit();
        ShowPanesSplit(hasDivision: false);
    }

    /// <summary>The nations' and divisions' heights: shares of the room in whole thousandths.</summary>
    private void ShowListsSplit()
    {
        DivisionsRow.MinHeight = 90;
        int share = listsShare is double s && s > 0 && s < 1 ? (int)Math.Round(s * 1000) : 500;
        NationsRow.Height = new GridLength(share, GridUnitType.Star);
        DivisionsRow.Height = new GridLength(1000 - share, GridUnitType.Star);
    }

    /// <summary>
    /// The Unit and Division panes' widths: the Unit pane at its own width, the Division pane the rest. A pane folded
    /// away by its button, or the Division pane without a division chosen, is hidden with the bar, and the other keeps
    /// its width: the hidden pane's room goes to the cards, and a division chosen takes its room from them.
    /// </summary>
    private void ShowPanesSplit(bool hasDivision)
    {
        bool showsUnit = paneButtons.RightShows || !hasDivision;
        bool showsDivision = hasDivision && paneButtons.LeftShows;
        bool showsBoth = showsUnit && showsDivision;
        UnitPaneBox.Visibility = showsUnit ? Visibility.Visible : Visibility.Collapsed;
        DivisionPaneBox.Visibility = showsDivision ? Visibility.Visible : Visibility.Collapsed;
        PanesSplit.Visibility = showsBoth ? Visibility.Visible : Visibility.Collapsed;
        PanesSplitColumn.Width = new GridLength(showsBoth ? 9 : 0);
        DivisionPaneColumn.Width = showsDivision ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        // beside, the panes' whole width is the panes' own, part of it shown when one is hidden; below, they have the
        // window's width
        double whole = Panes.WholeSecondWidth > 0 ? Panes.WholeSecondWidth : PanesBox.ActualWidth;
        bool divisionComesBack = showsBoth && !showedBothPanes;
        showedBothPanes = showsBoth;
        if (showsBoth) Panes.SecondPart = 1;
        if (divisionComesBack && Panes.WholeSecondWidth > 0)
        {
            // the Unit pane keeps the width it had alone, and the Division pane's room comes from the cards
            double unitAlone = UnitPaneAloneIn(whole);
            Panes.SetWholeSecondWidth(unitAlone + 9 + (divisionPaneWidth ?? unitAlone));
            whole = Panes.WholeSecondWidth;
        }
        if (!showsBoth) ShowOnePane(showsUnit, whole);
        ShowUnitPaneWidth(showsBoth);
        ShowLeastPaneWidths();
    }

    /// <summary>
    /// The Unit pane's width when the panes have this much room together: as dragged, or half; with both showing,
    /// never so wide or narrow that a pane loses its least width.
    /// </summary>
    private double UnitPaneWidthIn(double whole)
    {
        double wanted = unitPaneWidth ?? (whole - 9) / 2;
        double widest = Math.Max(UnitPaneLeast, whole - 9 - DivisionPaneLeast);
        return Math.Clamp(wanted, UnitPaneLeast, widest);
    }

    /// <summary>One pane alone shows its own part of the panes' whole width: the Unit pane its width alone, the Division pane the rest.</summary>
    private void ShowOnePane(bool showsUnit, double whole)
    {
        if (whole <= 0) Panes.SecondPart = 0.5;
        else if (showsUnit) Panes.SecondPart = UnitPaneAloneIn(whole) / whole;
        else Panes.SecondPart = (whole - UnitPaneWidthIn(whole) - 9) / whole;
    }

    /// <summary>The Unit pane's width when it shows alone: as dragged, or half the panes' whole width; never narrower than its least.</summary>
    private double UnitPaneAloneIn(double whole) => Math.Max(UnitPaneLeast, unitPaneWidth ?? (whole - 9) / 2);

    /// <summary>With both panes showing, the Division pane's width is kept, for when it comes back after being hidden.</summary>
    private void KeepDivisionPaneWidth()
    {
        if (UnitPaneBox.Visibility != Visibility.Visible || DivisionPaneBox.Visibility != Visibility.Visible || DivisionPaneBox.ActualWidth <= 0) return;
        divisionPaneWidth = Math.Round(DivisionPaneBox.ActualWidth);
        FilesPage.RememberSize("unitsDivisionPaneWidth", divisionPaneWidth);
    }

    /// <summary>The Unit pane's column: its own width beside the Division pane; alone, all the room the panes have.</summary>
    private void ShowUnitPaneWidth(bool showsBoth)
    {
        bool showsUnit = UnitPaneBox.Visibility == Visibility.Visible;
        if (!showsUnit) UnitPaneColumn.Width = new GridLength(0);
        else if (!showsBoth) UnitPaneColumn.Width = new GridLength(1, GridUnitType.Star);
        else UnitPaneColumn.Width = new GridLength(UnitPaneWidthIn(PanesBox.ActualWidth > 0 ? PanesBox.ActualWidth : Panes.WholeSecondWidth));
    }

    /// <summary>
    /// The panes' bar was let go: with the Unit pane alone, the width it was dragged to is its own from now on; with
    /// both showing, the Division pane took the change, so nothing is kept.
    /// </summary>
    private void KeepUnitPaneWidth()
    {
        KeepDivisionPaneWidth();
        if (UnitPaneBox.Visibility != Visibility.Visible || DivisionPaneBox.Visibility == Visibility.Visible) return;
        unitPaneWidth = Math.Round(UnitPaneBox.ActualWidth);
        FilesPage.RememberSize("unitsUnitPaneWidth", unitPaneWidth);
        ShowPanesSplit(ShowsDivisionUnits);
    }

    /// <summary>
    /// The panes' room changed (their bar dragged, the window resized): the Division pane takes the change, the Unit
    /// pane keeping its width as long as the Division pane keeps its least width.
    /// </summary>
    private void PanesBoxResized(object sender, SizeChangedEventArgs e)
    {
        ShowUnitPaneWidth(UnitPaneBox.Visibility == Visibility.Visible && DivisionPaneBox.Visibility == Visibility.Visible);
        ShowLeastPaneWidths();
    }

    /// <summary>A hidden pane needs no room; with both showing, each keeps up to 30% of the width for its boxes.</summary>
    private void ShowLeastPaneWidths()
    {
        double width = PanesBox.ActualWidth;
        UnitPaneColumn.MinWidth = UnitPaneBox.Visibility == Visibility.Visible ? Math.Min(200, 0.3 * width) : 0;
        DivisionPaneColumn.MinWidth = DivisionPaneBox.Visibility == Visibility.Visible ? Math.Min(240, 0.3 * width) : 0;
    }

    /// <summary>
    /// Units by descriptor, the first of each: a mod source can declare a unit twice, and a dictionary made with
    /// ToDictionary threw then, on every division shown.
    /// </summary>
    private static Dictionary<string, Unit> ByDescriptor(IEnumerable<Unit> units)
    {
        var byDescriptor = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in units) byDescriptor.TryAdd(unit.Descriptor, unit);
        return byDescriptor;
    }
}
