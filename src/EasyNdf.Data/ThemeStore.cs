using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EasyNdf.Data;

/// <summary>A theme the user made or imported. The built-in themes are part of the app, not files.</summary>
public sealed partial class CustomTheme
{
    /// <summary>
    /// A font's name as a theme keeps it (the HTML app's <c>fontName</c>): letters, digits, spaces, _ and - only, single
    /// spaces, at most 60 characters.
    /// </summary>
    public static string CleanFontName(string? name)
    {
        string kept = NotInFontName().Replace(name ?? "", "");
        // only plain spaces are left to squeeze, since every other kind of white space was taken out
        string squeezed = Spaces().Replace(kept, " ").Trim();
        return squeezed.Length > 60 ? squeezed[..60] : squeezed;
    }

    [GeneratedRegex("[^A-Za-z0-9 _\\-]")]
    private static partial Regex NotInFontName();

    [GeneratedRegex(" +")]
    private static partial Regex Spaces();

    /// <summary>The theme's name, shown in the Theme dropdown.</summary>
    public string Name { get; set; } = "";

    /// <summary>"dark" or "light": which built-in controls (such as scrollbars) suit it.</summary>
    public string Scheme { get; set; } = "dark";

    /// <summary>"plain" or "tread": the scrollbar style.</summary>
    public string Scrollbars { get; set; } = "plain";

    /// <summary>The font for the interface, or "" for the default.</summary>
    public string Font { get; set; } = "";

    /// <summary>The font for code and NDF text, or "" for the default.</summary>
    public string CodeFont { get; set; } = "";

    /// <summary>The theme's colours by key ("bg", "panel", "ink"...), as "#rrggbb".</summary>
    public Dictionary<string, string> Colors { get; set; } = [];

    /// <summary>The built-in theme it was made from, which "Reset colors" goes back to; "" for an imported theme (Olivine).</summary>
    public string BaseId { get; set; } = "";
}

/// <summary>
/// The custom themes, kept in the Themes folder: one file per theme, in the same format as "Export theme"
/// (<c>{EasyNDFTheme: 1, name, scheme, scrollbars, font, codeFont, colors}</c>).
/// </summary>
public sealed class ThemeStore
{
    /// <summary>The Themes folder; null when the themes live in memory only.</summary>
    private readonly string? folder;
    private readonly Dictionary<CustomTheme, string> pathOf = [];

    /// <summary>Reads every theme in the folder.</summary>
    public ThemeStore(string themesFolder)
    {
        folder = themesFolder;
        Directory.CreateDirectory(folder);
        foreach (string path in Directory.GetFiles(folder, "*.theme.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            if (JsonFiles.TryRead(path) is JsonObject json && Read(json) is CustomTheme theme)
            {
                Themes.Add(theme);
                pathOf[theme] = path;
            }
        }
    }

    /// <summary>Starts with no themes, kept in memory only: for data in memory only.</summary>
    public ThemeStore() { }

    /// <summary>The custom themes.</summary>
    public List<CustomTheme> Themes { get; } = [];

    /// <summary>
    /// Reads a theme file's JSON, as "Import theme..." does: the colours may be under "colors" or "c". Returns null when there
    /// are no colours. Checking the colours against the theme keys, and filling missing ones, is the app's job.
    /// </summary>
    public static CustomTheme? Read(JsonObject json)
    {
        if ((json["colors"] ?? json["c"]) is not JsonObject colors) return null;
        var theme = new CustomTheme
        {
            Name = JsonValues.AsText(json["name"]),
            Scheme = JsonValues.AsText(json["scheme"]) == "light" ? "light" : "dark",
            Scrollbars = JsonValues.AsText(json["scrollbars"]) == "tread" ? "tread" : "plain",
            Font = JsonValues.AsText(json["font"]),
            CodeFont = JsonValues.AsText(json["codeFont"]),
            BaseId = JsonValues.AsText(json["base"]),
        };
        foreach (var (key, value) in colors) theme.Colors[key] = JsonValues.AsText(value);
        return theme;
    }

    /// <summary>The text "Export theme" saves for a theme: without its base theme, as the HTML app exports it.</summary>
    public static string ExportText(CustomTheme theme)
    {
        JsonObject json = ToJson(theme);
        json.Remove("base");
        return JsonFiles.ToText(json);
    }

    /// <summary>Adds or saves a theme.</summary>
    public void Save(CustomTheme theme)
    {
        if (!Themes.Contains(theme)) Themes.Add(theme);
        if (folder is null) return;
        pathOf.TryGetValue(theme, out string? oldPath);
        string path = FileNames.FreePath(folder, theme.Name, ".theme.json", pathOf.Where(p => p.Key != theme).Select(p => p.Value).ToList());
        JsonFiles.Write(path, ToJson(theme));
        if (oldPath is not null && oldPath != path) File.Delete(oldPath);
        pathOf[theme] = path;
    }

    /// <summary>Deletes a theme and its file.</summary>
    public void Delete(CustomTheme theme)
    {
        if (pathOf.Remove(theme, out string? path)) File.Delete(path);
        Themes.Remove(theme);
    }

    private static JsonObject ToJson(CustomTheme theme)
    {
        var colors = new JsonObject();
        foreach (var (key, value) in theme.Colors) colors[key] = value;
        return new JsonObject
        {
            ["EasyNDFTheme"] = 1,
            ["name"] = theme.Name,
            ["scheme"] = theme.Scheme,
            ["scrollbars"] = theme.Scrollbars,
            ["font"] = theme.Font,
            ["codeFont"] = theme.CodeFont,
            ["colors"] = colors,
            ["base"] = theme.BaseId,
        };
    }
}
