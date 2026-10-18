using System.Globalization;
using EasyNdf.Engine.Ndf;

namespace EasyNdf.Data.Units;

/// <summary>
/// Reading values out of parsed NDF: the first property of a name anywhere inside a value, a number, a text, the last
/// part of a reference. The two games nest their units' fields differently, so fields are looked for by name.
/// </summary>
public static class NdfValues
{
    /// <summary>The first property of this name inside a value, looking through objects, lists and pairs in order.</summary>
    public static NdfValue? Find(NdfValue value, string name)
    {
        foreach (Property property in AllProperties(value))
            if (string.Equals(property.Name, name, StringComparison.Ordinal)) return property.Value;
        return null;
    }

    /// <summary>
    /// Every property inside a value: each object's own properties, object after object, outer objects before the
    /// objects inside them.
    /// </summary>
    public static IEnumerable<Property> AllProperties(NdfValue value) => AllObjects(value).SelectMany(o => o.Properties);

    /// <summary>Every object inside a value (and the value itself, when it is one), outer before inner, in written order.</summary>
    public static IEnumerable<ObjectValue> AllObjects(NdfValue value)
    {
        var waiting = new Stack<NdfValue>();
        waiting.Push(value);
        while (waiting.Count > 0)
        {
            // what is pushed last is read first, so each value's parts are pushed in reverse order
            switch (waiting.Pop())
            {
                case ObjectValue o:
                    yield return o;
                    for (int i = o.Properties.Count - 1; i >= 0; i--) waiting.Push(o.Properties[i].Value);
                    break;
                case ListValue list:
                    for (int i = list.Items.Count - 1; i >= 0; i--) waiting.Push(list.Items[i]);
                    break;
                case PairValue pair:
                    waiting.Push(pair.Second);
                    waiting.Push(pair.First);
                    break;
                case AssignValue assign:
                    waiting.Push(assign.Value);
                    break;
                case WrappedValue wrapped:
                    waiting.Push(wrapped.Inner);
                    break;
            }
        }
    }

    /// <summary>
    /// A value as a number: 12, -0.5, (68), ((68) * Metre) (the number before "* Metre"). Null when it is not one.
    /// </summary>
    public static double? Number(NdfValue? value)
    {
        switch (value)
        {
            case ScalarValue { Kind: ScalarKind.Number } scalar:
                return double.TryParse(scalar.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : null;
            case WrappedValue { Kind: WrapKind.Parentheses } wrapped:
                return Number(wrapped.Inner);
            case WrappedValue { Kind: WrapKind.Negative } negative:
                return Number(negative.Inner) is double inner ? -inner : null;
            case BinaryValue { Operator: "*" } product:
                // a length in metres: ((68) * Metre)
                return Number(product.Left);
            default:
                return null;
        }
    }

    /// <summary>
    /// A value as a text: a quoted text without its quotes, a name, or the last part of a path or reference
    /// ("TWargameCoalition/NATO" is "NATO", "$/GFX/Weapon/Ammo_X" is "Ammo_X"). Null for anything else, or nil.
    /// </summary>
    public static string? Text(NdfValue? value)
    {
        if (value is not ScalarValue scalar) return null;
        return scalar.Kind switch
        {
            ScalarKind.String => scalar.Text.Length >= 2 ? scalar.Text[1..^1] : "",
            ScalarKind.Identifier => scalar.Text,
            ScalarKind.Path or ScalarKind.Reference => LastPart(scalar.Text),
            _ => null,
        };
    }

    /// <summary>The part after the last slash: "EVisionRange/Standard" is "Standard".</summary>
    public static string LastPart(string text)
    {
        int slash = text.LastIndexOf('/');
        return slash < 0 ? text : text[(slash + 1)..];
    }

    /// <summary>The items of a list (a MAP is a list of pairs); none for anything else.</summary>
    public static IReadOnlyList<NdfValue> ItemsOf(NdfValue? value) => value is ListValue list ? list.Items : [];

    /// <summary>The pairs of a MAP, as their two values.</summary>
    public static IEnumerable<(NdfValue Key, NdfValue Value)> PairsOf(NdfValue? value)
    {
        foreach (NdfValue item in ItemsOf(value))
            if (item is PairValue pair) yield return (pair.First, pair.Second);
    }

    /// <summary>
    /// The number a MAP gives for the key whose last part is <paramref name="key"/>: in
    /// MAP [(EVisionRange/Standard, 3500.0)], "Standard" gives 3500.
    /// </summary>
    public static double? NumberFor(NdfValue? map, string key)
    {
        foreach (var (k, v) in PairsOf(map))
            if (Text(k) is string name && string.Equals(name, key, StringComparison.Ordinal)) return Number(v);
        return null;
    }
}
