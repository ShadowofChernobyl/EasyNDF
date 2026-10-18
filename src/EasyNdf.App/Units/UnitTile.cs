using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>Which unit is selected, shared by the tiles and rows that show units, so each can show whether it is the one.</summary>
public sealed class UnitSelection
{
    private Unit? selected;

    /// <summary>The selected unit; null for none.</summary>
    public Unit? Selected
    {
        get => selected;
        set
        {
            if (ReferenceEquals(selected, value)) return;
            selected = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Raised when another unit is selected.</summary>
    public event Action? Changed;
}

/// <summary>
/// A unit as a raised tile, for the Cards view: its picture, its cost in a small sunken box over the picture's
/// corner, its name in bold and its nation under it. Under the mouse the name turns the accent colour; selected, the
/// tile is sunk, on the selection colour.
/// </summary>
public sealed class UnitTile : Grid
{
    private readonly Bevel tile = new() { Padding = new Thickness(4) };
    private readonly TextBlock name;
    private readonly UnitSelection selection;

    /// <summary>The unit shown.</summary>
    public Unit Unit { get; }

    /// <summary>Builds the tile of a unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="firstSide">Whether the unit is on the game's first side (NATO, the Allies), for its nation's mark.</param>
    /// <param name="selection">Which unit is selected.</param>
    public UnitTile(Unit unit, bool firstSide, UnitSelection selection)
    {
        Unit = unit;
        this.selection = selection;
        Cursor = Cursors.Hand;
        Background = System.Windows.Media.Brushes.Transparent;

        name = new TextBlock { Text = unit.Name, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0), ToolTip = unit.Name };
        var nation = new TextBlock { Text = UnitNames.Nation(unit.Nation), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
        nation.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var nationLine = new DockPanel { Margin = new Thickness(0, 2, 0, 0), Children = { new NationMark(unit.Nation, firstSide), nation } };
        tile.Child = new StackPanel { Children = { new UnitPicture { Source = unit.PictureSource, Factory = unit.Factory }, name, nationLine } };
        Children.Add(tile);
        if (unit.Cost is not null) Children.Add(CostBadge(unit.Cost));

        ShowState();
        MouseEnter += (_, _) => ShowState();
        MouseLeave += (_, _) => ShowState();
        // the tile follows the selection while it is on screen
        Loaded += (_, _) => { selection.Changed += ShowState; ShowState(); };
        Unloaded += (_, _) => selection.Changed -= ShowState;
    }

    /// <summary>The cost, small and bold in a sunken box, 7 pixels in from the tile's top right corner.</summary>
    private static Bevel CostBadge(double? cost)
    {
        var text = new TextBlock { Text = UnitNumbers.Of(cost), FontSize = 11, FontWeight = FontWeights.Bold };
        var badge = new Bevel { Child = text, Padding = new Thickness(5, 0, 5, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 7, 0), ToolTip = Texts.Get("Units.Stat.Cost") };
        badge.SetResourceReference(Bevel.TopLeftProperty, "lo");
        badge.SetResourceReference(Bevel.BottomRightProperty, "hi");
        badge.SetResourceReference(Bevel.BackgroundProperty, "well");
        return badge;
    }

    /// <summary>Raised, or sunk on the selection colour when selected; the name in the accent colour when selected or under the mouse.</summary>
    private void ShowState()
    {
        bool isSelected = ReferenceEquals(selection.Selected, Unit);
        tile.SetResourceReference(Bevel.TopLeftProperty, isSelected ? "lo" : "hi");
        tile.SetResourceReference(Bevel.BottomRightProperty, isSelected ? "hi" : "lo");
        tile.SetResourceReference(Bevel.BackgroundProperty, isSelected ? "sel" : "panel");
        name.SetResourceReference(TextBlock.ForegroundProperty, isSelected || IsMouseOver ? "accent" : "ink");
    }
}
