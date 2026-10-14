using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Writing;

namespace EasyNdf.Engine.Viewer;

/// <summary>What kind of declaration an outline entry is.</summary>
public enum OutlineKind
{
    /// <summary>A named object: a descriptor.</summary>
    Object,
    /// <summary>A named value that is not an object: a constant.</summary>
    Constant,
    /// <summary>An unnamed declaration.</summary>
    Unnamed,
}

/// <summary>One declaration in a file's outline: the Editor tab's descriptor list.</summary>
/// <param name="Name">The declaration's name; empty for an unnamed one.</param>
/// <param name="Type">The object type; empty when the value is not an object.</param>
/// <param name="Kind">Descriptor, constant or unnamed.</param>
/// <param name="Start">Where the declaration starts in the text.</param>
/// <param name="End">Where it ends (one past its last character).</param>
public sealed record OutlineEntry(string Name, string Type, OutlineKind Kind, int Start, int End);

/// <summary>
/// One row of the descriptor viewer's property list.
/// </summary>
/// <param name="Depth">How deep the row is: 0 for the descriptor's own properties.</param>
/// <param name="Key">The property name, or [0], [1]... for list items, or "Key (Name)" for a pair.</param>
/// <param name="Start">Where the value starts in the declaration's text.</param>
public abstract record PropertyRow(int Depth, string Key, int Start);

/// <summary>A row with a value that can be shown and edited in place.</summary>
/// <param name="Value">The value as written in the text.</param>
/// <param name="End">Where the value ends in the declaration's text.</param>
public sealed record ValueRow(int Depth, string Key, int Start, string Value, int End) : PropertyRow(Depth, Key, Start);

/// <summary>A row that holds other rows: an object or a list.</summary>
/// <param name="Title">The object's type, or for a list its type (if any) and size, such as "[ 3 ]".</param>
/// <param name="Count">How many properties or items it holds.</param>
/// <param name="IsObject">True for an object, false for a list.</param>
public sealed record GroupRow(int Depth, string Key, int Start, string Title, int Count, bool IsObject) : PropertyRow(Depth, Key, Start);

/// <summary>The rows of one declaration's property list.</summary>
/// <param name="Name">The declaration's name; empty for an unnamed one.</param>
/// <param name="Type">The object type; empty when the value is not an object.</param>
/// <param name="Rows">Every property and sub-property, in order.</param>
/// <param name="IsCut">True when the list stopped at <see cref="DescriptorViewer.MaxRows"/> rows.</param>
public sealed record PropertyList(string Name, string Type, List<PropertyRow> Rows, bool IsCut);

/// <summary>One declaration and its properties at any depth, for the Filter Descriptors window.</summary>
/// <param name="Name">The declaration's name.</param>
/// <param name="Type">The object type; empty when the value is not an object.</param>
/// <param name="Properties">(name, value text) pairs. Long values are cut to 300 characters.</param>
public sealed record IndexEntry(string Name, string Type, List<(string Name, string Value)> Properties);

/// <summary>
/// The engine's side of the Editor tab's descriptor viewer and its filter: lists of declarations and properties,
/// with where each value sits in the text so the viewer can jump to it and edit it in place.
/// </summary>
public static class DescriptorViewer
{
    /// <summary>The property list stops after this many rows, so a huge descriptor cannot freeze the viewer.</summary>
    public const int MaxRows = 6000;

    /// <summary>The filter keeps at most this many properties per declaration.</summary>
    public const int MaxIndexedProperties = 20000;

    /// <summary>The filter keeps at most this many characters of a value.</summary>
    public const int MaxIndexedValueLength = 300;

    /// <summary>Every declaration in a text: its name, its type, and where it starts and ends.</summary>
    public static List<OutlineEntry> Outline(string text) =>
        Parser.Parse(text).Declarations.Select(d => new OutlineEntry(
            d.IsUnnamed ? "" : d.Name,
            d.TypeName,
            d.IsUnnamed ? OutlineKind.Unnamed : d.Value is ObjectValue ? OutlineKind.Object : OutlineKind.Constant,
            d.Start,
            d.End)).ToList();

    /// <summary>
    /// One declaration's text, as rows: every property and sub-property, with where each value sits in that text.
    /// Throws <see cref="NdfParseException"/> when the text is not valid NDF, and <see cref="ArgumentException"/>
    /// when it does not hold exactly one declaration.
    /// </summary>
    public static PropertyList Properties(string text)
    {
        var declarations = Parser.Parse(text).Declarations;
        if (declarations.Count != 1) throw new ArgumentException("expected one declaration, found " + declarations.Count);
        var top = declarations[0];
        var rows = new List<PropertyRow>();
        bool cut = false;

        void Value(string key, NdfValue value, int depth) =>
            rows.Add(new ValueRow(depth, key, value.Start!.Value, text[value.Start.Value..value.End!.Value], value.End.Value));

        // A short list of plain values on one line, such as a colour, is shown as one row.
        bool IsShortList(ListValue list) =>
            list.Items.Count > 0 && list.End - list.Start <= 64 && list.Items.All(i => i.IsPrimitive) &&
            !text[list.Start!.Value..list.End!.Value].Contains('\n');

        void Walk(string key, NdfValue value, int depth)
        {
            if (rows.Count >= MaxRows) { cut = true; return; }
            switch (value)
            {
                case ObjectValue obj:
                    rows.Add(new GroupRow(depth, key, obj.Start!.Value, obj.TypeName, obj.Properties.Count, IsObject: true));
                    foreach (var p in obj.Properties) Walk(p.Name, p.Value, depth + 1);
                    break;

                case ListValue list when IsShortList(list):
                    Value(key, list, depth);
                    break;

                case ListValue list:
                    rows.Add(new GroupRow(depth, key, list.Start!.Value, (list.TypeName ?? "") + "[ " + list.Items.Count + " ]", list.Items.Count, IsObject: false));
                    for (int j = 0; j < list.Items.Count; j++) Walk("[" + j + "]", list.Items[j], depth + 1);
                    break;

                // (Key, value): the key becomes part of the row's name, so a map reads as a list of named values.
                case PairValue { Kind: PairKind.Pair } pair when pair.First is ScalarValue:
                    Walk((key != "" ? key + " " : "") + "(" + CanonicalWriter.PrimitiveText(pair.First) + ")", pair.Second, depth);
                    break;

                case AssignValue assign:
                    Walk((key != "" ? key + " " : "") + assign.Name + " is", assign.Value, depth);
                    break;

                default:
                    Value(key, value, depth);
                    break;
            }
        }

        if (top.Value is ObjectValue topObject)
            foreach (var p in topObject.Properties) Walk(p.Name, p.Value, 0);
        else
            Walk(top.IsUnnamed ? "(value)" : top.Name, top.Value, 0);

        return new PropertyList(top.IsUnnamed ? "" : top.Name, top.TypeName, rows, cut);
    }

    /// <summary>
    /// Every named declaration with its type and its properties at any depth, as (name, value text) pairs:
    /// what the Filter Descriptors window searches.
    /// </summary>
    public static List<IndexEntry> Index(string text)
    {
        var entries = new List<IndexEntry>();
        foreach (var d in Parser.Parse(text).Declarations)
        {
            if (d.IsUnnamed) continue;
            var properties = new List<(string, string)>();

            void Add(string name, string value)
            {
                if (name == "" || properties.Count >= MaxIndexedProperties) return;
                properties.Add((name, value.Length > MaxIndexedValueLength ? value[..MaxIndexedValueLength] : value));
            }

            string TextOf(NdfValue v) => text[v.Start!.Value..v.End!.Value];

            void Walk(string name, NdfValue value)
            {
                switch (value)
                {
                    case ObjectValue obj:
                        Add(name, obj.TypeName);
                        foreach (var p in obj.Properties) Walk(p.Name, p.Value);
                        break;
                    case ListValue list:
                        Add(name, TextOf(list));
                        // Objects inside a list are searched too; their own name is not a property.
                        foreach (var item in list.Items)
                            if (item is ObjectValue or AssignValue) Walk("", item);
                        break;
                    case AssignValue assign:
                        Walk(assign.Name, assign.Value);
                        break;
                    default:
                        Add(name, TextOf(value));
                        break;
                }
            }

            if (d.Value is ObjectValue obj)
                foreach (var p in obj.Properties) Walk(p.Name, p.Value);
            entries.Add(new IndexEntry(d.Name, d.TypeName, properties));
        }
        return entries;
    }
}
