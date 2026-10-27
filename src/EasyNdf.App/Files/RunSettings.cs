using System.Text.Json.Nodes;
using EasyNdf.Engine.Export;

namespace EasyNdf.App.Files;

/// <summary>The settings Preview and Run export follow, read from the app's settings (see EasyNdf.Data/SettingsStore.cs).</summary>
public sealed record RunSettings
{
    /// <summary>"Export: every file that is on" rather than only the files the rules changed.</summary>
    public bool ExportEveryFile { get; init; }

    /// <summary>"Ask for the export folder each time": every Run export asks where to write the files.</summary>
    public bool AskExportFolder { get; init; } = true;

    /// <summary>"Keep source comments".</summary>
    public bool KeepSourceComments { get; init; } = true;

    /// <summary>How many edits Details lists for each rule.</summary>
    public int LogLimit { get; init; } = 500;

    /// <summary>"Decimal places", or null for no limit.</summary>
    public int? DecimalPlaces { get; init; } = 2;

    /// <summary>Whether an edited file starts with "// Edited with EasyNDF".</summary>
    public bool EditedWithLine { get; init; } = true;

    /// <summary>"Line endings".</summary>
    public LineEndings LineEndings { get; init; } = LineEndings.Lf;

    /// <summary>Reads the settings as the HTML app stores them.</summary>
    public static RunSettings From(JsonObject settings)
    {
        int decimals = settings["decimals"]?.GetValue<int>() ?? 2;
        return new RunSettings
        {
            ExportEveryFile = (string?)settings["exportScope"] == "all",
            AskExportFolder = settings["askExportFolder"]?.GetValue<bool>() != false,
            KeepSourceComments = settings["keepComments"]?.GetValue<bool>() != false,
            LogLimit = settings["logCap"]?.GetValue<int>() ?? 500,
            // 0, like an empty box, means no limit
            DecimalPlaces = decimals > 0 ? decimals : null,
            EditedWithLine = settings["stamp"]?.GetValue<bool>() != false,
            LineEndings = (string?)settings["eol"] switch { "crlf" => LineEndings.CrLf, "keep" => LineEndings.Keep, _ => LineEndings.Lf },
        };
    }
}
