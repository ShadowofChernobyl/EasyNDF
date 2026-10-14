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
}
