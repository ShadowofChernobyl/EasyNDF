using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyNdf.Data;

/// <summary>A saved descriptor filter: its conditions, and whether all of them or any one must match.</summary>
public sealed class SavedFilter
{
    /// <summary>The filter's name, shown in the "Saved filter" dropdown.</summary>
    public string Name { get; set; } = "";

    /// <summary>"All" or "Any", as the HTML app writes it.</summary>
    public string Gate { get; set; } = "All";

    /// <summary>The filter's conditions.</summary>
    public List<SavedFilterCondition> Conditions { get; set; } = [];
}

/// <summary>One condition of a saved filter, with the names the filter file uses.</summary>
public sealed record SavedFilterCondition(bool On, string Name, string Property, string Test, string Value);

/// <summary>
/// The saved descriptor filters, kept in the Filters folder: one file per filter, each in the format of the filter window's
/// "Export" (<c>{EasyNDF: "descriptor filters", Version: 1, Filters: {name: {Gate, Conditions}}}</c>), so each file can also be
/// imported by the HTML app.
/// </summary>
public sealed class FilterStore
{
    private readonly string folder;
    private readonly Dictionary<SavedFilter, string> pathOf = [];

    /// <summary>Reads every filter in the folder.</summary>
    public FilterStore(string filtersFolder)
    {
        folder = filtersFolder;
        Directory.CreateDirectory(folder);
        foreach (string path in Directory.GetFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            if (JsonFiles.TryRead(path) is not JsonObject json) continue;
            foreach (SavedFilter filter in FiltersIn(json))
            {
                filter.Name = FreeName(filter.Name);
                Filters.Add(filter);
                pathOf[filter] = path;
            }
        }
    }

    /// <summary>The saved filters.</summary>
    public List<SavedFilter> Filters { get; } = [];

    /// <summary>The filter with this name, or null.</summary>
    public SavedFilter? Find(string name) => Filters.FirstOrDefault(f => f.Name == name);

    /// <summary>Adds or saves a filter. A filter of the same name is replaced ("Save as..." asks first).</summary>
    public void Save(SavedFilter filter)
    {
        SavedFilter? same = Find(filter.Name);
        if (same is not null && same != filter) Delete(same);
        if (!Filters.Contains(filter)) Filters.Add(filter);

        pathOf.TryGetValue(filter, out string? oldPath);
        string path = FileNames.FreePath(folder, filter.Name, ".json", pathOf.Where(p => p.Key != filter).Select(p => p.Value).ToList());
        JsonFiles.Write(path, FileJson([filter]));
        if (oldPath is not null && oldPath != path) File.Delete(oldPath);
        pathOf[filter] = path;
    }

    /// <summary>Deletes a filter and its file.</summary>
    public void Delete(SavedFilter filter)
    {
        if (pathOf.Remove(filter, out string? path)) File.Delete(path);
        Filters.Remove(filter);
    }

    /// <summary>
    /// The text the filter window's "Export" saves: every saved filter, or <paramref name="current"/> (called "Filter")
    /// when none is saved.
    /// </summary>
    public string ExportText(SavedFilter current) =>
        JsonFiles.ToText(FileJson(Filters.Count > 0 ? Filters : [new SavedFilter { Name = "Filter", Gate = current.Gate, Conditions = current.Conditions }]));

    /// <summary>
    /// "Import...": adds the filters in a filter file, renaming any whose name is taken to "name (2)". Returns how many were
    /// added. Throws <see cref="ModFileException"/> when the file is not a filter file.
    /// </summary>
    public int Import(string text, string fileName)
    {
        JsonNode? json = null;
        try { json = JsonFiles.Parse(text); } catch (JsonException) { }
        if (json is not JsonObject file || file["Filters"] is not JsonObject)
            throw new ModFileException("\"" + fileName + "\" is not a filter file.");

        int added = 0;
        foreach (SavedFilter filter in FiltersIn(file))
        {
            filter.Name = FreeName(filter.Name);
            Save(filter);
            added++;
        }
        return added;
    }

    /// <summary>A filter name no saved filter has: the name itself, or "name (2)", "name (3)"...</summary>
    private string FreeName(string name)
    {
        string free = name;
        for (int i = 2; Find(free) is not null; i++) free = name + " (" + i + ")";
        return free;
    }

    /// <summary>The filters in a filter file's JSON. Entries without a "Conditions" list are left out, as in the HTML app.</summary>
    private static List<SavedFilter> FiltersIn(JsonObject file)
    {
        var found = new List<SavedFilter>();
        if (file["Filters"] is not JsonObject filters) return found;
        foreach (var (name, value) in filters)
        {
            if (value is not JsonObject entry || entry["Conditions"] is not JsonArray conditions) continue;
            found.Add(new SavedFilter
            {
                Name = name,
                Gate = JsonValues.AsText(entry["Gate"]) == "Any" ? "Any" : "All",
                Conditions = conditions.OfType<JsonObject>().Select(c => new SavedFilterCondition(
                    On: !JsonValues.IsFalse(c["On"]),
                    Name: JsonValues.AsText(c["Name"]),
                    Property: JsonValues.AsText(c["Property"]),
                    Test: JsonValues.AsText(c["Test"]),
                    Value: JsonValues.AsText(c["Value"]))).ToList(),
            });
        }
        return found;
    }

    private static JsonObject FileJson(IEnumerable<SavedFilter> filters)
    {
        var byName = new JsonObject();
        foreach (SavedFilter filter in filters)
        {
            byName[filter.Name] = new JsonObject
            {
                ["Gate"] = filter.Gate,
                ["Conditions"] = new JsonArray(filter.Conditions.Select(c => (JsonNode)new JsonObject
                {
                    ["On"] = c.On,
                    ["Name"] = c.Name,
                    ["Property"] = c.Property,
                    ["Test"] = c.Test,
                    ["Value"] = c.Value,
                }).ToArray()),
            };
        }
        return new JsonObject { ["EasyNDF"] = "descriptor filters", ["Version"] = 1, ["Filters"] = byName };
    }
}
