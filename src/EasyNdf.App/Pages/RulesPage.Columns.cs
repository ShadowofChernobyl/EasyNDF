using System.Windows;

namespace EasyNdf.App.Pages;

/// <summary>
/// The widths of the Mods tab's three columns: a grip in each gap makes the column at its left wider or narrower (the
/// Mods list, then the Rule Sets list); the rules take the rest. The widths are kept for next time; a double-click on a
/// grip puts its column back to its first width.
/// </summary>
public partial class RulesPage
{
    /// <summary>The Mods column's first width, as the HTML app's.</summary>
    private const double ModsFirstWidth = 270;

    /// <summary>The Rule Sets column's first width, as the HTML app's.</summary>
    private const double RuleSetsFirstWidth = 310;

    /// <summary>The narrowest the Mods and Rule Sets columns can be made: their buttons still fit.</summary>
    private const double ListLeast = 180;

    /// <summary>The narrowest the rules can be left: a rule's name and its buttons still fit.</summary>
    private const double RulesLeast = 300;

    /// <summary>The gap between two columns, which holds the grip.</summary>
    private const double Gap = 14;

    /// <summary>The widths chosen with the grips; null for the first widths.</summary>
    private double? modsWidth, ruleSetsWidth;

    /// <summary>Reads the widths kept last time, and makes the grips work.</summary>
    private void SetUpColumnGrips()
    {
        modsWidth = (double?)AppData.Current.State["sizes"]?["rulesModsWidth"];
        ruleSetsWidth = (double?)AppData.Current.State["sizes"]?["rulesRuleSetsWidth"];
        ModsSplit.Moved += ModsSplitMoved;
        ModsSplit.Reset += () => modsWidth = null;
        ModsSplit.Settled += () =>
        {
            ShowColumnWidths();
            FilesPage.RememberSize("rulesModsWidth", modsWidth);
        };
        RuleSetsSplit.Moved += RuleSetsSplitMoved;
        RuleSetsSplit.Reset += () => ruleSetsWidth = null;
        RuleSetsSplit.Settled += () =>
        {
            ShowColumnWidths();
            FilesPage.RememberSize("rulesRuleSetsWidth", ruleSetsWidth);
        };
        ShowColumnWidths();
    }

    /// <summary>The Mods grip dragged: the Mods column reaches to the middle of the gap the mouse is in.</summary>
    private void ModsSplitMoved(double x)
    {
        double most = ColumnsGrid.ActualWidth - Gap - RuleSetsColumn.ActualWidth - Gap - RulesLeast;
        modsWidth = Math.Round(Math.Clamp(x - Gap / 2, ListLeast, Math.Max(ListLeast, most)));
        ShowColumnWidths();
    }

    /// <summary>The Rule Sets grip dragged: the Rule Sets column reaches to the middle of the gap the mouse is in.</summary>
    private void RuleSetsSplitMoved(double x)
    {
        double left = ModsColumn.ActualWidth + Gap;
        double most = ColumnsGrid.ActualWidth - left - Gap - RulesLeast;
        ruleSetsWidth = Math.Round(Math.Clamp(x - Gap / 2 - left, ListLeast, Math.Max(ListLeast, most)));
        ShowColumnWidths();
    }

    /// <summary>Gives the columns their widths, the chosen ones or the first ones.</summary>
    private void ShowColumnWidths()
    {
        ModsColumn.Width = modsWidth ?? ModsFirstWidth;
        RuleSetsColumn.Width = new GridLength(ruleSetsWidth ?? RuleSetsFirstWidth);
    }
}
