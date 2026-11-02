using System.Windows;

namespace EasyNdf.App.Pages;

/// <summary>
/// The widths of the Mods tab's three columns. The Mods and Rule Sets columns keep the widths they were set to, and the
/// rules take the rest, so only the rules grow and shrink with the window; with "Panes grow with the window" on, the
/// three share the tab's width in proportion instead. None goes below its least width; a grip in each gap moves the
/// line between two columns, and the widths it leaves are kept for next time. A double-click on a grip puts its column
/// back to its first width. When the window is narrower than the columns need, they scroll sideways instead of being cut off.
/// </summary>
public partial class RulesPage
{
    /// <summary>The Mods column's first width, as the HTML app's, at the first window size.</summary>
    private const double ModsFirstWidth = 270;

    /// <summary>The Rule Sets column's first width, as the HTML app's, at the first window size.</summary>
    private const double RuleSetsFirstWidth = 310;

    /// <summary>The rules' first width beside those two, at the first window size.</summary>
    private const double RulesFirstWidth = 620;

    /// <summary>
    /// The narrowest the Mods column can be: a mod's name still has room beside its switch, its Armory letters and its
    /// warning mark (at 180 the name was left about 50 pixels).
    /// </summary>
    private const double ModsLeast = 240;

    /// <summary>The narrowest the Rule Sets column can be: its buttons still fit, two to a line.</summary>
    private const double RuleSetsLeast = 180;

    /// <summary>The narrowest the rules can be left: a rule's name and its buttons still fit.</summary>
    private const double RulesLeast = 280;

    /// <summary>The gap between two columns, which holds the grip.</summary>
    private const double Gap = 14;

    /// <summary>The columns' widths as last chosen with the grips, which the columns keep in proportion; null for the first ones.</summary>
    private double? modsWidth, ruleSetsWidth, rulesWidth;

    /// <summary>Reads the widths kept last time, and makes the grips work.</summary>
    private void SetUpColumnGrips()
    {
        modsWidth = (double?)AppData.Current.State["sizes"]?["rulesModsWidth"];
        ruleSetsWidth = (double?)AppData.Current.State["sizes"]?["rulesRuleSetsWidth"];
        rulesWidth = (double?)AppData.Current.State["sizes"]?["rulesRulesWidth"];
        ModsColumnDefinition.MinWidth = ModsLeast;
        RuleSetsColumn.MinWidth = RuleSetsLeast;
        RulesColumnDefinition.MinWidth = RulesLeast;
        ColumnsScroller.SizeChanged += (_, _) => FitColumnsGrid();
        // "Panes grow with the window" turned on or off
        Controls.PaneArrangement.Changed += ShowColumnWidths;
        ModsSplit.Moved += ModsSplitMoved;
        ModsSplit.Reset += () => modsWidth = null;
        ModsSplit.Settled += () =>
        {
            ShowColumnWidths();
            RememberColumnWidths();
        };
        RuleSetsSplit.Moved += RuleSetsSplitMoved;
        RuleSetsSplit.Reset += () => ruleSetsWidth = null;
        RuleSetsSplit.Settled += () =>
        {
            ShowColumnWidths();
            RememberColumnWidths();
        };
        ShowColumnWidths();
    }

    /// <summary>The width the three columns share, the two gaps left out.</summary>
    private double ColumnsRoom => Math.Max(0, ColumnsGrid.ActualWidth - 2 * Gap);

    /// <summary>The Mods grip dragged: the Mods column reaches to the middle of the gap the mouse is in; the rules take what is left.</summary>
    private void ModsSplitMoved(double x)
    {
        double ruleSets = RuleSetsColumn.ActualWidth;
        double most = ColumnsRoom - ruleSets - RulesLeast;
        double mods = Math.Round(Math.Clamp(x - Gap / 2, ModsLeast, Math.Max(ModsLeast, most)));
        (modsWidth, ruleSetsWidth, rulesWidth) = (mods, ruleSets, Math.Max(RulesLeast, ColumnsRoom - mods - ruleSets));
        ShowColumnWidths();
    }

    /// <summary>The Rule Sets grip dragged: the Rule Sets column reaches to the middle of the gap the mouse is in; the rules take what is left.</summary>
    private void RuleSetsSplitMoved(double x)
    {
        double mods = ModsColumnDefinition.ActualWidth;
        double most = ColumnsRoom - mods - RulesLeast;
        double ruleSets = Math.Round(Math.Clamp(x - Gap / 2 - mods - Gap, RuleSetsLeast, Math.Max(RuleSetsLeast, most)));
        (modsWidth, ruleSetsWidth, rulesWidth) = (mods, ruleSets, Math.Max(RulesLeast, ColumnsRoom - mods - ruleSets));
        ShowColumnWidths();
    }

    /// <summary>
    /// Gives the columns their widths: the Mods and Rule Sets columns the widths chosen, or the first ones, and the rules
    /// the rest. With "Panes grow with the window" on, the widths are taken as proportions instead, so a window made
    /// smaller or larger keeps the columns as they were set out.
    /// </summary>
    private void ShowColumnWidths()
    {
        if (Controls.PanePair.GrowsWithWindow)
        {
            ModsColumnDefinition.Width = new GridLength(modsWidth ?? ModsFirstWidth, GridUnitType.Star);
            RuleSetsColumn.Width = new GridLength(ruleSetsWidth ?? RuleSetsFirstWidth, GridUnitType.Star);
            RulesColumnDefinition.Width = new GridLength(rulesWidth ?? RulesFirstWidth, GridUnitType.Star);
        }
        else
        {
            ModsColumnDefinition.Width = new GridLength(modsWidth ?? ModsFirstWidth);
            RuleSetsColumn.Width = new GridLength(ruleSetsWidth ?? RuleSetsFirstWidth);
            RulesColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
        }
        FitColumnsGrid();
    }

    /// <summary>
    /// The columns fill the tab, but are never narrower together than they need: their least widths, or with fixed
    /// widths the Mods and Rule Sets columns' own widths beside the rules' least. Below that they scroll sideways.
    /// </summary>
    private void FitColumnsGrid()
    {
        double needed = Controls.PanePair.GrowsWithWindow
            ? ModsLeast + RuleSetsLeast + RulesLeast
            : (modsWidth ?? ModsFirstWidth) + (ruleSetsWidth ?? RuleSetsFirstWidth) + RulesLeast;
        ColumnsGrid.Width = Math.Max(needed + 2 * Gap, ColumnsScroller.ActualWidth);
    }

    /// <summary>Keeps the proportions for next time; a column put back to its first width forgets its own.</summary>
    private void RememberColumnWidths()
    {
        FilesPage.RememberSize("rulesModsWidth", modsWidth);
        FilesPage.RememberSize("rulesRuleSetsWidth", ruleSetsWidth);
        FilesPage.RememberSize("rulesRulesWidth", modsWidth is null && ruleSetsWidth is null ? null : rulesWidth);
    }
}

/// <summary>
/// The scroller that lets the Mods tab's columns scroll sideways when the window is too narrow for them. It leaves the
/// keys alone: a scroller takes the arrow keys, Home and End for itself, which kept them from the lists inside it and
/// from the tab's shortcuts (Ctrl+Up and Ctrl+Down move a mod).
/// </summary>
public sealed class SidewaysScroller : System.Windows.Controls.ScrollViewer
{
    /// <inheritdoc/>
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
    }
}
