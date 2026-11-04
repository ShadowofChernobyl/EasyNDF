using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using EasyNdf.App.Controls;

namespace EasyNdf.App.Pages;

/// <summary>
/// While the window is resized by its edges, or a bar between panes is dragged, the rows of tiles of a division's cards
/// and of its battlegroup keep the width they had, at the left, cut off or with room beside them; the headings and
/// category bars around them follow. As soon as the mouse stops for a moment, the tiles are laid out for the new width,
/// without waiting for the button to be let go. Laid out again on every move of the mouse, a big division's tiles made
/// each step of the drag take some 100 ms (YSM BEL, 144 units). While held, the tiles are kept as one picture, so the
/// steps of the drag need not draw them again.
/// </summary>
public partial class UnitsPage
{
    /// <summary>How long the mouse must stand still during a resize before the tiles are laid out for the new width.</summary>
    private static readonly TimeSpan ResizePause = TimeSpan.FromMilliseconds(150);

    /// <summary>The rows of tiles held at their width, with the alignment they had before.</summary>
    private readonly List<(UniformGrid Tiles, HorizontalAlignment Alignment)> heldTiles = [];

    private DispatcherTimer? resizeStill;

    /// <summary>Holds the tiles at each step of a drag, and lets them go when the mouse pauses or the drag ends.</summary>
    private void HoldCardsDuringResizes()
    {
        LayoutDrag.Stepped += _ => HoldCards();
        LayoutDrag.Ended += ReleaseCards;
    }

    private void HoldCards()
    {
        resizeStill ??= new DispatcherTimer(ResizePause, DispatcherPriority.Background, (_, _) => ReleaseCards(), Dispatcher) { IsEnabled = false };
        resizeStill.Stop();
        resizeStill.Start();
        if (heldTiles.Count > 0) return;
        foreach (FrameworkElement view in new FrameworkElement[] { DivisionCardsContent, BattlegroupContent })
        {
            if (!view.IsVisible) continue;
            foreach (UniformGrid tiles in Weapons.WeaponsArmory.VisualsIn<UniformGrid>(view))
            {
                // the rows of tiles only, not the grids inside a tile (its veterancy strip)
                if (tiles.ActualWidth <= 0 || IsInsideTile(tiles)) continue;
                heldTiles.Add((tiles, tiles.HorizontalAlignment));
                tiles.Width = tiles.ActualWidth;
                tiles.HorizontalAlignment = HorizontalAlignment.Left;
                // drawn once as a picture at the screen's scaling, rather than again at every step
                tiles.CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(tiles).DpiScaleX) { SnapsToDevicePixels = true };
            }
        }
    }

    /// <summary>Whether an element is part of a tile, as a tile's veterancy strip is.</summary>
    private static bool IsInsideTile(DependencyObject element)
    {
        for (DependencyObject? above = VisualTreeHelper.GetParent(element); above is not null; above = VisualTreeHelper.GetParent(above))
            if (above is Units.UnitTile) return true;
        return false;
    }

    /// <summary>The tiles take the width there is now, as many across as fit.</summary>
    private void ReleaseCards()
    {
        resizeStill?.Stop();
        foreach ((UniformGrid tiles, HorizontalAlignment alignment) in heldTiles)
        {
            tiles.Width = double.NaN;
            tiles.HorizontalAlignment = alignment;
            tiles.CacheMode = null;
        }
        heldTiles.Clear();
        if (drawIsWaiting) return;
        if (ShowsDivisionUnits && view == UnitView.Cards && Units.CardRowView.ColumnsFor(DivisionCardsRoom()) != divisionCardColumns) ShowDivisionColumns();
        // the cards that came into view are made once laid out: the list's size changed while they were held, so no
        // scroll of it will make them (DivisionCardsScrolled), and they stayed empty places
        MakeDivisionTilesSoon();
        if (ShowsBattlegroup && Units.CardRowView.ColumnsFor(RoomIn(BattlegroupScroller)) != battlegroupColumns) DrawBattlegroup();
    }
}
