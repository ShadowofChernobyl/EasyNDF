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
        if (Game != GameTitle.Warno)
        {
            // Steel Division 2: the armory screen's order (Recon first), then any tab it does not list (Defense)
            IReadOnlyList<string> known = UnitNames.FactoriesOf(Game, units);
            return loaded?.Catalog.TabOrder is { Count: > 0 } screen
                ? [.. screen.Where(known.Contains), .. known.Where(tab => !screen.Contains(tab))]
                : known;
        }
        List<string> order = TabChangeRules.OrderOf(OpenMod, units.Select(unit => unit.Factory));
        // the order the files give (the game's, or a mod's that changes the armory screen's list), unless the open mod
        // set one of its own with Move Left and Move Right: a tab a modder put second shows second
        if (OpenMod.TabOrder is not { Count: > 0 } && loaded?.Catalog.TabOrder is { Count: > 0 } listed)
            order = [.. listed, .. order.Where(tab => !listed.Contains(tab))];
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

    /// <summary>
    /// Edit Tab: the label and the tooltip; a box left empty keeps the game's. A spare tab's cost rows are the mod's
    /// still: they are set per division in the Divisions view, which the window's link opens on the tab.
    /// </summary>
    private async Task EditTab(string tab)
    {
        bool isSpare = TabChangeRules.SpareTabs.Contains(tab) && !TabChangeRules.HasGameDescription(tab) || tab == "Defense";
        ModTab now = ModTab.Of(OpenMod, tab)?.Copy() ?? new ModTab { Factory = tab };
        // a spare tab its divisions have no costs for gets the usual row, so the mod's generation does not stop
        if (isSpare && now.Slots is null && !DivisionsHaveRowFor(tab)) now.Slots = [.. TabChangeRules.UsualSlots];
        Dialogs.TabWindow window = MainView.Of(this).TabEditor;
        if (await window.Edit(now, GameTextsOf(tab)) is not ModTab kept) return;
        kept.Slots = now.Slots;
        kept.SlotsFor = now.SlotsFor is null ? null : new Dictionary<string, List<int>>(now.SlotsFor, StringComparer.Ordinal);
        ChangeTabs(mod =>
        {
            mod.Tabs.RemoveAll(each => each.Factory == tab);
            if (!kept.ChangesNothing) mod.Tabs.Add(kept);
        });
        if (window.WentToCosts) ShowCostsOf(tab);
    }

    /// <summary>
    /// The Divisions view on a tab, in Cards, where its heading has the division's cost buttons: the division shown
    /// when it offers a unit of the tab, else the first listed that does (a division without one has no row for it).
    /// </summary>
    private void ShowCostsOf(string tab)
    {
        if (view != UnitView.Cards) ViewClicked(CardsButton, new RoutedEventArgs());
        bool Offers(Division division) => division.Units.Any(unit => unitsByDescriptor.TryGetValue(unit.Unit, out Unit? found) && found.Factory == tab);
        if (chosenDivision is null || !Offers(chosenDivision))
            if (divisions.FirstOrDefault(Offers) is Division offering) ChooseDivision(offering);
        filter.Factory = tab;
        if (part != ArmoryPart.Divisions) ShowPart(ArmoryPart.Divisions);
        else ShowAll();
    }

    /// <summary>
    /// The cost row a division gets for a tab of the mod's that the division has no costs for (a spare tab), when it
    /// offers a unit of the tab: the division's own row, else the tab's (<see cref="TabChangeRules"/> adds it as the
    /// mod is generated). Null for any other tab or division.
    /// </summary>
    private List<int>? TabRowOf(Division division, string tab)
    {
        if (Game != GameTitle.Warno || division.Costs.ContainsKey(tab) || ModTab.Of(OpenMod, tab) is not ModTab own) return null;
        if (!division.Units.Any(unit => unitsByDescriptor.TryGetValue(unit.Unit, out Unit? found) && found.Factory == tab)) return null;
        return own.SlotsOf(division.Descriptor) is { Count: > 0 } row ? [.. row] : null;
    }

    /// <summary>A division as the Armory shows it: with the cost rows the mod's tabs give it (<see cref="TabRowOf"/>).</summary>
    private Division WithTabRows(Division division)
    {
        Dictionary<string, IReadOnlyList<int>>? costs = null;
        foreach (ModTab tab in OpenMod.Tabs)
            if (TabRowOf(division, tab.Factory) is List<int> row)
            {
                costs ??= new Dictionary<string, IReadOnlyList<int>>(division.Costs, StringComparer.Ordinal);
                costs[tab.Factory] = row;
            }
        return costs is null ? division : division with { Costs = costs };
    }

    /// <summary>
    /// A cost button changed a tab row the mod's tab gives (<see cref="TabRowOf"/>): the row becomes the division's own,
    /// for the tab. False when the tab gives the division no row: its costs are changed as any other.
    /// </summary>
    private bool ChangeTabRow(string tab, Action<List<int>> change)
    {
        if (chosenDivision is not Division division || TabRowOf(division, tab) is not List<int> row) return false;
        change(row);
        if (row.Count == 0) row.Add(1);
        string descriptor = division.Descriptor;
        ChangeTabs(mod =>
        {
            if (ModTab.Of(mod, tab) is not ModTab own) return;
            own.SlotsFor ??= new Dictionary<string, List<int>>(StringComparer.Ordinal);
            if (own.Slots is not null && row.SequenceEqual(own.Slots)) own.SlotsFor.Remove(descriptor);
            else own.SlotsFor[descriptor] = row;
            if (own.SlotsFor.Count == 0) own.SlotsFor = null;
        });
        return true;
    }

    /// <summary>
    /// A tab's label and tooltip texts without the mod, which Edit Tab's boxes start with: the game's own, or for a
    /// tab it has no description for, the label the tab gets when none is given.
    /// </summary>
    private ModTab GameTextsOf(string tab)
    {
        var own = new ModTab { Factory = tab, Label = UnitNames.FactoryShort(tab) };
        // the game has no tooltip of its own for Support: it shows Artillery's ("The King of the Battlefield")
        if (TabChangeRules.GameTokensOf(tab == "Support" ? "Art" : tab) is not var (label, title, text, more) || loaded is null) return own;
        if (tab == "Support") label = "";
        own.Label = loaded.Texts.Get(label) ?? own.Label;
        own.TipTitle = loaded.Texts.Get(title);
        own.TipText = loaded.Texts.Get(text);
        own.TipMore = loaded.Texts.Get(more);
        return own;
    }

    /// <summary>Whether the source's divisions have cost rows for a tab already: Defense has, in every division.</summary>
    private bool DivisionsHaveRowFor(string tab) => GameDivisions.Count > 0 && GameDivisions.All(division => division.Costs.ContainsKey(tab));

    /// <summary>New Tab: one of the spare tabs left, with its label and tooltip, and the usual cost row for its divisions.</summary>
    private async Task NewTab()
    {
        List<string> left = SpareTabsLeft();
        if (left.Count == 0) return;
        Dialogs.TabWindow window = MainView.Of(this).TabEditor;
        if (await window.New([.. left.Select(tab => (tab, UnitNames.FactoryShort(tab)))], GameTextsOf) is not ModTab made) return;
        made.Slots = DivisionsHaveRowFor(made.Factory) ? null : [.. TabChangeRules.UsualSlots];
        ChangeTabs(mod =>
        {
            mod.Tabs.RemoveAll(each => each.Factory == made.Factory);
            mod.Tabs.Add(made);
        });
        if (window.WentToCosts) ShowCostsOf(made.Factory);
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
