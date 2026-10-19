using EasyNdf.App.Divisions;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>What the workbench's screenshots and checks use to drive the Divisions tab without a mouse.</summary>
public partial class DivisionsPage
{
    /// <summary>The divisions the list can show: those offered in multiplayer.</summary>
    internal IReadOnlyList<Division> AllDivisions => divisions;

    /// <summary>The division shown on the right.</summary>
    internal Division? ChosenNow => chosen;

    /// <summary>How many divisions the list shows now.</summary>
    internal int ListedNow => DivisionList.Children.OfType<DivisionListRow>().Count();

    /// <summary>Shows a division, as a click in the list does.</summary>
    internal void ChooseNow(Division division) => Choose(division);

    /// <summary>Shows Cards or Table, as their buttons do.</summary>
    internal void ShowTableNow(bool table) => ViewClicked(table ? TableButton : CardsButton, new System.Windows.RoutedEventArgs());

    /// <summary>"Show in Units Tab" on a unit, as its menu does.</summary>
    internal void ShowInUnitsTabNow(Unit unit) => ShowInUnitsTab(unit);

    /// <summary>Selects a unit, as a click on it does.</summary>
    internal void SelectNow(Unit unit) => selection.Selected = unit;

    /// <summary>Adds a unit to a division, as its menu does.</summary>
    internal void AddToDivisionNow(Division division, Unit unit) => AddToDivision(division, unit);

    /// <summary>Makes a new division with a unit, as "Add to New Division…" does once the name is typed.</summary>
    internal void AddToNewDivisionNow(string name, Unit unit) =>
        MakeNewDivision(name, DivisionChanges.StartFor(GameDivisions, unit.Descriptor, unit.Nation, unit.Side)!, unit);

    /// <summary>Takes a unit out of the division shown, as its menu does.</summary>
    internal void RemoveFromDivisionNow(Unit unit) => RemoveFromDivision(chosen!, unit);

    /// <summary>The division rows that show the pencil of a division the mod changes.</summary>
    internal int ChangedRowsNow => DivisionList.Children.OfType<DivisionListRow>().Count(row => row.IsChanged);

    /// <summary>Renames, duplicates or deletes a division, as its menu does once the name is typed or the question answered.</summary>
    internal void RenameNow(Division division, string name)
    {
        DivisionChanges.Rename(OpenMod, GameDivisions, division.Descriptor, name);
        AppData.Current.Save(OpenMod);
    }

    /// <inheritdoc cref="RenameNow"/>
    internal void DuplicateNow(Division division, string name) => DuplicateAs(division, name);

    /// <inheritdoc cref="RenameNow"/>
    internal void DeleteNow(Division division)
    {
        DivisionChanges.Delete(OpenMod, division.Descriptor);
        AppData.Current.Save(OpenMod);
    }

    /// <summary>The division rows dimmed and struck through, as taken out of the game's list.</summary>
    internal int RemovedRowsNow => DivisionList.Children.OfType<DivisionListRow>().Count(row => row.IsRemoved);

    /// <summary>Lists only the divisions whose name holds this text, as typing in Find does.</summary>
    internal void FindNow(string text) => SearchBox.Text = text;
}
