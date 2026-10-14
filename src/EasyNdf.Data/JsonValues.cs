using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyNdf.Data;

/// <summary>Reading JSON values the way JavaScript would, for files that may have been written by hand.</summary>
public static class JsonValues
{
    /// <summary>
    /// The value as JavaScript's <c>String(value)</c> would give it for texts, numbers and true/false;
    /// a missing value or null gives "".
    /// </summary>
    public static string AsText(JsonNode? value)
    {
        if (value is not JsonValue plain) return "";
        return plain.GetValueKind() switch
        {
            JsonValueKind.String => plain.GetValue<string>(),
            JsonValueKind.Number => plain.ToJsonString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    /// <summary>Whether JavaScript counts the value as true: anything but false, 0, "", null or a missing value.</summary>
    public static bool IsTruthy(JsonNode? value)
    {
        if (value is null) return false;
        if (value is not JsonValue plain) return true; // an object or a list
        return plain.GetValueKind() switch
        {
            JsonValueKind.False or JsonValueKind.Null => false,
            JsonValueKind.String => plain.GetValue<string>() != "",
            JsonValueKind.Number => plain.GetValue<double>() is not (0 or double.NaN),
            _ => true,
        };
    }

    /// <summary>Whether the value is exactly false.</summary>
    public static bool IsFalse(JsonNode? value) => value is JsonValue plain && plain.GetValueKind() == JsonValueKind.False;
}
