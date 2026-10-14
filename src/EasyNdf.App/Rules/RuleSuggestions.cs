using System.Text.Json.Nodes;
using EasyNdf.App.Controls;
using EasyNdf.Data;

namespace EasyNdf.App.Rules;

/// <summary>
/// What the rule boxes suggest while typing (see <see cref="Suggestions"/>): in a formula, <c>value</c>, the functions,
/// the scanned property names and the preset's variables; in a property name, the scanned names and, in square
/// brackets, <c>FirstIndex</c> and <c>LastIndex</c>; in a value, what fits the condition's operand or the action's
/// operator. After <c>{</c>, every box suggests the preset's variables.
/// </summary>
public static class RuleSuggestions
{
    // the tooltips use the Help wiki's words (Formula rules, Testing One Item of a List)
    private const string RoundTip = "round(x): Round to the nearest whole number.\nround(x, step): Round to the nearest step, like 25.";

    private static readonly Suggestion[] FormulaWords =
    [
        new("value", "s-keyword", "the target's value now", Tip: "The current value of the property being changed."),
        new("min", "s-func", "min(a, b)", "min(", "The smaller of two values."),
        new("max", "s-func", "max(a, b)", "max(", "The larger of two values."),
        new("abs", "s-func", "abs(x)", "abs(", "The value without its minus sign."),
        new("floor", "s-func", "floor(x)", "floor(", "Round down to a whole number."),
        new("ceil", "s-func", "ceil(x)", "ceil(", "Round up to a whole number."),
        new("round", "s-func", "round(x) or round(x, step)", "round(", RoundTip),
    ];

    private static readonly Suggestion[] ListPositions =
    [
        new("FirstIndex", "s-keyword", "first item of the list", Tip: "The list's first item, the same as 0."),
        new("LastIndex", "s-keyword", "last item of the list", Tip: "The list's last item. LastIndex-1 is the one before it."),
    ];

    /// <summary>The variables of the open preset (its Variable rules), as {Name} with their value as the note.</summary>
    public static List<Suggestion> VariablesOf(RulesModel model)
    {
        var found = new List<Suggestion>();
        foreach (JsonNode? rule in model.Rules)
        {
            if (JsonValues.AsText(rule?["Kind"]) != "Variable") continue;
            string name = JsonValues.AsText(rule?["Params"]?["VarName"]);
            if (name != "" && found.All(v => v.Text != name))
                found.Add(new Suggestion(name, "s-const", "= " + JsonValues.AsText(rule?["Params"]?["Value"]), name + "}",
                    "A variable of this preset, set by its Variable rule."));
        }
        return found;
    }

    /// <summary>A Formula rule's formula.</summary>
    public static Func<string, int, SuggestionsAt?> Formula(IReadOnlyList<string> names, Func<List<Suggestion>> variables) => (text, caret) =>
    {
        int start = WordStart(text, caret);
        if (After(text, start) == '{') return new SuggestionsAt(start, variables());
        // formulas also take a variable's bare name
        IEnumerable<Suggestion> bareVariables = variables().Select(v => v with { Insert = v.Text });
        return new SuggestionsAt(start, [.. FormulaWords, .. bareVariables, .. Properties(names)]);
    };

    /// <summary>A property name or path: a Target, or a custom operand or target.</summary>
    public static Func<string, int, SuggestionsAt?> Path(IReadOnlyList<string> names, Func<List<Suggestion>> variables) => (text, caret) =>
    {
        int start = WordStart(text, caret);
        if (After(text, start) == '{') return new SuggestionsAt(start, variables());
        if (InSquareBrackets(text, start)) return new SuggestionsAt(start, ListPositions);
        return new SuggestionsAt(start, [.. Properties(names)]);
    };

    /// <summary>
    /// A condition's or action's value. <paramref name="operand"/> and <paramref name="operation"/> are read as it is
    /// typed, as they can change while the window is open.
    /// </summary>
    public static Func<string, int, SuggestionsAt?> Value(bool isAction, Func<string> operand, Func<string> operation, ScanLists scan,
        Func<List<Suggestion>> variables) => (text, caret) =>
    {
        int start = WordStart(text, caret);
        if (After(text, start) == '{') return new SuggestionsAt(start, variables());
        bool atStart = text[..start].Trim() == "";
        if (isAction)
        {
            string op = operation();
            // Remove takes a list position; Insert and Replace take one and a colon, then the value
            if (atStart && op.StartsWith("Remove", StringComparison.Ordinal)) return new SuggestionsAt(start, ListPositions);
            if (atStart && (op.StartsWith("Insert", StringComparison.Ordinal) || op.StartsWith("Replace", StringComparison.Ordinal)))
                return new SuggestionsAt(start, [.. ListPositions.Select(p => p with { Insert = p.Text + ": " })]);
            if (op.StartsWith("Set To", StringComparison.Ordinal))
                return new SuggestionsAt(start, [new("True", "s-const", "yes", Tip: "Sets the value to True."), new("False", "s-const", "no", Tip: "Sets the value to False.")]);
            return null;
        }
        return operand() switch
        {
            "Descriptor Title" => new SuggestionsAt(start, [.. scan.Titles.Select(t => new Suggestion(t, "s-name", "descriptor", Tip: "A descriptor in the scanned file."))]),
            "Descriptor Type" => new SuggestionsAt(start, [.. scan.Types.Select(t => new Suggestion(t, "s-type", "type", Tip: "A descriptor type in the scanned file."))]),
            _ => null,
        };
    };

    private static IEnumerable<Suggestion> Properties(IReadOnlyList<string> names) => names.Select(n => new Suggestion(n, "s-field", "property", Tip: "A property in the scanned file."));

    /// <summary>Where the word the caret is at the end of starts: letters, digits and underscores.</summary>
    private static int WordStart(string text, int caret)
    {
        int start = caret;
        while (start > 0 && (char.IsAsciiLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        return start;
    }

    /// <summary>The character just before a place, or '\0' at the start.</summary>
    private static char After(string text, int place) => place > 0 ? text[place - 1] : '\0';

    /// <summary>Whether a place is inside square brackets: a '[' before it that no ']' has closed.</summary>
    private static bool InSquareBrackets(string text, int place)
    {
        if (place == 0) return false;
        return text.LastIndexOf('[', place - 1) > text.LastIndexOf(']', place - 1);
    }
}
