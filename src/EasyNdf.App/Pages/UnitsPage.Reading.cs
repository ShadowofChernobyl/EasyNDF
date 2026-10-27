using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// While a source is read: the tab's layout is there already, its list empty, and over the dimmed page a card says
/// what is read and from where, with a bar that fills block by block and the four steps ticked off as they finish.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The steps of reading, in order, as the reader reports them.</summary>
    private static readonly UnitReadStep[] ReadingStepsInOrder =
        [UnitReadStep.FindingFiles, UnitReadStep.ReadingNames, UnitReadStep.ReadingUnits, UnitReadStep.PreparingPictures];

    /// <summary>
    /// Where each step of reading starts and ends on the bar, as shares of it. Reading the units takes the most time.
    /// The shares are a first guess, to be tuned by timing a game's read: the steps cannot say how far through
    /// themselves they are.
    /// </summary>
    private static (double Start, double End) ShareOf(UnitReadStep step) => step switch
    {
        UnitReadStep.FindingFiles => (0, 0.2),
        UnitReadStep.ReadingNames => (0.2, 0.4),
        UnitReadStep.ReadingUnits => (0.4, 0.9),
        _ => (0.9, 1),
    };

    /// <summary>Moves the bar on a block at a time while a step runs.</summary>
    private readonly System.Windows.Threading.DispatcherTimer readingTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool readingTimerStarted;

    /// <summary>Where the step being read ends on the bar: the bar creeps towards it but stops a block short.</summary>
    private double readingStepEnd;

    /// <summary>Shows the layout, its list empty, and the reading card over the dimmed page, its bar empty.</summary>
    private void StartReading(UnitSource source)
    {
        MessageArea.Visibility = Visibility.Collapsed;
        CountText.Text = "";
        ShowEmptyLayout();
        ReadingTitle.Text = Texts.Get("Units.Reading.Title", ("name", source.Name));
        ReadingWhere.Text = source.Folder;
        ReadingBar.Fraction = 0;
        isReading = true;
        ShowReadingCover();
        ShowReadingStep(UnitReadStep.FindingFiles);
        if (!readingTimerStarted)
        {
            readingTimer.Tick += (_, _) => CreepReadingBar();
            readingTimerStarted = true;
        }
        readingTimer.Start();
    }

    /// <summary>
    /// The layout as it will be, before the units: the Cards and Unit panes, the factory tabs' row and the list empty
    /// (a source read before this one is not left showing under the card).
    /// </summary>
    private void ShowEmptyLayout()
    {
        CompareBox.Visibility = Visibility.Collapsed;
        Panes.Visibility = Visibility.Visible;
        FactoryTabs.Children.Clear();
        CardsList.ItemsSource = null;
    }

    /// <summary>A step has begun: the bar is at least at its start, and the steps before it are ticked.</summary>
    private void ShowReadingStep(UnitReadStep step)
    {
        (double start, double end) = ShareOf(step);
        ReadingBar.Fraction = Math.Max(ReadingBar.Fraction, start);
        readingStepEnd = end;
        int now = Array.IndexOf(ReadingStepsInOrder, step);
        ReadingStepCount.Text = Texts.Get("Units.Reading.StepCount", ("step", Texts.Number(now + 1)), ("steps", Texts.Number(ReadingStepsInOrder.Length)));
        ReadingSteps.Children.Clear();
        for (int i = 0; i < ReadingStepsInOrder.Length; i++)
            ReadingSteps.Children.Add(StepLine(ReadingStepsInOrder[i], i < now ? StepState.Done : i == now ? StepState.Now : StepState.ToCome));
    }

    private enum StepState { Done, Now, ToCome }

    /// <summary>A step's line: ✓ in the accent colour once done, ● and bold in the accent colour while it runs, ○ dim before.</summary>
    private static DockPanel StepLine(UnitReadStep step, StepState state)
    {
        var mark = new TextBlock { Text = state switch { StepState.Done => "✓", StepState.Now => "●", _ => "○" }, Width = 14, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var name = new TextBlock { Text = Texts.Get(StepTextName(step)), FontWeight = state == StepState.Now ? FontWeights.Bold : FontWeights.Normal };
        mark.SetResourceReference(TextBlock.ForegroundProperty, state == StepState.ToCome ? "muted" : "accent");
        name.SetResourceReference(TextBlock.ForegroundProperty, state switch { StepState.Done => "ink", StepState.Now => "accent", _ => "muted" });
        return new DockPanel { Margin = new Thickness(0, 2, 0, 2), Children = { mark, name } };
    }

    private static string StepTextName(UnitReadStep step) => step switch
    {
        UnitReadStep.FindingFiles => "Units.Step.FindingFiles",
        UnitReadStep.ReadingNames => "Units.Step.ReadingNames",
        UnitReadStep.ReadingUnits => "Units.Step.ReadingUnits",
        _ => "Units.Step.PreparingPictures",
    };

    /// <summary>One block more, unless that would reach the end of the step being read.</summary>
    private void CreepReadingBar()
    {
        double oneBlock = 1.0 / BlockBar.Blocks;
        if (ReadingBar.Fraction + oneBlock < readingStepEnd - oneBlock / 2) ReadingBar.Fraction += oneBlock;
    }

    /// <summary>Hides the reading card: the read is done, or a message takes its place.</summary>
    private void StopReading()
    {
        readingTimer.Stop();
        isReading = false;
        ShowReadingCover();
    }

    /// <summary>Whether a source is being read, so the reading card shows while the tab does.</summary>
    private bool isReading;

    /// <summary>
    /// The reading card over the whole app, the title row and the tabs dimmed too, as behind the app's other windows:
    /// it is moved into the main view's cover the first time, and shown there while a source is read and this tab
    /// shows. The cover lets clicks through, so another tab can be chosen meanwhile, which hides it; coming back shows
    /// it again. The page itself is blocked by its own clear layer while it reads.
    /// </summary>
    private void ShowReadingCover()
    {
        ReadingCardHolder.Visibility = isReading ? Visibility.Visible : Visibility.Collapsed;
        if (MainView.Find(this) is not MainView main) return;
        if (!ReferenceEquals(main.ReadingCoverCard.Content, ReadingCard))
        {
            ReadingCardHolder.Children.Remove(ReadingCard);
            main.ReadingCoverCard.Content = ReadingCard;
        }
        main.ReadingCover.Visibility = isReading && IsVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
