using System.Windows.Threading;

namespace EasyNdf.App;

/// <summary>
/// Runs an action once for a burst of asks: the first ask queues it, and asks before it runs add nothing. It runs
/// after the work already waiting and before the screen is drawn again, so what it shows is never a frame late. When
/// thousands of files are read and each says it changed, the list's headings, column widths and undo history are then
/// worked out once a burst instead of once a file.
/// </summary>
/// <param name="action">What to run.</param>
public sealed class RunOnceSoon(Action action)
{
    // the thread it was made on, found once: finding it takes a lock the whole app shares
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private bool isAsked;

    /// <summary>Asks for the action to run soon, unless it is asked for already.</summary>
    public void Ask()
    {
        if (isAsked) return;
        isAsked = true;
        // DataBind comes after the Normal work a file's read sends back, and before Render
        dispatcher.BeginInvoke(DispatcherPriority.DataBind, RunIfAsked);
    }

    /// <summary>Runs the action now if it was asked for and has not run yet, for code that needs it done first.</summary>
    public void RunIfAsked()
    {
        if (!isAsked) return;
        isAsked = false;
        action();
    }
}
