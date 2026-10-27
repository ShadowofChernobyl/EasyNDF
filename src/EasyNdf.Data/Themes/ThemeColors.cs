using System.Globalization;
using System.Text.RegularExpressions;

namespace EasyNdf.Data.Themes;

/// <summary>
/// The colours a theme actually shows, worked out the way the HTML app's <c>themeColor</c> and <c>applyTheme</c> do:
/// missing colours are filled in, and the "Beveled edges" setting can add or remove the light and dark edges.
/// </summary>
public static partial class ThemeColors
{
    /// <summary>
    /// Every colour key's colour for a theme, as "#rrggbb".
    /// </summary>
    /// <param name="theme">The theme.</param>
    /// <param name="bevelSetting">The "Beveled edges" setting: null until the user touches it, when every theme is beveled.</param>
    public static Dictionary<string, string> For(Theme theme, bool? bevelSetting)
    {
        var colors = new Dictionary<string, string>();
        foreach (var (key, _) in BuiltInThemes.Keys) colors[key] = ColorOf(theme, key);
        ApplyBevelSetting(theme, colors, bevelSetting);
        return colors;
    }

    /// <summary>
    /// Whether a theme has beveled edges of its own: its two edge colours differ, as Olivine's do.
    /// </summary>
    public static bool HasOwnBevel(Theme theme) =>
        !string.Equals(ColorOf(theme, "hi"), ColorOf(theme, "lo"), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The colours for a new or imported theme: every key, each from <paramref name="colors"/> when it is a valid colour,
    /// otherwise from the base theme (Olivine when there is none). This is why an import says "Missing colors were taken from Olivine".
    /// </summary>
    public static Dictionary<string, string> Complete(IReadOnlyDictionary<string, string> colors, string baseId)
    {
        Theme from = BuiltInThemes.ById.GetValueOrDefault(baseId) ?? BuiltInThemes.ById[BuiltInThemes.DefaultId];
        var full = new Dictionary<string, string>();
        foreach (var (key, _) in BuiltInThemes.Keys)
        {
            string valid = ValidColor(colors.GetValueOrDefault(key));
            if (valid == "" && from.Colors.TryGetValue(key, out string? fallback)) valid = fallback;
            if (valid != "") full[key] = valid;
        }
        return full;
    }

    /// <summary>"#abc" and "#AABBCC" give "#aabbcc"; anything else gives "".</summary>
    public static string ValidColor(string? text)
    {
        string value = (text ?? "").Trim();
        if (ShortHex().IsMatch(value)) value = "#" + value[1] + value[1] + value[2] + value[2] + value[3] + value[3];
        return LongHex().IsMatch(value) ? value.ToLowerInvariant() : "";
    }

    /// <summary>
    /// One colour of a theme: its own, else its base theme's, else a colour worked out from others (new and old lines in
    /// "Changes only" use the success and error colours; the toggle mixes the accent with the box colour; the weapon
    /// pictures take the text colour), else Olivine's.
    /// </summary>
    private static string ColorOf(Theme theme, string key)
    {
        if (theme.Colors.TryGetValue(key, out string? own) && own != "") return own;
        if (BuiltInThemes.ById.TryGetValue(theme.BaseId, out Theme? baseTheme) && baseTheme.Colors.TryGetValue(key, out string? inherited)) return inherited;
        return key switch
        {
            "d-add" => ColorOf(theme, "ok"),
            "d-del" => ColorOf(theme, "warn"),
            "toggle" => HalfAndHalf(ColorOf(theme, "accent"), ColorOf(theme, "well")),
            // the game draws its weapon pictures light on dark; a theme without its own colour for them uses its text's
            "weapon-art" => ColorOf(theme, "ink"),
            _ => BuiltInThemes.ById[BuiltInThemes.DefaultId].Colors.GetValueOrDefault(key, "#000000"),
        };
    }

    /// <summary>
    /// The "Beveled edges" setting, on until the user turns it off: on gives a flat theme a lighter and a darker edge made
    /// from its own colours; off gives a beveled theme one edge colour.
    /// </summary>
    private static void ApplyBevelSetting(Theme theme, Dictionary<string, string> colors, bool? bevelSetting)
    {
        bool own = HasOwnBevel(theme), wanted = bevelSetting ?? true, light = theme.Scheme == "light";
        if (wanted && !own)
        {
            colors["hi"] = light ? Mix(colors["line"], 0.45, "#ffffff") : Mix(colors["line"], 0.82, colors["ink"]);
            colors["lo"] = light ? Mix(colors["line"], 0.62, "#000000") : Mix(colors["bg"], 0.45, "#000000");
        }
        else if (!wanted && own)
        {
            colors["lo"] = colors["hi"];
        }
    }

    /// <summary>The HTML app's <c>hexMix</c>: each channel the average of the two, halves rounded up.</summary>
    private static string HalfAndHalf(string a, string b)
    {
        int[] x = Channels(a), y = Channels(b);
        return ToHex((x[0] + y[0] + 1) / 2, (x[1] + y[1] + 1) / 2, (x[2] + y[2] + 1) / 2);
    }

    /// <summary>The Tread scrollbars' track (the HTML app's <c>color-mix(in srgb, var(--well) 60%, var(--lo))</c>).</summary>
    public static string ScrollTrack(IReadOnlyDictionary<string, string> colors) => Mix(colors["well"], 0.6, colors["lo"]);

    /// <summary>
    /// Adds the colours the app works out from a theme's own: the Tread scrollbars' track, and the Units tab's bars and
    /// side marks.
    /// </summary>
    public static void AddWorkedOut(Dictionary<string, string> colors)
    {
        colors["scroll-track"] = ScrollTrack(colors);
        colors["meter"] = Meter(colors);
        (colors["side-a"], colors["side-b"]) = Sides(colors);
    }

    /// <summary>
    /// The Units tab's bar colour: the accent, as Slate, Paper and Cartridge show it; but where the accent is the "ok"
    /// colour (Olivine), which marks the best unit, the caution colour, so a plain bar never looks like the best one.
    /// </summary>
    public static string Meter(IReadOnlyDictionary<string, string> colors) =>
        string.Equals(colors["accent"], colors["ok"], StringComparison.OrdinalIgnoreCase) ? colors["caution"] : colors["accent"];

    /// <summary>
    /// The Units tab's colours for the two sides of a game (NATO and PACT, Allies and Axis): a blue and a red, darker on a
    /// light theme so they stand out on its pale background.
    /// </summary>
    public static (string First, string Second) Sides(IReadOnlyDictionary<string, string> colors) =>
        IsLight(colors["bg"]) ? ("#3d6b9e", "#a3443d") : ("#6f93bd", "#bd6f68");

    /// <summary>Whether a colour is light: its brightness, as the eye sees it, is past half way.</summary>
    private static bool IsLight(string hex)
    {
        int[] rgb = Channels(hex);
        return 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2] > 127.5;
    }

    /// <summary>CSS's <c>color-mix(in srgb, a share%, b)</c>: <paramref name="share"/> of a, the rest of b.</summary>
    private static string Mix(string a, double share, string b)
    {
        int[] x = Channels(a), y = Channels(b);
        int Channel(int i) => (int)Math.Round(x[i] * share + y[i] * (1 - share), MidpointRounding.AwayFromZero);
        return ToHex(Channel(0), Channel(1), Channel(2));
    }

    /// <summary>The red, green and blue of "#rrggbb" (or "#rgb"), 0 to 255.</summary>
    public static int[] Channels(string hex)
    {
        string value = ValidColor(hex);
        if (value == "") return [0, 0, 0];
        return [Convert.ToInt32(value[1..3], 16), Convert.ToInt32(value[3..5], 16), Convert.ToInt32(value[5..7], 16)];
    }

    private static string ToHex(int red, int green, int blue) =>
        "#" + red.ToString("x2", CultureInfo.InvariantCulture) + green.ToString("x2", CultureInfo.InvariantCulture) + blue.ToString("x2", CultureInfo.InvariantCulture);

    [GeneratedRegex("\\A#[0-9a-fA-F]{3}\\z")]
    private static partial Regex ShortHex();

    [GeneratedRegex("\\A#[0-9a-fA-F]{6}\\z")]
    private static partial Regex LongHex();
}
