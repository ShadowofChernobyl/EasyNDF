using System.Text;
using System.Text.RegularExpressions;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Text;
using EasyNdf.Engine.Writing;

namespace EasyNdf.Engine.Rules;

/// <summary>What one action changed, as the Details list shows it.</summary>
/// <param name="Descriptor">The descriptor's name, or a stand-in's label.</param>
/// <param name="Target">The property or setting changed.</param>
/// <param name="Old">The value before, shortened to 120 characters.</param>
/// <param name="New">The value after, shortened to 120 characters.</param>
public sealed record EditLogEntry(string Descriptor, string Target, string Old, string New);

/// <summary>Counts and logs for the rule that is running.</summary>
public sealed class EditContext
{
    public int Edits { get; set; }
    public int Skipped { get; set; }

    /// <summary>The rule's edit log. Entries stop being added at <see cref="LogLimit"/>; counting goes on.</summary>
    public required List<EditLogEntry> Log { get; init; }
    public required int LogLimit { get; init; }

    /// <summary>The name of the rule that is running, for the renamed-descriptor check.</summary>
    public required string RuleName { get; init; }

    /// <summary>Each name a rule renamed a descriptor to, and the rule that did it.</summary>
    public required OrderedDictionary<string, string> Renamed { get; init; }

    /// <summary>Called when a value is left alone because it has too many digits to calculate with exactly.</summary>
    public required Action<Declaration, string> TooManyDigits { get; init; }

    /// <summary>The mod's name for "Edited by" comments.</summary>
    public required string ModName { get; init; }

    /// <summary>The "Decimal places" setting, or null for no limit.</summary>
    public required int? DecimalPlaces { get; init; }
}

/// <summary>
/// Applies one action to one descriptor: finds every value of the target property, at any depth, and changes it.
/// </summary>
public sealed partial class ActionApplier
{
    private readonly Declaration descriptor;
    private readonly RuleAction action;
    private readonly EditContext context;
    private readonly string target;
    private readonly string op;
    private readonly string value;
    private bool IsListOperation => op is "Insert" or "Replace";

    private ActionApplier(Declaration descriptor, RuleAction action, EditContext context, string target, string op)
    {
        this.descriptor = descriptor;
        this.action = action;
        this.context = context;
        this.target = target;
        this.op = op;
        value = action.Value ?? "";
    }

    /// <summary>Applies the action to the descriptor.</summary>
    public static void Apply(Declaration descriptor, RuleAction action, EditContext context)
    {
        string? target = action.TargetInUse;
        string op = EditComments.OperatorName(action.Operator);
        if (string.IsNullOrEmpty(target) || op == "" || op == "Add descriptor") return; // Add descriptor is a file-level action

        new ActionApplier(descriptor, action, context, target, op).Run();
    }

    private void Run()
    {
        // An object inside a descriptor has no title of its own, and the file's constants have neither title nor type.
        if (descriptor.IsStandIn && (target == Conditions.DescriptorTitle || (descriptor.IsConstantsStandIn && target == Conditions.DescriptorType)))
        {
            context.Skipped++;
            return;
        }
        if (target == Conditions.DescriptorTitle || target == Conditions.DescriptorType)
        {
            EditNameOrType();
            return;
        }
        Walk(descriptor.Value);
    }

    // ---- helpers ----

    /// <summary>"\n" typed into a rule's value box means a line break.</summary>
    private static string WithLineBreaks(string text) => text.Replace("\\n", "\n");

    private static string Shorten(string text) => text.Length > 120 ? text[..117] + "..." : text;

    private void Note(string oldText, string newText)
    {
        context.Edits++;
        if (context.Log.Count < context.LogLimit)
            context.Log.Add(new EditLogEntry(descriptor.LogName, target, Shorten(oldText), Shorten(newText)));
    }

    /// <summary>The text actions: Append, Prepend and Remove. Null when the operation does not apply to text.</summary>
    private string? TextOperation(string text)
    {
        string typed = WithLineBreaks(value);
        return op switch
        {
            "Append" => text + typed,
            "Prepend" => typed + text,
            "Remove" => typed == "" ? null : text.Replace(typed, ""),
            _ => null,
        };
    }

    private string CommentFor(string oldValue) => EditComments.For(action, oldValue, context.ModName);

    // ---- the descriptor's name and type ----

    private void EditNameOrType()
    {
        bool isTitle = target == Conditions.DescriptorTitle;
        var obj = descriptor.Value as ObjectValue;
        string current = isTitle ? descriptor.Name : obj?.TypeName ?? "";
        string? changed = TextOperation(current);
        if (changed == current) return;
        if (changed is null) { context.Skipped++; return; }

        // A comment written after the name or type is kept beside it, not inside it.
        var (name, comment) = CommentText.SplitTrailingComment(changed);
        if (isTitle)
        {
            descriptor.Name = name;
            if (comment != "") descriptor.NameComment = (descriptor.NameComment ?? "") + comment;
            context.Renamed[name] = context.RuleName;
        }
        else if (obj is not null)
        {
            obj.TypeName = name;
            if (comment != "") obj.TypeNameComment = (obj.TypeNameComment ?? "") + comment;
        }
        Note(current, changed);
    }

    // ---- walking the descriptor ----

    private void Walk(NdfValue node)
    {
        switch (node)
        {
            case ObjectValue obj:
                foreach (var property in obj.Properties) property.Value = Visit(property.Name, property.Value);
                break;

            case ListValue list:
                for (int i = 0; i < list.Items.Count; i++) list.Items[i] = Visit("Index" + i, list.Items[i]);
                break;

            case PairValue { Kind: PairKind.Pair } pair:
                // (Key, Value) with the key as the target: the value is changed, for actions that set or calculate.
                bool keyIsTarget = pair.First.IsPrimitive && CanonicalWriter.PrimitiveText(pair.First) == target;
                bool setsOrCalculates = Arithmetic.Operations.ContainsKey(op) || op is "Set To" or "Scale" or "Formula";
                if (keyIsTarget && pair.Second.IsPrimitive && setsOrCalculates)
                {
                    pair.Second = EditPrimitive(pair.Second);
                    break;
                }
                pair.First = Visit("Value1", pair.First);
                pair.Second = Visit("Value2", pair.Second);
                break;

            case PairValue or:
                or.First = Visit(null, or.First);
                or.Second = Visit(null, or.Second);
                break;

            case AssignValue assign:
                assign.Value = Visit(null, assign.Value);
                break;
        }
    }

    /// <summary>Visits one value found under a name. Returns the value to keep in its place.</summary>
    private NdfValue Visit(string? name, NdfValue node)
    {
        if (name == target)
        {
            if (node is ListValue list)
            {
                if (IsListOperation || op == "Remove")
                {
                    EditList(list);
                    return node;
                }
                if (op == "Append")
                {
                    string typed = WithLineBreaks(value);
                    list.AddedComment = (list.AddedComment ?? "") + typed + CommentFor("");
                    Note("", JsText.Trim(typed));
                    return node;
                }
            }
            else if (node.IsPrimitive && !IsListOperation)
            {
                return EditPrimitive(node);
            }
        }
        if (!node.IsPrimitive) Walk(node);
        return node;
    }

    // ---- single values ----

    /// <summary>Changes one single value. Returns the new value, or the same one when nothing changed.</summary>
    private NdfValue EditPrimitive(NdfValue node)
    {
        string old = CanonicalWriter.PrimitiveText(node);
        ScalarValue written;
        string extraComment = "";

        if (Arithmetic.Operations.TryGetValue(op, out var calculate))
        {
            double number = NumberText.ParseTyped(value);
            if (!NumberText.IsPlainNumber(old) || double.IsNaN(number)) { context.Skipped++; return node; }
            string typed = JsText.Trim(value);
            double result = calculate(JsNumber.ParsePlain(old), number);

            bool longWholeNumbers = Arithmetic.IsWholeNumberText(old) && Arithmetic.IsWholeNumberText(typed) &&
                (Arithmetic.HasTooManyDigits(old) || Arithmetic.HasTooManyDigits(typed) || Arithmetic.IsUnsafe(result));
            if (longWholeNumbers)
            {
                string? exact = Arithmetic.Exact(op, old, typed);
                if (exact is null) { context.Skipped++; context.TooManyDigits(descriptor, old); return node; }
                written = new ScalarValue(ScalarKind.Number, exact);
            }
            else
            {
                if (!double.IsFinite(result)) { context.Skipped++; return node; }
                if (Arithmetic.HasTooManyDigits(old) || Arithmetic.HasTooManyDigits(typed) || Arithmetic.IsUnsafe(result))
                {
                    context.Skipped++;
                    context.TooManyDigits(descriptor, old);
                    return node;
                }
                written = new ScalarValue(ScalarKind.Number,
                    Arithmetic.Format(Arithmetic.LimitDecimals(result, context.DecimalPlaces), old.Contains('.')));
            }
        }
        else if (op == "Set To")
        {
            var (text, comment) = CommentText.SplitTrailingComment(WithLineBreaks(value));
            extraComment = comment;
            written = ScalarValue.Written(text);
        }
        else if (op is "Append" or "Prepend" or "Remove")
        {
            if (node is ScalarValue { Kind: ScalarKind.String })
            {
                // Edit inside the quotes, and keep the quotes.
                char quote = old[0];
                string? changed = TextOperation(old[1..^1]);
                if (changed is null) { context.Skipped++; return node; }
                written = new ScalarValue(ScalarKind.String, quote + changed + quote);
            }
            else
            {
                string? changed = TextOperation(old);
                if (changed is null) { context.Skipped++; return node; }
                var (text, comment) = CommentText.SplitTrailingComment(changed);
                extraComment = comment;
                written = ScalarValue.Written(text);
            }
        }
        else if (op is "Scale" or "Formula")
        {
            double? result = op == "Scale" ? ScaleResult(old) : FormulaResult(old);
            if (result is null) { context.Skipped++; return node; }
            if (!double.IsFinite(result.Value)) { context.Skipped++; return node; }
            if (Arithmetic.HasTooManyDigits(old) || Arithmetic.IsUnsafe(result.Value))
            {
                context.Skipped++;
                context.TooManyDigits(descriptor, old);
                return node;
            }
            written = new ScalarValue(ScalarKind.Number, Arithmetic.Format(result.Value, old.Contains('.')));
            if (written.Text == old) return node;
        }
        else
        {
            return node;
        }

        // The value is already what the rule would make it: nothing is written, counted or commented.
        if (written.Text == old && extraComment == "") return node;

        written.AddedComment = CommentText.JoinComments(extraComment, (node.AddedComment ?? "") + CommentFor(old));
        if (node.LeadingComments is not null) written.LeadingComments = node.LeadingComments;
        Note(old, written.Text + extraComment);
        return written.TakesPlaceOf(node);
    }

    /// <summary>The Scale rule's result: calculate, then round to a step or to the decimal places, then limit.</summary>
    private double? ScaleResult(string old)
    {
        if (!NumberText.IsPlainNumber(old)) return null;
        double x = JsNumber.ParsePlain(old);
        var s = action.Scale!;
        double r = s.Operation is not null ? s.Operation(x, s.Value ?? double.NaN) : x;
        if (s.Step > 0)
        {
            double q = r / s.Step;
            double steps = s.RoundMode == "down" ? Math.Floor(q + 1e-9) : s.RoundMode == "up" ? Math.Ceiling(q - 1e-9) : JsNumber.Round(q);
            r = steps * s.Step;
        }
        else
        {
            r = Arithmetic.LimitDecimals(r, context.DecimalPlaces);
        }
        if (s.Min is not null && r < s.Min) r = s.Min.Value;
        if (s.Max is not null && r > s.Max) r = s.Max.Value;
        return r;
    }

    private double? FormulaResult(string old)
    {
        double x = NumberText.IsPlainNumber(old) ? JsNumber.ParsePlain(old) : double.NaN;
        return action.Formula!(x);
    }

    // ---- lists ----

    /// <summary>A list's items written on one line with no spaces, for the edit log.</summary>
    private static string Compact(ListValue list)
    {
        string text = CanonicalWriter.Write(new ListValue(list.TypeName, list.Items));
        var output = new StringBuilder(text.Length);
        foreach (char c in text)
            if (!JsText.IsWhiteSpace(c)) output.Append(c);
        return output.ToString();
    }

    /// <summary>Remove takes out one item; Insert and Replace take "index: value", where the index can be a number, FirstIndex or LastIndex, plus or minus a number.</summary>
    private void EditList(ListValue list)
    {
        string old = Compact(list);

        if (op == "Remove")
        {
            string wanted = TrailingCommas().Replace(JsText.Trim(WithLineBreaks(value)), "");
            int index = list.Items.FindIndex(item =>
            {
                string text = JsText.Trim(CanonicalWriter.Write(item));
                return text == wanted || (item is ScalarValue { Kind: ScalarKind.String } && JsText.Slice(text, 1, -1) == wanted);
            });
            if (index < 0) { context.Skipped++; return; }
            NdfValue gone = list.Items[index];
            list.Items.RemoveAt(index);
            if (gone.LeadingComments is not null)
            {
                // its comments move to whatever follows
                if (index < list.Items.Count)
                    list.Items[index].LeadingComments = [.. gone.LeadingComments, .. list.Items[index].LeadingComments ?? []];
                else
                    list.EndComments = [.. gone.LeadingComments, .. list.EndComments ?? []];
            }
        }
        else
        {
            int colon = value.IndexOf(':');
            if (colon < 0) { context.Skipped++; return; }
            string indexText = JsText.Trim(value[..colon]);
            string itemText = JsText.Trim(WithLineBreaks(value[(colon + 1)..]));
            int length = list.Items.Count;
            bool insert = op == "Insert";

            double index;
            Match named = NamedIndex().Match(indexText);
            if (named.Success)
            {
                double start = named.Groups[1].Value.Equals("firstindex", StringComparison.OrdinalIgnoreCase) ? 0 : (insert ? length : length - 1);
                index = start + (named.Groups[2].Success ? ParseWhole(named.Groups[2].Value) : 0);
            }
            else if (NumberIndex().IsMatch(indexText))
            {
                index = ParseWhole(indexText);
            }
            else { context.Skipped++; return; }
            if (index < 0 || index > (insert ? length : length - 1)) { context.Skipped++; return; }

            var (body, trailingComment) = SplitItemComment(itemText);
            var item = ScalarValue.Written(body);
            item.TrailingLineComment = trailingComment;
            int at = (int)index;

            if (op == "Replace" && trailingComment is null && JsText.Trim(CanonicalWriter.Write(list.Items[at])) == body) return; // already that value

            if (insert)
            {
                list.Items.Insert(at, item);
            }
            else
            {
                if (list.Items[at].LeadingComments is not null) item.LeadingComments = list.Items[at].LeadingComments;
                list.Items[at] = item.TakesPlaceOf(list.Items[at]);
            }
        }
        list.AddedComment = (list.AddedComment ?? "") + CommentFor(old);
        Note(old, Compact(list));
    }

    /// <summary>A whole number that may have spaces after its sign, such as "+ 2".</summary>
    private static double ParseWhole(string text) =>
        double.Parse(Regex.Replace(text, @"\s+", ""), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Splits a value typed for Insert or Replace into the value and a // comment on its last line.
    /// A // comment on an earlier line stays part of the value.
    /// </summary>
    private static (string Body, string? Comment) SplitItemComment(string text)
    {
        int lastBreak = text.LastIndexOf('\n');
        char quote = '\0';
        for (int j = 0; j < text.Length; j++)
        {
            char ch = text[j];
            if (ch == '\n') quote = '\0';
            else if (quote != '\0') { if (ch == quote) quote = '\0'; }
            else if (ch is '"' or '\'') quote = ch;
            else if (ch == '/' && JsText.At(text, j + 1) == '/')
            {
                if (j > lastBreak) return (JsText.TrimEnd(text[..j]), text[j..]);
                j = text.IndexOf('\n', j) - 1;
            }
        }
        return (text, null);
    }

    [GeneratedRegex(@",+\z")]
    private static partial Regex TrailingCommas();

    [GeneratedRegex(@"\A(lastindex|firstindex)\s*([+-]?\s*[0-9]+)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedIndex();

    [GeneratedRegex(@"\A-?[0-9]+\z")]
    private static partial Regex NumberIndex();
}
