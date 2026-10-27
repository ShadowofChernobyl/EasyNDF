using System.Windows;
using EasyNdf.Data;
using EasyNdf.App.Units;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>What the workbench's screenshots and checks use to drive the Units tab without a mouse.</summary>
public partial class UnitsPage
{
    /// <summary>Chooses a source and reads it at once, without looking for games first: the workbench's made-up install, and its mods.</summary>
    internal Task ReadNow(UnitSource source)
    {
        List<UnitSource> found = [new UnitSource(source.Game, source.GameFolder, null), .. GameInstall.ModsOf(source.GameFolder).Select(mod => new UnitSource(source.Game, source.GameFolder, mod))];
        started = true;
        sources.UseForChecks(found, source);
        return lastRead;
    }

    /// <summary>Shows the reading card as it is part way through reading, for a screenshot; the units shown stay under it.</summary>
    internal void ShowReadingBarNow(UnitReadStep step, double fraction)
    {
        StartReading(sources.Chosen ?? throw new InvalidOperationException("no source chosen"));
        readingTimer.Stop();
        ShowReadingStep(step);
        ReadingBar.Fraction = fraction;
    }

    /// <summary>Hides the reading card again.</summary>
    internal void HideReadingBarNow() => StopReading();

    /// <summary>Sets the zoom of the view shown, as the dropdown does.</summary>
    internal void SetZoomNow(int percent) => SetZoom(percent);

    /// <summary>A division's Cards view, for the mouse to be put over its headings and tiles.</summary>
    internal FrameworkElement DivisionCardsNow => DivisionCardsContent;

    /// <summary>What a division's Cards view shows now, to check a cost change does not draw it all again.</summary>
    internal object? DivisionCardsViewNow => DivisionCardsContent.Content;

    /// <summary>The shown view's zoom, in percent.</summary>
    internal int ZoomNow => ZoomOf(view);

    /// <summary>The cards shown now: every unit's, or the chosen division's; for the mouse to be put over.</summary>
    internal FrameworkElement CardsShownNow => ShowsDivisionUnits ? DivisionCardsScroller : CardsList;

    /// <summary>The zoom the dropdown shows, such as "150%".</summary>
    internal string ZoomShownNow => (ZoomChoice.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string ?? "";

    /// <summary>The number of tiles a row of Cards holds now.</summary>
    internal int CardColumnsNow => cardColumns;

    /// <summary>What the Unit pane shows.</summary>
    internal System.Windows.FrameworkElement? UnitPaneNow => UnitPaneContent.Content as System.Windows.FrameworkElement;

    /// <summary>Every unit read.</summary>
    internal IReadOnlyList<Unit> AllUnits => loaded?.Catalog.Units ?? [];

    /// <summary>Shows Cards, Table or Compare, as its button does.</summary>
    internal void ShowViewNow(string name) =>
        ViewClicked(name == "Table" ? TableButton : name == "Compare" ? CompareButton : CardsButton, new System.Windows.RoutedEventArgs());

    /// <summary>Selects a unit, as a click on it does.</summary>
    internal void SelectNow(Unit unit) => selection.Selected = unit;

    /// <summary>The unit selected, shown in the Unit pane.</summary>
    internal Unit? SelectedNow => selection.Selected;

    /// <summary>Every unit selected now.</summary>
    internal IReadOnlyList<Unit> AllSelectedNow => selection.All;

    /// <summary>Compares units, as their Compare does.</summary>
    internal void CompareNow(IEnumerable<Unit> units) => AddToCompare(units);

    /// <summary>Pins a unit on Compare, as "Pin for Comparison" does.</summary>
    internal void PinNow(Unit unit) => Pin(unit);

    /// <summary>Opens the Add Units window, as its button does.</summary>
    internal void OpenAddUnitsNow() => AddUnitsClicked(this, new System.Windows.RoutedEventArgs());

    /// <summary>The cards on Compare.</summary>
    internal IEnumerable<CompareCard> CompareCards => CompareList.Children.OfType<CompareCard>();

    /// <summary>The box Compare's cards are in.</summary>
    internal System.Windows.FrameworkElement CompareArea => CompareBox;

    /// <summary>The Cards view's lines.</summary>
    internal IEnumerable<object> CardLines => CardsList.ItemsSource?.Cast<object>() ?? [];

    // ---- the divisions ----

    /// <summary>The divisions the list can show: the game's multiplayer ones and the open mod's.</summary>
    internal IReadOnlyList<Data.Divisions.Division> AllDivisions => divisions;

    /// <summary>The division chosen in the list; null for All units.</summary>
    internal Data.Divisions.Division? ChosenDivisionNow => chosenDivision;

    /// <summary>How many units the centre shows now.</summary>
    internal int ShownCountNow => shown.Count;

    /// <summary>Whether the Division pane shows beside the Unit pane.</summary>
    internal bool DivisionPaneShownNow => DivisionPaneBox.Visibility == System.Windows.Visibility.Visible;

    /// <summary>Whether the chosen division shows in the division list's heading.</summary>
    internal bool ChosenDivisionShownNow => ChosenDivisionMark.Visibility == System.Windows.Visibility.Visible;

    /// <summary>Where the division list starts, to check the heading never moves it.</summary>
    internal double DivisionListTopNow => DivisionList.TranslatePoint(new System.Windows.Point(0, 0), this).Y;

    /// <summary>Whether the Unit pane shows in the panes' place.</summary>
    internal bool UnitPaneShownNow => UnitPaneBox.Visibility == System.Windows.Visibility.Visible;

    /// <summary>The width the Unit and Division panes take together, and the Division pane alone.</summary>
    internal (double Both, double Division) PanesWidthNow => (PanesBox.ActualWidth, DivisionPaneBox.ActualWidth);

    /// <summary>Whether the whole panes' place is folded away.</summary>
    internal bool PanesFoldedNow => Panes.IsFolded;

    /// <summary>Clicks the Unit or the Division button in the panes' heading.</summary>
    internal void PaneButtonNow(bool isUnit) => PaneButtonClicked(isUnit);

    /// <summary>Shows a sub-tab ("Units", "Divisions" or "Battlegroups"), as a click on it does.</summary>
    internal void ShowPartNow(string name) => ShowPart(Enum.Parse<ArmoryPart>(name));

    /// <summary>The Weapons sub-tab.</summary>
    internal Weapons.WeaponsArmory WeaponsNow => WeaponsView;

    /// <summary>The sub-tab shown.</summary>
    internal string PartNow => part.ToString();

    /// <summary>The Battlegroups sub-tab's view of the battlegroup.</summary>
    internal System.Windows.FrameworkElement BattlegroupNow => BattlegroupContent;

    /// <summary>Chooses a division in the list, or the Units sub-tab (null), as a click does.</summary>
    internal void ChooseDivisionNow(Data.Divisions.Division? division) => ChooseDivision(division);

    /// <summary>Adds a unit to a division, as its menu does.</summary>
    internal void AddToDivisionNow(Data.Divisions.Division division, Unit unit) => AddToDivision(division, [unit]);

    /// <summary>Turns "Only units in a division" on or off, as a click on it does.</summary>
    internal void OnlyInDivisionNow(bool only)
    {
        OnlyInDivision.IsChecked = only;
        OnlyInDivisionClicked(this, new System.Windows.RoutedEventArgs());
    }

    /// <summary>Adds a card to a type of the chosen division, as its heading's + does.</summary>
    internal void AddCostNow(string factory) => AddCost(factory);

    /// <summary>Takes a card of a type of the chosen division away, as Remove in its cost's pop-up does.</summary>
    internal void RemoveCostNow(string factory, int card) => RemoveCost(factory, card);

    /// <summary>Makes a new division with a unit, as "New Division…" does once the name is typed.</summary>
    internal void AddToNewDivisionNow(string name, Unit unit) =>
        MakeNewDivision(name, Data.Divisions.DivisionChanges.StartFor(GameDivisions, unit.Descriptor, unit.Nation, unit.Side)!, [unit]);

    /// <summary>Takes a unit out of the division chosen, as its menu does.</summary>
    internal void RemoveFromDivisionNow(Unit unit) => RemoveFromDivision(chosenDivision!, [unit]);

    /// <summary>Renames a division, as its menu does once the name is typed.</summary>
    internal void RenameNow(Data.Divisions.Division division, string name)
    {
        ChangeDivisions(mod => Data.Divisions.DivisionChanges.Rename(mod, GameDivisions, division.Descriptor, name));
    }

    /// <summary>Duplicates a division, as its menu does once the name is typed.</summary>
    internal void DuplicateNow(Data.Divisions.Division division, string name) => DuplicateAs(division, name);

    /// <summary>Deletes a division, as its menu does once the question is answered.</summary>
    internal void DeleteNow(Data.Divisions.Division division)
    {
        ChangeDivisions(mod => Data.Divisions.DivisionChanges.Delete(mod, division.Descriptor));
    }

    /// <summary>"⚙", as its button does: the Generate Divisions window.</summary>
    internal void OpenGenerateDivisionsNow() => GenerateDivisionsClicked(this, new System.Windows.RoutedEventArgs());

    /// <summary>Makes a generated division, as Generate in the Generate Divisions window does.</summary>
    internal void GenerateNow(Data.Divisions.DivisionGeneration generation, string name) => Generate([new Dialogs.GeneratedDivisionChoice(generation, name)]);

    /// <summary>"Make Fixed" above the chosen generated division's units.</summary>
    internal void MakeFixedNow() => MakeFixedClicked(this, new System.Windows.RoutedEventArgs());

    /// <summary>Whether "⚙ Generation Settings…" shows in the heading.</summary>
    internal bool GenerationSettingsButtonShownNow => GenerationSettingsButton.Visibility == System.Windows.Visibility.Visible;

    /// <summary>"⚙ Generation Settings…", as its button does.</summary>
    internal void OpenGenerationSettingsNow() => GenerationSettingsClicked(this, new System.Windows.RoutedEventArgs());

    /// <summary>Whether the line saying how the chosen division is made shows.</summary>
    internal bool GeneratedLineShownNow => GeneratedLine.Visibility == System.Windows.Visibility.Visible;

    /// <summary>"+ New Unit…", as its button does.</summary>
    internal void OpenNewUnitNow() => OpenNewUnitWindow();

    /// <summary>Duplicates a unit under a name, as Duplicate Unit… does once the name is typed.</summary>
    internal void DuplicateUnitNow(Unit unit, string name)
    {
        ModUnit copy = OpenMod.Units.FirstOrDefault(each => each.Descriptor == unit.Descriptor)?.Copy() ?? new ModUnit { MadeFrom = unit.Descriptor, IsDuplicate = true };
        copy.Name = name;
        copy.Descriptor = ModUnit.NewDescriptor(name, unit.Nation, TakenUnitDescriptors());
        MakeUnit(copy, []);
    }

    /// <summary>Deletes a unit the mod adds, as Delete Unit does once the question is answered.</summary>
    internal void DeleteUnitNow(Unit unit) => ChangeDivisions(mod => ModUnit.RemoveFrom(mod, unit.Descriptor));

    /// <summary>Works the units out again as the open mod makes them, as a change to it does soon after.</summary>
    internal Task ShowModValuesNow() => ShowModValues();

    /// <summary>The division rows that show the pencil of a division the mod changes.</summary>
    internal int ChangedRowsNow => DivisionList.Children.OfType<Divisions.DivisionListRow>().Count(row => row.IsChanged);

    /// <summary>The division rows dimmed and struck through, as taken out of the game's list.</summary>
    internal int RemovedRowsNow => DivisionList.Children.OfType<Divisions.DivisionListRow>().Count(row => row.IsRemoved);

    /// <summary>The loadout a unit's weapon changes are made on; null when not known.</summary>
    internal Data.Weapons.Loadout? OriginalLoadoutNow(Unit unit) => OriginalLoadoutOf(unit);

    /// <summary>Opens the Swap window for a place on a unit, as Swap… in the Unit pane does.</summary>
    internal void SwapWeaponNow(Unit unit, Data.Weapons.LoadoutMount mount) => SwapWeapon(unit, mount);
}
