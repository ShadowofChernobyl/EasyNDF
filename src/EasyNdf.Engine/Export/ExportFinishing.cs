using System.Text.RegularExpressions;
using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Export;

/// <summary>A mod's watermark: a comment line at the top of every file the mod exports, crediting its author.</summary>
/// <param name="On">Whether the line is written.</param>
/// <param name="Author">Who made the mod.</param>
/// <param name="Version">The mod's version. It is also added to the mod's name in "Edited by" comments.</param>
/// <param name="Game">The game version the mod is for.</param>
public sealed record Watermark(bool On, string Author, string Version, string Game)
{
    /// <summary>A watermark value as it is stored: line breaks become spaces, and at most 120 characters are kept.</summary>
    public static string Clean(string? value)
    {
        string text = Regex.Replace(value ?? "", "[\r\n]+", " ");
        return text.Length > 120 ? text[..120] : text;
    }
}

/// <summary>The "Line endings" setting.</summary>
public enum LineEndings
{
    /// <summary>"\n", as the game's own files use. The default.</summary>
    Lf,
    /// <summary>"\r\n".</summary>
    CrLf,
    /// <summary>Whatever the source file uses.</summary>
    Keep,
}

/// <summary>
/// What happens to an exported file after the engine has written it: its line endings, and the comment lines at the top
/// saying EasyNDF edited it and naming the mod.
/// </summary>
public static partial class ExportFinishing
{
    /// <summary>
    /// The watermark's line: the mod's name and version, then "by" the author, then "for game version", leaving out
    /// what is empty. For example "WTO 1.4, by Paul, for game version 1.2".
    /// </summary>
    public static string WatermarkLine(string? modName, Watermark watermark)
    {
        string version = JsText.Trim(watermark.Version), author = JsText.Trim(watermark.Author), game = JsText.Trim(watermark.Game);
        var parts = new List<string>();
        string nameAndVersion = JsText.Trim(JsText.Trim(modName ?? "") + (version != "" ? " " + version : ""));
        if (nameAndVersion != "") parts.Add(nameAndVersion);
        if (author != "") parts.Add("by " + author);
        if (game != "") parts.Add("for game version " + game);
        return string.Join(", ", parts);
    }

    /// <summary>The mod's name as "Edited by" comments give it: with the watermark's version when it has one.</summary>
    public static string ModNameForComments(string? modName, Watermark? watermark)
    {
        if (string.IsNullOrEmpty(modName)) return "";
        return modName + (watermark is { Version: not "" } ? " " + watermark.Version : "");
    }

    /// <summary>Applies the "Line endings" setting to an exported file.</summary>
    public static string WithLineEndings(string text, LineEndings setting) => setting switch
    {
        LineEndings.CrLf => AnyLineBreak().Replace(text, "\r\n"),
        LineEndings.Keep => text,
        _ => text.Replace("\r\n", "\n"),
    };

    /// <summary>
    /// Puts the comment lines an edited file starts with at its top: "// Edited with EasyNDF X.Y.Z" when that setting is
    /// on, and "// Mod: ..." when the mod's watermark is on. A file that already starts with them, because it was
    /// exported before, gets fresh ones in their place. Only files the rules changed get these lines.
    /// </summary>
    /// <param name="text">The exported file, after <see cref="WithLineEndings"/>.</param>
    /// <param name="appVersion">The app's version, such as "0.70.0".</param>
    /// <param name="editedWithLine">The "Edited with EasyNDF" setting.</param>
    /// <param name="modName">The mod's name.</param>
    /// <param name="watermark">The mod's watermark, or null when it has none.</param>
    public static string AddHeader(string text, string appVersion, bool editedWithLine, string? modName, Watermark? watermark)
    {
        string lineBreak = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        text = OldEditedWithLine().Replace(text, "", 1);
        text = OldModLine().Replace(text, "", 1);

        string header = "";
        if (editedWithLine) header += "// Edited with EasyNDF " + appVersion + lineBreak;
        if (watermark is { On: true })
        {
            string line = WatermarkLine(modName, watermark);
            if (line != "") header += "// Mod: " + line + lineBreak;
        }
        return header + text;
    }

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex AnyLineBreak();

    [GeneratedRegex(@"\A// Edited with EasyNDF [^\r\n]*\r?\n")]
    private static partial Regex OldEditedWithLine();

    [GeneratedRegex(@"\A// Mod: [^\r\n]*\r?\n")]
    private static partial Regex OldModLine();
}
