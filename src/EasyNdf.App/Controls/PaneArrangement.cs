using System.Text.Json.Nodes;

namespace EasyNdf.App.Controls;

/// <summary>
/// Whether the second pane of the Files and Editor tabs (Details, Descriptors) sits beside the main pane or below it.
/// One choice for both tabs. While neither the ▥ nor the ▤ button is pressed (setting "panesAuto", the default), the
/// window's size decides: beside when it is at least 1500 pixels wide and wider than it is tall. Once one is pressed,
/// the setting "panesSide" ("beside" or "below") does, until it is clicked again.
/// </summary>
public static class PaneArrangement
{
    /// <summary>The narrowest window that has its panes side by side, in Windows' scaled pixels, so any monitor decides alike.</summary>
    public const double NarrowestBeside = 1500;

    /// <summary>
    /// How far below that width the panes stay side by side once they are: while the window's edge is dragged across
    /// the line, they do not flip back and forth.
    /// </summary>
    private const double Margin = 60;

    private static double windowWidth = 1920, windowHeight = 1040;

    /// <summary>The automatic choice, which the margin keeps from one size to the next.</summary>
    private static bool automaticBeside = true;

    /// <summary>Whether the second panes are beside the main ones.</summary>
    public static bool IsBeside { get; private set; } = true;

    /// <summary>Raised when the panes move beside or below.</summary>
    public static event Action? Changed;

    private static JsonObject Settings => AppData.Current.Settings;

    /// <summary>Whether the window's size decides (setting "panesAuto", on unless set off).</summary>
    public static bool IsAutomatic => Settings["panesAuto"]?.GetValue<bool>() != false;

    /// <summary>The window has a new size, in Windows' scaled pixels.</summary>
    public static void WindowResized(double width, double height)
    {
        windowWidth = width;
        windowHeight = height;
        Update();
    }

    /// <summary>
    /// The panes are put beside or below by hand (the ▥ and ▤ buttons in a pane's heading). That turns "Arrange panes
    /// automatically" off, and is kept as the choice for when it is off.
    /// </summary>
    public static void Choose(bool beside)
    {
        Settings["panesAuto"] = false;
        Settings["panesSide"] = beside ? "beside" : "below";
        AppData.Current.SaveSettings();
        Update(alwaysTell: true);
    }

    /// <summary>Lets the window's size decide again (setting "panesAuto" on).</summary>
    public static void ChooseAutomatic()
    {
        Settings["panesAuto"] = true;
        AppData.Current.SaveSettings();
        Update(alwaysTell: true);
    }

    /// <summary>Works the choice out again: after the window is resized or a setting changes.</summary>
    /// <param name="alwaysTell">Tells even when the panes stay where they are, as after ▥ or ▤, which changes the settings.</param>
    public static void Update(bool alwaysTell = false)
    {
        bool beside = IsAutomatic ? AutomaticChoice() : (string?)Settings["panesSide"] != "below";
        if (beside == IsBeside && !alwaysTell) return;
        IsBeside = beside;
        Changed?.Invoke();
    }

    private static bool AutomaticChoice()
    {
        bool isWide = windowWidth > windowHeight;
        if (automaticBeside && (windowWidth < NarrowestBeside - Margin || !isWide)) automaticBeside = false;
        else if (!automaticBeside && windowWidth >= NarrowestBeside && isWide) automaticBeside = true;
        return automaticBeside;
    }
}
