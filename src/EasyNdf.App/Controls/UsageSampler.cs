using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace EasyNdf.App.Controls;

/// <summary>One reading of what the app takes from the computer, for the usage readout.</summary>
/// <param name="Cpu">The app's share of the whole processor, all cores together, in percent, as Task Manager shows it.</param>
/// <param name="Memory">Task Manager's Memory column: the memory only the app uses, in bytes (its private working set).</param>
/// <param name="DotNet">What the app's own data took after .NET's last clean-up, in bytes.</param>
/// <param name="Gpu">The graphics card's work for the app, in percent, as Task Manager shows it; null when Windows does not say.</param>
/// <param name="DiskPerSecond">What the app read and wrote since the last reading, in bytes per second.</param>
/// <param name="UiDelay">The longest a click or a key waited before the app got to it since the last reading, in milliseconds.</param>
/// <param name="Threads">How many threads the app runs.</param>
/// <param name="Handles">How many Windows handles the app holds (files, windows, timers...).</param>
/// <param name="CleanUps">How many clean-ups .NET has made of each kind: the small ones, the middle ones and the full ones.</param>
/// <param name="Paused">How long the app stood still for .NET's clean-ups in the last minute (or since it started measuring).</param>
/// <param name="PausedShare">The same as a share of that time, in percent.</param>
/// <param name="DotNetHeld">What .NET holds from Windows for the app's data, in use or kept ready, in bytes.</param>
/// <param name="DotNetWaiting">The part of <paramref name="DotNet"/> no longer in use but not yet freed, about, in bytes.</param>
public sealed record UsageReading(double Cpu, long Memory, long DotNet, double? Gpu, double DiskPerSecond, double UiDelay, int Threads,
    int Handles, (int Small, int Middle, int Full) CleanUps, TimeSpan Paused, double PausedShare, long DotNetHeld = 0, long DotNetWaiting = 0);

/// <summary>
/// Measures what the app takes from the computer while the usage readout is on (Settings, Debug), and hands each reading
/// to the screen. Everything is measured on a background thread, so the readout costs the screen nothing but drawing.
/// The numbers are Windows' own, the ones Task Manager shows, so they can be compared with it.
/// </summary>
public sealed class UsageSampler : IDisposable
{
    /// <summary>How often the screen is checked for how long it takes to answer.</summary>
    private static readonly TimeSpan ProbeEvery = TimeSpan.FromMilliseconds(100);

    /// <summary>A wait longer than this is a slow moment (the speed aim: a click answered within 100 ms).</summary>
    public const double SlowWait = 100;

    /// <summary>How far back the clean-up pauses are added up.</summary>
    private static readonly TimeSpan PauseWindow = TimeSpan.FromMinutes(1);

    private readonly Dispatcher screen;
    private readonly Action<UsageReading> readingTaken;
    private readonly Action<double> slowMoment;

    /// <summary>The clean-up pauses so far at each reading of the last minute: when, and the total then.</summary>
    private readonly Queue<(long At, TimeSpan Total)> pauses = new();
    private readonly Process process = Process.GetCurrentProcess();
    private readonly Timer readingTimer;
    private readonly Timer probeTimer;
    private readonly GpuCounters gpu = new();
    private readonly object probing = new();

    private TimeSpan lastProcessorTime;
    private ulong lastBytesMoved;
    private long lastReadingAt;

    /// <summary>When the probe waiting for the screen was sent, or 0 when none is waiting.</summary>
    private long probeSentAt;

    /// <summary>The longest wait for the screen since the last reading, in milliseconds.</summary>
    private double longestWait;

    /// <summary>Starts measuring.</summary>
    /// <param name="screen">The screen's dispatcher: the readings are handed over on it, and its waits are measured.</param>
    /// <param name="every">How often a reading is taken.</param>
    /// <param name="readingTaken">What to do with each reading, on the screen's thread.</param>
    /// <param name="slowMoment">What to do when the screen waited longer than <see cref="SlowWait"/>, with the wait in milliseconds, on the screen's thread.</param>
    public UsageSampler(Dispatcher screen, TimeSpan every, Action<UsageReading> readingTaken, Action<double> slowMoment)
    {
        this.screen = screen;
        this.readingTaken = readingTaken;
        this.slowMoment = slowMoment;
        pauses.Enqueue((Stopwatch.GetTimestamp(), GC.GetTotalPauseDuration()));
        lastProcessorTime = process.TotalProcessorTime;
        lastBytesMoved = BytesMoved();
        lastReadingAt = Stopwatch.GetTimestamp();
        probeTimer = new Timer(_ => Probe(), null, TimeSpan.Zero, ProbeEvery);
        readingTimer = new Timer(_ => TakeReading(), null, every, every);
    }

    /// <summary>Stops measuring.</summary>
    public void Dispose()
    {
        readingTimer.Dispose();
        probeTimer.Dispose();
        gpu.Dispose();
    }

    // ---- how long the screen takes to answer ----

    /// <summary>
    /// Sends the screen a task at the priority of a click or a key, and notes how long it waits. While one waits, no other
    /// is sent, and its wait so far counts, so a screen that hangs shows a delay that keeps growing.
    /// </summary>
    private void Probe()
    {
        lock (probing)
        {
            long now = Stopwatch.GetTimestamp();
            if (probeSentAt != 0)
            {
                longestWait = Math.Max(longestWait, Stopwatch.GetElapsedTime(probeSentAt, now).TotalMilliseconds);
                return;
            }
            probeSentAt = now;
        }
        screen.BeginInvoke(DispatcherPriority.Input, () =>
        {
            double waited;
            lock (probing)
            {
                waited = Stopwatch.GetElapsedTime(probeSentAt).TotalMilliseconds;
                longestWait = Math.Max(longestWait, waited);
                probeSentAt = 0;
            }
            if (waited > SlowWait) slowMoment(waited);
        });
    }

    // ---- the reading ----

    private void TakeReading()
    {
        long now = Stopwatch.GetTimestamp();
        double seconds = Math.Max(0.001, Stopwatch.GetElapsedTime(lastReadingAt, now).TotalSeconds);
        lastReadingAt = now;

        process.Refresh();
        TimeSpan processorTime = process.TotalProcessorTime;
        double cpu = (processorTime - lastProcessorTime).TotalSeconds / seconds / Environment.ProcessorCount * 100;
        lastProcessorTime = processorTime;

        ulong bytesMoved = BytesMoved();
        double diskPerSecond = (bytesMoved - lastBytesMoved) / seconds;
        lastBytesMoved = bytesMoved;

        double uiDelay;
        lock (probing)
        {
            uiDelay = longestWait;
            longestWait = 0;
        }

        // the clean-up pauses of the last minute: the total now, less the total a minute ago
        TimeSpan pausedTotal = GC.GetTotalPauseDuration();
        pauses.Enqueue((now, pausedTotal));
        while (pauses.Count > 2 && Stopwatch.GetElapsedTime(pauses.Peek().At, now) > PauseWindow) pauses.Dequeue();
        (long since, TimeSpan pausedBefore) = pauses.Peek();
        TimeSpan paused = pausedTotal - pausedBefore;
        double window = Math.Max(0.001, Stopwatch.GetElapsedTime(since, now).TotalSeconds);

        GCMemoryInfo heap = GC.GetGCMemoryInfo();
        var reading = new UsageReading(
            Cpu: Math.Clamp(cpu, 0, 100),
            Memory: PrivateWorkingSet(),
            DotNet: heap.HeapSizeBytes - heap.FragmentedBytes,
            Gpu: gpu.Percent(),
            DiskPerSecond: diskPerSecond,
            UiDelay: uiDelay,
            Threads: process.Threads.Count,
            Handles: process.HandleCount,
            CleanUps: (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)),
            DotNetHeld: heap.TotalCommittedBytes,
            DotNetWaiting: Memory.WaitingNow(),
            Paused: paused,
            PausedShare: paused.TotalSeconds / window * 100);
        screen.BeginInvoke(DispatcherPriority.Background, () => readingTaken(reading));
    }

    /// <summary>Task Manager's Memory column: what only the app uses of the computer's memory; what it holds when Windows is too old to say.</summary>
    private static long PrivateWorkingSet()
    {
        var counters = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
        if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size)) return 0;
        return (long)(counters.PrivateWorkingSetSize != 0 ? counters.PrivateWorkingSetSize : counters.PrivateUsage);
    }

    /// <summary>Every byte the app has read and written, files and all, since it started.</summary>
    private static ulong BytesMoved() =>
        GetProcessIoCounters(GetCurrentProcess(), out IoCounters counters) ? counters.ReadTransferCount + counters.WriteTransferCount : 0;

    // ---- Windows ----

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")] private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);
    [DllImport("kernel32.dll")] private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

    /// <summary>
    /// The graphics card's work for the app, read from Windows' "GPU Engine" counters as Task Manager reads them: each
    /// kind of engine (3D, copy, video...) adds up its share, and the busiest kind is the figure.
    /// </summary>
    private sealed class GpuCounters : IDisposable
    {
        /// <summary>How many readings before the counters are found again, as the app may start using another engine.</summary>
        private const int ReadingsBeforeRefinding = 30;

        private const uint FormatDouble = 0x200, FormatNoCap = 0x8000, MoreData = 0x800007D2;

        private IntPtr query, counter;
        private int readings;

        /// <summary>The figure, or null when Windows has no GPU counters for the app (some virtual machines).</summary>
        public double? Percent()
        {
            if (readings++ % ReadingsBeforeRefinding == 0) Find();
            if (query == IntPtr.Zero || PdhCollectQueryData(query) != 0) return null;
            uint size = 0;
            if (PdhGetFormattedCounterArrayW(counter, FormatDouble | FormatNoCap, ref size, out _, IntPtr.Zero) != MoreData) return null;
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, FormatDouble | FormatNoCap, ref size, out uint count, buffer) != 0) return null;
                var byKind = new Dictionary<string, double>();
                int itemSize = IntPtr.Size + 16;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buffer + i * itemSize;
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                    double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + IntPtr.Size + 8));
                    int kindAt = name.IndexOf("engtype_", StringComparison.Ordinal);
                    string kind = kindAt < 0 ? name : name[kindAt..];
                    byKind[kind] = byKind.GetValueOrDefault(kind) + value;
                }
                return byKind.Count == 0 ? 0 : Math.Clamp(byKind.Values.Max(), 0, 100);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        /// <summary>Finds the app's engines; a share is a difference between two readings, so the first one is taken now.</summary>
        private void Find()
        {
            Dispose();
            if (PdhOpenQueryW(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
            string path = $@"\GPU Engine(pid_{Environment.ProcessId}_*)\Utilization Percentage";
            if (PdhAddEnglishCounterW(query, path, IntPtr.Zero, out counter) != 0) { Dispose(); return; }
            PdhCollectQueryData(query);
        }

        public void Dispose()
        {
            if (query != IntPtr.Zero) PdhCloseQuery(query);
            query = counter = IntPtr.Zero;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr items);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    }
}
