using System.Windows.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// The orders a list of units to pick from can be sorted in (the Add Units window, the transports pop-up): the game's
/// own, by name, by cost either way, or by nation. A unit with no cost comes last either way.
/// </summary>
public static class UnitOrder
{
    /// <summary>The orders, by the code a sort dropdown keeps.</summary>
    public const string Game = "", Name = "name", CostLowest = "costUp", CostHighest = "costDown", Nation = "nation";

    /// <summary>Fills a sort dropdown with the orders, the game's first and chosen.</summary>
    public static void Fill(ComboBox choice)
    {
        choice.Items.Clear();
        foreach ((string code, string text) in new[]
        {
            (Game, "Units.Sort.Game"), (Name, "Units.Sort.Name"), (CostLowest, "Units.Sort.CostLowest"), (CostHighest, "Units.Sort.CostHighest"), (Nation, "Units.Sort.Nation"),
        })
            choice.Items.Add(new ComboBoxItem { Content = Texts.Get(text), Tag = code });
        choice.SelectedIndex = 0;
        choice.ToolTip = Texts.Get("Units.Sort.Tip");
    }

    /// <summary>The order a sort dropdown shows.</summary>
    public static string Chosen(ComboBox choice) => (choice.SelectedItem as ComboBoxItem)?.Tag as string ?? Game;

    /// <summary>Units in an order; ties keep the game's order.</summary>
    public static List<Unit> Sort(IEnumerable<Unit> units, string order)
    {
        StringComparer names = StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true);
        return order switch
        {
            Name => [.. units.OrderBy(unit => unit.Name, names)],
            CostLowest => [.. units.OrderBy(unit => unit.Cost is null).ThenBy(unit => unit.Cost)],
            CostHighest => [.. units.OrderBy(unit => unit.Cost is null).ThenByDescending(unit => unit.Cost)],
            Nation => [.. units.OrderBy(unit => UnitNames.Nation(unit.Nation), names).ThenBy(unit => unit.Name, names)],
            _ => [.. units],
        };
    }
}
