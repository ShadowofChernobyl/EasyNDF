using System.Diagnostics;

namespace EasyNdf.App;

/// <summary>
/// The app's longer jobs, by name, for the usage readout (Settings, Debug): its Busy label names the one running, CPU's
/// tooltip lists the last ones and how long each took, and a slow moment says which job was running. A job is started
/// with <c>using var job = Work.Start(...)</c> around the work, and can be renamed as it goes ("Exporting 87 of 117").
/// It costs a few list operations, so jobs are named whether the readout is on or not.
/// </summary>
public static class Work
{
    /// <summary>How many finished jobs are kept for the tooltip.</summary>
    private const int FinishedKept = 5;

    private static readonly object guard = new();
    private static readonly List<Job> running = [];
    private static readonly LinkedList<FinishedJob> finished = [];

    /// <summary>A job that finished: what it was, when it ended, and how long it took.</summary>
    public sealed record FinishedJob(string Name, DateTime EndedAt, TimeSpan Took);

    /// <summary>Starts a job; it ends when disposed.</summary>
    /// <param name="name">What it does, in the app's language, such as "Reading WARNO".</param>
    public static Job Start(string name)
    {
        var job = new Job(name);
        lock (guard) running.Add(job);
        // a job that names itself as it goes ("Exporting 1 of 117") is written down with its first name
        if (name != "") SessionLog.Write("Job", "Started: " + name);
        return job;
    }

    /// <summary>The job started last of those running, with how long it has run; null when none runs.</summary>
    public static (string Name, TimeSpan SoFar)? Current
    {
        get
        {
            lock (guard)
                return running.Count == 0 ? null : (running[^1].Name, Stopwatch.GetElapsedTime(running[^1].StartedAt));
        }
    }

    /// <summary>The last jobs that finished, the latest first.</summary>
    public static IReadOnlyList<FinishedJob> Finished
    {
        get { lock (guard) return [.. finished]; }
    }

    /// <summary>One job while it runs.</summary>
    public sealed class Job : IDisposable
    {
        internal Job(string name) => Name = name;

        /// <summary>What the job does now.</summary>
        public string Name { get; private set; }

        internal long StartedAt { get; } = Stopwatch.GetTimestamp();

        /// <summary>Says what the job does now, as one that counts its way through ("Exporting 87 of 117").</summary>
        public void Rename(string name)
        {
            bool first;
            lock (guard)
            {
                first = Name == "";
                Name = name;
            }
            if (first) SessionLog.Write("Job", "Started: " + name);
        }

        /// <summary>Ends the job, keeping it among the last ones finished.</summary>
        public void Dispose()
        {
            lock (guard)
            {
                if (!running.Remove(this)) return;
                TimeSpan took = Stopwatch.GetElapsedTime(StartedAt);
                finished.AddFirst(new FinishedJob(Name, DateTime.Now, took));
                while (finished.Count > FinishedKept) finished.RemoveLast();
                SessionLog.Write("Job", $"Finished: {Name} ({took.TotalSeconds:0.0} s)");
            }
        }
    }
}
