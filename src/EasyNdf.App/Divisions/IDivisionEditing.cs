using System.Windows;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// What the Division pane's boxes and buttons, and the cost buttons in the division's unit list, change in the division
/// shown; the Armory does it, saving each change in the open mod.
/// </summary>
public interface IDivisionEditing
{
    /// <summary>The nations a division can be of, each with its side: those of the game's divisions.</summary>
    IReadOnlyList<(string Country, string Side)> Nations { get; }

    /// <summary>The division types a division can be of: the game's, by key ("ARMORED").</summary>
    IReadOnlyList<string> Types { get; }

    /// <summary>Renames the division. Returns whether it was saved: a change can be refused (a file it needs is missing).</summary>
    bool Rename(string name);

    /// <summary>Changes the division's details, as <paramref name="change"/> sets them. Returns whether it was saved.</summary>
    bool ChangeDetails(Action<ModDivision> change);

    /// <summary>"Game's…": the game's emblems to choose one from, under <paramref name="button"/>.</summary>
    void ChooseGameEmblem(FrameworkElement button);

    /// <summary>"Picture…": a picture of the user's own for the emblem.</summary>
    void ChooseEmblemPicture();

    /// <summary>"+ Add…": units of the division to show on its card in the game.</summary>
    void AddStandoutUnits();

    /// <summary>Sets what one card of a type costs, in activation points.</summary>
    /// <param name="factory">The type's code: "Tanks".</param>
    /// <param name="card">The card, from 0 for the first.</param>
    /// <param name="cost">Its new cost.</param>
    void ChangeCost(string factory, int card, int cost);

    /// <summary>Takes one card of a type away; the cards after it each move up one.</summary>
    void RemoveCost(string factory, int card);

    /// <summary>One more card of a type, costing as the last one (1 for the first).</summary>
    void AddCost(string factory);

    /// <summary>"+ Add Units…": the Add Units window, to put more units in the division.</summary>
    /// <param name="factory">The type whose "+ Add Units…" was clicked, such as "Tanks", for the window to show that type's units first.</param>
    void AddUnits(string factory);

    /// <summary>Whether the file the division is in has been added on the Files tab, so it can be shown in the File Editor.</summary>
    bool CanShowDivisionInEditor { get; }

    /// <summary>Shows the division in its file in the File Editor.</summary>
    void ShowDivisionInEditor();

    /// <summary>
    /// A unit's cards, units per card, veterancy or need of a transport, as changed in its tile's pop-up or the table.
    /// Returns whether it was saved.
    /// </summary>
    bool ChangeRule(DivisionUnit rule);

    /// <summary>The × on a unit's tile: the unit is taken out of the division, once asked, as the Delete key does.</summary>
    void RemoveUnit(Unit unit);

    /// <summary>Shows the division's Official Battlegroup, on the Battlegroups sub-tab.</summary>
    void ShowBattlegroup();

    /// <summary>The transports the game accepts for a unit (see <see cref="Transports.ValidFor"/>), for its transports pop-up.</summary>
    IReadOnlyList<Unit> TransportsFor(Unit unit);
}
