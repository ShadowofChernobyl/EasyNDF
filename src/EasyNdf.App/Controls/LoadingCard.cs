using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace EasyNdf.App.Controls;

/// <summary>
/// The card shown over the dimmed app while something slow loads (a source read in the Armory, the mods applied, a file
/// opened in the Editor): what is loading and from where, a bar that fills block by block, "Step 2 of 3" and one line for
/// the step under way. Always the same width: a long name is cut short rather than widening the card.
/// </summary>
public sealed class LoadingCard : Bevel
{
    /// <summary>What is loading, in the accent colour.</summary>
    public TextBlock TitleText { get; } = new() { FontSize = 14, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };

    /// <summary>From where, or with what, under the title.</summary>
    public TextBlock WhereText { get; } = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 10) };

    /// <summary>"Step 2 of 3", at the title's right.</summary>
    public TextBlock StepCountText { get; } = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, 1) };

    /// <summary>The bar, always the same size.</summary>
    public BlockBar Bar { get; } = new();

    /// <summary>Under the bar: the step under way.</summary>
    public StackPanel StepLines { get; } = new() { Margin = new Thickness(0, 10, 0, 0) };

    /// <summary>Moves the bar on a block at a time while a step runs, towards <see cref="stepEnd"/>.</summary>
    private readonly DispatcherTimer creep = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private double stepEnd;

    public LoadingCard()
    {
        SetResourceReference(TopLeftProperty, "hi");
        SetResourceReference(BottomRightProperty, "lo");
        SetResourceReference(BackgroundProperty, "panel");
        Padding = new Thickness(18, 14, 18, 14);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Width = 420;
        TitleText.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        WhereText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        StepCountText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(StepCountText, Dock.Right);
        Child = new StackPanel { Children = { new DockPanel { Children = { StepCountText, TitleText } }, WhereText, Bar, StepLines } };
        creep.Tick += (_, _) => Creep();
    }

    /// <summary>A new load: its title and where from, the bar empty and creeping.</summary>
    public void Start(string title, string where)
    {
        TitleText.Text = title;
        WhereText.Text = where;
        Bar.Fraction = 0;
        StepLines.Children.Clear();
        creep.Start();
    }

    /// <summary>A step has begun: the bar at least at its start, creeping towards its end, and its line.</summary>
    /// <param name="number">The step, from 1.</param>
    /// <param name="steps">How many there are.</param>
    /// <param name="line">What it does.</param>
    /// <param name="start">Where it starts on the bar, as a share of it.</param>
    /// <param name="end">Where it ends.</param>
    public void ShowStep(int number, int steps, string line, double start, double end)
    {
        Bar.Fraction = Math.Max(Bar.Fraction, start);
        stepEnd = end;
        StepCountText.Text = Text.Texts.Get("Units.Reading.StepCount", ("step", Text.Texts.Number(number)), ("steps", Text.Texts.Number(steps)));
        ShowLine(line);
    }

    /// <summary>The one line under the bar.</summary>
    public void ShowLine(string line)
    {
        var text = new TextBlock { Text = line };
        text.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        StepLines.Children.Clear();
        StepLines.Children.Add(text);
    }

    /// <summary>Where the step under way ends on the bar: the bar creeps towards it but stops a block short.</summary>
    public double StepEnd
    {
        get => stepEnd;
        set => stepEnd = value;
    }

    /// <summary>The load is over: the bar stops.</summary>
    public void Stop() => creep.Stop();

    /// <summary>One block more, unless that would reach the end of the step under way.</summary>
    private void Creep()
    {
        double oneBlock = 1.0 / BlockBar.Blocks;
        if (Bar.Fraction + oneBlock < stepEnd - oneBlock / 2) Bar.Fraction += oneBlock;
    }
}
