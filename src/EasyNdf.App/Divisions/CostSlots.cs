using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Divisions;

/// <summary>
/// A category's activation-point costs as a row of small sunken boxes, one a card the category allows, each with what
/// that card costs. The tooltip gives the costs and their total.
/// </summary>
public sealed class CostSlots : StackPanel
{
    /// <summary>Builds the row for a category's costs, the first card's first.</summary>
    public CostSlots(IReadOnlyList<int> costs)
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        foreach (int cost in costs) Children.Add(Slot(cost));
        ToolTip = costs.Count == 0
            ? Texts.Get("Divisions.Slots.None")
            : Texts.Get("Divisions.Slots.Tip", ("costs", string.Join(", ", costs.Select(cost => Texts.Number(cost)))), ("total", Texts.Number(costs.Sum())));
    }

    /// <summary>One card's cost, small and bold in a sunken box, 2 pixels from the one before.</summary>
    private static Bevel Slot(int cost)
    {
        var text = new TextBlock { Text = Texts.Number(cost), FontSize = 11, FontWeight = FontWeights.Bold, MinWidth = 8, TextAlignment = TextAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "ink");
        var slot = new Bevel { Child = text, Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(2, 0, 0, 0) };
        slot.SetResourceReference(Bevel.TopLeftProperty, "lo");
        slot.SetResourceReference(Bevel.BottomRightProperty, "hi");
        slot.SetResourceReference(Bevel.BackgroundProperty, "well");
        return slot;
    }
}
