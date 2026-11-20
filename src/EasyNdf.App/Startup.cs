using System.IO;
using System.Runtime.InteropServices;
using Velopack;

namespace EasyNdf.App;

/// <summary>
/// Where the app starts: the installer's own steps first (Velopack's, as it installs, updates or uninstalls EasyNDF; they
/// may end the process before any window), then the app. An error before the app can show one of its own (a damaged
/// Windows, a part of it missing) is written to EasyNDF-Crash.txt and shown in Windows' own message box, so the app never
/// closes without a word.
/// </summary>
public static class Startup
{
    [STAThread]
    public static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
        try
        {
            VelopackApp.Build().Run();
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception problem)
        {
            Report(problem);
        }
    }

    /// <summary>Writes the error where it can be found (the data folder, else Temp) and shows it.</summary>
    private static void Report(Exception? problem)
    {
        string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  EasyNDF "
            + (typeof(Startup).Assembly.GetName().Version?.ToString(3) ?? "") + " on " + Environment.OSVersion.VersionString + Environment.NewLine
            + (problem?.ToString() ?? "An unknown error.") + Environment.NewLine + Environment.NewLine;
        string? written = null;
        foreach (string folder in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyNDF"), Path.GetTempPath() })
        {
            try
            {
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "EasyNDF-Crash.txt");
                File.AppendAllText(file, text);
                written = file;
                break;
            }
            catch (Exception notWritten) when (notWritten is IOException or UnauthorizedAccessException) { }
        }
        // Windows' own box: it needs nothing of WPF's, which may be what failed
        MessageBoxW(IntPtr.Zero, "EasyNDF could not start.\n\n" + (problem?.Message ?? "") + (written is null ? "" : "\n\nThe details are in " + written + "."),
            "EasyNDF", 0x10);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
