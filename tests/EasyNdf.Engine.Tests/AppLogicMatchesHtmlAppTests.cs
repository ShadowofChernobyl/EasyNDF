using System.Text.Json;
using System.Text.Json.Nodes;
using EasyNdf.Engine.Export;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Viewer;
using EasyNdf.Engine.Writing;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The HTML app's logic outside its engine, ported to C#: rule loading, the export header and line endings, the
/// descriptor filter, "Changes only" and file references. Cases/app-logic-cases.json holds random inputs with what the
/// HTML app's own functions gave; regenerate it with
/// <c>node tools/engine-check/app-logic.js "path/to/EasyNDF vX.Y.Z.html" tests/EasyNdf.Engine.Tests/Cases/app-logic-cases.json</c>.
/// </summary>
public class AppLogicMatchesHtmlAppTests
{
    private static readonly Lazy<JsonObject> Cases = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", "app-logic-cases.json")))!.AsObject());

    private static IEnumerable<JsonObject> CasesOf(string kind) => Cases.Value[kind]!.AsArray().Select(n => n!.AsObject());

    private static string Text(JsonNode? node) => node?.GetValue<string>() ?? "";

    /// <summary>JSON with its fields in name order, so two values compare equal whatever order their fields were made in.</summary>
    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Key) + ":" + (p.Key == "Seed" ? "\"(seed)\"" : Canonical(p.Value)))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canonical)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };

    [Fact]
    public void Rules_are_loaded_the_same_way()
    {
        // New Clone rules get a random seed in both apps, so seeds are compared only as being there.
        foreach (var c in CasesOf("loadRules"))
            Assert.Equal(Canonical(c["expected"]), Canonical(RuleLoading.LoadRules(c["input"]?.DeepClone())));
    }

    [Fact]
    public void The_export_header_and_line_endings_are_the_same()
    {
        foreach (var c in CasesOf("header"))
        {
            var settings = c["settings"]!.AsObject();
            var lineEndings = Text(settings["eol"]) switch { "crlf" => LineEndings.CrLf, "keep" => LineEndings.Keep, _ => LineEndings.Lf };
            Assert.Equal(Text(c["withEol"]), ExportFinishing.WithLineEndings(Text(c["text"]), lineEndings));

            var raw = c["watermark"]!.AsObject();
            var watermark = new Watermark(raw["on"]!.GetValue<bool>(), Watermark.Clean(Text(raw["author"])), Watermark.Clean(Text(raw["ver"])), Watermark.Clean(Text(raw["game"])));
            Assert.Equal(Text(c["cleaned"]!["author"]), watermark.Author);

            bool editedWith = settings["stamp"] is not JsonValue v || v.GetValue<bool>();
            string mod = Text(c["mod"]);
            Assert.Equal(Text(c["expected"]), ExportFinishing.AddHeader(Text(c["withEol"]), "0.70.0", editedWith, mod, watermark));
            Assert.Equal(Text(c["line"]), ExportFinishing.WatermarkLine(mod, watermark));
            Assert.Equal(Text(c["modForComments"]), ExportFinishing.ModNameForComments(mod, watermark));
        }
    }

    [Fact]
    public void The_descriptor_filter_gives_the_same_answers()
    {
        foreach (var c in CasesOf("filter"))
        {
            IndexEntry? descriptor = c["descriptor"] is JsonObject d
                ? new IndexEntry(Text(d["n"]), Text(d["t"]), d["p"]!.AsArray().Select(p => (Text(p![0]), Text(p[1]))).ToList())
                : null;
            var conditions = c["conds"]!.AsArray().Select(x => new FilterCondition(
                x!["on"]!.GetValue<bool>(), Text(x["p"]), Text(x["op"]), x["v"] is null ? null : Text(x["v"]))).ToList();
            bool expected = c["expected"]!.GetValue<bool>();
            Assert.True(expected == DescriptorFilter.Passes(descriptor, conditions, Text(c["gate"]) == "Any"), c.ToJsonString());
        }
    }

    [Fact]
    public void Changes_only_finds_the_same_changed_lines()
    {
        foreach (var c in CasesOf("diff"))
        {
            var a = c["a"]!.AsArray().Select(Text).ToList();
            var b = c["b"]!.AsArray().Select(Text).ToList();
            var expected = c["expected"]!.AsArray().Select(h => new DiffHunk((int)h![0]!, (int)h[1]!, (int)h[2]!, (int)h[3]!)).ToList();
            Assert.Equal(expected, LineDiff.Compare(a, b));
        }
    }

    [Fact]
    public void File_references_point_to_the_same_files()
    {
        foreach (var c in CasesOf("refs"))
        {
            var keys = c["keys"]!.AsArray().Select(Text).ToList();
            Assert.Equal(c["expected"] is null ? null : Text(c["expected"]), FileReferences.Find(Text(c["ref"]), keys));
            Assert.Equal(c["short"]!.AsArray().Select(Text).ToList(), keys.Select(k => FileReferences.ShortReference(k, keys)).ToList());
        }
    }
}
