using System.Text.RegularExpressions;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Text;
using EasyNdf.Engine.Writing;

namespace EasyNdf.Engine.Rules;

/// <summary>Testing a descriptor against a rule's conditions.</summary>
public static partial class Conditions
{
    /// <summary>The operand that tests the whole descriptor's text.</summary>
    public const string EntireDescriptor = "Entire Descriptor";

    /// <summary>The operand, or target, that is the descriptor's name.</summary>
    public const string DescriptorTitle = "Descriptor Title";

    /// <summary>The operand, or target, that is the descriptor's type.</summary>
    public const string DescriptorType = "Descriptor Type";

    /// <summary>
    /// True when the descriptor passes the conditions: all of them with gate "All", one of them otherwise.
    /// With no conditions the answer is <paramref name="whenNoConditions"/>.
    /// </summary>
    public static bool Pass(Declaration descriptor, List<Condition> conditions, bool gateIsAll, bool whenNoConditions)
    {
        if (conditions.Count == 0) return whenNoConditions;
        return gateIsAll
            ? conditions.All(c => Test(descriptor, c))
            : conditions.Any(c => Test(descriptor, c));
    }

    /// <summary>Every value of a property, at any depth: in objects by name, in lists as Index0, Index1..., in pairs as Value1 and Value2.</summary>
    public static void Collect(NdfValue value, string? name, List<NdfValue> found)
    {
        switch (value)
        {
            case ObjectValue obj:
                foreach (var property in obj.Properties)
                {
                    if (property.Name == name) found.Add(property.Value);
                    Collect(property.Value, name, found);
                }
                break;
            case ListValue list:
                for (int i = 0; i < list.Items.Count; i++)
                {
                    if ("Index" + i == name) found.Add(list.Items[i]);
                    Collect(list.Items[i], name, found);
                }
                break;
            case PairValue { Kind: PairKind.Pair } pair:
                // In a pair (Key, Value) the key can be used as a name for the value.
                if (pair.First.IsPrimitive && CanonicalWriter.PrimitiveText(pair.First) == name) found.Add(pair.Second);
                if (name == "Value1") found.Add(pair.First);
                if (name == "Value2") found.Add(pair.Second);
                Collect(pair.First, name, found);
                Collect(pair.Second, name, found);
                break;
            case PairValue or:
                Collect(or.First, name, found);
                Collect(or.Second, name, found);
                break;
            case AssignValue assign:
                Collect(assign.Value, name, found);
                break;
        }
    }

    /// <summary>One candidate a condition tests: the texts it can match, and its number, or NaN when it is not a number.</summary>
    private readonly record struct Candidate(string[] Forms, double Number);

    /// <summary>Tests one condition.</summary>
    public static bool Test(Declaration descriptor, Condition condition)
    {
        string? operand = condition.OperandInUse;
        string op = condition.Operator ?? "";
        string value = condition.Value ?? "";

        List<Candidate> candidates = CandidatesFor(descriptor, operand);

        if (NumberTests.TryGetValue(op, out var numberTest))
        {
            double wanted = NumberText.ParseTyped(value);
            if (double.IsNaN(wanted)) return false;
            return candidates.Any(c => !double.IsNaN(c.Number) && numberTest(c.Number, wanted));
        }

        bool negated = Negations.TryGetValue(op, out string? positive);
        if (!TextTests.TryGetValue(negated ? positive! : op, out var textTest)) return false;
        bool any = candidates.Any(c => c.Forms.Any(t => textTest(t, value)));
        return negated ? !any : any;
    }

    private static List<Candidate> CandidatesFor(Declaration descriptor, string? operand)
    {
        if (operand == EntireDescriptor)
        {
            // The text is kept on the descriptor until a rule changes it, so several rules testing it write it out once.
            string? text = descriptor.IsStandIn ? null : descriptor.CachedConditionText;
            if (text is null)
            {
                text = CanonicalWriter.Write(descriptor);
                if (!descriptor.IsStandIn) descriptor.CachedConditionText = text;
            }
            return [new Candidate([text], double.NaN)];
        }
        if (operand == DescriptorTitle) return [new Candidate([descriptor.Name], double.NaN)];
        if (operand == DescriptorType) return [new Candidate([descriptor.TypeName], double.NaN)];

        var values = new List<NdfValue>();
        Match index = IndexedOperand().Match(operand ?? "");
        if (index.Success)
        {
            // Name[LastIndex], Name[FirstIndex], Name[2], Name[LastIndex-1]: one item of a list, counted as the list actions count.
            var lists = new List<NdfValue>();
            Collect(descriptor.Value, index.Groups[1].Value, lists);
            foreach (var found in lists)
            {
                if (found is not ListValue list) continue;
                int length = list.Items.Count;
                string which = index.Groups[2].Value.ToLowerInvariant();
                double i = which == "lastindex" ? length - 1 : which == "firstindex" ? 0 : double.Parse(which, System.Globalization.CultureInfo.InvariantCulture);
                if (index.Groups[3].Success) i += double.Parse(Regex.Replace(index.Groups[3].Value, @"\s+", ""), System.Globalization.CultureInfo.InvariantCulture);
                if (i >= 0 && i < length) values.Add(list.Items[(int)i]);
            }
        }
        else
        {
            Collect(descriptor.Value, operand, values);
        }

        return values.Select(v =>
        {
            string text = v.IsPrimitive ? CanonicalWriter.PrimitiveText(v) : CanonicalWriter.Write(v);
            // A quoted text matches with or without its quotes.
            string[] forms = v is ScalarValue { Kind: ScalarKind.String } ? [text, text[1..^1]] : [text];
            double number = v.IsPrimitive && NumberText.IsPlainNumber(text) ? JsNumber.ParsePlain(text) : double.NaN;
            return new Candidate(forms, number);
        }).ToList();
    }

    /// <summary>Splits a comma-separated list typed into a condition, leaving out empty parts.</summary>
    private static List<string> SplitList(string text) =>
        text.Split(',').Select(JsText.Trim).Where(p => p != "").ToList();

    private static readonly Dictionary<string, Func<string, string, bool>> TextTests = new()
    {
        ["Starts With"] = (t, v) => t.StartsWith(v, StringComparison.Ordinal),
        ["Ends With"] = (t, v) => t.EndsWith(v, StringComparison.Ordinal),
        ["Contains"] = (t, v) => t.Contains(v, StringComparison.Ordinal),
        ["Is Equal To"] = (t, v) => t == v,
        ["Contains Any Of"] = (t, v) => SplitList(v).Any(p => t.Contains(p, StringComparison.Ordinal)),
        ["Contains All Of"] = (t, v) =>
        {
            var parts = SplitList(v);
            return parts.Count > 0 && parts.All(p => t.Contains(p, StringComparison.Ordinal));
        },
    };

    /// <summary>The operators that are the opposite of a text test.</summary>
    private static readonly Dictionary<string, string> Negations = new()
    {
        ["Does Not Contain"] = "Contains",
        ["Is Not Equal To"] = "Is Equal To",
    };

    private static readonly Dictionary<string, Func<double, double, bool>> NumberTests = new()
    {
        ["Is Less Than"] = (a, b) => a < b,
        ["Is Less Than Or Equal To"] = (a, b) => a <= b,
        ["Is Greater Than"] = (a, b) => a > b,
        ["Is Greater Than Or Equal To"] = (a, b) => a >= b,
    };

    /// <summary>True when the operator compares numbers.</summary>
    public static bool IsNumberTest(string? op) => op is not null && NumberTests.ContainsKey(op);

    // JavaScript: /^(.*\S)\s*\[\s*(lastindex|firstindex|\d+)\s*([+-]\s*\d+)?\s*\]$/i  (its "." stops at line breaks)
    [GeneratedRegex(@"\A([^\n\r\u2028\u2029]*\S)\s*\[\s*(lastindex|firstindex|[0-9]+)\s*([+-]\s*[0-9]+)?\s*\]\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IndexedOperand();
}
