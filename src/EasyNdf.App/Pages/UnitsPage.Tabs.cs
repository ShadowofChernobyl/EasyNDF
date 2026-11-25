using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Armory's tabs as the open mod has them (WARNO): their order, labels and tooltips, the spare tabs it brought in;
/// and a tab's right-click menu, which edits it, moves it, removes it or brings in another (<see cref="ModTab"/>).
/// Another game's tabs are the game's own, as they are.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The tabs to show: WARNO's in the open mod's order, with any spare tab a unit is in; another game's own.</summary>
    private IReadOnlyList<string> TabsShown()
    {
        IReadOnlyList<Unit> units = loaded?.Catalog.Units ?? [];
        if (Game != GameTitle.Warno) return UnitNames.FactoriesOf(Game, units);
        List<string> order = TabChangeRules.OrderOf(OpenMod, units.Select(unit => unit.Factory));
        // a tab some unit is in that neither the game nor the mod knows is still shown, last
        return [.. order, .. units.Select(unit => unit.Factory).Where(tab => tab != "" && !order.Contains(tab)).Distinct()];
    }

    /// <summary>A tab's label: the open mod's, or the app's own for it.</summary>
    private string TabLabel(string tab) => Game == GameTitle.Warno && ModTab.Of(OpenMod, tab)?.Label is { Length: > 0 } label ? label : UnitNames.FactoryShort(tab);

    /// <summary>A tab's tooltip: the open mod's tooltip title, or the app's own name for the tab.</summary>
    private string TabTip(string tab) => Game == GameTitle.Warno && ModTab.Of(OpenMod, tab)?.TipTitle is { Length: > 0 } title ? title : UnitNames.Factory(tab);

    /// <summary>Each unit's tab as the Armory shows it, for the cost rows a tab's divisions need (<see cref="ArmoryPresets.Update"/>).</summary>
    private Dictionary<string, string> ShownTabs()
    {
        var tabs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Unit unit in loaded?.Catalog.Units ?? []) tabs[unit.Descriptor] = unit.Factory;
        return tabs;
    }

    /// <summary>The spare tabs the open mod has not brought in and no unit is in.</summary>
    private List<string> SpareTabsLeft()
    {
        IReadOnlyList<string> shown = TabsShown();
        return [.. TabChangeRules.SpareTabs.Where(tab => !shown.Contains(tab))];
    }

    /// <summary>A tab's menu: Edit Tab, Move Left and Move Right, Remove Tab for a spare one of the mod's with no units, New Tab.</summary>
    private void OpenTabMenu(string tab, FrameworkElement at)
    {
        if (Game != GameTitle.Warno) return;
        IReadOnlyList<string> shown = TabsShown();
        int place = shown.ToList().IndexOf(tab);
        bool isSpare = TabChangeRules.SpareTabs.Contains(tab);
        bool hasUnits = (loaded?.Catalog.Units ?? []).Any(unit => unit.Factory == tab);
        // a tab's own text is bold: its menu's is not
        Menus.Show(at,
        [
            Menus.Heading(TabTip(tab)),
            new MenuChoice(Texts.Get("Units.Tabs.Menu.Edit"), () => _ = EditTab(tab)),
            new MenuChoice(Texts.Get("Units.Tabs.Menu.MoveLeft"), () => MoveTab(tab, -1), IsEnabled: place > 0),
            new MenuChoice(Texts.Get("Units.Tabs.Menu.MoveRight"), () => MoveTab(tab, +1), IsEnabled: place >= 0 && place < shown.Count - 1),
            isSpare ? new MenuChoice(Texts.Get("Units.Tabs.Menu.Remove"), () => RemoveTab(tab), IsEnabled: !hasUnits && ModTab.Of(OpenMod, tab) is not null,
                Tip: Texts.Get("Units.Tabs.Menu.Remove.Tip")) : null,
            Menus.Line,
            NewTabChoice(),
        ]).FontWeight = FontWeights.Normal;
    }

    private MenuChoice NewTabChoice() =>
        new(Texts.Get("Units.Tabs.Menu.New"), () => _ = NewTab(), IsEnabled: SpareTabsLeft().Count > 0, Tip: SpareTabsLeft().Count > 0 ? "" : Texts.Get("Units.Tabs.New.None"));

    /// <summary>Edit Tab: the label, the tooltip and, for a spare tab, the cost row; a box left empty keeps the game's.</summary>
    private async Task EditTab(string tab)
    {
        bool isSpare = TabChangeRules.SpareTabs.Contains(tab) && !TabChangeRules.HasGameDescription(tab) || tab == "Defense";
        ModTab now = ModTab.Of(OpenMod, tab)?.Copy() ?? new ModTab { Factory = tab };
        if (isSpare && now.Slots is null && !DivisionsHaveRowFor(tab)) now.Slots = [.. TabChangeRules.UsualSlots];
        if (await MainView.Of(this).TabEditor.Edit(now, GameTextsOf(tab), withSlots: now.Slots is not null) is not ModTab kept) return;
        ChangeTabs(mod =>
        {
            mod.Tabs.RemoveAll(each => each.Factory == tab);
            if (!kept.ChangesNothing) mod.Tabs.Add(kept);
        });
    }

    /// <summary>
    /// A tab's label and tooltip texts without the mod, which Edit Tab's boxes start with: the game's own, or for a
    /// tab it has no description for, the label the tab gets when none is given.
    /// </summary>
    private ModTab GameTextsOf(string tab)
    {
        var own = new ModTab { Factory = tab, Label = UnitNames.FactoryShort(tab) };
        if (TabChangeRules.GameTokensOf(tab) is not var (label, title, text, more) || loaded is null) return own;
        own.Label = loaded.Texts.Get(label) ?? own.Label;
        own.TipTitle = loaded.Texts.Get(title);
        own.TipText = loaded.Texts.Get(text);
        own.TipMore = loaded.Texts.Get(more);
        return own;
    }

    /// <summary>Whether the source's divisions have cost rows for a tab already: Defense has, in every division.</summary>
    private bool DivisionsHaveRowFor(string tab) => GameDivisions.Count > 0 && GameDivisions.All(division => division.Costs.ContainsKey(tab));

    /// <summary>New Tab: one of the spare tabs left, with its label, tooltip and cost row.</summary>
    private async Task NewTab()
    {
        List<string> left = SpareTabsLeft();
        if (left.Count == 0) return;
        if (await MainView.Of(this).TabEditor.New([.. left.Select(tab => (tab, UnitNames.FactoryShort(tab)))]) is not ModTab made) return;
        if (DivisionsHaveRowFor(made.Factory)) made.Slots = null;
        ChangeTabs(mod =>
        {
            mod.Tabs.RemoveAll(each => each.Factory == made.Factory);
            mod.Tabs.Add(made);
        });
    }

    /// <summary>Move Left and Move Right: the tab changes places with the one beside it, in the game and here.</summary>
    private void MoveTab(string tab, int direction)
    {
        List<string> order = [.. TabsShown()];
        int at = order.IndexOf(tab), to = at + direction;
        if (at < 0 || to < 0 || to >= order.Count) return;
        (order[at], order[to]) = (order[to], order[at]);
        ChangeTabs(mod => mod.TabOrder = order.SequenceEqual(TabChangeRules.OrderOf(new Mod { Tabs = mod.Tabs }, order)) ? null : order);
    }

    /// <summary>Remove Tab: a spare tab of the mod's with no units goes, with its place in the order.</summary>
    private void RemoveTab(string tab)
    {
        // the tab shown goes: every unit is shown instead of none
        if (filter.Factory == tab) filter.Factory = "";
        ChangeTabs(mod =>
        {
            mod.Tabs.RemoveAll(each => each.Factory == tab);
            mod.TabOrder?.Remove(tab);
            if (mod.TabOrder is { Count: 0 }) mod.TabOrder = null;
        });
    }

    /// <summary>A change to the open mod's tabs, saved as its other Armory changes are; the tab row is drawn again.</summary>
    private void ChangeTabs(Action<Mod> change)
    {
        if (!ChangeDivisions(change)) return;
        factoryTabsDrawnFor = null;
        ShowAll();
    }

    /// <summary>A spare tab units are moved to is brought in with the usual cost row, when the mod has not got it yet.</summary>
    private static void BringInTab(Mod mod, string tab, bool divisionsHaveRow)
    {
        if (!TabChangeRules.SpareTabs.Contains(tab) || ModTab.Of(mod, tab) is not null) return;
        mod.Tabs.Add(new ModTab { Factory = tab, Slots = divisionsHaveRow ? null : [.. TabChangeRules.UsualSlots] });
    }
}
