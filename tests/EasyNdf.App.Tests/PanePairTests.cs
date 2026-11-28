using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;

namespace EasyNdf.App.Tests;

/// <summary>
/// A tab's two panes keep their place in the grid when only their sizes change (the Armory's sub-tabs and divisions
/// change the part of the second pane shown), and are laid out exactly as a pair laid out anew at that size.
/// </summary>
public sealed class PanePairTests
{
    [Fact]
    public void Changing_the_part_shown_keeps_the_same_panes_and_sizes_them_as_a_new_pair() => WindowThread.Run(() =>
    {
        PanePair pair = NewPair("pairPart");
        List<UIElement> before = [.. pair.Children.Cast<UIElement>()];
        pair.SecondPart = 0.4;
        Assert.Equal(before, pair.Children.Cast<UIElement>());

        PanePair fresh = NewPair("pairPart", part: 0.4);
        Assert.Equal(Layout(fresh), Layout(pair));
    });

    [Fact]
    public void Folding_and_opening_again_gives_the_same_panes_in_the_same_places() => WindowThread.Run(() =>
    {
        PanePair pair = NewPair("pairFold");
        List<UIElement> before = [.. pair.Children.Cast<UIElement>()];
        string layoutBefore = Layout(pair);
        pair.Fold(true);
        Assert.Equal(3, pair.Children.Count);
        Assert.Equal("Auto", pair.ColumnDefinitions[2].Width.ToString());
        pair.Fold(false);
        Assert.Equal(before, pair.Children.Cast<UIElement>());
        Assert.Equal(layoutBefore, Layout(pair));
    });

    [Fact]
    public void A_new_main_pane_takes_the_old_ones_place() => WindowThread.Run(() =>
    {
        PanePair pair = NewPair("pairMain");
        var newMain = new Border();
        pair.Main = newMain;
        Assert.Same(newMain, pair.Children[0]);
        Assert.Equal(3, pair.Children.Count);
        Assert.Equal(0, Grid.GetColumn(newMain));
    });

    private static PanePair NewPair(string key, double part = 1)
    {
        var pair = new PanePair { Key = key };
        if (part != 1) pair.SecondPart = part;
        pair.Main = new Border();
        pair.Second = new Border();
        return pair;
    }

    /// <summary>The grid's columns and rows, and where each pane is, written out so two pairs can be compared.</summary>
    private static string Layout(PanePair pair) =>
        string.Join(" ", pair.ColumnDefinitions.Select(column => $"col {column.Width} min {column.MinWidth}"))
        + " | " + string.Join(" ", pair.RowDefinitions.Select(row => $"row {row.Height} min {row.MinHeight}"))
        + " | " + string.Join(" ", pair.Children.Cast<UIElement>().Select(pane => $"{pane.GetType().Name} {Grid.GetRow(pane)},{Grid.GetColumn(pane)} {pane.Visibility}"));
}
