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

    /// <summary>Shows the loading bar as it is part way through reading, for a screenshot; the units shown stay under it.</summary>
    internal void ShowReadingBarNow(UnitReadStep step, double fraction)
    {
        ShowMessage(Text.Texts.Get("Units.Reading", ("name", "WARNO")), offerChooser: false);
        StartReadingBar();
        readingTimer.Stop();
        ShowReadingStep(step);
        ReadingBar.Fraction = fraction;
    }

    /// <summary>Hides the loading bar and the message again.</summary>
    internal void HideReadingBarNow()
    {
        StopReadingBar();
        MessageArea.Visibility = System.Windows.Visibility.Collapsed;
    }

    /// <summary>Sets the zoom of the view shown, as the dropdown does.</summary>
    internal void SetZoomNow(int percent) => SetZoom(percent);

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
