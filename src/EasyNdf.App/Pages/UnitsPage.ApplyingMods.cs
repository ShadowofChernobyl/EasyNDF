using System.Windows.Threading;
using EasyNdf.App.Text;
using EasyNdf.App.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// While the units are worked out again from the mods that are on (mods switched on or off, rules changed, Show Mods'
/// Changes turned on), the reading card covers the page as while a source is read, so the values of the mods as they
/// were are not taken for the new ones: titled with the mods applied, with one line for the step under way. A quick one
/// shows nothing, so the card does not flash.
/// </summary>
public partial class UnitsPage
{
    /// <summary>How long the mods may take before the card shows.</summary>
    private static readonly TimeSpan ApplyingShowsAfter = TimeSpan.FromMilliseconds(250);

    /// <summary>The read of the mods' values the card shows, or 0 when it shows none (see <see cref="modValueReads"/>).</summary>
    private int applyingRead;

    /// <summary>The read of the mods' values waiting for <see cref="ApplyingShowsAfter"/>, or 0.</summary>
    private int applyingWaits;

    private DispatcherTimer? applyingDelay;

    /// <summary>The step under way, shown once the card shows.</summary>
    private ModApplyStep applyingStep;

    /// <summary>A read of the mods' values starts: the card shows if it is still going a moment later.</summary>
    private void StartApplyingMods(int read)
    {
        applyingWaits = read;
        applyingStep = ModApplyStep.RunningRules;
        applyingDelay ??= new DispatcherTimer(ApplyingShowsAfter, DispatcherPriority.Normal, (_, _) => ShowApplyingCard(), Dispatcher) { IsEnabled = false };
        applyingDelay.Stop();
        applyingDelay.Start();
    }

    private void ShowApplyingCard()
    {
        applyingDelay?.Stop();
        // a source being read has the card already
        if (applyingWaits == 0 || isReading) return;
        applyingRead = applyingWaits;
        applyingWaits = 0;
        IReadOnlyList<EasyNdf.Data.Mod> mods = Mods.ModStack.RunningFor(Game);
        ReadingCard.TitleText.Text = Texts.Count("Units.Applying.Title", mods.Count);
        ReadingCard.WhereText.Text = string.Join(", ", mods.Select(mod => mod.Name));
        ReadingCard.Bar.Fraction = 0;
        isReading = true;
        ShowReadingCover();
        ShowApplyingStep(applyingStep);
        if (!readingTimerStarted)
        {
            readingTimer.Tick += (_, _) => CreepReadingBar();
            readingTimerStarted = true;
        }
        readingTimer.Start();
    }

    /// <summary>A step of applying the mods has begun: its line, and the bar at least at its start.</summary>
    private void ShowApplyingStep(ModApplyStep step)
    {
        applyingStep = step;
        if (applyingRead == 0) return;
        (double start, double end) = step switch
        {
            ModApplyStep.RunningRules => (0, 0.5),
            ModApplyStep.ReadingUnits => (0.5, 0.9),
            _ => (0.9, 1.0),
        };
        ReadingCard.Bar.Fraction = Math.Max(ReadingCard.Bar.Fraction, start);
        readingStepEnd = end;
        ReadingCard.StepCountText.Text = Texts.Get("Units.Reading.StepCount", ("step", Texts.Number((int)step + 1)), ("steps", Texts.Number(3)));
        ShowStepLine(Texts.Get(step switch
        {
            ModApplyStep.RunningRules => "Units.Applying.Rules",
            ModApplyStep.ReadingUnits => "Units.Applying.Units",
            _ => "Units.Applying.Showing",
        }));
        // drawn now, before the work of showing the units holds the screen
        if (step == ModApplyStep.Showing) ReadingCard.UpdateLayout();
    }

    /// <summary>The read of the mods' values is over, or another took its place: the card goes, unless a source is read.</summary>
    private void StopApplyingMods(int read)
    {
        if (applyingWaits == read)
        {
            applyingWaits = 0;
            applyingDelay?.Stop();
        }
        if (applyingRead != 0 && applyingRead == read) StopReading();
    }
}
