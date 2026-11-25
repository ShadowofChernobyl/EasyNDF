using System.Text.Json;
using System.Text.Json.Nodes;
using EasyNdf.Data;
using EasyNdf.Engine.Mods;

namespace EasyNdf.App.Rules;

/// <summary>
/// Several rules, or several conditions or actions, configured as one: <see cref="Merge"/> makes one object of them, in
/// which a value they do not share is <see cref="Mark"/>; the windows change that object as they change any other, and
/// <see cref="Apply"/> then writes only what was changed into each of them. What still differs is left as it is.
/// </summary>
public static class Mixed
{
    /// <summary>What stands for a value the rules or rows do not share.</summary>
    public const string Mark = "...";

    /// <summary>A rule's lists of rows, merged row by row rather than as one value.</summary>
    private static readonly HashSet<string> RowLists = ["Conditions", "Actions", "SourceConditions"];

    /// <summary>Whether a value stands for one the rules or rows do not share.</summary>
    public static bool Is(JsonNode? value) =>
        value is JsonValue plain && plain.GetValueKind() == JsonValueKind.String && plain.GetValue<string>() == Mark;

    /// <summary>A checkbox's state for a value: neither on nor off (null) when not shared.</summary>
    public static bool? Checked(JsonNode? value) => Is(value) ? null : JsonValues.IsTruthy(value);

    /// <summary>Whether a value is on; one not shared counts as off.</summary>
    public static bool IsOn(JsonNode? value) => !Is(value) && JsonValues.IsTruthy(value);

    /// <summary>Whether an object holds a value not shared, at any depth.</summary>
    public static bool Has(JsonNode? node) => node switch
    {
        JsonObject inner => inner.Any(pair => Has(pair.Value)),
        JsonArray items => items.Any(Has),
        _ => Is(node),
    };

    /// <summary>One object for several: each value they share, and <see cref="Mark"/> for each they do not.</summary>
    public static JsonObject Merge(IReadOnlyList<JsonObject> items)
    {
        var merged = new JsonObject();
        foreach (string key in items.SelectMany(item => item.Select(pair => pair.Key)).Distinct().ToList())
            merged[key] = MergeValues(key, [.. items.Select(item => item[key])]);
        return merged;
    }

    private static JsonNode? MergeValues(string key, List<JsonNode?> values)
    {
        if (RowLists.Contains(key) && values.All(value => value is null or JsonArray))
        {
            // row by row: each row of the merged list stands for the row at its place in every rule that has one there
            List<JsonArray> lists = [.. values.OfType<JsonArray>()];
            var rows = new JsonArray();
            for (int place = 0; place < lists.Max(list => list.Count); place++)
                rows.Add(Merge([.. lists.Where(list => place < list.Count).Select(list => list[place]).OfType<JsonObject>()]));
            return rows;
        }
        if (values.Any(value => value is JsonObject) && values.All(value => value is null or JsonObject))
            return Merge([.. values.Select(value => value as JsonObject ?? Blank(key))]);
        JsonNode? first = values.FirstOrDefault(value => !IsEmpty(value)) ?? values.FirstOrDefault(value => value is not null);
        return values.All(value => Same(value, first)) ? first?.DeepClone() : Mark;
    }

    /// <summary>What an object without a part has in its place: an action without a comment has one that is off.</summary>
    private static JsonObject Blank(string key) => key == "Comment" ? RuleLoading.NewComment() : [];

    /// <summary>Nothing, no text and "false" are one value: a row made before a setting existed has it off.</summary>
    private static bool IsEmpty(JsonNode? value) =>
        value is null || value is JsonValue plain && (plain.GetValueKind() is JsonValueKind.False or JsonValueKind.Null
            || plain.GetValueKind() == JsonValueKind.String && plain.GetValue<string>() == "");

    private static bool Same(JsonNode? one, JsonNode? other) => IsEmpty(one) && IsEmpty(other) || JsonNode.DeepEquals(one, other);

    /// <summary>The rows of a merged rule, each with its place in its list, to tell afterwards which rows were moved, added or deleted.</summary>
    public static Dictionary<JsonObject, int> RowPlaces(JsonObject merged)
    {
        var places = new Dictionary<JsonObject, int>(ReferenceEqualityComparer.Instance);
        foreach (JsonArray rows in new[] { merged["Conditions"], merged["Actions"], merged["Params"]?["SourceConditions"] }.OfType<JsonArray>())
            for (int place = 0; place < rows.Count; place++)
                if (rows[place] is JsonObject row) places[row] = place;
        return places;
    }

    /// <summary>
    /// Writes what was changed in a merged object into each of the objects it stands for: a value that differs from what
    /// it was, in the same place of each. A list of rows whose rows were moved, added or deleted is put in the same
    /// order in each rule, with the rows each rule had at those places.
    /// </summary>
    /// <param name="before">The merged object as it was made.</param>
    /// <param name="now">The merged object after the changes.</param>
    /// <param name="targets">The objects it stands for.</param>
    /// <param name="rowPlaces">The merged rule's rows as it was made (<see cref="RowPlaces"/>); null for rows.</param>
    public static void Apply(JsonObject before, JsonObject now, IReadOnlyList<JsonObject> targets, Dictionary<JsonObject, int>? rowPlaces = null)
    {
        foreach (string key in before.Select(pair => pair.Key).Concat(now.Select(pair => pair.Key)).Distinct().ToList())
        {
            JsonNode? was = before[key], value = now[key];
            if (JsonNode.DeepEquals(was, value) || Is(value)) continue;
            if (RowLists.Contains(key) && value is JsonArray rows && rowPlaces is not null)
                ApplyRows(key, was as JsonArray ?? [], rows, targets, rowPlaces);
            else if (was is JsonObject wasObject && value is JsonObject valueObject)
                Apply(wasObject, valueObject, [.. targets.Select(target => Part(target, key))], rowPlaces);
            else
                foreach (JsonObject target in targets) target[key] = value?.DeepClone();
        }
    }

    /// <summary>An object's part by name, made when it has none.</summary>
    private static JsonObject Part(JsonObject owner, string key)
    {
        if (owner[key] is JsonObject part) return part;
        owner[key] = part = Blank(key);
        return part;
    }

    private static void ApplyRows(string key, JsonArray before, JsonArray now, IReadOnlyList<JsonObject> targets, Dictionary<JsonObject, int> rowPlaces)
    {
        int PlaceOf(JsonNode? row) => row is JsonObject known && rowPlaces.TryGetValue(known, out int place) && place < before.Count ? place : -1;
        bool sameRows = now.Count == before.Count && Enumerable.Range(0, now.Count).All(place => PlaceOf(now[place]) == place);
        foreach (JsonObject target in targets)
        {
            if (target[key] is not JsonArray rows) target[key] = rows = [];
            if (sameRows)
            {
                for (int place = 0; place < Math.Min(rows.Count, now.Count); place++)
                    if (rows[place] is JsonObject row) Apply((JsonObject)before[place]!, (JsonObject)now[place]!, [row]);
                continue;
            }
            List<JsonNode?> had = [.. rows];
            rows.Clear();
            foreach (JsonNode? row in now)
            {
                int place = PlaceOf(row);
                if (place < 0) rows.Add(WithoutMarks(row?.DeepClone()));
                else if (place < had.Count && had[place] is JsonObject own)
                {
                    Apply((JsonObject)before[place]!, (JsonObject)row!, [own]);
                    rows.Add(own);
                }
            }
        }
    }

    /// <summary>A new row is written whole: a value it does not have is empty.</summary>
    private static JsonNode? WithoutMarks(JsonNode? node)
    {
        if (node is JsonObject inner)
            foreach (string key in inner.Select(pair => pair.Key).ToList())
            {
                if (Is(inner[key])) inner[key] = "";
                else WithoutMarks(inner[key]);
            }
        return node;
    }
}
