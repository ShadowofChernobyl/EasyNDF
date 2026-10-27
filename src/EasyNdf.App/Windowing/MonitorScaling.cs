using System.Runtime.InteropServices;
using EasyNdf.Data;

namespace EasyNdf.App.Windowing;

/// <summary>
/// Settings › Appearance › Sharp on every monitor: tells Windows that the app redraws itself for each monitor's scaling
/// (100%, 125%, 150%…), so text and icons stay sharp when the window is moved to a monitor scaled differently, instead
/// of Windows stretching the picture. It can only be set before the app opens its first window, so a change to the
/// setting takes effect when the app restarts.
/// </summary>
internal static class MonitorScaling
{
    /// <summary>The setting's name in settings.json.</summary>
    public const string SettingKey = "perMonitorDpi";

    /// <summary>Windows' "per monitor, version 2" mode, as SetProcessDpiAwarenessContext takes it.</summary>
    private static readonly IntPtr PerMonitorVersion2 = new(-4);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>
    /// Turns per-monitor scaling on when the saved setting says so. Called first thing, before WPF reads how the app
    /// is scaled; the settings are read from their file here, as the app's data is not loaded yet.
    /// </summary>
    public static void ApplySaved()
    {
        try
        {
            var settings = new SettingsStore(AppFolders.ForThisApp().Settings).LoadSettings();
            if (settings[SettingKey]?.GetValue<bool>() == false) return;
            // Windows older than Windows 10 version 1703 lacks it: the app then scales as before
            SetProcessDpiAwarenessContext(PerMonitorVersion2);
        }
        catch (Exception)
        {
            // anything at all: this runs before the app can show an error, and a display preference must never stop it starting
        }
    }
}
