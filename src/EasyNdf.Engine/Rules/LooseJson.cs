using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using EasyNdf.Engine.Text;

namespace EasyNdf.Engine.Rules;

// Mod files are written by people and by two apps, so a field that should be text sometimes holds a number or true/false.
// The JavaScript app turns such values into text the way JavaScript's String() does; these converters do the same.

/// <summary>JavaScript's String(value) for one JSON value: 5 becomes "5", true becomes "true".</summary>
internal static class JsString
{
    public static string? Of(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonValue v when v.TryGetValue(out bool b) => b ? "true" : "false",
        JsonValue v when v.TryGetValue(out double d) => JsNumber.ToText(d),
        JsonArray a => string.Join(",", a.Select(x => Of(x) ?? "")),
        JsonObject => "[object Object]",
        _ => node.ToJsonString(),
    };

    /// <summary>JavaScript's truthiness: false, 0, "", null and NaN are false; everything else is true.</summary>
    public static bool IsTruthy(JsonNode? node) => node switch
    {
        null => false,
        JsonValue v when v.TryGetValue(out bool b) => b,
        JsonValue v when v.TryGetValue(out string? s) => s != "",
        JsonValue v when v.TryGetValue(out double d) => d != 0 && !double.IsNaN(d),
        _ => true,
    };
}

/// <summary>Reads any JSON value as text, the way JavaScript's String() would.</summary>
internal sealed class LooseTextConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsString.Of(JsonNode.Parse(ref reader));

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>Reads a switch that is on unless it is exactly false, like the JavaScript test <c>x !== false</c>.</summary>
internal sealed class OnlyFalseIsFalseConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        bool isFalse = reader.TokenType == JsonTokenType.False;
        reader.Skip(); // moves past an object or a list; does nothing for a single value
        return !isFalse;
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}

/// <summary>Reads a switch the way JavaScript's <c>if (x)</c> does: false, 0, "" and null are off.</summary>
internal sealed class TruthyConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsString.IsTruthy(JsonNode.Parse(ref reader));

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}

/// <summary>
/// The Params of a rule, read the way the JavaScript app reads them. Each method names the JavaScript expression it copies.
/// </summary>
public readonly struct RuleParams(Dictionary<string, JsonNode?>? values)
{
    private JsonNode? Get(string name) =>
        values is not null && values.TryGetValue(name, out var node) ? node : null;

    /// <summary>String(P.name == null ? '' : P.name)</summary>
    public string Text(string name) => JsString.Of(Get(name)) ?? "";

    /// <summary>String(P.name || fallback): the fallback when the value is missing, empty, 0 or false.</summary>
    public string TextOr(string name, string fallback) => JsString.IsTruthy(Get(name)) ? JsString.Of(Get(name))! : fallback;

    /// <summary>P.name === value: true only for that exact text.</summary>
    public bool Is(string name, string value) =>
        Get(name) is JsonValue v && v.TryGetValue(out string? s) && s == value;

    /// <summary>P.name !== false</summary>
    public bool IsNotFalse(string name) =>
        !(Get(name) is JsonValue v && v.TryGetValue(out bool b) && !b);

    /// <summary>!!P.name</summary>
    public bool IsOn(string name) => JsString.IsTruthy(Get(name));

    /// <summary>A list of conditions kept in the Params, such as a Lookup rule's SourceConditions.</summary>
    public List<Condition?> Conditions(string name) =>
        Get(name) is JsonArray array ? array.Deserialize<List<Condition?>>(RuleJson.Options) ?? [] : [];
}
