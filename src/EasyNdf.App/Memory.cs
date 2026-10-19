using System.Runtime;

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
