using EasyNdf.App.Units;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>What the workbench's screenshots and checks use to drive the Units tab without a mouse.</summary>
public partial class UnitsPage
{
    /// <summary>Reads a source at once, without looking for games first: the workbench's made-up install, and its mods.</summary>
    internal Task ReadNow(UnitSource source)
    {
        started = true;
        chosen = source;
        sources = [new UnitSource(source.Game, source.GameFolder, null), .. GameInstall.ModsOf(source.GameFolder).Select(mod => new UnitSource(source.Game, source.GameFolder, mod))];
        FillSourceList();
        fillingLists = true;
        SourceChoice.SelectedItem = ItemOf(source);
        fillingLists = false;
        return Read(source);
    }

    /// <summary>Every unit read.</summary>
    internal IReadOnlyList<Unit> AllUnits => loaded?.Catalog.Units ?? [];

    /// <summary>Shows Cards, Table or Compare, as its button does.</summary>
    internal void ShowViewNow(string name) =>
        ViewClicked(name == "Table" ? TableButton : name == "Compare" ? CompareButton : CardsButton, new System.Windows.RoutedEventArgs());

    /// <summary>Selects a unit, as a click on it does.</summary>
    internal void SelectNow(Unit unit) => selection.Selected = unit;

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
}
