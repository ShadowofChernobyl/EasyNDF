using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// A type's activation-point costs on a line under its heading in a division's unit list: a button for each card,
/// showing what that card costs, then a dashed + to add a card. The line wraps, so a freedom deck's dozens of cards all
/// show. Clicking a card's button opens a small pop-up under it to choose its cost,
/// or to take the card away. The wheel never changes a cost, so scrolling the list cannot change one by accident.
/// </summary>
public static class CostButtons
{
    /// <summary>The highest cost the pop-up offers, unless a card already costs more.</summary>
    private const int HighestOffered = 4;

    /// <summary>The buttons for one type's costs.</summary>
    /// <param name="factory">The type's code: "Tanks".</param>
    /// <param name="costs">What each card of the type costs, the first card's first.</param>
    /// <param name="editing">What changes the division's costs, saved in the open mod.</param>
    public static WrapPanel Build(string factory, IReadOnlyList<int> costs, IDivisionEditing editing)
    {
        var row = new WrapPanel();
        for (int card = 0; card < costs.Count; card++)
        {
            int chosen = card;
            Button button = CostButton(costs[card]);
            button.Margin = new Thickness(0, 0, 2, 2);
            button.ToolTip = Texts.Get("Divisions.Cost.Tip", ("card", Texts.Number(card + 1)), ("cost", Texts.Number(costs[card])));
            MarkIfChanged(button, factory, card);
            button.Click += (_, _) => OpenChoice(button, factory, chosen, costs[chosen], editing);
            row.Children.Add(button);
        }
        var add = new Button { Style = (Style)Application.Current.FindResource("DashedButton"), Content = "+", Padding = new Thickness(0), Width = 20, Height = 20, Margin = new Thickness(0, 0, 2, 2) };
        add.ToolTip = Texts.Get("Divisions.Pane.AddSlot.Tip");
        add.Click += (_, _) => editing.AddCost(factory);
        row.Children.Add(add);
        return row;
    }

    /// <summary>
    /// A card's cost the open mod changed, or a card it adds: its tooltip says what changed it (see
    /// <see cref="DivisionMarks"/>). Unlike other changed values, it keeps its colour and has no "*": the
    /// colours tell the costs apart, and a row of marked costs looked out of place.
    /// </summary>
    private static void MarkIfChanged(Button button, string factory, int card)
    {
        string CostOf(Data.Divisions.Division division) =>
            division.Costs.TryGetValue(factory, out IReadOnlyList<int>? costs) && card < costs.Count ? Texts.Number(costs[card]) : "";
        if (DivisionMarks.TipIfChanged(["Costs:" + factory], CostOf) is string tip) button.ToolTip = tip;
    }

    /// <summary>A cost in its sunken box, coloured by how dear it is.</summary>
    private static Button CostButton(int cost)
    {
        var button = new Button { Style = (Style)Application.Current.FindResource("CostButton"), Content = Texts.Number(cost) };
        button.SetResourceReference(Control.ForegroundProperty, CostColour(cost));
        return button;
    }

    /// <summary>The theme colour a cost is drawn in: 1 as fine, 2 as caution, 3 and more as dear.</summary>
    private static string CostColour(int cost) => cost <= 1 ? "ok" : cost == 2 ? "caution" : "warn";

    /// <summary>
    /// The pop-up under a card's button: which type and card, the costs to choose from with the current one outlined, and
    /// Remove. A choice, Esc or a click elsewhere closes it.
    /// </summary>
    private static void OpenChoice(Button owner, string factory, int card, int current, IDivisionEditing editing)
    {
        var popup = new Popup { PlacementTarget = owner, Placement = PlacementMode.Bottom, VerticalOffset = 2, StaysOpen = false, AllowsTransparency = true };
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        Button? currentButton = null;
        for (int cost = 1; cost <= Math.Max(HighestOffered, current); cost++)
        {
            int picked = cost;
            Button choice = CostButton(cost);
            choice.Click += (_, _) =>
            {
                popup.IsOpen = false;
                if (picked != current) editing.ChangeCost(factory, card, picked);
            };
            // the current cost outlined in the accent colour
            var holder = new Border { Child = choice, Margin = new Thickness(0, 0, 3, 0), BorderThickness = new Thickness(1) };
            if (cost == current)
            {
                holder.SetResourceReference(Border.BorderBrushProperty, "accent");
                currentButton = choice;
            }
            choices.Children.Add(holder);
        }
        var remove = new Button { Content = Texts.Get("Divisions.Cost.Remove"), Margin = new Thickness(6, 0, 0, 0), ToolTip = Texts.Get("Divisions.Cost.Remove.Tip") };
        remove.Click += (_, _) =>
        {
            popup.IsOpen = false;
            editing.RemoveCost(factory, card);
        };
        choices.Children.Add(remove);
        var title = new TextBlock { Text = Texts.Get("Divisions.Cost.Title", ("type", UnitNames.Factory(factory)), ("card", Texts.Number(card + 1))), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 5) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var box = new Bevel { Child = new StackPanel { Children = { title, choices } }, Padding = new Thickness(8, 6, 8, 7) };
        box.SetResourceReference(Bevel.TopLeftProperty, "hi");
        box.SetResourceReference(Bevel.BottomRightProperty, "lo");
        box.SetResourceReference(Bevel.BackgroundProperty, "panel");
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            popup.IsOpen = false;
        };
        popup.Child = box;
        // the keyboard goes to the current cost, so Esc works at once
        popup.Opened += (_, _) => currentButton?.Focus();
        popup.IsOpen = true;
    }
}
