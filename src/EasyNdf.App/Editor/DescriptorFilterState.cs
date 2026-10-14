using System.Text.Json.Nodes;
using EasyNdf.Engine.Viewer;

namespace EasyNdf.App.Editor;

/// <summary>One condition of the Filter Descriptors window, as it is edited (the HTML app's <c>dfState.conds</c> items).</summary>
public sealed class FilterRule
{
    /// <summary>Whether the condition is used.</summary>
    public bool On { get; set; } = true;

    /// <summary>A name for it, shown in the list; optional.</summary>
    public string Name { get; set; } = "";

    /// <summary>The property tested: a property name, "(Name)" or "(Type)".</summary>
    public string Property { get; set; } = "";

    /// <summary>The test: one of <see cref="DescriptorFilterState.Tests"/>.</summary>
    public string Test { get; set; } = "contains";

    /// <summary>What it is tested against.</summary>
    public string Value { get; set; } = "";

    /// <summary>Whether the test needs no value ("exists", "is missing").</summary>
    public bool NeedsNoValue => Test is "exists" or "is missing";

    /// <summary>The condition as the engine tests it.</summary>
    public FilterCondition ForEngine() => new(On, Property, Test, Value);

    /// <summary>Whether it is complete, and so counts: on, with a property, and with a value unless the test needs none.</summary>
    public bool IsComplete => DescriptorFilter.IsComplete(ForEngine());

    /// <summary>A copy of it.</summary>
    public FilterRule Copy() => new() { On = On, Name = Name, Property = Property, Test = Test, Value = Value };
}

/// <summary>
/// The Filter Descriptors window's filter (the HTML app's <c>dfState</c>): whether all or any of its conditions must
/// match, its conditions, and the name of the saved filter it came from. It is kept in State["dvFilter"], and the saved
/// filters in State["dvFilters"], in the HTML app's own format.
/// </summary>
public sealed class DescriptorFilterState
{
    /// <summary>The tests a condition can make, in the window's order (the HTML app's <c>DF_OPS</c>).</summary>
    public static readonly string[] Tests = ["contains", "is", "is not", "starts with", "ends with", "is greater than", "is less than", "exists", "is missing"];

    /// <summary>"All" or "Any".</summary>
    public string Gate { get; set; } = "All";

    /// <summary>The conditions, in order.</summary>
    public List<FilterRule> Rules { get; set; } = [];

    /// <summary>The saved filter it came from or was saved as; "" for none.</summary>
    public string Name { get; set; } = "";

    /// <summary>Whether any condition counts: the filter is on (the HTML app's <c>dfOn</c>).</summary>
    public bool IsOn => Rules.Any(r => r.IsComplete);

    /// <summary>Whether a descriptor passes, by its entry in the filter index (null when it is not there).</summary>
    public bool Passes(IndexEntry? descriptor) => DescriptorFilter.Passes(descriptor, Rules.Select(r => r.ForEngine()), Gate == "Any");

    /// <summary>A copy of its gate and conditions, with no name (the HTML app's <c>dfCopyOf</c>).</summary>
    public DescriptorFilterState Copy() => new() { Gate = Gate == "Any" ? "Any" : "All", Rules = [.. Rules.Select(r => r.Copy())] };

    // ---- kept as the HTML app keeps it: {gate, conds: [{on, n, p, op, v}], name} ----

    /// <summary>The filter as State keeps it.</summary>
    public JsonObject ToJson(bool withName = true)
    {
        var json = new JsonObject
        {
            ["gate"] = Gate,
            ["conds"] = new JsonArray([.. Rules.Select(r => (JsonNode)new JsonObject { ["on"] = r.On, ["n"] = r.Name, ["p"] = r.Property, ["op"] = r.Test, ["v"] = r.Value })]),
        };
        if (withName) json["name"] = Name;
        return json;
    }

    /// <summary>A filter as State keeps it; an empty filter for anything else.</summary>
    public static DescriptorFilterState FromJson(JsonNode? json)
    {
        var filter = new DescriptorFilterState();
        if (json is not JsonObject state || state["conds"] is not JsonArray conditions) return filter;
        filter.Gate = (string?)state["gate"] == "Any" ? "Any" : "All";
        filter.Name = state["name"] is JsonValue name && name.TryGetValue(out string? text) ? text : "";
        foreach (JsonNode? condition in conditions)
        {
            if (condition is not JsonObject c) continue;
            filter.Rules.Add(Rule(c["on"], c["n"], c["p"], c["op"], c["v"]));
        }
        return filter;
    }

    /// <summary>A condition from its parts as JSON, each made safe: a test that is not known becomes "contains".</summary>
    public static FilterRule Rule(JsonNode? on, JsonNode? name, JsonNode? property, JsonNode? test, JsonNode? value)
    {
        string testText = TextOf(test);
        return new FilterRule
        {
            On = !(on is JsonValue v && v.TryGetValue(out bool isOn) && !isOn),
            Name = TextOf(name),
            Property = TextOf(property),
            Test = Tests.Contains(testText) ? testText : "contains",
            Value = TextOf(value),
        };
    }

    /// <summary>A JSON value as text, as JavaScript's String() gives it; "" for none.</summary>
    private static string TextOf(JsonNode? node) => node switch
    {
        null => "",
        JsonValue value when value.TryGetValue(out string? text) => text,
        JsonValue value => value.ToJsonString(),
        _ => node.ToJsonString(),
    };

    /// <summary>The filter the viewer used last time, from State.</summary>
    public static DescriptorFilterState Load() => FromJson(AppData.Current.State["dvFilter"]);

    /// <summary>Keeps the filter in State.</summary>
    public void Save()
    {
        AppData.Current.State["dvFilter"] = ToJson();
        AppData.Current.SaveState();
    }

    /// <summary>The saved filters, by name, from State.</summary>
    public static SortedDictionary<string, DescriptorFilterState> LoadSaved()
    {
        var saved = new SortedDictionary<string, DescriptorFilterState>(StringComparer.CurrentCulture);
        if (AppData.Current.State["dvFilters"] is JsonObject all)
            foreach ((string name, JsonNode? filter) in all) saved[name] = FromJson(filter);
        return saved;
    }

    /// <summary>Keeps the saved filters in State.</summary>
    public static void StoreSaved(IReadOnlyDictionary<string, DescriptorFilterState> saved)
    {
        var all = new JsonObject();
        foreach ((string name, DescriptorFilterState filter) in saved) all[name] = filter.ToJson(withName: false);
        AppData.Current.State["dvFilters"] = all;
        AppData.Current.SaveState();
    }

    // ---- the clipboard and the filter file ----

    /// <summary>A condition on the clipboard: {EasyNDF: "filter condition", Condition: {On, Name, Property, Test, Value}}.</summary>
    public static string ClipboardText(FilterRule rule) => new JsonObject
    {
        ["EasyNDF"] = "filter condition",
        ["Condition"] = new JsonObject { ["On"] = rule.On, ["Name"] = rule.Name, ["Property"] = rule.Property, ["Test"] = rule.Test, ["Value"] = rule.Value },
    }.ToJsonString();

    /// <summary>A condition from the clipboard's text; null when it holds none.</summary>
    public static FilterRule? FromClipboard(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is JsonObject { } copied && copied["Condition"] is JsonObject c)
                return Rule(c["On"], c["Name"], c["Property"], c["Test"], c["Value"]);
        }
        catch (System.Text.Json.JsonException) { }
        return null;
    }

    /// <summary>Saved filters as a filter file: {EasyNDF: "descriptor filters", Version: 1, Filters: {name: {Gate, Conditions}}}.</summary>
    public static string FileText(IReadOnlyDictionary<string, DescriptorFilterState> filters)
    {
        var all = new JsonObject();
        foreach ((string name, DescriptorFilterState filter) in filters)
            all[name] = new JsonObject
            {
                ["Gate"] = filter.Gate,
                ["Conditions"] = new JsonArray([.. filter.Rules.Select(r => (JsonNode)new JsonObject
                    { ["On"] = r.On, ["Name"] = r.Name, ["Property"] = r.Property, ["Test"] = r.Test, ["Value"] = r.Value })]),
            };
        var file = new JsonObject { ["EasyNDF"] = "descriptor filters", ["Version"] = 1, ["Filters"] = all };
        return file.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The filters in a filter file, by name; null when it is not a filter file.</summary>
    public static List<(string Name, DescriptorFilterState Filter)>? FromFile(string text)
    {
        JsonNode? file;
        try { file = JsonNode.Parse(text); }
        catch (System.Text.Json.JsonException) { return null; }
        if (file?["Filters"] is not JsonObject filters) return null;
        var found = new List<(string, DescriptorFilterState)>();
        foreach ((string name, JsonNode? node) in filters)
        {
            if (node is not JsonObject filter || filter["Conditions"] is not JsonArray conditions) continue;
            var state = new DescriptorFilterState { Gate = (string?)filter["Gate"] == "Any" ? "Any" : "All" };
            foreach (JsonNode? condition in conditions)
                if (condition is JsonObject c) state.Rules.Add(Rule(c["On"], c["Name"], c["Property"], c["Test"], c["Value"]));
            found.Add((name, state));
        }
        return found;
    }
}
