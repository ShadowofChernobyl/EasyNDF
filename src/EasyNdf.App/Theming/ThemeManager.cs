using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using EasyNdf.Data;
using EasyNdf.Data.Themes;

namespace EasyNdf.App.Theming;

/// <summary>
/// Puts a theme's colours into the app's resources, one brush per colour key ("bg", "panel", "ink", "s-comment"...), the same
/// keys as the HTML app's CSS variables. Every control refers to them with <c>DynamicResource</c>, so changing the theme
/// recolours the whole app at once.
/// </summary>
public static class ThemeManager
{
    /// <summary>The theme in use.</summary>
    public static Theme Current { get; private set; } = BuiltInThemes.ById[BuiltInThemes.DefaultId];

    /// <summary>Applies a theme.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="bevelSetting">The "Beveled edges" setting; null until the user touches it.</param>
    /// <param name="treadSetting">The "Tread scrollbars" setting; null until the user touches it.</param>
    public static void Apply(Theme theme, bool? bevelSetting, bool? treadSetting = null)
    {
        Current = theme;
        ResourceDictionary resources = Application.Current.Resources;
        Dictionary<string, string> colours = ThemeColors.For(theme, bevelSetting);
        ThemeColors.AddWorkedOut(colours);
        foreach (var (key, hex) in colours)
        {
            int[] rgb = ThemeColors.Channels(hex);
            var brush = new SolidColorBrush(Color.FromRgb((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]));
            brush.Freeze();
            resources[key] = brush;
        }
        // the Fonts window's choice, for every theme; else the theme's own font
        resources["ui-font"] = FontFor(Chosen("uiFont") ?? theme.Font, UiFonts);
        resources["mono"] = FontFor(Chosen("codeFont") ?? theme.CodeFont, CodeFonts);
        // the "Tread scrollbars" setting, on until the user turns it off, chooses for every theme (Styles/Controls.xaml)
        bool tread = treadSetting ?? true;
        resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = resources[tread ? "TreadScrollBar" : "PlainScrollBar"];
        Applied?.Invoke();
    }

    /// <summary>
    /// Raised after a theme is applied. What draws itself with the theme's colours, rather than referring to them, draws
    /// itself again: the editors' NDF colours, line numbers and marks.
    /// </summary>
    public static event Action? Applied;

    /// <summary>
    /// The theme chosen on the Settings tab (setting "theme"): a built-in theme by its id, or "system" for Paper or Slate
    /// as Windows' own apps are set to; Olivine for anything else. With the "Beveled edges" setting. A theme since
    /// replaced (Light, Dark) is swapped for its replacement in the setting too.
    /// </summary>
    public static void ApplySaved()
    {
        JsonObject settings = EasyNdf.App.AppData.Current.Settings;
        string id = (string?)settings["theme"] ?? BuiltInThemes.DefaultId;
        if (BuiltInThemes.Replaced.ContainsKey(id))
        {
            id = BuiltInThemes.CurrentId(id);
            settings["theme"] = id;
        }
        if (id == "system") id = WindowsAppsAreLight() ? BuiltInThemes.AutomaticLightId : BuiltInThemes.AutomaticDarkId;
        Theme theme = CustomThemeFor(id) ?? BuiltInThemes.ById.GetValueOrDefault(id) ?? BuiltInThemes.ById[BuiltInThemes.DefaultId];
        Apply(theme, SwitchOrNone(settings["bevel"]), SwitchOrNone(settings["treadScrollbars"]));
    }

    /// <summary>A switch's setting, or null while the user has not touched it (the theme then decides).</summary>
    private static bool? SwitchOrNone(JsonNode? setting) => setting is JsonValue value && value.TryGetValue(out bool on) ? on : null;

    /// <summary>The key a user's own theme is chosen by in the "theme" setting: "custom:" and its name.</summary>
    public static string CustomKey(CustomTheme theme) => "custom:" + theme.Name;

    /// <summary>
    /// A user's own theme, by its key; null when there is none of that key. A colour it lacks comes from the theme it was
    /// based on, or is worked out from its other colours (see <see cref="ThemeColors.For"/>), so a copy of Dark keeps
    /// Dark's toggle rather than taking Olivine's.
    /// </summary>
    public static Theme? CustomThemeFor(string key)
    {
        if (CustomThemeOf(key) is not CustomTheme custom) return null;
        return new Theme(custom.Name, custom.Scheme, new Dictionary<string, string>(custom.Colors), custom.Font, custom.CodeFont, custom.BaseId);
    }

    /// <summary>A user's own theme as it is kept, by its key; null when there is none of that key.</summary>
    public static CustomTheme? CustomThemeOf(string key) =>
        key.StartsWith("custom:", StringComparison.Ordinal) ? EasyNdf.App.AppData.Current.Themes.Themes.FirstOrDefault(t => CustomKey(t) == key) : null;

    /// <summary>Whether Windows is set to light for apps (Settings, Personalisation, Colours); dark when it cannot be read.</summary>
    private static bool WindowsAppsAreLight()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light != 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }

    /// <summary>The interface fonts, in the HTML app's order: Tahoma, then Verdana, then Segoe UI.</summary>
    private const string UiFonts = "Tahoma, Verdana, Segoe UI";

    /// <summary>The fonts for NDF text, in the HTML app's order.</summary>
    private const string CodeFonts = "Cascadia Mono, Consolas, Lucida Console";

    /// <summary>A font chosen in the Fonts window (setting "uiFont" or "codeFont"); null for the theme's own.</summary>
    private static string? Chosen(string key) =>
        AppData.Current.Settings[key] is JsonValue value && value.TryGetValue(out string? name) && name.Trim() != "" ? name.Trim() : null;

    /// <summary>The font chosen in the Fonts window for the interface or the code; "" for the theme's own.</summary>
    public static string ChosenFont(bool isCodeFont) => Chosen(isCodeFont ? "codeFont" : "uiFont") ?? "";

    /// <summary>The font the theme in use names for the interface or the code, or the usual one when it names none.</summary>
    public static string ThemeFontName(bool isCodeFont)
    {
        string own = isCodeFont ? Current.CodeFont : Current.Font;
        return !string.IsNullOrWhiteSpace(own) ? own.Trim() : (isCodeFont ? CodeFonts : UiFonts).Split(',')[0].Trim();
    }

    /// <summary>A font by its name, with the usual interface or code fonts behind it for anything it lacks.</summary>
    public static FontFamily FontFamilyFor(string name, bool isCodeFont) => FontFor(name, isCodeFont ? CodeFonts : UiFonts);

    /// <summary>A theme may name its own font; it goes in front of the usual ones, which stay as the fallback.</summary>
    private static FontFamily FontFor(string themeFont, string usual) =>
        new(string.IsNullOrWhiteSpace(themeFont) ? usual : themeFont + ", " + usual);
}
