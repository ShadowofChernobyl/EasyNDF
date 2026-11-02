using System.Runtime;
using System.Windows.Input;
using System.Windows.Threading;

namespace EasyNdf.App;

/// <summary>
/// Gives memory back to Windows after large work. Reading and exporting a large game file (UniteDescriptor.ndf, 26 MB)
/// briefly takes several hundred MB; .NET keeps that memory afterwards in case it is needed again, so the app would
/// stay at about 900 MB in Task Manager while it sits idle. One full clean-up afterwards brings it back to about
/// 180 MB, in a tenth of a second.
/// </summary>
public static class Memory
{
    /// <summary>Frees what large work left behind, and gives it back to Windows.</summary>
    public static void GiveBack()
    {
        // large blocks (a file's text is one) are only moved together, freeing their gaps, when asked for
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    /// <summary>
    /// <see cref="GiveBack"/> for work that left a very large text in use, such as Steel Division 2's 117 MB
    /// UniteDescriptor.ndf open in the editor: moving it about would stop the app for most of a second, so the clean-up
    /// runs alongside the app instead, freeing what is left behind without moving what stays.
    /// </summary>
    public static void GiveBackAlongside() => GC.Collect(2, GCCollectionMode.Forced, blocking: false);

    /// <summary>
    /// The longest text (in characters) open in the editor for which a full give-back is made at once: it stops the app
    /// for about a fifth of a second at 26 million (WARNO's UniteDescriptor.ndf), longer with more.
    /// </summary>
    public const int LargestTextForFullGiveBack = 60_000_000;

    /// <summary>How much memory .NET may keep without using it before the idle clean-up gives it back.</summary>
    private const long SpareKept = 150L * 1024 * 1024;

    /// <summary>
    /// The most memory the app should take in Task Manager (Paul's aim: 900 MB at all times). Above it, the idle clean-up
    /// runs even when .NET's figures show little to give back.
    /// </summary>
    private const long MostTaken = 900L * 1024 * 1024;

    /// <summary>How long without a key or the mouse before the idle clean-up runs, so it never stops the app under the user's hand.</summary>
    private static readonly TimeSpan QuietFor = TimeSpan.FromSeconds(5);

    /// <summary>When a key or the mouse was last used.</summary>
    private static DateTime lastInput = DateTime.UtcNow;

    /// <summary>
    /// The idle clean-up (Paul's choice, option E of the memory report): every 15 seconds, once the app has gone 5 seconds
    /// without a key or the mouse and no long job is running, memory .NET keeps without using (more than 150 MB) is given
    /// back to Windows. What large work leaves behind (reading the mods' changes, a big file, an export) so no longer
    /// stays in Task Manager's figure while the app sits there.
    /// </summary>
    public static void TrimWhenIdle(Dispatcher dispatcher)
    {
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is KeyboardEventArgs or MouseButtonEventArgs or MouseWheelEventArgs) lastInput = DateTime.UtcNow;
        };
        var check = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dispatcher) { Interval = TimeSpan.FromSeconds(15) };
        check.Tick += (_, _) =>
        {
            if (longJobs > 0 || DateTime.UtcNow - lastInput < QuietFor) return;
            // only when more is kept than after the last clean-up, so an app left alone is not cleaned up again and again
            long taken = TakenNow();
            bool spare = SpareNow() > Math.Max(SpareKept, spareAfterLast + SpareKept / 3);
            bool tooMuch = taken > MostTaken && taken > takenAfterLast + SpareKept / 3;
            if (!spare && !tooMuch) return;
            BeforeGivingBack?.Invoke();
            GiveBack();
            spareAfterLast = SpareNow();
            takenAfterLast = TakenNow();
        };
        check.Start();
    }

    /// <summary>What else is put away before the idle clean-up: the large rule sets nothing uses (see <see cref="MainView"/>).</summary>
    public static Action? BeforeGivingBack { get; set; }

    /// <summary>What .NET kept without using after the last idle clean-up.</summary>
    private static long spareAfterLast;

    /// <summary>What the app took in Task Manager after the last idle clean-up.</summary>
    private static long takenAfterLast;

    /// <summary>The memory the app takes, as Task Manager counts it.</summary>
    private static long TakenNow()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    /// <summary>
    /// The memory .NET keeps without using: what it holds now, less what was in use after its last full clean-up. The
    /// figures of the last clean-up of any kind are not enough: the frequent small ones never look at the older and the
    /// large blocks, so those no longer used still counted as in use, and an app holding 1.7 GB with 420 MB in use was
    /// never cleaned up. What came into use since the last full clean-up counts as spare too; the clean-up then finds
    /// less to give back, once, and <see cref="spareAfterLast"/> keeps it from running again.
    /// </summary>
    private static long SpareNow()
    {
        GCMemoryInfo now = GC.GetGCMemoryInfo();
        GCMemoryInfo blocking = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        GCMemoryInfo background = GC.GetGCMemoryInfo(GCKind.Background);
        GCMemoryInfo full = blocking.Index >= background.Index ? blocking : background;
        long inUse = full.Index == 0 ? 0 : full.HeapSizeBytes - full.FragmentedBytes;
        return now.TotalCommittedBytes - inUse;
    }

    /// <summary>How many long jobs are running with <see cref="DuringLongWork"/>.</summary>
    private static int longJobs;

    /// <summary>The clean-up mode before the first long job started.</summary>
    private static GCLatencyMode usualMode;

    /// <summary>
    /// For a long job in the background, such as reading thousands of files: until the result is disposed, .NET's full
    /// clean-ups run alongside the app instead of stopping it, so typing and scrolling stay smooth while the job
    /// allocates. Then the usual mode is back and what the job left behind is given back (<see cref="GiveBack"/>).
    /// </summary>
    public static IDisposable DuringLongWork()
    {
        if (longJobs++ == 0)
        {
            usualMode = GCSettings.LatencyMode;
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }
        return new LongWork();
    }

    private sealed class LongWork : IDisposable
    {
        private bool isDone;

        public void Dispose()
        {
            if (isDone) return;
            isDone = true;
            if (--longJobs > 0) return;
            GCSettings.LatencyMode = usualMode;
            GiveBack();
        }
    }
}
