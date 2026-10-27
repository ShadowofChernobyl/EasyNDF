using System.Windows;
using System.Windows.Controls;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// Switching the Armory's sub-tabs shows a view again as it was drawn, when nothing it shows has changed since: making
/// a division's tiles, its battlegroup's, the division list and the panes again for each switch was what made it slow.
/// Every other change (the source, the mod, a filter, the zoom, the view, the language) counts as a change, so after
/// it each view is drawn again when it is next shown. A view drawn again in place (a category folded, the tiles laid
/// out for a new width) stays up to date by itself.
/// </summary>
public partial class UnitsPage
{
    /// <summary>Counts the changes to what the Armory shows; a view drawn before the last one is drawn again when shown.</summary>
    private int changesShown;

    /// <summary>
    /// What a view was drawn for: the count of changes then, the division it showed, the sub-tab, and the unit
    /// selected (for the Unit pane). Two alike mean the view would be drawn the same.
    /// </summary>
    private sealed record DrawnFor(int Changes, Division? Division, ArmoryPart Part, Unit? Selected = null);

    private DrawnFor? divisionListDrawnFor, divisionCardsDrawnFor, battlegroupDrawnFor, unitCardsDrawnFor, unitPaneDrawnFor, divisionPaneDrawnFor;
    private DrawnFor? nationListDrawnFor, factoryTabsDrawnFor;

    /// <summary>Something the Armory shows changed: each view is drawn again when it is next shown.</summary>
    private void ShownThingsChanged() => changesShown++;

    /// <summary>What a view would be drawn for now.</summary>
    private DrawnFor DrawnForNow() => new(changesShown, chosenDivision, part);

    /// <summary>
    /// What the Unit pane would be drawn for now: the unit selected. Neither the sub-tab nor the division changes it;
    /// its Show in Units button is shown or hidden for the sub-tab without drawing it again.
    /// </summary>
    private DrawnFor UnitPaneDrawnForNow() => new(changesShown, null, ArmoryPart.Units, selection.Selected);

    /// <summary>
    /// What the nation list's and the type tabs' counts would be drawn for now: they count a division's units on the
    /// Divisions sub-tab, and every unit on the others, so Units and Battlegroups share them.
    /// </summary>
    private DrawnFor CountsDrawnForNow() => new(changesShown, ShowsDivisionUnits ? chosenDivision : null, ArmoryPart.Units);

    /// <summary>
    /// Whether a view can be shown as it was drawn: on a sub-tab switch (<paramref name="mayKeep"/>), when it was drawn
    /// for what it would be drawn for now.
    /// </summary>
    private static bool IsDrawnFor(DrawnFor? drawnFor, DrawnFor now, bool mayKeep) => mayKeep && drawnFor == now;

    /// <summary>
    /// Shows the cards view of the sub-tab: as it was drawn, when it can be, or drawn now. A view kept as it was stops
    /// one that was waiting to be drawn (<see cref="DrawWhenLaidOut"/>): only the one shown is drawn.
    /// </summary>
    private void ShowCardsView(FrameworkElement cardsView, bool wasHidden, bool mayKeep)
    {
        DrawnFor now = DrawnForNow();
        if (ShowsBattlegroup)
        {
            if (IsDrawnFor(battlegroupDrawnFor, now, mayKeep)) KeepDrawnView();
            else DrawWhenLaidOut(cardsView, wasHidden, () =>
            {
                ScrollUpForAnotherDivision(BattlegroupScroller, battlegroupDrawnFor, now);
                DrawBattlegroup();
                battlegroupDrawnFor = now;
            });
        }
        else if (ShowsDivisionUnits)
        {
            if (IsDrawnFor(divisionCardsDrawnFor, now, mayKeep)) KeepDrawnView();
            else DrawWhenLaidOut(cardsView, wasHidden, () =>
            {
                ScrollUpForAnotherDivision(DivisionCardsScroller, divisionCardsDrawnFor, now);
                ShowDivisionCards();
                divisionCardsDrawnFor = now;
            });
        }
        else
        {
            if (IsDrawnFor(unitCardsDrawnFor, now, mayKeep)) KeepDrawnView();
            else DrawWhenLaidOut(cardsView, wasHidden, () => { ShowCards(); unitCardsDrawnFor = now; });
        }
    }

    /// <summary>
    /// A view drawn for another division than it showed starts at its top; drawn again for the same one (after a change
    /// to it), it stays where it was scrolled to.
    /// </summary>
    private static void ScrollUpForAnotherDivision(ScrollViewer scroller, DrawnFor? before, DrawnFor now)
    {
        if (before?.Division?.Descriptor != now.Division?.Descriptor) scroller.ScrollToTop();
    }

    /// <summary>A view is shown as it was drawn: a view still waiting to be drawn is not drawn after all.</summary>
    private void KeepDrawnView()
    {
        drawsAsked++;
        drawIsWaiting = false;
    }
}
