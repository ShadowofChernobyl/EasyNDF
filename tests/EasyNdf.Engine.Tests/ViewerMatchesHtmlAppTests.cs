using EngineCheck;

namespace EasyNdf.Engine.Tests;

/// <summary>
/// The descriptor viewer's lists (outline, property rows, filter index) must be exactly what the HTML app's engine
/// gives. Cases/viewer-cases.json holds texts with the HTML app's results; regenerate them with
/// <c>node tools/engine-check/js-engine.js viewer "path/to/EasyNDF vX.Y.Z.html" tests/EasyNdf.Engine.Tests/Cases/viewer-cases.json</c>.
/// </summary>
public class ViewerMatchesHtmlAppTests
{
    private static readonly Lazy<List<EngineCase>> Cases = new(() =>
        EngineCases.ReadCases(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", "viewer-cases.json"))));

    public static TheoryData<string> CaseNames()
    {
        var names = new TheoryData<string>();
        foreach (var c in Cases.Value) names.Add(c.Name);
        return names;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Viewer_gives_the_same_lists_as_the_HTML_app(string name)
    {
        EngineCase c = Cases.Value.First(x => x.Name == name);
        Assert.Equal(c.Viewer!.ToJsonString(), EngineCases.RunViewer(c.Text).ToJsonString());
    }
}
