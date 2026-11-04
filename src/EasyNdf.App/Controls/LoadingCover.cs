using System.Windows;
using System.Windows.Threading;

namespace EasyNdf.App.Controls;

/// <summary>
/// A page's loading card (<see cref="LoadingCard"/>) over the dimmed app while something slow runs on it: a file opened
/// in the Editor, a mod imported or exported. A load quicker than a quarter of a second shows nothing, so quick ones do
/// not flash it; a step that holds the screen has the card drawn first. The card shows while its page does: another tab
/// chosen hides it, coming back shows it again.
/// </summary>
public sealed class LoadingCover
{
    /// <summary>How long a load may take before the card shows.</summary>
    private static readonly TimeSpan ShowsAfter = TimeSpan.FromMilliseconds(250);

    private readonly FrameworkElement page;
    private readonly LoadingCard card = new();
    private readonly DispatcherTimer delay;

    /// <summary>The load the card is for, or 0 when none is under way; a newer load takes it over.</summary>
    private int now;

    private int started;
    private bool shown;
    private string[] steps = [];

    /// <summary>A cover for <paramref name="page"/>'s loads.</summary>
    public LoadingCover(FrameworkElement page)
    {
        this.page = page;
        delay = new DispatcherTimer(ShowsAfter, DispatcherPriority.Normal, (_, _) => ShowCard(), page.Dispatcher) { IsEnabled = false };
        page.IsVisibleChanged += (_, _) => ShowCover();
    }

    /// <summary>A load starts: the card shows if it is still going a moment later.</summary>
    /// <returns>The load's number, for its steps and its end.</returns>
    public int Start(string title, string where, params string[] stepLines)
    {
        int load = ++started;
        now = load;
        steps = stepLines;
        card.Start(title, where);
        Step(load, 0);
        delay.Stop();
        delay.Start();
        return load;
    }

    /// <summary>A step of the load has begun.</summary>
    public void Step(int load, int step)
    {
        if (load != now || step >= steps.Length) return;
        double share = 1.0 / steps.Length;
        card.ShowStep(step + 1, steps.Length, steps[step], step * share, (step + 1) * share);
    }

    /// <summary>
    /// A step that holds the screen begins: the card shows at once when <paramref name="showNow"/> (a big text), and is
    /// drawn before the step starts.
    /// </summary>
    public async Task StepThatHolds(int load, int step, bool showNow)
    {
        Step(load, step);
        if (load != now) return;
        if (!shown && (showNow || showsAgain)) ShowCard();
        showsAgain = false;
        if (shown) await page.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>The card goes while the load waits on the user (a question asked); its next step shows it again at once.</summary>
    public void Hide(int load)
    {
        if (load != now) return;
        delay.Stop();
        if (!shown) return;
        shown = false;
        showsAgain = true;
        ShowCover();
    }

    /// <summary>Whether the card was hidden for a question, and shows again at the load's next step.</summary>
    private bool showsAgain;

    /// <summary>The load is over: the card goes.</summary>
    public void End(int load)
    {
        if (load != now) return;
        now = 0;
        showsAgain = false;
        delay.Stop();
        card.Stop();
        if (!shown) return;
        shown = false;
        ShowCover();
    }

    private void ShowCard()
    {
        delay.Stop();
        if (now == 0) return;
        shown = true;
        ShowCover();
    }

    /// <summary>The card in the main view's cover while a load shows and the page does; left alone when another page's card is there.</summary>
    private void ShowCover()
    {
        if (MainView.Find(page) is not MainView main) return;
        if (shown && page.IsVisible)
        {
            main.ReadingCoverCard.Content = card;
            main.ReadingCover.Visibility = Visibility.Visible;
        }
        else if (ReferenceEquals(main.ReadingCoverCard.Content, card))
        {
            main.ReadingCover.Visibility = Visibility.Collapsed;
        }
    }
}
