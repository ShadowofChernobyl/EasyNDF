using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using EasyNdf.App.Theming;
using EasyNdf.Data;
using EasyNdf.Data.Themes;

namespace EasyNdf.App;

/// <summary>The app: it loads the saved data and theme, then opens the main window.</summary>
public partial class App : Application
{
    /// <summary>Runs before anything of WPF's: how the app is scaled on each monitor can only be set this early.</summary>
    static App() => Windowing.MonitorScaling.ApplySaved();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Controls.CssTextPosition.Register();
        Controls.SmoothScrolling.Register();
        Controls.LongText.Register();
        // the lists' one-line texts, until the saved setting is read
        Controls.ListText.Apply(wrap: false);

        // Switches for a PC that draws the app oddly (see README): "--render software" draws without the graphics
        // card, "--frame native" uses Windows' own title bar.
        if (HasSwitch(e.Args, "--render", "software")) RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        AppData.Load();
        Mods.ModStack.Start();
        DispatcherUnhandledException += ErrorNotCaught;
        Text.Texts.UseSaved();
        ThemeManager.ApplySaved();
        Controls.ListText.ApplySaved();
        int frameAt = Array.IndexOf(e.Args, "--frame");
        MainWindow = new MainWindow(frameAt >= 0 && frameAt + 1 < e.Args.Length ? e.Args[frameAt + 1] : "");
        MainWindow.Show();
    }

    /// <summary>
    /// An error nothing else caught: written to Errors.txt in the data folder and shown, so it can be reported, and the
    /// app keeps running rather than closing without a word. What the user was doing may be left half done, so the
    /// message says to check it.
    /// </summary>
    private void ErrorNotCaught(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        string? logFile = AppData.Current.Folders?.Root is string root ? System.IO.Path.Combine(root, "Errors.txt") : null;
        try
        {
            if (logFile is not null) System.IO.File.AppendAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + e.Exception + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception problem) when (problem is System.IO.IOException or UnauthorizedAccessException)
        {
            // the message below still says what went wrong
            logFile = null;
        }
        string message = Text.Texts.Get("Main.Error", ("error", e.Exception.Message))
            + (logFile is null ? "" : Environment.NewLine + Environment.NewLine + Text.Texts.Get("Main.Error.File", ("file", logFile)));
        MessageBox.Show(MainWindow, message, "EasyNDF", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>Whether the command line has <paramref name="name"/> followed by <paramref name="value"/>.</summary>
    private static bool HasSwitch(string[] args, string name, string value)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length && args[at + 1] == value;
    }
}
