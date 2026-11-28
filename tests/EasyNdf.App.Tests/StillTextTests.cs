using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;

namespace EasyNdf.App.Tests;

/// <summary>
/// A line of text on a card stays at its own width while it fits, ends in "…" at the room there is when it does not,
/// and during a drag follows that room in steps, put right when the drag ends.
/// </summary>
public sealed class StillTextTests
{
    private const string Words = "AutoCanon AP 20mm M621 GIAT";

    private static (StillText Line, TextBlock Text) NewLine()
    {
        var text = new TextBlock { Text = Words, TextTrimming = TextTrimming.CharacterEllipsis };
        return (new StillText(text), text);
    }

    private static void LayOut(UIElement element, double width)
    {
        element.Measure(new Size(width, 100));
        element.Arrange(new Rect(0, 0, width, 100));
    }

    [Fact]
    public void A_line_that_fits_stays_at_its_own_width_in_any_room() => WindowThread.Run(() =>
    {
        (StillText line, TextBlock text) = NewLine();
        LayOut(line, 1000);
        double own = text.RenderSize.Width;
        Assert.True(own is > 0 and < 1000);
        LayOut(line, 800);
        Assert.Equal(own, text.RenderSize.Width);
    });

    [Fact]
    public void A_line_that_does_not_fit_takes_the_room_there_is() => WindowThread.Run(() =>
    {
        (StillText line, TextBlock text) = NewLine();
        LayOut(line, 1000);
        LayOut(line, 60);
        Assert.Equal(60, text.RenderSize.Width);
        Assert.Equal(60, line.DesiredSize.Width);
    });

    [Fact]
    public void New_shorter_words_in_a_line_that_was_cut_ask_only_for_their_own_width() => WindowThread.Run(() =>
    {
        (StillText line, TextBlock text) = NewLine();
        LayOut(line, 60);
        text.Text = "AP";
        // as WPF does it: the text by itself first, at the width it had
        text.Measure(new Size(60, double.PositiveInfinity));
        LayOut(line, 60);
        Assert.True(line.DesiredSize.Width < 30, "asked for " + line.DesiredSize.Width);
    });

    [Fact]
    public void During_a_drag_a_cut_line_follows_the_room_in_steps_and_is_put_right_after() => WindowThread.Run(() =>
    {
        (StillText line, TextBlock text) = NewLine();
        LayOut(line, 1000);
        LayoutDrag.Start(LayoutDragKind.Panes);
        try
        {
            LayOut(line, 61);
            Assert.Equal(60, text.RenderSize.Width);
            LayOut(line, 62);
            Assert.Equal(60, text.RenderSize.Width);
        }
        finally
        {
            LayoutDrag.End();
        }
        Assert.False(line.IsMeasureValid);
        LayOut(line, 62);
        Assert.Equal(62, text.RenderSize.Width);
    });
}
