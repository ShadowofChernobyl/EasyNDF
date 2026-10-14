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
        ThemeManager.ApplySaved();
        Controls.ListText.ApplySaved();
        int frameAt = Array.IndexOf(e.Args, "--frame");
        MainWindow = new MainWindow(frameAt >= 0 && frameAt + 1 < e.Args.Length ? e.Args[frameAt + 1] : "");
        MainWindow.Show();
    }

    /// <summary>Whether the command line has <paramref name="name"/> followed by <paramref name="value"/>.</summary>
    private static bool HasSwitch(string[] args, string name, string value)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length && args[at + 1] == value;
    }
}
