using System.Globalization;
using System.Text.Json.Nodes;
using EasyNdf.Data.FileList;

namespace EasyNdf.Data.Tests;

/// <summary>
/// The file list logic gives the same answers as the HTML app's own functions, on the random rows stored in
/// Cases/file-list-cases.json (recorded with tools/engine-check/file-list.js). The desktop app has no Preset column:
/// a row's preset stands for a mod that runs on it, cases sorted or fitted by the Preset column are left out, and so
/// are rows given a preset that no longer exists, which can't happen any more.
/// </summary>
public class FileListMatchesHtmlAppTests
{
    private static readonly JsonObject Cases = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", "file-list-cases.json")))!.AsObject();

    /// <summary>The presets the recorded rows can have; "Gone" is a preset that no longer exists.</summary>
    private static readonly Dictionary<string, ModPreset> Presets = new[]
    {
        "WTO (Ammunition.ndf)", "WTO (GDConstants.ndf)", "WTO (Strategic/GDConstants.ndf)", "WTO (Constantes/Strategic/GDConstants.ndf)",
        "Other", "X (UniteDescriptor.ndf)",
    }.ToDictionary(n => n, n => new ModPreset { Name = n });

    // The recordings were made with English number formatting.
    public FileListMatchesHtmlAppTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Status_texts_and_filters_match()
    {
        foreach (JsonNode? recorded in Cases["rows"]!.AsArray())
        {
            if (HasMissingPreset(recorded!["row"]!)) continue;
            FileEntry row = RowFrom(recorded["row"]!);
            var (text, kind) = FileStatus.Of(row);
            Assert.Equal(InDesktopWords((string?)recorded["state"]!["t"]), text);
            Assert.Equal((string?)recorded["state"]!["c"], kind switch { StatusKind.Error => "err", StatusKind.Ok => "ok", StatusKind.Idle => "idle", _ => "" });
            Assert.Equal((bool)recorded["match"]!, FileListView.Shows(row, ShowOnlyFrom((string)recorded["filter"]!), (string)recorded["find"]!));
        }
    }

    [Fact]
    public void Sizes_and_contents_match()
    {
        foreach (JsonNode? recorded in Cases["sizes"]!.AsArray())
            Assert.Equal((string?)recorded!["text"], FileStatus.Size((long)(double)recorded["bytes"]!));
        foreach (JsonNode? recorded in Cases["holds"]!.AsArray())
        {
            JsonNode info = recorded!["info"]!;
            Assert.Equal((string?)recorded["text"], FileStatus.Holds((int)info["descriptors"]!, (int)info["constants"]!, (int)info["unnamed"]!));
        }
    }

    [Fact]
    public void Preset_matches_match()
    {
        foreach (JsonNode? recorded in Cases["presets"]!.AsArray())
        {
            var presets = recorded!["presets"]!.AsArray().Select(n => new ModPreset { Name = (string)n! }).ToList();
            ModPreset? found = PresetMatching.Match((string)recorded["fileName"]!, (string)recorded["path"]!, presets);
            Assert.Equal((string?)recorded["expected"], found?.Name ?? "");
        }
    }

    [Fact]
    public void Sorting_matches()
    {
        foreach (JsonNode? recorded in Cases["sorts"]!.AsArray())
        {
            if ((string)recorded!["column"]! == "preset") continue;
            // the Export folder column shows the folder the file goes into, which the HTML app never did
            if ((string)recorded["column"]! == "dir") continue;
            var rows = recorded!["rows"]!.AsArray().Select(r => RowFrom(r!)).ToList();
            var sorted = FileListView.Sorted(rows, ColumnFrom((string)recorded["column"]!), (bool)recorded["ascending"]!);
            Assert.Equal(recorded["order"]!.AsArray().Select(n => (int)n!), sorted.Select(r => r.Id));
        }
    }

    [Fact]
    public void Sorting_only_the_rows_shown_gives_the_order_of_sorting_all_then_picking_them_out()
    {
        string[] searches = ["", "a", "/", ".ndf", "unite", "zzz"];
        foreach (JsonNode? recorded in Cases["sorts"]!.AsArray())
        {
            if ((string)recorded!["column"]! is "preset" or "dir") continue;
            var rows = recorded["rows"]!.AsArray().Select(r => RowFrom(r!)).ToList();
            FileColumn column = ColumnFrom((string)recorded["column"]!);
            foreach (bool ascending in new[] { true, false })
                foreach (ShowOnly showOnly in Enum.GetValues<ShowOnly>())
                    foreach (string search in searches)
                    {
                        IEnumerable<int> allThenPicked = FileListView.Sorted(rows, column, ascending).Where(row => FileListView.Shows(row, showOnly, search)).Select(row => row.Id);
                        Assert.Equal(allThenPicked, FileListView.SortedAndShown(rows, column, ascending, showOnly, search).Select(row => row.Id));
                    }
        }
    }

    [Fact]
    public void Column_widths_match()
    {
        // The same stand-in for measuring text as file-list.js uses: characters times a width per font, rounded up.
        static int Width(string text, ColumnFit.ListFont font) =>
            (int)Math.Ceiling(text.Length * (font == ColumnFit.ListFont.Code ? 7.2 : font == ColumnFit.ListFont.Small ? 5.9 : 6.6));

        int compared = 0;
        foreach (JsonNode? recorded in Cases["fits"]!.AsArray())
        {
            var rows = recorded!["rows"]!.AsArray().Select(r => RowFrom(r!)).ToList();
            if (recorded["shown"]!.AsArray().Any(c => (string)c! == "preset")) continue;
            // the Export folder column shows "Browse..." or the folder, where the HTML app showed both and a ×
            if (recorded["shown"]!.AsArray().Any(c => (string)c! == "dir")) continue;
            // the status is a link to Details where the HTML app wrote "click the row for details" (rows with warnings or
            // rules that changed nothing): the Status column is not compared there, its text being shorter
            bool linked = rows.Any(row => FileStatus.HasDetailsLink(row) || FileStatus.Of(row).Kind == StatusKind.Idle);
            // "No rules" is shorter than the HTML app's "No preset", which can make Status narrower
            if (rows.Any(row => FileStatus.Of(row).Text.Contains("no rules", StringComparison.OrdinalIgnoreCase))) continue;
            compared++;
            var shown = recorded["shown"]!.AsArray().Select(c => ColumnFrom((string)c!)).ToList();
            var widths = ColumnFit.Widths(shown, rows, (bool)recorded["grouped"]!, (int)recorded["available"]!, Width);
            foreach (var (id, expected) in recorded["widths"]!.AsObject())
                if (!(linked && ColumnFrom(id) == FileColumn.Status)) Assert.Equal((int)expected!, widths[ColumnFrom(id)]);
        }
        // enough cases are left to be worth it
        Assert.True(compared >= 15, compared + " cases compared");
    }

    /// <summary>A row as the C# list holds it, from a recorded HTML app row.</summary>
    private static FileEntry RowFrom(JsonNode j)
    {
        string preset = (string?)j["preset"] ?? "";
        JsonNode? source = j["src"];
        return new FileEntry
        {
            Id = (int)j["id"]!,
            Name = (string)j["name"]!,
            Folder = (string)j["path"]!,
            Source = source is null ? null : "loaded",
            Size = source is null ? null : (long)(double)source["size"]!,
            Modified = source is null ? null : DateTime.UnixEpoch.AddMilliseconds((double)source["lastModified"]!),
            On = (bool)j["on"]!,
            ModsOnFile = Presets.ContainsKey(preset) ? [new ModOnFile("Mod", [preset], HasArmoryChanges: false)] : [],
            ExportFolder = (string?)j["dir"]?["name"],
            Lines = (int?)j["lines"],
            Info = (string)j["info"]!,
            Scanned = (bool)j["scanned"]!,
            Message = (string)j["msg"]!,
            IsError = (bool)j["err"]!,
            Done = (bool)j["done"]!,
            WarningCount = (int)j["warnN"]!,
            IdleRuleCount = (int)j["idleN"]!,
            Edits = (int?)j["res"]?["edits"],
        };
    }

    /// <summary>Whether a recorded row was given a preset that no longer exists.</summary>
    private static bool HasMissingPreset(JsonNode row) => (string?)row["preset"] is { Length: > 0 } preset && !Presets.ContainsKey(preset);

    /// <summary>
    /// The HTML app's status text as the desktop app words it, now that files run mods' rules, not one preset, and the
    /// status is a link to Details in place of "click the row for details".
    /// </summary>
    private static string? InDesktopWords(string? text) =>
        text?.Replace("Off, no preset", "Off, no rules", StringComparison.Ordinal).Replace("No preset", "No rules", StringComparison.Ordinal)
            .Replace("; click the row for details.", ".", StringComparison.Ordinal).Replace(". Click the row for details.", ".", StringComparison.Ordinal);

    private static ShowOnly ShowOnlyFrom(string filter) => filter switch
    {
        "preset" => ShowOnly.HasPreset, "nopreset" => ShowOnly.NoPreset, "on" => ShowOnly.On, "off" => ShowOnly.Off,
        "changed" => ShowOnly.Changed, "problem" => ShowOnly.ErrorsOrWarnings, "idle" => ShowOnly.RuleChangedNothing,
        "unread" => ShowOnly.NotRead, _ => ShowOnly.All,
    };

    private static FileColumn ColumnFrom(string column) => column switch
    {
        "on" => FileColumn.On, "file" => FileColumn.File, "size" => FileColumn.Size, "lines" => FileColumn.Lines,
        "modified" => FileColumn.Modified, "dir" => FileColumn.ExportFolder, "num" => FileColumn.Number,
        _ => FileColumn.Status,
    };
}
