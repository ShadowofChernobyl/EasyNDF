using System.Text.Json.Nodes;
using System.Windows;

namespace EasyNdf.App.Windowing;

/// <summary>
/// "Remember the window": where the main window was and how large, and whether it was maximized, kept when it closes and
/// given back when it opens. A place that is no longer on any screen (a monitor unplugged) is not used.
/// </summary>
public static class WindowPlace
{
    private static bool IsOn => AppData.Current.Settings["rememberWindow"]?.GetValue<bool>() != false;

    /// <summary>Keeps the window's place in the app's state; the caller saves the state.</summary>
    public static void Keep(Window window)
    {
        if (!IsOn) { AppData.Current.State.Remove("window"); return; }
        // while maximized or minimized, the size to come back to is the one it had before
        Rect normal = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
        AppData.Current.State["window"] = new JsonObject
        {
            ["left"] = Math.Round(normal.Left),
            ["top"] = Math.Round(normal.Top),
            ["width"] = Math.Round(normal.Width),
            ["height"] = Math.Round(normal.Height),
            ["maximized"] = window.WindowState == WindowState.Maximized,
        };
    }

    /// <summary>Puts the window back where it was, before it is shown.</summary>
    public static void Restore(Window window)
    {
        if (!IsOn || AppData.Current.State["window"] is not JsonObject kept) return;
        var place = new Rect((double?)kept["left"] ?? 0, (double?)kept["top"] ?? 0, (double?)kept["width"] ?? 0, (double?)kept["height"] ?? 0);
        if (place.Width < window.MinWidth || place.Height < window.MinHeight || !IsOnAScreen(place)) return;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = place.Left;
        window.Top = place.Top;
        window.Width = place.Width;
        window.Height = place.Height;
        if ((bool?)kept["maximized"] == true) window.WindowState = WindowState.Maximized;
    }

    /// <summary>Whether enough of the window's top would be on the screens to grab it by its title bar.</summary>
    private static bool IsOnAScreen(Rect place)
    {
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var titleBar = new Rect(place.Left, place.Top, place.Width, 40);
        titleBar.Intersect(screens);
        return !titleBar.IsEmpty && titleBar.Width >= 100;
    }
}
