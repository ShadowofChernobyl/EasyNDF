using System.Text.Json;
using System.Text.Json.Nodes;
using EasyNdf.Engine;
using EasyNdf.Engine.Rules;
using EasyNdf.Engine.Viewer;

namespace EngineCheck;

/// <summary>
/// One test case for comparing this engine with the HTML app's: a file, the rules to run, and the run settings.
/// The JavaScript side (tools/engine-check/js-engine.js) reads and writes the same format.
/// </summary>
public sealed class EngineCase
{
    public string Name { get; set; } = "";

    /// <summary>The NDF text to load.</summary>
    public string Text { get; set; } = "";

    /// <summary>The rules, as in a mod file.</summary>
    public JsonArray Rules { get; set; } = [];

    public bool WantText { get; set; } = true;
    public int Cap { get; set; } = 500;

    /// <summary>Other files for Lookup rules, by lower-case name.</summary>
    public Dictionary<string, string> Aux { get; set; } = [];

    public bool KeepComments { get; set; } = true;
    public bool Check { get; set; } = true;
    public string Mod { get; set; } = "";
    public int? Dec { get; set; }

    /// <summary>What the JavaScript engine gave, for cases stored with their expected result.</summary>
    public JsonObject? Expected { get; set; }

    /// <summary>What the JavaScript engine's descriptor viewer gave for <see cref="Text"/>, for viewer cases.</summary>
    public JsonObject? Viewer { get; set; }
}

/// <summary>Runs cases and turns results into the JSON both engines write, so they can be compared field by field.</summary>
public static class EngineCases
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<EngineCase> ReadCases(string json) =>
        JsonSerializer.Deserialize<List<EngineCase>>(json, Json) ?? [];

    /// <summary>Runs one case. An exception becomes an "error" field, as on the JavaScript side.</summary>
    public static JsonObject Run(EngineCase c)
    {
        var result = new JsonObject { ["name"] = c.Name };
        try
        {
            var engine = new NdfEngine();
            engine.Load(c.Text);
            var rules = c.Rules.Deserialize<List<Rule?>>(RuleJson.Options) ?? [];
            var run = engine.Run(rules, new RunOptions
            {
                WantText = c.WantText,
                LogLimit = c.Cap,
                OtherFiles = c.Aux,
                KeepSourceComments = c.KeepComments,
                CheckOutput = c.Check,
                ModName = c.Mod,
                DecimalPlaces = c.Dec,
            });
            result["text"] = run.Text;
            result["parseError"] = run.ParseError;
            result["stats"] = new JsonArray(run.Stats.Select(s => (JsonNode)new JsonObject
            {
                ["name"] = s.Name, ["kind"] = s.Kind, ["enabled"] = s.Enabled,
                ["matched"] = s.Matched, ["edits"] = s.Edits, ["skipped"] = s.Skipped, ["off"] = s.OffByGroup,
            }).ToArray());
            result["logs"] = new JsonArray(run.Logs.Select(l => (JsonNode)new JsonArray(l.Select(e => (JsonNode)new JsonObject
            {
                ["d"] = e.Descriptor, ["t"] = e.Target, ["o"] = e.Old, ["n"] = e.New,
            }).ToArray())).ToArray());
            result["warnings"] = new JsonArray(run.Warnings.Select(w => (JsonNode)new JsonObject
            {
                ["rule"] = w.Rule, ["msg"] = w.Message, ["type"] = w.Type,
            }).ToArray());
        }
        catch (Exception e)
        {
            result["error"] = e.Message;
        }
        return result;
    }

    /// <summary>
    /// The descriptor viewer's results for a text, in the JavaScript engine's shape: the outline, each declaration's
    /// property rows, and the filter index.
    /// </summary>
    public static JsonObject RunViewer(string text)
    {
        var result = new JsonObject();
        List<OutlineEntry> outline;
        try { outline = DescriptorViewer.Outline(text); }
        catch (Exception e) { result["outline"] = new JsonObject { ["error"] = e.Message }; return result; }

        string KindCode(OutlineKind k) => k switch { OutlineKind.Object => "o", OutlineKind.Constant => "c", _ => "u" };
        result["outline"] = new JsonArray(outline.Select(o => (JsonNode)new JsonObject
        {
            ["n"] = o.Name, ["t"] = o.Type, ["k"] = KindCode(o.Kind), ["s"] = o.Start, ["e"] = o.End,
        }).ToArray());

        result["props"] = new JsonArray(outline.Select(o =>
        {
            try
            {
                var list = DescriptorViewer.Properties(text[o.Start..o.End]);
                var rows = list.Rows.Select(r => (JsonNode)(r switch
                {
                    ValueRow v => new JsonObject { ["d"] = v.Depth, ["k"] = v.Key, ["v"] = v.Value, ["s"] = v.Start, ["e"] = v.End },
                    GroupRow g when g.IsObject => new JsonObject { ["d"] = g.Depth, ["k"] = g.Key, ["t"] = g.Title, ["n"] = g.Count, ["s"] = g.Start, ["o"] = 1 },
                    GroupRow g => new JsonObject { ["d"] = g.Depth, ["k"] = g.Key, ["t"] = g.Title, ["n"] = g.Count, ["s"] = g.Start },
                    _ => throw new InvalidOperationException(),
                })).ToArray();
                return (JsonNode)new JsonObject { ["n"] = list.Name, ["t"] = list.Type, ["rows"] = new JsonArray(rows), ["cut"] = list.IsCut };
            }
            catch (Exception e) { return new JsonObject { ["error"] = e.Message }; }
        }).ToArray());

        try
        {
            result["index"] = new JsonArray(DescriptorViewer.Index(text).Select(i => (JsonNode)new JsonObject
            {
                ["n"] = i.Name, ["t"] = i.Type,
                ["p"] = new JsonArray(i.Properties.Select(p => (JsonNode)new JsonArray(p.Name, p.Value)).ToArray()),
            }).ToArray());
        }
        catch (Exception e) { result["index"] = new JsonObject { ["error"] = e.Message }; }
        return result;
    }

    /// <summary>The fields that differ between two results, described for a person to read. Empty when they match.</summary>
    public static List<string> Differences(JsonObject expected, JsonObject actual)
    {
        var differences = new List<string>();
        foreach (string field in new[] { "error", "text", "parseError", "stats", "logs", "warnings" })
        {
            string e = expected[field]?.ToJsonString() ?? "null";
            string a = actual[field]?.ToJsonString() ?? "null";
            if (e == a) continue;
            if (field == "text" && expected[field] is JsonValue ev && actual[field] is JsonValue av)
                differences.Add("text differs " + FirstDifference(ev.GetValue<string>(), av.GetValue<string>()));
            else
                differences.Add(field + " differs:\n  expected " + Clip(e) + "\n  actual   " + Clip(a));
        }
        return differences;
    }

    private static string Clip(string text) => text.Length > 600 ? text[..600] + "..." : text;

    private static string FirstDifference(string expected, string actual)
    {
        int i = 0;
        while (i < expected.Length && i < actual.Length && expected[i] == actual[i]) i++;
        int line = 1;
        for (int k = 0; k < i; k++) if (expected[k] == '\n') line++;
        string Around(string s) => JsonSerializer.Serialize(s.Substring(Math.Max(0, i - 40), Math.Min(s.Length, i + 60) - Math.Max(0, i - 40)));
        return "at line " + line + " (character " + i + "):\n  expected " + Around(expected) + "\n  actual   " + Around(actual);
    }
}
