using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// While a source is read: the tab's layout is there already, its list empty, and over the dimmed page a card says
/// what is read and from where, with a bar that fills block by block and a line naming the step under way.
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
        ReadingCard.TitleText.Text = Texts.Get("Units.Reading.Title", ("name", source.Name));
        ReadingCard.WhereText.Text = source.Folder;
        ReadingCard.Bar.Fraction = 0;
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
        // the mods being applied meanwhile give the card back to the source's read
        applyingRead = 0;
        (double start, double end) = ShareOf(step);
        ReadingCard.Bar.Fraction = Math.Max(ReadingCard.Bar.Fraction, start);
        readingStepEnd = end;
        int now = Array.IndexOf(ReadingStepsInOrder, step);
        ReadingCard.StepCountText.Text = Texts.Get("Units.Reading.StepCount", ("step", Texts.Number(now + 1)), ("steps", Texts.Number(ReadingStepsInOrder.Length)));
        ShowStepLine(Texts.Get(StepTextName(step)));
    }

    /// <summary>The card's one line under the bar: the step under way, in the accent colour.</summary>
    private void ShowStepLine(string step)
    {
        var line = new TextBlock { Text = step };
        line.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        ReadingCard.StepLines.Children.Clear();
        ReadingCard.StepLines.Children.Add(line);
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
        if (ReadingCard.Bar.Fraction + oneBlock < readingStepEnd - oneBlock / 2) ReadingCard.Bar.Fraction += oneBlock;
    }

    /// <summary>Hides the reading card: the read is done, or a message takes its place.</summary>
    private void StopReading()
    {
        applyingRead = 0;
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
        if (!(isReading && IsVisible))
        {
            // another page's card (the Editor's) may be in the cover now: it is left as it is
            if (ReferenceEquals(main.ReadingCoverCard.Content, ReadingCard)) main.ReadingCover.Visibility = Visibility.Collapsed;
            return;
        }
        if (!ReferenceEquals(main.ReadingCoverCard.Content, ReadingCard))
        {
            ReadingCardHolder.Children.Remove(ReadingCard);
            main.ReadingCoverCard.Content = ReadingCard;
        }
        main.ReadingCover.Visibility = Visibility.Visible;
    }
}
