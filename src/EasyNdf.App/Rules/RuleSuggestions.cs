using System.Text.Json.Nodes;
using EasyNdf.App.Controls;
using EasyNdf.Data;
using EasyNdf.App.Text;

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
    private static string RoundTip => Texts.Get("Rules.Suggest.Round.Tip");

    // made each time they are shown, so they are in the language in use
    private static Suggestion[] FormulaWords =>
    [
        new("value", "s-keyword", Texts.Get("Rules.Suggest.Value"), Tip: Texts.Get("Rules.Suggest.Value.Tip")),
        new("min", "s-func", "min(a, b)", "min(", Texts.Get("Rules.Suggest.Min.Tip")),
        new("max", "s-func", "max(a, b)", "max(", Texts.Get("Rules.Suggest.Max.Tip")),
        new("abs", "s-func", "abs(x)", "abs(", Texts.Get("Rules.Suggest.Abs.Tip")),
        new("floor", "s-func", "floor(x)", "floor(", Texts.Get("Rules.Suggest.Floor.Tip")),
        new("ceil", "s-func", "ceil(x)", "ceil(", Texts.Get("Rules.Suggest.Ceil.Tip")),
        new("round", "s-func", Texts.Get("Rules.Suggest.Round"), "round(", RoundTip),
    ];

    private static Suggestion[] ListPositions =>
    [
        new("FirstIndex", "s-keyword", Texts.Get("Rules.Suggest.FirstIndex"), Tip: Texts.Get("Rules.Suggest.FirstIndex.Tip")),
        new("LastIndex", "s-keyword", Texts.Get("Rules.Suggest.LastIndex"), Tip: Texts.Get("Rules.Suggest.LastIndex.Tip")),
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
                    Texts.Get("Rules.Suggest.Variable.Tip")));
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
                return new SuggestionsAt(start, [new("True", "s-const", Texts.Get("Rules.Suggest.Yes"), Tip: Texts.Get("Rules.Suggest.True.Tip")), new("False", "s-const", Texts.Get("Rules.Suggest.No"), Tip: Texts.Get("Rules.Suggest.False.Tip"))]);
            return null;
        }
        return operand() switch
        {
            "Descriptor Title" => new SuggestionsAt(start, [.. scan.Titles.Select(t => new Suggestion(t, "s-name", Texts.Get("Rules.Suggest.Descriptor"), Tip: Texts.Get("Rules.Suggest.Descriptor.Tip")))]),
            "Descriptor Type" => new SuggestionsAt(start, [.. scan.Types.Select(t => new Suggestion(t, "s-type", Texts.Get("Rules.Suggest.Type"), Tip: Texts.Get("Rules.Suggest.Type.Tip")))]),
            _ => null,
        };
    };

    private static IEnumerable<Suggestion> Properties(IReadOnlyList<string> names) => names.Select(n => new Suggestion(n, "s-field", Texts.Get("Rules.Suggest.Property"), Tip: Texts.Get("Rules.Suggest.Property.Tip")));

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
