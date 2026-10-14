using System.Globalization;
using EasyNdf.Engine.Text;

namespace EasyNdf.Data.FileList;

/// <summary>How a row's status text is shown: in the normal colour, or as success, "look at this", or an error.</summary>
public enum StatusKind
{
    /// <summary>Plain text.</summary>
    Plain,

    /// <summary>Success: the export finished.</summary>
    Ok,

    /// <summary>Done, but with warnings or rules that changed nothing.</summary>
    Idle,

    /// <summary>Something failed.</summary>
    Error,
}

/// <summary>The texts of the file list: the Status column, file sizes and dates, and what a file holds.</summary>
public static class FileStatus
{
    /// <summary>
    /// The Status column's text for a row, and how it is shown (the HTML app's <c>jobState</c>): an error first; then
    /// "Off"; then what the last action said, with the warnings and rules that changed nothing; then whether the file is
    /// loaded and has a preset.
    /// </summary>
    public static (string Text, StatusKind Kind) Of(FileEntry row)
    {
        if (row.Message != "" && row.IsError) return (row.Message, StatusKind.Error);
        if (!row.On) return ("Off" + (row.HasPresetChosen ? "" : ", no preset") + InBrackets(row.Info), StatusKind.Plain);
        if (row.Message != "") return MessageWithCounts(row);
        if (row.Source is null) return ("File not loaded. Add " + row.Name + " again to reattach it.", StatusKind.Error);
        if (!row.HasPresetChosen) return ("No preset" + ReadOrNot(row), StatusKind.Plain);
        if (row.Preset is null) return ("Preset \"" + row.MissingPreset + "\" not found", StatusKind.Error);
        return ("Ready" + ReadOrNot(row), StatusKind.Plain);
    }

    /// <summary>"1 rule changed nothing", "3 rules changed nothing".</summary>
    public static string IdleText(int count) =>
        count == 1 ? "1 rule changed nothing" : Count(count) + " rules changed nothing";

    /// <summary>
    /// What a file holds, for the Status column: "952 descriptors", "12 constants", "2 unnamed descriptors", joined with commas.
    /// A file with neither constants nor unnamed descriptors says how many descriptors it has, even 0.
    /// </summary>
    public static string Holds(int descriptors, int constants, int unnamed)
    {
        var parts = new List<string>();
        if (descriptors != 0 || (constants == 0 && unnamed == 0)) parts.Add(Count(descriptors) + " descriptors");
        if (constants != 0) parts.Add(Count(constants) + " constants");
        if (unnamed != 0) parts.Add(Count(unnamed) + (unnamed == 1 ? " unnamed descriptor" : " unnamed descriptors"));
        return string.Join(", ", parts);
    }

    /// <summary>A file size as the Size column shows it: "512 B", "37 KB" (at least 1), "2.4 MB".</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1048576) return Count((long)Math.Max(1, JsNumber.Round(bytes / 1024.0))) + " KB";
        return JsNumber.ToFixed(bytes / 1048576.0, 1) + " MB";
    }

    /// <summary>A date as the Date modified column shows it: "2026-10-08 14:05".</summary>
    public static string Date(DateTime when) => when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A whole number with the user's thousands separator, as the browser's <c>toLocaleString</c> writes it.</summary>
    public static string Count(long number) => number.ToString("N0", CultureInfo.CurrentCulture);

    private static (string, StatusKind) MessageWithCounts(FileEntry row)
    {
        string text = row.Message;
        if (row.IdleRuleCount != 0)
            text = WithSentenceEnd(text) + IdleText(row.IdleRuleCount) + (row.WarningCount != 0 ? "" : "; click the row for details.");
        if (row.WarningCount != 0)
            text = WithSentenceEnd(text) + row.WarningCount + (row.WarningCount == 1 ? " warning" : " warnings") + "; click the row for details.";
        StatusKind kind = row.WarningCount != 0 || row.IdleRuleCount != 0 ? StatusKind.Idle : row.Done ? StatusKind.Ok : StatusKind.Plain;
        return (text, kind);
    }

    /// <summary>Text that ends a sentence gets a space after it; other text gets ". ".</summary>
    private static string WithSentenceEnd(string text) =>
        text + (text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') ? " " : ". ");

    /// <summary>" (952 descriptors)" once the file has been read, " (not read yet)" before.</summary>
    private static string ReadOrNot(FileEntry row) => row.Info != "" ? InBrackets(row.Info) : row.Scanned ? "" : " (not read yet)";

    private static string InBrackets(string info) => info != "" ? " (" + info + ")" : "";
}
