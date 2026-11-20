using System.IO;
using EasyNdf.Data;

namespace EasyNdf.App;

/// <summary>
/// Keeps each mod's folder in the Mods folder laid out as the mod is (<see cref="ModFileViews"/>): every mod at start,
/// and a mod again whenever it is saved, in the background. Saves come quickly while a mod is changed, so only the last
/// text of a mod is laid out, a moment after the changes stop.
/// </summary>
public static class ModFolders
{
    private static ModFileViews? views;
    private static readonly Dictionary<string, string> waiting = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object gate = new();
    private static Task worker = Task.CompletedTask;

    /// <summary>How long after a save a mod's folder is laid out, so a run of changes lays it out once.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    /// <summary>The views of the user's data; null for data in memory only.</summary>
    public static ModFileViews? Views => views;

    /// <summary>Starts keeping the mods' folders: every mod laid out now, in the background, and again after each save.</summary>
    public static void Start(AppData data)
    {
        if (data.Folders is not AppFolders folders) return;
        views = new ModFileViews(folders.Mods, data.ModFileStore, folders.Pictures, data.ModStore.FolderLock);
        ModFileViews.PictureFinder = views.FindPicture;
        data.ModStore.FileRenamed += (from, to) => views.Rename(from, to);
        data.ModStore.Saved += Queue;
        foreach (Mod mod in data.Mods)
            if (data.ModStore.FileStemOf(mod) is string stem) Queue(stem, ModFiles.StoredText(mod));
    }

    /// <summary>Where a picture the app kept is now, by the path a mod names it by; null when it is nowhere.</summary>
    public static string? FindPicture(string kept)
    {
        try { return views?.FindPicture(Path.GetFileName(kept)); }
        catch (Exception problem) when (problem is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A mod's text to lay out its folder from, the newest kept.</summary>
    private static void Queue(string stem, string text)
    {
        lock (gate)
        {
            waiting[stem] = text;
            if (!worker.IsCompleted) return;
            worker = Task.Run(Work);
        }
    }

    private static async Task Work()
    {
        while (true)
        {
            await Task.Delay(Settle);
            (string Stem, string Text)[] batch;
            lock (gate)
            {
                if (waiting.Count == 0) return;
                batch = [.. waiting.Select(pair => (pair.Key, pair.Value))];
                waiting.Clear();
            }
            foreach ((string stem, string text) in batch)
            {
                // a folder laid out is for the user to look at: nothing in it is worth stopping the app for
                try { views?.Sync(stem, text); }
                catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { }
            }
            views?.RemoveEmptyWaitingFolders();
        }
    }
}
