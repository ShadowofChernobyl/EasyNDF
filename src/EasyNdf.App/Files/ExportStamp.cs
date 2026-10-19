using System.Text.RegularExpressions;

namespace EasyNdf.App.Files;

/// <summary>
/// The "Edited with EasyNDF" comment that Run export puts at the top of a file it edits (setting "Edited with EasyNDF
/// comment"): finding it tells a file EasyNDF already exported from a clean one, as running the rules on such a file
/// again would apply them twice.
/// </summary>
public static partial class ExportStamp
{
    /// <summary>
    /// The stamp a file's text starts with: the "// Edited with EasyNDF ..." line, with the "// Mod: ..." line after
    /// it when there is one, without their "//"; null when the file does not start with it.
    /// </summary>
    public static string? Of(string text)
    {
        Match stamp = StampLines().Match(text);
        if (!stamp.Success) return null;
        string edited = stamp.Groups["edited"].Value.Trim();
        return stamp.Groups["mod"].Success ? edited + "\n" + stamp.Groups["mod"].Value.Trim() : edited;
    }

    // the lines as ExportFinishing.AddHeader writes them, after any byte-order mark
    [GeneratedRegex(@"\A﻿?// (?<edited>Edited with EasyNDF [^\r\n]*)\r?\n(?:// (?<mod>Mod: [^\r\n]*)\r?\n)?")]
    private static partial Regex StampLines();
}
