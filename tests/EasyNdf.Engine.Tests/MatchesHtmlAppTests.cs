using System.Text.Json;
using EngineCheck;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The engine must give exactly what the HTML app's engine gives. Each case in Cases/engine-cases.json holds a file,
/// rules and the HTML app's result; this test runs the case here and compares everything: the exported text, the
/// counts, the edit logs and the warnings.
/// </summary>
/// <remarks>
/// The cases are hand-written ones for each feature, followed by random ones from tools/engine-check/fuzz.js.
/// When the HTML app changes, give the cases new results with
/// <c>node tools/engine-check/js-engine.js expect "path/to/EasyNDF vX.Y.Z.html" tests/EasyNdf.Engine.Tests/Cases/engine-cases.json</c>.
/// </remarks>
public class MatchesHtmlAppTests
{
    private static readonly Lazy<List<EngineCase>> Cases = new(() =>
        EngineCases.ReadCases(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", "engine-cases.json"))));

    public static TheoryData<string> CaseNames()
    {
        var names = new TheoryData<string>();
        foreach (var c in Cases.Value)
            if (!UsesWhatTheHtmlAppLacks(c)) names.Add(c.Name);
        return names;
    }

    /// <summary>
    /// Whether a case uses a setting this engine has and the HTML app's does not, so the two rightly differ: a Table
    /// rule with conditions of its own (the HTML app ignores them), or a Lookup rule applied to objects of one type or
    /// to the constants. LookupScopeAndTableConditionTests covers those here.
    /// </summary>
    private static bool UsesWhatTheHtmlAppLacks(EngineCase c) =>
        c.Rules.OfType<System.Text.Json.Nodes.JsonObject>().Any(rule =>
            ((string?)rule["Kind"] == "Table" && rule["Conditions"] is System.Text.Json.Nodes.JsonArray { Count: > 0 }) ||
            ((string?)rule["Kind"] == "Lookup" && (string?)rule["Params"]?["Scope"] is "type" or "constants"));

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Case_gives_the_same_result_as_the_HTML_app(string name)
    {
        EngineCase c = Cases.Value.First(x => x.Name == name);
        var differences = EngineCases.Differences(c.Expected!, EngineCases.Run(c));
        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    [Fact]
    public void There_are_cases_to_run() => Assert.True(Cases.Value.Count > 100);
}
