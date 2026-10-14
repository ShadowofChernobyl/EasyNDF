using System.Text;
using EasyNdf.Engine.Ndf;

namespace EasyNdf.Engine.Writing;

/// <summary>
/// Writes a value from scratch in one fixed layout, without comments.
/// </summary>
/// <remarks>
/// This is not how exported files are written (see <see cref="ExportWriter"/>). It is the text conditions and the edit
/// log read, so a rule sees the same text whatever layout the file itself uses. The layout is that of the original
/// desktop app's writer: four spaces per level, property names padded to 34 characters.
/// </remarks>
public static class CanonicalWriter
{
    /// <summary>The text of one value.</summary>
    public static string Write(NdfValue value)
    {
        var output = new StringBuilder();
        WriteValue(value, output, indent: 0);
        return output.ToString();
    }

    /// <summary>The text of a whole declaration, ending with a line break.</summary>
    public static string Write(Declaration declaration)
    {
        var output = new StringBuilder();
        WriteDeclaration(declaration, output);
        return output.ToString();
    }

    /// <summary>Appends a declaration to the output.</summary>
    public static void WriteDeclaration(Declaration declaration, StringBuilder output)
    {
        if (declaration.IsUnnamed)
        {
            output.Append("unnamed ");
        }
        else
        {
            if (declaration.IsExported) output.Append("export ");
            output.Append(declaration.Prefix);
            output.Append(declaration.Name);
            if (declaration.TemplateParameters != "") output.Append('\n').Append(declaration.TemplateParameters);
            output.Append(" is ");
        }
        WriteValue(declaration.Value, output, indent: 0);
        output.Append('\n');
    }

    private static void NewLine(StringBuilder output, int indent)
    {
        output.Append('\n');
        for (int i = 0; i < indent; i++) output.Append("    ");
    }

    private static void WriteValue(NdfValue value, StringBuilder output, int indent)
    {
        switch (value)
        {
            case ObjectValue obj:
                output.Append(obj.TypeName);
                NewLine(output, indent);
                output.Append('(');
                foreach (var property in obj.Properties)
                {
                    NewLine(output, indent + 1);
                    if (property.IsMemberDeclaration) output.Append(property.Name).Append(" is ");
                    else output.Append(property.Name.PadRight(34)).Append("= ");
                    WriteValue(property.Value, output, indent + 1);
                }
                NewLine(output, indent);
                output.Append(')');
                break;

            case ListValue list:
                if (list.TypeName is not null) output.Append(list.TypeName);
                output.Append('[');
                foreach (var item in list.Items)
                {
                    NewLine(output, indent + 1);
                    WriteValue(item, output, indent + 1);
                    output.Append(',');
                }
                NewLine(output, indent);
                output.Append(']');
                break;

            case PairValue pair:
                output.Append('(');
                WriteValue(pair.First, output, indent);
                output.Append(pair.Kind == PairKind.Pair ? ", " : " | ");
                WriteValue(pair.Second, output, indent);
                output.Append(')');
                break;

            case AssignValue assign:
                output.Append(assign.Name).Append(" is ");
                WriteValue(assign.Value, output, indent);
                break;

            case BinaryValue binary:
                WriteValue(binary.Left, output, indent);
                output.Append(binary.Operator != "" ? " " + binary.Operator + " " : " ");
                WriteValue(binary.Right, output, indent);
                break;

            case WrappedValue { Kind: WrapKind.Parentheses } wrapped:
                output.Append('(');
                WriteValue(wrapped.Inner, output, indent);
                output.Append(')');
                break;

            case WrappedValue wrapped:
                output.Append('-');
                WriteValue(wrapped.Inner, output, indent);
                break;

            case ScalarValue { Kind: ScalarKind.Raw } raw:
                output.Append(CommentText.StripForConditions(raw.Text));
                break;

            case ScalarValue scalar:
                output.Append(scalar.Text);
                break;
        }
    }

    /// <summary>
    /// The text a rule sees for a single value. A calculation such as 5 * 60 is written out; other values give their text.
    /// </summary>
    public static string PrimitiveText(NdfValue value) => value switch
    {
        BinaryValue or WrappedValue => Write(value),
        ScalarValue scalar => scalar.Text,
        _ => "",
    };
}
