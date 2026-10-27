using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Division and Unit buttons in the panes' heading (<see cref="PaneHalfButtons"/>): each folds its own pane away
/// or brings it back. The Division pane and its button show only on the Divisions sub-tab.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The Unit pane's icon: a card with a picture line and two lines of text.</summary>
    private static readonly Geometry UnitIcon = Geometry.Parse("M 1.5,1.5 H 12.5 V 12.5 H 1.5 Z M 1.5,6 H 12.5 M 4,8.5 H 10 M 4,10.5 H 8");

    /// <summary>The Division pane's icon: a shield, as the divisions' emblems.</summary>
    private static readonly Geometry DivisionIcon = Geometry.Parse("M 7,1.5 L 12.5,3.5 V 7 C 12.5,10 10,12 7,13 C 4,12 1.5,10 1.5,7 V 3.5 Z");

    private PaneHalfButtons paneButtons = null!;

    /// <summary>Makes the two buttons and gives them to the panes' heading in place of its single one.</summary>
    private void PreparePaneButtons()
    {
        var division = new PaneHalf(DivisionIcon, () => Texts.Get("Divisions.Pane.Title"), () => Texts.Get("Divisions.Pane.Show"), "unitsDivisionPane")
        {
            IsAvailable = () => ShowsDivisionUnits,
        };
        var unit = new PaneHalf(UnitIcon, () => Texts.Get("Units.Pane.Title"), () => Texts.Get("Units.Pane.Show"), "unitsUnitPane");
        // the Unit button sits over its own pane, right of the Division pane and the bar between them
        paneButtons = new PaneHalfButtons(Panes, division, unit,
            () => DivisionPaneBox.ActualWidth + PanesSplitColumn.ActualWidth, () => ShowPanesSplit(ShowsDivisionUnits));
        DivisionPaneBox.SizeChanged += (_, _) => paneButtons.LineUp();
    }

    /// <summary>The buttons' names, arrows and tooltips as the panes are now.</summary>
    private void ShowPaneButtons() => paneButtons.Show();

    /// <summary>A pane's button clicked, as by the mouse (for the checks).</summary>
    private void PaneButtonClicked(bool isUnit) => paneButtons.Clicked(isLeft: !isUnit);
}
