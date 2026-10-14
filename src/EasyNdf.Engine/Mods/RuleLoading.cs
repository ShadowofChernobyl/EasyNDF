using System.Text.Json.Nodes;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Engine.Mods;

/// <summary>
/// Tidies rules as they are loaded from storage or a mod file, the way the HTML app does, so every rule has every
/// field and old formats keep working.
/// </summary>
/// <remarks>
/// <para>This works on the JSON itself, so fields this app does not know about are kept, as the HTML app keeps them.</para>
/// <list type="bullet">
/// <item>Missing fields get their defaults (those of a new rule, condition or action).</item>
/// <item>A rule of a type this app does not know is kept, switched off, with "[unknown rule type: X]" added to its name.</item>
/// <item>The earlier "Add descriptor" action becomes an Add rule of its own.</item>
/// <item><c>DataTypeToString</c> fields are written for the original WinForms app, which needs them.</item>
/// </list>
/// </remarks>
public static class RuleLoading
{
    /// <summary>Every rule type, with its name as the "New rule" list shows it.</summary>
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>
    {
        ["Edit"] = "Edit rule", ["Add"] = "Add descriptor", ["Clone"] = "Clone descriptor", ["Remove"] = "Remove descriptors",
        ["Replace"] = "Find and replace", ["Scale"] = "Scale", ["Formula"] = "Formula", ["Table"] = "Table",
        ["Lookup"] = "Lookup", ["Check"] = "Check", ["Group"] = "Group", ["Variable"] = "Variable",
    };

    /// <summary>The settings a new rule of each type starts with.</summary>
    private static JsonObject DefaultParams(string kind) => kind switch
    {
        "Add" => new() { ["Name"] = "", ["Text"] = "", ["Position"] = "end", ["After"] = "" },
        "Clone" => new() { ["Source"] = "", ["NewName"] = "", ["NewIds"] = true },
        "Replace" => new() { ["Find"] = "", ["Replace"] = "", ["Mode"] = "whole", ["CommentText"] = "", ["CommentOriginal"] = false },
        "Scale" => new()
        {
            ["Target"] = "", ["Op"] = "Multiply", ["Value"] = "", ["Step"] = "", ["RoundMode"] = "nearest",
            ["Min"] = "", ["Max"] = "", ["CommentText"] = "", ["CommentOriginal"] = false,
        },
        "Formula" => new() { ["Target"] = "", ["Expression"] = "", ["CommentText"] = "", ["CommentOriginal"] = false },
        "Table" => new() { ["Text"] = "", ["Op"] = "Set To", ["CommentText"] = "", ["CommentOriginal"] = false },
        "Lookup" => new()
        {
            ["SourceFile"] = "", ["SourceGate"] = "Any", ["SourceConditions"] = new JsonArray(),
            ["Collect"] = "Descriptor Title", ["MatchOn"] = "Descriptor Title",
        },
        "Check" => new() { ["Expect"] = "atLeast", ["Count"] = "1", ["Message"] = "" },
        "Variable" => new() { ["VarName"] = "", ["Value"] = "" },
        _ => new(),
    };

    public static JsonObject NewComment() => new()
    {
        ["Text"] = "", ["Enabled"] = false, ["IncludeTimestamp"] = false, ["IncludeOriginalValue"] = false,
        ["IncludeMod"] = false, ["DataTypeToString"] = "Comment",
    };

    public static JsonObject NewCondition() => new()
    {
        ["Name"] = "", ["Operand"] = "Descriptor Title", ["Operator"] = "Contains", ["Value"] = "",
        ["ManualOverride"] = false, ["CustomOperand"] = "", ["DataTypeToString"] = "Condition",
    };

    public static JsonObject NewAction() => new()
    {
        ["Name"] = "", ["Target"] = "", ["Operator"] = "Multiply [int, float]", ["Value"] = "",
        ["ManualOverride"] = false, ["CustomTarget"] = "", ["Comment"] = NewComment(), ["DataTypeToString"] = "Action",
    };

    /// <summary>A new rule of a type, as the "New rule" button makes it. A Clone rule gets a new seed for its GUIDs.</summary>
    public static JsonObject NewRule(string kind)
    {
        if (!Kinds.ContainsKey(kind)) kind = "Edit";
        bool edit = kind == "Edit";
        var rule = new JsonObject
        {
            ["Name"] = edit ? "New rule" : "New " + Kinds[kind].ToLowerInvariant(),
            ["Kind"] = kind,
            ["Conditions"] = edit ? new JsonArray(NewCondition()) : new JsonArray(),
            ["Actions"] = edit ? new JsonArray(NewAction()) : new JsonArray(),
            ["LogicGate"] = edit ? "Any" : "All",
            ["Params"] = DefaultParams(kind),
            ["DataTypeToString"] = "Rule",
            ["Enabled"] = true,
        };
        if (kind == "Clone") rule["Params"]!["Seed"] = NewSeed();
        return rule;
    }

    /// <summary>
    /// A random seed for a Clone rule's GUIDs, made when the rule is made. Two Clone rules with the same new name, in two
    /// files, then make different GUIDs, while one rule makes the same GUIDs on every export.
    /// </summary>
    public static string NewSeed()
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        return string.Concat(Enumerable.Range(0, 12).Select(_ => digits[Random.Shared.Next(digits.Length)]));
    }

    /// <summary>
    /// Loads a list of rules: tidies each one, and turns the earlier "Add descriptor" action into Add rules.
    /// Anything that is not a list gives no rules.
    /// </summary>
    public static JsonArray LoadRules(JsonNode? list)
    {
        var loaded = new JsonArray();
        if (list is not JsonArray rules) return loaded;
        foreach (var raw in rules)
        {
            JsonObject rule = Tidy(raw);
            var adds = new List<JsonObject>();
            var actions = (JsonArray)rule["Actions"]!;
            for (int i = 0; i < actions.Count; i++)
            {
                var action = (JsonObject)actions[i]!;
                if (!(JsString.Of(action["Operator"]) ?? "null").StartsWith("Add descriptor", StringComparison.Ordinal)) continue;

                // The action's target was the new descriptor's name and its value was the text.
                var target = JsString.IsTruthy(action["ManualOverride"]) ? action["CustomTarget"] : action["Target"];
                var add = NewRule("Add");
                add["Name"] = rule["Name"]?.DeepClone();
                add["Enabled"] = rule["Enabled"]?.DeepClone();
                add["Params"] = new JsonObject
                {
                    ["Name"] = JsString.IsTruthy(target) ? JsString.Of(target) : "",
                    ["Text"] = JsString.IsTruthy(action["Value"]) ? JsString.Of(action["Value"]) : "",
                    ["Position"] = "end",
                    ["After"] = "",
                };
                adds.Add(add);
                actions.RemoveAt(i--);
            }
            // An Edit rule that only held "Add descriptor" actions is replaced by the Add rules.
            bool onlyAdds = JsString.Of(rule["Kind"]) == "Edit" && adds.Count > 0 && actions.Count == 0;
            if (!onlyAdds) loaded.Add(rule);
            foreach (var add in adds) loaded.Add(add);
        }
        return loaded;
    }

    /// <summary>One rule with every field filled in.</summary>
    public static JsonObject Tidy(JsonNode? raw)
    {
        JsonNode? source = raw;
        string? rawKind = raw is JsonObject o && JsString.IsTruthy(o["Kind"]) ? JsString.Of(o["Kind"]) : null;
        if (rawKind is not null && !Kinds.ContainsKey(rawKind))
        {
            // A rule type this app does not know: keep it, switched off and labelled, so nothing is lost.
            var copy = (JsonObject)raw!.DeepClone();
            source = copy;
            string name = JsString.IsTruthy(copy["Name"]) ? JsString.Of(copy["Name"])! : "";
            copy["Enabled"] = false;
            copy["Name"] = name + " [unknown rule type: " + rawKind + "]";
        }
        string kind = rawKind is not null && Kinds.ContainsKey(rawKind) ? rawKind : "Edit";

        var rule = Assign(NewRule(kind), source);
        rule["Kind"] = kind;
        rule["DataTypeToString"] = "Rule";
        rule["Conditions"] = TidyConditions(rule["Conditions"]);

        var actions = new JsonArray();
        if (rule["Actions"] is JsonArray rawActions)
        {
            foreach (var a in rawActions)
            {
                var action = Assign(NewAction(), a);
                action["DataTypeToString"] = "Action";
                var comment = Assign(NewComment(), action["Comment"]);
                comment["DataTypeToString"] = "Comment";
                action["Comment"] = comment;
                actions.Add(action);
            }
        }
        rule["Actions"] = actions;

        // Params that are not an object (or a list) count as none.
        var rawParams = rule["Params"];
        rule["Params"] = Assign(DefaultParams(kind), rawParams is JsonObject or JsonArray ? rawParams : null);
        if (kind == "Lookup") rule["Params"]!["SourceConditions"] = TidyConditions(rule["Params"]!["SourceConditions"]);
        return rule;
    }

    private static JsonArray TidyConditions(JsonNode? list)
    {
        var conditions = new JsonArray();
        if (list is not JsonArray items) return conditions;
        foreach (var c in items)
        {
            var condition = NewCondition();
            condition["Operand"] = "";
            condition = Assign(condition, c);
            condition["DataTypeToString"] = "Condition";
            conditions.Add(condition);
        }
        return conditions;
    }

    /// <summary>
    /// JavaScript's Object.assign(target, source): every field of source is copied over target's. As in JavaScript,
    /// a list gives its items as fields "0", "1"..., a text its characters, and other values give nothing.
    /// That only matters for damaged mod files, but it keeps their rules the same in both apps.
    /// </summary>
    private static JsonObject Assign(JsonObject target, JsonNode? source)
    {
        switch (source)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj) target[key] = value?.DeepClone();
                break;
            case JsonArray list:
                for (int i = 0; i < list.Count; i++) target[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = list[i]?.DeepClone();
                break;
            case JsonValue v when v.TryGetValue(out string? text):
                for (int i = 0; i < text.Length; i++) target[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = text[i].ToString();
                break;
        }
        return target;
    }

    /// <summary>Loads a list of rules and reads them for the engine.</summary>
    public static List<Rule?> LoadForEngine(JsonNode? list) =>
        System.Text.Json.JsonSerializer.Deserialize<List<Rule?>>(LoadRules(list), RuleJson.Options) ?? [];
}
