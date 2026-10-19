using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Divisions tab: the multiplayer divisions of the source chosen (in its Source list, or on the Files or Units
/// tab), as the Units tab reads it, so the game is not read twice;
/// with the open mod's changes made and its new divisions added. On the left the divisions, the mod's first, then the
/// game's by side and nation; on the right the one chosen, its units as cards or a table, and the Division pane. This
/// file reads the divisions and fills the list; DivisionsPage.Views.cs shows the division chosen, and
/// DivisionsPage.Editing.cs holds the menus and what changes a division.
/// </summary>
public partial class DivisionsPage : UserControl
{
    /// <summary>The Units tab, whose source's divisions are shown.</summary>
    private UnitsPage? unitsTab;

    /// <summary>The units read on the Units tab, with their divisions; null until a source has been read.</summary>
    private LoadedUnits? loaded;

    /// <summary>The source's units by descriptor, to find each division's units.</summary>
    private Dictionary<string, Unit> unitsByDescriptor = new(StringComparer.Ordinal);

    /// <summary>The divisions shown: the game's multiplayer ones with the open mod's changes, then the mod's new ones.</summary>
    private List<Division> divisions = [];

    /// <summary>The division shown on the right; null when there is none.</summary>
    private Division? chosen;

    /// <summary>The side the list shows: empty for both.</summary>
    private string side = "";

    /// <summary>Builds the tab. It shows nothing until the Units tab has read a source.</summary>
    public DivisionsPage()
    {
        InitializeComponent();
        Panes.FoldedPlace = PaneSlot;
        Panes.SecondTitle = Texts.Get("Divisions.Pane.Title");
        selection.Changed += ShowPane;
        ViewScroller.SizeChanged += ViewResized;
        ViewScroller.AddHandler(MouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler(UnitDoubleClicked), handledEventsToo: true);
        TableList.AddHandler(MouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler(UnitDoubleClicked), handledEventsToo: true);
        Texts.LanguageChanged += LanguageChanged;
        PreviewKeyDown += FoldKeys;
        // another mod opened, or the open one changed: its divisions are shown
        AppData.Current.ModsChanged += _ => ModChanged();
        tableView = (string?)AppData.Current.State["divisionView"] == "Table";
        ShowViewButtons();
        ShowAll();
    }

    /// <inheritdoc/>
    public GameTitle Game => loaded?.Source.Game ?? GameTitle.Warno;

    /// <summary>The mod whose divisions are shown and changed: the one open on the Rules tab.</summary>
    private static Mod OpenMod => AppData.Current.OpenMod;

    /// <summary>Every division of the source, multiplayer or not, as the game has it: what the mod's divisions start from.</summary>
    private IReadOnlyList<Division> GameDivisions => loaded?.Catalog.Divisions ?? [];

    /// <summary>The source chosen, shared with the Files and Units tabs; the Units tab reads it.</summary>
    private SourceSelection? sources;

    /// <summary>Shows and chooses the main window's source in the tab's Source list.</summary>
    public void UseSources(SourceSelection shared)
    {
        sources = shared;
        SourceChoice.Selection = shared;
        shared.Changed += SourceChanged;
        SourceChanged();
    }

    /// <summary>
    /// Another source chosen, here or on another tab: its folder under the list, and "Reading…" in place of the views
    /// until the Units tab has read it.
    /// </summary>
    private void SourceChanged()
    {
        UnitSource? chosen = sources?.Chosen;
        WhereText.Text = chosen?.Folder ?? "";
        if (chosen is not null && loaded?.Source.Key != chosen.Key)
            ShowMessage(Texts.Get("Units.Reading", ("name", chosen.Name)));
    }

    /// <summary>
    /// The Units tab's loading bar moved, showed or went: the same bar under the "Reading…" line here, while the views
    /// wait for the source.
    /// </summary>
    private void FollowReadingBar(double? fraction, string step)
    {
        Visibility shown = fraction is null ? Visibility.Collapsed : Visibility.Visible;
        ReadingBar.Visibility = shown;
        ReadingStep.Visibility = shown;
        ReadingBar.Fraction = fraction ?? 0;
        ReadingStep.Text = step;
    }

    /// <summary>Shows the divisions of what the Units tab reads, now and each time it reads another source.</summary>
    public void Connect(UnitsPage units)
    {
        unitsTab = units;
        units.UnitsRead += UnitsRead;
        units.ReadingBarChanged += FollowReadingBar;
        if (units.Units is not null) UnitsRead();
    }

    /// <summary>The Units tab has read a source: its divisions are shown, the one shown last time chosen again when it has it.</summary>
    private void UnitsRead()
    {
        loaded = unitsTab?.Units;
        if (loaded is null) return;
        unitsByDescriptor = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in loaded.Catalog.Units) unitsByDescriptor[unit.Descriptor] = unit;
        side = "";
        factory = "";
        SearchBox.Text = "";
        selection.Selected = null;
        ApplyMod((string?)AppData.Current.State["division"]);
    }

    /// <summary>The open mod changed or another was opened: its divisions are shown, the one chosen kept when it is still there.</summary>
    private void ModChanged()
    {
        if (loaded is null) return;
        ApplyMod(chosen?.Descriptor);
    }

    /// <summary>
    /// The divisions again, with the open mod's changes; <paramref name="descriptor"/>'s division chosen when there is
    /// one, else the first. The unit selected stays selected when the division still has it.
    /// </summary>
    private void ApplyMod(string? descriptor)
    {
        divisions = DivisionChanges.Apply(GameDivisions, OpenMod);
        chosen = divisions.FirstOrDefault(division => division.Descriptor == descriptor) ?? divisions.FirstOrDefault();
        if (selection.Selected is Unit unit && chosen?.Units.Any(rule => rule.Unit == unit.Descriptor) != true) selection.Selected = null;
        ShowAll();
    }

    private void LanguageChanged()
    {
        Panes.SecondTitle = Texts.Get("Divisions.Pane.Title");
        ShowAll();
    }

    /// <summary>Everything again: the side buttons, the list, and the division chosen; or a message when there is nothing to show.</summary>
    private void ShowAll()
    {
        NewDivisionButton.IsEnabled = chosen is not null;
        FillSideChoices();
        FillList();
        if (loaded is null)
        {
            ShowMessage(Texts.Get(sources?.Chosen is not null ? "Units.Reading" : "Divisions.ChooseSourceFirst", ("name", sources?.Chosen?.Name)));
            return;
        }
        if (chosen is null)
        {
            ShowMessage(Texts.Get("Divisions.NoneInSource"));
            return;
        }
        MessageArea.Visibility = Visibility.Collapsed;
        Panes.Visibility = Visibility.Visible;
        ShowDivision();
    }

    /// <summary>Shows a line instead of the views: while reading, or when there is nothing to show.</summary>
    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageArea.Visibility = Visibility.Visible;
        Panes.Visibility = Visibility.Hidden;
        TitleText.Text = Texts.Get("Main.Divisions");
        SummaryText.Text = "";
        SavedInText.Text = "";
        CountText.Text = "";
        FactoryTabs.Children.Clear();
    }

    /// <summary>Brings the Units tab to the front.</summary>
    private void ShowUnitsTab() => MainView.Of(this).Tabs.SelectedItem = MainView.Of(this).UnitsTabItem;

    // ---- the list ----

    /// <summary>Both, and each side by its name, as buttons that stay pressed, as on the Units tab.</summary>
    private void FillSideChoices()
    {
        SideChoices.Children.Clear();
        foreach (string each in new[] { "" }.Concat(UnitNames.SidesOf(Game)))
        {
            var button = new ToggleButton
            {
                Content = each == "" ? Texts.Get("Units.BothSides") : UnitNames.Side(each), IsChecked = side == each,
                Style = (Style)FindResource("PressedToggle"), Padding = new Thickness(9, 1, 9, 1), Margin = new Thickness(SideChoices.Children.Count == 0 ? 0 : -1, 0, 0, 0),
                ToolTip = each == "" ? Texts.Get("Divisions.BothSides.Tip") : Texts.Get("Divisions.Side.Tip", ("side", UnitNames.Side(each))),
            };
            string clicked = each;
            button.Click += (_, _) =>
            {
                side = clicked;
                FillSideChoices();
                FillList();
            };
            SideChoices.Children.Add(button);
        }
    }

    /// <summary>
    /// The divisions that match the search and side: first the mod's new divisions, under the mod's name; then the
    /// game's, under a heading for each side (with its colour) and, within it, for each nation, A to Z, each nation's
    /// divisions in the game's order. A game division the mod changes is marked.
    /// </summary>
    private void FillList()
    {
        DivisionList.Children.Clear();
        string search = SearchBox.Text.Trim();
        List<Division> matching = [.. divisions.Where(division => (side == "" || division.Side == side) && Matches(division, search))];
        List<Division> modsOwn = [.. matching.Where(IsNewInMod)];
        if (modsOwn.Count > 0)
        {
            DivisionList.Children.Add(Heading(OpenMod.Name, "accent"));
            foreach (Division division in modsOwn) AddRow(division);
        }
        IReadOnlyList<string> sides = UnitNames.SidesOf(Game);
        IEnumerable<IGrouping<string, Division>> bySide = matching.Where(division => !IsNewInMod(division))
            .GroupBy(division => division.Side)
            .OrderBy(group => SideOrder(sides, group.Key));
        foreach (IGrouping<string, Division> sideGroup in bySide)
        {
            DivisionList.Children.Add(Heading(UnitNames.Side(sideGroup.Key), sideGroup.Key == sides[0] ? "side-a" : "side-b"));
            var byNation = sideGroup.GroupBy(division => division.Country)
                .OrderBy(group => UnitNames.Nation(group.Key), StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true));
            foreach (IGrouping<string, Division> nationGroup in byNation)
            {
                // Steel Division 2 gives its divisions no nation: they are listed under the side only
                if (nationGroup.Key != "") DivisionList.Children.Add(NationHeading(nationGroup.Key));
                foreach (Division division in nationGroup.OrderBy(division => division.ListOrder ?? double.MaxValue)) AddRow(division);
            }
        }
        if (matching.Count == 0 && loaded is not null)
            DivisionList.Children.Add(UnitPane.Muted(Texts.Get("Divisions.NoneMatch"), new Thickness(6, 2, 6, 2)));
    }

    /// <summary>A division's row in the list.</summary>
    private void AddRow(Division division)
    {
        bool isChanged = !IsNewInMod(division) && DivisionChanges.EntryOf(OpenMod, division.Descriptor) is not null;
        bool isRemoved = DivisionChanges.IsRemoved(OpenMod, division.Descriptor);
        var row = new DivisionListRow(division, ReferenceEquals(division, chosen), isChanged, isRemoved);
        row.Chosen += Choose;
        row.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenDivisionMenu(division, row);
        };
        DivisionList.Children.Add(row);
    }

    /// <summary>Whether a division is one of the mod's new divisions, rather than the game's.</summary>
    private static bool IsNewInMod(Division division) => DivisionChanges.EntryOf(OpenMod, division.Descriptor) is { IsNew: true };

    /// <summary>Whether a division's name or internal names hold the search text; an empty search matches all.</summary>
    private static bool Matches(Division division, string search) =>
        search == ""
        || division.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || division.CfgName.Contains(search, StringComparison.OrdinalIgnoreCase)
        || division.Descriptor.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>A side's place in the game's order; one the app does not know goes last.</summary>
    private static int SideOrder(IReadOnlyList<string> sides, string side)
    {
        int place = sides.ToList().IndexOf(side);
        return place < 0 ? sides.Count : place;
    }

    /// <summary>A group's heading in the list: a stripe of a theme colour, then its name in bold.</summary>
    private static DockPanel Heading(string text, string colour)
    {
        var stripe = new System.Windows.Shapes.Rectangle { Width = 3, Margin = new Thickness(0, 0, 6, 0) };
        stripe.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, colour);
        var name = new TextBlock { Text = text, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        return new DockPanel { Margin = new Thickness(6, 6, 6, 2), Children = { stripe, name } };
    }

    /// <summary>A nation's heading: its name, small and dim.</summary>
    private static TextBlock NationHeading(string nation)
    {
        var heading = new TextBlock { Text = UnitNames.Nation(nation), FontSize = 11, Margin = new Thickness(10, 3, 6, 1) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return heading;
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => FillList();

    /// <summary>A division clicked in the list: it is shown, and remembered for next time.</summary>
    private void Choose(Division division)
    {
        if (ReferenceEquals(division, chosen)) return;
        chosen = division;
        factory = "";
        selection.Selected = null;
        AppData.Current.State["division"] = division.Descriptor;
        AppData.Current.SaveState();
        FillList();
        ShowDivision();
        ViewScroller.ScrollToTop();
        PaneScroller.ScrollToTop();
    }
}
