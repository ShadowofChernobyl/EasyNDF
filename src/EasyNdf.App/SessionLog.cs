using System.Globalization;
using System.IO;

namespace EasyNdf.App;

/// <summary>
/// The session log (Settings, Debug, "Keep a log"): while it is on, one file for each time the app runs, in the data
/// folder's Logs, a line for each thing worth knowing afterwards: the jobs and what they found, the changes saved to
/// mods, mods switched on or off, Undo and Redo, settings changed, the tabs shown, errors and slow moments. Off by
/// default. Only the last few sessions' files are kept (the "Sessions kept" setting); the oldest go as a session starts.
/// </summary>
public static class SessionLog
{
    private static readonly object writing = new();
    private static StreamWriter? file;

    /// <summary>The folder of the log files; null when the app keeps no data folder.</summary>
    public static string? Folder => AppData.Current.Folders?.Root is string root ? Path.Combine(root, "Logs") : null;

    /// <summary>Whether the log is being written.</summary>
    public static bool IsOn
    {
        get { lock (writing) return file is not null; }
    }

    /// <summary>Starts or stops the log as the setting says: a file for this session is made the first time it is on.</summary>
    public static void Apply()
    {
        bool on = AppData.Current.Settings["logOn"]?.GetValue<bool>() == true;
        lock (writing)
        {
            if (on == (file is not null)) return;
            if (!on)
            {
                WriteLocked("Log", "Log switched off");
                file?.Dispose();
                file = null;
                return;
            }
            if (Folder is not string folder) return;
            try
            {
                Directory.CreateDirectory(folder);
                LeaveOnlyKept(folder, Kept() - 1);
                string name = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture) + ".log";
                file = new StreamWriter(new FileStream(Path.Combine(folder, name), FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
            {
                file = null;
                return;
            }
        }
        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        Write("Log", $"EasyNDF {version} on {Environment.OSVersion.VersionString}, {Environment.ProcessorCount} cores");
    }

    /// <summary>A line in the log, when it is on: the time, what part of the app it is from, and what happened.</summary>
    /// <param name="part">Where it happened, such as "Armory" or "Mods".</param>
    /// <param name="what">What happened.</param>
    public static void Write(string part, string what)
    {
        if (file is null) return;
        lock (writing) WriteLocked(part, what);
    }

    private static void WriteLocked(string part, string what)
    {
        if (file is null) return;
        try
        {
            file.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + part.PadRight(9) + " " + what.Replace(Environment.NewLine, " ⏎ "));
        }
        catch (IOException)
        {
            // a full disk or a file taken away: the app goes on without its log
        }
    }

    /// <summary>How many sessions' files are kept (the "Sessions kept" setting).</summary>
    private static int Kept() =>
        int.TryParse((string?)AppData.Current.Settings["logKeep"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kept) ? Math.Clamp(kept, 1, 500) : 10;

    /// <summary>Takes out the oldest log files until only so many are left.</summary>
    private static void LeaveOnlyKept(string folder, int left)
    {
        FileInfo[] files = new DirectoryInfo(folder).GetFiles("*.log");
        foreach (FileInfo old in files.OrderByDescending(found => found.Name, StringComparer.Ordinal).Skip(Math.Max(0, left)))
        {
            try { old.Delete(); }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException) { }
        }
    }
}
