namespace EasyNdf.App;

/// <summary>What kind of notification a message is, which sets its icon and how long it shows.</summary>
public enum NoticeKind
{
    /// <summary>Something was done, or found.</summary>
    Done,
    /// <summary>Nothing went wrong, but what was asked for did not happen.</summary>
    Caution,
    /// <summary>Something failed.</summary>
    Problem,
    /// <summary>Work is under way: it shows until the work ends, and is not kept in the recent list.</summary>
    Working,
}

/// <summary>A button on a notification, for what to do next: "Show Them".</summary>
/// <param name="Label">The button's words.</param>
/// <param name="Act">What it does, on the screen's thread.</param>
public sealed record NoticeAction(string Label, Action Act);

/// <summary>A notification.</summary>
/// <param name="Text">What it says.</param>
/// <param name="Kind">What kind it is.</param>
/// <param name="At">When it was said.</param>
/// <param name="Action">A button under the words; null for none.</param>
public sealed record Notice(string Text, NoticeKind Kind, DateTime At, NoticeAction? Action = null);

/// <summary>
/// The notifications of this session: what the last action did, shown as a popup that fades away. The recent ones are kept
/// in memory only, so they go when the app closes.
/// </summary>
public static class Notices
{
    private const int MostKept = 50;

    private static readonly List<Notice> recent = [];

    /// <summary>Raised with each notification, on whichever thread said it.</summary>
    public static event Action<Notice>? Said;

    /// <summary>Raised when the work a <see cref="NoticeKind.Working"/> notification told of has ended.</summary>
    public static event Action? WorkEnded;

    /// <summary>The recent notifications, the newest first.</summary>
    public static IReadOnlyList<Notice> Recent
    {
        get { lock (recent) return [.. recent]; }
    }

    /// <summary>Says something, with a button for what to do next when given. Nothing to say ends the work under way, if any.</summary>
    public static void Say(string message, NoticeKind kind = NoticeKind.Done, NoticeAction? action = null)
    {
        if (message == "") { WorkEnded?.Invoke(); return; }
        var notice = new Notice(message, kind, DateTime.Now, action);
        if (kind != NoticeKind.Working)
        {
            lock (recent)
            {
                recent.Insert(0, notice);
                if (recent.Count > MostKept) recent.RemoveAt(MostKept);
            }
        }
        Said?.Invoke(notice);
    }
}
