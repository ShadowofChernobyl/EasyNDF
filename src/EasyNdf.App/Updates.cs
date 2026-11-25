using System.Windows;
using EasyNdf.App.Text;
using Velopack;
using Velopack.Sources;

namespace EasyNdf.App;

/// <summary>
/// Updates, for EasyNDF as its installer installed it (Velopack): at start, a look at the releases on GitHub; a newer one
/// is offered in a notification, and only its Update Now button downloads it, then closes the app, updates it and opens it
/// again ("ask, then update"). The portable EasyNDF.exe is not installed, so it never looks. Settings › Data › Updates
/// turns the look off.
/// </summary>
public static class Updates
{
    /// <summary>The public repository whose GitHub Releases hold the installer and its updates.</summary>
    public const string Repository = "https://github.com/ShadowofChernobyl/EasyNDF";

    /// <summary>Looks for a newer release, in the background; says so when there is one. Nothing is said otherwise.</summary>
    public static void CheckSoon()
    {
        if (App.RunsTests || AppData.Current.Settings["checkUpdates"]?.GetValue<bool>() == false) return;
        Check(sayAll: false);
    }

    /// <summary>Settings › Data › Check Now: looks at once, whatever the Updates switch, and says what it found.</summary>
    public static void CheckNow() => Check(sayAll: true);

    private static async void Check(bool sayAll)
    {
        UpdateManager manager;
        UpdateInfo? update;
        try
        {
            manager = new UpdateManager(new GithubSource(Repository, null, false));
            if (!manager.IsInstalled)
            {
                if (sayAll) Notices.Say(Texts.Get("Main.Update.NotInstalled"), NoticeKind.Caution);
                return;
            }
            if (sayAll) Notices.Say(Texts.Get("Main.Update.Checking"), NoticeKind.Working);
            update = await manager.CheckForUpdatesAsync();
        }
        // offline, or GitHub busy: the next start looks again
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            if (sayAll) Notices.Say(Texts.Get("Main.Update.CheckFailed", ("reason", problem.Message)), NoticeKind.Problem);
            return;
        }
        if (update is null)
        {
            if (sayAll) Notices.Say(Texts.Get("Main.Update.Latest", ("version", typeof(Updates).Assembly.GetName().Version?.ToString(3) ?? "")), NoticeKind.Done);
            return;
        }
        string version = update.TargetFullRelease.Version.ToString();
        Notices.Say(Texts.Get("Main.Update.Ready", ("version", version)), NoticeKind.Done,
            new NoticeAction(Texts.Get("Main.Update.Now"), () => Install(manager, update, version)));
    }

    /// <summary>Update Now: downloads the update, then closes the app as its close button does; it is updated and opened again.</summary>
    private static async void Install(UpdateManager manager, UpdateInfo update, string version)
    {
        Notices.Say(Texts.Get("Main.Update.Downloading", ("version", version)), NoticeKind.Working);
        try
        {
            await manager.DownloadUpdatesAsync(update);
        }
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            Notices.Say("");
            Notices.Say(Texts.Get("Main.Update.Failed", ("reason", problem.Message)), NoticeKind.Problem);
            return;
        }
        Notices.Say("");
        // the update waits for the app to close, so whatever closing asks (unsaved text) is asked as always
        manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: false, restart: true);
        Application.Current.MainWindow?.Close();
    }
}
